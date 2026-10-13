using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The things about the NIGHT that are not about the racing: a dedication,
/// somebody's birthday, the raffle closing at eight, the canteen running
/// out of chips.
///
/// WHY THIS IS THE LAST HOLE IN THE DEPTH PLAN (11 Oct 2026)
///
/// The plan's Tier 2 asked for "the asides that are not about the race",
/// and the ROADMAP's own answer was that nothing in the app holds a line
/// like that and there is nowhere to type one. Everything the announcer
/// says is worked out from the timing: the results, the laps, the history,
/// the championship. All of it is true and none of it is about the room.
///
/// A club night is a room of forty people who know each other. The thing a
/// human caller does that no amount of lap data can is say "and that's
/// Jack's first race since the operation - good to have you back". That is
/// one sentence, it costs nothing, and it is the one people repeat in the
/// car park.
///
/// WHY EVERY NOTE IS APPROVED BEFORE IT CAN BE SPOKEN
///
/// This is the one store in the app whose contents go out over a PA in
/// somebody's words rather than the app's. A note typed by the race
/// director is approved by definition - they typed it. But the obvious next
/// step is letting the ROOM send them in from a phone, and the moment that
/// exists, anonymous text from a crowd is one tick box away from being read
/// aloud to it.
///
/// So the gate is built now, before there is anything behind it:
/// <see cref="ClubNote.Approved"/> is false for anything that did not come
/// from the race director's own keyboard, and <see cref="ToSay"/> offers
/// only approved notes. Adding a phone route later is then a route that
/// writes unapproved notes and a list to tick them off in - and there is no
/// version of that change which can accidentally put a stranger's words on
/// the PA.
///
/// WHY A STORE AND NOT A FEW LINES IN AppSettings
///
/// They expire. A note is for tonight, it is said once, and a club that
/// typed six of them last week must not have them read out this week - so
/// this is a list with a race day on it that is cleared when the day
/// changes, which is not a shape settings.json is for. It is also the one
/// file in the app a club might reasonably want to delete.
/// </summary>
internal sealed class ClubNotes
{
    private static string FilePath => Path.Combine(AppPaths.Root, "club-notes.json");

    /// <summary>
    /// The most notes one night may hold.
    ///
    /// Twenty. Not a limit anybody will reach by typing - it is there so a
    /// phone route added later cannot fill a club's disk or hand the
    /// announcer a hundred things to choose between, and so the file stays
    /// something a person can read.
    /// </summary>
    internal const int MostNotes = 20;

    /// <summary>
    /// The longest one note may be.
    ///
    /// Two hundred characters, which is about fifteen seconds aloud. A
    /// note is an aside, not a speech: the announcer works it into a
    /// sentence of their own, and anything longer stops being something
    /// that can be worked in at all.
    /// </summary>
    internal const int MostChars = 200;

    /// <summary>
    /// The longest a sender's name may be, the page's own maxlength and the
    /// relay's NOTE_MAX_NAME. Held here too because the page's limit is
    /// only the browser's: a request made without it reached the store,
    /// the file and the window at any length.
    /// </summary>
    internal const int MostNameChars = 40;

    /// <summary>
    /// The most of <see cref="MostNotes"/> that may come from the ROOM
    /// rather than from the race director's keyboard.
    ///
    /// Twelve of the twenty, so eight are always left for the person
    /// running the night. Without this split, a phone route is a way for
    /// the room to fill the list and leave the race director unable to add
    /// the one note that actually matters - and they would have no idea
    /// why, because the message would just say the list was full.
    ///
    /// It is a flood ceiling and nothing more. The approval gate is what
    /// keeps a stranger's words off the PA; this only keeps the list a
    /// person can read and the file a size a disk does not notice.
    /// </summary>
    internal const int MostFromTheRoom = 12;

    /// <summary>
    /// How old a note held under no day can be and still be taken onto the
    /// day when it opens - see NewMeeting.
    ///
    /// A DAY, because "added since" is only true for what was added since
    /// the app last started with no day open. Notes typed with no day open
    /// and then never followed by one - a night called off - would
    /// otherwise wait under no day and be read out at the next meeting.
    /// </summary>
    internal static readonly TimeSpan AdoptedWithin = TimeSpan.FromDays(1);

    private sealed class NotesFile
    {
        public string DayId { get; set; } = string.Empty;
        public List<ClubNote> Notes { get; set; } = new();
    }

    private readonly NotesFile _file;

