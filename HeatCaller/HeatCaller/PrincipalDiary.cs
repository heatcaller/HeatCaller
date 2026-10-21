using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// What a team's principal SAID, so the next time he sits in the booth the
/// host can put it back to him.
///
/// WHY THIS IS THE CHANGE THAT MATTERS (18 Oct 2026)
///
/// Everything else done to the principal this week makes one interview
/// better. This is the one that makes the segment worth coming back for.
/// A guest who has to account for what he said a fortnight ago, in front
/// of a room that heard him say it, is in a different kind of interview
/// from a guest being asked how tonight went - and it costs one stored
/// sentence per team per night.
///
/// "Two weeks ago you sat here and told me the starts would be fixed" is
/// the line, and the room's reaction to it is the whole return on this
/// file.
///
/// HOW THIS IS ALLOWED WHEN NightPromises REFUSES THE SAME THING
///
/// Read NightPromises first, because at a glance this breaks its rule. It
/// will not record that the two voices disagreed, and says why at length:
/// we know a BRIEF told the co-host to disagree, we do not know that it
/// did, and "they disagreed" built from an instruction is an invented
/// claim about the last five minutes of the room's own evening.
///
/// The difference here is the difference that rule turns on. This does not
/// store an instruction, a plan or an intention. It stores the WORDS THAT
/// WENT TO AIR, verbatim, after the model wrote them and
/// CommentaryService.ParseInterview split them - the same strings the
/// voice read out. That is an observation of something the room heard,
/// which is the one kind of record NightPromises' own summary allows:
/// "everything in this layer is arithmetic on the record the app already
/// keeps, plus the fact that a segment about a named subject went to air".
/// This is that second clause, with the sentence attached.
///
/// AND IT NEVER SCORES HIM. This is the line, and it is not negotiable.
/// The app does not store, compute or say whether he kept his word.
/// Whether "we will look at our starts" was honored is not a question
/// arithmetic can answer - and a host announcing that a man in the room
/// had failed to deliver, on the strength of a sentence this app wrote
/// itself, would be the worst thing in the whole commentary layer.
///
/// So the prompt gets two things and no verdict: his own words, and
/// tonight's facts, which are already there. The host reads one next to
/// the other and the room draws its own conclusion - which is both the
/// honest arrangement and, as it happens, far better radio than being told
/// what to think.
///
/// WRITTEN DOWN, UNLIKE NightPromises, which lapses in twenty-five minutes
/// and loses itself on a restart on purpose. The opposite is wanted here:
/// the whole value is the fortnight, so it survives a restart, a new
/// season and a laptop lid. See <see cref="StaysWorthQuoting"/> for the
/// one limit on that.
/// </summary>
internal sealed class PrincipalDiary
{
    /// <summary>
    /// One thing a team's principal said in the booth.
    /// </summary>
    /// <param name="DriverId">The driver whose principal said it - the transponder, as every result keys on.</param>
    /// <param name="Name">The driver, as the club called them then. For the console and for nothing else; the interview names them from the profile as it stands now.</param>
    /// <param name="PrincipalName">What the club called the principal then, or blank. Kept so a club that has since named him does not have last month's words put into a stranger's mouth.</param>
    /// <param name="AtUtc">When it went to air. Described rather than read out - see <see cref="FactLines"/>.</param>
    /// <param name="RaceLabel">The race the interview followed, as the club labelled it. Blank where it had none.</param>
    /// <param name="Said">
    /// His own words, exactly as the voice read them.
    ///
    /// THE LAST ANSWER OF THE INTERVIEW, which is the third beat - the one
    /// commitment about the team's own work (see
    /// CommentaryService.PrincipalExchanges). The others are a man being
    /// asked how his night went, and nobody needs those put back to them.
    ///
    /// STORED VERBATIM AND NEVER TOUCHED. Not summarized, not shortened,
    /// not tidied: the entire reason the line lands is that it is what he
    /// said, and a paraphrase quoted back as a quotation is the app
    /// putting words in his mouth in front of the people who heard the
    /// real ones.
    /// </param>
    internal sealed record Entry(
        string DriverId,
        string Name,
        string PrincipalName,
        DateTime AtUtc,
        string RaceLabel,
        string Said);

    /// <summary>
    /// How long one of these is worth putting back to him.
    ///
    /// Ten weeks. Long enough to span the fortnight the line is for and a
    /// club's skipped weeks either side of it; short enough that nobody is
    /// asked to account for a sentence from another season, which is a
    /// callback the room has no memory of and which would land as the
    /// announcer having a long and slightly strange grudge.
    ///
    /// SHORTER THAN PastMeetings.StillWorthRecalling (400 days) on
    /// purpose. A race from last season is a fact and ages well; a thing
    /// somebody said does not.
    /// </summary>
    internal static readonly TimeSpan StaysWorthQuoting = TimeSpan.FromDays(70);

