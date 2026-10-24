using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>How a race-day manager request reached the laptop.</summary>
public enum RaceDayChannel
{
    /// <summary>The /manage page on the track's own Wi-Fi.</summary>
    WiFi,

    /// <summary>Through the online relay - from anywhere. Never race control.</summary>
    Relay,
}

/// <summary>
/// Everything the race-day manager works with, handed over once by
/// MainForm when the pages exist. The stores and controls are the SAME
/// objects the desk uses - nothing here is a copy - and the delegates are
/// MainForm's own methods, so a phone does exactly what the desk does.
/// </summary>
public sealed class RaceDayServiceHost
{
    /// <summary>The main window: the thread every store belongs to, and the window whose being disabled means a dialog is open.</summary>
    public required Control UiThread { get; init; }

    public required AppSettings Settings { get; init; }
    public required UndoService Undo { get; init; }
    public required RaceTiming Race { get; init; }
    public required RaceEventStore Events { get; init; }
    public required RaceClassStore Classes { get; init; }
    public required ProfileStore Profiles { get; init; }
    public required RoundDefinitionStore Rounds { get; init; }
    public required SessionHistoryStore History { get; init; }
    public required CheckInStore CheckIns { get; init; }
    public required TechInspectionStore Tech { get; init; }
    public required DayScheduleStore Schedule { get; init; }

    /// <summary>The battery packs - a driver's packs and the one in their car (driver.setPack). Optional: without it a driver has none.</summary>
    public BatteryPackStore? Batteries { get; init; }

    /// <summary>The Program tab. Its plan/apply methods are the programme logic; see RaceProgramControl.</summary>
    public required RaceProgramControl Program { get; init; }

    /// <summary>Race Control, for the flag triggers.</summary>
    public required RaceControlControl RaceControl { get; init; }

    /// <summary>The round picked for the session now - what is on track when a race is live.</summary>
    public required Func<RoundDefinition?> ActiveRound { get; init; }

    /// <summary>MainForm.StartRoundNow: (round, confirmedOverRunningRace, logLine) - null when started, else why not.</summary>
    public required Func<RoundDefinition, bool, string, string?> StartRound { get; init; }

    /// <summary>MainForm.ApplyPhoneCheckIn: (transponder, wanted) - true when it changed anything.</summary>
    public required Func<string, bool, bool> SetCheckedIn { get; init; }

    /// <summary>Sends one session's results (and the standings) to the online relay again.</summary>
    public required Action<SessionRecord> RepublishSession { get; init; }

    /// <summary>Brings what the desk shows up to date after a phone change: the round picker, the page on screen, the format.</summary>
    public required Action RefreshDesk { get; init; }

    /// <summary>A line for the laptop's console.</summary>
    public required Action<string> Log { get; init; }

    /// <summary>True once the window has agreed to close.</summary>
    public Func<bool> IsClosing { get; init; } = () => false;
}

/// <summary>
/// The race-day manager: the one way a phone changes anything on race
/// day - setting up the day, entries and check-in, the programme, starting
/// races (Wi-Fi only) and results.
///
/// WHY ONE ENTRY POINT. Every store in this app belongs to the UI thread,
/// and every desk action is a dialog wrapped round a few store calls. A
/// phone has no dialog. So this takes a request as JSON from any thread,
/// carries it onto the UI thread, runs the SAME store calls and the same
/// plan/apply methods the desk runs (RaceProgramControl, HeatBalanceForm's
/// rules, MainForm.StartRoundNow, ResultEditor), and answers in JSON. The
/// desk keeps its dialogs; the phone gets its questions as a two-step
/// confirm instead.
///
/// FOUR RULES every change follows:
///  - It refuses rather than asks when it is not safe: while a race or
///    practice is running for anything destructive, and while the laptop
///    has a dialog open (a phone change made under an open dialog would be
///    written over when that dialog is answered).
///  - Anything the desk would confirm, the phone confirms: the first call
///    answers needsConfirm with the same words and a token; the second call
///    carries the token and does it. The token is bound to the operation,
///    its arguments and the state of the day, and lasts a minute - so a yes
///    to "build 12 heats" can never build something else.
///  - Undo is captured the way the desk captures it, labelled "Phone: ...",
///    so the desk's Undo button says what it would undo.
///  - The desk is refreshed and the console told, so the person at the
///    laptop can see what the phone did.
///
/// The exact protocol - every op, its arguments and its answer - is in
/// raceday-protocol.md, which the web page and the relay are built from.
/// </summary>
public sealed partial class RaceDayService
{
    /// <summary>How long a caller waits for the UI thread before being told the laptop did not answer.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a confirmation token stays good.</summary>
    public static readonly TimeSpan ConfirmLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The largest request accepted, in characters.</summary>
    public const int MaxRequestChars = 64 * 1024;

