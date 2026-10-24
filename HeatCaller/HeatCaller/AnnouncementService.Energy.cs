namespace HeatCaller;

/// <summary>
/// How much life the voice puts into a line, by how big the race is
/// (roadmap, "Sounding like a broadcast", item 13 - built 22 Oct 2026).
///
/// WHY
///
/// <see cref="RaceStature"/> already gives an evening its shape in HOW MUCH
/// is said - three lines in ten minutes in practice, nine in the top main.
/// What it did not touch was HOW it is said: the heat-three lap call and
/// the last-lap pass for the A-main came out of ElevenLabs with exactly the
/// same stability and style. A commentator does not sound the same in the
/// first qualifier and the final. So the delivery steps with the stature
/// as well: a little steadier below the mains, a little livelier in the top
/// main and the last race of the night.
///
/// THREE STEPS, NOT A DIAL
///
/// The saved-voice library keys a take on the voice settings it was spoken
/// with (VoiceLibrary.KeyFor), so every distinct setting is a distinct take
/// to pay for. Three fixed steps - and the middle one is the club's own
/// settings, untouched - bound that to at most three takes of any line,
/// and in practice far fewer: see <see cref="AnnouncementService.WithTheRacesEnergy"/>,
/// which plays any take of the line that already exists before it buys a
/// new one.
///
/// SMALL, AND ROUND THE CLUB'S OWN SETTINGS
///
/// One tenth either way on stability and on style. The club chose its
/// voice and tuned it on Voice Settings; this leans on that tuning, it does
/// not replace it. A step never takes stability below 0.30 or style above
/// 0.50 unless the club was already there - the two places ElevenLabs
/// starts to wander off the voice - and never moves the other way past what
/// the club set: a steadier step never makes a voice flatter than 0.85.
///
/// WHAT IT LEAVES ALONE
///
/// Kokoro, which has no such settings. The HeatCaller voice, whose settings
/// are the relay's own tuning of each voice and whose takes are shared
/// between clubs - a per-race variant there would split that shared cache
/// and charge every club for it. ElevenLabs v3 models, which take stability
/// as one of three fixed presets rather than a number and get their
/// expression from the audio tags the writer already puts in. And any line
/// with a delivery of its own (the race summary's), a speed-only change
/// apart, which already says how it should sound.
/// </summary>
public enum DeliveryEnergy
{
    /// <summary>A touch steadier than the club's settings: practice and the heats.</summary>
    Steady,

    /// <summary>The club's own settings, exactly as saved.</summary>
    Normal,

    /// <summary>A touch livelier: the top main, and the last race of the night.</summary>
    Lively,
}

/// <summary>The arithmetic behind <see cref="DeliveryEnergy"/> - pure, so it is tested on its own.</summary>
internal static class DeliveryEnergies
{
    /// <summary>How far one step moves stability and style.</summary>
    internal const double Step = 0.10;

    /// <summary>A livelier step never takes stability below this - unless the club's own setting already is.</summary>
    internal const double LeastStability = 0.30;

    /// <summary>A steadier step never takes stability above this - unless the club's own setting already is.</summary>
    internal const double MostStability = 0.85;

    /// <summary>A livelier step never takes style above this - unless the club's own setting already is.</summary>
    internal const double MostStyle = 0.50;

    /// <summary>
    /// The step for a race of this stature.
    ///
    /// Unknown - a club with no programme - is <see cref="DeliveryEnergy.Normal"/>,
    /// for the reason RaceStature.Unknown gives: a club that never told the
    /// app anything keeps exactly the voice it had. The last race of the
    /// night goes up one step, as it gets two more lines of airtime: the
    /// room has stayed for it, whatever it is.
    /// </summary>
    internal static DeliveryEnergy For(RaceStature stature)
    {
        var energy = stature.Billing switch
        {
            RaceBilling.Practice => DeliveryEnergy.Steady,
            RaceBilling.Heat => DeliveryEnergy.Steady,
            RaceBilling.LesserMain => DeliveryEnergy.Normal,
            RaceBilling.TopMain => DeliveryEnergy.Lively,
            _ => DeliveryEnergy.Normal,
        };

        if (stature.Billing != RaceBilling.Unknown && stature.LastOfTheNight)
        {
            energy = energy == DeliveryEnergy.Steady ? DeliveryEnergy.Normal : DeliveryEnergy.Lively;
        }

        return energy;
    }

