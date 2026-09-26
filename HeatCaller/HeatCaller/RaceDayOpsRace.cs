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
        if (race.IsPracticeMode && flag is "stage" or "countdown" or "green" or "checkered" or "reset")
        {
            throw new OpRefused("Practice is running - stop it on the Practice page first.");
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
            "stage" => () => control.TriggerStage(confirmedOverRunningRace: StillConfirmed()),
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
}
