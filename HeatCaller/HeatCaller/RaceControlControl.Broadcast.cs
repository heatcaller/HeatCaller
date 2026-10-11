namespace HeatCaller;

/// <summary>
/// The race director's hand on the broadcast: playing the last segment
/// again, and stopping the one on air.
///
/// WHY THESE EXIST
///
/// Asked for on 11 Oct 2026, and the Hold button on the announcer strip is
/// the reason it was obvious: Hold had been the only thing on the busiest
/// screen in the app that could do anything about the show, and it only
/// stops the NEXT thing. Everything already said was final, and everything
/// in progress ran to its end whatever happened in the room.
///
/// A race night has both. A recap talked over by forty people cheering, a
/// PA that cut out halfway, a club owner who missed the honours - those
/// want the line back. A segment that started as somebody picked up the
/// microphone, or over a crash being marshalled, wants stopping. Neither
/// was reachable without the developer console open.
///
/// WHY THEY ARE HERE AND NOT ON THE ANNOUNCER SCREEN
///
/// The announcer screen is where the show is RUN FROM, and it is on a
/// different monitor - often facing the other way, often not open at all.
/// Race Control is where the person with the microphone is looking. That is
/// the same argument AnnouncerStrip makes for itself at length, and these
/// are its buttons.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>
    /// What a replay calls itself while it holds the broadcast floor - see
    /// <see cref="ShowFloor"/>.
    /// </summary>
    private const string PlayingItAgain = "a segment being played again";

    /// <summary>
    /// Puts the last segment out again, with its own show round it where
    /// the club scores that segment.
    ///
    /// WHAT IS REPLAYED IS THE WORDS, NOT THE WRITING. Nothing goes to the
    /// model and nothing is spent: the line is the one the director already
    /// holds in LastSaid, so this costs a club nothing and works with the
    /// AI switched off at the wall, which is exactly when somebody is most
    /// likely to want it.
    ///
    /// NOT BUDGETED AND NOT REMEMBERED, deliberately. The budget is there
    /// to stop the ROOM hearing too much, and a line the room asked to hear
    /// again is not the budget's business; and the rotations that keep a
    /// phrasing from being used twice in a night have already counted this
    /// one. Playing it again is the race director's decision, not the
    /// director's.
    ///
    /// THROUGH THE QUEUE, like everything else. A test line that bypassed
    /// the voice queue would never raise SpeakingChanged, so the bed would
    /// not duck under it - the same trap TestShowMusicAsync's own note
    /// warns about.
    /// </summary>
    private async Task ReplayLastSegmentAsync()
    {
        if (Commentary?.Invoke() is not { } director || director.LastSaid is not { } last)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Nothing to play again - the announcer has not said a segment yet tonight.");

            return;
        }

        if (_announcer.Muted)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Not playing it again - the announcer is muted. Unmute it on the Mixer.");

            return;
        }

        var named = ShowBlock.CaptionFor(last.Kind) ?? CommentaryBudget.Spec(last.Kind).Title;

        // The floor, so the announcer screen's director does not start a
        // segment of its own over the top of this one - the same claim the
        // recap sequence takes, and the same reason. Not failed on: a
        // replay asked for while the programme is running is the race
        // director overruling it, and the line below says so.
        var claimed = ShowFloor.TryClaim(PlayingItAgain);

        if (!claimed)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Playing {named} again over {ShowFloor.HeldBy ?? "what is already on"} - asked for from Race Control.");
        }

        try
        {
            // AN EXCHANGE GOES OUT AS AN EXCHANGE. LastSaid.Text is the
            // FLATTENED form, which carries "Eric: " in front of the
            // co-host's half for the Announcer screen's feed to show - and
            // a flattened exchange read over a PA reads the names out loud.
            // So the turns are spoken from the turns, in their own voices,
            // exactly as SpeakTurnPlainly does it.
            void Speak()
            {
                if (last.IsExchange)
                {
                    for (var at = 0; at < last.Turns.Count; at++)
                    {
                        var turn = last.Turns[at];

                        // Padded the way a live exchange is - trimmed for a
                        // turn with another hard against it. A replay that
                        // left a second of air where the live one has a beat
                        // would not be the same segment. See
                        // CommentaryExchange.ExchangeRun.
                        var run = CommentaryExchange.ExchangeRun.At(at, last.Turns.Count);

                        _announcer.SpeakReminder(
                            turn.Line,
                            null,
                            turn.FromCoHost ? _settings.CoHostVoice : null,
                            showOnScreens: true,
                            tail: run.AnotherFollows
                                ? AnnouncementService.SpeechTail.BackToBack
                                : AnnouncementService.SpeechTail.Normal,
                            lead: run.FollowsAnother
                                ? AnnouncementService.SpeechLead.FollowsOn
                                : AnnouncementService.SpeechLead.Normal);
                    }

                    return;
                }

                _announcer.SpeakReminder(last.Text, showOnScreens: true);
            }

            var treatment = ShowMusic?.Invoke();

            // RunAsync answers false when there was no music to put round
            // it - and in that case it has NOT spoken, so the caller must.
            if (treatment is not null
                && treatment.Treats(last.Kind)
                && await treatment.RunAsync(Speak, last.Kind).ConfigureAwait(true))
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [show] {named} played again, with its show round it - asked for from Race Control.");

                return;
            }

            Speak();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] {named} played again - asked for from Race Control.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't play {named} again: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (claimed)
            {
                ShowFloor.Release(PlayingItAgain);
            }
        }
    }

    /// <summary>
    /// Stops the show that is on air: the bed is ridden down and whatever
    /// the running order had left is dropped.
    ///
    /// WHAT THIS DOES NOT DO, and it is worth being plain about because the
    /// button says Skip. The line the voice is PART WAY THROUGH is not cut
    /// off. Cutting it would mean clearing the voice queue, and the queue
    /// is where the checkered flag, the countdown, the green and a driver's
    /// release call all sit - every one of them marked priority precisely
    /// so that colour commentary cannot bin them. A Skip button that could
    /// swallow a release call and leave a car sitting on the line is worse
    /// than no Skip button, so this stops the SHOW and lets the sentence
    /// finish.
    ///
    /// In the hall that is the right behaviour anyway: a segment stopped
    /// mid-word sounds like a fault, where one that finishes its sentence
    /// and then hands back sounds like a programme moving on.
    ///
    /// Safe when nothing is on air - AbandonBlock does nothing then - and
    /// safe twice.
    /// </summary>
    private void SkipWhatIsOnAir()
    {
        if (ShowMusic?.Invoke() is not { } treatment)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Nothing to skip - this PC has no show music set up.");

            return;
        }

        try
        {
            treatment.AbandonBlock();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [show] Show stopped from Race Control - the bed rides down and the rest of the "
                + "running order is dropped. The line being spoken finishes.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Couldn't stop the show: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
