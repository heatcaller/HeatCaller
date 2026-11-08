using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The voice queue: building a line, synthesising and playing it, which
/// calls interrupt or supersede others, and the queue loop.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Formats a millisecond duration as speech text with an explicit
    /// "point" between the whole and fractional seconds, e.g. "5 point
    /// eight eight seconds". The fractional digits are spelled out as
    /// words rather than left as digit characters - just separating
    /// digits with a space ("8 8") wasn't reliable either: it worked
    /// sometimes, but ElevenLabs' own text normalization apparently still
    /// re-collapsed it back into the two-digit number "eighty-eight" other
    /// times, since digit characters still look like a number in context.
    /// Spelling them as words removes anything number-shaped for the
    /// engine to reinterpret.
    /// </summary>
    /// <remarks>
    /// MOVED TO RaceNumbers ON 14 OCT 2026, and the reading changed with
    /// it: a lap time now comes back as "five oh six" rather than "5 point
    /// zero six seconds", which is the sentence a club's own log showed
    /// the announcer saying and which nobody who has called a race has
    /// ever said.
    ///
    /// EVERY RULE THIS METHOD LEARNED IS KEPT, which is why it is a
    /// delegation and not a replacement. The digits are still spelled as
    /// words, for the reason the summary above records - ElevenLabs
    /// re-collapsed "8 8" into "eighty-eight" some of the time and not
    /// others - and the rounding is still to hundredths. What is new is
    /// only the convention the words are arranged in, and the durations
    /// that are NOT lap times (a sector, a session clock) still get the
    /// explicit reading, through RaceNumbers.Plainly.
    ///
    /// DigitWords, the table this method spelled digits from, went on
    /// 15 Oct 2026 with its last reader: the split-loop gap is now said
    /// through RaceNumbers.GapSpoken as well ("two tenths up", not "point
    /// two seconds").
    ///
    /// Still here, and still the funnel, so the one call site that wants a
    /// lap time and the one that wants a sector both keep working - see
    /// RaceNumbers for which is which.
    /// </remarks>
    private static string FormatSecondsForSpeech(long ms) => RaceNumbers.LapSpoken(ms);

    /// <summary>
    /// The same duration written for READING rather than speaking - "5.88s".
    ///
    /// The spoken form deliberately spells digits out as words so the TTS
    /// engine can't re-collapse them into "eighty-eight", but that text was
    /// also what got shown on the Big Screen and the web pages, where it
    /// read as "5 point eight eight seconds". Screens want the numerals.
    /// </summary>
    private static string FormatSecondsForDisplay(long ms)
    {
        // Full millisecond precision. The centisecond rounding belongs to
        // the SPOKEN form only - nobody wants to hear "six point nine three
        // five seconds" - but a screen should show what was actually timed.
        // This previously reused the spoken rounding, so 6.935s displayed
        // as 6.94s and the number on screen disagreed with the lap chart.
        //
        // Invariant, like the sector display beside it (17 Oct 2026, Bug
        // Hunt 8, A06-8): the PC's culture put "6,935s" on a comma-locale PC.
        return (ms / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "s";
    }

    /// <param name="suffix">
    /// Appended to the finished line, for context the template itself can't
    /// carry - currently the runner-up's streak.
    /// </param>
    /// <param name="extras">
    /// Placeholders only some lines have - the split loop's {sector},
    /// {sectortime}, {split} and {gap} - each with its spoken and its
    /// on-screen form.
    /// </param>
    /// <param name="autoTail">
    /// A sentence added to the end of the chosen line UNLESS that line
    /// already uses the named placeholder - the lead change count, which a
    /// club can either let the announcer add or write into its own lines.
    /// The same rule "Include lap time" goes by.
    /// </param>
    /// <param name="sayByMs">
    /// The latest TimingClock time the line is still true at - the time-left
    /// calls (28 Oct 2026). One that waited in the queue past it is not said:
    /// "thirty seconds" at twenty seconds left is worse than nothing.
    /// </param>
    private void EnqueueVoice(AnnouncementEventType type, RaceStanding? standing, GatePassEvent? gate, long? lapTimeMs = null, int? streakCount = null, string? suffix = null, string? roundLabel = null, string? className = null, string? marshals = null, IReadOnlyDictionary<string, (string Spoken, string Display)>? extras = null, (string Placeholder, string Tail)? autoTail = null, long? sayByMs = null)
    {
        if (Muted)
        {
            return;
        }

        var profile = _voiceStore.Current;
        if (!profile.Events.TryGetValue(type.ToString(), out var config) || !CallOn(config))
        {
            // Says which event was skipped and why - a silent announcement
            // is otherwise indistinguishable from one that never fired.
            StatusMessage?.Invoke(config is null
                ? $"[voice] {type} skipped - no configuration entry."
                : $"[voice] {type} skipped - switched off for {(IsPracticeMode ? "practice" : "races")}.");
            return;
        }

        if (HeldByCaution(type, config))
        {
            StatusMessage?.Invoke($"[voice] {type} held - a flag is out.");
            return;
        }

        // THE CLUB'S DIAL, HERE BECAUSE THIS IS THE FUNNEL (14 Oct 2026).
        // Every template-based call comes through EnqueueVoice, and only
        // some of them go through the per-lap candidate gate - so a floor
        // applied only there would have left the running order, a lapping
        // and the split calls louder than the lap count beside them, at
        // the same stop on the same dial.
        //
        // Said in the log like every other refusal above it: a club that
        // has turned the dial down and then wonders where a call went has
        // to be able to read the answer.
        if (!DialLetsItSpeak(type, config))
        {
            StatusMessage?.Invoke(
                $"[voice] {type} held - the announcer is set to {ShowChatter.For(Chatter).Name}.");
            return;
        }

        // A flag with a line already rendered skips synthesis entirely -
        // unless a sponsor presents it (the race result), when the saved
        // take would leave their name off; the live line carries it.
        var sponsoredHere = SponsoredMoments.ForEvent(type) is { } sponsoredMoment && SponsorOf(sponsoredMoment) is not null;

        if (IsInterruptingAnnouncement(type) && !sponsoredHere && TrySpeakPreparedFlag(type, config))
        {
            return;
        }

        var candidates = config.EffectiveTemplates;
        if (candidates.Count == 0)
        {
            return;
        }

        // Pick one phrasing at random each time, so an event that can
        // legitimately fire many times in a row (e.g. several new fastest
        // laps back to back) doesn't read out the identical sentence
        // every single time.
        var chosenTemplate = candidates.Count == 1 ? candidates[0] : TemplateBag.Pick(candidates);

        var transponder = standing?.Transponder ?? gate?.Transponder;
        var driverProfile = transponder is not null ? _profiles.Get(transponder) : null;

        // Diagnostic: this exact lookup has been reported as returning the
        // raw transponder ID instead of a configured driver name, and the
        // obvious first fix (case sensitivity) didn't resolve it - so log
        // enough here to see definitively, on the next test, whether the
        // profile is being found at all and what's actually in it, rather
        // than guessing at a second cause blind.
        StatusMessage?.Invoke(driverProfile is null
            ? $"[voice] Driver lookup: transponder=\"{transponder}\" -> NO PROFILE FOUND for this exact key."
            : $"[voice] Driver lookup: transponder=\"{transponder}\" -> found profile, DriverName=\"{driverProfile.DriverName}\"");

        // SpokenName prefers the announcer-specific name when one is set -
        // a nickname the crowd knows, or a spelling the voice actually
        // pronounces correctly - and falls back to the real name.
        var driver = driverProfile is not null && !string.IsNullOrWhiteSpace(driverProfile.SpokenName)
            ? driverProfile.SpokenName
            : standing?.DisplayName ?? transponder ?? "Racer";

        // A car said by its number stays in the room (23 Oct 2026, bug hunt
        // 10): noted, so listen-along, the public captions and the podium
        // videos leave the line out - see NoteSpokenAsNumber.
        NoteSpokenAsNumber(transponder, driver);

        // Diagnostic: an announcement was reported with {driver} resolving
        // to an empty string ("The  get fastest lap!") despite
        // RaceStanding.DisplayName always falling back to the transponder
        // ID and never returning empty - which shouldn't be possible from
        // the code as written. Logging the raw chosen template alongside
        // the resolved driver value will show definitively, on the next
        // occurrence, whether "driver" itself is genuinely empty here or
        // something else (e.g. an unusual character in a saved custom
        // template) is going on, rather than guessing again.
        StatusMessage?.Invoke($"[voice] Template chosen: \"{chosenTemplate}\"  resolved driver=\"{driver}\"");
        var car = transponder is null ? string.Empty : _profiles.CarModelFor(transponder);
        var lap = standing?.Laps.ToString() ?? string.Empty;
        var time = standing is { FastLapMs: > 0 } ? FormatSecondsForSpeech(standing.FastLapMs) : string.Empty;
        var lapTimeText = lapTimeMs.HasValue ? FormatSecondsForSpeech(lapTimeMs.Value) : string.Empty;

        // Display variants, substituted into a parallel string so screens
        // show numerals while the voice still gets the spelled-out form.
        var timeDisplay = standing is { FastLapMs: > 0 } ? FormatSecondsForDisplay(standing.FastLapMs) : string.Empty;
        var lapTimeDisplay = lapTimeMs.HasValue ? FormatSecondsForDisplay(lapTimeMs.Value) : string.Empty;
        var gateId = gate?.Gate ?? string.Empty;
        var streakText = streakCount?.ToString() ?? string.Empty;

        var roundText = roundLabel ?? string.Empty;
        var classText = className ?? string.Empty;
        var marshalsText = marshals ?? string.Empty;

        // Every one of these can legitimately be empty - a driver with no
        // car model, a session with no round, a lap with no time yet - and
        // an empty substitution used to leave the words that introduced
        // it: "in his !". OrMissing marks them; TidyTemplate takes the
        // clause out.
        // The occasional placeholders first, each as it is said or shown.
        string WithExtras(string line, bool display)
        {
            if (extras is null)
            {
                return line;
            }

            foreach (var (name, value) in extras)
            {
                line = line.Replace("{" + name + "}", AnnouncementText.OrMissing(display ? value.Display : value.Spoken), StringComparison.OrdinalIgnoreCase);
            }

            return line;
        }

        // Placeholders matched whatever their case, the way the release
        // call matches them - "{Driver}" was otherwise spoken out loud.
        var text = WithExtras(chosenTemplate, display: false)
            .Replace("{driver}", driver, StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(car), StringComparison.OrdinalIgnoreCase)
            .Replace("{lap}", AnnouncementText.OrMissing(lap), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", AnnouncementText.OrMissing(time), StringComparison.OrdinalIgnoreCase)
            .Replace("{laptime}", AnnouncementText.OrMissing(lapTimeText), StringComparison.OrdinalIgnoreCase)
            .Replace("{gate}", AnnouncementText.OrMissing(gateId), StringComparison.OrdinalIgnoreCase)
            .Replace("{streak}", AnnouncementText.OrMissing(streakText), StringComparison.OrdinalIgnoreCase)
            .Replace("{round}", AnnouncementText.OrMissing(roundText), StringComparison.OrdinalIgnoreCase)
            .Replace("{class}", AnnouncementText.OrMissing(classText), StringComparison.OrdinalIgnoreCase)
            .WithMarshals(marshalsText)
            .WithDriverDetails(driverProfile)
            .TidyTemplate();

        var displayText = WithExtras(chosenTemplate, display: true)
            .Replace("{driver}", driver, StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(car), StringComparison.OrdinalIgnoreCase)
            .Replace("{lap}", AnnouncementText.OrMissing(lap), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", AnnouncementText.OrMissing(timeDisplay), StringComparison.OrdinalIgnoreCase)
            .Replace("{laptime}", AnnouncementText.OrMissing(lapTimeDisplay), StringComparison.OrdinalIgnoreCase)
            .Replace("{gate}", AnnouncementText.OrMissing(gateId), StringComparison.OrdinalIgnoreCase)
            .Replace("{streak}", AnnouncementText.OrMissing(streakText), StringComparison.OrdinalIgnoreCase)
            .Replace("{round}", AnnouncementText.OrMissing(roundText), StringComparison.OrdinalIgnoreCase)
            .Replace("{class}", AnnouncementText.OrMissing(classText), StringComparison.OrdinalIgnoreCase)
            .WithMarshals(marshalsText)
            .WithDriverDetails(driverProfile)
            .TidyTemplate();

        // "Include lap time" auto-appends the relevant time for anyone who
        // wants it spoken without writing {laptime}/{time} into every one
        // of their phrasing variants by hand. Skipped if the CHOSEN line
        // already mentions it, so it's never said twice.
        if (type == AnnouncementEventType.LapCompleted && config.IncludeLapTime && lapTimeText.Length > 0
            && !chosenTemplate.Contains("{laptime}", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} Lap time: {lapTimeText}.".Trim();
            displayText = $"{displayText} Lap time: {lapTimeDisplay}.".Trim();
        }

        if (type == AnnouncementEventType.FastLap && config.IncludeLapTime && time.Length > 0
            && !chosenTemplate.Contains("{time}", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} New fastest time: {time}.".Trim();
            displayText = $"{displayText} New fastest time: {timeDisplay}.".Trim();
        }

        // The dedicated lap-time-only announcement: either "Lap time: X
        // seconds." or, with the label turned off, just "X seconds." - the
        // whole point of this event is to be as short as the user wants.
        // Optionally prefixed with the driver's name, for when more than
        // one racer is on track and a bare number doesn't say whose it is.
        if (type == AnnouncementEventType.LapTime && lapTimeText.Length > 0)
        {
            text = config.SayLapTimeLabel ? $"Lap time: {lapTimeText}." : $"{lapTimeText}.";
            displayText = config.SayLapTimeLabel ? $"Lap time: {lapTimeDisplay}." : $"{lapTimeDisplay}.";
            if (config.IncludeDriverNameInLapTime)
            {
                text = $"{driver}, {text}";
                displayText = $"{driver}, {displayText}";
            }
        }

        if (text.Length == 0)
        {
            return;
        }

        if (autoTail is { } tail && tail.Tail.Length > 0
            && !chosenTemplate.Contains(tail.Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            text = (text + tail.Tail).Trim();
            displayText = (displayText + tail.Tail).Trim();
        }

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            text = (text + suffix).Trim();
            displayText = (displayText + suffix).Trim();
        }

        // At this announcement's own Speed. The Voice Settings page offers
        // one per announcement and its Test button plays at it, but every
        // line through here used to go out at the profile's speed - so a
        // race never sounded like its test. It is also what makes a
        // prepared flag line and this live fallback the same request (see
        // PrimeFlagLineAsync), so either one's saved copy serves both.
        var speed = config.SpeedFor(profile.Speed);

        // The sponsor's credit when a sponsor has this moment, rationed and
        // counted by SponsorMentions. Counted in the work itself, so a line
        // the queue drops is not one the sponsor is told they got.
        //
        // WHY (23 Oct 2026, bug hunt 10): it was counted at the START of the
        // work, and SynthesizeAndPlayAsync can still drop the line after that
        // - a flag or the countdown while it synthesised, a mute, no voice at
        // all - so the sponsor report counted mentions nobody heard and the
        // ration was spent for three minutes. Now it is counted at the one
        // point the line is certain to be heard (SynthesizeAndPlayAsync's
        // spoken), and given back on every other way out.
        var (sponsorTag, countMention, releaseMention) = SponsorTag(type);

        if (sponsorTag.Length > 0)
        {
            text = SponsoredMoments.WithCredit(text, sponsorTag);
            displayText = SponsoredMoments.WithCredit(displayText, sponsorTag);
        }

        // Screens get the numeral form; the voice engine gets the
        // spelled-out one. Shown only if the line is accepted - see
        // EnqueueVoiceWork.
        //
        // A line the queue turns down - or keeps and then loses - gives the
        // sponsor's ration back (20 Oct 2026, Bug Hunt 8), or the sponsor
        // went unnamed for three minutes over a line nobody heard.
        var accepted = EnqueueVoiceWork(type, transponder,
            async () =>
            {
                var said = false;

                try
                {
                    if (sayByMs is { } sayBy && TimingClock.NowMs > sayBy)
                    {
                        StatusMessage?.Invoke($"[voice] Dropped {type} - it waited too long to still be true (\"{displayText}\").");
                        return;
                    }

                    await SynthesizeAndPlayAsync(text, "[voice] Announcing", speedOverride: speed,
                        spoken: () =>
                        {
                            said = true;
                            countMention?.Invoke();
                        });
                }
                finally
                {
                    if (!said)
                    {
                        releaseMention?.Invoke();
                    }
                }
            },
            displayText: displayText.Length > 0 ? displayText : text,
            lost: releaseMention,
            sayByMs: sayByMs);

        if (!accepted)
        {
            releaseMention?.Invoke();
        }
    }

    /// <summary>
    /// Shared synthesize-then-play logic used by every voice announcement
    /// path, generic or rivalry-specific alike - ElevenLabs, the HeatCaller
    /// relay or Kokoro, as RenderLineAsync decides.
    /// </summary>
    /// <param name="skippedLogMessage">
    /// Said when ElevenLabs is chosen and the line can't be spoken at all -
    /// no key or voice, and no stand-in to cover. A missing Kokoro model
    /// says so in its own words, below.
    /// </param>
    /// <param name="deliveryOverride">
    /// Optional per-call voice settings. Used by the race summary, which
    /// wants more expressive delivery than a routine lap call - turning the
    /// whole announcer up instead would make every announcement over-acted.
    /// </param>
    /// <param name="honourMute">
    /// False only for a test asked for by hand, which must speak even while
    /// the announcer is muted. Everything else checks the mute again after
    /// synthesis and after waiting for the speaker, because a mute can land
    /// while a line is in either.
    /// </param>
    /// <param name="voice">Another speaker's voice for this line alone - the recap's mechanic. Null for the announcer.</param>
    /// <param name="forListeners">
    /// False for a test asked for by hand (SpeakTest). A name check before
    /// doors open reads out every saved driver's full name, and none of
    /// that is the race - it must not go out on the public listen page.
    /// </param>
    /// <param name="spoken">
    /// Run once the line is certain to be heard - past every check that can
    /// still drop it, just before the speaker (23 Oct 2026, bug hunt 10). A
    /// sponsor's mention is counted here and nowhere earlier. Not run when
    /// the line is dropped or its synthesis fails.
    /// </param>
    private async Task SynthesizeAndPlayAsync(
        string text,
        string announcingLogPrefix,
        string skippedLogMessage = "[voice] Skipped announcement - ElevenLabs API key or voice not configured.",
        (double Stability, double Style)? deliveryOverride = null,
        double? speedOverride = null,
        bool honourMute = true,
        SecondVoice? voice = null,
        SpeechTail tail = SpeechTail.Normal,
        SpeechLead lead = SpeechLead.Normal,
        bool forListeners = true,
        Action? spoken = null)
    {
        var currentProfile = _voiceStore.Current;

        if (deliveryOverride is { } delivery)
        {
            // A shallow copy so the override applies to THIS line only and
            // doesn't leak into the stored profile - mutating the real one
            // would leave every later announcement using summary settings.
            currentProfile = currentProfile.CloneForDelivery(delivery.Stability, delivery.Style, speedOverride);
        }
        else if (speedOverride is { } speedOnly)
        {
            // Speed without a delivery override - a long announcement that
            // needs room to breathe but no change in expression.
            currentProfile = currentProfile.CloneForDelivery(
                currentProfile.Stability, currentProfile.Style, speedOnly);
        }

        // Who would say it without the second voice - for a Kokoro voice on
        // the Cast tab that Kokoro can't say after all (RenderCastLineAsync).
        var withoutVoice = currentProfile;

        if (voice is not null)
        {
            // After the delivery, which it keeps: only who is speaking changes.
            currentProfile = currentProfile.CloneWithVoice(voice);
        }

        // Strips [excited]-style tags on models that can't use them, so a
        // tagged template doesn't get read out bracket by bracket when
        // someone switches off v3.
        var beforeSanitise = text;
        text = currentProfile.SanitiseForModel(text);

        // Reports what the RACE actually used. Tests build a profile from
        // the on-screen controls while races read the SAVED one, so an
        // unsaved model change makes tests and races behave differently -
        // this makes that visible instead of leaving it to be guessed at.
        //
        // ElevenLabs only. Kokoro never uses tags, so taking them out for
        // it is expected rather than a model to go and change - and the
        // advice to pick a v3 model would be wrong for it.
        if (currentProfile.EffectiveEngine == VoiceEngine.ElevenLabs
            && !ReferenceEquals(beforeSanitise, text) && beforeSanitise != text)
        {
            StatusMessage?.Invoke(
                $"[voice] Audio tags stripped - saved model is \"{currentProfile.ModelId}\", which doesn't support them. "
                + "Pick a v3 model on the Voice tab - it saves as you change it.");
        }

        // Whether ANY voice can say it. This asked for an ElevenLabs key and
        // voice only, which with a free voice chosen - or standing in for a
        // club with no key - would have skipped every line.
        //
        // The Kokoro arm is the loud one. Since the Windows voice went there
        // is nothing under Kokoro, so a PC without the model is a PC that
        // cannot say a word - and the only honest thing to do with that is
        // say which button fixes it, in the console, in words. Said once per
        // reason rather than per line (LogStandInOnce) so a race night does
        // not fill the console with the same sentence a hundred times, and
        // the Mixer carries the same thing as a banner for anyone not
        // watching the log (DescribeVoiceBlocker).
        if (!CanSpeak(currentProfile))
        {
            if (currentProfile.EffectiveEngine == VoiceEngine.Kokoro)
            {
                var why = KokoroSpeech.UnavailableReason ?? "the Kokoro voice can't speak on this PC";

                LogStandInOnce("no-voice", NoVoiceAtAllMessage(why));
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - {why}.");
                return;
            }

            StatusMessage?.Invoke(skippedLogMessage);
            return;
        }

        if (IsSupersededByInterrupt())
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - a flag call interrupted before it spoke.");
            return;
        }

        if (IsCutByCountdown())
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the countdown started before it spoke.");
            return;
        }

        // A battle line whose two cars have swapped - see AnnouncementService.BattleLines.
        if (IsOvertakenOnTrack())
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the two cars it is about swapped before it spoke.");
            return;
        }

        if (honourMute && Muted)
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the announcer is muted.");
            return;
        }

        // How big the race is, in the delivery (22 Oct 2026) - a race line
        // only: not a line with a delivery of its own (the race summary's),
        // not a test pressed by hand, and not the second voice. See
        // AnnouncementService.Energy.
        if (deliveryOverride is null && honourMute && forListeners && voice is null)
        {
            currentProfile = WithTheRacesEnergy(currentProfile, text);
        }

        StatusMessage?.Invoke($"{announcingLogPrefix}: \"{text}\"");
        var rendering = System.Diagnostics.Stopwatch.StartNew();
        var synthesis = await RenderCastLineAsync(withoutVoice, currentProfile, voice, text);
        var audio = synthesis.Audio;
        rendering.Stop();

        // How long the words took to make (8 Nov 2026). The log line above is
        // written BEFORE the render, so a slow model's seconds were invisible
        // - and they are what made every call on a 5.5s track a lap late.
        if (rendering.ElapsedMilliseconds >= 1_500)
        {
            StatusMessage?.Invoke($"[voice] Rendered in {Seconds(rendering.ElapsedMilliseconds)} ({currentProfile.EffectiveEngine}{(currentProfile.EffectiveEngine == VoiceEngine.ElevenLabs ? " " + currentProfile.ModelId : string.Empty)}, {(synthesis.FromLibrary ? "saved take" : "new")}).");
        }

        // A race-control call with its own sound - the start horn, the
        // time-up buzzer, the finish fanfare - lets the sound play first.
        // Waited for AFTER synthesis, so the words are rendered while the
        // sound plays and follow it straight away rather than a network
        // round trip later.
        if (_leadInFor.Value is { } leadInType)
        {
            await WaitForLeadInSoundAsync(leadInType);
        }

        await _audioPlaybackLock.WaitAsync();
        try
        {
            // Checked again after synthesis and after the playback lock: a
            // flag pressed while this line was on the network (or waiting
            // for the speaker) has already cleared the decks, and this line
            // must not then start speaking over it.
            if (IsSupersededByInterrupt())
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - a flag call interrupted while it was being synthesized.");
                return;
            }

            // The same for a countdown: the line was on the network or
            // waiting for the speaker when it started, and the countdown
            // sounds are what must be heard now.
            if (IsCutByCountdown())
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the countdown started while it was being synthesized.");
                return;
            }

            // And for a battle line whose two cars swapped meanwhile.
            if (IsOvertakenOnTrack())
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the two cars it is about swapped while it was being synthesized.");
                return;
            }

            // And for a lap call or an AI line the race moved past while it
            // was rendered (8 Nov 2026): on eleven_v3 that is seconds - see
            // AnnouncementService.Freshness.
            if (WhyDequeuedLineIsStale() is { } stale)
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - {stale} (rendered in {Seconds(rendering.ElapsedMilliseconds)}).");
                return;
            }

            // Muted while it was being synthesised or waiting its turn.
            if (honourMute && Muted)
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the announcer was muted before it spoke.");
                return;
            }

            // Boosted only when something else is actually playing loudly -
            // supplied by the ambience service, which knows its own current
            // level. A blanket boost would just make every announcement
            // louder, including over silence. Not clamped to 1.0 here - see
            // AnnouncementGain for why that clamp made the boost do nothing.
            var volume = AnnouncementGain(currentProfile.Volume);

            // Past every check that drops a line: it is heard from here, so
            // this is where a sponsor's mention counts - see spoken. A
            // failing count never costs the line.
            if (spoken is not null)
            {
                try
                {
                    spoken();
                }
                catch
                {
                    // The report is a courtesy.
                }
            }

            try
            {
                // ANYONE LISTENING FROM HOME, from here and nowhere else
                // (13 Oct 2026). This is the one point in the app where a
                // line is definitely about to be heard: everything above
                // can still drop it for a flag, a countdown or a mute, and
                // a listener must hear what the room hears and nothing it
                // did not. Offered BEFORE the speaker rather than after,
                // because after would put the whole length of the line on
                // top of the lag. It returns at once - see
                // AnnouncementService.ListenAlong.
                //
                // With whether the audio says the names shortened (23 Oct
                // 2026, bug hunt 10), so a line whose audio says them in
                // full stays off the public page - see OfferToListenersOnly.
                var onAir = forListeners && OfferLineToListeners(text, audio, AudioFromShortenedText(synthesis.Source, text));

                // The pads the CALLER asked for, since 11 Oct 2026. This
                // was the default 0.9s tail and the full lead whatever the
                // line was, so two turns of one exchange paid both in full
                // with the hand-over's beat on top - see SpeechLead.
                // WHY (23 Oct 2026, bug hunt 10): re-checked once the line is registered - see PlayPcmAsync.
                await PlayAsync(audio, volume, TailSecondsFor(tail), LeadSecondsFor(lead), StillWantedUnlessOvertaken(honourMute), onAir);
            }
            catch (Exception ex) when (synthesis.FromLibrary && IsUndecodable(ex))
            {
                // WHY (20 Oct 2026, Bug Hunt 8): a line recorded for a
                // soundscape also comes back as LineSource.Library (see
                // RenderLineAsync), so a recording cut short by a crash
                // used to land here and delete the ElevenLabs take saved
                // under the same words - the good file - while the broken
                // recording stayed first in line and failed every time.
                // Now a recording is recognized by being byte-for-byte what
                // the soundscape library holds for these words (this path
                // only runs on a failure, so the extra read costs nothing on
                // a race night), the saved take is left alone, and the
                // console says which file needs re-recording. The
                // recording itself is not deleted: throwing away a club's
                // recording is theirs to decide, on the soundscape page.
                if (StingerLibrary.Shared.TryLoad(text) is { } recorded
                    && recorded.AsSpan().SequenceEqual(audio))
                {
                    StatusMessage?.Invoke($"[voice] The recorded soundscape line \"{text}\" won't play - re-record or delete it on the soundscape page.");
                    throw;
                }

                // A saved line that won't decode would fail like this every
                // time it came round. Forgotten, so the next time it is
                // wanted it is rendered afresh.
                VoiceLibrary.Shared.Forget(currentProfile, ForCloudVoice(text));
                throw;
            }
        }
        finally
        {
            _audioPlaybackLock.Release();
        }
    }

    /// <summary>What NAudio throws for bytes that are not the audio they claim to be - as opposed to a missing speaker.</summary>
    private static bool IsUndecodable(Exception ex) =>
        ex is InvalidDataException or FormatException or EndOfStreamException;

    // Tracks whether ElevenLabs is CURRENTLY refusing requests for quota
    // reasons, so the console shows one clear notice when voice goes down
    // and one clear notice when it comes back - instead of repeating the
    // same raw JSON error block on every single announcement attempt for
    // the rest of the race. Sound effects never touch this since they
    // don't call ElevenLabs at all.
    //
    // Set and cleared only under _voiceHealthLock (NoteQuotaExceeded,
    // NoteElevenLabsAnswered) - flag calls and lines rendered in advance
    // reach it from pool threads, not just the voice queue - and volatile
    // for the pages that read it without the lock.
    private volatile bool _voiceQuotaExceeded;

    /// <summary>
    /// True when ElevenLabs last refused a request for quota reasons.
    ///
    /// Exposed so the UI can say "no voice because the quota is gone"
    /// rather than leaving someone to wonder why a Test button does
    /// nothing - the failure happens asynchronously on the voice queue,
    /// long after the button returns.
    /// </summary>
    public bool VoiceQuotaExceeded => _voiceQuotaExceeded;

    /// <summary>
    /// Whether a voice could speak at all right now, and why not if it can't.
    ///
    /// Null while Kokoro is standing in for ElevenLabs or the relay - there
    /// IS a voice then. DescribeStandIn says why it is standing in.
    ///
    /// Kokoro itself being unable to speak is NOT a stand-in: nothing covers
    /// for it, so it answers here, and what reads this (the Mixer's red NO
    /// VOICE banner) is what a club sees without watching the console.
    /// </summary>
    public string? DescribeVoiceBlocker()
    {
        var profile = _voiceStore.Current;

        if (Muted)
        {
            return "the announcer is muted";
        }

        if (StandInReasonFor(profile) is not null)
        {
            return null;
        }

        if (profile.EffectiveEngine == VoiceEngine.Kokoro)
        {
            // The load failure as well as the reason: UnavailableReason lets
            // a failed load go after a minute so a line can try again, and
            // reading only that had the Mixer say "Ready" between tries on a
            // PC where nothing could speak at all.
            return KokoroSpeech.UnavailableReason ?? KokoroSpeech.LoadFailure;
        }

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            if (!profile.HeatCallerReady)
            {
                return "no HeatCaller voice is chosen";
            }

            // Nothing is standing in - that was checked above - so the only
            // way this cannot speak is the relay refusing on a PC with no
            // Kokoro model to cover for it.
            lock (_heatCallerLock)
            {
                return KokoroSpeech.IsAvailable ? null : _heatCallerReason;
            }
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return "no ElevenLabs API key is set";
        }

        if (string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            return "no voice is selected";
        }

        if (_voiceQuotaExceeded)
        {
            return "the ElevenLabs quota is exhausted";
        }

        return null;
    }

    /// <summary>
    /// Why the free Kokoro voice is standing in for ElevenLabs right now -
    /// "the free Kokoro voice is standing in - the ElevenLabs quota is used
    /// up" - or null when it isn't. For the Mixer, which shows it as a notice
    /// rather than the red NO VOICE banner: the announcer can still speak.
    /// </summary>
    public string? DescribeStandIn() =>
        StandInReasonFor(_voiceStore.Current) is { } reason
            ? $"the free Kokoro voice is standing in - {reason}"
            : null;

    // True while a voice line is actively being synthesized/played. Used to
    // DROP new per-lap announcements outright rather than queue them behind
    // it - on a fast track, several laps can pass in the time one
    // announcement takes to speak (plus the ~1s of audio device warm-up
    // padding baked into every one), and a queued backlog would read out
    // increasingly stale info well after it stopped being relevant. A
    // dropped announcement is better than a late one here.
    private volatile bool _isVoiceSpeaking;

    /// <summary>
    /// Calls that cut straight across whatever is being said.
    ///
    /// A flag is an instruction to the drivers, and an instruction that
    /// waits politely for a lap-time announcement to finish is late by the
    /// time it lands. These stop the current line mid-word, jump the queue,
    /// and start synthesising the moment the button is pressed.
    /// </summary>
    private static bool IsInterruptingAnnouncement(AnnouncementEventType type) => type is
        AnnouncementEventType.RaceStarted or
        AnnouncementEventType.GreenFlagResumed or
        AnnouncementEventType.YellowFlag or
        AnnouncementEventType.RedFlag or
        AnnouncementEventType.RaceCompleted or
        AnnouncementEventType.RaceTimeOver;

    /// <summary>
    /// Race-control announcements that must never be dropped or delayed
    /// behind routine chatter. The drop-while-speaking guard and FIFO
    /// ordering exist to stop per-lap announcements piling up on a fast
    /// track - but a yellow or red is safety information, it's rare, and
    /// losing one because the announcer happened to be mid-sentence about
    /// someone's lap time is exactly backwards.
    /// </summary>
    /// <summary>
    /// Whether the club's chattiness dial will let this call be said at
    /// all - see ShowChatter.CallIsWorthIt, which has the reasoning and
    /// the floors.
    ///
    /// AN INSTRUCTION IS NEVER HELD, and the list of those is
    /// <see cref="IsPriorityAnnouncement"/> rather than a second list kept
    /// here. That list already means exactly the right thing - it is what
    /// a flag's queue purge spares, for the same reason a taste dial must
    /// not swallow it: a countdown, a green, a grid call, a driver's
    /// release and the finish are the race being run.
    ///
    /// JUMP START WITH THEM, which the priority list does not include and
    /// this does. A jump start is a RULING - it is what the penalty on the
    /// Classes page is applied for - and at its shipped weight of 50 the
    /// quiet stops would have silenced it. It is not on the priority list
    /// because it does not need to cut across a flag, which is a different
    /// question from whether taste may drop it.
    /// </summary>
    private bool DialLetsItSpeak(AnnouncementEventType type, AnnouncementEventConfig config) =>
        ShowChatter.CallIsWorthIt(
            config.Weight,
            Chatter,
            alwaysSaid: IsPriorityAnnouncement(type) || type == AnnouncementEventType.JumpStart);

    private static bool IsPriorityAnnouncement(AnnouncementEventType type) => type is
        AnnouncementEventType.RaceStaging or
        AnnouncementEventType.CountdownStarted or
        AnnouncementEventType.RaceStarted or
        AnnouncementEventType.YellowFlag or
        AnnouncementEventType.RedFlag or
        AnnouncementEventType.GreenFlagResumed or
        AnnouncementEventType.RaceTimeOver or
        AnnouncementEventType.RaceCompleted or

        // A release is an instruction to a driver sitting still waiting for
        // it. Dropping one behind lap chatter leaves a car on the line.
        AnnouncementEventType.DriverRelease or
        AnnouncementEventType.AchievementUnlocked or

        // The round being called to the grid - an instruction, and made
        // immediately before staging, whose own priority purge used to bin
        // it. See NotifyGridCall.
        AnnouncementEventType.GridCall or

        // Reminders are deliberate, scheduled speech - a schedule warning,
        // the recap teaser, the race summary itself. Without this they were
        // dropped whenever something else happened to be speaking, and the
        // teaser fires immediately after the RaceCompleted call, so it was
        // dropped every single time.
        AnnouncementEventType.Reminder;

    /// <summary>
    /// Which of those a flag call SPARES: the queued lines a yellow, a red,
    /// a green, the start or the finish leaves in the queue, and the lines
    /// a flag cannot overtake once they have been taken off it (see
    /// _dequeuedUnderGeneration).
    ///
    /// "Priority" carries four things at once - not dropped behind chatter,
    /// spoken under a caution, bins the routine lines queued ahead of it,
    /// and survives a flag clearing the queue. Reminder was made priority
    /// for the first of those (scheduled speech was being dropped whenever
    /// anything else happened to be speaking, and the recap teaser fires
    /// straight after the finish call, so it went every time) and took the
    /// fourth with it. That is the one it should never have had: a reminder
    /// is the only priority type that is not an instruction to a driver,
    /// and the only one that can be a minute and a half long.
    ///
    /// WHAT WENT WRONG. The post-race recap is queued as a Reminder, up to
    /// 90 seconds of it. Green pressed from Idle has no countdown, so the
    /// countdown's own cut - the one check that was never exempt - never
    /// runs; ClearTheDecksFor stops what is playing and bins the queue, and
    /// spared the recap. It then won the speaker and read the PREVIOUS
    /// race's result over a green race, with "and they're off" landing a
    /// minute and a half late. A yellow or a red pressed while a reminder
    /// was on the network, or waiting on the playback lock, did the same.
    ///
    /// Everything else stays spared, deliberately. The grid call, the
    /// staging call and a driver release are short instructions to drivers
    /// that a flag does not make untrue, and a badge is a race moment
    /// queued with the finish call rather than ahead of it. A caution still
    /// lets a reminder speak and chatter still cannot drop one - this is
    /// only about a flag. The countdown spares nothing at all and never did
    /// (CutForCountdown).
    ///
    /// Internal rather than private so the rule itself can be read back in
    /// a test - see BugHunt5ShowLatchTests.
    /// </summary>
    internal static bool SurvivesAFlag(AnnouncementEventType type) =>
        IsPriorityAnnouncement(type) && type is not AnnouncementEventType.Reminder;

    /// <summary>
    /// One-off race moments that wait their turn instead of being dropped
    /// when something else is mid-sentence.
    ///
    /// Each fires once per race (or rarely, for a record), so there is no
    /// backlog to fear - but they were being thrown away by the
    /// drop-while-speaking rule meant for lap chatter, and on a busy track
    /// something is nearly always speaking, so "last lap" and "one minute to
    /// go" were routinely never heard.
    ///
    /// Kept separate from IsPriorityAnnouncement on purpose. Priority also
    /// means "speaks through a yellow", "survives a flag clearing the
    /// queue" and "bins the routine lines queued ahead of it" - none of
    /// which is right for these. They only need to not be dropped - which
    /// includes not being binned by a release call or reminder queued after
    /// them (see EnqueueVoiceWork). A flag still clears them.
    /// </summary>
    internal static bool IsWorthWaitingFor(AnnouncementEventType type) => type is
        AnnouncementEventType.LastLap or
        AnnouncementEventType.FinalMinute or
        AnnouncementEventType.TrackRecord or

        // The time left (28 Oct 2026): a race moment like the final minute,
        // and as easily lost behind lap chatter. A newer one replaces one
        // still waiting (see EnqueueVoiceWork), and one that waits too long
        // is not said (EnqueueVoice's sayByMs), so they cannot pile up.
        AnnouncementEventType.MinutesLeft or
        AnnouncementEventType.FinalSeconds or

        // The race's fastest lap (1 Nov 2026). Dropped while another line
        // played, the last one said was often not the one on the board: in
        // the 15:40 race Annie Wayout's 13.992 was called, then Dee Ploy's
        // 12.754 was thrown away behind it and stood unsaid to the flag. A
        // newer one replaces any still waiting, whoever set it (see
        // EnqueueVoiceWork and BinWaitingFastLaps), so only the current
        // holder is ever said.
        AnnouncementEventType.FastLap or

        // The running order. It rations itself to once every N leader laps
        // and uses up that interval the moment it is built, so dropping it
        // because a driver's lap call was still playing meant waiting N more
        // laps for a chance that was likely to be dropped the same way - on
        // a full field the order could go a whole race without being read.
        // A newer report replaces one still waiting (see EnqueueVoiceWork),
        // so it cannot build a backlog either.
        AnnouncementEventType.PositionReport;

    /// <summary>
    /// Where a call coming back from HeldOver goes in the queue: after every
    /// leading instruction (IsPriorityAnnouncement) or race moment
    /// (IsWorthWaitingFor), ahead of any routine line. 17 Oct 2026, Bug Hunt
    /// 8, A06-1.
    /// </summary>
    internal static int HeldOverSlot(IReadOnlyList<AnnouncementEventType> queued)
    {
        var i = 0;

        while (i < queued.Count && (IsPriorityAnnouncement(queued[i]) || IsWorthWaitingFor(queued[i])))
        {
            i++;
        }

        return i;
    }

    /// <summary>
    /// Whether a newer line about a driver REPLACES an older one still
    /// waiting to be spoken.
    ///
    /// True for almost everything, and rightly so: a driver's lap time,
    /// their position, their streak are all STATUS. Two of them queued for
    /// the same driver means the first is already out of date, and saying
    /// it before the second one would be saying something untrue.
    ///
    /// Achievements are not status, they are FACTS - and that is the bug
    /// this exists to name. A driver who takes two badges in one race had
    /// the first one silently deleted from the queue the moment the second
    /// arrived, because both carried the same driver and the same event
    /// type. One race unlocked Hat-trick and Record Holder for the same
    /// driver and a third badge for another; the other driver's was called
    /// and Hat-trick simply never existed. Nothing logged it, because from
    /// the queue's point of view nothing went wrong.
    /// </summary>
    private static bool SupersedesPerDriver(AnnouncementEventType type) =>
        type is not AnnouncementEventType.AchievementUnlocked;

    /// <summary>
    /// The calls about the time left in a timed race (28 Oct 2026). Any one
    /// of them makes any other still waiting out of date: "two minutes" not
    /// yet said when "one minute to go" arrives is no longer true.
    /// </summary>
    internal static bool IsTimeLeftCall(AnnouncementEventType type) => type is
        AnnouncementEventType.MinutesLeft or
        AnnouncementEventType.FinalMinute or
        AnnouncementEventType.FinalSeconds;

    /// <summary>
    /// Queues a voice line, first dropping any PENDING (not yet started)
    /// announcement of the same type for the same transponder. Without
    /// this, a driver on a fast track could rack up several more laps
    /// before an early streak announcement even starts playing, and the
    /// backlog would read out increasingly stale streak counts one after
    /// another - "3 lap streak... 4 lap streak... 5 lap streak..." - long
    /// after the driver has moved on. Only the most recent one matters.
    /// Announcements already speaking are left alone; only queued-but-not-
    /// started items are eligible to be superseded.
    ///
    /// Priority (race-control) announcements are exempt from the
    /// drop-while-speaking rule and clear routine items out of the queue -
    /// see IsPriorityAnnouncement.
    ///
    /// Pass the line's screen text as displayText rather than raising it
    /// before the call. The screens, the Announcer page's feed and the
    /// crowd ambience all take AnnouncementTextReady as "this was just
    /// said", and the lines raised it first and only then came here - where
    /// a busy announcer, the post-race quiet or a muted ghost then dropped
    /// the line. So the Big Screen showed, and the crowd surged for, calls
    /// nobody heard. Raised here, only once the line is actually accepted.
    ///
    /// Returns whether the line was accepted (queued, or spoken at once for
    /// a flag call) or kept for the end of the line (HeldOver), for a caller
    /// that has to remember it was said - see CheckCloseBattles. Accepted is
    /// not a promise it is heard: a flag can still clear it from the queue,
    /// as it always could.
    /// </summary>
    /// <param name="heldOver">
    /// True when this is a call coming back from being kept for the end of
    /// a line (see HeldOver) - already counted as offered, and not to be
    /// kept a second time.
    /// </param>
    /// <param name="lost">
    /// What to run if the call is KEPT for the end of a line and then lost
    /// before it is said (20 Oct 2026, Bug Hunt 8) - see
    /// RoutineCallGetsTheAir. A call turned down at once is the caller's to
    /// undo, from the false this returns.
    /// </param>
    /// <param name="paced">
    /// Run when the false this returns is a HOLD by the pacing dial rather
    /// than a drop (22 Oct 2026, known issues) - so a caller that would offer
    /// the same call again on the next crossing (CheckCloseBattles) can wait
    /// out the gap instead of being held again and again.
    /// </param>
    /// <param name="sayByMs">
    /// The TimingClock time the line must START by, or it is not said -
    /// checked as it comes off the queue and again once it is rendered. A
    /// lap call (IsLapBoundCall) gets LapCallSayWithinMs from now when the
    /// caller gives none. See AnnouncementService.Freshness.
    /// </param>
    /// <param name="facts">
    /// For an AI line written during a race: the race it was written
    /// against. It is dropped once the race makes it untrue, and it neither
    /// bins the lap calls queued ahead of it nor waits in front of the ones
    /// queued after it. See AnnouncementService.Freshness.
    /// </param>
    private bool EnqueueVoiceWork(AnnouncementEventType type, string? transponder, Func<Task> work, string? displayText = null, string? onAir = null, bool heldOver = false, Action? lost = null, Action? paced = null, int? routineWeight = null, long? sayByMs = null, RaceFactsStamp? facts = null)
    {
        // From the moment it was first offered: a call kept for the end of a
        // line comes back with the deadline it went in with.
        sayByMs ??= IsLapBoundCall(type) ? TimingClock.NowMs + LapCallSayWithinMs : null;

        // Written against a race that has already moved on - nothing to queue.
        if (WhyStale(null, facts) is { } untrue)
        {
            StatusMessage?.Invoke($"[voice] Dropped an AI line before it was queued - {untrue}: \"{displayText ?? onAir ?? type.ToString()}\".");
            NoteKeptCallLost(heldOver, type, transponder, displayText, untrue);
            return false;
        }

        // 17 Oct 2026, Bug Hunt 8, A06-5: a call already past the entry
        // points' mute check - a field flush still in its window, a call kept
        // for the end of a line - reached here after the mute and raised its
        // screen text (and could be queued) for a line nobody would hear.
        // SpeakTest, the one path meant to speak while muted, does not come
        // through here.
        if (Muted)
        {
            NoteKeptCallLost(heldOver, type, transponder, displayText, "the announcer was muted");
            return false;
        }

        // A line about two cars (1 Nov 2026) - see AnnouncementService.BattleLines.
        // One whose two cars swapped while it was being written, or while it
        // was kept for the end of the last line, would contradict the board
        // from its first word.
        var battle = BattleLineBeingQueued();

        if (battle is { Overtaken: true })
        {
            StatusMessage?.Invoke($"[voice] Dropped {type} - the two cars it is about swapped before it was queued.");
            NoteKeptCallLost(heldOver, type, transponder, displayText, "the two cars it was about swapped");
            SayBattleLineDropped(battle.Pair);
            return false;
        }

        // A call about a lap the window was told of too late (23 Oct 2026,
        // timing layer, P4b) - see WhileLapCallsAreStale. Only ever true on
        // the thread handling that lap, so a line coming back from being kept
        // (heldOver) or a batch flushing elsewhere is never caught by it.
        if (SkippedAsStale())
        {
            NoteKeptCallLost(heldOver, type, transponder, displayText, "its lap was too old to call by the time the window heard of it");
            return false;
        }

        // A line about a ghost - its lap, its streak, the lead it took -
        // while ghosts are not announced (AnnounceGhostsProvider).
        if (MutedGhost(transponder))
        {
            NoteKeptCallLost(heldOver, type, transponder, displayText, "ghost cars were switched off the announcer");
            return false;
        }

        // Checked before the priority rules, not after: a track record or a
        // personal best set on a lap completed after the flag is still a
        // line about a race that is already over, and "priority" only ever
        // meant "don't drop this behind lap chatter".
        if (_postRaceQuiet && !IsAllowedAfterRaceOver(type))
        {
            StatusMessage?.Invoke($"[voice] Dropped {type} - the race is over; only the wrap-up speaks now.");
            NoteKeptCallLost(heldOver, type, transponder, displayText, "the race finished - only the wrap-up speaks after the flag");
            return false;
        }

        var isPriority = IsPriorityAnnouncement(type);

        // THE FIELD-WIDE PACING GATE AND THE COUNT (14 Oct 2026). This was
        // a bare drop-while-speaking test, first come first served, and
        // nothing anywhere counted what it threw away - about twenty lines
        // out of one eight-lap demo with three cars in it. See
        // AnnouncerDensity for why the club's per-driver cap could not
        // reach this, AnnouncerLoad for why a fault that only ever wrote
        // itself to the console went unreported, and
        // RoutineCallGetsTheAir, which still makes exactly this test
        // first and in the same words.
        //
        // The two lists are asked HERE rather than inside the gate, so
        // everything the queue spares - an instruction, a race moment, the
        // running order - never reaches a dial about taste at all.
        //
        // A routine call refused only because the speaker is busy may be
        // KEPT for the end of the line (HeldOver) - and it comes back in
        // through here, so every rule above still applies to it then.
        //
        // A KEPT call reports true (17 Oct 2026, Bug Hunt 8, A06-2): it is
        // going to be offered again, so FirePairLine must keep the pair's
        // once-per-race claim rather than give it back as if dropped.
        if (!isPriority && !IsWorthWaitingFor(type)
            && !RoutineCallGetsTheAir(
                type,
                out var kept,
                // With its own weight when it has one (23 Oct 2026, bug hunt
                // 10), so a kept rivalry line comes back weighed the same.
                //
                // And with the two cars it is about (3 Nov 2026): the scope
                // that tagged it is long gone by the time it is offered again.
                holdOver: heldOver ? null : () =>
                {
                    using (AboutBattle(battle?.Pair))
                    {
                        return EnqueueVoiceWork(type, transponder, work, displayText, onAir, heldOver: true, lost: lost, routineWeight: routineWeight, sayByMs: sayByMs, facts: facts);
                    }
                },
                alreadyCounted: heldOver,
                lost: lost,
                transponder: transponder,
                words: displayText,
                paced: paced,
                routineWeight: routineWeight))
        {
            return kept;
        }

        // Past every rule that drops a line, so the screens show only what
        // is going to be said - see the summary.
        if (displayText is not null)
        {
            RaiseAnnouncementText(displayText, transponder);
        }

        if (IsInterruptingAnnouncement(type))
        {
            SpeakInterrupting(type, work);
            return true;
        }

        // What the lines binned below were to run if lost - given back once
        // the queue lock is released (23 Oct 2026, bug hunt 10): see
        // BinQueuedLocked.
        List<Action>? giveBack = null;

        lock (_voiceQueueLock)
        {
            if (transponder is not null && SupersedesPerDriver(type))
            {
                BinQueuedLocked(item => item.Type == type && item.Transponder == transponder, ref giveBack);
            }

            // A running order is about the whole field, not the driver it is
            // queued under, so a newer one replaces any still waiting even
            // when the lead has changed hands in between - two orders read
            // back to back, the first already out of date, is the backlog
            // the per-driver rule above exists to prevent.
            if (type == AnnouncementEventType.PositionReport)
            {
                BinQueuedLocked(item => item.Type == AnnouncementEventType.PositionReport, ref giveBack);
            }

            // The race's fastest lap the same way (1 Nov 2026): a newer one
            // means the one waiting is no longer the fastest, whoever set it.
            if (type == AnnouncementEventType.FastLap)
            {
                BinQueuedLocked(item => item.Type == AnnouncementEventType.FastLap, ref giveBack);
            }

            // The same for the clock (28 Oct 2026): a newer time-left call
            // replaces an older one still waiting, so a busy announcer says
            // the time as it is now rather than every mark it missed.
            if (IsTimeLeftCall(type))
            {
                BinQueuedLocked(item => IsTimeLeftCall(item.Type), ref giveBack);
            }

            // Not for an AI line written during a race (8 Nov 2026): it is
            // colour about the race, and the lap calls waiting ahead of it are
            // the race. It used to bin them - "Cleared 1 queued announcement
            // to make way for Reminder" - see AnnouncementService.Freshness.
            if (isPriority && facts is null)
            {
                // Drop any queued-but-not-yet-started routine announcements.
                // Once a flag is out, lap chatter from a moment ago is stale
                // and would only delay the message that actually matters.
                //
                // "Last lap", "one minute to go" and a track record are kept:
                // they are race moments rather than chatter, and a release
                // call or a reminder arriving behind them used to bin them
                // anyway - the very thing IsWorthWaitingFor exists to stop.
                // A real flag still clears them (ClearTheDecksFor), since a
                // yellow or a finish does make them stale.
                var discarded = BinQueuedLocked(item =>
                    !IsPriorityAnnouncement(item.Type) && !IsWorthWaitingFor(item.Type), ref giveBack);
                if (discarded > 0)
                {
                    StatusMessage?.Invoke($"[voice] Cleared {Plural.Of(discarded, "queued announcement")} to make way for {type}.");
                }
            }

            // A call kept for the end of the last line goes NEXT, not to the
            // back: it was kept because it was the most important thing to
            // say the moment the speaker was free, and behind a queue it
            // would be said long after the moment it was about.
            //
            // Next AFTER any instruction or race moment already waiting,
            // though (17 Oct 2026, Bug Hunt 8, A06-1): index 0 put a kept lap
            // call ahead of a release, a grid call or "last lap" queued while
            // the line it waited on was still speaking.
            var at = heldOver ? HeldOverSlot(_voiceQueue.Select(waiting => waiting.Type).ToList()) : _voiceQueue.Count;

            // A lap call goes in front of any AI line still waiting (8 Nov
            // 2026): the 4.936 that was the fastest lap of the race sat
            // behind two of them and was still in the queue at the flag.
            if (IsLapBoundCall(type))
            {
                at = Math.Min(at, LapCallSlot(_voiceQueue.Select(waiting => waiting.Facts is not null).ToList()));
            }

            var queued = new QueuedVoiceItem(type, transponder, OnAir: onAir, Battle: battle, SayByMs: sayByMs, Facts: facts, Work: async () =>
            {
                try
                {
                    // "Quota available again" is no longer worked out here. It
                    // is said where ElevenLabs actually answers - see
                    // NoteElevenLabsAnswered - which is still only a request
                    // ElevenLabs answered (a saved line proves nothing about
                    // the quota), but now includes the flag calls and the
                    // lines rendered in advance that never came through here.
                    await work();
                }
                catch (Exception ex) when (IsQuotaExceeded(ex))
                {
                    // Only reached with the stand-in off (or no Kokoro model
                    // to stand in with) - with it on, Kokoro has already
                    // said this line. The first refusal says the quota is
                    // gone; the rest say this line was skipped.
                    if (!NoteQuotaExceeded(standingIn: false))
                    {
                        StatusMessage?.Invoke("[voice] Still over quota - skipping this announcement.");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Playback failed: {ex.Message}");
                }
            });

            _voiceQueue.Insert(at, queued);

            // Kept beside the line, so a line binned before it is said gives
            // the sponsor's ration (or a battle's claim) back - see
            // BinQueuedLocked.
            if (lost is not null)
            {
                (_lostIfBinned ??= new System.Runtime.CompilerServices.ConditionalWeakTable<QueuedVoiceItem, Action>())
                    .AddOrUpdate(queued, lost);
            }
        }

        GiveBackLost(giveBack);

        _voiceQueueSignal.Release();
        return true;
    }

    /// <summary>
    /// What each queued line is to run if it is thrown away before it is said
    /// - EnqueueVoiceWork's <c>lost</c>. Made on first use (a part file has no
    /// initializers); read and written only under _voiceQueueLock. Weak, so a
    /// line said or binned takes its entry with it.
    /// </summary>
    private System.Runtime.CompilerServices.ConditionalWeakTable<QueuedVoiceItem, Action>? _lostIfBinned;

    /// <summary>
    /// Throws away any fastest-lap call still waiting, on a crossing that has
    /// just beaten it (1 Nov 2026). EnqueueVoiceWork does the same when the
    /// new lap's own call is queued, but that call can be turned down before
    /// it gets there - the per-lap limit, the club's dial, switched off - and
    /// the old holder's line would then be said over the new holder's lap.
    /// </summary>
    private void BinWaitingFastLaps()
    {
        List<Action>? giveBack = null;
        int binned;

        lock (_voiceQueueLock)
        {
            binned = BinQueuedLocked(item => item.Type == AnnouncementEventType.FastLap, ref giveBack);
        }

        GiveBackLost(giveBack);

        if (binned > 0)
        {
            StatusMessage?.Invoke("[voice] Dropped a waiting FastLap - a quicker lap has just beaten it.");
        }
    }

    /// <summary>
    /// Removes the queued lines that match, as List.RemoveAll does, and adds
    /// what each was to run if lost to <paramref name="giveBack"/> - for the
    /// caller to run with <see cref="GiveBackLost"/> AFTER the queue lock is
    /// released (a battle's give-back takes the battles lock). Call only
    /// under _voiceQueueLock.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, bug hunt 10): a sponsored line reserves the
    /// sponsor's ration as it is built, and only a line turned down at the
    /// door gave it back. One binned from the queue - replaced by a newer one
    /// for the same driver, or cleared by a priority line - kept the ration
    /// stamped for three minutes over a line nobody heard, so the sponsor's
    /// name was left off every call for that moment.
    /// </remarks>
    private int BinQueuedLocked(Predicate<QueuedVoiceItem> match, ref List<Action>? giveBack)
    {
        if (_lostIfBinned is { } table)
        {
            foreach (var item in _voiceQueue)
            {
                if (match(item) && table.TryGetValue(item, out var lost))
                {
                    (giveBack ??= new List<Action>()).Add(lost);
                }
            }
        }

        return _voiceQueue.RemoveAll(match);
    }

    /// <summary>Runs what <see cref="BinQueuedLocked"/> collected. Outside the queue lock; never throws.</summary>
    private void GiveBackLost(IEnumerable<Action>? giveBack)
    {
        if (giveBack is null)
        {
            return;
        }

        foreach (var lost in giveBack)
        {
            try
            {
                lost();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Couldn't give back a binned line's reservation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Speaks a flag call at once: the line being spoken is cut off, the
    /// queued chatter behind it is binned, and synthesis starts immediately
    /// rather than waiting for a turn in the queue.
    ///
    /// Deliberately outside the voice queue. Putting it at the front of the
    /// queue would still leave it waiting on the line in progress, and the
    /// whole point is that a yellow can't wait.
    /// </summary>
    private void SpeakInterrupting(AnnouncementEventType type, Func<Task> work)
    {
        // Everything routine goes; a race-control line already queued stays.
        var generation = ClearTheDecksFor(type);

        // The finish call holds the voice queue until it has spoken - taken
        // here, synchronously, so NotifyRaceCompleted can see it was.
        var finishHold = ClaimFinishCallHold(type);

        _ = Task.Run(async () =>
        {
            // The flag's line carries the generation it just started, so it
            // passes its own interrupt check but not a LATER flag's. Without
            // that, a flag whose line was still synthesising could get the
            // speaker after a newer flag that played from memory - "time's
            // up" after the finish call, or "yellow" after the green that
            // lifted it. Set here, inside the task, so it is scoped to this
            // line alone and replaces anything copied from the caller.
            _dequeuedUnderGeneration.Value = generation;

            // Never cut by a countdown (see _countdownCutGeneration) - a
            // flag or a start call is the newest instruction, not something
            // left over. Cleared so nothing copied from the caller applies.
            _dequeuedUnderCountdown.Value = null;

            // Tells SynthesizeAndPlayAsync which call this is, so it waits
            // for the call's own sound AFTER synthesising rather than before
            // - the words are rendered while the sound plays.
            _leadInFor.Value = type;

            Interlocked.Increment(ref _voiceInFlight);

            // Named by its kind rather than carrying a label: a flag call
            // IS the thing the strip should say - "Yellow flag" - and
            // nothing wraps one in a segment.
            BeginSpeaking(type);

            try
            {
                await work();
            }
            catch (Exception ex) when (IsQuotaExceeded(ex))
            {
                // The same handling the queue gives its lines. Flag calls
                // never reach the queue, so a spent quota met here was never
                // recorded: the Mixer never said the quota was gone, and every
                // press of a flag dumped ElevenLabs' raw JSON in the console.
                if (!NoteQuotaExceeded(standingIn: false))
                {
                    StatusMessage?.Invoke($"[voice] Still over quota - skipping {type}.");
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] {type} failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _voiceInFlight);
                EndSpeaking();

                // However the call ended - spoken, dropped, failed - the
                // queue it was holding carries on.
                finishHold?.TrySetResult();
            }
        });
    }

    /// <summary>
    /// ElevenLabsClient surfaces API errors as a generic InvalidOperationException
    /// with the raw response body embedded in the message, so quota errors are
    /// detected by looking for ElevenLabs' own "quota_exceeded" error code in
    /// that text rather than a dedicated exception type.
    /// </summary>
    private static bool IsQuotaExceeded(Exception ex) =>
        ex.Message.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase);

    /// <summary>Independent voice playback loop - see the class-level remarks on why this is separate from sound effects.</summary>
    private async Task RunVoiceQueueLoopAsync()
    {
        while (true)
        {
            await _voiceQueueSignal.WaitAsync();

            // Nothing from the queue speaks until the finish call has - the
            // badges worked out at the flag used to beat it to the speaker.
            // Waited for BEFORE taking the next line, so what is taken is
            // whatever is at the front once the call is done. Always ends:
            // see FinishCallHoldMaxWait.
            await WaitForFinishCallAsync();

            Func<Task>? work;
            int? generation;
            int? countdownGeneration;

            // What this line is, for the race director's strip - see
            // AnnouncementService.OnAirNow. Taken here, with the work,
            // because this is the only point at which the queue knows which
            // line is about to be spoken rather than merely waiting.
            AnnouncementEventType? onAirCall = null;
            string? onAirNamed = null;

            // What the line runs if it is never said - for the mute below.
            Action? lostIfUnsaid = null;

            // The fight a battle line is about, so the board can cut it off.
            BattleLine? battle = null;

            // Its deadline and the race it was written against - see AnnouncementService.Freshness.
            long? sayBy = null;
            RaceFactsStamp? facts = null;
            var dequeuedType = AnnouncementEventType.Reminder;

            lock (_voiceQueueLock)
            {
                if (_voiceQueue.Count > 0)
                {
                    var item = _voiceQueue[0];
                    work = item.Work;
                    onAirCall = item.Type;
                    onAirNamed = item.OnAir;
                    battle = item.Battle;
                    sayBy = item.SayByMs;
                    facts = item.Facts;
                    dequeuedType = item.Type;
                    _battleOnAir = battle;
                    _voiceQueue.RemoveAt(0);

                    if (_lostIfBinned is { } table && table.TryGetValue(item, out var lost))
                    {
                        lostIfUnsaid = lost;
                    }

                    // Captured under the queue lock, which ClearTheDecksFor's
                    // queue purge also takes: a line either gets the
                    // pre-flag generation (and is dropped if still
                    // synthesising when the flag lands) or was never here.
                    // Exempt exactly where the purge is exempt - this read
                    // IsPriorityAnnouncement, which spared a Reminder and
                    // so let a whole stale recap speak over a green. See
                    // SurvivesAFlag.
                    generation = SurvivesAFlag(item.Type)
                        ? null
                        : Volatile.Read(ref _interruptGeneration);

                    // Every line, priority or not: the countdown cuts off
                    // everything still talking. Read under the same lock
                    // CutForCountdown bumps it under.
                    countdownGeneration = Volatile.Read(ref _countdownCutGeneration);

                    // So a pass seen after a flag has cut this battle line
                    // does not stop the flag call instead - see CutSinceTaken.
                    if (battle is not null)
                    {
                        battle.TakenUnderInterrupt = Volatile.Read(ref _interruptGeneration);
                        battle.TakenUnderCountdown = countdownGeneration;
                    }
                }
                else
                {
                    work = null;
                    generation = null;
                    countdownGeneration = null;
                }
            }

            // Flows into work() and everything it awaits; reset every
            // iteration so nothing leaks from one line to the next.
            _dequeuedUnderGeneration.Value = generation;
            _dequeuedUnderCountdown.Value = countdownGeneration;
            _dequeuedBattle.Value = battle;
            _dequeuedFreshness.Value = (sayBy, facts);

            // Gone stale while it waited its turn (8 Nov 2026) - a lap call
            // past its few seconds, or an AI line the race has overtaken.
            // Dropped the way a mute drops it, below, with the reason said.
            var stale = work is null ? null : WhyStale(sayBy, facts);

            if (stale is not null)
            {
                StatusMessage?.Invoke(facts is not null
                    ? $"[voice] Dropped a waiting AI line unheard - {stale}."
                    : $"[voice] Dropped {dequeuedType} unheard - {stale}.");
            }

            // Mute can land between an item being queued and it being
            // played, so check again here rather than trusting the
            // enqueue-time guard alone.
            //
            // Its reservation is given back (23 Oct 2026, bug hunt 10): the
            // work never runs, so nothing else would.
            if (Muted || stale is not null)
            {
                if (lostIfUnsaid is not null)
                {
                    GiveBackLost(new[] { lostIfUnsaid });
                }

                if (battle is not null)
                {
                    lock (_voiceQueueLock)
                    {
                        if (ReferenceEquals(_battleOnAir, battle))
                        {
                            _battleOnAir = null;
                        }
                    }
                }

                continue;
            }

            // Heard, or about to be - so its battle watch was not wasted.
            if (battle is not null)
            {
                SayBattleLineOnAir(battle.Pair);
            }

            if (work is null)
            {
                continue; // shouldn't happen, but never let a spurious signal kill the loop
            }

            _isVoiceSpeaking = true;
            Interlocked.Increment(ref _voiceInFlight);

            // Raised around the ACTUAL playback, so anything ducking under
            // the voice covers the real speaking window rather than the
            // moment a line was queued - those can be seconds apart when
            // the queue is busy.
            BeginSpeaking(onAirCall, onAirNamed);

            try
            {
                await work();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice queue] Unexpected playback error: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _voiceInFlight);
                _isVoiceSpeaking = false;
                EndSpeaking();

                if (battle is not null)
                {
                    lock (_voiceQueueLock)
                    {
                        if (ReferenceEquals(_battleOnAir, battle))
                        {
                            _battleOnAir = null;
                        }
                    }
                }
            }

            // The line has ended, so the one call kept for its end - if a
            // bigger one arrived while it was speaking - gets its turn.
            // See HeldOver.
            try
            {
                OfferTheHeldOverCall();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice queue] Couldn't offer the kept call: {ex.Message}");
            }
        }
    }
}
