namespace HeatCaller;

/// <summary>
/// Drivers' own crew, on the main window's side (15 Nov 2026, driver crew
/// plan): the voices a phone may offer, kept here once listed so the
/// driverme document and a crew ask from a phone both answer from the same
/// list, and listed again when the Cast tab changes what is offered or the
/// engine changes under it.
/// </summary>
public sealed partial class MainForm
{
    /// <summary>The engine and voices last listed for drivers, or null before the first listing (or with drivers' crews off).</summary>
    private DriverLive.CrewForPhone? _crewForPhone;

    /// <summary>The engine <see cref="_crewForPhone"/> was listed for - a switch on Voice Settings lists again.</summary>
    private VoiceEngine? _crewListedFor;

    /// <summary>Set while a listing is under way, so a second one is not started on top of it.</summary>
    private bool _crewListing;

    /// <summary>When the voices were last listed, or tried - see CrewListingDue.</summary>
    private DateTime _crewListedUtc = DateTime.MinValue;

    /// <summary>Whether the last try to list them failed, so it is tried again sooner.</summary>
    private bool _crewListFailed;

    /// <summary>Every voice the engine had at the last good listing, hidden ones included, as keys - what "still there" is checked against.</summary>
    private HashSet<string>? _crewOnAccount;

    /// <summary>
    /// How often the voices are listed again (15 Nov 2026): a voice added to
    /// the ElevenLabs account reaches the phones, and one deleted from it
    /// stops being used, within ten minutes.
    /// </summary>
    private static readonly TimeSpan CrewListEvery = TimeSpan.FromMinutes(10);

    /// <summary>How soon a listing that failed (no network, say) is tried again.</summary>
    private static readonly TimeSpan CrewListRetry = TimeSpan.FromMinutes(1);

    /// <summary>A new listing is due: another engine, the last one is old, or it failed a minute ago.</summary>
    private bool CrewListingDue(VoiceEngine engine, DateTime nowUtc) =>
        _crewListedFor != engine
        || nowUtc - _crewListedUtc >= (_crewListFailed ? CrewListRetry : CrewListEvery);

    /// <summary>
    /// Whether a driver's picked voice is still on the engine's list, for the
    /// interviews (RaceControlControl.CrewVoiceStillThere). True when there is
    /// no good listing for the engine in use yet - not knowing is not a
    /// reason to take a driver's pick away.
    /// </summary>
    internal bool CrewVoiceStillOffered(CrewVoicePick pick) =>
        _crewOnAccount is not { } keys || _crewListedFor != DriverCrew.EngineOf(pick) || keys.Contains(DriverCrew.KeyOf(pick));

    /// <summary>
    /// What driverme tells the phones about the crew: null with drivers'
    /// crews off, and - when the engine has changed since the last listing
    /// - starts a listing for the new one and gives what it has until then.
    /// </summary>
    private DriverLive.CrewForPhone? CrewForPhoneNow()
    {
        if (!_settings.DriversPickCrew)
        {
            return null;
        }

        var engine = _voiceStore.Current.EffectiveEngine;

        if (CrewListingDue(engine, DateTime.UtcNow))
        {
            _ = ListCrewVoicesAsync();
        }

        return _crewForPhone is { } listed && listed.Engine == engine
            ? listed with { JuniorNotesFromPhone = _settings.JuniorNotesFromPhone }
            : new DriverLive.CrewForPhone(engine, Array.Empty<CrewVoiceOption>(), _settings.JuniorNotesFromPhone);
    }

