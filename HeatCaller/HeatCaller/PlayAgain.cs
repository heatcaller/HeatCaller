namespace HeatCaller;

/// <summary>What kind of line a kept line was - for its chip, and for which line a press plays.</summary>
public enum SaidKind
{
    CallUp,
    Countdown,
    Start,
    Flag,
    LastLap,
    Finish,
    Winner,
    Record,
    RaceCall,
    Commentary,
    Notice,
}

/// <summary>Where the race is, as far as Play Again cares.</summary>
public enum PlayAgainPhase
{
    /// <summary>No race on: the call-up is what's wanted again.</summary>
    BetweenRaces,

    /// <summary>Staging or the countdown: nothing is replayed over the start.</summary>
    Starting,

    /// <summary>A race (or practice) on track.</summary>
    Racing,

    /// <summary>The checkered flag is out.</summary>
    AfterTheFlag,
}

/// <summary>
/// One line the room heard tonight, kept so it can be played again.
/// </summary>
/// <param name="N">Its number in the log, counting up from 1 - newer is higher.</param>
/// <param name="Text">The words as the voice was given them, tags and all - what <paramref name="Profile"/> looks up in the voice library.</param>
/// <param name="Shown">The words as a person reads them: no [tags], swears starred.</param>
/// <param name="Speaker">Who said it, by first name ("Finley").</param>
/// <param name="Stamp">The race as it was the moment it was heard.</param>
/// <param name="Facts">An AI line's own facts, when it was written during a race - null for the rest.</param>
/// <param name="Audio">The audio the room heard, when it was in hand (a streamed line's isn't).</param>
/// <param name="Profile">The voice settings it was said in, for the voice library when <paramref name="Audio"/> is null.</param>
/// <param name="Group">Lines of one commentary exchange share a group, and play together.</param>
internal sealed record SaidLine(
    long N,
    string Text,
    string Shown,
    SaidKind Kind,
    string Speaker,
    DateTime AtLocal,
    RaceFactsStamp Stamp,
    RaceFactsStamp? Facts,
    byte[]? Audio,
    VoiceProfile? Profile,
    string? Label,
    long Group);

/// <summary>What one press of the bar's Play button would do now.</summary>
/// <param name="Caption">The button's words: "Play winner", "Play call-up", "Play again", or why not.</param>
/// <param name="Lines">What it plays, in the order they were said - empty when it plays nothing.</param>
internal sealed record PlayAgainPress(string Caption, string? Detail, bool Enabled, IReadOnlyList<SaidLine> Lines);

/// <summary>
/// PLAY AGAIN (25 Nov 2026, asked for by Asher: "add a play again option on
/// race control", then "A bar, with the split button from B, and a change
/// the flags like in C" - plans/play-again-plan.md, board G). The rules,
/// kept away from the window so they are tested without one: what is kept,
/// what is out of date, and what the bar's one press plays as the race goes.
///
/// WHY: a driver at the far end of the pits misses the call-up, the room
/// talks over the winner. The only Play again was the Broadcast window's,
/// which re-said the last AI commentary segment and nothing else - not a
/// call-up, a flag call or the winner.
/// </summary>
internal static class PlayAgain
{
    /// <summary>How many lines are kept.</summary>
    public const int MostLines = 20;

    /// <summary>How much audio is kept, in bytes, before the oldest lines go.</summary>
    public const long MostAudioBytes = 20L * 1024 * 1024;

    /// <summary>Commentary turns this close together, under one label, are one exchange.</summary>
    public static readonly TimeSpan ExchangeGap = TimeSpan.FromSeconds(20);

    /// <summary>The words the list shows for a line that can't be played again.</summary>
    public const string OutOfDateWords = "Out of date - the race has moved on";

    /// <summary>
    /// The race night a moment belongs to: a night runs 6am to 6am, so a
    /// heat at 1am is still the evening before's.
    /// </summary>
    public static DateTime NightOf(DateTime local) => local.AddHours(-6).Date;

