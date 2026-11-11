namespace HeatCaller;

/// <summary>
/// Records the night's two bookends - the cold open and the sign-off - as
/// they go out, so the club ends an evening with a file of the broadcast's
/// top and tail and not just of its races.
///
/// WHY THESE TWO AND NOTHING ELSE (11 Oct 2026)
///
/// Every race is already recorded, and the recording runs until the
/// broadcast after the flag has finished - so a race's recap, its honours,
/// its interview and its colour are all in that race's own mp3. What was
/// in no file at all was the half of the evening that is not a race: the
/// cold open before the first one and the sign-off after the last. Those
/// are the two set pieces a club would actually post, because they are
/// about the NIGHT rather than about one heat - and they were the only
/// parts of the broadcast that existed once and were then gone.
///
/// WHY IT RECORDS THE REAL ONE RATHER THAN WRITING A NEW ONE
///
/// What comes out of here is the broadcast the room actually heard, with
/// the club's own opener in front of it, the bed under it and the playout
/// at the end, because it IS that broadcast with a recorder round it.
/// Nothing is written twice, nothing is rendered twice, no model is asked
/// anything, and the file cannot disagree with what the club remembers
/// hearing.
///
/// The separately written "night in four minutes" this note used to say
/// was a different and bigger thing was built on 12 Oct 2026 - and it came
/// back here rather than growing a second way to make audio. See
/// NightRecap: it writes a longer script after the last race and then
/// plays it out through the show like any other segment, with
/// StartForTheNight below putting a recorder round it. So the two are the
/// same mechanism and differ only in what is said.
///
/// THE ONE REAL HAZARD, AND WHY Busy EXISTS
///
/// AudioBus takes exactly ONE listener (see AudioBus.Listen - it is a
/// single field, deliberately). Starting a recorder while the race
/// recorder is listening would quietly displace it, and the race's own mp3
/// would end wherever that happened with nothing to say why. A bookend
/// cannot normally coincide with a race - the cold open is before the
/// first and the sign-off after the last - but "cannot normally" is not a
/// guarantee, and the thing at risk is the club's recording of a race.
/// So this refuses to start whenever anything is already recording.
/// </summary>
internal sealed class BookendRecording
{
    private readonly RaceAudioStore _store;
    private readonly RaceAudioRecorder _recorder;

    private BookendRecording(RaceAudioStore store, RaceAudioRecorder recorder)
    {
        _store = store;
        _recorder = recorder;
    }

    /// <summary>
    /// Whether this segment is one of the night's bookends.
    ///
    /// The two the running order already treats as the ends of the evening
    /// - ShowRunningOrder gives the cold open the whole opener and the
    /// sign-off its playout, for the same reason these are the two worth
    /// keeping.
    /// </summary>
    internal static bool IsBookend(SegmentKind kind) =>
        kind is SegmentKind.ColdOpen or SegmentKind.SignOffNumbers;

    /// <summary>
    /// What the file is called - the crowd's own name for the segment, so a
    /// club looking in the folder sees "Tonight Open" and "Sign-Off" beside
    /// the races rather than a SegmentKind.
    /// </summary>
    private static string LabelFor(SegmentKind kind) =>
        ShowBlock.CaptionFor(kind) ?? (kind == SegmentKind.ColdOpen ? "Tonight Open" : "Sign-Off");

    /// <summary>
    /// Starts recording this bookend, or returns null - which is the
    /// answer in every ordinary case: the segment is not a bookend, the
    /// club has recording switched off, there is no room on the disk, or
    /// something is already recording.
    ///
    /// Null is never an error and is never reported as one. The only one
    /// of those four worth a line is the collision, because it is the one
    /// a club could not have predicted.
    /// </summary>
    internal static BookendRecording? StartFor(SegmentKind kind, Action<string> log)
    {
        if (!IsBookend(kind))
        {
            return null;
        }

        try
        {
            // ALREADY RECORDING. See the note on this class: AudioBus
            // takes one listener, and the thing that would be lost is a
            // race's own mp3.
            if (AudioBus.Listening)
            {
                log($"{AppTime.LogStampNow()}  [audio] The {LabelFor(kind)} is not being recorded on its own - "
                    + "something is already recording, and it keeps the tap. Nothing is lost: whatever is "
                    + "recording has this in it.");

                return null;
            }

            var store = RaceAudioStore.Load();

            // StartFor answers null for the club's switch being off and
            // for a disk with no room, and says so itself in the second
            // case. Nothing to add here.
            if (RaceAudioRecorder.StartFor(store, LabelFor(kind), DateTime.UtcNow, log) is not { } recorder)
            {
                return null;
            }

            return new BookendRecording(store, recorder);
        }
        catch (Exception ex)
        {
            // A recording is the one thing on a race night that is nice to
            // have. It must never be the reason a cold open does not go
            // out.
            log($"{AppTime.LogStampNow()}  [audio] Couldn't start recording the {LabelFor(kind)} ({ex.Message}). "
                + "The broadcast itself is unaffected.");

            return null;
        }
    }

