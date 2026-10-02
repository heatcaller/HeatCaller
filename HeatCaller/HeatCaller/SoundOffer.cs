namespace HeatCaller;

/// <summary>
/// What a setup wizard needs to offer the sound, handed in by MainForm -
/// which owns the stores, the Undo list and the pages that show the sound,
/// so applying from a wizard is the same apply, with the same undo, as on
/// the Soundscapes page.
/// </summary>
/// <param name="Soundscapes">The soundscapes to offer, in the Soundscapes page's order.</param>
/// <param name="SetNow">What the sound was last set to - a soundscape's name, "lap times only", or empty when neither is known.</param>
/// <param name="ApplySoundscape">Applies a soundscape, as Apply on the Soundscapes page does, and refreshes the pages.</param>
/// <param name="ApplyLapTimesOnly">Sets the sound to lap times only (LapTimesOnly.Apply), and refreshes the pages.</param>
internal sealed record SoundOfferHost(
    Func<IReadOnlyList<Theme>> Soundscapes,
    Func<string> SetNow,
    Action<Theme> ApplySoundscape,
    Action ApplyLapTimesOnly);

/// <summary>
/// The sound, as the setup wizards offer it: a "Sound:" drop-down - keep
/// what is set now, one of the soundscapes, or lap times only - with what
/// the choice means under it, and "Don't ask about the sound again" under
/// that. Built once and added to a wizard's own FieldGrid (AddTo), so it
/// lines up with the step's other fields; the same three in both wizards.
///
/// The choice is applied when the step is left (Save), and only when it is
/// a different one from the last this wizard applied - going Back and Next
/// again does not apply the same soundscape twice, or put a second entry on
/// the Undo list. What it applies stays applied until it is changed, as an
/// apply on the Soundscapes page does.
/// </summary>
internal sealed class SoundOffer
{
    /// <summary>The drop-down's least width at the normal text size - wider when a choice needs it.</summary>
    private const int PickerWidth = 300;

    public const string LapTimesOnlyChoice = "Lap times only";

    private const string KeepHint =
        "The announcer, sounds, music and ambience stay as they are. Soundscapes can be tried and changed any time on the Soundscapes page.";

    private const string LapTimesOnlyHint =
        "The announcer reads each driver's lap time and calls the start, the finish and the flags - nothing else. "
        + "No music, no ambience, and none of the sound effects that play on a lap.";

    private readonly AppSettings _settings;
    private readonly SoundOfferHost _host;

    /// <summary>The drop-down's measured width, kept until its font or list changes - see Pieces.PickerWidth.</summary>
    private (Font Font, int Count, int Width)? _measured;

    /// <summary>The last choice this wizard applied, or null when it has applied none.</summary>
    private Choice? _applied;

    public SoundOffer(AppSettings settings, SoundOfferHost host)
    {
        _settings = settings;
        _host = host;

        Picker.DropDownStyle = ComboBoxStyle.DropDownList;

        Picker.Items.Add(KeepChoice(host.SetNow()));

        foreach (var theme in host.Soundscapes())
        {
            Picker.Items.Add(new Choice(ChoiceKind.Soundscape, theme, theme.Name, theme.Description));
        }

        Picker.Items.Add(new Choice(ChoiceKind.LapTimesOnly, null, LapTimesOnlyChoice, LapTimesOnlyHint));
        Picker.SelectedIndex = 0;

        // A soundscape is named by whoever saved it - an "&" in it is meant.
        Hint.UseMnemonic = false;
        Hint.Wordings = Picker.Items.Cast<Choice>().Select(c => c.Hint).ToArray();
        Hint.Text = KeepHint;

        Picker.SelectedIndexChanged += (_, _) => Hint.Text = (Picker.SelectedItem as Choice)?.Hint ?? string.Empty;

        DontAsk.Text = "Don't ask about the sound again";
        DontAsk.AutoSize = true;

        // The tick is kept the moment it changes, not only when the step is
        // left. Save runs from a wizard's SaveCurrentStep - Next, Back, a
        // rail click - and a wizard can be left without any of those: closed
        // with the window's X, which neither wizard turns into a save. A
        // club that ticked "Don't ask about the sound again" and then closed
        // the window has still answered the question, and being offered the
        // sound again next time (with the Soundscapes page's tick showing it
        // on) says the program ignored them. The sound choice itself still
        // waits for the step to be left - applying a soundscape is a bigger
        // thing, with an Undo entry, and the drop-down is passed through on
        // the way to the one wanted. Save keeps its own check below, so
        // calling it afterward changes nothing that was kept here.
        DontAsk.CheckedChanged += (_, _) => KeepTick();
    }

