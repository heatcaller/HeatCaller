using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Marshals in place (12 Nov 2026, setup plan D4).
///
/// WHY: at most clubs, missing turn marshals hold the next race up more
/// often than missing drivers do. Auto start (Phase 1) went green on time
/// whether or not anyone was on the posts. For a club that ticks "Wait for
/// the marshals before starting" (the Auto start switch's right-click menu),
/// a minute before the next race this banner names its marshals and their
/// posts, with Marshals in place to confirm them. At zero an unconfirmed
/// start waits, and the announcer calls the posts once. Someone's missing...
/// logs a no-show on the round, for the club's marshal rule.
/// </summary>
public sealed partial class RaceControlControl
{
    private readonly TableLayoutPanel _marshalsBanner = new();
    private readonly Label _lblMarshalsBanner = new();
    private readonly Button _btnMarshalsMissing = new() { Text = "Someone's missing..." };

    /// <summary>The round the marshals were last called for at zero, so the call is made once.</summary>
    private string? _marshalsCalledFor;

    /// <summary>
    /// A marshal didn't turn up for a round: the round, "Name (Post)" and
    /// their transponder. MainForm saves it on the round and applies the
    /// club's marshal rule.
    /// </summary>
    public event Action<RoundDefinition, string, string>? MarshalNoShow;

    /// <summary>A no-show taken back off (setup plan step 8): the round, and "Name (Post)". MainForm undoes the rule.</summary>
    public event Action<RoundDefinition, string>? MarshalNoShowTakenBack;

    /// <summary>The marshals of the round the banner is showing, for Someone's missing.</summary>
    private IReadOnlyList<(string Name, string Post)> _marshalsShown = Array.Empty<(string, string)>();

    private RoundDefinition? _marshalsRound;

