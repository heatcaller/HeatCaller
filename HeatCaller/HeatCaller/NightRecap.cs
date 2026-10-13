namespace HeatCaller;

/// <summary>
/// The night in four minutes: a piece about the whole evening, written
/// after the last race, played out once and kept as a file.
///
/// WHY IT IS NOT THE SIGN-OFF (12 Oct 2026)
///
/// The sign-off is twenty seconds and it is said to a room that was there.
/// This is for the people who were not: a driver's family at home, the
/// member who could not make it, the one who left after their main. It is
/// the thing a club posts, and it is the only output of this whole app that
/// somebody outside the building would ever choose to sit through.
///
/// So it is longer, it is structured, and it is allowed to repeat things
/// the room already heard - the result of a race the sign-off mentioned in
/// passing is exactly what somebody who was not there wants. That is why
/// the facts are packed with no "already said" filter at all; see
/// CommentaryDirector.NightFacts.
///
/// WHY IT PLAYS IN THE HALL RATHER THAN BEING RENDERED SILENTLY
///
/// A piece rendered straight to a file, read to nobody, is the purer idea
/// and <see cref="BookendRecording"/>'s own note says it is worth building.
/// This is not that, and the difference is deliberate rather than a
/// shortcut: what comes out of here is a real broadcast with a recorder
/// round it, so it has the club's opener in front of it, the bed under it
/// and the playout at the end, and the file cannot disagree with anything.
/// A silent render would need a path from a written line to audio bytes
/// that bypasses the voice queue, the bed and the ducking - which is a
/// second way to produce the show, and a second way to produce the show is
/// a second way for it to sound wrong.
///
/// It also costs nothing to let the room hear it. It is played after the
/// last race, while people are packing up, and a club that does not want
/// that simply does not press the button.
///
/// SAID IN CHUNKS, NOT AS ONE LINE
///
/// Four minutes handed to a voice engine as a single request is a request
/// that times out, gets truncated, or comes back as one unbroken wall with
/// no breath in it. <see cref="Chunks"/> breaks the script at sentence
/// boundaries into pieces a voice can take, each queued like any other
/// line - so a flag call can still jump in front of whatever of it is
/// unspoken, which on a night that is not quite over yet matters.
/// </summary>
internal static class NightRecap
{
    /// <summary>
    /// The longest one spoken chunk may run to.
    ///
    /// Twenty seconds, which is about the length of the longest thing this
    /// app already says in one piece (a sponsor's read is capped at
    /// twenty-five). Short enough that every voice engine takes it without
    /// complaint, long enough that the seams land at paragraph breaks
    /// rather than every other sentence.
    /// </summary>
    internal static readonly TimeSpan LongestChunk = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The most chunks one recap may be.
    ///
    /// Fifteen at twenty seconds is five minutes, which is past "four
    /// minutes" and well past anything the word limit will actually
    /// produce. It is a backstop against a model that ignored the length
    /// entirely, not a target - and a recap that hit it would still be
    /// something a club could post.
    /// </summary>
    internal const int MostChunks = 15;

    /// <summary>
    /// The longest the whole recap may take to read before whatever is
    /// waiting on it gives up and tidies itself away.
    ///
    /// Worked out from the two figures above rather than typed, because the
    /// two figures above are what decides it: fifteen pieces of twenty
    /// seconds is five minutes, and the minute on top covers the wait while
    /// each piece is fetched and started.
    ///
    /// WHY IT HAD TO BE SAID AT ALL. ShowTreatment winds a show up after
    /// four minutes, which is right for a segment and a minute short of a
    /// recap at its limit - so the music came up under the last of it, and
    /// the bookend recording closed its file there too. Passed to
    /// ShowTreatment.RunLongAsync and WaitForTheReadAsync by the two
    /// callers that read this out. Found 12 Oct 2026.
    /// </summary>
    internal static readonly TimeSpan LongestRead =
        (LongestChunk * MostChunks) + TimeSpan.FromMinutes(1);

    /// <summary>
    /// Extra words the recap is allowed over the club's own per-line limit.
    ///
    /// The Commentary page's word limit is about one SPOKEN LINE - thirty
    /// or forty words. This is a piece, so it gets its own allowance
    /// outright rather than a multiple of a setting that was never about
    /// this: four hundred words is a shade under three minutes read aloud,
    /// which is the length a club night actually has material for.
    ///
    /// A club that has set a very short line limit still gets a proper
    /// recap, which is right - the two settings are about different things,
    /// and somebody who wanted terse race calls did not thereby ask for a
    /// one-paragraph summary of their evening.
    /// </summary>
    internal const int Words = 400;

    /// <summary>
    /// What to ask the prompt builder for on top of the club's own
    /// per-line limit, so the recap lands on <see cref="Words"/> whatever
    /// that limit is set to.
    ///
    /// CommentaryService.BuildSystemPrompt ADDS its argument to the club's
    /// limit rather than replacing it, which is right for the race summary
    /// it was built for and wrong here: handed four hundred flat, a club
    /// that had set a three-hundred-word line limit would get a
    /// seven-hundred-word recap - five minutes, not the four this is named
    /// for - and a club with a terse thirty-word limit would get a
    /// different length again. The piece should be the same length for
    /// everybody.
    ///
    /// A club that has set a line limit LONGER than the whole recap keeps
    /// it: they have asked for long writing outright, and shortening it
    /// here would be this file overruling their setting rather than
    /// filling in around it.
    /// </summary>
    internal static int ExtraWordsOver(int clubLimit) =>
        Math.Max(0, Words - Math.Max(0, clubLimit));

