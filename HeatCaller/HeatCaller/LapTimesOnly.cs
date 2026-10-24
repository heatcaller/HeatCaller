using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// "Lap times only" - the plainest race night the announcer can give: each
/// driver's lap time read out, the calls a race cannot do without, and
/// nothing else. Offered beside the soundscapes in the setup wizards (see
/// SoundOffer).
///
/// It is not a soundscape - a soundscape brings sounds, lines and a
/// background, and this takes them away - so it is a set of switches on the
/// club's own settings, all of which stay where they are put until the club
/// changes them, as an applied soundscape's do:
///
///  - Voice: the lap time, the green flag, time up, the checkered flag and
///    the yellow, red and green-again flags are switched on. The rest of the
///    start and finish (staging, the countdown, a jump start, a staggered
///    driver's release, the grid call, last lap, final minute) and the
///    program's reminders are left as the club has them. Everything else -
///    fast laps, records, leader changes, battles, streaks, ghosts, badges,
///    position reports - is switched off. Only the switches: the wording is
///    the club's, so this is the same on every voice, the HeatCaller voice
///    included (whose lines are fixed, but whose switches are not).
///  - Sound effects: the stingers that play on a lap (Stingers) are switched
///    off. The start, countdown, flag and finish sounds are left alone.
///  - Music and ambience are no longer brought up with a race (the Mixer's
///    "Bring up:"), and the spoken race intros, recaps and the teaser before
///    a recap are switched off. What those five were is kept
///    (AppSettings.LapTimesOnly), and put back by <see cref="Leave"/> when a
///    soundscape is applied again.
///  - The applied soundscape's mark is cleared: none is in use now.
///
/// Works on the stores and the settings only, never on a page; the host
/// refreshes the pages afterwards, as it does after a soundscape is applied.
/// </summary>
internal static class LapTimesOnly
{
    /// <summary>The announcements switched on.</summary>
    public static readonly IReadOnlySet<AnnouncementEventType> Spoken = new HashSet<AnnouncementEventType>
    {
        AnnouncementEventType.LapTime,
        AnnouncementEventType.RaceStarted,
        AnnouncementEventType.RaceTimeOver,
        AnnouncementEventType.RaceCompleted,
        AnnouncementEventType.YellowFlag,
        AnnouncementEventType.RedFlag,
        AnnouncementEventType.GreenFlagResumed,
    };

    /// <summary>
    /// The announcements left exactly as the club has them: the rest of the
    /// start and the finish, which a club sets up for the way it races - a
    /// staggered start needs its releases, a countdown read out may be the
    /// only one it has - the reminders of what races next, and the split
    /// loop's time and coaching.
    /// </summary>
    public static readonly IReadOnlySet<AnnouncementEventType> LeftAsSet = new HashSet<AnnouncementEventType>
    {
        AnnouncementEventType.RaceStaging,
        AnnouncementEventType.CountdownStarted,
        AnnouncementEventType.JumpStart,
        AnnouncementEventType.DriverRelease,
        AnnouncementEventType.GridCall,
        AnnouncementEventType.LastLap,
        AnnouncementEventType.FinalMinute,
        AnnouncementEventType.Reminder,

        // The split loop's own lap time, and the coaching a driver asked
        // for (22 Oct 2026): a time read out, which is what this mode is,
        // so left as the club has them rather than switched off. The fight
        // at the split and the sectors at the flag are chatter, and go.
        AnnouncementEventType.SplitTime,
        AnnouncementEventType.SplitDelta,

        // The rest of the coaching (22 Oct 2026), for the same reason: a
        // driver the desk is coaching asked to hear it, and it is a time.
        AnnouncementEventType.TargetPace,
        AnnouncementEventType.ConsistencyCoach,
        AnnouncementEventType.GhostGap,
    };

    /// <summary>The sound effects switched off: every one that plays on a lap.</summary>
    public static readonly IReadOnlySet<SoundEffectKind> Stingers = new HashSet<SoundEffectKind>
    {
        SoundEffectKind.GoodLap,
        SoundEffectKind.InvalidLap,
        SoundEffectKind.PersonalBest,
        SoundEffectKind.FastLap,
        SoundEffectKind.TrackRecord,
        SoundEffectKind.Streak,
        SoundEffectKind.LeaderChange,
    };

