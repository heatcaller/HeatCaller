using Velopack;
using Velopack.Sources;

namespace HeatCaller;

/// <summary>
/// Updates, on the one rule that matters: NEVER MID-MEETING.
///
/// A race night is live, in front of people, with cars on the track and
/// nothing to fall back on. An update that restarted the app at the wrong
/// moment would be worse than no updates at all - so the shape here is
/// deliberately timid:
///
///   CHECK quietly. At startup and on a slow timer afterwards. No internet,
///   a feed that is not there yet, a rate limit: say nothing at all. A club
///   on a phone hotspot at a track must never see an error about updates.
///
///   DOWNLOAD in the background, and only the difference - Velopack's delta
///   packages mean a 750 MB app with a 310 MB model that never changes still
///   updates in a few MB.
///
///   ASK, and only when NOTHING IS IN PROGRESS: no race day open, Race
///   Control idle, and no unfinished race in the journal (see
///   NothingInProgress). Between races on a race night, all three can be
///   true and the question would still be an interruption - which is why
///   "After tonight" is one of the answers and the default one.
///
///   BACK UP first, always, before an update is applied. The one thing that
///   cannot be undone by publishing the old build again is data a new version
///   has already rewritten.
///
///   NEVER RESTART ON ITS OWN. Velopack is told not to apply anything at
///   startup by itself (SetAutoApplyOnStartup(false) in Program) - every
///   restart here was asked for by somebody.
///
/// WHAT THE THREE ANSWERS DO
///
///   Update now      backs up, applies, and restarts - now.
///   After tonight   keeps the downloaded version and installs it the NEXT
///                   TIME HEATCALLER STARTS, which is the only moment that
///                   is safe: nothing is open, no race is running, nobody
///                   is waiting. (A start in the middle of a meeting - a
///                   crash, a new look - is checked, and waits for the next
///                   one; see ApplyWhatWasHeldForNextStart.) Remembered in settings
///                   (UpdateWaitingForNextStart), because it is a promise
///                   that has to outlive the process.
///   Not now         nothing. Not asked again this run; asked again next
///                   start, because a new version is still news.
///
/// The feed is a SEPARATE PUBLIC REPO. The source repo is private, and
/// reading a private feed would need a GitHub token shipped inside the app,
/// where it would not be a secret for long.
/// </summary>
internal static class AppUpdates
{
    /// <summary>
    /// The public releases repo the updater reads. Not the source repo -
    /// see the note at the top of this class.
    /// </summary>
    internal const string FeedUrl = "https://github.com/heatcaller/HeatCaller-releases";

    /// <summary>
    /// The channel clubs are on. `vpk pack -c beta` has to match it, or a
    /// club on the beta channel is told there is nothing new forever.
    /// </summary>
    internal const string Channel = "beta";

    /// <summary>How long after startup the first check waits, so it is never part of what makes the app slow to open.</summary>
    internal static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(2);

    /// <summary>How often it looks again. Long: a club leaves the app open all evening, and there is nothing urgent about a release.</summary>
    internal static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    /// <summary>Where the pre-update backup goes, and the words that say what it is for.</summary>
    internal const string BackupReason = "before updating HeatCaller";

    /// <summary>
    /// What the app is told to show. The host owns every window, so this
    /// class decides and the host does it - which is also what makes the
    /// deciding testable.
    /// </summary>
    internal enum Answer
    {
        UpdateNow = 0,
        AfterTonight = 1,
        NotNow = 2,
    }

    /// <summary>The three buttons, in the order Answer numbers them. Last is the one a stray Escape must land on.</summary>
    internal static readonly string[] AnswerLabels = { "Update now", "After tonight", "Not now" };

