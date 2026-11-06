using System.Text;

namespace HeatCaller;

/// <summary>
/// The race-day manager's results ops: recent results, one result in full,
/// sending one to the online results page again, and - only when the club
/// has switched it on - correcting a saved result. See raceday-protocol.md.
///
/// Corrections go through ResultEditor, the same arithmetic as the laptop's
/// Edit Result dialog, and are saved with SessionHistoryStore.SaveEdited, so
/// the online results and standings are re-sent the same way whichever of
/// the two made the change.
/// </summary>
public sealed partial class RaceDayService
{
    private void RegisterResultOps()
    {
        Register("results.list", OpKind.Read, ResultsList);
        Register("results.get", OpKind.Read, ResultsGet);
        Register("results.publish", OpKind.Change, ResultsPublish);
        Register("result.adjust", OpKind.Change, ResultAdjust);
    }

    /// <summary>What turning corrections on is called on the laptop, for the refusal when it is off.</summary>
    private const string EditingOff =
        "Correcting saved results is switched off on the laptop. Turn on \"Allow corrections to saved results\" on the Local Screens page to use it.";

    private object SessionRow(SessionRecord session, RaceDayChannel channel)
    {
        var round = session.RoundId is null ? null : _host.Rounds.Get(session.RoundId);
        var winner = session.Results.OrderBy(r => r.Position).FirstOrDefault();

        return new
        {
            id = session.Id,
            label = session.Label,
            type = session.SessionType == RaceSessionType.Race ? "race" : "practice",
            demo = session.IsDemoSession(),
            imported = session.IsImported(),
            startedUtc = session.StartedAtUtc,
            endedUtc = session.EndedAtUtc,
            track = session.TrackLayoutName,
            roundId = round?.Id,
            roundLabel = round?.RoundLabel,
            className = round?.ClassName,
            drivers = session.Results.Count,
            // Channel-aware: an unnamed winner's transponder went out in
            // full through the relay here (see ShownName).
            winner = winner is null ? null : ShownName(winner.DriverName, winner.Transponder, channel),
            corrected = session.Edits.Count > 0,
            editable = CanCorrect(session) is null,
        };
    }

    /// <summary>Why this session's result cannot be corrected from here, or null when it can.</summary>
    private string? CanCorrect(SessionRecord session)
    {
        if (!_host.Settings.AllowResultEditing)
        {
            return EditingOff;
        }

        if (session.SessionType != RaceSessionType.Race || session.IsNotScored())
        {
            return "Only a real race's result can be corrected - not practice, a demo race or one pasted in from another program.";
        }

        return null;
    }

    private object ResultRow(SessionResultEntry entry, RaceDayChannel channel) => new
    {
        position = entry.Position,
        id = DriverIdFor(entry.Transponder),
        // ShownName, not a blank check: an unnamed car's result is saved
        // with the transponder AS its name (RaceFiling), which is not blank,
        // so the whole number went through the relay beside a transponder
        // column careful to show only its last three characters.
        name = ShownName(entry.DriverName, entry.Transponder, channel),
        car = entry.CarModel,
        laps = entry.Laps,
        fastLapMs = entry.FastLapMs,
        totalMs = entry.ElapsedMs,

        // "active" is this app's word for an ordinary finisher, not news.
        status = string.Equals(entry.Status, "active", StringComparison.OrdinalIgnoreCase) ? string.Empty : entry.Status,
        outcome = entry.Outcome,
        addedLaps = entry.AddedLaps,
        timePenaltyMs = entry.TimePenaltyMs,
        lapPenalty = entry.LapPenalty,
        corrected = entry.IsCorrected(),
        asRaced = entry.AsRaced is { } raced
            ? new { position = raced.Position, laps = raced.Laps, totalMs = raced.ElapsedMs }
            : null,
    };