    /// <summary>
    /// Serialises every read and write of <see cref="_file"/>.
    ///
    /// WHY THIS CLASS NEEDS ONE, which it shipped without (12 Oct 2026).
    /// A note from the room arrives on an HttpListener thread
    /// (WebServerService) or is folded in off the relay publisher's own
    /// thread, while the Club Notes window lists them on the UI thread and
    /// the announcer's half-second tick asks for the next one to say. The
    /// notes are a List, and a list added to while it is being enumerated
    /// throws InvalidOperationException - which in ClubNotesForm.Fill, with
    /// no try/catch round it, is a crash dialog in front of a room on the
    /// one night people are sending things in.
    ///
    /// Save() is called from inside this, which puts the file write under
    /// the lock too - wanted rather than tolerated, because two threads
    /// serialising the same object at once is how half a notes file gets
    /// onto a disk.
    /// </summary>
    private readonly object _gate = new();

    private ClubNotes(NotesFile file) => _file = file;

    /// <summary>True when the file was there but could not be read - see SafeFile.HoldWritesAfterUnreadable.</summary>
    internal bool LoadedUnreadable { get; private init; }

    internal static ClubNotes Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var file = JsonSerializer.Deserialize<NotesFile>(SafeFile.ReadAllTextWithRetry(FilePath));