    /// <summary>What every change refuses with while a dialog is open on the laptop.</summary>
    public const string BusyMessage = "The laptop is busy with a question - try again in a moment.";

    /// <summary>
    /// Whether a PIN is good enough to guard the race-day manager: six or
    /// more digits and nothing else - stricter than the phone remote's four
    /// (WebServerService.IsUsableRemotePin), because this one can touch
    /// entries, the programme and saved results rather than only throw a
    /// flag. Digits, the same as the remote's, because the /manage page
    /// asks for it with a phone's number pad.
    /// </summary>
    public static bool IsUsableManagerPin(string? pin) =>
        pin is { Length: >= 6 } && pin.All(char.IsAsciiDigit);

    private const int MaxPendingConfirms = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RaceDayServiceHost _host;

    /// <summary>
    /// The key driver ids are made with. New every time the app starts, so
    /// an id is only good for one run of the app - a phone re-reads its
    /// lists after a restart, which it has to anyway. Transponder numbers
    /// themselves never have to travel as the key.
    /// </summary>
    private readonly byte[] _idKey = RandomNumberGenerator.GetBytes(32);

    private readonly Dictionary<string, PendingConfirm> _confirms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Op> _ops = new(StringComparer.Ordinal);

    /// <summary>Counts changes made through here. Part of the state stamp, so any phone change retires every outstanding confirmation.</summary>
    private int _changeCount;

    public RaceDayService(RaceDayServiceHost host)
    {
        _host = host;

        RegisterTodayOps();
        RegisterEntryOps();
        RegisterProgramOps();
        RegisterRaceOps();
        RegisterResultOps();

        Register("hello", OpKind.Read, Hello);
    }

    /// <summary>
    /// Raised on the UI thread after a phone change has been made, with a
    /// one-line description - for the Local Screens page's "last phone
    /// action" line.
    /// </summary>
    public event Action<string>? ActionApplied;

    /// <summary>The last change a phone made, as a line of text, or empty. UI thread.</summary>
    public string LastAction { get; private set; } = string.Empty;

    /// <summary>When that was, or null.</summary>
    public DateTime? LastActionUtc { get; private set; }

    /// <summary>Every op name, for the web and relay pages to check against. Race-control ops are listed too - the channel decides.</summary>
    public IReadOnlyCollection<string> OpNames => _ops.Keys.ToList();

    // ------------------------------------------------------------------
    //  The entry point
    // ------------------------------------------------------------------

    /// <summary>
    /// Answers one manager request. Callable from ANY thread: the work is
    /// done on the UI thread and the answer comes back as JSON - always
    /// JSON, never an exception, even for nonsense.
    ///
    /// If the UI thread has not picked the request up within
    /// <see cref="AnswerTimeout"/>, it is withdrawn and the answer says so
    /// - "nothing was changed" is then true, because a withdrawn request
    /// never runs. A request that has already STARTED is waited for: saying
    /// "didn't answer" about a change that is being made would be a lie.
    /// </summary>
    public async Task<string> HandleJsonAsync(string requestJson, RaceDayChannel channel, CancellationToken ct)
    {
        Request request;

        try
        {
            request = Parse(requestJson);
        }
        catch (RequestRejected rejected)
        {
            return Serialize(new Response { Id = rejected.RequestId, Ok = false, Error = rejected.Message });
        }

        var window = _host.UiThread;

        if (_host.IsClosing() || window.IsDisposed || !window.IsHandleCreated)
        {
            return Fail(request.Id, "HeatCaller on the laptop is closing or still starting - try again in a moment.");
        }

        // Already on the UI thread (a caller there, or a test): no hop.
        if (!window.InvokeRequired)
        {
            return RunSafely(request, channel);
        }

        var call = new PendingCall();
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            window.BeginInvoke(new Action(() =>
            {
                // Withdrawn while it was waiting: never run.
                if (Interlocked.CompareExchange(ref call.State, PendingCall.Running, PendingCall.Waiting) != PendingCall.Waiting)
                {
                    return;
                }

                answer.TrySetResult(RunSafely(request, channel));
            }));
        }
        catch (InvalidOperationException)
        {
            // The window went between the check and the post. Also covers
            // ObjectDisposedException, which derives from it.
            return Fail(request.Id, "HeatCaller on the laptop is closing - try again when it is back.");
        }