    /// <summary>"Sound:" - what to do with it.</summary>
    public ComboBox Picker { get; } = new();

    /// <summary>What the choice means, under it. As tall as the longest it can say, so the step does not hop as it changes.</summary>
    public SteadyLabel Hint { get; } = new();

    /// <summary>Turns the offer off for both wizards - back on from the Soundscapes page.</summary>
    public CheckBox DontAsk { get; } = new ThemedCheckBox();

    /// <summary>What the sound came to, for a summary: the choice this wizard applied, or what was set already.</summary>
    public string Summary
    {
        get
        {
            if (_applied is { } applied)
            {
                return applied.Kind == ChoiceKind.LapTimesOnly ? LapTimesOnlyChoice : applied.Name;
            }

            var setNow = _host.SetNow();
            return setNow.Length > 0 ? $"{char.ToUpperInvariant(setNow[0])}{setNow[1..]}, as it was" : "As it was";
        }
    }

    /// <summary>The three rows, in a wizard's grid: the drop-down with its caption, what it means, and the tick.</summary>
    public void AddTo(FieldGrid grid)
    {
        grid.AddRow("Sound:", Picker);
        grid.AddHint(Hint);
        grid.AddField(DontAsk);
    }

    /// <summary>The drop-down as wide as its longest choice, or as it was made at the text size in use if that is more. Called from a wizard's SizeFields.</summary>
    public void SizeFields()
    {
        var width = Math.Max(UiLayout.AtTextSize(PickerWidth), Pieces.PickerWidth(Picker, ref _measured));

        if (Picker.Width != width)
        {
            Picker.Width = width;
        }
    }

    /// <summary>
    /// Applies the choice when it is not one this wizard applied last, and
    /// keeps the tick. Called as the wizard leaves the step it is on; safe to
    /// call more than once.
    /// </summary>
    public void Save()
    {
        if (Picker.SelectedItem is Choice { Kind: not ChoiceKind.Keep } picked && !ReferenceEquals(picked, _applied))
        {
            if (picked.Kind == ChoiceKind.LapTimesOnly)
            {
                _host.ApplyLapTimesOnly();
            }
            else if (picked.Soundscape is { } theme)
            {
                _host.ApplySoundscape(theme);
            }

            _applied = picked;

            // "Keep what's set now (...)" was named when the wizard opened,
            // and what is set now has just changed. Left as it was, going
            // Back showed "Keep what's set now (Arcade)" after lap times
            // only had been applied - and picking it kept lap times only,
            // because Keep applies nothing. Renamed from what is set now, it
            // says what keeping really keeps. The item at 0 is never the
            // one selected here (Keep is not applied), so the selection
            // stays put; the width is measured again in case the new name
            // is the longest.
            Picker.Items[0] = KeepChoice(_host.SetNow());
            _measured = null;
            SizeFields();
        }

        KeepTick();
    }

    /// <summary>Writes "Don't ask about the sound again" to the settings, and saves them, when it differs from what they hold.</summary>
    private void KeepTick()
    {
        var ask = !DontAsk.Checked;

        if (_settings.AskAboutSoundInWizards != ask)
        {
            _settings.AskAboutSoundInWizards = ask;
            _settings.Save();
        }
    }

    /// <summary>"Keep what's set now", naming what that is when it is known - see SoundOfferHost.SetNow.</summary>
    private static Choice KeepChoice(string setNow) =>
        new(ChoiceKind.Keep, null, setNow.Length > 0 ? $"Keep what's set now ({setNow})" : "Keep what's set now", KeepHint);

    private enum ChoiceKind
    {
        Keep,
        Soundscape,
        LapTimesOnly,
    }

    /// <summary>One of the drop-down's choices. Shown by its name.</summary>
    private sealed record Choice(ChoiceKind Kind, Theme? Soundscape, string Name, string Hint)
    {
        public override string ToString() => Name;
    }
}