    /// <summary>
    /// Whether an update may be APPLIED at this moment. The whole
    /// never-mid-meeting rule, as one function of three facts, so
    /// BugHunt4UpdateTests can hold it to them without a race night.
    /// </summary>
    /// <param name="raceDayOpen">A race day is open - the club is at the track.</param>
    /// <param name="raceState">What Race Control is doing. Anything but Idle means a race is being set up, started or run.</param>
    /// <param name="unfinishedRaceInJournal">The race journal has a race in it that was never saved - a crash or a close mid-race.</param>
    internal static bool NothingInProgress(bool raceDayOpen, RaceState raceState, bool unfinishedRaceInJournal) =>
        !raceDayOpen
        && raceState == RaceState.Idle
        && !unfinishedRaceInJournal;

    /// <summary>
    /// The same rule, with the journal read only when the other two have not
    /// already held the update back.
    ///
    /// WHY (10 Nov 2026, bug hunt 16): the live check passed
    /// RaceJournal.Unfinished() as a plain bool, so it read the journal of
    /// the race being run on every update offer. That read holds the file
    /// open without delete sharing, and a lap's journal write landing inside
    /// it failed its swap and raised "race journal could not be written"
    /// about a race that was fine. Mid-race the answer is already no, so
    /// there is nothing to read the journal for.
    /// </summary>
    internal static bool NothingInProgress(bool raceDayOpen, RaceState raceState, Func<bool> unfinishedRaceInJournal) =>
        !raceDayOpen
        && raceState == RaceState.Idle
        && !unfinishedRaceInJournal();

    /// <summary>
    /// Why an update is being held back, for the console - so a club that
    /// wonders why it has not updated can see the reason rather than guess.
    /// Null when nothing is holding it.
    /// </summary>
    internal static string? HeldBecause(bool raceDayOpen, RaceState raceState, bool unfinishedRaceInJournal)
    {
        if (raceDayOpen)
        {
            return "a race day is open";
        }

        if (raceState != RaceState.Idle)
        {
            return $"Race Control is busy ({raceState})";
        }

        return unfinishedRaceInJournal ? "there is an unfinished race in the journal" : null;
    }

    /// <summary>What the question says. Its own method so the wording is testable and in one place.</summary>
    internal static string Question(string newVersion, string thisVersion) =>
        $"HeatCaller {newVersion} is ready to install. This PC is on {thisVersion}.\r\n\r\n"
        + "It takes a few seconds and restarts the app. A backup of the club's data is taken first, "
        + "whichever you choose below, so nothing is at risk either way.\r\n\r\n"
        + "If there is any chance of racing tonight, choose After tonight - it installs the next time "
        + "HeatCaller is opened, and nothing happens in the meantime.";

    // --- The live part ------------------------------------------------------

    // EVERY ONE OF THESE IS CALLED FROM A BACKGROUND THREAD. CheckAsync is
    // started and forgotten, and what follows the first await is not on the
    // UI thread - so each of these five is MainForm's job to marshal
    // (Invoke), and each one here is written as though it blocks, because it
    // does. Reading race state or opening a window from the wrong thread is
    // the kind of fault that only shows up on somebody else's race night.

    /// <summary>
    /// What the app is doing right now: NothingInProgress, answered from the
    /// live race day, Race Control and journal. Set once by MainForm, and
    /// must read them on the UI thread.
    /// </summary>
    internal static Func<bool>? NothingInProgressNow { get; set; }

    /// <summary>
    /// Shows the question (new version, this version) and hands back which
    /// button was pressed. Set once by MainForm, and must open the window on
    /// the UI thread and block until it is answered.
    /// </summary>
    internal static Func<string, string, Answer>? Ask { get; set; }

    /// <summary>
    /// Makes the backup and hands back where it went, or throws. Set once by
    /// MainForm. The zip itself is safe off the UI thread, but the secrets it
    /// needs are not - see BackupService.CaptureSecrets.
    /// </summary>
    internal static Func<string>? BackUp { get; set; }