    /// <summary>
    /// Lists the voices drivers may pick on the engine in use, hidden and
    /// reserved ones left out. A failure (no key, no network) is said in the
    /// console and leaves what was listed before.
    /// </summary>
    private async Task ListCrewVoicesAsync()
    {
        if (_crewListing || !_settings.DriversPickCrew)
        {
            return;
        }

        _crewListing = true;
        var engine = _voiceStore.Current.EffectiveEngine;

        try
        {
            var all = await CrewVoicesOfEngineAsync(engine);
            var hidden = new HashSet<string>(_settings.CrewVoicesHidden ?? new List<string>(), StringComparer.Ordinal);
            var reserved = DriverCrew.Reserved(_settings, _announcer.AnnouncerCrewKey(engine), engine);

            _crewForPhone = new DriverLive.CrewForPhone(engine, DriverCrew.Pickable(all, hidden, reserved));
            _crewOnAccount = all.Select(v => DriverCrew.KeyOf(v.Pick)).ToHashSet(StringComparer.Ordinal);
            _crewListedFor = engine;
            _crewListFailed = false;
            _crewListedUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Tried again in a minute, not every few seconds. What was listed
            // before stays, so phones keep the voices they had.
            _crewListedFor = engine;
            _crewListFailed = true;
            _crewListedUtc = DateTime.UtcNow;
            OnClientRawMessage($"{AppTime.LogStampNow()}  [crew] The voices drivers can pick could not be listed - {ex.Message}.");
        }
        finally
        {
            _crewListing = false;
        }
    }

    /// <summary>The Cast tab changed what drivers may pick: list again, now.</summary>
    private void CrewSettingsChanged()
    {
        _crewListedFor = null;
        _crewListedUtc = DateTime.MinValue;
        _ = ListCrewVoicesAsync();
    }

    /// <summary>
    /// The voices drivers could be offered on the engine in use, for the
    /// Cast tab's list - the announcer's and co-hosts' already left out,
    /// the hidden ones still in (the list is where they are unhidden).
    /// </summary>
    private async Task<IReadOnlyList<CrewVoiceOption>> CrewVoicesForCastAsync()
    {
        var engine = _voiceStore.Current.EffectiveEngine;
        var reserved = DriverCrew.Reserved(_settings, _announcer.AnnouncerCrewKey(engine), engine);
        var all = await CrewVoicesOfEngineAsync(engine);

        return DriverCrew.Pickable(all, Array.Empty<string>(), reserved);
    }

    /// <summary>Every voice an engine has, with its sample when it has one. Throws with a sentence the page can show.</summary>
    private async Task<IReadOnlyList<CrewVoiceOption>> CrewVoicesOfEngineAsync(VoiceEngine engine)
    {
        var word = DriverCrew.EngineWord(engine);

        switch (engine)
        {
            case VoiceEngine.Kokoro:
                return KokoroSpeech.InstalledVoices()
                    .Select(v => new CrewVoiceOption(new CrewVoicePick { Engine = word, Id = v.Name, Name = v.Display }, string.Empty))
                    .ToList();

            case VoiceEngine.HeatCaller:
            {
                var catalogue = await _heatCallerVoice.CatalogueAsync();

                if (!catalogue.Enabled)
                {
                    throw new InvalidOperationException("the HeatCaller voice isn't included for this club yet");
                }

                return catalogue.Voices
                    .Select(v => new CrewVoiceOption(new CrewVoicePick { Engine = word, Id = v.Id, Name = v.Name, Rev = v.Rev }, string.Empty))
                    .ToList();
            }

            default:
            {
                var key = _voiceStore.Current.ApiKey;

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("there's no ElevenLabs API key on the Voice tab");
                }

                var voices = await new ElevenLabsClient().GetVoicesAsync(key);

                return voices
                    .Select(v => new CrewVoiceOption(new CrewVoicePick { Engine = word, Id = v.VoiceId, Name = v.Name }, v.PreviewUrl))
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Tonight's racing is done: races were run tonight and Race Control has
    /// none left to run (15 Nov 2026) - a goal sent now is for the next night.
    /// </summary>
    private bool TonightsRacingIsOver()
    {
        if (_raceControlControl is not { } raceControl || raceControl.UpcomingRaces(1).Count > 0)
        {
            return false;
        }

        return TonightAtTheTrack.Races(_sessionHistory.All, id => _rounds.Get(id), DateTime.Now).Any();
    }

    /// <summary>
    /// A voice key a phone sent, as the voice it means on the engine in use,
    /// or null when it is not one drivers may pick now (hidden, reserved,
    /// on another engine, or not listed yet).
    /// </summary>
    internal CrewVoicePick? CrewVoiceForKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || CrewForPhoneNow() is not { } crew)
        {
            return null;
        }

        return crew.Voices.FirstOrDefault(v => DriverCrew.KeyOf(v.Pick) == key.Trim())?.Pick.Copy();
    }
}
