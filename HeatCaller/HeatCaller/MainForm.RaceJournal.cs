namespace HeatCaller;

/// <summary>
/// MainForm's filing and crash journal: the race filing service and the
/// journal recorder, made here, and the session's events that drive the
/// journal.
///
/// Added 23 Oct 2026 (timing layer, P2). Filing (RaceFilingService) and the
/// crash journal (RaceJournalRecorder) used to live inside Race Control, so
/// the journal was written only when that page's handlers ran. They are the
/// app's now: Race Control files through the service and writes its own
/// moments (a restore, an undo, closing) through the recorder, and every
/// crossing, flag and correction is journaled from here whether or not the
/// page is built. Fields stay in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Makes the filing service and the journal recorder, and wires the
    /// session's events to the journal. Called before the pages are built,
    /// so these handlers are on the session ahead of Race Control's - the
    /// journal (and the race recording that follows it) is written at the
    /// green before the start is called, as it was when Race Control wrote it
    /// first thing in its own flag handler.
    /// </summary>
    private void WireRaceFiling()
    {
        _journalRecorder = new RaceJournalRecorder(_race);

        _raceFiling = new RaceFilingService(
            _race, _journalRecorder, _sessionHistory, _profiles, _trackLayouts, _settings, _rounds, _raceEvents)
        {
            Log = OnClientRawMessage,
        };

        // An earlier race that could not be filed is tried again before the
        // next race's first journal write replaces it - see
        // RaceFilingService.RetryUnfiledBeforeJournal.
        _journalRecorder.BeforeEachWrite = _raceFiling.RetryUnfiledBeforeJournal;

        // WHY (24 Nov 2026, bug hunt 24 #88, Asher): the race's lead change
        // count goes in the journal too, so a race picked up after a crash
        // does not call its fifth lead change "the first". LeadTracker locks,
        // so this is safe on the timing thread as well.
        _journalRecorder.LeadsSource = () => _announcer?.Leads.Snapshot();

        // The journal says when it cannot write, and when it can again -
        // the one moment the race is running without its safety net is
        // worth a line in the console.
        _journalRecorder.Journal.StatusChanged += message =>
            OnClientRawMessage($"{AppTime.LogStampNow()}  [race] {message}");

        // The moments Race Control wrote the journal at, from the session's
        // own events. On the UI thread, as Race Control's handlers were: the
        // session and the journal are not thread-safe, and in this phase the
        // session's events all arrive here anyway.
        // (StateChanged, FlagChanged, LapProcessed, StandingAdjusted and
        // StandingRemoved - see RaceJournalRecorder.Listen.)
        //
        // WHY (23 Oct 2026, timing layer): the show's block is abandoned
        // first, before the marshal, as Race Control's flag handler does.
        // These handlers now run AHEAD of that one, and at the green a
        // write on the UI thread also starts the race recording (opening
        // the store and the capture) - so without this the block's music
        // and speech ran on through all of that, where it used to stop the
        // moment the flag changed. The very call Race Control makes, through
        // its own ShowMusic, so it does nothing until that is wired; harmless
        // twice (ShowTreatment.AbandonBlock).
        _race.FlagChanged += _ => _raceControlControl?.ShowMusic?.Invoke()?.AbandonBlock();

        // WHY (23 Oct 2026, timing layer, P4b): threaded, the journal listens
        // on the timing thread, so it is written at every lap and flag even
        // while this window is frozen - see RaceJournalRecorder.ListenOnTimingSide.
        // The unfiled-race retry it holds a write back for is asked there
        // (UnfiledRetryDue, safe off the UI thread) and run here, by a post.
        if (_race.IsThreaded)
        {
            _journalRecorder.RetryDueBeforeWrite = _raceFiling.UnfiledRetryDue;

            // WHY (24 Oct 2026, timing layer, P8): when the window is too
            // busy to retry an unfiled race in time, the journal writes the
            // race running anyway and says so here (posted, so on this thread).
            _journalRecorder.Log = line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}");
            _journalRecorder.ListenOnTimingSide(TryPost);
        }
        else
        {
            _journalRecorder.Listen(OnJournalMoment);
        }
    }

    /// <summary>
    /// Runs a journal moment on the UI thread - straight away when already
    /// on it, otherwise posted, never waited for (Race Control's pattern).
    /// </summary>
    private void OnJournalMoment(Action moment)
    {
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(moment);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // The window is closing: nothing is left to post to. Closing
                // flushes the journal itself (FlushJournal).
            }

            return;
        }

        moment();
    }
}