                if (file is not null)
                {
                    file.Notes ??= new List<ClubNote>();

                    return new ClubNotes(file);
                }
            }
        }
        catch (Exception ex)
        {
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new ClubNotes(new NotesFile()) { LoadedUnreadable = unreadable };
    }

    private void Save()
    {
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(
            _file,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Every note held for the night in hand, oldest first - for the page that lists them.</summary>
    internal IReadOnlyList<ClubNote> All
    {
        get
        {
            lock (_gate)
            {
                // A COPY, not the list itself. Handing the live list out
                // put the caller's enumeration outside the lock, where a
                // note arriving from the room appends to it - and
                // ClubNotesForm.Fill walks it with no try/catch. At most
                // MostNotes of them, which is twenty.
                return _file.Notes.ToList();
            }
        }
    }

    /// <summary>Which race day these notes belong to.</summary>
    internal string DayId
    {
        get
        {
            lock (_gate)
            {
                return _file.DayId;
            }
        }
    }

    /// <summary>
    /// A new race day: last week's notes go.
    ///
    /// CLEARED RATHER THAN KEPT AND FILTERED. A note is for one night and
    /// is said once, so there is nothing a kept one could ever be used for
    /// - and a club that opens a new day with six unsaid dedications in the
    /// file would have them read out at the wrong meeting, to the wrong
    /// room, about people who are not there. The one thing worse than an
    /// announcer with nothing to say.
    ///
    /// Cheap and idempotent for the day it is already on, like every other
    /// NewMeeting in the app, so it can be called from a tick.
    /// </summary>
    internal void NewMeeting(string? dayId)
    {
        lock (_gate)
        {
            var day = (dayId ?? string.Empty).Trim();

            if (string.Equals(_file.DayId, day, StringComparison.Ordinal))
            {
                return;
            }

            // FROM NO DAY TO A DAY IS ADOPTED, NOT CLEARED. The app puts
            // this on no day when it starts with none open (which is what
            // clears last week's), so anything held under no day was added
            // since - tonight's, typed or sent in before the day was
            // opened. Clearing it then wiped exactly what the night wanted.
            var adopt = string.IsNullOrEmpty(_file.DayId) && day.Length > 0;

            _file.DayId = day;

            if (!adopt)
            {
                _file.Notes.Clear();
            }
            else
            {
                var now = DateTime.UtcNow;

                _file.Notes.RemoveAll(one => now - one.AddedAtUtc > AdoptedWithin);
            }

            Save();
        }
    }

    /// <summary>
    /// Adds a note the race director typed, approved because they typed
    /// it - and answers why not, in their words, when it cannot be added.
    ///
    /// Null means it went in.
    /// </summary>
    internal string? Add(string? text, string? from = null)
    {
        lock (_gate)
        {
            var said = Tidy(text);

            if (said.Length == 0)
            {
                return "There is nothing to say in it.";
            }

            if (said.Length > MostChars)
            {
                return $"That is {said.Length} characters. A note is an aside the announcer works into a sentence, "
                       + $"so it has to fit in {MostChars} - about fifteen seconds aloud.";
            }

            if (_file.Notes.Count >= MostNotes)
            {
                return $"There are already {MostNotes} notes for tonight. Remove one that has been said.";
            }

            // THE SAME NOTE TWICE is nearly always somebody pressing the
            // button again rather than two different asides. Said once is the
            // whole promise of a note.
            if (_file.Notes.Any(one => string.Equals(Tidy(one.Text), said, StringComparison.CurrentCultureIgnoreCase)))
            {
                return "That note is already on tonight's list.";
            }

            _file.Notes.Add(new ClubNote
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = said,
                From = TidyName(from),
                AddedAtUtc = DateTime.UtcNow,

                // TYPED BY THE RACE DIRECTOR, so approved. Anything arriving
                // from anywhere else must come in as false - see the note on
                // this class.
                Approved = true,
            });

            Save();

            return null;
        }
    }

    /// <summary>
    /// Adds a note that came in from the room - a phone on the club Wi-Fi,
    /// or the club's page on the relay.
    ///
    /// ARRIVES UNAPPROVED, ALWAYS, and that is the whole of the design
    /// this class was built around before either route existed: nothing
    /// with <see cref="ClubNote.Approved"/> false is ever offered to the
    /// announcer, so there is no version of a phone route that can
    /// accidentally put a stranger's words over a PA. The race director
    /// ticks it off, or it is never said.
    ///
    /// Held to <see cref="MostFromTheRoom"/> rather than
    /// <see cref="MostNotes"/>, so the room cannot fill the list and leave
    /// the person running the night unable to add anything.
    ///
    /// Null means it went in. Anything else is why not, in words a
    /// stranger on a phone can read - these messages go back over a web
    /// route to somebody who is not a club official and has no idea what
    /// a note is.
    /// </summary>
    /// <param name="text">The aside, in their words.</param>
    /// <param name="from">Who sent it, if they said. Never trusted for anything but being read out.</param>
    internal string? AddFromTheRoom(string? text, string? from = null)
    {
        lock (_gate)
        {
            var said = Tidy(text);

            if (said.Length == 0)
            {
                return "There is nothing in it.";
            }

            if (said.Length > MostChars)
            {
                return $"That is a bit long - keep it under {MostChars} characters so the announcer can work it "
                       + "into a sentence.";
            }

            if (_file.Notes.Count(one => !WasTyped(one)) >= MostFromTheRoom)
            {
                return "The announcer has plenty to get through already tonight. Try again in a while.";
            }

            if (_file.Notes.Count >= MostNotes)
            {
                return "The announcer has plenty to get through already tonight. Try again in a while.";
            }

            if (_file.Notes.Any(one => string.Equals(Tidy(one.Text), said, StringComparison.CurrentCultureIgnoreCase)))
            {
                // Said as though it worked, because it did: somebody pressing
                // send twice has got their message in, and telling them it
                // was a duplicate invites them to reword it and send it again.
                return null;
            }

            _file.Notes.Add(new ClubNote
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = said,
                From = TidyName(from),
                AddedAtUtc = DateTime.UtcNow,
                FromTheRoom = true,

                // THE GATE. Not a mistake, not a default to be improved on
                // later, and not something a caller may pass in.
                Approved = false,
            });

            Save();

            return null;
        }
    }

    /// <summary>
    /// Whether a note came from the race director's own keyboard, as
    /// opposed to from the room.
    ///
    /// Reads the flag, and treats a note from a file written before the
    /// flag existed as typed - which is what it was, since there was no
    /// other way in.
    /// </summary>
    internal static bool WasTyped(ClubNote note) => !note.FromTheRoom;

    /// <summary>
    /// The notes waiting for the race director to tick them off, oldest
    /// first - what the approval list shows and what the button's count
    /// counts.
    /// </summary>
    internal IReadOnlyList<ClubNote> Waiting
    {
        get
        {
            lock (_gate)
            {
                return _file.Notes.Where(one => !one.Approved).ToList();
            }
        }
    }

    /// <summary>Takes a note off tonight's list. Safe for an id that is not there.</summary>
    internal void Remove(string? id)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            if (_file.Notes.RemoveAll(one => string.Equals(one.Id, id, StringComparison.Ordinal)) > 0)
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Marks a note as approved - for the race director ticking off
    /// something that came in from the room.
    ///
    /// Here rather than a settable property on the note, so that the one
    /// transition this store cares about has one door and the file is
    /// written when it happens.
    /// </summary>
    internal void Approve(string? id)
    {
        lock (_gate)
        {
            if (_file.Notes.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal)) is not { } note)
            {
                return;
            }

            if (note.Approved)
            {
                return;
            }

            note.Approved = true;
            Save();
        }
    }

    /// <summary>
    /// The next note the announcer may say: approved, and not said yet.
    ///
    /// OLDEST FIRST, because a dedication typed at seven is about something
    /// that was happening at seven. Null when there is nothing, which is
    /// the usual answer and is why the segment this feeds is quiet on most
    /// nights.
    /// </summary>
    internal ClubNote? ToSay()
    {
        lock (_gate)
        {
            return _file.Notes.FirstOrDefault(one => one.Approved && one.SaidAtUtc is null);
        }
    }

    /// <summary>
    /// A note has actually gone out.
    ///
    /// Must be called AFTER the line has reached the announcer and never
    /// before, for the reason SponsorRotation gives about paid reads: a
    /// note marked said but never spoken is a dedication the club promised
    /// somebody and nobody heard.
    /// </summary>
    internal void Said(string? id)
    {
        lock (_gate)
        {
            if (_file.Notes.FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.Ordinal)) is not { } note)
            {
                return;
            }

            if (note.SaidAtUtc is not null)
            {
                return;
            }

            note.SaidAtUtc = DateTime.UtcNow;
            Save();
        }
    }

    /// <summary>
    /// Whether tonight's list has room for another from the room.
    ///
    /// EXISTS SO THE RELAY CAN BE TOLD (13 Oct 2026). Both of
    /// <see cref="AddFromTheRoom"/>'s refusals are asked here, in that
    /// order, so the answer cannot drift from the thing it describes - and
    /// it has to be both: the night's own cap on messages from the room
    /// (<see cref="MostFromTheRoom"/>) is the one a busy evening reaches,
    /// and the whole list's cap (<see cref="MostNotes"/>) is the one a
    /// club that has typed a lot of its own reaches.
    ///
    /// Until this was sent, the relay knew only whether the club wanted
    /// shout-outs at all - so it took a thirteenth, answered the sender
    /// {"ok":true}, and the laptop refused it on arrival. The sender was
    /// told it went through.
    /// </summary>
    internal bool NoRoomFromTheRoom
    {
        get
        {
            lock (_gate)
            {
                return _file.Notes.Count(one => !WasTyped(one)) >= MostFromTheRoom
                       || _file.Notes.Count >= MostNotes;
            }
        }
    }

    /// <summary>How many are waiting to be said, and how many are waiting to be approved - for a page to show.</summary>
    internal (int ToSay, int ToApprove, int Said) Counts()
    {
        lock (_gate)
        {
            return (_file.Notes.Count(one => one.Approved && one.SaidAtUtc is null),
                _file.Notes.Count(one => !one.Approved),
                _file.Notes.Count(one => one.SaidAtUtc is not null));
        }
    }

    /// <summary>
    /// Text as it is stored: trimmed, with the line breaks taken out.
    ///
    /// A note goes to a model inside a facts block and then to a voice. A
    /// newline in it would break the one-fact-per-line shape every other
    /// fact in the app keeps, and a voice reads it as nothing at all.
    /// </summary>
    private static string Tidy(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(" ", text.Split('\n', '\r').Select(part => part.Trim()).Where(part => part.Length > 0)).Trim();

    /// <summary>A sender's name as it is stored: tidied, and held to <see cref="MostNameChars"/>.</summary>
    private static string TidyName(string? from)
    {
        var name = Tidy(from);

        if (name.Length <= MostNameChars)
        {
            return name;
        }

        var cut = MostNameChars;

        // Never split a surrogate pair - half an emoji is invalid text.
        if (char.IsHighSurrogate(name[cut - 1]))
        {
            cut--;
        }

        return name[..cut].TrimEnd();
    }
}