    /// <summary>
    /// The longest a stored sentence may be.
    ///
    /// An answer is already capped at CommentaryService.MaxAnswerWords
    /// before it is spoken, so this is a backstop against a hand-edited
    /// file rather than a budget - and it is here because the one thing
    /// read straight out of this file and into a prompt must not be able
    /// to be a kilobyte of someone else's text.
    /// </summary>
    internal const int MostSaidChars = 400;

    /// <summary>
    /// How many teams are remembered.
    ///
    /// One entry per driver and a cap well above any club's grid, so this
    /// is a guard on a file that has been running for years rather than a
    /// limit anybody meets. When it is full the OLDEST goes, which is also
    /// the one that was closest to lapsing anyway.
    /// </summary>
    internal const int MostRemembered = 200;

    private readonly List<Entry> _entries;

    private static string FilePath => Path.Combine(AppPaths.Root, "principal-diary.json");

    private PrincipalDiary(List<Entry> entries)
    {
        _entries = entries;
    }

    /// <summary>Every entry held, newest first. For the console and the tests.</summary>
    internal IReadOnlyList<Entry> All => _entries.OrderByDescending(e => e.AtUtc).ToList();

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable, and RivalryStore for what this
    /// prevents. A diary that loaded empty because the file was locked for
    /// a moment at launch, and was then saved over, would throw away every
    /// callback a club had built up.
    /// </summary>
    internal bool LoadedUnreadable { get; private init; }

    internal static PrincipalDiary Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = SafeFile.ReadAllTextWithRetry(FilePath);
                var loaded = JsonSerializer.Deserialize<List<Entry>>(json);