    /// <summary>
    /// What kind of line it was, from how it reached the voice: its call
    /// type, the name it was queued under (the call-ups' "Call-up") and the
    /// voice path's log prefix ("[voice] Winner").
    /// </summary>
    public static SaidKind KindOf(AnnouncementEventType? call, string? label, string? logPrefix)
    {
        var prefix = logPrefix ?? string.Empty;

        if (prefix.StartsWith("[voice] Winner", StringComparison.Ordinal) || prefix.StartsWith("[voice] Class winners", StringComparison.Ordinal))
        {
            return SaidKind.Winner;
        }

        if (label is { } named && named.Contains("call-up", StringComparison.OrdinalIgnoreCase))
        {
            return SaidKind.CallUp;
        }

        return call switch
        {
            AnnouncementEventType.GridCall or AnnouncementEventType.DriverRelease => SaidKind.CallUp,
            AnnouncementEventType.RaceStaging or AnnouncementEventType.CountdownStarted => SaidKind.Countdown,
            AnnouncementEventType.RaceStarted => SaidKind.Start,
            AnnouncementEventType.YellowFlag or AnnouncementEventType.RedFlag or AnnouncementEventType.GreenFlagResumed
                or AnnouncementEventType.RaceTimeOver or AnnouncementEventType.JumpStart => SaidKind.Flag,
            AnnouncementEventType.LastLap => SaidKind.LastLap,
            AnnouncementEventType.RaceCompleted => SaidKind.Finish,
            AnnouncementEventType.TrackRecord or AnnouncementEventType.AchievementUnlocked or AnnouncementEventType.SectorsRecap => SaidKind.Record,
            AnnouncementEventType.Reminder when label is { Length: > 0 } && !IsNotice(label) => SaidKind.Commentary,
            AnnouncementEventType.Reminder => SaidKind.Notice,
            null => SaidKind.Notice,
            _ => SaidKind.RaceCall,
        };
    }

    /// <summary>The names a notice is queued under rather than a commentary segment.</summary>
    private static bool IsNotice(string label) =>
        label.Equals("Notice", StringComparison.OrdinalIgnoreCase)
        || label.Contains("meeting", StringComparison.OrdinalIgnoreCase)
        || label.Contains("gap", StringComparison.OrdinalIgnoreCase);

    /// <summary>The chip's word for a kind of line.</summary>
    public static string KindWords(SaidKind kind) => kind switch
    {
        SaidKind.CallUp => "Call-up",
        SaidKind.Countdown => "Countdown",
        SaidKind.Start => "Start",
        SaidKind.Flag => "Flag",
        SaidKind.LastLap => "Last lap",
        SaidKind.Finish => "Finish",
        SaidKind.Winner => "Winner",
        SaidKind.Record => "Record",
        SaidKind.RaceCall => "Race call",
        SaidKind.Commentary => "Commentary",
        _ => "Notice",
    };

    /// <summary>
    /// Why a kept line can't be played again now, or null while it can.
    ///
    /// Trust over chatter: a gap, a position, a lap time or the minutes
    /// left is a call about the race as it stood, and played after the
    /// leader has done another lap - or once the race is over - it tells the
    /// room something that isn't true. An AI line written during a race is
    /// held to its own facts, as the voice queue holds it (its deadline
    /// aside: a replay is late by nature). Call-ups, flags, the finish, the
    /// winner, records and notices stay true all night.
    /// </summary>
    public static string? WhyOutOfDate(SaidLine line, RaceFactsStamp now, bool raceOverSince)
    {
        if (line.Kind == SaidKind.RaceCall
            && (now.Race != line.Stamp.Race || now.LeaderLaps > line.Stamp.LeaderLaps || raceOverSince))
        {
            return OutOfDateWords;
        }

        if (line.Facts is { } facts && AnnouncementService.WhyNoLongerTrue(facts with { SayByMs = null }, now) is not null)
        {
            return OutOfDateWords;
        }

        return null;
    }