    /// <summary>The marshal banner, in the cars banner's warning frame. Called from the layout.</summary>
    private void BuildMarshalsAsk()
    {
        _marshalsBanner.AutoSize = true;
        _marshalsBanner.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _marshalsBanner.ColumnCount = 1;
        _marshalsBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _marshalsBanner.Padding = new Padding(14, 10, 14, 10);
        _marshalsBanner.Visible = false;
        _marshalsBanner.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Warning, 2);
            e.Graphics.DrawRectangle(pen, 1, 1, _marshalsBanner.Width - 2, _marshalsBanner.Height - 2);
        };

        _lblMarshalsBanner.AutoSize = true;
        _lblMarshalsBanner.Font = AppTheme.BoldFont;
        _lblMarshalsBanner.Margin = new Padding(0, 6, 12, 0);
        _lblMarshalsBanner.MaximumSize = new Size(720, 0);

        var inPlace = new Button { Text = "Marshals in place" };
        inPlace.Click += (_, _) => ConfirmMarshals();

        _btnMarshalsMissing.Click += (_, _) => ShowMarshalsMissingMenu();

        _marshalsBanner.Controls.Add(new ButtonBar(tight: true).With(_lblMarshalsBanner, inPlace, _btnMarshalsMissing));

        BuildSwapNotice();
    }

    // ── Late and failed swaps from phones (13 Nov 2026) ─────────────────────
    //
    // A swap two phones agree early is routine: the log and Driver Requests'
    // Answered list have it, and the banner above names the cover. A late one,
    // or a yes that couldn't be made, can leave a corner wrong - that comes
    // up here, in the same warning frame, until the desk says Got it.
    // See MarshalSwapNotices.

    private readonly TableLayoutPanel _swapNotice = new();
    private readonly Label _lblSwapNotice = new();

    private void BuildSwapNotice()
    {
        _swapNotice.AutoSize = true;
        _swapNotice.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _swapNotice.ColumnCount = 1;
        _swapNotice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _swapNotice.Padding = new Padding(14, 10, 14, 10);
        _swapNotice.Visible = false;
        _swapNotice.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Warning, 2);
            e.Graphics.DrawRectangle(pen, 1, 1, _swapNotice.Width - 2, _swapNotice.Height - 2);
        };

        _lblSwapNotice.AutoSize = true;
        _lblSwapNotice.Font = AppTheme.BoldFont;
        _lblSwapNotice.Margin = new Padding(0, 6, 12, 0);
        _lblSwapNotice.MaximumSize = new Size(720, 0);

        var gotIt = new Button { Text = "Got it" };
        gotIt.Click += (_, _) =>
        {
            _swapNotice.Visible = false;
            _lblSwapNotice.Text = string.Empty;
        };

        _swapNotice.Controls.Add(new ButtonBar(tight: true).With(_lblSwapNotice, gotIt));
    }

    /// <summary>
    /// Puts a late or failed swap in front of the director, and in the log.
    /// A second one before Got it goes under the first, so neither is lost.
    /// </summary>
    public void ShowMarshalSwapNotice(string notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
        {
            return;
        }

        _lblSwapNotice.Text = _swapNotice.Visible && _lblSwapNotice.Text.Length > 0
            ? _lblSwapNotice.Text + Environment.NewLine + notice
            : notice;
        _swapNotice.Visible = true;
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [marshals] {notice}");
    }

    /// <summary>Why a swap just made for this round is late (MarshalSwapNotices.Lateness), or null when it isn't.</summary>
    public string? MarshalSwapLateness(string roundId, DateTime nowUtc)
    {
        var active = _activeRound?.Invoke();
        var onTrack = string.Equals(active?.Id, roundId, StringComparison.Ordinal)
                      && _race.State is not (RaceState.Idle or RaceState.Finished);
        var isNext = string.Equals(NextRound()?.Id, roundId, StringComparison.Ordinal);
        var counting = _autoStart.IsOpen && string.Equals(_autoStart.RoundId, roundId, StringComparison.Ordinal);

        return MarshalSwapNotices.Lateness(
            onTrack,
            isNext,
            marshalsCalled: string.Equals(_marshalsCalledFor, roundId, StringComparison.Ordinal),
            marshalsBeingChecked: _marshalsBanner.Visible && string.Equals(_marshalsRound?.Id, roundId, StringComparison.Ordinal),
            untilStart: counting ? _autoStart.Remaining(nowUtc, Gap.PushedBackMinutes) : null);
    }

    /// <summary>Who a marshal on a round is covering for, by the name the announcer uses - null when it's their own post.</summary>
    private string? CoveringFor(RoundDefinition round, string transponder)
    {
        if (round.MarshalCovers is not { Count: > 0 } covers || string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var owner = covers.FirstOrDefault(kv => string.Equals(kv.Value?.Trim(), transponder.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
        return string.IsNullOrWhiteSpace(owner) ? null : SpokenNameOf(owner);
    }

    /// <summary>
    /// Once a second from the gap: shows the ask from a minute before the
    /// start until somebody confirms, and at zero calls the posts once.
    /// </summary>
    private void ShowMarshalsAsk(RoundDefinition next, IReadOnlyList<(string Name, string Post)> marshals, bool waitForMarshals, DateTime nowUtc,
        IReadOnlyList<bool>? arrived = null, IReadOnlyList<string?>? coveringFor = null)
    {
        var remaining = _autoStart.Remaining(nowUtc, Gap.PushedBackMinutes);
        var show = waitForMarshals
                   && !_autoStart.MarshalsConfirmed
                   && (_autoStartState == AutoStartState.WaitingForMarshals
                       || remaining is { } left && left <= AutoStartClock.AskForMarshalsFrom);

        if (!show)
        {
            HideMarshalsAsk();
            return;
        }

        _marshalsShown = marshals;
        _marshalsRound = next;

        // "Sam for Max" on a covered post (13 Nov 2026), so the director looks
        // for the right person on the corner. Only on the banner: the
        // announcer calls Sam, and Someone's missing... matches the plain names.
        var labelled = coveringFor is null
            ? marshals
            : marshals.Select((m, i) => (MarshalSwapNotices.BannerName(m.Name, i < coveringFor.Count ? coveringFor[i] : null), m.Post)).ToList();

        var text = MarshalsInPlace.Ask(next.RoundLabel, labelled, waiting: _autoStartState == AutoStartState.WaitingForMarshals, arrived);

        if (_lblMarshalsBanner.Text != text)
        {
            _lblMarshalsBanner.Text = text;
        }

        // Always assigned, never compared: see UnknownTransponderBanner.RefreshVisibility.
        _marshalsBanner.Visible = true;

        if (_autoStartState == AutoStartState.WaitingForMarshals && _marshalsCalledFor != next.Id)
        {
            _marshalsCalledFor = next.Id;

            // Only the posts still empty (setup plan step 8): calling Hugh
            // to Turn 1 when his phone says he's standing on it is noise.
            var missing = arrived is null
                ? marshals
                : marshals.Where((_, i) => !(i < arrived.Count && arrived[i])).ToList();

            SayGapLine(MarshalsInPlace.Call(next.RoundLabel, missing.Count > 0 ? missing : marshals), "Marshals");
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {next.RoundLabel} is waiting for its marshals. Marshals in place starts it.");
        }
    }

    private void HideMarshalsAsk()
    {
        _marshalsBanner.Visible = false;
        _marshalsShown = Array.Empty<(string, string)>();
        _marshalsRound = null;
    }

    /// <summary>The banner's Marshals in place.</summary>
    private void ConfirmMarshals()
    {
        _autoStart.ConfirmMarshals();
        HideMarshalsAsk();
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshals in place - confirmed at the desk.");
    }

    /// <summary>Someone's missing...: each marshal, to log as a no-show.</summary>
    private void ShowMarshalsMissingMenu()
    {
        if (_marshalsRound is not { } round || _marshalsShown.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip();

        // Each marshal's transponder too (12 Nov 2026, setup plan step 8), so
        // the club's marshal rule can find their results.
        var posts = MarshalPostsFor(round);

        foreach (var (name, post) in _marshalsShown)
        {
            var who = MarshalsInPlace.Who(name, post);
            var transponder = posts.FirstOrDefault(p => p.Name == name && p.Post == post).Transponder ?? string.Empty;
            var logged = round.MarshalNoShows?.Contains(who, StringComparer.OrdinalIgnoreCase) == true;

            // Logged already: the same line takes it back off (setup plan
            // step 8) - a no-show logged by mistake costs a driver a result.
            var item = new ToolStripMenuItem(logged ? $"{who} did turn up - take the no-show off" : $"{who} didn't turn up") { Checked = logged };
            item.Click += (_, _) =>
            {
                if (logged)
                {
                    MarshalNoShowTakenBack?.Invoke(round, who);
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshal no-show taken off for {round.RoundLabel}: {who}.");
                    return;
                }

                MarshalNoShow?.Invoke(round, who, transponder);
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] Marshal no-show logged for {round.RoundLabel}: {who}. "
                                      + "Get someone onto the post, then press Marshals in place.");
            };
            menu.Items.Add(item);
        }

        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(_btnMarshalsMissing, new Point(0, _btnMarshalsMissing.Height));
    }

    /// <summary>The Auto start switch menu's "Wait for the marshals before starting".</summary>
    private void ToggleWaitForMarshals()
    {
        _settings.AutoStartWaitForMarshals = !_settings.AutoStartWaitForMarshals;
        _settings.Save();

        if (!_settings.AutoStartWaitForMarshals)
        {
            HideMarshalsAsk();
        }

        StatusMessage?.Invoke(_settings.AutoStartWaitForMarshals
            ? $"{AppTime.LogStampNow()}  [auto] Auto start now waits for Marshals in place before each race with marshals."
            : $"{AppTime.LogStampNow()}  [auto] Auto start no longer waits for the marshals.");
    }
}

/// <summary>The marshals-in-place wording (setup plan D4), pure so it is tested.</summary>
public static class MarshalsInPlace
{
    /// <summary>One marshal as the banner and the no-show log name them: "Hugh Turn (Turn 3)".</summary>
    public static string Who(string name, string post) =>
        string.IsNullOrWhiteSpace(post) ? name : $"{name} ({post})";

    /// <summary>
    /// The banner: "Marshals for Q2 Heat 3: Hugh Turn (Turn 1), Rhoda Rage
    /// (Turn 2). In place?" - or, once the start is waiting, "Q2 Heat 3 is
    /// waiting for its marshals: ...".
    /// </summary>
    public static string Ask(string roundLabel, IReadOnlyList<(string Name, string Post)> marshals, bool waiting) =>
        Ask(roundLabel, marshals, waiting, null);

    /// <summary>
    /// The banner, once a marshal has said from their phone they're at their
    /// post (12 Nov 2026, setup plan step 8): "Marshals for Q2 Heat 3 - 2 of 3
    /// in: Hugh Turn, Rhoda Rage; waiting for Axel Grease (Turn 3)." With
    /// nobody in from a phone, exactly the old wording.
    /// </summary>
    /// <param name="arrived">For each marshal, in the same order, whether they're in. Null or all false for none.</param>
    public static string Ask(string roundLabel, IReadOnlyList<(string Name, string Post)> marshals, bool waiting, IReadOnlyList<bool>? arrived)
    {
        var inCount = arrived is null ? 0 : marshals.Where((_, i) => i < arrived.Count && arrived[i]).Count();

        if (inCount == 0)
        {
            var who = string.Join(", ", marshals.Select(m => Who(m.Name, m.Post)));

            return waiting
                ? $"{roundLabel} is waiting for its marshals: {who}."
                : $"Marshals for {roundLabel}: {who}. In place?";
        }

        var inNames = marshals.Where((_, i) => i < arrived!.Count && arrived[i]).Select(m => m.Name);
        var missing = marshals.Where((_, i) => !(i < arrived!.Count && arrived[i])).Select(m => Who(m.Name, m.Post)).ToList();
        var head = waiting ? $"{roundLabel} is waiting for its marshals" : $"Marshals for {roundLabel}";

        return missing.Count == 0
            ? $"{head} - all {marshals.Count} in: {string.Join(", ", inNames)}."
            : $"{head} - {inCount} of {marshals.Count} in: {string.Join(", ", inNames)}; waiting for {string.Join(", ", missing)}.";
    }

    /// <summary>What the announcer says once, at zero: "Marshals for Q2 Heat 3, to your posts please: Hugh Turn on Turn 1 and Rhoda Rage on Turn 2. We start when you're in place."</summary>
    public static string Call(string roundLabel, IReadOnlyList<(string Name, string Post)> marshals)
    {
        var each = marshals
            .Select(m => string.IsNullOrWhiteSpace(m.Post) ? m.Name : $"{m.Name} on {m.Post}")
            .ToList();

        var list = each.Count switch
        {
            0 => string.Empty,
            1 => each[0],
            _ => $"{string.Join(", ", each.Take(each.Count - 1))} and {each[^1]}",
        };

        return $"Marshals for {roundLabel}, to your posts please: {list}. We start when you're in place.";
    }
}

/// <summary>The deputy's phone (12 Nov 2026, setup plan D8): Start now and Marshals in place from the remote.</summary>
public sealed partial class RaceControlControl
{
    /// <summary>The remote's "autonow": start the next race now. Null when it can't, with the reason in <paramref name="refusal"/>.</summary>
    private string? RemoteAutoNow(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "Auto start isn't counting down to a race right now.";
            return null;
        }

        refusal = null;
        AutoStartNow();
        return "Starting now.";
    }

    /// <summary>The remote's "marshalsin": the next race's marshals are in place.</summary>
    private string? RemoteMarshalsIn(out string? refusal)
    {
        if (!AutoStartOn || !_autoStart.IsOpen)
        {
            refusal = "No race is waiting for its marshals right now.";
            return null;
        }

        refusal = null;
        ConfirmMarshals();
        return "Marshals in place.";
    }
}