    /// <summary>
    /// The longest a recap may be as text, before it is split.
    ///
    /// NOT SponsorCopy's cap, and the difference is the whole reason this
    /// has its own: that one is six hundred characters, sized for a
    /// sponsor's read, and a four-hundred-word piece is nearer two and a
    /// half thousand. Running the recap through SponsorCopy.Tidy would have
    /// thrown away three quarters of it silently - the club would have got
    /// a recap that stopped a minute in and nothing anywhere would have
    /// said why.
    ///
    /// Six thousand is above anything <see cref="MostChunks"/> would keep
    /// in any case (fifteen chunks of twenty seconds is seven hundred and
    /// fifty words, call it five thousand characters), so this cap never
    /// cuts a piece the splitter would have used. It is here to bound what
    /// goes through at all, not to shape the writing.
    /// </summary>
    internal const int MostChars = 6000;

    /// <summary>
    /// The script as one block: no line breaks, no brackets, nothing a
    /// model could have used to fake an audio tag, and not longer than
    /// <see cref="MostChars"/>.
    ///
    /// The same guard every other piece of text in this app goes through on
    /// its way to being said, with the recap's own cap rather than the
    /// sponsor one. Read a little long on purpose so the trim below has
    /// something to trim back FROM: Field's cap is a chop at the character,
    /// which lands mid-word.
    /// </summary>
    private static string OneBlock(string? script)
    {
        var cleaned = CommentaryService.Field(script, MostChars + 200);

        if (cleaned.Length <= MostChars)
        {
            return cleaned;
        }

        var head = cleaned[..MostChars];
        var stop = head.LastIndexOfAny(new[] { '.', '!', '?' });

        if (stop > 0)
        {
            return head[..(stop + 1)];
        }

        var space = head.LastIndexOf(' ');

        return (space > 0 ? head[..space] : head).TrimEnd();
    }

    /// <summary>
    /// The script broken into pieces a voice can take, in order.
    ///
    /// Split at SENTENCE boundaries and never inside one, through
    /// SponsorCopy's own splitter - the app has one definition of where a
    /// sentence ends and a second would disagree with it on an abbreviation
    /// or a lap time one day.
    ///
    /// A single sentence longer than <see cref="LongestChunk"/> on its own
    /// goes out as its own chunk rather than being cut: a sentence split
    /// mid-clause by a voice engine is worse than a long one, and a model
    /// asked for a broadcast piece does not write minute-long sentences.
    /// </summary>
    internal static IReadOnlyList<string> Chunks(string? script)
    {
        var said = new List<string>();

        if (string.IsNullOrWhiteSpace(script))
        {
            return said;
        }

        var sentences = SponsorCopy.Sentences(OneBlock(script));

        var building = new System.Text.StringBuilder();

        foreach (var sentence in sentences)
        {
            var next = sentence.Trim();

            if (next.Length == 0)
            {
                continue;
            }

            if (building.Length == 0)
            {
                building.Append(next);
                continue;
            }

            var together = building + " " + next;

            if (SponsorCopy.SpokenLength(together) <= LongestChunk)
            {
                building.Clear();
                building.Append(together);
                continue;
            }

            said.Add(building.ToString());
            building.Clear();
            building.Append(next);

            if (said.Count >= MostChunks)
            {
                // Everything past the backstop is dropped rather than run
                // together into one enormous last chunk. See MostChunks.
                building.Clear();
                break;
            }
        }

        if (building.Length > 0 && said.Count < MostChunks)
        {
            said.Add(building.ToString());
        }

        return said;
    }

    /// <summary>
    /// What the file is called, so a club looking in the night's folder
    /// sees it beside the races and the bookends and knows at a glance
    /// which one to post.
    /// </summary>
    internal const string Label = "The Night";

    /// <summary>
    /// What the Big Screen's band says while the recap is read.
    ///
    /// SEPARATE FROM THE TREATMENT IT BORROWS (13 Oct 2026). The recap
    /// takes the sign-off's music - the same shape of thing, an opener in
    /// front and a playout at the end - and took the sign-off's NAME with
    /// it, so the band read "Sign-Off" through four minutes of the night
    /// being recapped. The band is the one thing on that screen a crowd
    /// reads for a plain statement of what is happening.
    ///
    /// Not <see cref="Label"/>, which is the recording's file name: "The
    /// Night" is right on a folder listing and thin on a band.
    /// </summary>
    internal const string OnTheBand = "Tonight's Racing";

    /// <summary>
    /// How the model is told to write it.
    ///
    /// THREE THINGS IT IS TOLD TO DO, and they are the whole difference
    /// between this and the sign-off. Say who it is for - somebody who was
    /// not there - so it explains rather than refers. Move through the
    /// evening in order, because a list of superlatives is not a story.
    /// And finish on the night rather than on a number.
    ///
    /// AND THE SAME RULE EVERY OTHER PROMPT HERE HAS: the facts above are
    /// all of it. A recap is the one place a model would be most tempted to
    /// smooth over a thin evening with something plausible, and a club that
    /// posts an invented result has posted a lie about its own members.
    /// </summary>
    internal const string Instruction =
        "Write the night as a short piece for somebody who was NOT at the track - a driver's family at home, a "
        + "member who could not make it. Move through the evening roughly in order rather than listing the "
        + "highlights, use several short paragraphs, and finish on the night as a whole rather than on a figure. "
        + "Name people as the facts name them, and explain rather than refer: \"the A-main\" means nothing to "
        + "somebody who was not watching, so say what it was. "
        + "Everything you say must come from the facts above - every name, time, position and count. Do not add a "
        + "result, a lap time, an incident, a championship position or a mood that is not there, and where the "
        + "facts are thin, write a shorter piece rather than filling it out.";
}