    /// <summary>
    /// The log with <paramref name="line"/> added at the front: tonight's
    /// lines only (a new night starts it again), at most <see cref="MostLines"/>,
    /// and at most <see cref="MostAudioBytes"/> of audio - the oldest go first.
    /// </summary>
    public static IReadOnlyList<SaidLine> Added(IReadOnlyList<SaidLine> log, SaidLine line)
    {
        var night = NightOf(line.AtLocal);
        var kept = new List<SaidLine> { line };
        var bytes = (long)(line.Audio?.Length ?? 0);

        foreach (var older in log)
        {
            if (NightOf(older.AtLocal) != night || kept.Count >= MostLines)
            {
                break;
            }

            bytes += older.Audio?.Length ?? 0;

            if (bytes > MostAudioBytes)
            {
                break;
            }

            kept.Add(older);
        }

        return kept;
    }

    /// <summary>
    /// The group a new commentary line joins: the newest line's, when that
    /// was commentary under the same name a moment ago (the co-host's turn
    /// in an exchange) - otherwise a group of its own.
    /// </summary>
    public static long GroupFor(IReadOnlyList<SaidLine> log, SaidKind kind, string? label, DateTime atLocal, long n)
    {
        if (kind == SaidKind.Commentary && log.Count > 0 && log[0] is { Kind: SaidKind.Commentary } last
            && string.Equals(last.Label, label, StringComparison.OrdinalIgnoreCase)
            && atLocal - last.AtLocal <= ExchangeGap)
        {
            return last.Group;
        }

        return n;
    }

    /// <summary>A line and the rest of its exchange, in the order they were said.</summary>
    public static IReadOnlyList<SaidLine> WithItsExchange(IReadOnlyList<SaidLine> log, SaidLine line) =>
        log.Where(l => l.Group == line.Group).OrderBy(l => l.N).ToList();

    /// <summary>
    /// What one press plays now, and what the button says - following the
    /// flags (board G): between races the last call-up, over the start
    /// nothing, during a race the newest line from it that's still true,
    /// after the flag the winner (then the class winners). A press that has
    /// nothing better plays the newest line still true, and says "Play again".
    /// </summary>
    /// <param name="log">The kept lines, newest first.</param>
    /// <param name="raceOverSince">Whether the race the stamps name has finished - see <see cref="WhyOutOfDate"/>.</param>
    public static PlayAgainPress MainPress(IReadOnlyList<SaidLine> log, PlayAgainPhase phase, bool muted, RaceFactsStamp now, bool raceOverSince)
    {
        if (muted)
        {
            return new("Muted", "M to unmute", false, Array.Empty<SaidLine>());
        }

        if (phase == PlayAgainPhase.Starting)
        {
            return new("Play again", "after the start", false, Array.Empty<SaidLine>());
        }

        var playable = log.Where(l => WhyOutOfDate(l, now, raceOverSince) is null).ToList();

        if (playable.Count == 0)
        {
            return new("Play again", log.Count == 0 ? "nothing said yet" : "nothing still true", false, Array.Empty<SaidLine>());
        }

        PlayAgainPress Again(SaidLine newest) => new("Play again", PlayAgainWords.Summary(newest), true, WithItsExchange(log, newest));

        switch (phase)
        {
            case PlayAgainPhase.AfterTheFlag:
            {
                // A call-up for the next race since the flag is what's wanted now.
                var winners = playable.Where(l => l.Kind == SaidKind.Winner && l.Stamp.Race == now.Race).OrderBy(l => l.N).ToList();
                var callUp = playable.FirstOrDefault(l => l.Kind == SaidKind.CallUp);

                if (callUp is not null && (winners.Count == 0 || callUp.N > winners[^1].N))
                {
                    return new("Play call-up", PlayAgainWords.Said(callUp), true, new[] { callUp });
                }

                if (winners.Count > 0)
                {
                    return new("Play winner", winners.Count > 1 ? "Winner, then class winners" : "The winner", true, winners);
                }

                return Again(playable[0]);
            }

            case PlayAgainPhase.BetweenRaces:
            {
                var callUp = playable.FirstOrDefault(l => l.Kind == SaidKind.CallUp);
                return callUp is not null
                    ? new("Play call-up", PlayAgainWords.Said(callUp), true, new[] { callUp })
                    : Again(playable[0]);
            }

            default:
            {
                // Racing: this race's lines only - the last race's are history.
                var thisRace = playable.FirstOrDefault(l => l.Stamp.Race == now.Race);
                return thisRace is not null
                    ? Again(thisRace)
                    : new("Play again", "nothing said this race yet", false, Array.Empty<SaidLine>());
            }
        }
    }

