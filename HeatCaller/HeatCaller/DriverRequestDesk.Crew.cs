using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A driver's own crew and notes from their phone (15 Nov 2026, driver crew
/// plan, DriverAsk.Crew).
///
/// ONLY WHAT COULD BE SPOKEN WAITS FOR THE DESK. A voice pick goes straight
/// on: it can only be one the club has left visible (DriverCrew.Pickable),
/// so there is nothing for anybody to check, and it is applied the moment
/// the laptop has it (<see cref="FileWaitingCrewVoices"/>). The names and
/// the notes are words the PA will say, so they wait to be approved like
/// any details change. A request with both is split: the voices apply, and
/// the words wait.
/// </summary>
internal sealed partial class DriverRequestDesk
{
    /// <summary>The voice fields of a crew request.</summary>
    internal static readonly string[] CrewVoiceFields = { "pVoice", "tVoice" };

    /// <summary>The fields of a crew request that are words, in the order the desk is shown them.</summary>
    internal static readonly string[] CrewTextFields = { "pName", "pSay", "tName", "tSay", "goal", "thanks", "story" };

    /// <summary>The notes among them - what a junior may only send with a parent (plan 5.2).</summary>
    internal static readonly string[] CrewNoteFields = { "goal", "thanks", "story" };

    /// <summary>
    /// The names among them, with their "said as" spellings (19 Nov 2026) -
    /// what an under-13 may not send from a phone at all (15 Nov 2026).
    /// </summary>
    internal static readonly string[] CrewNameFields = { "pName", "pSay", "tName", "tSay" };

    /// <summary>The "said as" spellings among them (19 Nov 2026).</summary>
    internal static readonly string[] CrewSaidAsFields = { "pSay", "tSay" };

    /// <summary>
    /// One word field of a crew request as the approve dialog shows it (19
    /// Nov 2026): what they sent, what the profile has now, and the most it
    /// may be.
    /// </summary>
    internal sealed record CrewField(string Key, string Caption, string Current, string Proposed, int MaxChars);

    /// <summary>The word fields a crew request carries, in <see cref="CrewTextFields"/> order.</summary>
    internal IReadOnlyList<CrewField> CrewFieldsOf(DriverRequest request, DriverProfile? profile) =>
        CrewTextFields
            .Where(request.Texts.ContainsKey)
            .Select(key => new CrewField(
                key,
                CrewCaption(key),
                profile is null ? string.Empty : CrewTextOf(profile, key),
                request.Text(key),
                DriverRequestStore.LimitOf(key)))
            .ToList();

    /// <summary>Said to the phone when the desk approved some of a crew request, not all of it (19 Nov 2026).</summary>
    internal const string CrewPartlySaid = "Some of it is in - ask the desk about the rest";

    /// <summary>Said to the phone when the desk reworded something before approving it (19 Nov 2026).</summary>
    internal const string CrewEditedSaid = "Approved, with a change from the desk";

    /// <summary>Why the desk can't apply an under-13's crew names from a phone (15 Nov 2026).</summary>
    internal const string Under13NamesRefusal =
        "An under-13's crew names are picked at the desk, with a parent there, not from a phone. Decline it, "
        + "and type them on their profile. (No age band set counts as under 13 - set it in Papers.)";

    /// <summary>
    /// The voice a phone's key means now, or null when it is not one drivers
    /// may pick (hidden, reserved, another engine, or never listed). Set by
    /// the host to MainForm.CrewVoiceForKey; without it every pick waits.
    /// </summary>
    public Func<string, CrewVoicePick?>? CrewVoiceOf { get; set; }

    /// <summary>
    /// Whether the voices of the engine in use have been listed yet (15 Nov
    /// 2026, bug hunt 21). Until they are, a pick is left waiting rather than
    /// declined as gone - at start-up, after an engine switch, or while the
    /// listing fails, no key is known. Set by the host; without it the
    /// listing is taken as known, as before.
    /// </summary>
    public Func<bool>? CrewVoicesListed { get; set; }

    /// <summary>
    /// Whether tonight's racing is done - races run and none left - so a
    /// goal sent now is for the next night (plan 2.4). Set by the host;
    /// without it a goal is always for the night it was sent on.
    /// </summary>
    public Func<bool>? TonightIsOver { get; set; }

    /// <summary>The next night the club has announced (AppSettings.NextNight), or null.</summary>
    public Func<DateTime?>? NextNightDay { get; set; }

    /// <summary>Said to the phone when its goal is kept for the next night.</summary>
    internal const string GoalHeldSaid = "Your goal is kept for your next race night";

    /// <summary>Said to the phone when its words went in but a voice it picked could not.</summary>
    internal const string WordsInVoiceGoneSaid = "Your names and notes are in, but that voice isn't available any more - pick another";

