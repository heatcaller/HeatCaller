using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// THE WARM STANDBY PC (11 Nov 2026, race-night build plan, Phase 6.2): the
/// rules a second laptop on the track's network goes by while it keeps a
/// copy of the timing PC's race-night data, and when it takes over. Pure,
/// so all of it is tested; the moving parts are StandbyMirror (the copying),
/// WebServerService.Standby (the timing PC handing the copy out) and the
/// Standby PC card on Backup &amp; Restore (the Take Over button).
///
/// WHY (11 Nov 2026): a laptop that dies mid-meeting is the club's worst
/// night - LiveTime's answer is Parent/Child mode, a second PC that follows
/// the first. This is the first, deliberately small step towards that:
///
///   - ONE WAY. The timing PC hands out a copy; the standby only ever reads
///     it. Nothing the standby does can reach the timing PC.
///   - NEVER INTO THE LIVE FOLDER. The copies are kept as zips in a folder
///     of their own (MirrorFolder). The standby's own running data is never
///     written to while it is mirroring, so there is no moment where two
///     copies of the app are both writing the same race.
///   - TAKE OVER BY HAND. The director decides the timing PC is gone and
///     presses Take Over. That restores the newest copy through the same
///     code every backup restore goes through (BackupService.RestoreBackup,
///     with its safety backup first), restarts, and this PC is the timing
///     PC - the director plugs the decoder in. No automatic failover: two
///     PCs both deciding they are in charge is the distributed-state
///     problem the plan warns about, and it is far worse than thirty
///     seconds of somebody pressing a button.
///   - ONCE TAKEN OVER, NEVER COPIED OVER. Take Over leaves a marker
///     (TakenOverMarkerName) in the data folder. While it is there the
///     mirror refuses to copy and Take Over refuses to run again, so a
///     timing PC that comes back to life can never have its night pulled
///     over the one this PC has been timing since.
///
/// The copy is the club's data, not this PC's: drivers, classes, rounds,
/// results, check-ins, the schedule, the race in progress. The settings
/// and anything else that belongs to the machine (MachineOwnFiles) stay
/// as the standby has them - its decoder port, its voice, its own relay
/// keys. Keys and PINs never travel in the copy at all.
/// </summary>
internal static class WarmStandby
{
    /// <summary>Where the timing PC hands the copy out, on its Local Screens server.</summary>
    public const string SnapshotPath = "/api/standby/snapshot";

    /// <summary>The header the copy's time (UTC, round-trip format) comes back in.</summary>
    public const string TakenAtHeader = "X-HeatCaller-Snapshot-Utc";

    /// <summary>
    /// The header saying how old the copy already is when it is sent, in
    /// whole milliseconds on the timing PC's own clock - see CopyTakenOnThisClock.
    /// </summary>
    public const string AgeHeader = "X-HeatCaller-Snapshot-Age-Ms";

    /// <summary>
    /// The longest age the AgeHeader may say and still be believed. The
    /// timing PC reuses a copy for a few seconds only; anything more is a
    /// broken answer, and the stamp is used instead.
    /// </summary>
    public static readonly TimeSpan MostBelievableServedAge = TimeSpan.FromMinutes(1);

    /// <summary>The header the timing PC's name comes back in, for the standby to show.</summary>
    public const string PrimaryNameHeader = "X-HeatCaller-Primary";

    /// <summary>How often the standby asks for a fresh copy.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(15);

    /// <summary>After this long without a fresh copy the standby says it is behind.</summary>
    public static readonly TimeSpan Behind = TimeSpan.FromMinutes(1);

    /// <summary>The newest copy, in MirrorFolder.</summary>
    public const string LatestFileName = "standby-latest.zip";

    /// <summary>The copy before it, kept in case the newest turns out wrong.</summary>
    public const string PreviousFileName = "standby-previous.zip";

    /// <summary>
    /// Left in the data folder by Take Over. While it is there this PC is
    /// the timing PC and is never mirrored into - see the class notes.
    /// </summary>
    public const string TakenOverMarkerName = "standby-taken-over.json";