    /// <summary>The phase from the race's state - practice counts as racing.</summary>
    public static PlayAgainPhase PhaseOf(RaceState state, bool practice) => state switch
    {
        _ when practice => PlayAgainPhase.Racing,
        RaceState.Staging or RaceState.Countdown => PlayAgainPhase.Starting,
        RaceState.Racing => PlayAgainPhase.Racing,
        RaceState.Finished => PlayAgainPhase.AfterTheFlag,
        _ => PlayAgainPhase.BetweenRaces,
    };
}

/// <summary>What the On Air bar shows at rest once something has been said: "Last said · Winner · 8:41 PM" and the words.</summary>
internal sealed record LastSaidShown(string Kind, Color Colour, string When, string Words)
{
    public string Lead => $"Last said · {Kind} · {When}";
}

/// <summary>The words Play Again shows - the bar's resting text, the list's rows.</summary>
internal static class PlayAgainWords
{
    /// <summary>"Winner · 20:41".</summary>
    public static string Summary(SaidLine line) => $"{PlayAgain.KindWords(line.Kind)} · {AppTime.TimeOfDay(line.AtLocal)}";

    /// <summary>"Said 8:44 PM" - a call-up's line under the button, where the button already says what it is.</summary>
    public static string Said(SaidLine line) => $"Said {AppTime.TimeOfDay(line.AtLocal)}";

    /// <summary>
    /// The line under the button with how long it plays on the end: "The
    /// winner · 0:05". Unchanged when the length isn't known (a streamed
    /// line keeps no audio) or there is nothing to play.
    /// </summary>
    public static string WithLength(string? detail, double? seconds)
    {
        var words = detail ?? string.Empty;

        if (seconds is not { } s || s <= 0)
        {
            return words;
        }

        var length = TimeSpan.FromSeconds(Math.Max(1, Math.Round(s)));
        var clock = $"{(int)length.TotalMinutes}:{length.Seconds:00}";
        return words.Length == 0 ? clock : $"{words} · {clock}";
    }

    /// <summary>The bar at rest: "Last said · Winner · 20:41", and the words in quotes.</summary>
    public static (string Lead, string Words)? Resting(IReadOnlyList<SaidLine> log) =>
        log.Count == 0 ? null : ($"Last said · {Summary(log[0])}", $"“{log[0].Shown}”");

    /// <summary>A row of the list: its title line, and the words (or why it can't be played).</summary>
    public static (string Title, string Note) Row(SaidLine line, string? outOfDate) =>
        (string.Join(" · ", new[] { PlayAgain.KindWords(line.Kind), AppTime.TimeWithSeconds(line.AtLocal), line.Speaker }.Where(s => s.Length > 0)), outOfDate ?? line.Shown);

    /// <summary>Words cut to a list row's length, with "…" - the menu is as wide as its longest row.</summary>
    public static string Shortened(string words, int most = 72) =>
        words.Length <= most ? words : words[..(most - 1)].TrimEnd() + "…";

    /// <summary>
    /// Who said it, by first name: "Finley" from "Finley - announcer". Blank
    /// for a voice with no name.
    /// </summary>
    public static string SpeakerName(string? voiceName)
    {
        var name = (voiceName ?? string.Empty).Trim();

        foreach (var cut in new[] { " - ", " – ", " (", "," })
        {
            var at = name.IndexOf(cut, StringComparison.Ordinal);

            if (at > 0)
            {
                name = name[..at];
            }
        }

        return name.Trim();
    }
}
