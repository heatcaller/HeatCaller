using System.Globalization;

namespace HeatCaller;

/// <summary>
/// One line's trip from the lap it is about to the room hearing it - every
/// step stamped on TimingClock, so the console can say where the time went.
/// </summary>
/// <remarks>
/// WHY (10 Nov 2026, voice speed plan, phase 0). With eleven_v4_turbo the
/// render fell from seconds to under one, and the calls still got lost -
/// 22 in one race "still speaking the previous one". The render had been
/// the only step anyone could see (the "Rendered in" line, and only past
/// 1.5 s), so nobody could say what the rest of the time was: the batching
/// windows, the wait in the queue, the silences either side of a line, the
/// device's buffer, the fixed beat after it. Every change to the queue is
/// judged against these numbers, so they come first.
///
/// Filled in by the voice loop (taken, done), SynthesizeAndPlayAsync
/// (render, speaker) and PlayAsync (first pulled, lengths). A line that
/// renders twice inside one piece of work - none does today - keeps its
/// first render: the setters below only write a stamp that is still empty.
///
/// Read on the voice loop only, after the work is done. Written from the
/// work and from the audio thread's first read, which happens before the
/// work's await on the bus returns - so the loop never reads a half-set line.
/// </remarks>
internal sealed class VoiceLineTiming
{
    /// <summary>
    /// How far ahead of the speaker the output reads - its buffer less the
    /// one being played - so a line first pulled now is heard about this
    /// much later. The same sum the level meters make (AudioLevels.Feeder).
    /// An estimate: it is what the device was asked for, not what it gave.
    /// </summary>
    internal static readonly long DeviceAheadMs =
        AudioBus.OutputLatencyMs - AudioBus.OutputLatencyMs / Math.Max(1, AudioBus.OutputBuffers);

    internal required AnnouncementEventType Type { get; init; }

    /// <summary>When the car crossed the line, for a call about that lap; null for any other call.</summary>
    internal long? LapSeenAtMs { get; init; }

    /// <summary>When it went into the voice queue.</summary>
    internal required long QueuedAtMs { get; init; }

    /// <summary>When the voice loop took it off the queue - from here the speaker counts as busy.</summary>
    internal required long TakenAtMs { get; init; }

    private long? _renderStartMs;
    private long? _renderEndMs;
    private long? _speakerAtMs;

    /// <summary>-1 until the output first reads the line. Written on the audio thread.</summary>
    private long _firstPulledMs = -1;

    internal long? RenderStartMs => _renderStartMs;
    internal long? RenderEndMs => _renderEndMs;

    /// <summary>When it had the playback lock and was past every check that can still drop it.</summary>
    internal long? SpeakerAtMs => _speakerAtMs;

    /// <summary>When the output first read it - the start of its lead-in silence. Null if it never played.</summary>
    internal long? FirstPulledMs => Interlocked.Read(ref _firstPulledMs) is var pulled and >= 0 ? pulled : null;

    /// <summary>When the loop got the speaker back.</summary>
    internal long? DoneAtMs { get; set; }

    /// <summary>"ElevenLabs eleven_v4_turbo", "a saved take", "Kokoro" - see RenderedBy.</summary>
    internal string? RenderedBy { get; set; }

    internal int Characters { get; set; }

    internal long LeadMs { get; set; }
    internal long SpeechMs { get; set; }
    internal long TailMs { get; set; }

    /// <summary>Why it was not said, when that is known; a line that never played and says nothing here was dropped by a check with its own console line.</summary>
    internal string? DroppedBecause { get; set; }

    internal void RenderStarted(long atMs) => _renderStartMs ??= atMs;

    internal void RenderEnded(long atMs) => _renderEndMs ??= atMs;

    internal void GotTheSpeaker(long atMs) => _speakerAtMs ??= atMs;

    /// <summary>On the audio thread, from FirstReadSampleProvider.</summary>
    internal void FirstPulled(long atMs) => Interlocked.CompareExchange(ref _firstPulledMs, Math.Max(0, atMs), -1);

    internal bool Played => FirstPulledMs is not null;

    /// <summary>When its first word reached the room, as near as the app can know - see DeviceAheadMs.</summary>
    internal long? WordsHeardAtMs => FirstPulledMs is { } pulled ? pulled + LeadMs + DeviceAheadMs : null;

    /// <summary>The lap to the first word: the number the room feels.</summary>
    internal long? LapToWordsMs => LapSeenAtMs is { } lap && WordsHeardAtMs is { } words ? words - lap : null;

    /// <summary>The lap to the queue: the per-lap batch and the field window.</summary>
    internal long? BatchedMs => LapSeenAtMs is { } lap ? QueuedAtMs - lap : null;

    internal long WaitedMs => TakenAtMs - QueuedAtMs;

    internal long? RenderMs => RenderStartMs is { } start && RenderEndMs is { } end ? end - start : null;

    /// <summary>How long the speaker was taken for this line - render, silences, the beat after it and all.</summary>
    internal long? SpeakerHeldMs => DoneAtMs is { } done ? done - TakenAtMs : null;

