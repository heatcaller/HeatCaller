namespace HeatCaller;

/// <summary>
/// The announcer's last few lines, on their way to anyone listening from
/// home.
///
/// WHAT THIS IS FOR (13 Oct 2026)
///
/// The club page has had a Listen button since the relay side of this
/// landed. This is the other half: the laptop taking what the announcer
/// just said and putting it where that button can reach it. A driver's
/// family who could not get to the track hear the race called, a lap or
/// two behind the room, which is close enough to feel like being there.
///
/// WHY THE QUEUE IS SHALLOW AND THROWS THE OLDEST AWAY
///
/// Live audio that arrives late is not audio, it is noise: a call for a
/// pass that happened ninety seconds ago, read over the pass that is
/// happening now. So this holds <see cref="MostHeld"/> lines and no more,
/// and when the connection cannot keep up it drops the OLDEST rather than
/// refusing the newest. A listener on a bad night hears gaps; they never
/// hear the night out of order.
///
/// That is the opposite of how this app treats results, which are queued,
/// retried and never dropped while there is room. The difference is that
/// a result is still true an hour later and a lap call is not.
///
/// AND WHY IT DOES NOT GO THROUGH THE RESULTS QUEUE
///
/// It would have been two lines to push these through
/// <c>RelayPublisher.Enqueue</c> with an item id, the way results and the
/// results index go. That queue drops its OLDEST when it fills, and it is
/// shared - so a busy evening of announcing would have quietly pushed a
/// session's results off the front of it. Results are the thing a club
/// actually needs the relay for. Audio gets its own queue, its own cap and
/// its own drop policy for that reason, and nothing it does can cost a
/// club a result.
///
/// MP3 ONLY, AND ALREADY ENCODED
///
/// Nothing here touches NAudio or a codec. It is handed bytes that are
/// already an mp3 and judges them; the sniffing and the encoding happen on
/// the audio side, where that code already lives
/// (AnnouncementService.ListenAlong). What is left here is the policy -
/// what is worth sending, what is too big, what has gone stale - and the
/// policy is the part worth testing.
/// </summary>
internal sealed class ListenAlong
{
    /// <summary>The relay channel these land in. Must match the relay's own registry (relay/src/types.ts).</summary>
    internal const string Channel = "listen";

    /// <summary>
    /// The largest mp3 that can be sent.
    ///
    /// The relay takes one listen document of 256 KB
    /// (COLLECTION_ITEM_MAX_BYTES). The audio travels base64'd inside
    /// JSON, which costs a third on top, so the raw mp3 has to come in
    /// under about 192 KB - and there is the rest of the document to pay
    /// for as well. 180 KB leaves room for the words, the timestamp and
    /// the braces without having to be exact about any of them.
    ///
    /// At the 96 kbps the encoder asks for first, that is a little over
    /// fifteen seconds. Lap calls and flag calls are two or three; a race
    /// recap can be a minute, and a recap is one of the things this will
    /// refuse. See <see cref="Verdict.TooLong"/> - it is refused out loud
    /// rather than truncated, because half a sentence is worse than none.
    /// </summary>
    internal const int MostBytes = 180 * 1024;

    /// <summary>
    /// The largest finished document the relay will take for one line -
    /// its COLLECTION_ITEM_MAX_BYTES.
    ///
    /// Written down here as well as there because the two ends cannot
    /// reference each other, and because <see cref="MostBytes"/> is only
    /// correct as long as this is what it is being measured against. The
    /// test that proves one fits the other is
    /// ListenAlongTests.The_largest_line_allowed_still_fits_one_relay_document.
    /// </summary>
    internal const int MostDocumentBytes = 256 * 1024;

    /// <summary>
    /// How many lines wait at once.
    ///
    /// Six, at two or three seconds each, is about fifteen seconds of
    /// slack - enough to ride out one slow request without the listener
    /// noticing, and short enough that a connection which has genuinely
    /// fallen over loses the backlog instead of replaying it.
    /// </summary>
    internal const int MostHeld = 6;