/// <summary>
/// Marshals in place from the marshals' own phones, and the countdown the
/// phones show (12 Nov 2026, setup plan step 8).
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The countdown last worked out for the phones. Replaced whole, so read from any thread.</summary>
    private volatile PhoneCountdown? _phoneCountdown;

    /// <summary>The round the "all in from their phones" line was last logged for, so it is said once.</summary>
    private string? _allInLoggedFor;

    /// <summary>
    /// What the Live and Schedule pages count down between races (setup plan
    /// step 8): the next race's start and whether it's counting, held or due.
    /// Null with Auto start not counting. Safe off the UI thread.
    /// </summary>
    public PhoneCountdown? CountdownForPhones => _phoneCountdown;

    /// <summary>
    /// A marshal tapped "I'm at my post" on their phone (the "post" ask, filed
    /// by the laptop by itself): recorded for that round, if they really are
    /// one of its marshals and it hasn't run. The words go back to the phone.
    /// </summary>
    /// <param name="roundId">The round the phone named.</param>
    /// <param name="transponder">The driver's main transponder.</param>
    public (bool Ok, string Said) MarshalAtPost(string roundId, string transponder)
    {
        if (_rounds?.Get(roundId) is not { } round)
        {
            return (false, "That race isn't in tonight's program any more.");
        }

        if (!string.IsNullOrWhiteSpace(round.LinkedSessionId))
        {
            return (false, $"{round.RoundLabel} has already run.");
        }

        var main = MainOf(transponder);
        var post = MarshalPostsFor(round).FirstOrDefault(p => string.Equals(p.Main, main, StringComparison.OrdinalIgnoreCase));

        if (post.Main is null)
        {
            return (false, $"You're not marshalling {round.RoundLabel} any more - have a look at your duty.");
        }

        if (_autoStart.Posts.In(round.Id, main))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] {MarshalsInPlace.Who(post.Name, post.Post)} is at their post for {round.RoundLabel} (from their phone).");
        }

        return (true, string.IsNullOrWhiteSpace(post.Post)
            ? $"Thanks - Race Control has you at your post for {round.RoundLabel}."
            : $"Thanks - Race Control has you on {post.Post} for {round.RoundLabel}.");
    }

    /// <summary>Whether a marshal (any of their transponders) has said from their phone they're in for a round.</summary>
    public bool MarshalIsIn(string roundId, string transponder) => _autoStart.Posts.IsIn(roundId, MainOf(transponder));

    /// <summary>A transponder's owner's main transponder, or itself.</summary>
    private string MainOf(string transponder) =>
        _profiles.Get(transponder)?.Transponder is { Length: > 0 } main ? main.Trim() : transponder.Trim();

    /// <summary>
    /// From the gap's tick: when every marshal of the next race has said from
    /// their phone they're in, the start stops waiting for them - as if the
    /// desk had pressed Marshals in place. Returns who's in, in the posts' order.
    /// </summary>
    private IReadOnlyList<bool> MarshalsFromPhones(RoundDefinition next, IReadOnlyList<(string Main, string Name, string Post, string Transponder)> posts)
    {
        var mains = posts.Select(p => p.Main).ToList();
        var arrived = _autoStart.Posts.Arrived(next.Id, mains);

        if (!_autoStart.MarshalsConfirmed && _autoStart.Posts.AllIn(next.Id, mains))
        {
            _autoStart.ConfirmMarshals();

            if (_allInLoggedFor != next.Id)
            {
                _allInLoggedFor = next.Id;
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [auto] All {Plural.Of(mains.Count, "marshal")} for {next.RoundLabel} said from their phones they're at their posts.");
            }
        }

        return arrived;
    }

    /// <summary>The phones' countdown, worked out again: after every gap tick, and when the gap closes.</summary>
    private void UpdatePhoneCountdown(DateTime now) =>
        _phoneCountdown = AutoStartOn && _autoStart.IsOpen
            ? PhoneCountdown.For(_autoStartState, _autoStart.StartsAtUtc(now, Gap.PushedBackMinutes))
            : null;
}