    /// <summary>True while lap times only is what the sound was last set to.</summary>
    public static bool IsOn(AppSettings settings) => settings.LapTimesOnly is not null;

    /// <summary>
    /// Sets the sound to lap times only, and returns the action that puts
    /// back everything this changed, for Undo.
    /// </summary>
    public static Action Apply(
        AppSettings settings,
        VoiceSettingsStore voiceStore,
        SoundEffectsStore soundStore,
        ThemeStore themeStore)
    {
        // The voice, the sound effects and the applied soundscape's mark,
        // taken as they are now - the same undo an applied soundscape has.
        var putBackStores = ThemeStore.CaptureForUndo(null, voiceStore, soundStore);
        var settingsBefore = Snapshot.Of(settings);

        // Applied a second time, the first time's record of what to put back
        // is kept: the second would only record what the first switched off.
        var putBack = settings.LapTimesOnly ?? new LapTimesOnlyPutBack
        {
            StartMusic = settings.AudioStartMusic,
            StartAmbience = settings.AudioStartAmbience,
            RaceIntros = settings.CommentaryRaceIntros,
            RaceRecaps = settings.CommentaryRaceSummaries,
            RecapTeaser = settings.SummaryTeaserEnabled,
        };

        // A hand-edited settings file can leave it null.
        putBack.Events ??= new();

        // --- Voice. A copy saved as a new profile, as ThemeStore.Apply does.
        var current = voiceStore.Current;
        var voice = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(current)) ?? VoiceProfile.CreateDefault();
        voice.CopyApiKeyFrom(current);

        var defaults = VoiceProfile.CreateDefault().Events;

        foreach (var type in Enum.GetValues<AnnouncementEventType>())
        {
            if (LeftAsSet.Contains(type))
            {
                continue;
            }

            var key = type.ToString();
            var on = Spoken.Contains(type);

            if (!voice.Events.TryGetValue(key, out var config) || config is null)
            {
                // Missing is off already (the announcer reads a missing
                // event as one switched off). One to switch on gets the
                // standard lines - the only ones there are to read.
                if (!on || !defaults.TryGetValue(key, out var standard))
                {
                    continue;
                }

                config = JsonSerializer.Deserialize<AnnouncementEventConfig>(JsonSerializer.Serialize(standard))!;
                voice.Events[key] = config;

                // Nothing to put back but "missing", which is off.
                putBack.Events.TryAdd(key, new LapTimesOnlyPutBack.Switches { Enabled = false, EnabledInPractice = null });
            }
            else
            {
                putBack.Events.TryAdd(key, new LapTimesOnlyPutBack.Switches { Enabled = config.Enabled, EnabledInPractice = config.EnabledInPractice });
            }

            // In practice too: the same night, read the same way.
            config.Enabled = on;
            config.EnabledInPractice = null;
        }

        voiceStore.Save(voice);

        // --- Sound effects: the stingers off, the rest as they are.
        var sound = SoundEffectsStore.Copy(soundStore.Current);

        foreach (var kind in Stingers)
        {
            var key = kind.ToString();

            if (sound.Effects.TryGetValue(key, out var effect) && effect is not null)
            {
                effect.Enabled = false;
                effect.EnabledInPractice = null;
            }
            else
            {
                sound.Effects[key] = new SoundEffectConfig { Enabled = false };
            }
        }

        soundStore.Save(sound);

        // --- Music, ambience and the spoken extras.
        settings.LapTimesOnly = putBack;
        settings.AudioStartMusic = false;
        settings.AudioStartAmbience = false;
        settings.CommentaryRaceIntros = false;
        settings.CommentaryRaceSummaries = false;
        settings.SummaryTeaserEnabled = false;
        settings.Save();

        // No soundscape is in use now.
        themeStore.ClearApplied();

