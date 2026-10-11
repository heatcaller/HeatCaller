namespace HeatCaller;

/// <summary>
/// The show music that ships with the app: music beds to run under the
/// voice, and openers to land in front of it.
///
/// WHERE. Assets\ShowMusic\Beds\*.mp3 and Assets\ShowMusic\Openers\*.mp3
/// beside the executable, copied there by the build (see the csproj, and
/// note the Link= attribute there, without which MSBuild flattens the two
/// folders into one and every lookup here stops finding anything). Played
/// from there rather than copied into %AppData%, for the reason SoundPacks
/// gives for doing the same: nobody edits them, and a new version of the
/// app replacing them is exactly what should happen.
///
/// HOW A PIECE IS CHOSEN. Per part, in this order - the same three tiers
/// SoundPacks sets out for an effect, and deliberately the same shape:
///   1. the club's own file, if one is set and still there;
///   2. the piece this part is set to from the shipped library;
///   3. nothing, and the show runs as it did before there was any of this -
///      no opener, a bed-swell instead of a sting, or no music at all.
///
/// WHY A FOLDER LISTING RATHER THAN A TABLE. SoundPacks hard-codes its five
/// packs because each carries a description and a set of per-effect volumes
/// that have to be written down somewhere. A bed has none of that: its name
/// is the whole of what a club needs to choose by, and the shape of it -
/// where it fades in, where its body loops - is MEASURED from the file by
/// ShowShape.Find rather than declared. So the library is whatever is in
/// the folder, and adding a piece is dropping an mp3 in beside the others.
///
/// NAMING. "Stinger" is already taken in this app - see StingerLibrary,
/// which holds recorded VOICE lines - so the short pieces are openers here
/// and never stingers. They are offered for the STING as well, because a
/// mark between two items of a block is the same kind of sound; whether one
/// is short enough to work as a mark is something a club hears for itself,
/// and ShowMusic.Describe says how long each one runs.
/// </summary>
internal static class ShowMusicLibrary
{
    /// <summary>One piece of shipped music: what it is called, and where it is.</summary>
    /// <param name="Name">The file's name without its extension - "Hard Count (150 BPM)". What a club picks it by, and what is stored.</param>
    /// <param name="Path">The full path beside the executable.</param>
    internal sealed record Piece(string Name, string Path);

    /// <summary>The folder for a part's pieces, under Assets beside the exe.</summary>
    private const string Root = "ShowMusic";

    /// <summary>Which folder a part's shipped pieces are in.</summary>
    private static string FolderFor(ShowPart part) => part == ShowPart.Bed ? "Beds" : "Openers";

    /// <summary>
    /// The shipped pieces for a part, by name.
    ///
    /// Read from disk each time rather than cached. It is one directory
    /// listing of a handful of files, it is asked only when the Commentary
    /// page is drawn or a show opens, and a cache here would hold a stale
    /// list for the rest of the night after an update replaced the folder.
    /// Empty when the folder is missing, which is a damaged install and
    /// costs the club the shipped music rather than the show.
    /// </summary>
    internal static IReadOnlyList<Piece> For(ShowPart part)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", Root, FolderFor(part));

            if (!Directory.Exists(folder))
            {
                return Array.Empty<Piece>();
            }

            return Directory.EnumerateFiles(folder)
                .Where(path => ShowMusic.Extensions.Contains(
                    System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => new Piece(System.IO.Path.GetFileNameWithoutExtension(path), path))

                // By name, so the list reads the same every time and does
                // not depend on the order the file system hands them back.
                .OrderBy(piece => piece.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            // A folder that cannot be listed is a damaged install. The club
            // loses the shipped music and keeps their own - see the class
            // summary's third tier.
            return Array.Empty<Piece>();
        }
    }

    /// <summary>
    /// The shipped file this part is set to, or null when it is set to
    /// nothing, or to a name that is not in the folder any more.
    ///
    /// A name that has gone answers NULL rather than falling back to
    /// another piece: the club chose that one, and quietly playing a
    /// different piece under their broadcast is worse than playing none.
    /// ShowMusic.Describe says so on the page.
    /// </summary>
    internal static string? FileFor(ShowPart part, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var wanted = name.Trim();

        foreach (var piece in For(part))
        {
            if (string.Equals(piece.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return piece.Path;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a name is one this library still has - for the page, which
    /// says when a chosen piece has gone.
    /// </summary>
    internal static bool Has(ShowPart part, string? name) => FileFor(part, name) is not null;

    /// <summary>
    /// What a club's settings should start at on a machine that has never
    /// had them set.
    ///
    /// THE OPENER AND THE BED, NOT THE STING. A sting is a MARK between two
    /// items - a second or two - and nothing shipped is that short: the
    /// openers run six to ten seconds, which is a piece of music rather
    /// than a punctuation mark. So the sting starts at nothing and the show
    /// marks its breaks with a swell of the bed, exactly as it did before
    /// this library existed, and a club that wants one of the openers there
    /// can choose it.
    ///
    /// Null when the folder has nothing in it, so a damaged install writes
    /// no setting rather than a name that will never resolve.
    /// </summary>
    internal static string? DefaultFor(ShowPart part)
    {
        if (part == ShowPart.Sting)
        {
            return null;
        }

        var pieces = For(part);

        if (pieces.Count == 0)
        {
            return null;
        }

        // The one named as the first choice if it is there, otherwise
        // whatever sorts first - so a club always gets something rather
        // than nothing because a file was renamed.
        var wanted = part == ShowPart.Bed ? PreferredBed : PreferredOpener;

        foreach (var piece in pieces)
        {
            if (string.Equals(piece.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return piece.Name;
            }
        }

        return pieces[0].Name;
    }

    /// <summary>
    /// The bed a fresh install starts on. Mid-tempo and brass-led rather
    /// than the fastest or the most cinematic: it has to sit under a voice
    /// reading lap times for three minutes without becoming the thing the
    /// room is listening to.
    /// </summary>
    private const string PreferredBed = "Opening Drive (132 BPM)";

    /// <summary>The opener a fresh install starts on - the plainest of them, and the one whose name says what it is for.</summary>
    private const string PreferredOpener = "Opening Hit";
}
