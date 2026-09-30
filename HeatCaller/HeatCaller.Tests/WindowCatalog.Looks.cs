namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The look editor (LookEditorForm) as Themes' Looks card opens it: New
    /// Look... on a built-in look, which hands it a copy named as the card
    /// names it; Edit... on one of the club's own, with the longest name a
    /// look keeps and a long description; and Edit... on one whose colours
    /// fail every readability check - the longest list of warnings it can
    /// show as it opens, which makes the preview's side taller than the
    /// colours. And the two lines Sounds' Soundscapes asks for when its
    /// settings are saved as a new soundscape - the name, then the
    /// description (TextPromptForm, from ThemesControl.PromptForText).
    ///
    /// None of them saves anything or changes the look in use: the editor
    /// works on the copy it is handed, and only saves when Save is pressed.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> LookWindows()
    {
        // As the Looks card's New Look... makes it: the look under a fresh
        // custom key, "(copy)" after its name.
        static Look LookCopyOf(string key)
        {
            var source = Looks.Find(key)!;

            return source.CloneAsCustom(Looks.NewCustomKey(), $"{source.Name} (copy)");
        }

        // One of the club's own: a name of 40 characters, the most a look
        // keeps, and a description near the 200 it keeps.
        static Look LookClubsOwn() => new()
        {
            Key = Looks.NewCustomKey(),
            Name = "Northfield Mini-Z Club Championship Wall",
            Description = "Championship nights on the big wall at the Northfield hall: race-wall lime on near-black so the "
                          + "leaderboard reads from the back of the room, and the Synthwave header for the finals and prizes.",
            Family = Look.FamilySynthwave,
            Colors = Looks.Find("race-wall")!.Colors.Clone(),
        };

        // Every colour a grey within a shade of the others, so every one of
        // the eight readability checks fails.
        static Look LookHardToRead() => new()
        {
            Key = Looks.NewCustomKey(),
            Name = "Fog",
            Family = Look.FamilyLight,
            Colors = new LookColors
            {
                Bg = "#8A8A8A",
                Surface = "#8C8C8C",
                RowAlt = "#8B8B8B",
                Primary = "#909090",
                OnPrimary = "#949494",
                Accent = "#888888",
                Text = "#999999",
                Muted = "#868686",
                Border = "#858585",
                Good = "#8E8E8E",
                Bad = "#929292",
            },
        };

        const string saveSoundscape = "Save Current Settings as New Soundscape";

        return new (string, Func<Form>)[]
        {
            ("New Look", () => new LookEditorForm(LookCopyOf("race-wall"), isNew: true)),
            ("Edit Look (the club's own, longest name)", () => new LookEditorForm(LookClubsOwn(), isNew: false)),
            ("Edit Look (every readability warning)", () => new LookEditorForm(LookHardToRead(), isNew: false)),
            (saveSoundscape, () => new TextPromptForm(saveSoundscape, "Name:", string.Empty)),
            (saveSoundscape + " (description)", () => new TextPromptForm(saveSoundscape, "Short description (optional):", string.Empty)),
        };
    }
}