    /// <summary>
    /// How old a line may be when its turn finally comes.
    ///
    /// Past this it is thrown away unsent. The relay keeps forty items and
    /// the page plays them in order, so a line sent late would be played
    /// late - after everything that happened since. Forty-five seconds is
    /// about as far behind the room as "listening along" can honestly be
    /// stretched.
    /// </summary>
    internal static readonly TimeSpan GoesStale = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How old a segment of the stream (ListenStream) may be when its turn
    /// comes. Much less than a line: the page jumps to the newest segment
    /// whenever it falls behind, so one sent a quarter of a minute late is
    /// one nobody will play.
    /// </summary>
    internal static readonly TimeSpan SegmentGoesStale = TimeSpan.FromSeconds(15);

    /// <summary>What happened to a line offered for listeners.</summary>
    internal enum Verdict
    {
        /// <summary>Held, and will be sent.</summary>
        Held,

        /// <summary>The club has not switched listening on, so nothing is collected at all.</summary>
        NobodyIsListening,

        /// <summary>No audio, or too little of it to be audio.</summary>
        NothingToSend,

        /// <summary>Bigger than one relay document, so it cannot be sent whole and will not be sent in part.</summary>
        TooLong,
    }

    /// <summary>One line of the announcer's, waiting its turn.</summary>
    /// <param name="Id">The relay item id - see <see cref="IdFor"/>. Sorted order is the order it was said in.</param>
    /// <param name="Text">What was said, for the page to show beside it. May be empty.</param>
    /// <param name="Mp3">The audio, already encoded.</param>
    /// <param name="AtUtc">When it was said, which is what <see cref="GoesStale"/> is measured from.</param>
    /// <param name="Segment">For a stretch of the stream rather than a line (2 Nov 2026 - see ListenStream): how the page is to join it to the rest. Null for a line.</param>
    internal sealed record Spoken(string Id, string Text, byte[] Mp3, DateTime AtUtc, ListenSegment? Segment = null);

    private readonly object _gate = new();
    private readonly Queue<Spoken> _held = new();

    // The one Take last handed out, which the publisher may still be
    // sending, and whether newer ones pushed it off the queue meanwhile
    // (bug hunt 17). It used to be counted as dropped the moment it was
    // pushed off, and Done then did nothing for it - so a line or stretch
    // the relay had, and listeners heard, was reported as one that never
    // went out, and the slowest delivery of all was left out of the
    // stream's health. It is counted when the send's answer is known
    // instead: by Done or LetGo, or by the next Take when neither came.
    private Spoken? _sending;
    private bool _sendingPushedOff;

    private bool _wanted;

    /// <summary>Stream segments that reached the relay tonight, in seconds of audio, and how many did not - for <see cref="Report"/>.</summary>
    private double _streamedSeconds;
    private int _segmentsMissed;

    // How the stretches got to the relay since TakeDelivery last asked
    // (11 Nov 2026 - see ListenStream.HealthEvery): how many, how long from
    // being cut to the relay having them, and how many never got there.
    private int _deliveredSegments;
    private double _deliveredSeconds;
    private double _worstDelivery;
    private int _lateSegments;
    private int _missedSince;

    /// <summary>
    /// A stretch that reached the relay later than this after it was cut is
    /// late: the page starts a second ahead (relay/src/pages.ts,
    /// STREAM_AHEAD_S), so one this late can leave the listener in silence.
    /// </summary>
    internal static readonly TimeSpan LateDelivery = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How the stream's stretches reached the relay over a while - see
    /// <see cref="TakeDelivery"/>. Seconds are from the stretch being cut to
    /// the relay answering for it.
    /// </summary>
    internal readonly record struct Delivery(int Sent, double AverageSeconds, double WorstSeconds, int Late, int Missed)
    {
        /// <summary>The console's words for it.</summary>
        public string Words()
        {
            static string S(double seconds) =>
                seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";

            var text = Sent == 0
                ? "No stretches reached the relay"
                : $"{Plural.Of(Sent, "stretch", "stretches")} to the relay, {S(AverageSeconds)} on average, worst {S(WorstSeconds)}";

            if (Late > 0)
            {
                text += $", {Late} late (over {S(LateDelivery.TotalSeconds)})";
            }

            if (Missed > 0)
            {
                text += $", {Missed} never sent";
            }

            return text;
        }
    }

