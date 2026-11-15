using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Sponsor/promo slides shown in rotation on the Big Screen between
/// leaderboard views - useful at a commercial venue, easily left empty and
/// ignored at home. Image files are copied into a dedicated folder (like
/// track attachments) rather than referenced in place, so moving or
/// deleting the original doesn't silently break the display mid-event.
/// </summary>
public sealed class SponsorStore
{
    private SponsorSettings _settings;

    private static string RootFolder => AppPaths.Root;

    private static string FilePath => Path.Combine(RootFolder, "sponsors.json");
    private static string ImageFolder => Path.Combine(RootFolder, "Sponsors");

    private SponsorStore(SponsorSettings settings)
    {
        _settings = settings;
    }

    public static SponsorStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<SponsorSettings>(SafeFile.ReadAllTextWithRetry(FilePath));
                if (loaded is not null)
                {
                    // Slide names are joined onto the Sponsors folder and
                    // served to anyone on the club network. A name that is
                    // anything but a plain file name - from a hand-edited
                    // file or a restored backup - is dropped here, once, so
                    // nothing further along ever sees it.
                    loaded.SlideFileNames = (loaded.SlideFileNames ?? new List<string>())
                        .Where(PlainFileName.IsSafeLeaf)
                        .ToList();

                    // A hand-edited file, or one from a backup written
                    // before the sponsors themselves were kept here, can
                    // hold a sponsor with no id of its own - and an id is
                    // what the rotation counts a sponsor's reads against,
                    // so one without would be counted against every other
                    // sponsor that also had none. Filled in once, here.
                    //
                    // A null entry ("Sponsors": [ {...}, null ]) is dropped
                    // here too. It threw on sponsor.Id below, and a throw in
                    // here loads the whole file as unreadable - every sponsor
                    // and slide gone, and the next edit saving that over the
                    // club's file. Null text is filled in while here, so
                    // nothing further along has to ask.
                    loaded.Sponsors = (loaded.Sponsors ?? new List<SponsorBooking>())
                        .Where(sponsor => sponsor is not null)
                        .ToList();

                    foreach (var sponsor in loaded.Sponsors)
                    {
                        if (string.IsNullOrWhiteSpace(sponsor.Id))
                        {
                            sponsor.Id = NewSponsorId();
                        }

                        sponsor.Name ??= string.Empty;
                        sponsor.Tagline ??= string.Empty;
                        sponsor.SalesCopy ??= string.Empty;
                        sponsor.PictureFileName ??= string.Empty;
                        sponsor.WebName ??= string.Empty;
                        sponsor.LogoFileName ??= string.Empty;
                        sponsor.Website ??= string.Empty;
                        sponsor.Presents ??= new List<string>();
                    }

                    var store = new SponsorStore(loaded);

                    // Once (22 Oct 2026, bug hunt) - see SegmentsDefaultReviewed.
                    if (!loaded.SegmentsDefaultReviewed)
                    {
                        loaded.ReadDuringSegments = false;
                        loaded.SegmentsDefaultReviewed = true;

                        try
                        {
                            store.Save();
                        }
                        catch
                        {
                            // Applied in memory either way; the next save keeps it.
                        }
                    }

                    return store;
                }
            }
        }
        catch (Exception ex)
        {
            // Corrupt/unreadable file: start fresh rather than crashing on launch.
            // But, when it could not be read at all or no copy of it could be
            // kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        // A new file starts on the new default and needs no review.
        return new SponsorStore(new SponsorSettings { SegmentsDefaultReviewed = true }) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. A briefly locked file used to load empty and the
    /// next save replaced the club's sponsor slide list with nothing.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // these defaults would replace it. The console has already said why.
        if (LoadedUnreadable)
        {
            return;
        }

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public SponsorSettings Current => _settings;

    public IReadOnlyList<string> SlideFileNames => _settings.SlideFileNames;

    /// <summary>Full path to a stored slide, or null if the file has gone missing.</summary>
    public string? ResolvePath(string fileName)
    {
        var path = SlidePath(ImageFolder, fileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// A slide's path inside <paramref name="folder"/>, or null if the name
    /// could point anywhere else. Asked before every read, move and delete -
    /// see PlainFileName.
    /// </summary>
    private static string? SlidePath(string folder, string fileName)
    {
        if (!PlainFileName.IsSafeLeaf(fileName))
        {
            return null;
        }

        var path = Path.Combine(folder, fileName);
        return PlainFileName.IsInside(folder, path) ? path : null;
    }

    /// <summary>
    /// The stored name for a newly added slide: a short unique prefix, so
    /// two files with the same name from different folders don't collide,
    /// then as much of the original name as passes IsSafeLeaf - kept
    /// because it's what the user recognises in the list.
    /// </summary>
    private static string NewSlideFileName(string sourceFilePath)
    {
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var original = Path.GetFileName(sourceFilePath);
        var extension = Path.GetExtension(original);
        var stem = Path.GetFileNameWithoutExtension(original).Trim(' ', '.');

        if (extension.Length > 16 || !PlainFileName.IsSafeLeaf(prefix + extension))
        {
            extension = string.Empty;
        }

        var room = 200 - prefix.Length - 1 - extension.Length;
        if (stem.Length > room)
        {
            stem = stem[..room].TrimEnd(' ', '.');
        }

        var name = $"{prefix}_{stem}{extension}";
        return PlainFileName.IsSafeLeaf(name) ? name : prefix + extension;
    }

    /// <summary>
    /// Copies an image in from wherever the user picked it. Returns the
    /// stored filename, or null with the reason in <paramref name="error"/>.
    /// </summary>
    public string? AddSlide(string sourceFilePath, out string? error)
    {
        try
        {
            // Sized up from its length and its header first (see
            // ImageProbe). Any file used to be copied in unlooked-at, and a
            // slide is decoded whole every time the Sponsors page shows it
            // and every time it comes round on the Big Screen - so a huge
            // photo, or a small file whose header claims tens of thousands
            // of pixels each way, froze the app there, rotation after
            // rotation, or crashed it out of memory.
            if (ImageProbe.PickedProblem(sourceFilePath, "show as a sponsor slide", "Choose a smaller copy.", out var bytes) is { } problem)
            {
                error = problem;
                return null;
            }

            Directory.CreateDirectory(ImageFolder);

            // Prefix with a short unique id so two files with the same name
            // from different folders don't collide and overwrite each other.
            var fileName = NewSlideFileName(sourceFilePath);

            // The bytes that were checked rather than a second copy of the
            // file, so what is stored is exactly what passed.
            File.WriteAllBytes(Path.Combine(ImageFolder, fileName), bytes);

            _settings.SlideFileNames.Add(fileName);
            Save();
            error = null;
            return fileName;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Copies a video in from wherever the user picked it, and puts it in
    /// the rotation beside the pictures. Returns the stored filename, or
    /// null with the reason in <paramref name="error"/>.
    ///
    /// Copied with File.Copy rather than read and written back the way
    /// AddSlide does it: an ad can be hundreds of megabytes, and AddSlide's
    /// way - ReadAllBytes then WriteAllBytes - would hold the whole thing
    /// in memory twice over. Nothing here needs the bytes, because nothing
    /// decodes a video on the way in; SponsorMedia.PickedProblem has
    /// already looked at its length and its header.
    /// </summary>
    public string? AddVideo(string sourceFilePath, out string? error)
    {
        try
        {
            if (SponsorMedia.PickedProblem(sourceFilePath) is { } problem)
            {
                error = problem;
                return null;
            }

            Directory.CreateDirectory(ImageFolder);

            var fileName = NewSlideFileName(sourceFilePath);
            var destination = SlidePath(ImageFolder, fileName);

            if (destination is null)
            {
                // NewSlideFileName already holds its result to IsSafeLeaf,
                // so this cannot happen - but a silent wrong-folder copy is
                // not the failure to risk being wrong about.
                error = "That file's name can't be stored safely. Rename it and try again.";
                return null;
            }

            File.Copy(sourceFilePath, destination, overwrite: false);

            _settings.SlideFileNames.Add(fileName);
            Save();
            error = null;
            return fileName;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// A stored slide's bytes, ready to decode - or null when the file is
    /// more than AddSlide takes in: too long, not a PNG, JPEG, GIF or BMP,
    /// or by its header too many pixels. A slide is kept exactly as it was
    /// picked, so this lets through everything AddSlide does and nothing
    /// bigger; what it stops is one planted in a restored backup, or one
    /// added before AddSlide looked. For the Big Screen and the Sponsors
    /// page, which both used to decode whatever the file held. Throws when
    /// the file can't be read at all; both callers catch that.
    /// </summary>
    public static byte[]? ReadSlide(string path) => ImageProbe.ReadStored(path);

    private static string StashFolder => Path.Combine(ImageFolder, "_removed");

    /// <summary>
    /// Removes a slide from the rotation, moving its image aside rather
    /// than deleting it so the removal can be undone. Deleting outright
    /// would restore the rotation entry on undo but leave it pointing at
    /// a file that no longer exists.
    ///
    /// Aside means the app's undo holding folder (see UndoHoldingFolder),
    /// one folder per removal, so several removals can each be undone and
    /// old ones are cleared after a week. Returns where the image went, for
    /// RestoreSlide - null when it had none or it couldn't be moved (a
    /// locked file just lingers where it was).
    /// </summary>
    public string? RemoveSlide(string fileName)
    {
        var index = _settings.SlideFileNames.IndexOf(fileName);
        _settings.SlideFileNames.Remove(fileName);
        Save();

        string? held = null;
        var path = SlidePath(ImageFolder, fileName);
        if (path is not null && File.Exists(path))
        {
            held = UndoHoldingFolder.MoveIn(path, UndoHoldingFolder.NewFolder());
        }

        _lastRemovedIndex = index;
        return held;
    }

    /// <summary>Where the last removed slide sat in the rotation, so undo can put it back in the same place.</summary>
    private int _lastRemovedIndex = -1;

    /// <summary>
    /// Restores a removed slide to the rotation, at the place it held. Pairs
    /// with RemoveSlide to undo a removal: <paramref name="held"/> is what
    /// RemoveSlide returned. Without it, the old per-name stash is looked in
    /// (removals made by an older version). A slide already back in the
    /// rotation is not listed twice.
    /// </summary>
    public void RestoreSlide(string fileName, int index, string? held = null)
    {
        // Only a name that could have been removed can come back.
        if (!PlainFileName.IsSafeLeaf(fileName))
        {
            return;
        }

        var restored = SlidePath(ImageFolder, fileName);
        if (held is not null && restored is not null)
        {
            UndoHoldingFolder.MoveBack(held, restored);
        }
        else
        {
            try
            {
                var stashed = SlidePath(StashFolder, fileName);
                if (stashed is not null && restored is not null && File.Exists(stashed))
                {
                    Directory.CreateDirectory(ImageFolder);
                    File.Move(stashed, restored, overwrite: true);
                }
            }
            catch
            {
                // The rotation entry still comes back either way.
            }
        }

        if (!_settings.SlideFileNames.Contains(fileName))
        {
            var at = index >= 0 && index <= _settings.SlideFileNames.Count ? index : _settings.SlideFileNames.Count;
            _settings.SlideFileNames.Insert(at, fileName);
        }

        Save();
    }

    /// <summary>Index the most recent RemoveSlide took the file from.</summary>
    public int LastRemovedIndex => _lastRemovedIndex;

    /// <summary>
    /// Raised after an undo has changed the slides or the timing, so the
    /// Sponsors page re-reads the store. Not raised by ordinary saves.
    /// </summary>
    public event Action? Restored;

    /// <summary>Tells whatever shows this store that an undo just changed it. UI thread.</summary>
    public void NotifyRestored() => Restored?.Invoke();

    public void Move(string fileName, int direction)
    {
        var index = _settings.SlideFileNames.IndexOf(fileName);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _settings.SlideFileNames.Count)
        {
            return;
        }

        (_settings.SlideFileNames[index], _settings.SlideFileNames[newIndex]) =
            (_settings.SlideFileNames[newIndex], _settings.SlideFileNames[index]);
        Save();
    }

    // ------------------------------------------------------------------
    // The sponsors themselves, and the reads they bought
    //
    // WHY THEY LIVE HERE AND NOT IN AppSettings
    //
    // This file already IS the club's sponsor settings - the slides, the
    // videos, the rotation's timing - and a sponsor's name, their tagline
    // and the copy they paid to have read are the same kind of thing: a
    // commercial arrangement one club has and the club next door does not.
    // AppSettings is the race laptop's settings, it is already the largest
    // file in the app, and a club that exports or restores their sponsors
    // should get all of them in one go, which they do here (sponsors.json
    // sits beside the Sponsors folder the slides are in).
    //
    // The rotation that decides whose turn it is lives in SponsorReads.cs
    // and holds no settings at all. This gives it the list; it gives
    // nothing back.
    // ------------------------------------------------------------------

    /// <summary>The club's sponsors, in the order they were listed. Empty on a fresh install.</summary>
    public IReadOnlyList<SponsorBooking> Sponsors => _settings.Sponsors;

    /// <summary>
    /// A short id for a new sponsor. Short because it goes in a saved
    /// rotation and in the console, and never shown to anybody - the club
    /// identifies a sponsor by their name, which they are free to change
    /// without the night's read counts moving to somebody else.
    /// </summary>
    private static string NewSponsorId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Adds a sponsor and returns it, so the page can select it and put the
    /// cursor in the name box. Named rather than blank: a list row with no
    /// text in it reads as a fault.
    /// </summary>
    public SponsorBooking AddSponsor(string name = "New sponsor")
    {
        var sponsor = new SponsorBooking { Id = NewSponsorId(), Name = name };

        _settings.Sponsors.Add(sponsor);
        Save();

        return sponsor;
    }

    /// <summary>The sponsor with this id, or null. Asked by the page for whatever is selected.</summary>
    public SponsorBooking? Sponsor(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : _settings.Sponsors.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Takes a sponsor off the list and returns where they were, for undo -
    /// the same shape as RemoveSlide, minus the file to move aside. Minus
    /// one when there was no such sponsor.
    ///
    /// Nothing is moved to the undo holding folder because nothing here is
    /// a file: the whole record goes back in through
    /// <see cref="RestoreSponsor"/>, which is why the caller must keep hold
    /// of it BEFORE calling this.
    /// </summary>
    /// <summary>
    /// What the crowd screen should show for one sponsor's read, or null
    /// when there is no such sponsor.
    ///
    /// THE PICTURE IS CHECKED TO BE ON DISK HERE, not when it is shown. A
    /// club that pointed a read at a slide and then removed that slide
    /// from the rotation has deleted the file, and the honest answer is
    /// then the sponsor's name again rather than a gap on the screen - so
    /// the missing file is resolved to null at the one point that can see
    /// the folder. See SponsorBooking.PictureFileName.
    /// </summary>
    public SponsorOnScreen? OnScreenFor(string id)
    {
        if (Sponsor(id) is not { } sponsor)
        {
            return null;
        }

        var picture = sponsor.PictureFileName;

        // A video is not a picture. The folder holds both, and a read is a
        // spoken thing with its own length - handing LibVLC a clip to play
        // underneath it would be two adverts at once, which is the exact
        // collision the commercial break was built to stop.
        var usable = !string.IsNullOrWhiteSpace(picture)
            && !SponsorMedia.IsVideoName(picture)
            && ResolvePath(picture) is not null;

        return new SponsorOnScreen(usable ? picture : null, sponsor.Name ?? string.Empty);
    }

    public int RemoveSponsor(string id)
    {
        var sponsor = Sponsor(id);

        if (sponsor is null)
        {
            return -1;
        }

        var index = _settings.Sponsors.IndexOf(sponsor);

        _settings.Sponsors.Remove(sponsor);
        Save();

        return index;
    }

    /// <summary>
    /// Puts a removed sponsor back where they were. Pairs with
    /// <see cref="RemoveSponsor"/>. A sponsor already on the list is not
    /// listed twice - the undo may be run after the club has re-added them
    /// by hand.
    /// </summary>
    public void RestoreSponsor(SponsorBooking sponsor, int index)
    {
        if (string.IsNullOrWhiteSpace(sponsor.Id) || Sponsor(sponsor.Id) is not null)
        {
            return;
        }

        var at = index >= 0 && index <= _settings.Sponsors.Count ? index : _settings.Sponsors.Count;

        _settings.Sponsors.Insert(at, sponsor);
        Save();
    }

    /// <summary>
    /// Moves a sponsor up or down the list. The order only decides who gets
    /// the very first read of a night with no history behind it - after
    /// that the rotation is on shares, not on order - but that first read
    /// is the one a club notices.
    /// </summary>
    public void MoveSponsor(string id, int direction)
    {
        var sponsor = Sponsor(id);

        if (sponsor is null)
        {
            return;
        }

        var index = _settings.Sponsors.IndexOf(sponsor);
        var moved = index + direction;

        if (index < 0 || moved < 0 || moved >= _settings.Sponsors.Count)
        {
            return;
        }

        (_settings.Sponsors[index], _settings.Sponsors[moved]) =
            (_settings.Sponsors[moved], _settings.Sponsors[index]);

        Save();
    }

    /// <summary>
    /// Who is sold and where the club lets them be heard - what
    /// CommentaryDirector.SponsorReads is wired to.
    ///
    /// Built fresh each time rather than held: a sponsor sold at the sign-up
    /// desk mid-evening has to be in the rotation by the next gap, and the
    /// rotation handles a late arrival itself (SponsorRotation catches them
    /// up rather than making them wait their turn).
    ///
    /// A sponsor with no name is left out, not read as a blank - the
    /// rotation drops them too, but the club's own count of "how many are
    /// being read" should not include a half-filled row somebody added and
    /// walked away from.
    /// </summary>
    public SponsorReadPlan ReadPlan()
    {
        var slots = _settings.Sponsors
            .Where(s => s.Enabled)
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .Select(s => new SponsorSlot(
                s.Id,
                s.Name,
                Spoken(s.Tagline),
                Math.Clamp(s.Weight, 1, 10),
                Spoken(s.SalesCopy)))
            .ToList();

        var placement = SponsorPlacement.Nowhere;

        if (_settings.ReadDuringAdBreaks)
        {
            placement |= SponsorPlacement.AdBreaks;
        }

        if (_settings.ReadDuringSegments)
        {
            placement |= SponsorPlacement.Segments;
        }

        return new SponsorReadPlan(slots, placement);
    }

    /// <summary>
    /// A typed field as the rotation wants it: null when there is nothing
    /// in it. The rotation treats an empty tagline and an absent one the
    /// same, but "null means they did not buy one" is the thing the code
    /// downstream reads, so it is said here rather than left to a
    /// whitespace test in three places.
    /// </summary>
    private static string? Spoken(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? null : typed;
}

/// <summary>
/// One sponsor the club has sold airtime to, as the Sponsors page edits
/// them and sponsors.json keeps them.
///
/// A class with settable properties rather than the record struct the
/// rotation uses (SponsorSlot): this is what is saved and what a text box
/// writes into as it is typed, and the rotation's one is a snapshot handed
/// over for a single decision. Keeping them apart is what lets the rotation
/// stay a table test with no store anywhere near it.
/// </summary>
public sealed class SponsorBooking
{
    /// <summary>
    /// What the night's read counts are kept against. Set when the sponsor
    /// is added and never changed - the name can be corrected mid-evening
    /// without the reads already given moving to somebody else.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name as it should be SAID, which is not always as it is written
    /// on the banner: "and co" rather than "&amp; Co.", and the
    /// pronunciation spelt out where it has to be. Read by a synthetic
    /// voice, so an ampersand or an abbreviation is a gamble.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// One short line, where they gave one. Heard on their first read of
    /// the night, and - since 21 Oct 2026 - after every credit for a moment
    /// they present (SponsoredMoments.Tag, .Credit and .LeadIn), which is
    /// rationed already and is the sponsor's name riding on the race rather
    /// than an advert.
    /// </summary>
    public string Tagline { get; set; } = string.Empty;

    /// <summary>
    /// The few sentences they paid to have read out, spoken as written.
    /// Held to SponsorCopy.MaxChars by the page that edits it and again by
    /// SponsorCopy.Tidy, which is what every read goes through.
    /// </summary>
    public string SalesCopy { get; set; } = string.Empty;

    /// <summary>
    /// The moments of the race this sponsor puts their name to - "the fastest
    /// lap, presented by" - as SponsoredMoment names. Empty for a sponsor who
    /// bought reads and nothing else, which is every booking saved before
    /// sponsored segments existed. See SponsoredMoments.
    /// </summary>
    public List<string> Presents { get; set; } = new();

    /// <summary>
    /// How big a share of the night's reads they bought. One by default; a
    /// sponsor on two gets twice the reads of a sponsor on one, however
    /// many reads the night turns out to have room for.
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Whether they are in the rotation at all. On by default, and turned
    /// off rather than deleted for a sponsor whose season has finished -
    /// their copy is kept, which is the whole point, and they stop being
    /// read tonight.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The picture the crowd screen shows while this sponsor's read is
    /// being said (12 Oct 2026). A name in the same folder as the slides,
    /// so a club can either upload one for the read or point it at a
    /// picture already in the rotation.
    ///
    /// EMPTY IS THE ORDINARY CASE and has a real answer rather than a
    /// blank screen: the Big Screen shows this sponsor's
    /// <see cref="Name"/>, taken from this page, for the length of the
    /// read. See SponsorOnAir.
    ///
    /// Also treated as empty when the file has gone - a club that removed
    /// it from the slide rotation has deleted it, and the name is the
    /// right answer again rather than an error. See
    /// SponsorStore.OnScreenFor.
    /// </summary>
    public string PictureFileName { get; set; } = string.Empty;

    /// <summary>
    /// The name as it is WRITTEN, for the club's web page (5 Nov 2026) -
    /// "Smith &amp; Co." where <see cref="Name"/> is spelt to be said. Empty
    /// uses Name. See RelaySponsors.
    /// </summary>
    public string WebName { get; set; } = string.Empty;

    /// <summary>
    /// Their logo (14 Nov 2026, stream overlay plan section 1): a
    /// "sponsor-logo-…png" in the Sponsors folder, made by SponsorLogo.Import.
    /// Separate from <see cref="PictureFileName"/>, the slide for their read:
    /// the logo goes on their card on the club's Live page and, later, on the
    /// stream's graphics. Empty is no logo, which is every booking saved
    /// before this existed.
    /// </summary>
    public string LogoFileName { get; set; } = string.Empty;

    /// <summary>
    /// Their website, linked from their card on the club's Live page
    /// (5 Nov 2026), and every tap on it counted for the sponsor report.
    /// Empty is no link.
    /// </summary>
    public string Website { get; set; } = string.Empty;
}

/// <summary>
/// What the crowd screen should show while one sponsor's read goes out -
/// their picture where they have one, and their name where they do not.
///
/// A snapshot handed over for a single decision, like the rotation's own:
/// see SponsorOnAir, which holds it for the length of the read, and
/// SponsorStore.OnScreenFor, which is the only thing that builds one.
/// </summary>
/// <param name="PictureFileName">The picture, already checked to be on disk, or null when there is none to show.</param>
/// <param name="Name">The sponsor as this page spells it - what the screen shows when there is no picture.</param>
public sealed record SponsorOnScreen(string? PictureFileName, string Name);

public sealed class SponsorSettings
{
    /// <summary>Whether to show sponsor slides at all. Off by default - a home setup has no use for this.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long the leaderboard shows before cutting to a sponsor slide.</summary>
    public int LeaderboardSeconds { get; set; } = 60;

    /// <summary>How long each sponsor slide stays up before returning to the leaderboard.</summary>
    public int SlideSeconds { get; set; } = 8;

    /// <summary>Never interrupt the leaderboard to show a slide while a race is actually running - on by default, since mid-race is exactly when people need the live standings.</summary>
    public bool PauseDuringRace { get; set; } = true;

    /// <summary>
    /// Whether a sponsor video plays its own soundtrack. On by default: an
    /// ad with no sound is half an ad, and a club that adds a video has
    /// almost certainly been given one with a voiceover.
    /// </summary>
    public bool PlayVideoSound { get; set; } = true;

    /// <summary>
    /// Whether the music and the crowd bed go silent while a sponsor video
    /// with sound is playing. On by default - see
    /// MusicPlayer.SetAdBreakMuted.
    ///
    /// Only has any effect while PlayVideoSound is on: a silent video has
    /// nothing to be talked over.
    /// </summary>
    public bool MuteMusicDuringVideo { get; set; } = true;

    public List<string> SlideFileNames { get; set; } = new();

    /// <summary>
    /// The sponsors the club has sold airtime to, in the order they listed
    /// them. Empty on a fresh install, which is the opt-in: nothing is read
    /// out until somebody puts a sponsor in here.
    /// </summary>
    public List<SponsorBooking> Sponsors { get; set; } = new();

    /// <summary>
    /// Whether a paid read goes out on the tail of an ad break - once the
    /// sponsor's own video has finished and before the screen goes back to
    /// the standings.
    ///
    /// On by default, unlike nearly everything else in this file, and for a
    /// different reason: the list above is what a home setup leaves empty,
    /// so this only ever applies to a club that has already typed a sponsor
    /// in. One that typed copy wants it read; the switch is here for the
    /// club that wants the slides and not the voice.
    /// </summary>
    public bool ReadDuringAdBreaks { get; set; } = true;

    /// <summary>
    /// Whether a paid read goes out in the gap between races, where the
    /// commentary segments live, as well as in the commercial break.
    ///
    /// OFF BY DEFAULT (21 Oct 2026). A read heard straight after a race,
    /// with no break around it, sounded like the announcer had wandered into
    /// an advert - the commercial break is where a club expects its sponsors
    /// to be heard. Ticked, the reads also rotate through the gaps between
    /// races. A club that already chose is not changed: the saved choice
    /// stands.
    /// </summary>
    public bool ReadDuringSegments { get; set; }

    /// <summary>
    /// Set once the switch to "off by default" above has been applied to
    /// this file (22 Oct 2026, bug hunt). Every file saved before then holds
    /// ReadDuringSegments = true - the old default, written by every save,
    /// not a choice anybody made - so the change never reached a club that
    /// already had a sponsors file. Absent (false) on those files: Load
    /// turns the reads between races off once, then sets this.
    /// </summary>
    public bool SegmentsDefaultReviewed { get; set; }
}
