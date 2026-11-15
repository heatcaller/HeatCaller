namespace HeatCaller;

/// <summary>
/// Closing while a race's clips and highlight videos are still being made
/// (13 Nov 2026, bug hunt 17).
///
/// That work runs on a background thread for minutes after the flag - the
/// videos one at a time, the clips sent to the drivers' own nights last of
/// all - and it dies with the app. The window used to close on it without a
/// word, because no race was running. Now a person closing it is asked, and
/// can wait: the close is refused, the window carries on as normal, and it
/// closes by itself when the work is done.
/// </summary>
/// <remarks>
/// NOTHING HERE WAITS ON THE UI THREAD. The work logs through this window
/// and sends through the relay publisher, so a close that blocked until it
/// finished could wait on work that was waiting on it. The wait is a timer
/// that looks at RaceAudioRecorder.AfterRaceWork once a second.
///
/// Only a person is asked (see AsksBeforeClosingOnClips). Windows shutting
/// down, or a restart after a restore, closes as before; what that leaves
/// half made is removed by RaceAudioRecorder.SweepOrphans.
/// </remarks>
public partial class MainForm
{
    /// <summary>The buttons on the question, safest last: Escape and a closed window are the last one - see AppDialog.Choose.</summary>
    internal static readonly string[] ClipsClosingAnswers = { "Close now", "Wait, then close" };

    /// <summary>Running while the window is waiting to close on the clips; null otherwise.</summary>
    private System.Windows.Forms.Timer? _closeWhenClipsDone;

    /// <summary>
    /// Whether a close is held up to ask about clips and videos still being
    /// made: only when a person is closing the window, and only while there
    /// are some.
    /// </summary>
    internal static bool AsksBeforeClosingOnClips(CloseReason reason, int racesBeingFinished) =>
        reason == CloseReason.UserClosing && racesBeingFinished > 0;

    /// <summary>What is still being made, in words: "the drivers' clips and highlight videos from the last race".</summary>
    internal static string ClipsStillBeingMade(int races, int withVideos) =>
        (withVideos > 0 ? "the drivers' clips and highlight videos" : "the drivers' clips")
        + (races > 1 ? $" from the last {races} races" : " from the last race");

    /// <summary>The question put to somebody closing the window while clips are still being made.</summary>
    internal static string ClipsClosingQuestion(int races, int withVideos) =>
        $"HeatCaller is still making {ClipsStillBeingMade(races, withVideos)}.\n\n"
        + "Wait, and HeatCaller closes by itself as soon as they are finished."
        + (withVideos > 0 ? " A highlight video takes a few minutes." : string.Empty)
        + "\n\n"
        + "Close now, and the ones not finished are lost: they are not made again the next time HeatCaller opens, "
        + "and any clips still to go to the drivers' own nights are not sent. The results are saved, and the "
        + "race's own recording is kept.";

    /// <summary>The window's title while it waits to close.</summary>
    internal static string ClipsWaitingTitle(string title, int races, int withVideos) =>
        $"{title} - closing when {ClipsStillBeingMade(races, withVideos)} are finished";

    /// <summary>
    /// True while waiting to close on the clips with the race that was on the
    /// track when the person said to close anyway still running. Cleared once
    /// that race is off the track.
    /// </summary>
    private bool _closingOnRunningRace;

    /// <summary>True only inside the wait's own Close() when the race it was agreed on is still running: the race question isn't asked again.</summary>
    private bool _closingAgreedMidRace;

    /// <summary>
    /// Whether the race on the track stops the wait to close: only one that
    /// started while it waited.
    /// </summary>
    /// <remarks>
    /// WHY (14 Nov 2026, bug hunt 19): the person closing mid-race is asked
    /// "A race is running. Close HeatCaller anyway?" before the clips
    /// question. That race, already agreed to, stopped the wait on its first
    /// tick with "A race has started", and the app never closed.
    /// </remarks>
    internal static bool RaceStartedWhileWaitingForClips(bool raceOnTrack, bool closingOnRunningRace) =>
        raceOnTrack && !closingOnRunningRace;

    /// <summary>
    /// Asks, when there is something to ask about. True when the close is to
    /// be refused for now because the answer was to wait.
    /// </summary>
    private bool WaitsForClipsBeforeClosing(CloseReason reason)
    {
        var (races, withVideos) = RaceAudioRecorder.AfterRaceWork;

        if (!AsksBeforeClosingOnClips(reason, races))
        {
            StopWaitingForClips();
            return false;
        }

        var answer = AppDialog.Choose(
            this,
            ClipsClosingQuestion(races, withVideos),
            "Clips still being made",
            MessageBoxIcon.Warning,
            defaultIndex: ClipsClosingAnswers.Length - 1,
            ClipsClosingAnswers);

        // "Close now" - or they finished while the question was up.
        if (answer == 0 || RaceAudioRecorder.AfterRaceWork.Races == 0)
        {
            StopWaitingForClips();
            return false;
        }

        if (_closeWhenClipsDone is null)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Waiting for {ClipsStillBeingMade(races, withVideos)} "
                + "before closing. HeatCaller closes by itself when they are finished.");

            _closeWhenClipsDone = new System.Windows.Forms.Timer { Interval = 1000 };
            _closeWhenClipsDone.Tick += (_, _) => CloseIfClipsAreDone();
            _closeWhenClipsDone.Start();
        }

        // Reaching here mid-race means the person said to close anyway.
        _closingOnRunningRace = _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        Text = ClipsWaitingTitle(AppVersion.Full, races, withVideos);
        return true;
    }

    /// <summary>The once-a-second look while waiting: closes the window when the work is over.</summary>
    private void CloseIfClipsAreDone()
    {
        if (_closingDown || IsDisposed || Disposing)
        {
            StopWaitingForClips();
            return;
        }

        // A race started while it waited: the window is in use again, and
        // must not close under it when the last video lands. Not the race
        // the person already agreed to close on - see RaceStartedWhileWaitingForClips.
        var raceOnTrack = _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        _closingOnRunningRace &= raceOnTrack;

        if (RaceStartedWhileWaitingForClips(raceOnTrack, _closingOnRunningRace))
        {
            StopWaitingForClips();
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] A race has started, so HeatCaller is staying open. "
                + "Close it again when the racing is done.");
            return;
        }

        var (races, withVideos) = RaceAudioRecorder.AfterRaceWork;

        if (races > 0)
        {
            Text = ClipsWaitingTitle(AppVersion.Full, races, withVideos);
            return;
        }

        // Not from under a window somebody has open on top of this one -
        // closing would throw away whatever they were typing in it. The
        // next tick looks again.
        if (Application.OpenForms.Cast<Form>().Any(form => form.Modal))
        {
            return;
        }

        // The race the person agreed to close on, still running: not asked
        // about it a second time.
        var agreedToRace = _closingOnRunningRace;

        StopWaitingForClips();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] The clips are finished. Closing HeatCaller.");

        _closingAgreedMidRace = agreedToRace;

        try
        {
            Close();
        }
        finally
        {
            _closingAgreedMidRace = false;
        }
    }

    /// <summary>Stops waiting to close, and puts the window's title back.</summary>
    private void StopWaitingForClips()
    {
        if (_closeWhenClipsDone is not { } timer)
        {
            return;
        }

        _closeWhenClipsDone = null;
        _closingOnRunningRace = false;
        timer.Stop();
        timer.Dispose();

        if (!IsDisposed && !Disposing)
        {
            Text = AppVersion.Full;
        }
    }
}
