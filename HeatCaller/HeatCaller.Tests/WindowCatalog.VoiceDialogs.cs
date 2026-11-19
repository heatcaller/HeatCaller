namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two voice windows the Sounds list also measures (18 Nov 2026, dialog
    /// batch 5). The crew voices picker (CrewVoicesForm, from Cast's crew
    /// voices button): a long account's voices, some hidden and some names
    /// long, and an account with none. And the lines the announcer did not
    /// say (UnsaidLinesForm, from Race Control's lost-lines count): a busy
    /// race with lost and held lines, long words and a long reason among
    /// them, and a race with nothing lost yet.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> VoiceDialogWindows() => new (string, Func<Form>)[]
    {
        ("Voices drivers can pick", () => CrewVoices(24, hideEvery: 3)),
        ("Voices drivers can pick (none)", () => CrewVoices(0, hideEvery: 0)),
        ("Lines not said this race", () => new UnsaidLinesForm(UnsaidBusyRace(), () => "AI commentary this race: 4 segments weighed up, 3 said, 1 held back for a lap call.")),
        ("Lines not said this race (nothing yet)", () => new UnsaidLinesForm(new AnnouncerLoad())),
    };

    private static CrewVoicesForm CrewVoices(int count, int hideEvery)
    {
        var voices = Enumerable.Range(1, count)
            .Select(i => new CrewVoiceOption(
                new CrewVoicePick
                {
                    Engine = "elevenlabs",
                    Id = $"voice-{i:00}",
                    Name = i % 5 == 0 ? $"Alexandria Montgomery-Fitzwilliam (cloned, studio take {i})" : $"Voice number {i}",
                },
                "Sample"))
            .ToList();

        var hidden = voices
            .Where((_, i) => hideEvery > 0 && i % hideEvery == 0)
            .Select(v => DriverCrew.KeyOf(v.Pick))
            .ToList();

        return new CrewVoicesForm(voices, hidden);
    }

    private static AnnouncerLoad UnsaidBusyRace()
    {
        var load = new AnnouncerLoad();
        var start = DateTime.UtcNow.AddMinutes(-5);

        for (var i = 0; i < 18; i++)
        {
            var lost = i % 3 != 2;
            var line = new UnsaidLine(
                start.AddSeconds(i * 14),
                lost ? UnsaidKind.Lost : UnsaidKind.Held,
                i % 4 == 0 ? "Lap time" : i % 4 == 1 ? "Gap to leader" : i % 4 == 2 ? "Fast lap" : "Position change",
                i % 2 == 0 ? "Alexandria Montgomery-Fitzwilliam" : "Sam",
                i == 4 ? null : i % 5 == 0
                    ? "Alexandria Montgomery-Fitzwilliam takes another tenth out of the leader, a fourteen point two, and the gap is down to under a second with two laps to go!"
                    : "Sam, a nine flat.",
                lost
                    ? "Lost: the race recap was still being said, and by the time it finished the lap was two laps old."
                    : "Held: the pacing dial keeps a gap between routine calls.");

            load.NoteOffered();

            if (lost)
            {
                load.NoteDropped(line);
            }
            else
            {
                load.NoteHeld(line);
            }
        }

        return load;
    }
}