        var first = await Task.WhenAny(answer.Task, Task.Delay(AnswerTimeout, ct)).ConfigureAwait(false);

        if (first == answer.Task)
        {
            return await answer.Task.ConfigureAwait(false);
        }

        if (Interlocked.CompareExchange(ref call.State, PendingCall.Withdrawn, PendingCall.Waiting) == PendingCall.Waiting)
        {
            return Fail(request.Id, ct.IsCancellationRequested
                ? "Canceled - nothing was changed."
                : "The laptop didn't answer in time - nothing was changed. Try again.");
        }

        // Started: its answer is coming. Waited for, but not for ever - a
        // change that has not finished in another minute has hit something
        // this cannot see, and the phone is told to look before retrying.
        var second = await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);

        return second == answer.Task
            ? await answer.Task.ConfigureAwait(false)
            : Fail(request.Id, "The laptop is still working on that. Check what it did before trying again.");
    }

    /// <summary>A request handed over to the UI thread: waiting, running, or withdrawn by a caller that stopped waiting.</summary>
    private sealed class PendingCall
    {
        public const int Waiting = 0;
        public const int Running = 1;
        public const int Withdrawn = 2;

        public int State = Waiting;
    }

    /// <summary>Run with a net under it: an op that throws answers with the reason instead of taking a thread down.</summary>
    private string RunSafely(Request request, RaceDayChannel channel)
    {
        try
        {
            return Run(request, channel);
        }
        catch (Exception ex)
        {
            _host.Log($"{AppTime.LogStampNow()}  [phone] \"{request.Op}\" failed: {ex.Message}");
            return Fail(request.Id, $"The laptop hit a problem doing that: {ex.Message}");
        }
    }

    /// <summary>Runs one request. UI thread only.</summary>
    private string Run(Request request, RaceDayChannel channel)
    {
        ForgetExpiredConfirms();

        if (!_ops.TryGetValue(request.Op, out var op))
        {
            return Fail(request.Id, $"The laptop doesn't know how to \"{request.Op}\". Is the page out of date? Reload it.");
        }

        if (op.Kind == OpKind.RaceControl && channel != RaceDayChannel.WiFi)
        {
            return Fail(request.Id, "Starting races and flags only work on the track's Wi-Fi, not through the online relay.");
        }

        if (op.Kind != OpKind.Read && !op.AllowedWhileBusy && LaptopBusy())
        {
            return Fail(request.Id, BusyMessage);
        }

        var context = new OpContext(request.Args, channel);

        if (!string.IsNullOrEmpty(request.Confirm))
        {
            if (!_confirms.Remove(request.Confirm, out var pending)
                || pending.Op != op.Name
                || pending.Args != request.ArgsKey
                || pending.Channel != channel)
            {
                return Fail(request.Id, "That confirmation isn't one the laptop gave for this - ask again.");
            }

            if (pending.ExpiresUtc < DateTime.UtcNow)
            {
                return Fail(request.Id, "That confirmation ran out (they last a minute) - ask again.");
            }

            if (pending.Stamp != StateStamp())
            {
                return Fail(request.Id, "Things changed on the laptop since you were asked. Look again, then confirm again.");
            }

            context.Confirmed = true;
        }

        OpResult result;

        try
        {
            result = op.Run(context);
        }
        catch (OpRefused refused)
        {
            result = OpResult.Refuse(refused.Message);
        }

        var response = new Response
        {
            Id = request.Id,
            Ok = result.Ok,
            Error = result.Error,
            NeedsConfirm = result.NeedsConfirm,
            Message = result.Message,
            Data = result.Data,
        };

        if (result.NeedsConfirm)
        {
            response.ConfirmToken = IssueConfirm(op.Name, request.ArgsKey, channel);
        }

        if (result.ChangeLine is { Length: > 0 } line)
        {
            AfterChange(line, channel);
        }

        return Serialize(response);
    }

    /// <summary>Everything a phone change is followed by: the stamp moves on, the desk redraws, the console and the status line say what happened.</summary>
    private void AfterChange(string line, RaceDayChannel channel)
    {
        _changeCount++;

        var said = $"{line} ({(channel == RaceDayChannel.WiFi ? "phone on Wi-Fi" : "phone via the relay")})";

        LastAction = said;
        LastActionUtc = DateTime.UtcNow;

        try
        {
            _host.RefreshDesk();
        }
        catch
        {
            // A page failing to redraw must not turn a change that WAS made
            // into an error the phone would retry.
        }

        _host.Log($"{AppTime.LogStampNow()}  [phone] {said}");

        try
        {
            ActionApplied?.Invoke(said);
        }
        catch
        {
            // As above: a listener's failure is not the phone's problem.
        }
    }

    // ------------------------------------------------------------------
    //  Guards
    // ------------------------------------------------------------------

    /// <summary>
    /// True while a dialog is open on the laptop.
    ///
    /// Two checks, because there are two kinds of dialog. The app's own -
    /// AppDialog, every editor form - are Forms shown modally, and
    /// Application.OpenForms lists them. The system's - a file picker, the
    /// plain MessageBox AppDialog falls back to - are not Forms at all, but
    /// every modal dialog disables the window it belongs to while it is up,
    /// and the main window being disabled is something Windows can be asked.
    /// </summary>
    private bool LaptopBusy()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.Visible && form.Modal)
            {
                return true;
            }
        }

        var window = _host.UiThread;

        return window.IsHandleCreated && !IsWindowEnabled(window.Handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    /// <summary>True while a race is staged, counting down or running, or practice is on.</summary>
    private bool SessionLive =>
        _host.Race.IsPracticeMode
        || _host.Race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

    /// <summary>Refuses a destructive change while something is on track. Says which.</summary>
    private void RefuseWhileLive(string what)
    {
        if (_host.Race.IsPracticeMode)
        {
            throw new OpRefused($"Practice is running - {what} when it has stopped.");
        }

        if (_host.Race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            throw new OpRefused($"A race is on track - {what} when it has finished.");
        }
    }

    /// <summary>The round on track right now, or null. Anything but Idle counts: a finished race is still waiting to be saved.</summary>
    private string? RoundOnTrackId => _host.Race.State == RaceState.Idle ? null : _host.ActiveRound()?.Id;

    // ------------------------------------------------------------------
    //  Confirmations
    // ------------------------------------------------------------------

    private sealed record PendingConfirm(string Op, string Args, string Stamp, DateTime ExpiresUtc, RaceDayChannel Channel);

    private string IssueConfirm(string op, string argsKey, RaceDayChannel channel)
    {
        // Bounded: a phone asking and never confirming, over and over, must
        // not grow this for ever. The oldest go first.
        while (_confirms.Count >= MaxPendingConfirms)
        {
            var oldest = _confirms.OrderBy(kv => kv.Value.ExpiresUtc).First().Key;
            _confirms.Remove(oldest);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        _confirms[token] = new PendingConfirm(op, argsKey, StateStamp(), DateTime.UtcNow + ConfirmLifetime, channel);

        return token;
    }

    private void ForgetExpiredConfirms()
    {
        var now = DateTime.UtcNow;

        foreach (var token in _confirms.Where(kv => kv.Value.ExpiresUtc < now).Select(kv => kv.Key).ToList())
        {
            _confirms.Remove(token);
        }
    }

    /// <summary>
    /// A fingerprint of everything a confirmed change could depend on: the
    /// current race day and its settings, today's programme and its rosters,
    /// the race on track, how many results and corrections there are, how
    /// many drivers, and every change made through here. A confirmation is
    /// only good while this has not moved - so "build 12 heats" confirmed
    /// after someone at the desk added a driver is asked again.
    /// </summary>
    private string StateStamp()
    {
        var text = new StringBuilder();

        text.Append(_changeCount).Append('|').Append(_host.Events.CurrentId);

        var day = _host.Events.Active;

        if (day is not null)
        {
            text.Append('|').Append(day.QualifyingRounds)
                .Append(',').Append(day.DriversPerHeat)
                .Append(',').Append(day.CheckedInDriversOnly)
                .Append(',').Append(day.IsOpen)
                .Append(',').Append(day.TrackLayoutName)
                .Append(',').Append(string.Join(",", day.ClassNames));

            if (day.CheckedInDriversOnly)
            {
                text.Append("|in:").Append(_host.CheckIns.Count);
            }
        }

        foreach (var round in _host.Program.TodaysRounds())
        {
            text.Append('|').Append(round.Id)
                .Append(':').Append(round.Order)
                .Append(':').Append(round.IsRecorded ? 1 : 0)
                .Append(':').Append(round.BumpUpsApplied ? 1 : 0)
                .Append(':').Append(string.Join(",", round.AssignedTransponders));
        }

        var race = _host.Race;

        text.Append("|race:").Append(race.State)
            .Append(':').Append(race.SessionGeneration)
            .Append(':').Append(race.IsPracticeMode)
            .Append(':').Append(race.IsDemoMode)
            .Append(':').Append(_host.ActiveRound()?.Id);

        var sessions = _host.History.All;

        text.Append("|history:").Append(sessions.Count).Append(':').Append(sessions.Sum(s => s.Edits.Count));
        text.Append("|drivers:").Append(_host.Profiles.AllIncludingGhosts.Count);

        foreach (var raceClass in _host.Classes.All)
        {
            text.Append("|class:").Append(raceClass.Name)
                .Append(':').Append(raceClass.MainSize)
                .Append(':').Append(raceClass.BumpUpCount)
                .Append(':').Append(raceClass.QualifyingMethod);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));

        return Convert.ToHexString(hash, 0, 12);
    }

    // ------------------------------------------------------------------
    //  Driver ids
    // ------------------------------------------------------------------

    /// <summary>
    /// The id a phone knows a driver by: 16 hex characters made from the
    /// transponder with a key that never leaves this laptop. Stable for this
    /// run of the app, meaningless to anybody without the key.
    /// </summary>
    public string DriverIdFor(string transponder)
    {
        var bytes = HMACSHA256.HashData(_idKey, Encoding.UTF8.GetBytes((transponder ?? string.Empty).Trim().ToUpperInvariant()));

        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>The transponder behind a driver id, from the drivers on file (ghosts included), or null.</summary>
    private string? TransponderForDriverId(string? driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return null;
        }

        return _host.Profiles.AllIncludingGhosts
            .Select(p => p.Transponder)
            .FirstOrDefault(t => string.Equals(DriverIdFor(t), driverId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The transponder behind an id among a given set (a result, a roster) - for cars with no profile.</summary>
    private string? TransponderForDriverId(string? driverId, IEnumerable<string> among)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return null;
        }

        return among.FirstOrDefault(t => string.Equals(DriverIdFor(t), driverId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A driver on file by id, or a refusal the phone can show.</summary>
    private DriverProfile RequireDriver(OpContext context, string argName = "driverId")
    {
        var transponder = TransponderForDriverId(context.Text(argName));

        return transponder is not null && _host.Profiles.Get(transponder) is { } profile
            ? profile
            : throw new OpRefused("That driver isn't on the laptop any more - reload the list.");
    }

    /// <summary>
    /// A transponder number as a phone may see it. In full on the track's
    /// Wi-Fi, where the desk shows it too; through the relay only its last
    /// three characters - transponder numbers are kept off the internet
    /// (see relay-deployment), and three are enough to tell two cars apart.
    /// </summary>
    private static string ShowTransponder(string? transponder, RaceDayChannel channel)
    {
        var text = (transponder ?? string.Empty).Trim();

        if (text.Length == 0 || channel == RaceDayChannel.WiFi)
        {
            return text;
        }

        return text.Length <= 3 ? "..." : "..." + text[^3..];
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

    /// <summary>
    /// A driver's name as a phone on this channel may see it in a list: the
    /// name, or - for a car nobody has named - the transponder as
    /// <see cref="ShowTransponder"/> shows it. NameOf falls back to the
    /// transponder in full, which is right on the laptop and the track's
    /// Wi-Fi but put an unnamed car's whole number into answers sent
    /// through the relay, next to a transponder column careful to show only
    /// its last three characters. A name that is only the transponder
    /// again (what some results store for an unnamed car) counts as none.
    /// </summary>
    private static string ShownName(string? driverName, string transponder, RaceDayChannel channel)
    {
        var name = (driverName ?? string.Empty).Trim();

        return name.Length == 0 || string.Equals(name, (transponder ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            ? ShowTransponder(transponder, channel)
            : name;
    }

    private string NameOfTransponder(string transponder) =>
        _host.Profiles.Get(transponder) is { } profile ? NameOf(profile) : transponder;

    /// <summary>
    /// NameOf for text that goes back to the phone - a refusal, a question,
    /// an answer - rather than the laptop's own console and undo list. An
    /// unnamed driver is their transponder as this channel may see it (see
    /// ShownName), not the whole number: "1234567 is entered in Stock"
    /// went out through the relay to anyone holding the manager PIN.
    /// </summary>
    private static string ShownNameOf(DriverProfile profile, RaceDayChannel channel) =>
        ShownName(profile.DriverName, profile.Transponder, channel);

    /// <summary>NameOfTransponder for text that goes back to the phone. See ShownNameOf.</summary>
    private string ShownNameOfTransponder(string transponder, RaceDayChannel channel) =>
        _host.Profiles.Get(transponder) is { } profile ? ShownNameOf(profile, channel) : ShowTransponder(transponder, channel);

    /// <summary>
    /// A bump-up row's name for the phone (RaceProgramControl.PlanBumpUps).
    /// The row is named as the feeder's result was filed, which for a car
    /// nobody has named is the transponder it was heard on - its main one or
    /// one of its others - and that went out in full through the relay in
    /// the bump-up question. On the Wi-Fi it is left as the desk shows it.
    /// </summary>
    internal static string ShownPromotionName(MainsBuilder.PlannedPromotion promotion, ProfileStore profiles, RaceDayChannel channel)
    {
        if (channel == RaceDayChannel.WiFi)
        {
            return promotion.DriverName;
        }

        var name = (promotion.DriverName ?? string.Empty).Trim();

        return profiles.Get(promotion.Transponder) is { } profile && profile.AllTransponderIds.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? ShownNameOf(profile, channel)
            : ShownName(name, promotion.Transponder, channel);
    }

    /// <summary>
    /// ProfileStore.DescribeClash for an answer to the phone. An unnamed
    /// owner is described by their main transponder, which went out in
    /// full through the relay - "Transponder 555 is already one of the
    /// profile for 7654321's transponders" gave away a number the phone
    /// never typed. So every number but the ones the phone sent
    /// (<paramref name="typed"/>) is written as ShowTransponder writes it.
    /// </summary>
    internal static string? ClashForPhone(ProfileStore profiles, DriverProfile candidate, string? editing, RaceDayChannel channel, params string[] typed) =>
        profiles.DescribeClash(candidate, editing, id =>
            typed.Any(t => string.Equals(t?.Trim(), id?.Trim(), StringComparison.OrdinalIgnoreCase)) ? id : ShowTransponder(id, channel));

    /// <summary>
    /// The laptop's undo list as a phone on this channel may see it: every
    /// entry on the track's Wi-Fi, none through the relay, which is only
    /// told how many there are (undoCount). The entries are the desk's own
    /// words and name transponders in full - "Transfer transponder 1234567
    /// to Alex", "Phone: Add driver '7654321'" for a car with no name - and
    /// one about a number nobody owns any more can't be picked out of the
    /// text to be shortened.
    ///
    /// Through the relay, one line with just the count in place of the
    /// entries. The relay is deployed on its own, and its page from before
    /// undoCount shows this list as it comes: sent empty, it said "Nothing
    /// to undo." with changes waiting. The page that reads undoCount
    /// ignores the line.
    /// </summary>
    internal static IReadOnlyList<string> UndoListFor(IReadOnlyList<string> pending, RaceDayChannel channel) =>
        channel == RaceDayChannel.WiFi ? pending
        : pending.Count == 0 ? Array.Empty<string>()
        : new[] { pending.Count == 1 ? "1 change - undo it on the laptop" : $"{pending.Count} changes - undo them on the laptop" };

    // ------------------------------------------------------------------
    //  Ops
    // ------------------------------------------------------------------

    private enum OpKind
    {
        /// <summary>Changes nothing. Allowed on both channels, and while the laptop is busy.</summary>
        Read,

        /// <summary>Changes the day. Refused while a dialog is open.</summary>
        Change,

        /// <summary>Starts races and throws flags. Wi-Fi only, and refused while a dialog is open unless marked otherwise.</summary>
        RaceControl,
    }

    private sealed record Op(string Name, OpKind Kind, Func<OpContext, OpResult> Run, bool AllowedWhileBusy);

    private void Register(string name, OpKind kind, Func<OpContext, OpResult> run, bool allowedWhileBusy = false) =>
        _ops[name] = new Op(name, kind, run, allowedWhileBusy);

    /// <summary>What an op answers. Build with the static helpers.</summary>
    private sealed class OpResult
    {
        public bool Ok { get; private init; }
        public string? Error { get; private init; }
        public bool NeedsConfirm { get; private init; }
        public string? Message { get; private init; }
        public object? Data { get; private init; }

        /// <summary>Set when something was changed: the line the console and the status line show.</summary>
        public string? ChangeLine { get; private init; }

        public static OpResult Read(object? data) => new() { Ok = true, Data = data };

        public static OpResult Refuse(string error) => new() { Ok = false, Error = error };

        /// <summary>Asks first. The same op with the same args and the token then does it.</summary>
        public static OpResult Ask(string message, object? data = null) =>
            new() { Ok = true, NeedsConfirm = true, Message = message, Data = data };

        public static OpResult Done(string changeLine, string message, object? data = null) =>
            new() { Ok = true, Message = message, Data = data, ChangeLine = changeLine };
    }

    /// <summary>A refusal thrown from inside an op - bad arguments, not safe now - answered as ok:false with this message.</summary>
    private sealed class OpRefused : Exception
    {
        public OpRefused(string message)
            : base(message)
        {
        }
    }

    /// <summary>One request's arguments, and whether it arrived confirmed.</summary>
    private sealed class OpContext
    {
        public OpContext(JsonElement args, RaceDayChannel channel)
        {
            Args = args;
            Channel = channel;
        }

        public JsonElement Args { get; }
        public RaceDayChannel Channel { get; }
        public bool Confirmed { get; set; }

        public bool Has(string name) =>
            Args.ValueKind == JsonValueKind.Object
            && Args.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

        /// <summary>A string argument, or null when it is missing or null. Refuses a value that is not a string.</summary>
        public string? Text(string name)
        {
            if (!Has(name))
            {
                return null;
            }

            var value = Args.GetProperty(name);

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new OpRefused($"\"{name}\" has to be text.");
        }

        public string RequireText(string name) =>
            Text(name) is { } text && text.Trim().Length > 0
                ? text.Trim()
                : throw new OpRefused($"\"{name}\" is needed.");

        /// <summary>A whole-number argument, or null when missing. Refuses anything else, and anything outside min..max.</summary>
        public int? Int(string name, int min, int max)
        {
            if (!Has(name))
            {
                return null;
            }

            var value = Args.GetProperty(name);

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            {
                throw new OpRefused($"\"{name}\" has to be a whole number.");
            }

            return number < min || number > max
                ? throw new OpRefused($"\"{name}\" has to be between {min} and {max}.")
                : number;
        }

        /// <summary>A number (decimals allowed), or null when missing.</summary>
        public double? Number(string name, double min, double max)
        {
            if (!Has(name))
            {
                return null;
            }

            var value = Args.GetProperty(name);

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || double.IsNaN(number))
            {
                throw new OpRefused($"\"{name}\" has to be a number.");
            }

            return number < min || number > max
                ? throw new OpRefused($"\"{name}\" has to be between {min} and {max}.")
                : number;
        }

        public bool? Bool(string name)
        {
            if (!Has(name))
            {
                return null;
            }

            return Args.GetProperty(name).ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new OpRefused($"\"{name}\" has to be true or false."),
            };
        }

        /// <summary>A list of strings, or null when missing.</summary>
        public List<string>? TextList(string name, int maxCount)
        {
            if (!Has(name))
            {
                return null;
            }

            var value = Args.GetProperty(name);

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new OpRefused($"\"{name}\" has to be a list.");
            }

            var list = new List<string>();

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    throw new OpRefused($"\"{name}\" has to be a list of text.");
                }

                list.Add(item.GetString() ?? string.Empty);

                if (list.Count > maxCount)
                {
                    throw new OpRefused($"\"{name}\" has too many entries (at most {maxCount}).");
                }
            }

            return list;
        }
    }

    // ------------------------------------------------------------------
    //  The wire format
    // ------------------------------------------------------------------

    private sealed record Request(string? Id, string Op, JsonElement Args, string ArgsKey, string? Confirm);

    /// <summary>A request refused before it got as far as an op - unreadable, too big, no op.</summary>
    private sealed class RequestRejected : Exception
    {
        public RequestRejected(string message, string? requestId = null)
            : base(message)
        {
            RequestId = requestId;
        }

        public string? RequestId { get; }
    }

    private sealed class Response
    {
        public string? Id { get; set; }
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public bool NeedsConfirm { get; set; }
        public string? Message { get; set; }
        public string? ConfirmToken { get; set; }
        public object? Data { get; set; }
    }

    private static Request Parse(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            throw new RequestRejected("The request was empty.");
        }

        if (requestJson.Length > MaxRequestChars)
        {
            throw new RequestRejected("The request was too big.");
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new RequestRejected("The request wasn't readable.");
            }

            string? id = null;

            if (root.TryGetProperty("id", out var idValue))
            {
                id = idValue.ValueKind switch
                {
                    JsonValueKind.String => idValue.GetString(),
                    JsonValueKind.Number => idValue.GetRawText(),
                    _ => null,
                };

                // Echoed back, so kept short.
                if (id is { Length: > 100 })
                {
                    id = id[..100];
                }
            }

            if (!root.TryGetProperty("op", out var opValue) || opValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(opValue.GetString()))
            {
                throw new RequestRejected("The request didn't say what to do.", id);
            }

            JsonElement args;

            if (root.TryGetProperty("args", out var argsValue) && argsValue.ValueKind == JsonValueKind.Object)
            {
                // Cloned so it outlives the document, which is disposed here
                // long before the op runs on the UI thread.
                args = argsValue.Clone();
            }
            else if (root.TryGetProperty("args", out var notObject) && notObject.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                throw new RequestRejected("\"args\" has to be an object.", id);
            }
            else
            {
                using var empty = JsonDocument.Parse("{}");
                args = empty.RootElement.Clone();
            }

            string? confirm = null;

            if (root.TryGetProperty("confirm", out var confirmValue) && confirmValue.ValueKind == JsonValueKind.String)
            {
                confirm = confirmValue.GetString();
            }

            // The arguments as a confirmation compares them: re-written
            // compactly, so spacing a phone happens to send cannot matter.
            var argsKey = JsonSerializer.Serialize(args);

            return new Request(id, opValue.GetString()!.Trim(), args, argsKey, confirm);
        }
        catch (JsonException)
        {
            throw new RequestRejected("The request wasn't readable.");
        }
    }

    private static string Serialize(Response response) => JsonSerializer.Serialize(response, JsonOptions);

    private static string Fail(string? id, string error) =>
        Serialize(new Response { Id = id, Ok = false, Error = error });

    /// <summary>
    /// What the phone needs to know before it draws anything: which channel
    /// it came in on (so the Race tab can be hidden over the relay), whether
    /// corrections are switched on, and whether the laptop is busy.
    /// </summary>
    private OpResult Hello(OpContext context) =>
        OpResult.Read(new
        {
            app = AppVersion.Short,
            channel = context.Channel == RaceDayChannel.WiFi ? "wifi" : "relay",
            raceControl = context.Channel == RaceDayChannel.WiFi,
            allowResultEditing = _host.Settings.AllowResultEditing,
            busy = LaptopBusy(),
            ops = _ops.Keys
                .Where(name => context.Channel == RaceDayChannel.WiFi || _ops[name].Kind != OpKind.RaceControl)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
        });
}