    /// <summary>
    /// Recent saved sessions, newest first. Races only unless practice is
    /// asked for; today's only when asked - the race day's rounds when one
    /// is open, otherwise the last eighteen hours (the same "tonight" the
    /// Move Drivers form uses without a race day).
    /// </summary>
    private OpResult ResultsList(OpContext context)
    {
        var limit = context.Int("limit", 1, 200) ?? 30;
        var todayOnly = context.Bool("todayOnly") ?? false;
        var includePractice = context.Bool("includePractice") ?? false;
        var roundId = context.Text("roundId");

        var day = _host.Events.Active is { IsLegacy: false } active ? active : null;
        var dayRounds = day?.RoundIds.ToHashSet(StringComparer.Ordinal);
        var since = DateTime.UtcNow - TimeSpan.FromHours(18);

        var sessions = _host.History.All
            .Where(s => includePractice || s.SessionType == RaceSessionType.Race)
            .Where(s => roundId is null || string.Equals(s.RoundId, roundId, StringComparison.Ordinal))
            .Where(s => !todayOnly
                        || (dayRounds is not null
                            ? s.RoundId is not null && dayRounds.Contains(s.RoundId)
                            : s.StartedAtUtc >= since))
            .ToList();

        return OpResult.Read(new
        {
            total = sessions.Count,
            allowResultEditing = _host.Settings.AllowResultEditing,
            sessions = sessions.Take(limit).Select(s => SessionRow(s, context.Channel)).ToList(),
        });
    }

    private SessionRecord RequireSession(OpContext context) =>
        _host.History.Get(context.RequireText("sessionId"))
        ?? throw new OpRefused("That result isn't on the laptop any more - reload the list.");

    private OpResult ResultsGet(OpContext context)
    {
        var session = RequireSession(context);

        return OpResult.Read(new
        {
            session = SessionRow(session, context.Channel),
            results = session.Results.OrderBy(r => r.Position).Select(r => ResultRow(r, context.Channel)).ToList(),
            edits = session.Edits.Select(e => new
            {
                atUtc = e.AtUtc,

                // The edit log keeps the name ResultEditor gave it, which for
                // an unnamed car is the whole transponder. See ResultRow.
                name = ShownName(e.DriverName, e.Transponder, context.Channel),
                change = e.Change,
                reason = e.Reason,
                source = e.Source,
            }).ToList(),
            canEdit = CanCorrect(session) is null,
            editBlockedReason = CanCorrect(session),
        });
    }

    /// <summary>
    /// Sends one result, and the championship standings, to the online
    /// relay again. Publishing is otherwise automatic - on the flag, and
    /// again after a correction - so this is for "the page doesn't show it":
    /// a relay that was asleep, or a result from before the relay was set up.
    /// The track's Wi-Fi pages read the laptop directly and never need it.
    /// </summary>
    private OpResult ResultsPublish(OpContext context)
    {
        var session = RequireSession(context);

        if (session.IsImported())
        {
            throw new OpRefused("A race pasted in from another program is never published - it is kept on the laptop for the record.");
        }

        if (session.IsDemoSession())
        {
            throw new OpRefused("A demo race is never published - it is a rehearsal, not a result.");
        }

        if (!_host.Settings.RelayEnabled)
        {
            throw new OpRefused("The online relay isn't switched on on the laptop (Club Website page), so there is nowhere to publish to. "
                                + "The results on the track's Wi-Fi pages are always up to date.");
        }

        // 2026-11-05, Bug hunt 11: a race whose filing failed (race-history.json
        // locked, SessionHistoryStore.Add returned false) is still in History.All,
        // so it shows on /manage - but nothing publishes a race that is not on
        // disk (SessionHistoryStore.Saved / SavedView). This op was the one way
        // round that rule, leaving a result on the relay the laptop could forget.
        if (_host.History.IsUnsaved(session))
        {
            throw new OpRefused("This race isn't saved to the race history yet (the laptop couldn't write the file) - "
                                + "it can't be published until it is. Save it on the laptop first.");
        }

        _host.RepublishSession(session);

        return OpResult.Done($"Re-sent \"{session.Label}\" to the online results", "Sent to the online results page.");
    }

