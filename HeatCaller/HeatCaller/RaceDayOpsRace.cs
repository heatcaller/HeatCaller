namespace HeatCaller;

/// <summary>
/// The race-day manager's race-control ops: start a round, start the next
/// one, and the flags. On the track's Wi-Fi ONLY - the service refuses
/// every one of these that arrives through the relay, whatever the page
/// sent (see Run), because nobody should be able to start a race on a
/// track they cannot see.
///
/// Starting goes through MainForm.StartRoundNow, the same method the
/// Program tab and the Next Up bar now use; the desk's three questions
/// (no drivers? already run? start now?) and its "abandon the running
/// race?" become the phone's confirmation. Flags go through Race Control's
/// own Trigger methods, with the two checks that would otherwise open a
/// dialog on an unattended laptop - practice running, and staging over a
/// running race - made here first. So are the flags Race Control refuses
/// outright - a checkered with no race running, a green on the last lap -
/// since a refusal made after the flag has been sent never reaches the
/// phone.
/// </summary>
public sealed partial class RaceDayService
{
    private void RegisterRaceOps()
    {
        Register("race.startRound", OpKind.RaceControl, RaceStartRound);
        Register("race.startNext", OpKind.RaceControl, RaceStartNext);

        // Yellow and red are the two flags a marshal may need while the
        // laptop has a question open: they only slow or stop the cars, and
        // the flag remote has always sent them regardless. Everything else
        // waits for the laptop to be free.
        Register("race.flag", OpKind.RaceControl, RaceFlag, allowedWhileBusy: true);

        // Scoring by hand when the decoder is down (11 Nov 2026) - see
        // HandScoring. Wi-Fi only, like the flags: a lap counted from a
        // phone has to be counted by somebody who can see the track. Taken
        // while the laptop has a question open, as yellow and red are -
        // the cars don't stop for a dialog, and a lap is only a lap.
        Register("race.handCars", OpKind.RaceControl, RaceHandCars, allowedWhileBusy: true);
        Register("race.handScoring", OpKind.RaceControl, RaceHandScoring, allowedWhileBusy: true);
        Register("race.handLap", OpKind.RaceControl, RaceHandLap, allowedWhileBusy: true);

        // A car the decoder never saw before it died (11 Nov 2026) - see
        // RaceControlControl.HandAddCar. Wi-Fi only, like the taps.
        Register("race.handAdd", OpKind.RaceControl, RaceHandAdd, allowedWhileBusy: true);
    }

    /// <summary>
    /// The phone's hand-scoring grid: whether it is on (or on offer), and one
    /// entry per car in HandScoring's fixed order. Changes nothing.
    /// </summary>
    private OpResult RaceHandCars(OpContext context)
    {
        var control = _host.RaceControl;
        var race = _host.Race;

        return OpResult.Read(new
        {
            on = control.HandScoringOn,
            offer = HandScoring.ShouldOffer(control.TimingProblemKey?.Invoke(), race.State, race.IsPracticeMode, control.HandScoringOn),
            racing = race.State == RaceState.Racing && !race.IsPracticeMode,

            // Which race this is, so a phone's Undo last tap never reaches
            // back into the last race's taps (bug hunt 18).
            raceGeneration = race.SessionGeneration,
            cars = control.HandScoringCars()
                .Select(c => new
                {
                    id = DriverIdFor(c.Transponder),
                    name = c.Name,
                    laps = c.Laps,
                    lapsText = c.LapsText,
                })
                .ToList(),

            // The cars that can be put on the grid while it is on - this
            // heat's roster first (11 Nov 2026). Names as the grid's are.
            addable = control.HandScoringOn
                ? control.HandScoringAddable()
                    .Select(a => new { id = DriverIdFor(a.Transponder), name = a.Name, onRoster = a.OnRoster })
                    .ToList()
                : null,
        });
    }