    /// <summary>
    /// The club's stability and style moved one step. Rounded to two places
    /// so the same step always gives the same numbers - and so the same
    /// saved take.
    /// </summary>
    internal static (double Stability, double Style) Apply(double stability, double style, DeliveryEnergy energy)
    {
        stability = Math.Clamp(stability, 0, 1);
        style = Math.Clamp(style, 0, 1);

        var (s, t) = energy switch
        {
            DeliveryEnergy.Lively => (
                Math.Max(stability - Step, Math.Min(stability, LeastStability)),
                Math.Min(style + Step, Math.Max(style, MostStyle))),

            DeliveryEnergy.Steady => (
                Math.Min(stability + Step, Math.Max(stability, MostStability)),
                Math.Max(style - Step, 0)),

            _ => (stability, style),
        };

        return (Math.Round(s, 2), Math.Round(t, 2));
    }

    /// <summary>Whether this voice is one the step applies to - see the remarks on <see cref="DeliveryEnergy"/>.</summary>
    internal static bool AppliesTo(VoiceProfile profile) =>
        profile.EffectiveEngine == VoiceEngine.ElevenLabs
        && !profile.ModelId.Contains("v3", StringComparison.OrdinalIgnoreCase);

    /// <summary>The other two steps, for looking up a take already saved.</summary>
    internal static DeliveryEnergy[] Others(DeliveryEnergy energy) =>
        Enum.GetValues<DeliveryEnergy>().Where(other => other != energy).ToArray();
}

public sealed partial class AnnouncementService
{
    /// <summary>
    /// Whether the delivery follows the race (<see cref="DeliveryEnergy"/>).
    /// Null - a host that never wires the setting - is on.
    /// </summary>
    public Func<bool>? EnergyFollowsTheRace { get; set; }

    /// <summary>The last step the console was told about, so it hears about a change rather than every line.</summary>
    private int _energyLogged = -1;

    /// <summary>
    /// The voice for one routine line, with the race's energy in it.
    ///
    /// ANY TAKE THAT EXISTS IS PLAYED BEFORE ONE IS BOUGHT. In order: a take
    /// already saved at this race's step; the take at the club's own
    /// settings - which is what Prepare Lines saves and what every line
    /// before this feature was saved as, so a prepared line is still found
    /// and still free; then a take at either other step. Only a line with no
    /// take at all is rendered, and then at this race's step. So the energy
    /// never makes the club pay for a line twice: a phrase the club has
    /// already bought is heard as it was bought, and the difference is heard
    /// in the lines that are new tonight - which, with drivers' names and
    /// times in them, is most of what a race says.
    /// </summary>
    /// <param name="profile">The voice as it would otherwise speak the line.</param>
    /// <param name="text">The line as it goes to RenderLineAsync - shortened here the same way to look its take up.</param>
    private VoiceProfile WithTheRacesEnergy(VoiceProfile profile, string text)
    {
        if (!DeliveryEnergies.AppliesTo(profile) || EnergyFollowsTheRace?.Invoke() == false)
        {
            return profile;
        }

        DeliveryEnergy energy;

        try
        {
            // Read off the club's programme, which can be being edited -
            // see StatureProvider. A read that fails is a Normal line, not
            // a lost one.
            energy = DeliveryEnergies.For(Stature);
        }
        catch (Exception)
        {
            return profile;
        }

        if (energy == DeliveryEnergy.Normal)
        {
            return profile;
        }

        try
        {
            var saved = ForCloudVoice(text);
            var stepped = Stepped(profile, energy);

            if (VoiceLibrary.Shared.Contains(stepped, saved))
            {
                return stepped;
            }

            if (VoiceLibrary.Shared.Contains(profile, saved))
            {
                return profile;
            }

            foreach (var other in DeliveryEnergies.Others(energy).Where(other => other != DeliveryEnergy.Normal))
            {
                var otherStep = Stepped(profile, other);

                if (VoiceLibrary.Shared.Contains(otherStep, saved))
                {
                    return otherStep;
                }
            }

            if (Interlocked.Exchange(ref _energyLogged, (int)energy) != (int)energy)
            {
                StatusMessage?.Invoke(energy == DeliveryEnergy.Lively
                    ? $"[voice] A livelier delivery for {StatureForTheLog()} (Voice Settings' own settings, one step up)."
                    : $"[voice] A steadier delivery for {StatureForTheLog()} - the voice is saved for the mains.");
            }

            return stepped;
        }
        catch (Exception)
        {
            // The library could not be asked - a locked folder. The club's
            // own settings, which is what the line would always have had.
            return profile;
        }
    }

    private string StatureForTheLog()
    {
        try
        {
            return Stature.Describe;
        }
        catch (Exception)
        {
            return "this race";
        }
    }

    private static VoiceProfile Stepped(VoiceProfile profile, DeliveryEnergy energy)
    {
        var (stability, style) = DeliveryEnergies.Apply(profile.Stability, profile.Style, energy);
        return profile.CloneForDelivery(stability, style);
    }
}
