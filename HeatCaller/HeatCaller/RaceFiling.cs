namespace HeatCaller;

/// <summary>
/// Files a finished race, in the one order that cannot lose it: the history
/// write first, and only if that landed, the round marked as run and the
/// crash journal let go.
///
/// Pulled out of RaceControlControl.SaveCompletedSessionToHistory so the
/// order can be tested. The page still builds the record and does
/// everything around the filing - the badges, the spoken summary, the
/// results card, the "not saved" dialog - through the two callbacks, which
/// run at exactly the points they always did.
/// </summary>
internal static class RaceFiling
{
    /// <summary>
    /// The finish call, then the filing - and nothing the announcer does can
    /// stand between the two.
    ///
    /// The one handler that saves a finished race announced first, and the
    /// finish call does real work on this thread (the finish-call hold,
    /// a sound, the voice template). The event is raised through SafeRaise,
    /// which logs whatever escapes a handler and carries on - so a template
    /// that threw took the save with it, and the board showed a finished
    /// race nobody had filed (25 Sep audit). The call stays first, so it
    /// still lands the moment the flag drops.
    /// </summary>
    public static void AfterTheFlag(Action announce, Action file, Action<string> log)
    {
        try
        {
            announce();
        }
        catch (Exception ex)
        {
            log($"[announcer] The finish call failed ({ex.Message}). The race is being saved all the same.");
        }

        file();
    }

    public enum Outcome
    {
        /// <summary>The history could not be written. Nothing else was touched: the round is still unrun and the journal still holds the race.</summary>
        NotSaved,

        /// <summary>In the history, the round (if any) linked, the journal cleared.</summary>
        Saved,
    }

    /// <param name="round">The round the race was run for, or null when none was picked or found.</param>
    /// <param name="autoAdvanced">True when the round was picked automatically - it is said in the log.</param>
    /// <param name="autoAdvanceWanted">Whether automatic round picking is on - only for the log line when there is no round.</param>
    /// <param name="afterSaved">Runs once the history write has landed, before the round is linked: badges, the spoken summary.</param>
    /// <param name="raceDayOpen">False when no race day is open - which is why no round was picked automatically (RoundPicker), and the log says so.</param>
    /// <param name="beforeJournalCleared">
    /// Runs once the round is linked, before the journal goes: the results
    /// card and SessionSaved. SessionSaved must come AFTER the link - the
    /// "Round:" box is rebuilt from the unrun rounds on it, and raised
    /// before, it kept the round that had just been run selected, so the
    /// next race was filed against it too.
    /// </param>
    public static Outcome Commit(
        SessionRecord record,
        RoundDefinition? round,
        bool autoAdvanced,
        bool autoAdvanceWanted,
        SessionHistoryStore history,
        RoundDefinitionStore? rounds,
        RaceJournal journal,
        Action afterSaved,
        Action beforeJournalCleared,
        Action<string> log,
        bool raceDayOpen = true)
    {
        // The one thing everything below stands on. SafeFile returns false
        // rather than throwing when a write cannot be made - a locked or full
        // disk must not take the app down mid-race - so this answer has to be
        // asked for. It used to be dropped: the operator was told the race was
        // saved, the heat was marked recorded, the result was published, and
        // then the race journal was deleted, all on the strength of a write
        // that never happened. One backup tool holding race-history.json open
        // for a second lost an A-main outright.
        //
        // The heat is deliberately left unrecorded and the journal
        // deliberately left alone, so the next launch offers this race back.
        if (!history.Add(record))
        {
            return Outcome.NotSaved;
        }

        log($"[session] Saved \"{record.Label}\" to history with {Plural.Of(record.Results.Count, "racer")}.");

        afterSaved();

        // Tie the round to this session so the race program shows it as run,
        // and qualifying/seeding can find its results. Never for a demo: it
        // would mark a real heat as run with invented results.
        if (!record.IsDemo && round is not null && rounds is not null)
        {
            if (rounds.MarkRecorded(round.Id, record.Id))
            {
                if (autoAdvanced)
                {
                    log($"[session] No round was selected, so this race was assigned to the next one in the "
                        + $"program: \"{round.RoundLabel}\" ({round.ClassName}).");
                }

                // Read back from the store rather than trusting the object
                // just handed to it - this is the assertion that matters.
                var verify = rounds.Get(round.Id);
                log($"[session] Linked round \"{round.RoundLabel}\" class=\"{round.ClassName}\" id={round.Id[..Math.Min(8, round.Id.Length)]} "
                    + $"-> store now reports IsRecorded={verify?.IsRecorded.ToString() ?? "ROUND MISSING"}.");
            }
            else
            {
                log($"[session] Could NOT link to round \"{round.RoundLabel}\" - that round is no longer in the race "
                    + "program (deleted or re-generated since it was selected).");
            }
        }
        else if (!record.IsDemo && rounds is not null)
        {
            // Saved, but not attached to anything. Worth saying: a heat
            // staying on "Scheduled" after a race looks like a bug, and the
            // actual cause is just that no round was selected.
            log(!raceDayOpen
                ? "[session] Saved to history, but NOT tied to a heat - no race day is open, so no round is picked "
                  + "automatically (it could only be one left over from an earlier meeting). If it belongs to a heat, "
                  + "open the race day and pick the round in the \"Round:\" box before the start."
                : autoAdvanceWanted
                ? "[session] Saved to history, but NOT tied to a heat - the race program has no unrecorded "
                  + "rounds left for it to advance to."
                : "[session] Saved to history, but NOT tied to a heat - no round was picked in the \"Round:\" box on Race Control, and "
                  + "\"Start the next race automatically\" is off for this day (Race Day > Setup).");
        }

        beforeJournalCleared();

        // Safely in the history now: the journal has done its job, and its
        // absence is what tells the next launch there is nothing to restore.
        // Only reachable when the write above actually landed.
        journal.Clear();

        return Outcome.Saved;
    }