    /// <summary>One console line: where this line's time went.</summary>
    internal string Describe()
    {
        var parts = new List<string>();

        if (BatchedMs is { } batched)
        {
            parts.Add($"batched {S(batched)}");
        }

        parts.Add($"waited {S(WaitedMs)}");

        if (RenderMs is { } render)
        {
            parts.Add($"render {S(render)}" + (RenderedBy is { Length: > 0 } by ? $" ({by}, {Characters} chars)" : ""));
        }
        else if (RenderedBy is { Length: > 0 } ahead)
        {
            // A line rendered before it was queued - a recap, a release call.
            parts.Add($"{ahead} ({Characters} chars)");
        }

        if (RenderEndMs is { } rendered && SpeakerAtMs is { } speaker)
        {
            parts.Add($"to speaker {S(speaker - rendered)}");
        }

        if (Played)
        {
            parts.Add($"lead {S(LeadMs)} + device ~{S(DeviceAheadMs)}");
        }

        var head = LapToWordsMs is { } lapToWords
            ? $"lap to first word {S(lapToWords)}"
            : Played ? "said" : "not said" + (DroppedBecause is { Length: > 0 } why ? $" ({why})" : "");

        var held = SpeakerHeldMs is { } heldMs
            ? Played
                ? $" | speaker held {S(heldMs)} for {S(SpeechMs)} of words"
                : $" | speaker held {S(heldMs)}"
            : "";

        return $"[timing] {Type}: {head} - {string.Join(", ", parts)}{held}.";
    }

    internal static string S(long ms) =>
        (ms / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "s";
}

/// <summary>
/// Every line the voice loop took this race, for the summary said at the
/// checkered flag. Kept from staging to the next staging, in memory only,
/// like AnnouncerLoad beside it.
/// </summary>
internal sealed class VoiceTimingTally
{
    /// <summary>A race is a few hundred lines at most; past this the oldest go.</summary>
    internal const int MostKept = 1_000;

    private readonly object _gate = new();
    private readonly List<VoiceLineTiming> _lines = new();

    private int _renderedAhead;

    /// <summary>A line rendered ahead this race (10 Nov 2026) - see AnnouncementService.RenderAhead. Those used are the lines whose RenderedBy says so.</summary>
    internal void NoteRenderedAhead() => Interlocked.Increment(ref _renderedAhead);

    internal int RenderedAhead => Volatile.Read(ref _renderedAhead);

    internal void Reset()
    {
        lock (_gate)
        {
            _lines.Clear();
        }

        Interlocked.Exchange(ref _renderedAhead, 0);
    }

    internal void Add(VoiceLineTiming line)
    {
        lock (_gate)
        {
            if (_lines.Count >= MostKept)
            {
                _lines.RemoveAt(0);
            }

            _lines.Add(line);
        }
    }

    internal IReadOnlyList<VoiceLineTiming> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <summary>
    /// The race in one line: how quickly lap calls reached the room, how
    /// long renders took, how much of the speaker's time was words, and -
    /// from AnnouncerLoad - how many calls never got near it. Null when the
    /// loop took nothing this race.
    /// </summary>
    internal string? Summary(int offered, int dropped, int held)
    {
        var lines = Lines;

        if (lines.Count == 0)
        {
            return null;
        }

        var played = lines.Where(l => l.Played).ToList();
        var lapToWords = played.Select(l => l.LapToWordsMs).OfType<long>().ToList();
        var renders = lines.Where(l => l.RenderedBy is { } by && by.StartsWith("ElevenLabs", StringComparison.Ordinal))
            .Select(l => l.RenderMs).OfType<long>().ToList();
        var waits = lines.Select(l => l.WaitedMs).ToList();
        var heldMs = lines.Select(l => l.SpeakerHeldMs).OfType<long>().Sum();
        var wordsMs = played.Sum(l => l.SpeechMs);

        var parts = new List<string>
        {
            $"{played.Count} of {lines.Count} lines taken were said",
        };

        if (lapToWords.Count > 0)
        {
            parts.Add($"lap calls reached the room in {VoiceLineTiming.S(Percentile(lapToWords, 50))} (median), "
                + $"{VoiceLineTiming.S(Percentile(lapToWords, 90))} (90%), over {lapToWords.Count}");
        }

        if (renders.Count > 0)
        {
            parts.Add($"ElevenLabs rendered in {VoiceLineTiming.S(Percentile(renders, 50))} (median), "
                + $"{VoiceLineTiming.S(Percentile(renders, 90))} (90%), over {renders.Count}");
        }

        parts.Add($"waited in the queue {VoiceLineTiming.S(Percentile(waits, 50))} (median)");

        if (heldMs > 0)
        {
            parts.Add($"words were {Percent(wordsMs, heldMs)} of the {VoiceLineTiming.S(heldMs)} the speaker was held");
        }

        if (RenderedAhead > 0)
        {
            // Every one not used was a request paid for and never heard.
            var used = lines.Count(l => l.RenderedBy?.StartsWith("rendered ahead", StringComparison.Ordinal) == true);
            parts.Add($"{used} of {RenderedAhead} lines rendered ahead were used");
        }

        parts.Add($"{dropped} of {offered} routine calls lost behind a line being spoken, {held} held by the pacing dial");

        return "[timing] This race: " + string.Join("; ", parts) + ".";
    }

    /// <summary>Nearest-rank percentile of a non-empty list.</summary>
    internal static long Percentile(IReadOnlyCollection<long> values, int percent)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);

        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private static string Percent(long part, long whole) =>
        whole <= 0 ? "0%" : (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";
}