    /// <summary>
    /// How the stretches got to the relay since this was last asked, which
    /// starts the count again; null when there were none either way.
    /// </summary>
    internal Delivery? TakeDelivery()
    {
        lock (_gate)
        {
            Delivery? delivery = _deliveredSegments == 0 && _missedSince == 0
                ? null
                : new Delivery(
                    _deliveredSegments,
                    _deliveredSegments == 0 ? 0 : _deliveredSeconds / _deliveredSegments,
                    _worstDelivery,
                    _lateSegments,
                    _missedSince);

            _deliveredSegments = 0;
            _deliveredSeconds = 0;
            _worstDelivery = 0;
            _lateSegments = 0;
            _missedSince = 0;
            return delivery;
        }
    }

    /// <summary>
    /// The meeting the counts and the numbering belong to.
    ///
    /// Null-as-empty on purpose: a club with no race day open is ONE
    /// state, not a new one on every call, or every line would reset the
    /// numbering and every id would be L000001.
    /// </summary>
    private string? _dayId;
    private long _numbered;

    /// <summary>
    /// When this numbering started, in UTC ticks - the front of every id.
    ///
    /// WITHOUT IT THE IDS REPEATED. The count starts over on every launch
    /// and every new meeting, and the page skips any id it has already
    /// heard - so after a restart mid-evening, or with last week's lines
    /// still on the relay, tonight's L000001 onwards were taken for lines
    /// already played and the listener heard nothing. A later start is a
    /// larger number, so the ids still sort into the order they were said.
    /// </summary>
    private long _run = DateTime.UtcNow.Ticks;
    private int _offered;
    private int _sent;
    private int _dropped;
    private int _stale;
    private int _refused;