    /// <summary>
    /// Puts a car the decoder never saw on the board - {driverId}, one of
    /// race.handCars' addable - through Race Control's HandAddCar, the
    /// desk's own path. Hand scoring has to be on, as for a tap.
    /// </summary>
    private OpResult RaceHandAdd(OpContext context)
    {
        var control = _host.RaceControl;

        if (!control.HandScoringOn)
        {
            throw new OpRefused("Hand scoring isn't on for this race - switch it on first.");
        }

        var addable = control.HandScoringAddable();
        var transponder = TransponderForDriverId(context.Text("driverId"), addable.Select(a => a.Transponder))
                          ?? throw new OpRefused("That car can't be added - it may be on the grid already. Look again.");

        if (control.HandAddCar(transponder, "a phone") is { } refusal)
        {
            throw new OpRefused(refusal);
        }

        var name = addable.First(a => string.Equals(a.Transponder, transponder, StringComparison.OrdinalIgnoreCase)).Name;

        return OpResult.Done("Car added", $"{name} is on the grid with no laps - tap them each time they cross.", new { id = DriverIdFor(transponder) });
    }

    /// <summary>Switches hand scoring on or off for the race running now - {on: true|false}.</summary>
    private OpResult RaceHandScoring(OpContext context)
    {
        var control = _host.RaceControl;
        var on = context.Bool("on") ?? throw new OpRefused("\"on\" has to be true or false.");

        if (!on)
        {
            if (!control.HandScoringOn)
            {
                return OpResult.Read(new { on = false });
            }

            control.StopHandScoring();
            return OpResult.Done("Hand scoring off", "Hand scoring is off - laps are counted from the loop again.", new { on = false });
        }

        if (control.HandScoringOn)
        {
            return OpResult.Read(new { on = true });
        }

        if (control.StartHandScoring("a phone") is { } refusal)
        {
            throw new OpRefused(refusal);
        }

        return OpResult.Done("Hand scoring on", "Scoring by hand - tap a car each time it crosses the line.", new { on = true });
    }

