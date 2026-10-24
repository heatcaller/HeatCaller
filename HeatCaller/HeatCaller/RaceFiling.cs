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
    /// <param name="demoMayFillTheProgramme">
    /// Whether a DEMO race may mark its round as run - see
    /// AppSettings.DemoFillsTheProgramme, which is how a club rehearses a
    /// whole night. It does not change what a demo counts for: every
    /// record in this app is guarded by IsDemoSession where it is computed,
    /// not by the round link.
    /// </param>
    /// <param name="beforeJournalCleared">
    /// Runs once the round is linked, before the journal goes: the results
    /// card and SessionSaved. SessionSaved must come AFTER the link - the
    /// "Round:" box is rebuilt from the unrun rounds on it, and raised
    /// before, it kept the round that had just been run selected, so the
    /// next race was filed against it too.
    /// </param>
    /// <summary>
    /// Whether linking this race to that round would throw away a result
    /// that really happened - a DEMO landing on a round a real race has
    /// already run.
    ///
    /// THE ONE THING A DEMO NIGHT MUST NOT DO. MarkRecorded overwrites the
    /// link, so without this, demoing a heat that had already been raced
    /// for real would replace its result in the program with invented laps -
    /// and the real result, still in history, would no longer be attached
    /// to anything. Qualifying and the mains would then seed off the demo.
    ///
    /// A demo replacing ANOTHER demo is fine and is how re-running a
    /// rehearsal works. A real race replacing anything is fine too: that is
    /// a club re-running a heat, which is theirs to do.
    /// </summary>
    private static bool WouldOverwriteARealResult(
        SessionRecord record,
        RoundDefinition round,
        SessionHistoryStore history,
        Action<string> log)
    {
        if (!record.IsDemo || string.IsNullOrEmpty(round.LinkedSessionId))
        {
            return false;
        }

        // A link pointing at a session that is no longer there is not a real
        // result - it is a stale link, and ClearRecorded exists for it.
        if (history.Get(round.LinkedSessionId) is not { } already || already.IsDemoSession())
        {
            return false;
        }

        log($"[session] This demo was NOT tied to \"{round.RoundLabel}\" - that round has a real result "
            + "already, and a rehearsal must not replace one. Delete the real race from Race History first "
            + "if you meant to run the round again.");

        return true;
    }

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
        bool raceDayOpen = true,
        bool demoMayFillTheProgramme = false)
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
        // and qualifying/seeding can find its results.
        //
        // A DEMO ONLY WHEN THE CLUB HAS ASKED FOR ONE (13 Oct 2026). This
        // used to be a flat "never for a demo", which was right while a
        // demo was a single race in isolation - and it is exactly what made
        // a whole night impossible to rehearse, because heats that never
        // mark themselves run give qualifying nothing to read and mains
        // nothing to seed from. See AppSettings.DemoFillsTheProgramme.
        //
        // The old comment's worry - "it would mark a real heat as run with
        // invented results" - is answered by the guard below rather than by
        // refusing every demo: what must not happen is a demo replacing a
        // result that really happened.
        var demoMayLink = !record.IsDemo || demoMayFillTheProgramme;

        if (demoMayLink && round is not null && rounds is not null && !WouldOverwriteARealResult(record, round, history, log))
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
        else if (demoMayLink && !record.IsDemo && rounds is not null)
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
    /// <param name="jumpStarted">
    /// Whether race control marked a transponder as jumping the start -
    /// RaceControlSession.HasJumpStarted when filing a real race. Optional
    /// (22 Oct 2026, driver experience) so every other caller is unchanged
    /// and files nobody as a jump starter.
    /// </param>
    public static List<SessionResultEntry> ResultsFrom(
        IReadOnlyList<RacerStanding> racers, ProfileStore profiles, Func<string, bool>? jumpStarted = null) =>
        racers.Select((s, i) =>
        {
            var profile = profiles.Get(s.TransponderId);
            return new SessionResultEntry
            {
                Position = i + 1,
                Transponder = s.TransponderId,
                DriverName = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : s.TransponderId,
                // The car the transponder heard is fitted to, when its
                // driver has said; otherwise their active car.
                CarModel = profiles.CarModelFor(s.TransponderId),
                Laps = s.Laps,
                FastLapMs = s.FastestLapMs,
                ElapsedMs = s.ElapsedMs,
                Status = "active",
                LapTimesMs = s.LapTimesMs.ToList(),
                SectorTimesMs = SessionResultEntry.SectorsToSave(s),
                RejectedLaps = s.RejectedLaps,
                LapsLed = s.LapsLed,
                StartPosition = s.StartPosition,
                // 20 Oct 2026: the in-race penalty, so qualifying by best
                // consecutive laps can charge it (see SessionResultEntry).
                RacePenaltyMs = Math.Max(0, s.PenaltyMs),

                // 22 Oct 2026: laps race control counted by hand, so the
                // result can say so (HandCountedLaps).
                LapsByHand = HandCountedLaps.During(s),

                // 22 Oct 2026 (driver experience): kept so the driver can be
                // told their added seconds were for a jump start (PenaltyWords).
                JumpStarted = jumpStarted?.Invoke(s.TransponderId) ?? false,

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
