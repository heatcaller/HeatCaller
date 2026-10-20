namespace HeatCaller;

/// <summary>
/// How the COMMENTARY BOOTH sounds, as distinct from trackside.
///
/// WHY THIS EXISTS (18 Oct 2026)
///
/// The Team Principal's interview is written to be a different place from
/// the rest of the broadcast: the recap show's host has him sitting in the
/// booth, hours after the race, where the Technical Lead's word was down
/// in pit lane at the flag. The prompt says so, the segment is on air as
/// <c>OnAirParts.Booth</c>, and the model writes him measured and
/// unhurried.
///
/// And then it played in the recap's voice settings, which are the
/// punchiest in the app. SpeakRaceSummary works out one delivery up front
/// - "punchier delivery for the recap's own lines", style exaggeration
/// cranked off its default zero - and hands it to everything downstream,
/// the booth interview's questions included. So a host who had just been
/// told to settle into a chair asked his three questions in the voice he
/// calls a photo finish in.
///
/// That is the whole of what this file fixes, and it is worth being
/// precise about why it is the ONLY thing it fixes.
///
/// WHAT WAS ALREADY THERE, AND IS DELIBERATELY NOT REBUILT HERE
///
/// Three things a "booth treatment" would obviously want already exist,
/// and each of them would have been a bug to add a second time:
///
///   THE MUSIC BED. The booth interview runs inside the recap's show -
///   SpeakRaceSummary step 6 - and that show's bed is already running
///   under every part of it. ShowSegments says why a second one would be
///   wrong: "a second bed over" an open show is the fault, not the
///   feature.
///
///   THE DUCK. The recap raises a deep duck before it is queued and holds
///   it until the show's way out, so the music is ALREADY pulled down
///   across the interview rather than flapping per sentence.
///
///   THE TICK BOXES. PrincipalBeforeRace and PrincipalAfterRace are a
///   club's switches for this segment and have been since it shipped.
///
/// WHAT THIS APP CANNOT DO, said plainly so nobody looks for it here
///
/// A real booth treatment is acoustic: closer, drier, no crowd, a little
/// room tone. There is no such thing in HeatCaller. The audio path is
/// clips, levels and a voice - AudioBus and SmoothGainSampleProvider -
/// with no filtering, no reverb and nothing that could add or remove a
/// room. Writing one would be a DSP project, and a half-built one would
/// make a club's broadcast sound worse rather than closer.
///
/// So the lever this file uses is the one the app actually has: the voice
/// settings. Flatter and less performed IS the audible difference between
/// a man in a chair and a man at a trackside rail, and it costs nothing -
/// no extra render, no extra allowance, two numbers changed on a request
/// that was being made anyway.
/// </summary>
internal static class BoothTreatment
{
    /// <summary>
    /// How far the booth's stability is pulled towards perfectly flat,
    /// as a fraction of the distance left.
    ///
    /// Half, rather than all the way. Stability at 1 is not calm, it is
    /// inert - the same voice reading a shopping list - and a principal
    /// accounting for a night his team got wrong still has something at
    /// stake. Halfway is the difference being audible without the booth
    /// becoming an answering machine.
    /// </summary>
    internal const double TowardsFlat = 0.5;

    /// <summary>
    /// How much of the recap's style exaggeration the booth keeps.
    ///
    /// Half of it. Style is the setting that makes the recap sound like a
    /// broadcast rather than a reading, and the booth is still part of
    /// that broadcast - cutting it to nothing would make the interview
    /// sound like a different app had written it. Half keeps the show's
    /// character and takes the trackside push off it.
    /// </summary>
    internal const double KeepsOfStyle = 0.5;

    /// <summary>
    /// The booth's delivery, worked out from the recap's.
    ///
    /// NULL IN MEANS NULL OUT, and that is a decision rather than a
    /// fallthrough. A null delivery is a club that has NOT turned on
    /// SummaryUseBroadcastVoice - it wants its own voice profile used as
    /// configured, everywhere. Handing that club a booth setting it never
    /// asked for would be this file overriding a switch the club
    /// deliberately left off, in order to fix a problem that only exists
    /// when the switch is on. With no recap delivery there is no punchy
    /// delivery to take the punch off.
    ///
    /// Clamped to 0..1 at the end because the two constants above are
    /// applied to numbers that come from a club's own settings box, and a
    /// hand-edited settings file can hold anything.
    /// </summary>
    /// <param name="recap">
    /// What SpeakRaceSummary worked out for the recap's own lines -
    /// (SummaryVoiceStability, SummaryVoiceStyle), or null when the club
    /// is not using them.
    /// </param>
    internal static (double Stability, double Style)? Delivery((double Stability, double Style)? recap)
    {
        if (recap is not { } punchy)
        {
            return null;
        }

        var stability = punchy.Stability + ((1.0 - punchy.Stability) * TowardsFlat);
        var style = punchy.Style * KeepsOfStyle;

        return (Math.Clamp(stability, 0.0, 1.0), Math.Clamp(style, 0.0, 1.0));
    }

    /// <summary>
    /// How the console says what the booth is doing, or empty when there
    /// is nothing to say about it.
    ///
    /// Here because the change this file makes is INAUDIBLE AS A CAUSE: a
    /// club that thinks the booth sounds flat has no way to tell whether
    /// that is this calculation, their own voice profile, or the model
    /// writing short answers. One line in the console when it is applied
    /// is the difference between a setting and a mystery.
    /// </summary>
    internal static string Describe((double Stability, double Style)? recap)
    {
        if (Delivery(recap) is not { } booth)
        {
            return string.Empty;
        }

        return $"in the booth's own delivery - steadier and less performed than the recap "
               + $"(stability {booth.Stability:0.00}, style {booth.Style:0.00})";
    }
}