    /// <summary>
    /// A finished race's result rows, one per car, in the order given
    /// (finishing order - the caller has already left the ghosts out). The
    /// name and car come from the driver's profile, falling back to the
    /// transponder for a car nobody has named.
    ///
    /// Moved here unchanged from RaceControlControl.SaveCompletedSessionToHistory
    /// (26 Sep 2026), so the race-night test (RaceNightTests) files a race
    /// with the very rows Race Control saves.
    /// </summary>
    public static List<SessionResultEntry> ResultsFrom(IReadOnlyList<RacerStanding> racers, ProfileStore profiles) =>
        racers.Select((s, i) =>
        {
            var profile = profiles.Get(s.TransponderId);
            return new SessionResultEntry
            {
                Position = i + 1,
                Transponder = s.TransponderId,
                DriverName = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : s.TransponderId,
                CarModel = profile?.CarModel ?? string.Empty,
                Laps = s.Laps,
                FastLapMs = s.FastestLapMs,
                ElapsedMs = s.ElapsedMs,
                Status = "active",
                LapTimesMs = s.LapTimesMs.ToList(),
                RejectedLaps = s.RejectedLaps,
                LapsLed = s.LapsLed,
                StartPosition = s.StartPosition,

                // Copies, not the live warnings. The board keeps its
                // standings after the flag, and Clear Penalty there zeroes
                // each warning's time - which, through the shared objects,
                // quietly rewrote the saved result's warnings the next time
                // the history was written, although the log says a change
                // after the flag leaves the history alone.
                Warnings = s.Warnings.Select(w => new RaceWarning
                {
                    AtUtc = w.AtUtc,
                    RaceTimeMs = w.RaceTimeMs,
                    Number = w.Number,
                    Reason = w.Reason,
                    Action = w.Action,
                    PenaltyMs = w.PenaltyMs,
                }).ToList(),
            };
        }).ToList();
}
