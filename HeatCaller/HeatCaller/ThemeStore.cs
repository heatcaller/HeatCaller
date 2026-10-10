using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeatCaller;

/// <summary>
/// Loads/saves the list of themes to %AppData%\HeatCaller\themes.json.
/// Seeds the built-in starter themes on first run; on later runs, merges in
/// any built-ins that don't already exist (by name) so upgrading the app to
/// a version with new starter themes doesn't require deleting this file.
/// User-created custom themes are left alone either way.
/// </summary>
public sealed class ThemeStore
{
    private readonly List<Theme> _themes;

    public IReadOnlyList<Theme> All => _themes.OrderByDescending(t => t.IsBuiltIn).ThenBy(t => t.Name).ToList();

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "themes.json");

    private ThemeStore(List<Theme> themes)
    {
        _themes = themes;
    }

    public static ThemeStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<Theme>>(json);
                if (loaded is not null)
                {
                    loaded.RemoveAll(t => t is null);

                    // Applying a theme copies its sounds into the live
                    // settings, so they get the same check as those do.
                    foreach (var theme in loaded)
                    {
                        // A hand-edited or restored file can say "Voice": null
                        // or "SoundEffects": null. Apply copies both straight
                        // into the live settings, and threw on a null one -
                        // on the Soundscapes page, or from a venue picking
                        // this soundscape. A missing half is the default.
                        theme.Id ??= Guid.NewGuid().ToString("N");
                        theme.Name ??= string.Empty;
                        theme.Description ??= string.Empty;
                        theme.SoundPack ??= string.Empty;
                        theme.Voice ??= VoiceProfile.CreateDefault();
                        theme.Voice.FillNulls();
                        foreach (var (key, config) in VoiceProfile.CreateDefault().Events)
                        {
                            // What the voice settings' own load does for a
                            // kind of line it has never seen - and for a null.
                            if (!theme.Voice.Events.TryGetValue(key, out var existing) || existing is null)
                            {
                                theme.Voice.Events[key] = config;
                            }
                        }

                        theme.SoundEffects ??= SoundEffectsProfile.CreateDefault();
                        SoundEffectsStore.FillMissing(theme.SoundEffects);

                        SoundEffectsStore.ClearNetworkPaths(theme.SoundEffects);

                        // Themes never carry the ElevenLabs key. Older
                        // versions could write it into this file in plain
                        // text; it is not taken from here (the voice
                        // settings are the one place the key lives), only
                        // dropped, and the file re-saved below without it.
                        theme.Voice?.ForgetApiKey();
                    }

                    var hadKey = ThemesFileHasApiKey(json);

                    var added = false;

                    // Built-ins saved before their ids were fixed carry a
                    // random one - see Theme.BuiltInId. They are re-id'd
                    // below, by name, so this launch and every later one
                    // agree about which soundscape is which. Each old id is
                    // remembered with the id that replaces it, so the
                    // "applied" marker can be carried across below rather
                    // than lost.
                    var reIded = new Dictionary<string, string>(StringComparer.Ordinal);

                    // Built-ins from an older version are about to be
                    // removed and replaced by this version's fresh copies -
                    // and the one the club applied may be among them. The
                    // map has to be filled for those BEFORE they go: once
                    // removed they never reach the re-id loop, and the
                    // marker would keep pointing at an id nothing has any
                    // more - exactly the silent "Theme music stopped working
                    // after an update" Theme.BuiltInId was written to end.
                    // A built-in that was renamed when the packs arrived
                    // follows its new name. Only mapped to an id this
                    // version actually ships; one that was dropped has
                    // nothing to carry the marker to.
                    var shippedIds = Theme.CreateBuiltInDefaults().Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

                    foreach (var old in loaded.Where(t => t.IsBuiltIn && t.BuiltInVersion < Theme.CurrentBuiltInVersion))
                    {
                        var name = Theme.RenamedBuiltIns.TryGetValue(old.Name, out var renamed) ? renamed : old.Name;
                        var stable = Theme.BuiltInId(name);

                        if (shippedIds.Contains(stable) && !string.Equals(old.Id, stable, StringComparison.Ordinal))
                        {
                            reIded[old.Id] = stable;
                        }
                    }

                    // Built-ins from an older version are replaced
                    // wholesale by this version's - see
                    // Theme.CurrentBuiltInVersion. They can't be edited on
                    // screen, so there is nothing of the club's in them; its
                    // own copies are custom themes and stay as they are.
                    if (loaded.RemoveAll(t => t.IsBuiltIn && t.BuiltInVersion < Theme.CurrentBuiltInVersion) > 0)
                    {
                        added = true;
                    }

                    foreach (var theme in loaded.Where(t => t.IsBuiltIn))
                    {
                        var stable = Theme.BuiltInId(theme.Name);

                        if (!string.Equals(theme.Id, stable, StringComparison.Ordinal))
                        {
                            reIded[theme.Id] = stable;
                            theme.Id = stable;
                            added = true;
                        }
                    }

                    // By ID, not by name. Name was wrong twice over: a club's
                    // own soundscape called "Arcade" silently stopped the
                    // built-in Arcade from ever being added, and - now that
                    // built-ins keep a fixed id - matching on name would let
                    // a renamed built-in be added a second time alongside
                    // itself.
                    var existingIds = loaded.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

                    foreach (var builtIn in Theme.CreateBuiltInDefaults())
                    {
                        if (!existingIds.Contains(builtIn.Id))
                        {
                            loaded.Add(builtIn);
                            added = true;
                        }
                    }

                    // Only rewritten when an upgrade actually brought new
                    // starter themes, or a key needs purging from the file
                    // - there's nothing to save otherwise.
                    var store = new ThemeStore(loaded);
                    if (added || hadKey)
                    {
                        store.Save();
                    }

                    // The applied marker follows a re-id. Without this the
                    // one upgrade that fixes the ids would itself be the
                    // upgrade that loses the club's applied soundscape.
                    if (store.ActiveThemeId is { } active
                        && reIded.TryGetValue(active, out var movedTo))
                    {
                        try
                        {
                            SafeFile.WriteAllText(ActiveFilePath, movedTo);
                        }
                        catch
                        {
                            // A marker on a card is not worth an error - see
                            // ActiveThemeId.
                        }
                    }
                    return store;
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start with the built-ins rather than
            // crashing on launch.
            //
            // Not saved here. The built-ins are all a fresh store holds, and
            // writing them now would replace the user's custom themes the
            // moment the app opened - before anyone had seen the message
            // saying the file couldn't be read. When the file was damaged
            // and a copy of it was kept, they're written on the first real
            // change instead. When it could not be read at all, or no copy
            // could be kept, nothing is written this session (see
            // LoadedUnreadable).
            var hold = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
            return new ThemeStore(Theme.CreateBuiltInDefaults()) { LoadedUnreadable = hold };
        }

        // First run: seed the file with the starter themes.
        var fresh = new ThemeStore(Theme.CreateBuiltInDefaults());
        fresh.Save();
        return fresh;
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds every soundscape
    /// the club has made. It used to be loaded as the built-ins alone and
    /// then written over whole at the first edit - Customize on a built-in,
    /// or Save current settings as new - so a backup or sync tool holding
    /// it open for a moment at launch was enough to lose every custom
    /// soundscape, with no copy kept. What this store holds is then the
    /// built-ins plus whatever was made this session, and none of it is
    /// saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // the built-ins over it would replace every custom soundscape. The
        // console has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        var options = new JsonSerializerOptions { WriteIndented = true };

        // Every theme's voice is already key-less (see Load, Apply and
        // SnapshotCurrent), but the file is the one thing that gets copied
        // around and shared, so the key fields are also struck from the
        // text itself. Done on the JSON rather than on the Theme objects,
        // so this can never blank a key on a profile somebody else holds.
        var node = JsonSerializer.SerializeToNode(_themes, options);
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject theme && theme["Voice"] is JsonObject voice)
                {
                    voice.Remove(nameof(VoiceProfile.ApiKey));
                    voice.Remove(nameof(VoiceProfile.ApiKeySealed));
                }
            }
        }

        var json = node is null ? "[]" : node.ToJsonString(options);
        SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// True when any theme in the file's text still holds an ElevenLabs key
    /// - plain text from an older version, or a sealed one - so the file
    /// is re-saved without it.
    /// </summary>
    private static bool ThemesFileHasApiKey(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var theme in document.RootElement.EnumerateArray())
            {
                if (theme.ValueKind != JsonValueKind.Object
                    || !theme.TryGetProperty(nameof(Theme.Voice), out var voice)
                    || voice.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (voice.TryGetProperty(nameof(VoiceProfile.ApiKey), out _))
                {
                    return true;
                }

                if (voice.TryGetProperty(nameof(VoiceProfile.ApiKeySealed), out var sealedKey)
                    && sealedKey.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(sealedKey.GetString()))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Deserialize already succeeded on this text; nothing to find.
        }

        return false;
    }

    public Theme? Get(string id) => _themes.FirstOrDefault(t => t.Id == id);

    private static string ActiveFilePath => Path.Combine(
        AppPaths.Root,
        "soundscape-active.txt");

    /// <summary>
    /// The soundscape applied last - on the Soundscapes page or by a venue -
    /// so its card can say so. Kept in a file of its own rather than in
    /// themes.json, which is a plain list other versions read.
    ///
    /// "Last applied", not "in use": any setting changed on Voice or Sound
    /// Effects since then is the club's own, and nothing here tracks that.
    /// </summary>
    public string? ActiveThemeId
    {
        get
        {
            try
            {
                if (File.Exists(ActiveFilePath))
                {
                    var id = SafeFile.ReadAllTextWithRetry(ActiveFilePath).Trim();
                    return string.IsNullOrEmpty(id) ? null : id;
                }
            }
            catch
            {
                // A marker on a card is not worth an error.
            }

            return null;
        }
    }

    /// <summary>Records a soundscape as the last applied. Call after <see cref="Apply"/>.</summary>
    public void MarkApplied(Theme theme)
    {
        try
        {
            SafeFile.WriteAllText(ActiveFilePath, theme.Id);
        }
        catch
        {
            // See ActiveThemeId.
        }
    }

    /// <summary>
    /// Records that no soundscape is in use - the sound was set to lap times
    /// only (see LapTimesOnly). Theme music then has nothing to play, which
    /// is meant: lap times only has no music.
    /// </summary>
    public void ClearApplied()
    {
        try
        {
            if (File.Exists(ActiveFilePath))
            {
                File.Delete(ActiveFilePath);
            }
        }
        catch
        {
            // See ActiveThemeId.
        }
    }

    /// <summary>
    /// A theme by name, following a built-in that was renamed (a venue may
    /// still remember "Pro Broadcast" - that is now "Broadcast").
    /// </summary>
    public Theme? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = _themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return Theme.RenamedBuiltIns.TryGetValue(name, out var renamed)
            ? _themes.FirstOrDefault(t => t.IsBuiltIn && string.Equals(t.Name, renamed, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    public void Upsert(Theme theme)
    {
        var index = _themes.FindIndex(t => t.Id == theme.Id);
        if (index >= 0)
        {
            _themes[index] = theme;
        }
        else
        {
            _themes.Add(theme);
        }
        Save();
    }

    public void Delete(string id)
    {
        _themes.RemoveAll(t => t.Id == id);
        Save();
    }

    /// <summary>
    /// Raised after an undo has changed the soundscapes, so the Soundscapes
    /// page re-reads them. True when the undo also put back the live voice,
    /// sound and ambience settings (an Apply undone), so the host rebuilds
    /// the pages that show those. Not raised by ordinary saves.
    /// </summary>
    public event Action<bool>? Restored;

    /// <summary>Tells whatever shows soundscapes that an undo just changed them. UI thread.</summary>
    public void NotifyRestored(bool liveSettingsRestored) => Restored?.Invoke(liveSettingsRestored);

    /// <summary>
    /// Takes everything <see cref="Apply"/> is about to overwrite and returns
    /// the action that puts it back, for Undo. Call it immediately before
    /// Apply - on the Soundscapes page, by a venue with a preferred
    /// soundscape, and by the setup wizards. With no theme (lap times only,
    /// which is no soundscape - see LapTimesOnly) the ambience is left out.
    ///
    /// What comes back: the announcer's lines, delivery, voice, model and
    /// language; every sound effect; the soundscape crowd layers in Ambience
    /// and its on/off switch (only when this soundscape touches Ambience);
    /// and which soundscape was last applied.
    ///
    /// What doesn't: the things Apply itself leaves as this PC has them - the
    /// API key, the voice and sound volumes, the wireless speaker, the output
    /// format and which engine speaks - are taken from whatever they are when
    /// Undo is pressed, so undoing an apply never reverts a level set on the
    /// Mixer since. The club's own ambience layers are never touched either.
    ///
    /// Works on the stores only, never on a page. The caller refreshes the
    /// pages afterwards (the Soundscapes page through NotifyRestored(true),
    /// which the host answers as it does an Apply).
    ///
    /// The music is left out on purpose: Apply no longer changes it (see
    /// ApplyBackground) - Theme music follows the last-applied soundscape,
    /// which this does put back.
    /// </summary>
    public static Action CaptureForUndo(
        Theme? theme,
        VoiceSettingsStore voiceStore,
        SoundEffectsStore soundStore,
        AmbienceStore? ambienceStore = null)
    {
        // Serialised copies, so nothing that changes the live objects later
        // can reach into what the undo puts back.
        var voiceJson = JsonSerializer.Serialize(voiceStore.Current);
        var soundJson = JsonSerializer.Serialize(soundStore.Current);

        string? activeBefore = null;
        try
        {
            activeBefore = File.Exists(ActiveFilePath) ? SafeFile.ReadAllTextWithRetry(ActiveFilePath).Trim() : null;
        }
        catch
        {
            // A marker on a card is not worth an error - see ActiveThemeId.
        }

        // Ambience only when this soundscape changes it: putting back the
        // crowd layers of a soundscape that never touched them would undo
        // whatever the club did to them since, for nothing.
        var touchesAmbience = ambienceStore is not null && theme?.Background?.AmbienceBed is not null;
        var crowdBefore = touchesAmbience
            ? ambienceStore!.Current.Layers
                .Select((layer, index) => (Index: index, Layer: layer.Clone()))
                .Where(x => x.Layer.Key.StartsWith(SoundPacks.BedLayerKeyPrefix, StringComparison.Ordinal))
                .ToList()
            : new List<(int Index, AmbienceLayer Layer)>();
        var ambienceEnabledBefore = ambienceStore?.Current.Enabled ?? false;
        var crowdVersionBefore = ambienceStore?.Current.SoundscapeCrowdVersion ?? 0;

        return () =>
        {
            // --- Voice: the style back, this PC's own settings as they are now.
            var voice = JsonSerializer.Deserialize<VoiceProfile>(voiceJson);
            if (voice is not null)
            {
                var now = voiceStore.Current;
                voice.CopyApiKeyFrom(now);
                voice.Volume = now.Volume;
                voice.WirelessSpeaker = now.WirelessSpeaker;
                voice.OutputFormat = now.OutputFormat;
                voice.Engine = now.Engine;

                // WindowsVoiceName was copied here too, until the Windows
                // voice was retired on 10 Oct 2026 and the property went with
                // it. WindowsVoiceStandsIn is now KokoroStandsIn - the same
                // question with a different voice answering it, which is why
                // it still serialises under the old name.
                voice.KokoroStandsIn = now.KokoroStandsIn;
                voice.KokoroVoiceName = now.KokoroVoiceName;
                voice.HeatCallerVoice = now.HeatCallerVoice;
                voice.HeatCallerVoiceName = now.HeatCallerVoiceName;
                voice.HeatCallerRev = now.HeatCallerRev;
                voiceStore.Save(voice);
            }

            // --- Sound effects: every row back, at today's Mixer level.
            var sound = JsonSerializer.Deserialize<SoundEffectsProfile>(soundJson);
            if (sound is not null)
            {
                sound.Volume = soundStore.Current.Volume;
                soundStore.Save(sound);
            }

            // --- Ambience: the soundscape's pair out, the old pair back where
            // it was. Changed in place, like Apply - the player and the
            // Ambience page hold on to this object.
            if (touchesAmbience && ambienceStore is not null)
            {
                var settings = ambienceStore.Current;
                settings.Layers.RemoveAll(l => l.Key.StartsWith(SoundPacks.BedLayerKeyPrefix, StringComparison.Ordinal));

                foreach (var (index, layer) in crowdBefore.OrderBy(x => x.Index))
                {
                    settings.Layers.Insert(Math.Clamp(index, 0, settings.Layers.Count), layer.Clone());
                }

                settings.Enabled = ambienceEnabledBefore;
                settings.SoundscapeCrowdVersion = crowdVersionBefore;
                ambienceStore.Save();
            }

            // --- Which soundscape was last applied, for its card and for
            // Theme music.
            try
            {
                if (string.IsNullOrEmpty(activeBefore))
                {
                    if (File.Exists(ActiveFilePath))
                    {
                        File.Delete(ActiveFilePath);
                    }
                }
                else
                {
                    SafeFile.WriteAllText(ActiveFilePath, activeBefore);
                }
            }
            catch
            {
                // See ActiveThemeId.
            }
        };
    }

    /// <summary>
    /// Applies a theme's voice and sound settings as the app's current,
    /// active settings. The current ElevenLabs API key is preserved
    /// regardless of what's in the theme - themes describe personality,
    /// not credentials, so switching themes never touches it.
    ///
    /// Nor does a theme touch how THIS PC plays: its playback volume, the
    /// wireless-speaker setting, the ElevenLabs output format (a question of
    /// the account's plan), the language unless the theme sets one, and
    /// whether it speaks with ElevenLabs or the Windows voice. Venues apply a
    /// preferred theme through here too, so the same rule holds for them.
    /// </summary>
    public static void Apply(
        Theme theme,
        VoiceSettingsStore voiceStore,
        SoundEffectsStore soundStore,
        AmbienceStore? ambienceStore = null,
        MusicStore? musicStore = null)
    {
        var voiceCopy = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(theme.Voice))!;
        var current = voiceStore.Current;

        // The key as it stands, locked or not - never the theme's.
        voiceCopy.CopyApiKeyFrom(current);

        // The voice itself stays too, unless the theme brings one of its
        // own. The built-in themes have no voice in them - the app can't
        // know which voices are on somebody's ElevenLabs account - and
        // copying their blank voice over the real one left nothing to speak
        // with until a voice was picked again, while their stock model
        // quietly undid a model change. A custom theme saved with a voice
        // picked carries that voice on purpose, and gets it, together with
        // the model it was saved with.
        if (string.IsNullOrWhiteSpace(voiceCopy.VoiceId))
        {
            voiceCopy.VoiceId = current.VoiceId;
            voiceCopy.VoiceName = current.VoiceName;
            voiceCopy.ModelId = current.ModelId;
        }

        // This PC's playback, and its account's output format, stay as they
        // are. Applying a style used to reset all of them to whatever the
        // style was saved with (the built-in styles: full volume, wireless
        // on, default format) - a style is how the race sounds, not how the
        // speakers here are set up.
        voiceCopy.Volume = current.Volume;
        voiceCopy.WirelessSpeaker = current.WirelessSpeaker;
        voiceCopy.OutputFormat = current.OutputFormat;

        // The language follows the same rule as the voice: kept, unless the
        // style carries one of its own. The built-in styles have none.
        if (string.IsNullOrWhiteSpace(voiceCopy.LanguageCode))
        {
            voiceCopy.LanguageCode = current.LanguageCode;
        }

        // And a style never moves a club between ElevenLabs, the HeatCaller
        // voice and the free one - every style has one of these saved in it
        // (the built-in ones were written when that meant the Windows voice,
        // and a saved engine number of 1 now reads as Kokoro - see
        // VoiceEngines.RetiredWindows), and applying it would otherwise
        // switch the club over. The HeatCaller voice's own choice is kept for
        // the same reason: it is the club's, and no style knows about it.
        voiceCopy.Engine = current.EffectiveEngine;

        // WindowsVoiceName was kept here too, until the Windows voice went on
        // 10 Oct 2026. KokoroStandsIn is the old WindowsVoiceStandsIn under a
        // new name, and is the club's choice rather than the style's.
        voiceCopy.KokoroStandsIn = current.KokoroStandsIn;
        voiceCopy.KokoroVoiceName = current.KokoroVoiceName;
        voiceCopy.HeatCallerVoice = current.HeatCallerVoice;
        voiceCopy.HeatCallerVoiceName = current.HeatCallerVoiceName;
        voiceCopy.HeatCallerRev = current.HeatCallerRev;

        // And on the HeatCaller voice a style leaves the WORDING alone too.
        //
        // Those lines are fixed while that voice speaks - Voice Settings
        // makes them read-only - because the account behind it is
        // HeatCaller's. A style is the one thing that could put other words
        // in behind that, and a custom style is words somebody typed: a club
        // could have saved its own lines as a style first and applied them
        // here. Everything else a style carries - the sounds, the speeds,
        // the look - applies as it always has.
        if (current.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            voiceCopy.Events = current.Events;
        }

        // Stamped current. A style saved by an older version carries the
        // line version it was saved with, and saved as it was, the next
        // launch took the settings for ones written before the current
        // built-in lines and replaced every line with the defaults - so the
        // lines the style was applied for were gone after a restart. The
        // style's lines are the ones wanted; that is what applying it means.
        voiceCopy.TemplateVersion = VoiceSettingsStore.CurrentTemplateVersion;

        voiceStore.Save(voiceCopy);

        var soundCopy = JsonSerializer.Deserialize<SoundEffectsProfile>(JsonSerializer.Serialize(theme.SoundEffects))!;

        // The sound effects level is this PC's too - it is the Mixer's
        // fader - so it stays put for the same reason the voice volume does.
        soundCopy.Volume = soundStore.Current.Volume;

        soundStore.Save(soundCopy);

        // The music store is no longer changed here - see ApplyBackground.
        // It stays a parameter so the callers (the Soundscapes page and
        // venues) keep one call for the lot.
        if (theme.Background is { } background)
        {
            ApplyBackground(background, ambienceStore);
        }
    }

    /// <summary>
    /// Puts a soundscape's crowd into Ambience - see SoundscapeBackground for
    /// what is and isn't touched. The settings object is changed in place,
    /// since the ambience player and its page hold on to it.
    ///
    /// The music is NOT touched any more. Applying a soundscape used to
    /// switch every library track off except the soundscape's own, so the
    /// club's playlist ticks were overwritten and its music effectively
    /// became the one theme track. Theme music is now a mode of the Music
    /// page (MusicSettings.PlayThemeMusic): the player reads the applied
    /// soundscape's tracks from ActiveThemeMusic, and the club's ticks are
    /// left exactly as it set them.
    /// </summary>
    private static void ApplyBackground(SoundscapeBackground background, AmbienceStore? ambienceStore)
    {
        if (ambienceStore is null || background.AmbienceBed is not { } bed)
        {
            return;
        }

        var settings = ambienceStore.Current;
        var layers = settings.Layers;

        // Only layers a soundscape put there - its bed and its crowd roar
        // both carry the prefix - so re-applying replaces the pair and the
        // club's own layers stay.
        var removed = layers.RemoveAll(l => l.Key.StartsWith(SoundPacks.BedLayerKeyPrefix, StringComparison.Ordinal));

        var added = new[] { SoundPacks.BedLayer(bed), SoundPacks.RoarLayer(bed) }
            .Where(l => l is not null)
            .Select(l => l!)
            .ToList();

        layers.InsertRange(0, added);

        // Put there by this version, so already up to date - the start-up
        // check must not later add back a roar the club has since deleted.
        if (added.Any(l => l.Key.EndsWith(SoundPacks.RoarLayerKeySuffix, StringComparison.Ordinal)))
        {
            settings.SoundscapeCrowdVersion = AmbienceStore.CurrentSoundscapeCrowdVersion;
        }

        // Switched on, so the crowd is actually heard. A soundscape used to
        // leave this as it was, and a bed added to an Ambience that was off
        // simply never played - the soundscape sounded like it had no crowd
        // at all. This does not START anything: ambience plays when a
        // session brings it up (Race Day's audio options) or it is started
        // on the Ambience page, as before.
        //
        // Unless the club has set Play to "My ambience": these beds are then
        // silent by their own instruction, and switching ambience on for
        // them would be the same lie pointing the other way - the feature
        // on, a bed present, nothing audible.
        //
        // The Play choice itself is left exactly as they set it. Moving it
        // back to Both here would be a soundscape quietly overruling a
        // decision made on purpose, which is worse than a crowd that waits
        // until it is asked for.
        if (added.Count > 0 && added.Any(l => l.PlaysUnder(settings.Source)))
        {
            settings.Enabled = true;
        }

        if (removed > 0 || added.Count > 0)
        {
            ambienceStore.Save();
        }
    }

    /// <summary>
    /// What the Music page's "Theme music" plays: the last applied
    /// soundscape's tracks, by title, with its name for saying so. Null when
    /// no soundscape has been applied (or the one applied has since been
    /// deleted); an empty title list when it has no music of its own - Pit
    /// Lane, or a custom one set to leave the music alone.
    /// </summary>
    public ThemeMusicSource? ActiveThemeMusic()
    {
        if (ActiveThemeId is not { } id || Get(id) is not { } theme)
        {
            return null;
        }

        var titles = theme.Background?.MusicPlaylist ?? new List<string>();
        // Trimmed, because the library side is: a title saved as "Neon Grid "
        // matched no track, and Theme music quietly played the club's own.
        return new ThemeMusicSource(theme.Name, titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList());
    }

    /// <summary>What a soundscape's background would do on this PC, in a sentence for the Apply confirmation. Empty when it has none.</summary>
    public static string DescribeBackground(Theme theme, MusicStore? musicStore)
    {
        if (theme.Background is not { } background)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (background.AmbienceBed is { } bed)
        {
            if (SoundPacks.Find(bed) is not { } pack)
            {
                parts.Add("any soundscape crowd is taken out of Ambience");
            }
            else if (SoundPacks.AmbienceFileFor(pack.Key) is null && SoundPacks.CrowdRoarFileFor(pack.Key) is null)
            {
                parts.Add($"the {pack.Name} crowd would go into Ambience, but its sound files aren't installed on this PC");
            }
            else
            {
                parts.Add($"the {pack.Name} crowd is added to Ambience - a low murmur all night, "
                          + "with a roar that swells at the big moments - and Ambience is switched on "
                          + "(your own ambience layers stay as they are)");
            }
        }

        if (background.MusicPlaylist is { Count: > 0 } && musicStore is not null)
        {
            var have = musicStore.Current.Tracks
                .Where(t => background.MusicPlaylist.Contains(t.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase))
                .Select(t => t.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mode = musicStore.Current.PlayThemeMusic == true
                ? "the Music page is set to play theme music"
                : "the Music page is set to play your music - switch it to Theme music to hear this";

            parts.Add(have.Count == 0
                ? "its music isn't in your library, so theme music will fall back to your own"
                : have.Count == 1
                    ? $"its theme music is \"{have[0]}\" ({mode}; your own playlist ticks are left as they are)"
                    : $"its theme music is its {have.Count} tracks ({mode}; your own playlist ticks are left as they are)");
        }
        else if (musicStore is not null)
        {
            parts.Add("it has no music of its own, so theme music falls back to your own music");
        }

        return parts.Count == 0
            ? string.Empty
            : "Underneath: " + string.Join("; ", parts) + ". Whether music plays stays as you have it.";
    }

    /// <summary>Captures the CURRENT active voice/sound settings into a theme snapshot - used both for creating a new custom theme and for "update this theme from current settings". The API key is stripped so it's never written into a theme file.</summary>
    public static Theme SnapshotCurrent(VoiceSettingsStore voiceStore, SoundEffectsStore soundStore, string name, string description)
    {
        var voiceCopy = JsonSerializer.Deserialize<VoiceProfile>(JsonSerializer.Serialize(voiceStore.Current))!;

        // ForgetApiKey rather than blanking ApiKey: a key still locked to
        // another PC would otherwise ride along as sealed text.
        voiceCopy.ForgetApiKey();

        var soundCopy = JsonSerializer.Deserialize<SoundEffectsProfile>(JsonSerializer.Serialize(soundStore.Current))!;

        return new Theme
        {
            Name = name,
            Description = description,
            Voice = voiceCopy,
            SoundEffects = soundCopy,
        };
    }
}
