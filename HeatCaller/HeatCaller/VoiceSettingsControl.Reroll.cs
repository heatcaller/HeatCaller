namespace HeatCaller;

/// <summary>
/// RE-ROLLING A SAVED LINE (22 Oct 2026, known issues).
///
/// A take that came out badly could only be forgotten - made afresh the
/// next time a race wanted it, unheard until then - and with the HeatCaller
/// voice not even that: the relay kept its own copy and handed the same
/// take straight back. Re-roll asks for a new take now, past every cache
/// (the relay's "fresh"), plays it, and keeps it in place of the old one,
/// so what was just heard is what the race plays.
///
/// It is charged like any line that was not saved, which is why the list
/// shows what each line costs and the button asks first.
/// </summary>
public sealed partial class VoiceSettingsControl
{
    private readonly Button _btnRerollLine = new();

    /// <summary>What the last Re-roll did, under the saved lines' buttons - where it was pressed.</summary>
    private readonly Label _lblRerollStatus = new() { AutoSize = true, Visible = false };

    /// <summary>
    /// What a line costs to make again, in characters - the unit the
    /// HeatCaller allowance and ElevenLabs both count. Audio tags left out:
    /// the least either could count, as HeatCallerVoiceClient measures.
    /// </summary>
    internal static int CharactersCharged(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : VoiceProfile.StripAudioTags(text).Trim().Length;

    /// <summary>
    /// Why a saved line cannot be re-rolled with the voice as it is set now,
    /// or null when it can. Only a line saved under exactly the current
    /// voice and settings can be: the new take has to replace the old one,
    /// not sit beside it under another key the race would never ask for.
    /// </summary>
    internal static string? WhyNoReroll(VoiceProfile profile, VoiceLibrary.SavedLine line, string expectedKey, string? textTheRaceSends = null)
    {
        // A take saved before "Shorten published names" was ticked (7 Nov
        // 2026, known issues): the race now asks for the shortened words, so
        // re-rolling the full ones would send full names to the relay once
        // more and keep a take the race never plays.
        if (profile.EffectiveEngine != VoiceEngine.Kokoro
            && textTheRaceSends is not null
            && !string.IsNullOrWhiteSpace(line.Text)
            && !string.Equals(textTheRaceSends, line.Text, StringComparison.Ordinal))
        {
            // Only the round's letter respelt (10 Nov 2026, bug hunt): a take
            // saved as "A-Main" before the online voices were sent "Ay-Main"
            // (see RaceNumbers.SpokenRoundNames) is not about names at all.
            if (string.Equals(RaceNumbers.SpokenRoundNames(line.Text, cloudVoice: true), textTheRaceSends, StringComparison.Ordinal))
            {
                return "That line was saved before \"A-Main\" was spelt \"Ay-Main\" for the online voices - "
                       + "the race asks for the new spelling, so this take is never played. Use Forget Line instead.";
            }

            return "That line has drivers' full names in it, and the club now shortens names for the online voices - "
                   + "the race asks for the short form, so this take is never played. Use Forget Line instead.";
        }

        if (profile.EffectiveEngine == VoiceEngine.Kokoro)
        {
            return "Kokoro lines are made on this laptop every time - there is no saved take to re-roll.";
        }

        if (string.IsNullOrWhiteSpace(line.Text) || line.Text == "(words not recorded)")
        {
            return "That line's words weren't recorded with it, so it can't be asked for again. Use Forget Line instead.";
        }

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller && CharactersCharged(line.Text) > HeatCallerVoiceClient.MaxCharacters)
        {
            return "That line is spoken in pieces, so it can't be re-rolled in one go. Use Forget Line instead - it is made afresh the next time it's wanted.";
        }

        if (!string.Equals(expectedKey, line.Key, StringComparison.OrdinalIgnoreCase))
        {
            return "That line was saved with a different voice or different settings (an event's own speed, say). "
                   + "Use Forget Line instead - it is made afresh, as the race asks for it, the next time it's wanted.";
        }

        return null;
    }