    /// <summary>
    /// Whether the club has switched listening on.
    ///
    /// Read on every offer rather than captured once, so unticking the box
    /// stops lines being collected from that moment - and the lines
    /// already held are let go, because a club that has just switched this
    /// off does not want the last fifteen seconds going out anyway.
    /// </summary>
    internal bool Wanted
    {
        get
        {
            lock (_gate)
            {
                return _wanted;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_wanted == value)
                {
                    return;
                }

                _wanted = value;

                if (!value)
                {
                    _held.Clear();
                    _sending = null;
                    _sendingPushedOff = false;
                }
            }
        }
    }

    /// <summary>How many lines were offered tonight, whatever became of them.</summary>
    internal int OfferedTonight
    {
        get
        {
            lock (_gate)
            {
                return _offered;
            }
        }
    }

    /// <summary>How many reached the relay.</summary>
    internal int SentTonight
    {
        get
        {
            lock (_gate)
            {
                return _sent;
            }
        }
    }

    /// <summary>How many were pushed off the front because the connection could not keep up.</summary>
    internal int DroppedTonight
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>How many waited so long they were not worth sending.</summary>
    internal int StaleTonight
    {
        get
        {
            lock (_gate)
            {
                return _stale;
            }
        }
    }

    /// <summary>How many were too big for one relay document.</summary>
    internal int RefusedTonight
    {
        get
        {
            lock (_gate)
            {
                return _refused;
            }
        }
    }

    /// <summary>The start of this numbering - see <see cref="IdFor"/>.</summary>
    internal long Run
    {
        get
        {
            lock (_gate)
            {
                return _run;
            }
        }
    }

    /// <summary>How many are waiting right now.</summary>
    internal int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>
    /// Starts the evening's numbering over and forgets what was held.
    ///
    /// CHEAP AND IDEMPOTENT for the day it is already on, exactly like
    /// ClubNotes.NewMeeting and the director's own, and for the same
    /// reason: there is no race-day-changed event in this app, so the day
    /// goes in on the same call the work does and the class notices for
    /// itself.
    ///
    /// Why it matters: the counts in <see cref="Report"/> are a NIGHT's
    /// counts, so carrying them over would have the announcer's recap
    /// reading out the season's. The numbering starting over matters less -
    /// the relay rolls its own oldest away (ChannelSpec.rollOldest) - but
    /// an id that climbed all season would eventually outgrow its padding
    /// and then sort wrongly, which is the one failure
    /// <see cref="IdFor"/> exists to prevent.
    /// </summary>
    internal void NewMeeting(string? dayId)
    {
        lock (_gate)
        {
            var day = dayId ?? string.Empty;

            if (string.Equals(_dayId, day, StringComparison.Ordinal))
            {
                return;
            }

            _dayId = day;
            _held.Clear();
            _sending = null;
            _sendingPushedOff = false;
            _numbered = 0;

            // Never the same run twice, even two meetings inside one tick.
            _run = Math.Max(DateTime.UtcNow.Ticks, _run + 1);
            _offered = 0;
            _sent = 0;
            _dropped = 0;
            _stale = 0;
            _refused = 0;
            _streamedSeconds = 0;
            _segmentsMissed = 0;
        }
    }

    /// <summary>
    /// Offers a stretch of the stream (2 Nov 2026 - see ListenStream). The
    /// same queue as the lines and the same rule - the oldest goes when it is
    /// full - but counted apart, so the recap says how long the room was
    /// heard for rather than calling every three seconds a line.
    /// </summary>
    internal Verdict OfferSegment(ListenSegment segment)
    {
        if (segment.Mp3.Length == 0 || segment.Mp3.Length > MostBytes)
        {
            return segment.Mp3.Length == 0 ? Verdict.NothingToSend : Verdict.TooLong;
        }

        lock (_gate)
        {
            if (!_wanted)
            {
                return Verdict.NobodyIsListening;
            }

            while (_held.Count >= MostHeld)
            {
                PushOffOldest();
            }

            var said =segment.Captions.Count > 0 ? segment.Captions[^1].Text : string.Empty;
            _held.Enqueue(new Spoken(IdFor(_run, ++_numbered), said, segment.Mp3, segment.AtUtc, segment));
        }

        return Verdict.Held;
    }

    /// <summary>
    /// Makes room by letting the oldest go. Counted as missed here unless it
    /// is the one being sent, whose count waits for the answer - see
    /// <see cref="_sending"/>. Under the lock.
    /// </summary>
    private void PushOffOldest()
    {
        var oldest = _held.Dequeue();

        if (ReferenceEquals(oldest, _sending))
        {
            _sendingPushedOff = true;
            return;
        }

        Missed(oldest, stale: false);
    }

    /// <summary>
    /// Whether this is the one being sent and it was pushed off the queue
    /// meanwhile - and either way, it is no longer being sent. Under the lock.
    /// </summary>
    private bool WasPushedOffWhileSending(Spoken what)
    {
        if (!ReferenceEquals(_sending, what))
        {
            return false;
        }

        var pushedOff = _sendingPushedOff;
        _sending = null;
        _sendingPushedOff = false;
        return pushedOff;
    }

    /// <summary>Counts something that never went out, as a line or as a segment. Under the lock.</summary>
    private void Missed(Spoken what, bool stale)
    {
        if (what.Segment is not null)
        {
            _segmentsMissed++;
            _missedSince++;
        }
        else if (stale)
        {
            _stale++;
        }
        else
        {
            _dropped++;
        }
    }

    /// <summary>
    /// Offers a line the announcer is about to say.
    ///
    /// Takes a lock and a queue operation and nothing else: this is called
    /// from the audio path, immediately before the speaker, and anything
    /// slow here would be heard as the announcer being late. The sending
    /// happens on the publisher's own background pass.
    /// </summary>
    internal Verdict Offer(string? text, byte[]? mp3, DateTime atUtc)
    {
        // Judged before the counter moves, so a night with listening
        // switched off reports nothing rather than reporting that it threw
        // every line away.
        lock (_gate)
        {
            if (!_wanted)
            {
                return Verdict.NobodyIsListening;
            }
        }

        if (mp3 is null || mp3.Length < 256)
        {
            lock (_gate)
            {
                _offered++;
            }

            return Verdict.NothingToSend;
        }

        if (mp3.Length > MostBytes)
        {
            lock (_gate)
            {
                _offered++;
                _refused++;
            }

            return Verdict.TooLong;
        }

        lock (_gate)
        {
            // Checked again under the lock the queue is modified under: the
            // box can be unticked between the first check and here, and a
            // line added after the clear would sit there until the next
            // one.
            if (!_wanted)
            {
                return Verdict.NobodyIsListening;
            }

            _offered++;

            // THE OLDEST GOES, not the newest. See the class summary - this
            // one line is the whole difference between this queue and the
            // results queue.
            while (_held.Count >= MostHeld)
            {
                PushOffOldest();
            }

            _held.Enqueue(new Spoken(IdFor(_run, ++_numbered), (text ?? string.Empty).Trim(), mp3, atUtc));
        }

        return Verdict.Held;
    }

    /// <summary>
    /// The next line worth sending, or null when there is nothing.
    ///
    /// Anything that went stale while it waited is thrown away here rather
    /// than sent - so a connection that comes back after a minute off
    /// starts from what is being said now instead of working through what
    /// was being said then.
    /// </summary>
    internal Spoken? Take(DateTime nowUtc)
    {
        lock (_gate)
        {
            // Asked again with no answer for the last one: its send failed.
            // Had it stayed at the head that would be the retry, but newer
            // ones pushed it off, so now it is one that never went out.
            if (_sending is { } failed && _sendingPushedOff)
            {
                Missed(failed, stale: false);
            }

            _sending = null;
            _sendingPushedOff = false;

            while (_held.Count > 0)
            {
                var next = _held.Peek();

                if (nowUtc - next.AtUtc <= (next.Segment is null ? GoesStale : SegmentGoesStale))
                {
                    _sending = next;
                    return next;
                }

                Missed(_held.Dequeue(), stale: true);
            }

            return null;
        }
    }

    /// <summary>
    /// Says the line reached the relay, so it leaves the queue.
    ///
    /// Takes the line rather than trusting the head to still be the same
    /// one: the box can be unticked while a request is in flight, which
    /// clears the queue, and dequeuing blindly after that would throw away
    /// a line that had never been sent.
    ///
    /// There is no matching failure call, because there is nothing for one
    /// to do: <see cref="Take"/> leaves the head in place, so NOT calling
    /// this is the retry. The line goes out next pass, or is thrown away
    /// for being stale, or is pushed off the front by newer lines - all
    /// three better than a retry loop that holds the evening up.
    /// </summary>
    /// <param name="nowUtc">When the relay answered for it - now, outside tests. See <see cref="TakeDelivery"/>.</param>
    internal void Done(Spoken sent, DateTime? nowUtc = null)
    {
        lock (_gate)
        {
            // Pushed off the queue while it was being sent, it is no longer
            // there to dequeue - but the relay has it, so it counts as sent.
            var pushedOff = WasPushedOffWhileSending(sent);

            if (pushedOff || (_held.Count > 0 && ReferenceEquals(_held.Peek(), sent)))
            {
                if (!pushedOff)
                {
                    _held.Dequeue();
                }

                if (sent.Segment is { } segment)
                {
                    _streamedSeconds += segment.Seconds;

                    var took = Math.Max(0, ((nowUtc ?? DateTime.UtcNow) - segment.AtUtc).TotalSeconds);
                    _deliveredSegments++;
                    _deliveredSeconds += took;
                    _worstDelivery = Math.Max(_worstDelivery, took);

                    if (took > LateDelivery.TotalSeconds)
                    {
                        _lateSegments++;
                    }
                }
                else
                {
                    _sent++;
                }
            }
        }
    }

    /// <summary>
    /// Says the line will never be sendable, so it leaves the queue
    /// WITHOUT being counted as sent.
    ///
    /// For the failures that would fail the same way on every pass - a
    /// document the relay refuses as too large, audio that will not
    /// package. Retrying those is the queue holding the evening up to
    /// achieve nothing, and calling <see cref="Done"/> for them would have
    /// the night's recap tell the club those lines went out. They did not.
    /// </summary>
    internal void LetGo(Spoken line)
    {
        lock (_gate)
        {
            var pushedOff = WasPushedOffWhileSending(line);

            if (pushedOff || (_held.Count > 0 && ReferenceEquals(_held.Peek(), line)))
            {
                if (!pushedOff)
                {
                    _held.Dequeue();
                }

                if (line.Segment is not null)
                {
                    _segmentsMissed++;
                    _missedSince++;
                }
                else
                {
                    _refused++;
                }
            }
        }
    }

    /// <summary>
    /// The item id for the nth line of the evening, zero-padded.
    ///
    /// PADDED BECAUSE THE PAGE SORTS BY ID. The relay hands a collection
    /// back and the page plays it in sorted order, so "10" must not sort
    /// between "1" and "2" - which is exactly what unpadded numbers do,
    /// and it would have played the evening scrambled after the ninth
    /// line. Six digits is a million lines, which is more than any club
    /// will say in a night.
    ///
    /// The run - when the numbering started, see <see cref="_run"/> - goes
    /// in front, padded to the nineteen digits a tick count can need, so
    /// no two launches or meetings ever hand out the same id.
    /// </summary>
    internal static string IdFor(long run, long n) =>
        "L" + Math.Max(0, run).ToString("D19", System.Globalization.CultureInfo.InvariantCulture)
        + "-" + Math.Max(0, n).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How the document is written. The default encoder writes every '+'
    /// as the six bytes "\u002B", and mp3 audio in base64 is about one '+'
    /// in sixty-four characters - which took a line near
    /// <see cref="MostBytes"/> past the relay's 256 KB. The page reads it
    /// with JSON.parse and shows the words as text, so nothing here needs
    /// escaping for HTML.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions BodyOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The relay document for one line.
    ///
    /// The relay never reads this - a club's payload is its own business
    /// (relay/src/types.ts) - so the shape here and the shape the page
    /// expects (LISTEN_JS in relay/src/pages.ts) are the same agreement in
    /// two places, and both have to be changed together.
    /// </summary>
    internal static string Body(Spoken line)
    {
        var at = line.AtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        if (line.Segment is not { } segment)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                audio = Convert.ToBase64String(line.Mp3),
                said = line.Text,
                at,
            }, BodyOptions);
        }

        // A stretch of the stream (2 Nov 2026 - see ListenStream). "kind"
        // is what tells the page to join it to the one before rather than
        // play it as a line; "lead" frames are decoded and not played; "run"
        // and "n" say whether it follows straight on from the last one.
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            kind = "mix",
            audio = Convert.ToBase64String(line.Mp3),
            said = line.Text,
            at,
            lead = segment.Lead,
            frames = segment.Frames,
            spf = segment.SamplesPerFrame,
            rate = segment.SampleRate,
            run = segment.Run.ToString(System.Globalization.CultureInfo.InvariantCulture),
            n = segment.Number,
            captions = segment.Captions.Select(c => new { text = c.Text, at = Math.Round(c.AtSeconds, 2) }),
        }, BodyOptions);
    }

    /// <summary>Why a line was not held, in words a club can act on.</summary>
    internal static string Because(Verdict verdict) => verdict switch
    {
        Verdict.Held => "it is on its way to anyone listening",
        Verdict.NobodyIsListening => "the club is not letting anybody listen along",
        Verdict.NothingToSend => "there was no audio to send",
        Verdict.TooLong => "it is longer than one relay document holds, and half a line is worse than none",
        _ => "of something this version does not have words for",
    };

    /// <summary>
    /// What listening cost and carried tonight, for the announcer's recap.
    ///
    /// Null when nothing was offered, so a club that never switched this
    /// on reads nothing about it.
    /// </summary>
    internal string? Report()
    {
        int offered, sent, dropped, stale, refused, segmentsMissed;
        double streamed;

        lock (_gate)
        {
            offered = _offered;
            sent = _sent;
            dropped = _dropped;
            stale = _stale;
            refused = _refused;
            streamed = _streamedSeconds;
            segmentsMissed = _segmentsMissed;
        }

        // The room's sound, streamed (2 Nov 2026): said in minutes, as
        // three-second segments counted as lines would mean nothing.
        string? stream = null;

        if (streamed > 0 || segmentsMissed > 0)
        {
            var minutes = (int)Math.Round(streamed / 60);

            stream = minutes > 0
                ? $"Streamed {Plural.Of(minutes, "minute")} of the room to anyone listening along"
                : "Streamed the room to anyone listening along";

            if (segmentsMissed > 0)
            {
                stream += $", with {Plural.Of(segmentsMissed, "gap")} where the connection could not keep up";
            }

            stream += ".";
        }

        if (offered == 0)
        {
            return stream;
        }

        var text = $"Sent {Plural.Of(sent, "line")} out to anyone listening along";

        var missed = dropped + stale;

        if (missed > 0)
        {
            text += $", and {missed} did not make it in time";
        }

        if (refused > 0)
        {
            // "could not be sent" rather than "was too long": this counts
            // both the lines refused at the door for size and the ones the
            // relay would never take, and the console already carries the
            // specific reason for each. The distinction a club needs from
            // this line is the one above - late, versus never.
            text += $". {Plural.Of(refused, "line")} could not be sent at all";
        }

        return stream is null ? text + "." : text + ". " + stream;
    }
}