    /// <summary>
    /// Starts recording the night's own recap - see <see cref="NightRecap"/>.
    ///
    /// The bookends above are recognised by their SegmentKind because they
    /// go out as segments. The night recap is not a segment: it is asked
    /// for by somebody pressing a button, so it says what it is rather
    /// than being recognised. Everything else is identical, including the
    /// refusal while anything is already recording - and that refusal
    /// matters more here, because this is the longest single thing the
    /// app ever plays and it would hold the tap for all of it.
    /// </summary>
    internal static BookendRecording? StartForTheNight(Action<string> log) =>
        StartForAPiece(NightRecap.Label, "tonight's recap", log);

    /// <summary>
    /// Starts recording one driver's own piece - see <see cref="DriverCut"/>.
    ///
    /// Its own label so a club looking in the night's folder sees "Bill
    /// Hargrove - their night" beside the races, the bookends and The
    /// Night, and knows at a glance which file goes to whom.
    /// </summary>
    internal static BookendRecording? StartForADriver(string? driverName, Action<string> log) =>
        StartForAPiece(DriverCut.LabelFor(driverName), "a driver's own piece", log);

    /// <summary>
    /// Starts recording the season's own piece - see <see cref="SeasonPiece"/>.
    /// Labelled with the season's name, so a club with a Winter Series and
    /// a Summer Series can tell the two files apart.
    /// </summary>
    internal static BookendRecording? StartForASeason(string? seasonName, Action<string> log) =>
        StartForAPiece(SeasonPiece.LabelFor(seasonName), "the season's piece", log);

    /// <summary>
    /// The one way a piece that is not a segment gets recorded.
    ///
    /// ONE METHOD rather than the same twenty lines per piece, which is
    /// what it had become the moment there were two of them. Everything
    /// here is the night recap's own logic, unchanged - the refusal while
    /// anything else is recording, the store, the label - with the label
    /// and what to call it in the log handed in.
    /// </summary>
    /// <param name="label">What the file is called in the night's folder.</param>
    /// <param name="called">What the log calls it, in the middle of a sentence.</param>
    private static BookendRecording? StartForAPiece(string label, string called, Action<string> log)
    {
        try
        {
            if (AudioBus.Listening)
            {
                log($"{AppTime.LogStampNow()}  [audio] {char.ToUpperInvariant(called[0])}{called[1..]} is not "
                    + "being recorded on its own - something is already recording, and it keeps the tap. "
                    + "Nothing is lost: whatever is recording has this in it.");

                return null;
            }

            var store = RaceAudioStore.Load();

            if (RaceAudioRecorder.StartFor(store, label, DateTime.UtcNow, log) is not { } recorder)
            {
                return null;
            }

            return new BookendRecording(store, recorder);
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [audio] Couldn't start recording {called} ({ex.Message}). "
                + "The broadcast itself is unaffected.");

            return null;
        }
    }

    /// <summary>
    /// Closes the recording off and writes the mp3, on a pool thread.
    ///
    /// SAFE TO CALL FROM A FINALLY, which is where it is called from, and
    /// that is the whole of its contract: it never throws, and it never
    /// waits. BeginStop unhooks the tap immediately so nothing more is
    /// taken in; the encoding happens on a pool thread afterwards, exactly
    /// as the race's own does, because encoding an mp3 on the thread that
    /// has just finished a broadcast is how a club's next race gets a late
    /// start.
    ///
    /// No clips and no driver names: a bookend is about the night, so
    /// Publish is handed no race to cut per-driver stretches out of. See
    /// RaceHighlights.
    /// </summary>
    internal void Finish(Action<string> log)
    {
        try
        {
            _recorder.BeginStop();

            var store = _store;
            var recorder = _recorder;

            _ = Task.Run(() =>
            {
                try
                {
                    RaceAudioRecorder.Publish(recorder.Stop(), store, record: null, releaseOffsetsMs: null, log);
                }
                catch
                {
                    // Publish has its own catch for everything it does.
                    // This is for the log callback itself having gone -
                    // not worth an unobserved exception on a pool thread.
                }
            });
        }
        catch (Exception ex)
        {
            log($"{AppTime.LogStampNow()}  [audio] Couldn't finish recording the broadcast ({ex.Message}). "
                + "Anything already written is where it was.");
        }
    }
}