    /// <summary>
    /// The profile a saved line was made with, of the two a race speaks
    /// with: <paramref name="profile"/> itself, or the same on the race-calls
    /// model when one is set (AnnouncementService.ForRaceCall) and the line's
    /// key is that one's. A race call ("Last lap!") is saved under the
    /// race-calls model, so checked against the main model alone it was
    /// refused as "a different voice or different settings" - and a new take
    /// made on the main model would be one the race never asks for.
    /// </summary>
    internal static VoiceProfile ProfileSavedUnder(VoiceProfile profile, VoiceLibrary.SavedLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Text)
            || string.Equals(VoiceLibrary.KeyFor(profile, line.Text), line.Key, StringComparison.OrdinalIgnoreCase))
        {
            return profile;
        }

        // Any race call does: they all go to the one race-calls model.
        var onRaceModel = AnnouncementService.ForRaceCall(profile, AnnouncementEventType.LastLap);

        return !ReferenceEquals(onRaceModel, profile)
               && string.Equals(VoiceLibrary.KeyFor(onRaceModel, line.Text), line.Key, StringComparison.OrdinalIgnoreCase)
            ? onRaceModel
            : profile;
    }

    private void WireRerollButton()
    {
        _btnRerollLine.Text = "Re-roll Line...";
        _btnRerollLine.Enabled = false;
        _btnRerollLine.Click += BtnRerollLine_Click;
        _lblRerollStatus.ForeColor = AppTheme.TextMuted;
    }

    private void RerollSays(string text, Color colour)
    {
        _lblRerollStatus.Text = text;
        _lblRerollStatus.ForeColor = colour;
        _lblRerollStatus.Visible = true;
    }

    private async void BtnRerollLine_Click(object? sender, EventArgs e)
    {
        if (_cmbSavedLines.SelectedItem is not SavedLineItem item || _heatCallerHearingRunning)
        {
            return;
        }

        // The SAVED profile, which is what a race speaks with - not the
        // boxes on screen, which may be part-way through a change.
        var line = item.Line;

        // On the race-calls model when that is what the line was saved with.
        var profile = ProfileSavedUnder(_store.Current, line);

        var textTheRaceSends = Announcer?.TextForCloudVoice(line.Text);

        if (WhyNoReroll(profile, line, VoiceLibrary.KeyFor(profile, line.Text), textTheRaceSends) is { } why)
        {
            RerollSays(why, AppTheme.Danger);
            return;
        }

        var cost = CharactersCharged(line.Text);
        var where = profile.EffectiveEngine == VoiceEngine.HeatCaller
            ? "the club's HeatCaller voice allowance"
            : "your ElevenLabs credits";

        if (AppDialog.Show(this,
                $"Make a new take of this line?\r\n\r\n\"{line.Text}\"\r\n\r\n"
                + $"It costs {Plural.Of(cost, "character")} of {where}, like any line that wasn't saved. "
                + "The new take is played, and replaces the old one for the race.",
                "Re-roll a saved line", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _heatCallerHearingRunning = true;
        PreservingScroll(() => _btnRerollLine.Enabled = false);
        RerollSays("Making a new take...", AppTheme.TextMuted);

        try
        {
            byte[] audio;

            if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
            {
                if (HeatCallerVoice is not { } client)
                {
                    throw new InvalidOperationException("The HeatCaller voice isn't set up on this page yet.");
                }

                // Never shared: the relay keeps a fresh take for this club
                // alone whatever this says, and the new take is kept on
                // this laptop below under the key the race asks for.
                (audio, _, _) = await client.SpeakAsync(profile.HeatCallerVoice, line.Text, shared: false, fresh: true);
                VoiceLibrary.Shared.Save(profile, line.Text, audio);
            }
            else
            {
                // The same fresh take Test makes: it replaces the saved one.
                audio = await AnnouncementService.SynthesizeThroughGateAsync(_elevenLabs, profile, line.Text, reuseSaved: false);
            }

            // Anything held in memory for the race goes, so the new take is
            // what is read back when the line is next wanted.
            Announcer?.ForgetPreparedAudio();
            Status($"Re-rolled a saved line ({Plural.Of(cost, "character")}): \"{line.Text}\"");

            if (Gone)
            {
                return;
            }

            await PlayTestAudioAsync(audio, profile.Volume);

            if (!Gone)
            {
                RerollSays($"New take kept - {Plural.Of(cost, "character")}. The race plays this one now.", AppTheme.Success);
            }
        }
        catch (Exception ex)
        {
            if (!Gone)
            {
                RerollSays($"No new take: {SummariseApiError(ex.Message)} The old take is kept.", AppTheme.Danger);
            }
        }
        finally
        {
            _heatCallerHearingRunning = false;

            if (!Gone)
            {
                PreservingScroll(() => _btnRerollLine.Enabled = _cmbSavedLines.Items.Count > 0);
            }

            RequestLibraryRefresh();
        }
    }
}