    /// <summary>
    /// Corrects a saved race result. Each change names a driver and gives
    /// what their correction should be IN TOTAL (see ResultAdjustment) -
    /// anything left out stays as it is now. Always confirmed: the first
    /// call answers with the new order, the second saves it.
    /// </summary>
    private OpResult ResultAdjust(OpContext context)
    {
        var session = RequireSession(context);

        if (CanCorrect(session) is { } blocked)
        {
            throw new OpRefused(blocked);
        }

        var reason = context.Text("reason")?.Trim() ?? string.Empty;

        if (reason.Length == 0)
        {
            throw new OpRefused("Give a reason for the correction - it is kept with the result.");
        }

        if (reason.Length > 200)
        {
            throw new OpRefused("That reason is too long - 200 characters at most.");
        }

        var adjustments = ReadAdjustments(context, session);

        // Named as this phone may see them - the refusal goes back to it.
        if (ResultEditor.Validate(session, adjustments, e => ShownName(e.DriverName, e.Transponder, context.Channel)) is { } invalid)
        {
            throw new OpRefused(invalid);
        }

        var changes = ResultEditor.DescribeChanges(session, adjustments);

        // Said, and as a refusal. It used to answer ok with nothing to say,
        // and the phone showed "Done" - to a steward who had typed 1 for one
        // more lap on a driver already given one, believing it was added.
        if (changes.Count == 0)
        {
            throw new OpRefused("Nothing to change - the result already has those values. Each one is the driver's correction "
                                + "in total, not an amount to add on.");
        }

        if (!context.Confirmed)
        {
            var preview = ResultEditor.Preview(session, adjustments);

            return OpResult.Ask(CorrectionQuestion(session, changes, preview, context.Channel), new
            {
                preview = preview.Select(r => ResultRow(r, context.Channel)).ToList(),
            });
        }

        var snapshot = ResultEditor.Snapshot(session);

        var count = ResultEditor.Apply(session, adjustments, reason, "Phone");

        // The answer below tells the driver this correction is what the
        // championship uses from now on, and SaveEdited republishes it to the
        // results page and the standings. Both would be lies if the write did
        // not land - and the next launch, reloading the uncorrected result,
        // would silently reverse a decision already announced.
        if (!_host.History.SaveEdited(session))
        {
            ResultEditor.Restore(session, snapshot);

            return OpResult.Refuse(
                "That correction could not be saved - something on the race laptop has race-history.json open, "
                + "or its disk is full. Nothing has been changed. Try again in a moment.");
        }

        // Only once the correction is on disk: an undo step left behind by a
        // save that failed would spend the laptop's next Undo on a change
        // that never happened (see ResultEditor.Apply).
        var correctedId = session.Id;

        _host.Undo.Capture($"Phone: Correct the result of '{session.Label}'", () =>
        {
            // As the laptop's own correction undo (RaceHistoryControl): not
            // onto a race deleted since, and not reported as done when the
            // old result could not be written - memory and disk would then
            // disagree until the next restart quietly brought the correction
            // back.
            if (!ReferenceEquals(_host.History.Get(correctedId), session))
            {
                throw new InvalidOperationException($"\"{session.Label}\" has been deleted since. Nothing was changed.");
            }

            var corrected = ResultEditor.Snapshot(session);
            ResultEditor.Restore(session, snapshot);

            if (!_host.History.SaveEdited(session))
            {
                ResultEditor.Restore(session, corrected);
                _host.RefreshDesk();
                throw new InvalidOperationException(
                    $"undoing the correction to \"{session.Label}\" could not be written to the race history - something else "
                    + "on this PC has the file open, or the disk is full. The corrected result is still in place.");
            }

            _host.RefreshDesk();
        });

        return OpResult.Done(
            $"Corrected \"{session.Label}\" ({Plural.Of(count, "driver")}): {reason}",
            "Saved. The corrected result is what qualifying, the mains, the championship and the results pages use from now on.",
            new { results = session.Results.OrderBy(r => r.Position).Select(r => ResultRow(r, context.Channel)).ToList() });
    }

