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
    /// AND ONE PIECE MORE THAN THE BACKSTOP (14 Oct 2026), because the
    /// sign-off is added past it - see <see cref="SignOff"/>. Sixteen
    /// pieces rather than fifteen, which is twenty seconds nobody will
    /// ever use and the alternative is the music coming up under the one
    /// sentence this change exists to guarantee.
    internal static readonly TimeSpan LongestRead =
        (LongestChunk * (MostChunks + 1)) + TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a recap - or a driver's own piece, which is read the same
    /// way - may hold the broadcast floor: the writing, then the read.
    ///
    /// THE FLOOR'S OWN CEILING WAS SHORTER THAN THIS (14 Oct 2026). A claim
    /// is let go after six minutes (see ShowFloor), and the floor is claimed
    /// BEFORE the model is asked - so up to three minutes of writing and
    /// then a read of up to LongestRead came to more than nine, and the
    /// claim was broken partway through the recap with the director's tick
    /// free to open a second show on top of it. Five minutes on top of the
    /// read covers the long-piece write timeout, the driver's clip, the
    /// opener and the playout. A write that runs longer still is caught
    /// once it comes back: the caller checks it still holds the floor
    /// before it plays anything.
    /// </summary>
    internal static readonly TimeSpan LongestHold = LongestRead + TimeSpan.FromMinutes(5);

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
    /// How the host signs the show off - written here, not asked for.
    ///
    /// ASKED FOR FROM THE FLOOR (14 Oct 2026). A recap show has to END
    /// rather than stop: the host names himself and names the show, the
    /// way every broadcast anybody has ever posted does - "I'm Frankie
    /// Johnny, see you next time on Race Day Ruckus". This is the one
    /// output of the app somebody outside the building sits through, and a
    /// four-minute piece that simply runs out of sentences is the thing a
    /// listener notices about it.
    ///
    /// WRITTEN IN CODE AND NOT BY THE MODEL, which is the whole point.
    /// The requirement is "always", and the model is the one part of this
    /// that cannot promise always: <see cref="Instruction"/> now asks it
    /// NOT to sign off and this line is put on afterwards. Asking nicely
    /// was the obvious cheaper fix and it fails in two directions at once
    /// - the 0.6B that ships in the installer drops a closing line often
    /// enough to notice, and the sign-off is also the LAST sentence of a
    /// four-hundred-word piece, so on the nights the word allowance ran
    /// out it was the sentence that got cut. A club cannot tell those two
    /// apart from the floor, and neither can the log.
    ///
    /// SAID EVEN WITH NEITHER NAME TYPED. A show with no name still has
    /// to end, and "that's the night, see you next time" is a sign-off
    /// where silence is not. Every other naming in this app goes quiet
    /// when the club has typed nothing, which is right for a name dropped
    /// mid-paragraph and wrong for the last thing said all evening.
    /// </summary>
    internal static string SignOff(string? host, string? show)
    {
        var who = CommentaryService.Field(host, CommentaryService.MaxShowNameChars);
        var what = CommentaryService.Field(show, CommentaryService.MaxShowNameChars);

        if (who.Length > 0 && what.Length > 0)
        {
            return $"I'm {who}. See you next time on {what}.";
        }

        if (who.Length > 0)
        {
            return $"I'm {who}. See you next time.";
        }

        if (what.Length > 0)
        {
            return $"That's the night from {what}. See you next time.";
        }

        return "That's the night. See you next time.";
    }

    /// <summary>
    /// What marks a sentence as the model having said goodbye on its own.
    ///
    /// Only ever matched against the LAST sentences of a piece, and only
    /// against short ones - see <see cref="LongestFarewell"/>. "We'll see
    /// you back here when the championship goes down to the last round"
    /// in the middle of a paragraph is a figure of speech, not a sign-off,
    /// and a list like this one applied to a whole script would eat real
    /// writing.
    /// </summary>
    private static readonly string[] Farewells =
    {
        "see you", "see ya", "goodnight", "good night", "until next", "next time",
        "that's all", "thats all", "that is all", "from me", "from all of us",
        "signing off", "sign off", "take care", "drive safe", "safe home",
        "thanks for listening", "thanks for joining", "thank you for listening",
        "thank you for joining", "goodbye", "good bye", "bye for now",
    };

    /// <summary>
    /// The longest a sentence may run and still be read as a goodbye
    /// rather than a paragraph that happens to contain one of the words.
    /// </summary>
    private const int LongestFarewell = 22;

    private static bool IsFarewell(string sentence)
    {
        var words = sentence.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).Length;

        if (words == 0 || words > LongestFarewell)
        {
            return false;
        }

        var lower = sentence.ToLowerInvariant();

        foreach (var mark in Farewells)
        {
            if (lower.Contains(mark, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
    /// <param name="signOff">
    /// The last thing said, added as a piece of its own - see
    /// <see cref="SignOff"/>. Null or empty leaves the piece exactly as
    /// written, which is what a caller that only wants to measure it
    /// wants.
    /// </param>
    internal static IReadOnlyList<string> Chunks(string? script, string? signOff = null)
    {
        var said = new List<string>();

        if (string.IsNullOrWhiteSpace(script))
        {
            return said;
        }

        var sentences = SponsorCopy.Sentences(OneBlock(script)).ToList();

        // THE MODEL'S OWN GOODBYE, TAKEN OFF THE END (14 Oct 2026). A
        // sign-off is put on below whatever happens, so a model that said
        // goodbye anyway would have the room hear two of them back to
        // back - which sounds like a fault rather than a flourish.
        //
        // Never the last sentence standing: a piece that was ONLY a
        // goodbye would otherwise come back empty and be reported to the
        // club as "the model did not write anything usable", which is a
        // different and wrong complaint.
        if (!string.IsNullOrWhiteSpace(signOff))
        {
            while (sentences.Count > 1 && IsFarewell(sentences[^1]))
            {
                sentences.RemoveAt(sentences.Count - 1);
            }
        }

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

        // AND THE SIGN-OFF, PAST THE BACKSTOP ON PURPOSE.
        // <see cref="MostChunks"/> throws away everything after the
        // fifteenth piece, and the sign-off is the one piece that must be
        // said - so it goes on AFTER that cut rather than queueing behind
        // it. A model that ignored the length entirely would otherwise
        // have taken the sign-off down with the rest of its overrun, which
        // is precisely the night a club most wants the show to end
        // properly. LongestRead allows for the extra piece.
        //
        // Not onto nothing, though: the caller reads an empty list as "the
        // model wrote nothing usable" and tells the club so, and a show
        // that is a sign-off and nothing else is worse than that message.
        if (said.Count > 0 && !string.IsNullOrWhiteSpace(signOff))
        {
            said.Add(signOff.Trim());
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
        + "facts are thin, write a shorter piece rather than filling it out. "
        // THE SIGN-OFF IS NOT THE MODEL'S JOB (14 Oct 2026) - see SignOff
        // for why it is written in code instead. Said here as well so the
        // piece does not run into the one that is added: a model told
        // nothing writes its own goodbye about a third of the time, and
        // Chunks then has to take it back off.
        + "Do not sign off, say goodbye, name yourself or name the show at the end - the sign-off is a "
        + "separate line added afterward. Finish on the night itself.";
}