    /// <summary>The largest copy the standby accepts. The stores come to a few MB; this is a crafted or broken answer.</summary>
    public const long MaxSnapshotBytes = 64L * 1024 * 1024;

    /// <summary>The most a copy may unpack to, for the same reason.</summary>
    public const long MaxUnpackedBytes = 512L * 1024 * 1024;

    /// <summary>Where the copies are kept: a folder of their own beside the backups, never inside the data folder.</summary>
    public static string MirrorFolder => Path.Combine(BackupService.DefaultBackupFolder, "Standby Copies");

    /// <summary>
    /// The music and sound lists: settings that name audio files in this
    /// PC's own folders (Music, the soundscapes, the club's imaging), which
    /// are never copied. Part of MachineOwnFiles.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): these were handed out and put in place
    /// on Take Over like the club's stores, but the files they point at never
    /// travel. So after Take Over the music list showed the timing PC's own
    /// tracks as missing, the standby's own added tracks dropped off it, and
    /// a soundscape made on the standby was gone from its list - on the one
    /// night it mattered, with nothing saying why. They now stay as the
    /// standby has them, like its voice. Sponsors still travel: their names
    /// are the club's and are read out and shown on the phones.
    /// </summary>
    public static readonly IReadOnlyCollection<string> MachineOwnSoundLists = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "music.json",
        "themes.json",
        "ambience.json",
        "imaging.json",
        "sound-effects.json",
    };

    /// <summary>
    /// Files at the top of the data folder that belong to this PC, not to
    /// the club, so are neither handed out nor replaced on Take Over: the
    /// settings (decoder port, voice choice, sealed keys), the voice files,
    /// the music and sound lists (MachineOwnSoundLists), this PC's own
    /// metering and licence acceptance, the restore record, and the
    /// taken-over marker itself.
    ///
    /// The window of published results is a setting too, but it is no use
    /// without relay-results.json, which IS copied - so that file carries
    /// the window with it (RelayResultsIndex.Ledger.From).
    /// </summary>
    public static readonly IReadOnlyCollection<string> MachineOwnFiles = new HashSet<string>(MachineOwnSoundLists, StringComparer.OrdinalIgnoreCase)
    {
        "settings.json",
        "voice-settings.json",
        "voice-allowance.json",
        "usage-meters.json",
        "terms-accepted.json",
        RestoredSettings.FileName,
        TakenOverMarkerName,

        // Not a store (.txt), so never copied anyway; listed so Take Over
        // keeps the standby's applied soundscape rather than clearing it
        // as "not in the backup" - its music is this PC's own.
        ThemeStore.ActiveFileName,
    };

    /// <summary>
    /// Whether a file at the top of the data folder goes in the copy: every
    /// .json store except the machine's own (MachineOwnFiles) and a save's
    /// half-written temp file. Takes a bare file name - anything with a
    /// folder in it is not a store and is never copied.
    /// </summary>
    public static bool IsMirroredFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains(':')
            || name.StartsWith('.'))
        {
            return false;
        }

        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
               && !MachineOwnFiles.Contains(name);
    }

    /// <summary>
    /// Whether a PIN is good enough to guard the copy: eight characters or
    /// more, letters and digits only. Longer than the remote's four on
    /// purpose - the copy is every driver's name, phone and email, not a
    /// red flag - and letters allowed because it is typed once on a
    /// keyboard, not on a phone's number pad.
    /// </summary>
    public static bool IsUsablePin(string? pin) =>
        pin is { Length: >= 8 } && pin.All(char.IsAsciiLetterOrDigit);

    /// <summary>
    /// Whether an address is on the track's own network: this PC, the
    /// private IPv4 ranges a venue router hands out, link-local, and IPv6
    /// unique-local and link-local. The copy is only ever handed to these -
    /// a timing PC that is somehow reachable from the internet still never
    /// gives the club's data to it.
    /// </summary>
    public static bool IsLanAddress(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();

            // fe80::/10 link-local, fc00::/7 unique-local.
            return (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) || (b[0] & 0xfe) == 0xfc;
        }

        return false;
    }

    /// <summary>
    /// The timing PC's address as typed on the standby, made into the
    /// address of its copy - or null with a reason when it can't be.
    /// Takes "192.168.1.20", "192.168.1.20:8081", "TIMING-PC" or a full
    /// "http://192.168.1.20:8080/" alike. Only addresses on the track's own
    /// network: a private IP, or a name with no dots (or .local / .lan /
    /// .home), the names a router or Windows hands out on the club's own
    /// network - the same rule the Local Screens server keeps.
    /// </summary>
    public static Uri? SnapshotAddress(string? typed, int defaultPort, out string? problem)
    {
        problem = null;
        var text = (typed ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            problem = "Type the timing PC's address.";
            return null;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            problem = "That doesn't look like an address. Type it as shown on the timing PC, for example 192.168.1.20:8080.";
            return null;
        }

        var host = uri.Host.Trim('[', ']').TrimEnd('.');

        var onTheTrackNetwork = IPAddress.TryParse(host, out var ip)
            ? IsLanAddress(ip)
            : !host.Contains('.')
              || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
              || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
              || host.EndsWith(".home", StringComparison.OrdinalIgnoreCase);

        if (!onTheTrackNetwork)
        {
            problem = "The timing PC has to be on this network (an address like 192.168.x.x or the PC's name). The copy never goes over the internet.";
            return null;
        }

        var port = HasPort(text) ? uri.Port : defaultPort;

        return new UriBuilder(uri.Scheme, uri.Host, port, SnapshotPath).Uri;
    }

    /// <summary>
    /// Whether an address names its port: "host:8081" or "[fe80::1]:8081".
    /// Uri can't say - it reports port 80 the same whether it was typed or
    /// not - and the Local Screens server is on 8080 unless moved.
    /// </summary>
    private static bool HasPort(string withScheme)
    {
        var authority = withScheme[(withScheme.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var slash = authority.IndexOf('/');
        if (slash >= 0)
        {
            authority = authority[..slash];
        }

        var afterHost = authority.StartsWith('[') && authority.IndexOf(']') is var close and > 0
            ? authority[(close + 1)..]
            : authority.Contains(':') && authority.IndexOf(':') == authority.LastIndexOf(':')
                ? authority[authority.IndexOf(':')..]
                : string.Empty;

        return afterHost.Length > 1 && afterHost[0] == ':' && afterHost[1..].All(char.IsAsciiDigit);
    }

    /// <summary>What a received copy turned out to be: usable, with how many stores it holds, or refused and why.</summary>
    public sealed record Check(bool Ok, int Files, string? Problem);

    /// <summary>
    /// Checks a copy before it is kept, so that only a copy Take Over can
    /// actually restore ever replaces the last good one. Every entry must
    /// be a store at the top level (IsMirroredFile) - nothing with a folder
    /// in its name, so nothing that could land outside the data folder -
    /// the drivers file must be there (what BackupService.RestoreBackup
    /// looks for), and the whole must unpack to a sane size, counted by
    /// what really comes out rather than what the zip claims. Never throws.
    /// </summary>
    public static Check Validate(Stream zip)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);

            var files = 0;
            var hasProfiles = false;
            long unpacked = 0;
            var buffer = new byte[81920];

            foreach (var entry in archive.Entries)
            {
                // WHY (23 Nov 2026, bug hunt 24): a timing PC on an older
                // HeatCaller still sends the music and sound lists, which are
                // now this PC's own. Refusing the whole copy for one of them
                // would stop the standby copying at all until both PCs were
                // updated, so they are let through (their bytes still count
                // towards the size cap) and not counted as club stores.
                var soundList = MachineOwnSoundLists.Contains(entry.FullName);

                if (!soundList && !IsMirroredFile(entry.FullName))
                {
                    return new Check(false, 0, $"The copy has a file it shouldn't (\"{entry.FullName}\"). It was not kept.");
                }

                using (var input = entry.Open())
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        unpacked += read;
                        if (unpacked > MaxUnpackedBytes)
                        {
                            return new Check(false, 0, "The copy unpacks to far more than race data could. It was not kept.");
                        }
                    }
                }

                if (soundList)
                {
                    continue;
                }

                hasProfiles |= entry.FullName.Equals("profiles.json", StringComparison.OrdinalIgnoreCase);
                files++;
            }

            if (!hasProfiles)
            {
                return new Check(false, 0, "The timing PC has no drivers saved yet, so there is nothing to take over from.");
            }

            return new Check(true, files, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return new Check(false, 0, "The copy arrived damaged. It was not kept; the next one will be tried.");
        }
    }

    /// <summary>Written into the data folder by Take Over - see TakenOverMarkerName.</summary>
    public sealed record TakenOver(DateTime AtUtc, string? From, DateTime? CopyTakenUtc);

    /// <summary>The marker in a data folder, or null when this PC has never taken over. Never throws; a marker that can't be read still counts.</summary>
    public static TakenOver? ReadTakenOver(string dataRoot)
    {
        var path = Path.Combine(dataRoot, TakenOverMarkerName);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TakenOver>(File.ReadAllText(path))
                   ?? new TakenOver(File.GetLastWriteTimeUtc(path), null, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Damaged, but there: still taken over. Safer to refuse a copy
            // than to pull one over a night this PC may be timing.
            return new TakenOver(DateTime.MinValue, null, null);
        }
        catch
        {
            return File.Exists(path) ? new TakenOver(DateTime.MinValue, null, null) : null;
        }
    }

    /// <summary>Leaves the marker. Throws if it can't, so Take Over never goes ahead unmarked.</summary>
    public static void WriteTakenOver(string dataRoot, TakenOver marker)
    {
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, TakenOverMarkerName), JsonSerializer.Serialize(marker));
    }

    /// <summary>Takes the marker away: "make this a standby again", asked for by hand.</summary>
    public static void ClearTakenOver(string dataRoot)
    {
        var path = Path.Combine(dataRoot, TakenOverMarkerName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>How the standby is getting on, as one value any thread can read.</summary>
    /// <param name="Enabled">This PC is set to be a standby.</param>
    /// <param name="LastCopyUtc">When the newest kept copy was taken on the timing PC, or null.</param>
    /// <param name="LastTriedUtc">When the standby last asked.</param>
    /// <param name="Files">How many stores the newest copy holds.</param>
    /// <param name="Primary">The timing PC's name as it gave it, or the address typed.</param>
    /// <param name="LastError">Why the last ask failed, or null when it worked.</param>
    /// <param name="TakenOverUtc">Set once this PC has taken over: it is the timing PC now and copies nothing.</param>
    public sealed record MirrorStatus(
        bool Enabled,
        DateTime? LastCopyUtc,
        DateTime? LastTriedUtc,
        int Files,
        string? Primary,
        string? LastError,
        DateTime? TakenOverUtc)
    {
        public static readonly MirrorStatus Off = new(false, null, null, 0, null, null, null);
    }

    /// <summary>
    /// The AgeHeader's value, worked out on the timing PC: how long ago,
    /// by its own clock, the copy it is sending was taken. Never negative.
    /// </summary>
    public static string ServedAgeHeaderValue(DateTime takenUtc, DateTime nowUtc)
    {
        var ms = (long)Math.Max(0, (nowUtc - takenUtc).TotalMilliseconds);
        return ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// When a copy that just arrived was taken, on THIS PC's clock - the
    /// value MirrorStatus.LastCopyUtc holds, and every age shown is worked
    /// out from. With the AgeHeader (a number from 0 up to
    /// MostBelievableServedAge) it is the moment it arrived less that age;
    /// without it (a timing PC on an older HeatCaller) the timing PC's stamp,
    /// never later than the moment it arrived.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the age was this PC's clock less the
    /// timing PC's stamp - one PC's clock against another's. Two offline
    /// track laptops a few minutes apart, or one set to the wrong time zone,
    /// made a copy that arrived a second ago read "7 min ago" (or "2 h") all
    /// night, "BEHIND" after one missed ask, and Take Over said the newest
    /// copy was hours old - enough to talk a director out of a good copy.
    /// An age measured on one clock and added on the other cancels the
    /// difference out.
    /// </summary>
    public static DateTime CopyTakenOnThisClock(DateTime receivedUtc, string? servedAgeHeader, DateTime? stampUtc)
    {
        if (long.TryParse(servedAgeHeader?.Trim(), System.Globalization.NumberStyles.None,
                          System.Globalization.CultureInfo.InvariantCulture, out var ms)
            && ms <= MostBelievableServedAge.TotalMilliseconds)
        {
            return receivedUtc - TimeSpan.FromMilliseconds(ms);
        }

        var taken = stampUtc ?? receivedUtc;
        return taken > receivedUtc ? receivedUtc : taken;
    }

    /// <summary>How long ago, in the fewest words: "12 s", "3 min", "2 h".</summary>
    public static string Age(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalSeconds < 60
            ? $"{(int)span.TotalSeconds} s"
            : span.TotalMinutes < 120
                ? $"{(int)span.TotalMinutes} min"
                : $"{(int)span.TotalHours} h";
    }

    /// <summary>
    /// The standby's status in one line, for the Standby PC card and the
    /// Race Control bar. Null when this PC is not a standby.
    /// </summary>
    public static string? Line(MirrorStatus status, DateTime nowUtc)
    {
        if (status.TakenOverUtc is not null)
        {
            return "This PC took over as the timing PC. Standby copying is off.";
        }

        if (!status.Enabled)
        {
            return null;
        }

        var from = string.IsNullOrWhiteSpace(status.Primary) ? "the timing PC" : status.Primary;

        if (status.LastCopyUtc is not { } last)
        {
            return status.LastError is { } first
                ? $"Standby PC: no copy yet from {from}. {first}"
                : $"Standby PC: waiting for the first copy from {from}.";
        }

        var age = nowUtc - last;
        var line = $"Standby PC: copy of {from} from {Age(age)} ago";

        if (status.LastError is { } error)
        {
            return age >= Behind
                ? $"{line} - BEHIND. {error}"
                : $"{line}. {error}";
        }

        return line + ".";
    }

    /// <summary>
    /// The question Take Over asks, with how old the copy is, said plainly:
    /// anything the timing PC saved after it is not on this PC. A count of
    /// zero is "not known" and is left out: no copy that is kept is empty,
    /// and "0 files" would say a good copy was.
    /// </summary>
    public static string TakeOverQuestion(DateTime copyTakenUtc, DateTime nowUtc, string? primary, int files)
    {
        var from = string.IsNullOrWhiteSpace(primary) ? "the timing PC" : primary;
        var age = nowUtc - copyTakenUtc;

        // No count is one not known, not an empty copy: a standby started
        // after the timing PC died has the copy on disk but has never been
        // told how many files are in it, and "(0 files)" read as nothing to
        // take over from at the one moment the question matters.
        var holds = files > 0 ? $" ({Plural.Of(files, "file")})" : string.Empty;

        return $"Take over from {from}?\n\n"
               + $"The newest copy is {Age(age)} old{holds}. Anything {from} saved after that is not on this PC - "
               + "check the last race's result once you are running.\n\n"
               + "This PC's current data is saved to a safety backup first, then the copy is put in place and HeatCaller restarts. "
               + "After that this PC is the timing PC: connect the decoder to it. It stops copying, and nothing is ever copied over it again "
               + "unless you make it a standby again by hand.\n\n"
               + "Only do this when the timing PC really can't carry on. Continue?";
    }
}