    /// <summary>The "changes" list, read into adjustments. A field left out keeps the driver's current correction.</summary>
    private List<ResultAdjustment> ReadAdjustments(OpContext context, SessionRecord session)
    {
        if (!context.Has("changes") || context.Args.GetProperty("changes").ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new OpRefused("\"changes\" has to be a list of drivers to correct.");
        }

        var adjustments = new List<ResultAdjustment>();

        foreach (var item in context.Args.GetProperty("changes").EnumerateArray())
        {
            if (adjustments.Count >= 100)
            {
                throw new OpRefused("Too many changes in one go.");
            }

            if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                throw new OpRefused("Each change has to be an object.");
            }

            adjustments.Add(ReadAdjustment(new OpContext(item, context.Channel), session));
        }

        return adjustments;
    }

    /// <summary>
    /// One driver's change. The driver is found among the result's own
    /// rows, so a car nobody named can be corrected too. Missing fields keep
    /// the driver's current correction; "outcome": "" clears a DNF or DQ.
    /// </summary>
    private ResultAdjustment ReadAdjustment(OpContext change, SessionRecord session)
    {
        var transponder = TransponderForDriverId(change.Text("driverId"), session.Results.Select(r => r.Transponder))
                          ?? throw new OpRefused("One of those drivers isn't in this result - reload it.");

        var entry = session.Results.First(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        var now = ResultEditor.CurrentOf(entry);

        var outcome = now.Outcome;

        if (change.Has("outcome"))
        {
            outcome = ResultEditor.NormaliseOutcome(change.Text("outcome"))
                      ?? throw new OpRefused("\"outcome\" has to be \"\" (finished), \"DNF\" or \"DQ\".");
        }

        var seconds = change.Number("timePenaltySeconds", 0, ResultEditor.MaxTimePenaltyMs / 1000.0);

        return new ResultAdjustment(
            entry.Transponder,
            change.Int("addedLaps", -ResultEditor.MaxLapChange, ResultEditor.MaxLapChange) ?? now.AddedLaps,
            seconds is { } s ? (long)Math.Round(s * 1000) : now.TimePenaltyMs,
            change.Int("lapPenalty", 0, ResultEditor.MaxLapChange) ?? now.LapPenalty,
            outcome);
    }

    /// <summary>What the phone is asked before a correction is saved: the changes, the new order, and what a correction does not do.</summary>
    private string CorrectionQuestion(
        SessionRecord session,
        List<(SessionResultEntry Entry, string Change)> changes,
        List<SessionResultEntry> preview,
        RaceDayChannel channel)
    {
        var text = new StringBuilder();

        text.Append($"Correct \"{session.Label}\"?\r\n\r\n");

        // Names as the phone on this channel may see them: an unnamed car's
        // whole transponder is not sent through the relay (see ShownName).
        foreach (var (entry, change) in changes)
        {
            text.Append($"   {ShownName(entry.DriverName, entry.Transponder, channel)}: {change}\r\n");
        }

        text.Append("\r\nNew order:\r\n");

        foreach (var row in preview)
        {
            var was = session.Results.FirstOrDefault(r => string.Equals(r.Transponder, row.Transponder, StringComparison.OrdinalIgnoreCase))?.Position;
            var moved = was is { } before && before != row.Position ? $"  (was P{before})" : string.Empty;
            var outcome = string.IsNullOrEmpty(row.Outcome) ? string.Empty : $"  {row.Outcome}";

            text.Append($"   P{row.Position}  {ShownName(row.DriverName, row.Transponder, channel)}  {Plural.Of(row.Laps, "lap")}{outcome}{moved}\r\n");
        }

        if (session.RoundId is not null && _host.Rounds.Get(session.RoundId) is { Kind: RoundKind.Qualifier } round
            && TodaysRoundsForClass(round.ClassName).Any(r => r.Kind == RoundKind.Main))
        {
            text.Append($"\r\nThe mains for {round.ClassName} have already been seeded. They are not changed by this - re-seed them if this changes who belongs in which main.");
        }

        text.Append("\r\n\r\nBadges already earned for this race are not recalculated.");

        return text.ToString();
    }
}
