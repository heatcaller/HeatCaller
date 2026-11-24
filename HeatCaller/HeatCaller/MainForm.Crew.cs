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
    internal bool CrewVoiceStillOffered(CrewVoicePick pick) => CrewVoiceStillOffered(_crewOnAccount, _crewListedFor, pick);

    /// <summary>CrewVoiceStillOffered against the keys and engine given - for tests.</summary>
    internal static bool CrewVoiceStillOffered(HashSet<string>? onAccount, VoiceEngine? listedFor, CrewVoicePick pick) =>
        onAccount is not { } keys || listedFor != DriverCrew.EngineOf(pick) || keys.Contains(DriverCrew.KeyOf(pick));

    /// <summary>
    /// An ElevenLabs account's voices with HeatCaller's six after them
    /// (24 Nov 2026, Asher), those the account already has left out - so a
    /// driver can pick one without the club saving it on elevenlabs.io first.
    /// No sample: the phone has nothing to play for a voice the account
    /// hasn't saved.
    /// </summary>
    internal static IReadOnlyList<CrewVoiceOption> CrewVoicesWithSuggested(IEnumerable<CrewVoiceOption> account)
    {
        var list = account.ToList();
        var word = DriverCrew.EngineWord(VoiceEngine.ElevenLabs);

        foreach (var (_, voice) in RetiredVoices.Suggested)
        {
            if (!list.Any(v => string.Equals(v.Pick.Id, voice.Id, StringComparison.Ordinal)))
            {
                list.Add(new CrewVoiceOption(new CrewVoicePick { Engine = word, Id = voice.Id, Name = voice.Name }, string.Empty));
            }
        }

        return list;
    }

    /// <summary>
    /// The keys a driver's pick is checked against (CrewVoiceStillOffered):
    /// every voice listed and, on ElevenLabs, every Library voice HeatCaller
    /// suggests or moves a retired voice to (RetiredVoices.Offered).
    /// </summary>
    /// <remarks>
    /// WHY (24 Nov 2026): a crew pick moved off a retired voice on 1 January
    /// - Jessica to Jade, say - is a Library voice the account never saved,
    /// so it was not on the account's list and read as "no longer on the
    /// engine's list": the driver lost their voice to the club's.
    /// </remarks>
    internal static HashSet<string> CrewKeysStillThere(IEnumerable<CrewVoiceOption> listed, VoiceEngine engine)
    {
        var keys = listed.Select(v => DriverCrew.KeyOf(v.Pick)).ToHashSet(StringComparer.Ordinal);

        if (engine == VoiceEngine.ElevenLabs)
        {
            var word = DriverCrew.EngineWord(engine);

            foreach (var id in RetiredVoices.Offered)
            {
                keys.Add(DriverCrew.KeyOf(new CrewVoicePick { Engine = word, Id = id }));
            }
        }

        return keys;
    }

    /// <summary>
    /// The keys still to check picks against after a listing for
    /// <paramref name="engine"/> failed: those of the last good listing when
    /// it was for the same engine, and none (not known) when it was for
    /// another - see ListCrewVoicesAsync.
    /// </summary>
    internal static HashSet<string>? KeysAfterFailedListing(HashSet<string>? onAccount, VoiceEngine? goodListingFor, VoiceEngine engine) =>
        goodListingFor == engine ? onAccount : null;

    /// <summary>
    /// Whether there is a good listing of the engine in use's voices, for
    /// DriverRequestDesk.CrewVoicesListed (15 Nov 2026, bug hunt 21): until
    /// there is, a driver's pick waits rather than being declined as gone.
    /// Starts a listing when one is due.
    /// </summary>
    internal bool CrewVoicesListedNow()
    {
        _ = CrewForPhoneNow();
        return _crewForPhone is { } listed && listed.Engine == _voiceStore.Current.EffectiveEngine;
    }

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
            ? listed with { Voices = CrewSamples.Marked(listed.Voices, _crewSamplesOnRelay) }
            : new DriverLive.CrewForPhone(engine, Array.Empty<CrewVoiceOption>());
    }

    /// <summary>
    /// The voices the desk may pick for a driver's crew on their profile (19
    /// Nov 2026): the same list their phone is offered, or null with drivers'
    /// crews off or nothing listed yet.
    /// </summary>
    internal IReadOnlyList<CrewVoiceOption>? CrewVoicesForDesk() =>
        CrewForPhoneNow() is { Voices.Count: > 0 } crew ? crew.Voices : null;

    /// <summary>The keys of the crew samples the relay has (CrewSamples), or null before it has been asked. Swapped whole, never changed in place.</summary>
    private volatile IReadOnlySet<string>? _crewSamplesOnRelay;

    /// <summary>Set while samples are being sent, so a second pass is not started on top of it.</summary>
    private int _crewSampling;

    /// <summary>The last sample problem said in the console, so the same one is not said again and again.</summary>
    private string? _crewSampleProblemSaid;

    /// <summary>
    /// Sends the relay a sample of each HeatCaller or Kokoro voice drivers
    /// may pick that it does not have yet (19 Nov 2026, CrewSamples): fetched
    /// or rendered once on this PC and kept, then sent once. In the
    /// background after each listing; a failure is said once and tried again
    /// at the next listing.
    /// </summary>
    private async Task SendCrewSamplesAsync(DriverLive.CrewForPhone listed)
    {
        if (!CrewSamples.MadeHere(listed.Engine) || !_relayPublisher.CanSendCrewSamples
            || Interlocked.Exchange(ref _crewSampling, 1) == 1)
        {
            return;
        }

        try
        {
            if (await _relayPublisher.CrewSampleKeysAsync() is not { } onRelay)
            {
                return;
            }

            _crewSamplesOnRelay = onRelay.ToHashSet(StringComparer.Ordinal);
            var sent = 0;
            string? problem = null;

            foreach (var voice in CrewSamples.StillToSend(listed.Voices, onRelay).Take(CrewSamples.MostPerPass))
            {
                try
                {
                    var mp3 = await CrewSamples.SampleOfAsync(
                        voice.Pick,
                        (id, cancel) => _heatCallerVoice.SampleAsync(id, cancel),
                        (id, line, cancel) => KokoroSpeech.RenderAsync(new VoiceProfile { KokoroVoiceName = id }, line, cancel, renderingAhead: true));

                    var key = DriverCrew.KeyOf(voice.Pick);

                    if (await _relayPublisher.UploadCrewSampleAsync(key, mp3) is { } refused)
                    {
                        problem ??= refused;
                        continue;
                    }

                    onRelay.Add(key);
                    _crewSamplesOnRelay = onRelay.ToHashSet(StringComparer.Ordinal);
                    sent++;
                }
                catch (Exception ex)
                {
                    problem ??= $"\"{voice.Pick.Name}\": {ex.Message}";
                }
            }

            if (sent > 0)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [crew] Sent the relay {Plural.Of(sent, "voice sample")} for drivers' phones.");
            }

            // Said once, not every ten minutes while it keeps failing the same way.
            if (problem is not null && !string.Equals(problem, _crewSampleProblemSaid, StringComparison.Ordinal))
            {
                _crewSampleProblemSaid = problem;
                OnClientRawMessage($"{AppTime.LogStampNow()}  [crew] A voice sample for drivers' phones could not be sent - {problem}. Tried again later.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _crewSampling, 0);
        }
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
            _crewOnAccount = CrewKeysStillThere(all, engine);
            _crewListedFor = engine;
            _crewListFailed = false;
            _crewListedUtc = DateTime.UtcNow;

            // Samples for the phones' Play buttons, where the engine has none of its own (19 Nov 2026).
            _ = SendCrewSamplesAsync(_crewForPhone);
        }
        catch (Exception ex)
        {
            // Tried again in a minute, not every few seconds. What was listed
            // before stays, so phones keep the voices they had.
            //
            // BUT NOT ANOTHER ENGINE'S KEYS (15 Nov 2026, bug hunt 21): after
            // a switch whose first listing failed, the old engine's keys were
            // taken as this one's, no driver's pick was on them, and every
            // crew voice fell back to the club's. Not known is "still there".
            _crewOnAccount = KeysAfterFailedListing(_crewOnAccount, _crewForPhone?.Engine, engine);
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

    /// <summary>
    /// ElevenLabs' Default voices stop on 31 December 2026 (24 Nov 2026,
    /// Asher). From the next day, as the settings load: each seat still on
    /// one moves to HeatCaller's voice for it, and each driver's crew voice
    /// on one to ElevenLabs' named replacement (RetiredVoices). Saved, and
    /// said once in the console - the next start finds nothing to move.
    /// </summary>
    /// <remarks>
    /// WHY: without it, every line in a retired voice failed from 1 January
    /// and Kokoro said it, until somebody noticed and picked again. Saved
    /// lines are kept by voice id, so a moved seat's lines are rendered
    /// afresh in the new voice; none of the old voice's play beside them.
    /// </remarks>
    private void SwitchRetiredVoices(DateOnly today)
    {
        try
        {
            var moves = RetiredVoices.SwitchSeats(_settings, _voiceStore.Current, today);

            if (moves.Count > 0)
            {
                _settings.Save();
                _voiceStore.Save(_voiceStore.Current);

                foreach (var move in moves)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  {RetiredVoices.Words(move)}");
                }
            }

            var crew = RetiredVoices.SwitchCrew(_profiles.All, today);

            if (crew.Count > 0)
            {
                _profiles.UpsertMany(crew);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [crew] ElevenLabs retired its Default voices on {RetiredVoices.LastDayWords}: "
                    + $"{Plural.Of(crew.Count, "driver's crew voice", "drivers' crew voices")} moved to ElevenLabs' replacement "
                    + "(or back to the club's voice where there is none).");
            }
        }
        catch (Exception ex)
        {
            // Never in the way of the app opening: the lines in a retired voice
            // still go to the stand-in, and the Cast tab still says so.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [voice] Couldn't move the retired ElevenLabs voices - {ex.Message}.");
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

                return CrewVoicesWithSuggested(voices
                    .Select(v => new CrewVoiceOption(new CrewVoicePick { Engine = word, Id = v.VoiceId, Name = v.Name }, v.PreviewUrl)));
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

        // NOT IN THE GAP BEFORE THE FINALS (15 Nov 2026, bug hunt 21): the
        // heats are all run and the mains not built yet, so Race Control has
        // nothing to run - but the programme still has a step, and a goal
        // approved then is for the finals, not the next night.
        if (ProgrammeStillToRun(raceControl.NextStepProvider?.Invoke()))
        {
            return false;
        }

        return TonightAtTheTrack.Races(_sessionHistory.All, id => _rounds.Get(id), DateTime.Now).Any();
    }

    /// <summary>
    /// Whether the programme's next step is more racing tonight: rounds to
    /// run, or mains or bump-ups still to build. Not "build qualifying",
    /// which a club running its races by hand sees all night.
    /// </summary>
    internal static bool ProgrammeStillToRun(ProgramStep? step) =>
        step?.Stage is ProgramStage.RunNext or ProgramStage.PostHeats or ProgramStage.LateArrivals
            or ProgramStage.BumpUps or ProgramStage.SeedMains;

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