    /// <summary>
    /// Keeps the "After tonight" answer: puts the version in
    /// UpdateWaitingForNextStart and saves the settings, handing back whether
    /// the save happened. Set once by MainForm, and must do it on the UI
    /// thread - the live settings are that thread's to change, and a save
    /// from here could serialise them while it was changing them, or land
    /// after a newer save and put older settings back on disk.
    /// </summary>
    internal static Func<string, bool>? HoldForNextStart { get; set; }

    /// <summary>
    /// Runs the "Update now" install on the UI thread, after the last saves
    /// closing the window would have made - tonight's transponder readings,
    /// the journal, the settings - and SafeFile.Freeze, handing back false
    /// when the window could not take it (it is closing). Set once by
    /// MainForm. Velopack ends the process straight away, so FormClosing
    /// never runs: without this, all of that was skipped, from this
    /// background thread. Unset, the install runs here as it always did.
    /// </summary>
    internal static Func<Action, bool>? RunInstallOnUiThread { get; set; }

    /// <summary>Where this class says things. The console, by way of MainForm.</summary>
    internal static Action<string>? Log { get; set; }

    private static readonly object Gate = new();
    private static UpdateManager? _manager;
    private static bool _busy;

    /// <summary>A version already offered this run and answered "Not now" - not offered again until the app is started again.</summary>
    private static string? _declinedThisRun;