        return () =>
        {
            putBackStores();
            settingsBefore.PutBack(settings);
            settings.Save();
        };
    }

    /// <summary>
    /// Leaves lap times only, as a soundscape is about to be applied: the
    /// announcements it switched were switched back as they were, music and
    /// ambience come up with a race again and the spoken extras are back, as
    /// each was before - and returns the action that undoes that, or null
    /// when the sound was not lap times only.
    ///
    /// The announcements matter on the HeatCaller voice, where applying a
    /// soundscape keeps the club's own (ThemeStore.Apply). On every other
    /// voice the soundscape replaces them next anyway.
    ///
    /// Call ThemeStore.CaptureForUndo FIRST: the voice is put back by that
    /// (as it was before this), and the undo returned here puts back only the
    /// settings.
    /// </summary>
    public static Action? Leave(AppSettings settings, VoiceSettingsStore voiceStore)
    {
        if (settings.LapTimesOnly is not { } putBack)
        {
            return null;
        }

        var settingsBefore = Snapshot.Of(settings);

        if (putBack.Events is { Count: > 0 })
        {
            var current = voiceStore.Current;
            var voice = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(current)) ?? VoiceProfile.CreateDefault();
            voice.CopyApiKeyFrom(current);

            foreach (var (key, switches) in putBack.Events)
            {
                // A hand-edited settings file can leave one null.
                if (switches is null)
                {
                    continue;
                }

                if (voice.Events.TryGetValue(key, out var config) && config is not null)
                {
                    config.Enabled = switches.Enabled;
                    config.EnabledInPractice = switches.EnabledInPractice;
                }
            }

            voiceStore.Save(voice);
        }

        settings.AudioStartMusic = putBack.StartMusic;
        settings.AudioStartAmbience = putBack.StartAmbience;
        settings.CommentaryRaceIntros = putBack.RaceIntros;
        settings.CommentaryRaceSummaries = putBack.RaceRecaps;
        settings.SummaryTeaserEnabled = putBack.RecapTeaser;
        settings.LapTimesOnly = null;
        settings.Save();

        return () =>
        {
            settingsBefore.PutBack(settings);
            settings.Save();
        };
    }

    /// <summary>The settings either of these changes, as they were - for an undo.</summary>
    private sealed record Snapshot(
        bool StartMusic,
        bool StartAmbience,
        bool RaceIntros,
        bool RaceRecaps,
        bool RecapTeaser,
        string? PutBackJson)
    {
        public static Snapshot Of(AppSettings settings) => new(
            settings.AudioStartMusic,
            settings.AudioStartAmbience,
            settings.CommentaryRaceIntros,
            settings.CommentaryRaceSummaries,
            settings.SummaryTeaserEnabled,

            // A copy, so changing the live one later cannot reach it.
            settings.LapTimesOnly is null ? null : JsonSerializer.Serialize(settings.LapTimesOnly));

        public void PutBack(AppSettings settings)
        {
            settings.AudioStartMusic = StartMusic;
            settings.AudioStartAmbience = StartAmbience;
            settings.CommentaryRaceIntros = RaceIntros;
            settings.CommentaryRaceSummaries = RaceRecaps;
            settings.SummaryTeaserEnabled = RecapTeaser;
            settings.LapTimesOnly = PutBackJson is null ? null : JsonSerializer.Deserialize<LapTimesOnlyPutBack>(PutBackJson);
        }
    }
}

/// <summary>
/// What lap times only changed, as it was before - kept in settings.json
/// while it is the sound in use, so applying a soundscape later puts it
/// back (see LapTimesOnly.Leave). Null in settings whenever it is not.
/// </summary>
public sealed class LapTimesOnlyPutBack
{
    public bool StartMusic { get; set; }
    public bool StartAmbience { get; set; }
    public bool RaceIntros { get; set; }
    public bool RaceRecaps { get; set; }
    public bool RecapTeaser { get; set; }

    /// <summary>Each announcement it switched, by its AnnouncementEventType name, as it was.</summary>
    public Dictionary<string, Switches> Events { get; set; } = new();

    public sealed class Switches
    {
        public bool Enabled { get; set; }
        public bool? EnabledInPractice { get; set; }
    }
}