    /// <summary>
    /// One tap from the phone's grid - {driverId, delta: 1 or -1} - through
    /// Race Control's HandLap, the desk's own path. Hand scoring has to be
    /// on: a stray tap on a phone left on the Race tab must not add laps to
    /// a race the loop is timing.
    /// </summary>
    private OpResult RaceHandLap(OpContext context)
    {
        var control = _host.RaceControl;

        if (!control.HandScoringOn)
        {
            throw new OpRefused("Hand scoring isn't on for this race - switch it on first.");
        }

        var delta = context.Int("delta", -1, 1) ?? 1;

        if (delta == 0)
        {
            throw new OpRefused("\"delta\" has to be 1 or -1.");
        }

        var cars = control.HandScoringCars();
        var transponder = TransponderForDriverId(context.Text("driverId"), cars.Select(c => c.Transponder))
                          ?? throw new OpRefused("That car isn't on the board any more - look again.");

        if (control.HandLap(transponder, delta, "a phone") is { } refusal)
        {
            throw new OpRefused(refusal);
        }

        var car = control.HandScoringCars().FirstOrDefault(c => string.Equals(c.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        return OpResult.Read(new
        {
            id = DriverIdFor(transponder),
            laps = car?.Laps ?? 0,
            lapsText = car?.LapsText ?? string.Empty,
        });
    }

    private OpResult RaceStartRound(OpContext context) =>
        StartRoundFromPhone(RequireTodaysRound(context, "roundId"), context, "the program");

    /// <summary>
    /// The Next Up bar's Start Next: refused unless the session is idle or
    /// finished, as that button refuses.
    /// </summary>
    private OpResult RaceStartNext(OpContext context)
    {
        if (_host.Race.State is not (RaceState.Idle or RaceState.Finished))
        {
            throw new OpRefused("A session is already under way - finish or reset it first.");
        }

        var next = NextRoundToStart()
                   ?? throw new OpRefused("Nothing left to run - the program is complete.");

        // Named in the args so the confirmation is bound to THIS round: if
        // the next round changes before the yes, the yes is refused.
        if (context.Text("roundId") is { } expected && !string.Equals(expected, next.Id, StringComparison.Ordinal))
        {
            throw new OpRefused("The next round has changed - look again.");
        }

        return StartRoundFromPhone(next, context, "Start Next");
    }

    private OpResult StartRoundFromPhone(RoundDefinition round, OpContext context, string from)
    {
        if (_host.Race.IsPracticeMode)
        {
            throw new OpRefused("Practice is running, so a race can't be started yet - stop practice first on the Practice page.");
        }

        if (!context.Confirmed)
        {
            // The desk's three questions, as one.
            var question = new List<string>();

            if (_host.Race.State == RaceState.Racing)
            {
                question.Add(_host.Race.IsDemoMode
                    ? "A demo is running - starting this ends it."
                    : "A race is running - starting this ABANDONS it: its laps are cleared and it is not saved. (Undo on the laptop can put it back until the new race goes green.)");
            }

            if (round.AssignedTransponders.Count == 0)
            {
                question.Add($"\"{round.RoundLabel}\" has no drivers assigned.");
            }

            if (round.IsRecorded)
            {
                question.Add($"\"{round.RoundLabel}\" has already been run. Starting it again records a new session and won't replace the existing result.");
            }

            question.Add($"Start \"{round.RoundLabel}\" ({round.ClassName}) now? {Plural.Of(round.AssignedTransponders.Count, "driver")}. Staging and the countdown begin immediately.");

            return OpResult.Ask(string.Join("\r\n\r\n", question), new { roundId = round.Id });
        }

        var refusal = _host.StartRound(round, true, $"[phone] Starting \"{round.RoundLabel}\" from {from} on a phone.");

        if (refusal is not null)
        {
            throw new OpRefused(refusal);
        }

        return OpResult.Done($"Started {round.RoundLabel} ({round.ClassName})", $"\"{round.RoundLabel}\" is starting.", new { roundId = round.Id });
    }

    /// <summary>
    /// A flag, through Race Control's own Trigger methods. Posted to run
    /// after this answer rather than run inside it: a checkered flag saves
    /// the race, and what follows that on the laptop (the results card,
    /// badge pop-ups, "who was that unknown car?") must not hold the phone's
    /// answer hostage. So the answer means "sent", and says so - which is
    /// why every refusal Race Control would make is made here first, before
    /// the flag is sent. (The flag remote waits a few seconds for Race
    /// Control's own answer instead - see RaceControlControl.RunRemoteCommand.)
    /// </summary>
    private OpResult RaceFlag(OpContext context)
    {
        var flag = context.RequireText("flag").ToLowerInvariant();
        var race = _host.Race;

        if (flag is not ("stage" or "countdown" or "green" or "yellow" or "red" or "checkered" or "reset"))
        {
            throw new OpRefused("\"flag\" has to be stage, countdown, green, yellow, red, checkered or reset.");
        }

        // Everything but yellow and red waits while the laptop has a
        // question open - see RegisterRaceOps.
        if (flag is not ("yellow" or "red") && LaptopBusy())
        {
            throw new OpRefused(BusyMessage);
        }

        // Race Control refuses these during practice WITH A DIALOG on the
        // laptop; refused here instead, where the person asking can see why.
        //
        // Red too, now that Race Control refuses it in practice (a red in
        // practice used to stop every lap counting, with no green that could
        // clear it) - otherwise the phone read "RED sent" for a flag the
        // laptop had turned down with a dialog nobody near the phone saw.
        // And yellow, for the same reason - see PracticeRefusal.
        if (PracticeRefusal(flag, race.IsPracticeMode) is { } inPractice)
        {
            throw new OpRefused(inPractice);
        }

        // A checkered with no race running, and a green on the last lap, are
        // refused by Race Control - see its CheckeredRefusal and GreenRefusal.
        // It can only say so in its log once the flag runs, by which time this
        // answer has gone, and the phone would read "sent" for a flag that did
        // nothing. So they are refused here first, in the same words. (A
        // checkered used to go through from any state, and before the green it
        // saved whatever was left on the board as a new race.)
        var refusal = flag switch
        {
            "checkered" => RaceControlControl.CheckeredRefusal(race.State),
            "green" => RaceControlControl.GreenRefusal(race.State, race.Flag),
            _ => null,
        };

        if (refusal is not null)
        {
            throw new OpRefused(refusal);
        }

        // Which race the phone is looking at, when it says. Bound the same
        // way Start Next binds its round: a page that sends the race it was
        // shown gets a question - and a yes - about THAT race, and is told to
        // look again if another has taken its place. Optional, so a page
        // that does not send it is answered as before (the confirmation's
        // state stamp still covers it).
        var abandonsRace = flag is "stage" or "countdown" && race.State == RaceState.Racing;

        if (abandonsRace
            && context.Int("raceGeneration", 0, int.MaxValue) is { } shownRace
            && shownRace != race.SessionGeneration)
        {
            throw new OpRefused("The race on track has changed since you looked - look again before abandoning it.");
        }

        if (!context.Confirmed)
        {
            if (abandonsRace)
            {
                return OpResult.Ask("A race is running. Staging now abandons it: every lap it has counted is cleared and it is not saved. "
                                    + "To end it and keep the result, throw the checkered flag instead.\r\n\r\nAbandon the running race?",
                    new { raceGeneration = race.SessionGeneration });
            }

            if (flag == "reset" && (race.State != RaceState.Idle || race.GetOrderedStandings().Count > 0))
            {
                return OpResult.Ask("Reset the race? The board is cleared. Undo on the laptop can put it back.");
            }
        }

        var control = _host.RaceControl;
        var confirmed = context.Confirmed;

        // The race the yes was given about. The flag runs a moment after this
        // answer, on the laptop's own thread, and by then a different race
        // may be on track - one nobody agreed to abandon. So the yes only
        // carries over while it is still the same race; otherwise Race
        // Control asks again itself, as it would for a key press.
        var confirmedRace = race.SessionGeneration;
        bool StillConfirmed() => confirmed && race.SessionGeneration == confirmedRace;

        Action trigger = flag switch
        {
            // The yes was given on the phone - passed on so the laptop does
            // not put the same question to nobody.
            // Through On Air when it is the normal start (17 Oct 2026) -
            // see RaceControlControl.TriggerStart.
            "stage" => () => control.TriggerStart(confirmedOverRunningRace: StillConfirmed()),
            "countdown" => () => control.TriggerCountdown(confirmedOverRunningRace: StillConfirmed()),
            "green" => control.TriggerGreen,
            "yellow" => control.TriggerYellow,
            "red" => control.TriggerRed,
            "checkered" => control.TriggerCheckered,
            _ => control.TriggerReset,
        };

        try
        {
            _host.UiThread.BeginInvoke(trigger);
        }
        catch (InvalidOperationException)
        {
            throw new OpRefused("The laptop is closing - the flag was NOT sent.");
        }

        return OpResult.Done($"Sent {flag.ToUpperInvariant()}", $"{flag.ToUpperInvariant()} sent.", new { flag, sent = true });
    }

    /// <summary>
    /// The phone's answer to a flag Race Control would refuse because
    /// practice is running, or null when it isn't refused for that.
    ///
    /// Yellow is refused as red is. It used to be sent: a yellow in practice
    /// can't be cleared (Green is refused while practice runs), and the
    /// announcer held every lap call under it until practice was stopped.
    /// </summary>
    internal static string? PracticeRefusal(string flag, bool practiceRunning) =>
        practiceRunning && flag is "stage" or "countdown" or "green" or "yellow" or "checkered" or "reset" or "red"
            ? "Practice is running - stop it on the Practice page first."
            : null;
}