    /// <summary>The field a crew request carries, once its voices are filed, when one of them could not be had.</summary>
    internal const string VoiceGoneMark = "voiceGone";

    /// <summary>Said to the phone for a voice it can no longer have.</summary>
    internal const string CrewVoiceGoneSaid = "That voice isn't available any more - pick another.";

    /// <summary>Said to the phone for a request whose only change was its voices.</summary>
    internal const string CrewVoiceAppliedSaid = "Applied";

    /// <summary>
    /// Applies the voice picks of every crew request still waiting, and
    /// answers the ones that were only voices. Returns how many voices
    /// changed. On the UI thread's roster tick, beside the setups.
    /// </summary>
    public int FileWaitingCrewVoices()
    {
        if (CrewVoiceOf is null || !_settings.DriversPickCrew)
        {
            return 0;
        }

        var changed = 0;

        foreach (var request in _store.Waiting.Where(r => r.Kind == DriverAsk.Crew).ToList())
        {
            changed += FileCrewVoices(request);
        }

        return changed;
    }

    /// <summary>
    /// One request's voice picks, applied: each key the club still offers
    /// goes onto the profile ("" puts the club's own back), and the voice
    /// fields come out of the request. A request with nothing else is
    /// answered - "Applied", or declined when no voice it asked for could be
    /// had. Returns how many voices changed.
    /// </summary>
    internal int FileCrewVoices(DriverRequest request)
    {
        var asked = CrewVoiceFields.Where(request.Texts.ContainsKey).ToList();

        if (asked.Count == 0 || DriverOf(request) is not { } profile || CrewVoiceOf is null)
        {
            return 0;
        }

        // Not known is not gone: left whole for the next tick (see CrewVoicesListed).
        if (CrewVoicesListed?.Invoke() == false && asked.Any(field => request.Text(field).Trim().Length > 0))
        {
            return 0;
        }

        var updated = ProfileStore.Snapshot(profile);
        var applied = 0;
        var gone = 0;

        foreach (var field in asked)
        {
            var key = request.Text(field).Trim();
            CrewVoicePick? pick = null;

            if (key.Length > 0)
            {
                pick = CrewVoiceOf(key);

                if (pick is null)
                {
                    gone++;
                    continue;
                }
            }

            if (field == "pVoice")
            {
                updated.CrewPrincipalVoice = pick;
            }
            else
            {
                updated.CrewTechVoice = pick;
            }

            applied++;
        }

        if (applied > 0)
        {
            _profiles.Upsert(updated);

            // No names: the session log goes into support bundles.
            _log($"[driver requests] A driver picked {(applied == 1 ? "a voice" : "voices")} for their own crew from their phone.");
        }

        // A VOICE THAT COULD NOT BE HAD, in a request with words still to
        // wait for the desk: marked, so the answer the phone gets when the
        // desk approves the words says so (15 Nov 2026).
        if (!_store.DropTexts(request.Id, asked, gone > 0 && CrewTextFields.Any(request.Texts.ContainsKey) ? VoiceGoneMark : null))
        {
            return applied;
        }

        // Nothing left for the desk to read: answered here.
        if (!CrewTextFields.Any(request.Texts.ContainsKey))
        {
            _store.Answer(
                request.Id,
                applied > 0 ? DriverAsk.Approved : DriverAsk.Declined,
                applied > 0 ? CrewVoiceAppliedSaid : CrewVoiceGoneSaid,
                DateTime.UtcNow);
        }

        return applied;
    }

    /// <summary>One line per change a crew request asks for ("Principal's name: 'Gravel' (was empty)").</summary>
    internal IReadOnlyList<string> CrewLines(DriverRequest request, DriverProfile? profile)
    {
        var lines = new List<string>();

        foreach (var key in CrewVoiceFields.Concat(CrewTextFields))
        {
            if (!request.Texts.TryGetValue(key, out var proposed))
            {
                continue;
            }

            if (CrewVoiceFields.Contains(key))
            {
                var name = proposed.Length == 0 ? "the club's own" : CrewVoiceOf?.Invoke(proposed)?.Name ?? "a voice no longer offered";
                lines.Add($"{CrewCaption(key)}: {name} (applies by itself)");
                continue;
            }

            var current = profile is null ? null : CrewTextOf(profile, key);
            lines.Add($"{CrewCaption(key)}: " + (proposed.Length == 0 ? "cleared" : $"'{proposed}'")
                      + (current is null ? string.Empty : current.Length == 0 ? " (was empty)" : $" (was '{current}')"));
        }

        if (lines.Count == 0)
        {
            lines.Add("No changes");
        }

        return lines;
    }