                if (loaded is not null)
                {
                    // A hand-edited or restored file can say null where the
                    // app never writes one - repaired as RivalryStore and
                    // ThemeStore do. An entry with no driver or nothing
                    // said is dropped rather than repaired: there is no
                    // sensible default for either, and an empty quotation
                    // put back to somebody is worse than no callback.
                    loaded.RemoveAll(e =>
                        e is null
                        || string.IsNullOrWhiteSpace(e.DriverId)
                        || string.IsNullOrWhiteSpace(e.Said));

                    return new PrincipalDiary(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new PrincipalDiary(new List<Entry>()) { LoadedUnreadable = unreadable };
    }

    private void Save()
    {
        if (LoadedUnreadable)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteAllText(FilePath, json);
    }

    /// <summary>
    /// What this team's principal said last, or null when there is nothing
    /// worth putting back to him.
    ///
    /// Null four ways, and every one of them is the honest answer: nobody
    /// asked about, nothing stored for them, a sentence older than
    /// <see cref="StaysWorthQuoting"/>, and - the one that is easy to miss
    /// - a sentence from EARLIER TONIGHT.
    ///
    /// TONIGHT'S OWN WORDS ARE NOT A CALLBACK. A club running three mains
    /// would otherwise have the host open the second booth interview by
    /// quoting the first one back at a man who said it twenty minutes ago,
    /// which is not a callback but an argument. The line wants a gap the
    /// room can feel, and a night is the smallest one there is.
    /// </summary>
    /// <param name="principalName">What the club calls the principal NOW (Voice > Cast), or blank. Only a sentence said under the same name comes back.</param>
    /// <param name="nowUtc">Now, so a lapsed sentence is not quoted. Passed in rather than read so this is testable.</param>
    internal Entry? LastSaidBy(string? driverId, string? principalName, DateTime nowUtc)
    {
        // WHY (20 Oct 2026): Entry.PrincipalName was stored "so a club that
        // has since named him does not have last month's words put into a
        // stranger's mouth", but nothing ever compared it - rename the
        // principal from Alex to Morgan and the host quoted Alex's sentence
        // back to Morgan as his own. Now a sentence said under a different
        // name (blank and a name count as different, which is exactly the
        // "has since named him" case) is not a callback. The ?? covers a
        // hand-edited file with a null name, which Load does not repair.
        var principal = (principalName ?? string.Empty).Trim();

        return LastSaidBy(driverId, nowUtc, e =>
            string.Equals((e.PrincipalName ?? string.Empty).Trim(), principal, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The same, without the principal's name - kept for callers written
    /// before the name was checked.
    /// </summary>
    /// <remarks>
    /// WHY (20 Oct 2026): DOES NOT CHECK THE NAME, so it can still quote one
    /// principal's words to another. Kept only so existing callers keep
    /// building; the booth should call the overload that takes the name.
    /// </remarks>
    internal Entry? LastSaidBy(string? driverId, DateTime nowUtc) => LastSaidBy(driverId, nowUtc, _ => true);

    private Entry? LastSaidBy(string? driverId, DateTime nowUtc, Func<Entry, bool> sameVoice)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return null;
        }

        var tonight = TonightAtTheTrack.NightOf(nowUtc.ToLocalTime());

        return _entries
            .Where(e => string.Equals(e.DriverId, driverId, StringComparison.OrdinalIgnoreCase))
            .Where(sameVoice)
            .Where(e => nowUtc - e.AtUtc <= StaysWorthQuoting)
            .Where(e => TonightAtTheTrack.NightOf(e.AtUtc.ToLocalTime()) < tonight)
            .OrderByDescending(e => e.AtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Keeps what he said, replacing whatever this team's principal said
    /// before.
    ///
    /// ONE SENTENCE PER TEAM, NOT A TRANSCRIPT. A log of every booth
    /// interview a club has ever run would be a queue whose head is the
    /// stalest thing in it - NightPromises' argument about its own cap,
    /// and it applies harder here because the newest sentence is always
    /// the one worth asking about. The old one is not history worth
    /// keeping; it is a callback that has been used.
    ///
    /// Does nothing at all when there is no driver, nothing said, or the
    /// file could not be read this session.
    /// </summary>
    /// <param name="said">His own words. Trimmed, capped at <see cref="MostSaidChars"/>, and otherwise untouched.</param>
    internal void Keep(string? driverId, string? driverName, string? principalName, string? raceLabel, string? said, DateTime atUtc)
    {
        var who = (driverId ?? string.Empty).Trim();
        var words = (said ?? string.Empty).Trim();

        if (who.Length == 0 || words.Length == 0)
        {
            return;
        }

        if (words.Length > MostSaidChars)
        {
            words = words[..MostSaidChars].TrimEnd();
        }

        _entries.RemoveAll(e => string.Equals(e.DriverId, who, StringComparison.OrdinalIgnoreCase));

        _entries.Add(new Entry(
            who,
            (driverName ?? string.Empty).Trim(),
            (principalName ?? string.Empty).Trim(),
            atUtc,
            (raceLabel ?? string.Empty).Trim(),
            words));

        if (_entries.Count > MostRemembered)
        {
            foreach (var old in _entries.OrderBy(e => e.AtUtc).Take(_entries.Count - MostRemembered).ToList())
            {
                _entries.Remove(old);
            }
        }

        Save();
    }

    /// <summary>
    /// Forgets everything stored for one driver.
    ///
    /// Here because a club that renames a driver, hands a transponder to
    /// somebody else, or deletes a race day should not have last month's
    /// words put to whoever holds that transponder now.
    ///
    /// WHY (20 Oct 2026): this used to say "see RaceDayDeleter and
    /// TransponderTransfer for the other half of that", but neither calls
    /// it - nothing in the app does yet. The principal-name check in
    /// LastSaidBy covers a renamed principal; a handed-over transponder
    /// still needs those callers wired in.
    /// </summary>
    internal void Forget(string? driverId)
    {
        var who = (driverId ?? string.Empty).Trim();

        if (who.Length == 0 || _entries.RemoveAll(e => string.Equals(e.DriverId, who, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        Save();
    }

    /// <summary>
    /// What a stored sentence becomes in the prompt: his words, and when
    /// he said them. No more than that.
    ///
    /// THERE IS NO THIRD LINE HERE AND THAT IS THE POINT. The obvious
    /// next line - what has happened since, or whether it came true - is
    /// not written, because the prompt ALREADY CARRIES tonight's facts:
    /// PrincipalFacts has put the night's results, the pace and what it
    /// cost in front of the model a few lines further up. The host reads
    /// the quotation next to those and the room does the arithmetic.
    ///
    /// Adding a line that said how it turned out would mean this file
    /// deciding whether a man kept his word, which it refuses to do - see
    /// the class summary. The restraint is not an omission; it is the
    /// feature.
    ///
    /// Empty for null, which is most nights.
    /// </summary>
    /// <param name="nowUtc">Now, for dating it. Defaults to the clock; passed in by the tests.</param>
    internal static IReadOnlyList<string> FactLines(Entry? entry, DateTime? nowUtc = null)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Said))
        {
            return Array.Empty<string>();
        }

        // THROUGH THE PROMPT CLEANER, because this is the one string in
        // any prompt in this app that came out of a FILE A PERSON CAN
        // EDIT. Everything else in every facts block is computed. Field
        // takes out the brackets and braces that could close a facts block
        // or fake an audio tag, which is exactly what a hand-written line
        // in principal-diary.json could otherwise do - and it would be
        // read out over the PA.
        //
        // AT ITS OWN LENGTH, not CleanLong's sixty characters: this is a
        // whole sentence somebody said, and sixty characters would cut
        // every quotation off mid-word.
        var words = CommentaryService.Field(entry.Said, MostSaidChars);

        if (words.Length == 0)
        {
            return Array.Empty<string>();
        }

        var when = PastMeetings.HowLongAgo(entry.AtUtc, nowUtc ?? DateTime.UtcNow);

        // The race is named only where the result had a label, and the
        // comma belongs to the clause rather than to the label - a race
        // with no name used to leave "in this booth two weeks ago word for
        // word", which is the sort of seam a model smooths over by
        // inventing a race name.
        var where = CommentaryFacts.Clean(entry.RaceLabel) is { Length: > 0 } label
            ? $", after {label},"
            : ",";

        return new[]
        {
            $"What this team's principal said in this booth {when}{where} word for word: \"{words}\"",
        };
    }
}