    /// <summary>
    /// The update manager, or null when this copy is not an installed one -
    /// a developer running from bin\Debug, or a portable build. Nothing here
    /// does anything in that case, which is what makes it safe to call from
    /// MainForm without a switch around it.
    /// </summary>
    private static UpdateManager? Manager()
    {
        lock (Gate)
        {
            if (_manager is not null)
            {
                return _manager;
            }

            try
            {
                var source = new GithubSource(FeedUrl, accessToken: null, prerelease: false);
                var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = Channel });

                if (!manager.IsInstalled)
                {
                    return null;
                }

                _manager = manager;
                return _manager;
            }
            catch (Exception)
            {
                // A feed URL that will not parse, a locator that cannot find
                // the install: no updates, and nothing said.
                return null;
            }
        }
    }

    /// <summary>Whether this copy can update itself at all - an installed build, with checks left on.</summary>
    /// <remarks>
    /// Never in a rehearsal. The backup before an update would be of the
    /// rehearsal copy, and Velopack restarts the new version as the real app
    /// on the club's real data - which was never backed up - while whoever
    /// is practising thinks they are still in the copy.
    /// </remarks>
    internal static bool Available(AppSettings settings) =>
        settings.UpdateChecksOn && !AppPaths.IsRehearsal && Manager() is not null;

    /// <summary>
    /// The switches the new version is restarted with. Only --dpi-aware is
    /// carried over: it is how this copy was asked to draw itself, and
    /// losing it at an update would quietly change the look. --rehearsal
    /// never gets this far (see Available), and the rest are Velopack's or
    /// the freeze watchdog's, which never reach a running app.
    /// </summary>
    private static string[]? RestartArgs() =>
        Program.DpiAwareWanted(Environment.GetCommandLineArgs()) ? new[] { Program.DpiAwareSwitch } : null;

    /// <summary>
    /// The "After tonight" promise, kept. Called from MainForm's startup,
    /// before the main window is of any use to anybody: if an update was
    /// downloaded and held for the next start, this IS the next start.
    ///
    /// Returns true when it is applying one, in which case the app is about
    /// to be restarted by Velopack and the caller should stop what it is
    /// doing.
    ///
    /// The next start is NOT always between meetings: the app restarts
    /// itself for a new look, after a crash, or because a volunteer closed
    /// and opened it - all of which can happen with a race day open, hours
    /// after "After tonight" was answered before it was opened. So the same
    /// facts Offer asks about are asked again here, and while any of them
    /// holds the promise is kept (not cleared) for a later start.
    /// </summary>
    /// <param name="raceDayOpen">A race day is open, or the race days could not be read and it cannot be told.</param>
    /// <param name="raceState">What Race Control is doing - Idle at any ordinary start.</param>
    /// <param name="unfinishedRaceInJournal">The race journal has an unfinished race in it.</param>
    internal static bool ApplyWhatWasHeldForNextStart(
        AppSettings settings, bool raceDayOpen, RaceState raceState, bool unfinishedRaceInJournal)
    {
        var waiting = settings.UpdateWaitingForNextStart;

        if (waiting.Length == 0)
        {
            return false;
        }

        // A rehearsal's settings are a copy of the club's, promise and all.
        // It is the real app's promise to keep, not the copy's - see
        // Available - so it is left alone in both.
        if (AppPaths.IsRehearsal)
        {
            return false;
        }

        if (HeldBecause(raceDayOpen, raceState, unfinishedRaceInJournal) is { } why)
        {
            Say($"The update to {waiting} held for the next start is still waiting - {why}. It installs at a start when nothing is.");
            return false;
        }

        // Cleared first, whatever happens next. A promise that cannot be
        // kept - the staged package went, the version moved on - must not be
        // retried at every start forever.
        settings.UpdateWaitingForNextStart = string.Empty;
        settings.Save();

        var manager = Manager();

        if (manager?.UpdatePendingRestart is not { } pending)
        {
            Say($"The update to {waiting} that was held for the next start isn't there any more - it will be offered again.");
            return false;
        }

        var pendingVersion = pending.Version?.ToString() ?? string.Empty;

        if (!string.Equals(pendingVersion, waiting, StringComparison.OrdinalIgnoreCase))
        {
            Say($"{waiting} was held for the next start but {pendingVersion} is what is staged - not installing it without asking again.");
            return false;
        }

        Say($"Installing {waiting}, held from last time. Backing up first.");

        if (!TryBackUp())
        {
            return false;
        }

        try
        {
            // Installs and restarts into the new version. This is the start
            // of a run that has barely begun - there is no window up yet and
            // nothing for the club to be looking at - and the restart was
            // asked for: "After tonight" is somebody saying yes, just later.
            manager.ApplyUpdatesAndRestart(pending, RestartArgs());
            return true;
        }
        catch (Exception ex)
        {
            Say($"The update to {waiting} couldn't be installed ({ex.GetType().Name}: {ex.Message}) - carrying on with this version.");
            return false;
        }
    }

    /// <summary>
    /// Looks for an update, downloads it if there is one, and asks what to do
    /// about it. Everything in here is quiet on failure: see the class
    /// comment.
    ///
    /// Call it and forget it. One at a time, and never while one is still
    /// running.
    /// </summary>
    internal static async Task CheckAsync(AppSettings settings)
    {
        if (!settings.UpdateChecksOn)
        {
            return;
        }

        lock (Gate)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
        }

        try
        {
            if (Manager() is not { } manager)
            {
                return;
            }

            UpdateInfo? update;

            try
            {
                update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // No internet, GitHub unreachable, rate limited, the feed not
                // published yet. All of it silent, by design: an update check
                // is not something a club asked for, so it may not interrupt
                // them to fail.
                return;
            }

            if (update is null)
            {
                return;
            }

            var newVersion = update.TargetFullRelease.Version?.ToString() ?? string.Empty;

            if (newVersion.Length == 0)
            {
                return;
            }

            if (string.Equals(newVersion, _declinedThisRun, StringComparison.OrdinalIgnoreCase)
                || string.Equals(newVersion, settings.UpdateWaitingForNextStart, StringComparison.OrdinalIgnoreCase))
            {
                // Already answered. Nothing to say again.
                return;
            }

            Say($"HeatCaller {newVersion} is available. Downloading it in the background.");

            try
            {
                await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Say($"The download of {newVersion} didn't finish ({ex.GetType().Name}) - it will be tried again later.");
                return;
            }

            Say($"{newVersion} is downloaded and ready.");

            Offer(manager, update, newVersion);
        }
        finally
        {
            lock (Gate)
            {
                _busy = false;
            }
        }
    }

    /// <summary>
    /// Asks, once the app is doing nothing. Waits rather than interrupts: a
    /// downloaded update sits there, and the question comes the next time
    /// this is called and the app is idle.
    /// </summary>
    private static void Offer(UpdateManager manager, UpdateInfo update, string newVersion)
    {
        if (NothingInProgressNow is not { } idle || Ask is not { } ask || HoldForNextStart is not { } hold)
        {
            // No host wired up. The update stays downloaded; nothing is
            // applied without somebody being asked.
            return;
        }

        if (!idle())
        {
            Say($"{newVersion} is ready and waiting - it won't be offered while a race day is open or a race is on.");
            return;
        }

        var answer = ask(newVersion, AppVersion.Number);

        switch (answer)
        {
            case Answer.UpdateNow:
                Say($"Installing {newVersion} now. Backing up first.");

                if (!TryBackUp())
                {
                    return;
                }

                // Checked again, right at the end: the question was on screen
                // for as long as it took somebody to read it, and a race can
                // have been staged in that time.
                if (!idle())
                {
                    Say("Something started while that was on screen - not installing now. It will be offered again.");
                    return;
                }

                try
                {
                    if (RunInstallOnUiThread is { } onUiThread)
                    {
                        // On the UI thread, after the saves closing would
                        // have made - see RunInstallOnUiThread. A failed
                        // install is caught there and carried back here, so
                        // it is not mistaken for the window having gone.
                        Exception? failed = null;

                        var ran = onUiThread(() =>
                        {
                            try
                            {
                                manager.ApplyUpdatesAndRestart(update.TargetFullRelease, RestartArgs());
                            }
                            catch (Exception ex)
                            {
                                failed = ex;
                            }
                        });

                        if (failed is not null)
                        {
                            // The saves were frozen just before the install,
                            // so nothing more is written this run.
                            Say($"The update couldn't be installed ({failed.GetType().Name}: {failed.Message}) - nothing was changed, "
                                + "but HeatCaller has stopped saving for this run. Close it and open it again before carrying on.");
                        }
                        else if (!ran)
                        {
                            Say("HeatCaller is closing down - not installing now. It will be offered again.");
                        }
                    }
                    else
                    {
                        manager.ApplyUpdatesAndRestart(update.TargetFullRelease, RestartArgs());
                    }
                }
                catch (Exception ex)
                {
                    Say($"The update couldn't be installed ({ex.GetType().Name}: {ex.Message}) - nothing was changed.");
                }

                return;

            case Answer.AfterTonight:
                // Not set and saved here: this is a background thread. See
                // HoldForNextStart.
                if (!hold(newVersion))
                {
                    _declinedThisRun = newVersion;
                    Say($"{newVersion} couldn't be held for the next start (the settings weren't saved) - it will be offered again next time HeatCaller starts.");
                    return;
                }

                Say($"{newVersion} will be installed the next time HeatCaller is opened. Nothing happens before then.");
                return;

            default:
                _declinedThisRun = newVersion;
                Say($"{newVersion} was left for now. It will be offered again next time HeatCaller starts.");
                return;
        }
    }

    /// <summary>
    /// The backup before an update. False means it did not happen, and
    /// nothing is installed in that case: an update with no way back is the
    /// one thing this class will not do.
    /// </summary>
    private static bool TryBackUp()
    {
        if (BackUp is not { } backUp)
        {
            Say("No backup could be taken (nothing is wired up to make one), so the update was not installed.");
            return false;
        }

        try
        {
            var path = backUp();
            Say($"Backed up {BackupReason} to {path}.");
            return true;
        }
        catch (Exception ex)
        {
            Say($"The backup {BackupReason} failed ({ex.GetType().Name}: {ex.Message}), so the update was NOT installed. Nothing was changed.");
            return false;
        }
    }

    private static void Say(string line) => Log?.Invoke("[update] " + line);
}