/// <summary>
/// One thing about the night that is not about the racing - see
/// <see cref="ClubNotes"/>.
///
/// A class rather than a record because the store changes two fields on a
/// note in place (<see cref="Approved"/> and <see cref="SaidAtUtc"/>) and
/// because System.Text.Json reads it back out of the file.
/// </summary>
internal sealed class ClubNote
{
    /// <summary>What the note is marked said and approved against.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The aside itself, in the club's own words - "it's Jack's first race since his operation".</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Who sent it, where that is known and worth saying - "from the
    /// canteen", "Dave". Empty for a note the race director typed, which
    /// needs no attribution.
    /// </summary>
    public string From { get; set; } = string.Empty;

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the race director has agreed it may be said out loud.
    ///
    /// True for anything typed on the race PC - they typed it. The flag
    /// exists for what comes next: a note sent in from a phone in the room
    /// arrives false, and nothing that is false is ever offered to the
    /// announcer. See the note on <see cref="ClubNotes"/>.
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>When it went out, or null. A note is said once.</summary>
    public DateTime? SaidAtUtc { get; set; }

    /// <summary>
    /// Whether it came in from the room - a phone on the club Wi-Fi or the
    /// club's page on the relay - rather than from the race director's own
    /// keyboard.
    ///
    /// Kept apart from <see cref="Approved"/> because the two answer
    /// different questions and both are needed: Approved is "may this be
    /// said", this is "where did it come from". A note from the room that
    /// has been ticked off is approved AND from the room, and the list
    /// still wants to show which it was - the race director who approved
    /// forty of them wants to see at a glance which ones they wrote.
    ///
    /// False on a note from a file written before this existed, which is
    /// correct: there was no other way in then.
    /// </summary>
    public bool FromTheRoom { get; set; }
}
