namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The Sounds page's windows. The soundscape editor (SoundscapeEditorForm,
    /// from Soundscapes' Edit... and Customize...): a club's own soundscape
    /// with every sound on a file of its own - an "own file" note on every
    /// line, the widest the sounds' lines get - its own playlist, every name
    /// long and more lines than the lines box shows, on a PC that speaks with
    /// the HeatCaller voice, so the note about that shows too; a club's copy
    /// of a built-in with its pack's music, as the Windows voice sees it; and
    /// a built-in being customized, which opens as a new soundscape with no
    /// Delete. And Ambience's layer editor (AmbienceLayerForm, from Add
    /// Layer... and Edit...): a new layer, and one with a long name and a
    /// long file path. None of them is given anything that plays - no preview
    /// to run, no ambience - so nothing is heard and no audio device opened.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SoundWindows() => new (string, Func<Form>)[]
    {
        ("Edit soundscape (every sound its own file, long names, HeatCaller voice)", () => SoundscapeEditor(
            SoundscapeEveryFieldLong(),
            isNew: false,
            heatCallerVoice: true,
            library: new List<string>())),
        ("Edit soundscape (a copy of a built-in)", () => SoundscapeEditor(
            SoundscapeFromBuiltIn("Synthwave", "Synthwave - Friday nights"),
            isNew: false,
            heatCallerVoice: false,
            library: SoundscapeWholeLibrary())),
        ("New soundscape", () => SoundscapeEditor(
            SoundscapeFromBuiltIn("Pit Lane", "Pit Lane (my version)"),
            isNew: true,
            heatCallerVoice: false,
            library: SoundscapeWholeLibrary())),
        ("Add Ambience Layer", () => new AmbienceLayerForm(null)),
        ("Edit Ambience Layer (long name and file)", () => new AmbienceLayerForm(SoundLongLayer())),
    };

    /// <summary>
    /// The editor as the Soundscapes page opens it, over the soundscapes in
    /// the test's data folder - with a preview that plays nothing and a stop
    /// that stops nothing, and the voice this PC speaks with: the HeatCaller
    /// voice, or a new install's Windows voice.
    /// </summary>
    private static SoundscapeEditorForm SoundscapeEditor(Theme working, bool isNew, bool heatCallerVoice, IReadOnlyList<string> library) =>
        new(
            working,
            isNew,
            ThemeStore.Load(),
            heatCallerVoice ? new VoiceProfile { Engine = VoiceEngine.HeatCaller } : VoiceProfile.CreateDefault(),
            (_, _) => Task.CompletedTask,
            () => { },
            library);

    /// <summary>
    /// A club's own soundscape with everything in it as long as it gets: a
    /// long name and a description of more lines than its box shows; every
    /// sound on a file of its own, the packs they fall back to in turn; a
    /// crowd, and a playlist of its own that is no pack's; its delivery at
    /// the top of each range; and the first race moment - the one shown as
    /// the editor opens - with more lines than the lines box shows, each
    /// wider than it.
    /// </summary>
    private static Theme SoundscapeEveryFieldLong()
    {
        var voice = VoiceProfile.CreateDefault();
        voice.Stability = 1.0;
        voice.Style = 1.0;
        voice.Speed = 1.2;

        voice.Events[nameof(AnnouncementEventType.RaceStaging)] = new AnnouncementEventConfig
        {
            Enabled = true,
            Templates = Enumerable.Range(1, 12)
                .Select(i => $"[shouting] Drivers for heat {i}, bring those cars to the grid - Alexandria Montgomery-Fitzwilliam, that means you as well!")
                .ToList(),
        };

        var packs = new[] { SoundPacks.ClassicKey, "Podium", "Arcade", "Broadcast", "Synthwave", "PitLane" };
        var effects = SoundEffectsProfile.CreateDefault();
        var index = 0;

        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            var config = effects.Effects.GetValueOrDefault(kind.ToString()) ?? new SoundEffectConfig();

            config.BuiltInSound = packs[index++ % packs.Length];
            config.CustomFilePath = $@"D:\Club Sounds\Friday Night Thunder\Montgomery-Fitzwilliam recordings\{kind} - air horn and crowd (final mix, 48kHz).wav";

            effects.Effects[kind.ToString()] = config;
        }

        return new Theme
        {
            Name = "Friday Night Thunder - Alexandria Montgomery-Fitzwilliam's trackside special",
            Description = "Our own air horns, bells and the Friday crowd, recorded at the 2026 club final, with the loudest announcer "
                + "we could find. Every sound plays a file of its own; the packs are only what each one falls back to when the "
                + "club's sound drive isn't plugged in. Ask Alexandria before changing any of it.",
            SoundPack = "PitLane",
            Voice = voice,
            SoundEffects = effects,
            Background = new SoundscapeBackground
            {
                AmbienceBed = "PitLane",
                MusicPlaylist = new List<string> { "Neon Grid", "Victory Lane", "Our Club Anthem (2026 remaster)", "Last Lap Lights" },
            },
        };
    }

    /// <summary>
    /// A built-in soundscape copied as the Soundscapes page copies one to
    /// edit: an id of its own, no longer a built-in, and a name of its own.
    /// </summary>
    private static Theme SoundscapeFromBuiltIn(string builtIn, string name)
    {
        var theme = Theme.CreateBuiltInDefaults().First(t => t.Name == builtIn);

        theme.Id = Guid.NewGuid().ToString("N");
        theme.IsBuiltIn = false;
        theme.BuiltInVersion = 0;
        theme.Name = name;

        return theme;
    }

    /// <summary>Every pack's music in the club's library, so no music choice says it is not there yet.</summary>
    private static List<string> SoundscapeWholeLibrary() =>
        SoundPacks.PackMusic.Values.SelectMany(titles => titles).ToList();

    /// <summary>A layer with a long name and a long path to its file, its levels running from 5% to 100%.</summary>
    private static AmbienceLayer SoundLongLayer() => new()
    {
        Name = "Alexandria Montgomery-Fitzwilliam's grandstand crowd - main straight",
        FilePath = @"D:\Club Sounds\Ambience\Grandstand crowd recorded at the 2026 club final (seamless loop, 48kHz, normalized).wav",
        Enabled = true,
        IdleVolume = 0.05f,
        AnticipationVolume = 0.3f,
        RacingVolume = 0.45f,
        ExcitedVolume = 0.85f,
        PeakVolume = 1.0f,
    };
}