    /// <summary>
    /// What the desk should know before approving a crew request: a junior's
    /// words (spoken over the PA and the stream), and any note that names
    /// another driver - "Bill cheats" must never reach the PA unread.
    /// </summary>
    internal IEnumerable<string> CrewNotes(DriverRequest request, DriverProfile? profile)
    {
        if (profile?.IsJunior == true)
        {
            yield return "Junior";

            if (!profile.CrewNotesParentConsent)
            {
                yield return "No parent's consent on their profile - their notes won't be said until it is ticked";
            }
        }

        if (request.Text(VoiceGoneMark).Length > 0)
        {
            yield return "A voice they picked isn't offered any more - their phone is told when you answer";
        }

        var others = _profiles.All
            .Where(p => profile is null || !string.Equals(p.Transponder, profile.Transponder, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => new[] { p.DriverName, p.AnnouncerName })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        foreach (var key in CrewTextFields)
        {
            if (request.Texts.TryGetValue(key, out var text) && DriverCrew.NamesAnotherDriver(text, others) is { } named)
            {
                yield return $"Names another driver ({named})";
                yield break;
            }
        }
    }

    /// <summary>
    /// The warning the approve question carries for a junior (plan 5.2,
    /// rule 1): their words go out over the PA and the stream.
    /// </summary>
    internal const string JuniorCrewWarning =
        "A junior's words, spoken over the PA and the stream. Check for real names, a school, an age or where they live.";

    /// <summary>
    /// Applies a crew request's words to the driver's profile - the names
    /// and notes; the voices have gone already - with an undo, as
    /// ApplyDetails does. A name that is not a name is refused, and so is any
    /// junior's note from a phone (the relay refuses it too).
    /// </summary>
    public bool ApplyCrew(DriverRequest request, out string refusal) => ApplyCrew(request, null, out refusal);

    /// <summary>
    /// ApplyCrew with what the desk ticked (19 Nov 2026, per-field approve):
    /// each ticked field as the desk left it in its box. A field not in
    /// <paramref name="chosen"/> is left as it is on the profile. Null takes
    /// every field as the phone sent it.
    /// </summary>
    public bool ApplyCrew(DriverRequest request, IReadOnlyDictionary<string, string>? chosen, out string refusal)
    {
        if (request.Kind != DriverAsk.Crew || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered, or replaced by a newer one from the same driver.";
            return false;
        }

        if (DriverOf(request) is not { } profile)
        {
            refusal = "Nobody on this laptop has that badge, so there is no profile to change. Discard it.";
            return false;
        }

        if (request.Proof != DriverAsk.ByPin)
        {
            refusal = "A crew request needs the driver's personal PIN, and this one came with the badge code only.";
            return false;
        }

        // WHAT IS APPLIED (19 Nov 2026, per-field approve): the fields the
        // desk ticked, as it left them in their boxes - or, with no choice
        // given, everything as the phone sent it. Every rule below is read
        // against these, so unticking a field the rules refuse lets the
        // rest through.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in CrewTextFields)
        {
            if (!request.Texts.TryGetValue(key, out var sent))
            {
                continue;
            }

            if (chosen is null)
            {
                texts[key] = sent;
            }
            else if (chosen.TryGetValue(key, out var kept))
            {
                texts[key] = kept ?? string.Empty;
            }
        }

        if (chosen is not null && texts.Count == 0)
        {
            refusal = "Nothing is ticked. Tick a change to approve, or decline the request.";
            return false;
        }

        string Chosen(string key) => texts.TryGetValue(key, out var text) ? text : string.Empty;

        foreach (var key in new[] { "pName", "tName" })
        {
            if (Chosen(key) is { Length: > 0 } typed && !DriverCrew.IsAName(typed))
            {
                refusal = $"\"{typed}\" isn't a name the announcer can say - letters, spaces, hyphens and apostrophes only. Fix it, or untick it.";
                return false;
            }
        }

        foreach (var key in CrewSaidAsFields)
        {
            if (Chosen(key) is { Length: > 0 } typed && !DriverCrew.IsASaidAs(typed))
            {
                refusal = $"\"{typed}\" can't be read out - a \"said as\" spelling is letters, spaces, hyphens and apostrophes only. Fix it, or untick it.";
                return false;
            }
        }

        // AN UNDER-13'S CREW NAMES ARE PICKED AT THE DESK (15 Nov 2026,
        // children's privacy plan 2.1): never from a phone, with a parent
        // there. A voice pick is still fine, and so is clearing a name. A
        // junior with no age band counts as under 13 (TreatAsUnder13). The
        // relay refuses these too; this is for an ask sent before it did.
        if (profile.TreatAsUnder13 && CrewNameFields.Any(k => Chosen(k).Length > 0))
        {
            refusal = Under13NamesRefusal;
            return false;
        }

        // A junior's notes are only ever typed at the desk (15 Nov 2026: the
        // switch that let a club take them from phones, with a parent's tick,
        // was removed). Clearing one is fine: an empty note says nothing.
        if (profile.IsJunior && CrewNoteFields.Any(k => Chosen(k).Length > 0))
        {
            refusal = "A junior's notes are typed at the desk, with a parent there, not sent from a phone. Decline it, "
                      + "and type them on their profile with the parent's consent ticked.";
            return false;
        }

        var before = ProfileStore.Snapshot(profile);
        var updated = ProfileStore.Snapshot(profile);
        var goalLater = false;

        var edited = false;

        foreach (var key in CrewTextFields)
        {
            if (!texts.TryGetValue(key, out var raw))
            {
                continue;
            }

            var text = DriverCrew.Clean(raw);
            var limit = DriverRequestStore.LimitOf(key);
            text = text.Length > limit ? text[..limit].TrimEnd() : text;
            edited |= !string.Equals(text, DriverCrew.Clean(request.Text(key)), StringComparison.Ordinal);

            switch (key)
            {
                case "pName":
                    updated.CrewPrincipalName = text;
                    break;
                case "pSay":
                    updated.CrewPrincipalSaidAs = text;
                    break;
                case "tName":
                    updated.CrewTechName = text;
                    break;
                case "tSay":
                    updated.CrewTechSaidAs = text;
                    break;
                case "goal":
                    var (night, held) = DriverCrew.GoalNight(
                        DateTime.SpecifyKind(request.AskedUtc, DateTimeKind.Utc).ToLocalTime(),
                        TonightIsOver?.Invoke() == true && DriverRequestStore.NightOf(request.AskedUtc) == DriverRequestStore.NightOf(DateTime.UtcNow),
                        NextNightDay?.Invoke());
                    updated.CrewGoal = text;
                    updated.CrewGoalNight = text.Length == 0 ? null : night;
                    updated.CrewGoalHeld = text.Length > 0 && held;
                    goalLater = text.Length > 0 && (held || night > DriverRequestStore.NightOf(request.AskedUtc));
                    break;
                case "thanks":
                    updated.CrewThanks = text;
                    break;
                case "story":
                    updated.CrewCarStory = text;
                    break;
            }
        }

        var name = NameOf(profile);
        var transponderKey = profile.Transponder;
        var after = JsonSerializer.Serialize(updated);
        var profiles = _profiles;
        var store = _store;
        var id = request.Id;

        _undo.Capture($"Apply {name}'s crew and notes (their phone was told yes)", () =>
        {
            var nowOnFile = profiles.Get(transponderKey);

            if (nowOnFile is null || JsonSerializer.Serialize(nowOnFile) != after)
            {
                throw new InvalidOperationException(
                    $"{name} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
            }

            profiles.PutBack(new[] { before }, Array.Empty<string>());
            store.Reopen(id);
        });

        _profiles.Upsert(updated);

        if (_profiles.Get(transponderKey) is { } saved)
        {
            after = JsonSerializer.Serialize(saved);
        }

        // What the phone is told: a voice it could not have first, then
        // that only some of it went in, or went in reworded (19 Nov 2026),
        // then a goal kept for the next night.
        var partly = CrewTextFields.Any(k => request.Texts.ContainsKey(k) && !texts.ContainsKey(k));
        var said = request.Text(VoiceGoneMark).Length > 0
            ? WordsInVoiceGoneSaid
            : partly ? CrewPartlySaid
            : edited ? CrewEditedSaid
            : goalLater ? GoalHeldSaid : string.Empty;

        _store.Answer(request.Id, DriverAsk.Approved, said, DateTime.UtcNow);
        _log("[driver requests] Applied a driver's own crew names and notes from their phone.");

        refusal = string.Empty;
        return true;
    }

    internal static string CrewCaption(string key) => key switch
    {
        "pName" => "Principal's name",
        "pSay" => "Principal said as",
        "tName" => "Tech lead's name",
        "tSay" => "Tech lead said as",
        "pVoice" => "Principal's voice",
        "tVoice" => "Tech lead's voice",
        "goal" => "Tonight's goal",
        "thanks" => "Thanks",
        "story" => "Car's story",
        _ => key,
    };

    private static string CrewTextOf(DriverProfile profile, string key) => key switch
    {
        "pName" => profile.CrewPrincipalName ?? string.Empty,
        "pSay" => profile.CrewPrincipalSaidAs ?? string.Empty,
        "tName" => profile.CrewTechName ?? string.Empty,
        "tSay" => profile.CrewTechSaidAs ?? string.Empty,
        "goal" => profile.CrewGoal ?? string.Empty,
        "thanks" => profile.CrewThanks ?? string.Empty,
        "story" => profile.CrewCarStory ?? string.Empty,
        _ => string.Empty,
    };
}
