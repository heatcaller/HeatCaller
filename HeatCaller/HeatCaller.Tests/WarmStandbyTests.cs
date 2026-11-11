using System.IO.Compression;
using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The warm standby PC (11 Nov 2026, race-night build plan, Phase 6.2) and
/// the offline-first line (Phase 6.3): what is copied, who may have it, the
/// checks on a copy, the mirror, Take Over's restore and its marker.
/// </summary>
public sealed class WarmStandbyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Mirror => Path.Combine(_data.Folder, "mirror");

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    // --- What is copied ---------------------------------------------------------

    [Theory]
    [InlineData("profiles.json", true)]
    [InlineData("race-rounds.json", true)]
    [InlineData("checkin.json", true)]
    [InlineData("race-in-progress.json", true)]
    [InlineData("settings.json", false)]
    [InlineData("SETTINGS.JSON", false)]
    [InlineData("voice-settings.json", false)]
    [InlineData("usage-meters.json", false)]
    [InlineData("restored-settings.json", false)]
    [InlineData(WarmStandby.TakenOverMarkerName, false)]
    [InlineData("secrets.hgk", false)]
    [InlineData("events.json.1234.tmp", false)]
    [InlineData("Music/index.json", false)]
    [InlineData("..\\profiles.json", false)]
    [InlineData("../profiles.json", false)]
    [InlineData("", false)]
    public void Only_the_clubs_stores_at_the_top_are_copied(string name, bool copied) =>
        Assert.Equal(copied, WarmStandby.IsMirroredFile(name));

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("club2026night", true)]
    [InlineData("1234567", false)]
    [InlineData("1234 5678", false)]
    [InlineData("pass-word!", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_standby_pin_is_eight_letters_or_digits(string? pin, bool usable) =>
        Assert.Equal(usable, WarmStandby.IsUsablePin(pin));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.4.4", true)]
    [InlineData("172.32.4.4", false)]
    [InlineData("169.254.10.10", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:4860::8888", false)]
    [InlineData("::ffff:192.168.1.9", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void The_copy_only_goes_to_the_tracks_own_network(string address, bool lan) =>
        Assert.Equal(lan, WarmStandby.IsLanAddress(IPAddress.Parse(address)));

    [Fact]
    public void No_address_is_not_the_network() => Assert.False(WarmStandby.IsLanAddress(null));

    // --- The timing PC's address, as typed --------------------------------------

    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20:8080/api/standby/snapshot")]
    [InlineData(" 192.168.1.20:8081 ", "http://192.168.1.20:8081/api/standby/snapshot")]
    [InlineData("http://192.168.1.20:8080/", "http://192.168.1.20:8080/api/standby/snapshot")]
    [InlineData("http://192.168.1.20/", "http://192.168.1.20:8080/api/standby/snapshot")]
    [InlineData("192.168.1.20:80", "http://192.168.1.20/api/standby/snapshot")]
    [InlineData("TIMING-PC", "http://timing-pc:8080/api/standby/snapshot")]
    [InlineData("timing.local:9000", "http://timing.local:9000/api/standby/snapshot")]
    [InlineData("[fe80::1]:8080", "http://[fe80::1]:8080/api/standby/snapshot")]
    public void The_typed_address_becomes_the_copys_address(string typed, string expected)
    {
        var uri = WarmStandby.SnapshotAddress(typed, 8080, out var problem);

        Assert.Null(problem);
        Assert.Equal(expected, uri!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8.8.8.8")]
    [InlineData("heatcaller.example.com")]
    [InlineData("ftp://192.168.1.20")]
    [InlineData("http://user:pw@192.168.1.20")]
    public void An_address_off_the_track_network_is_refused(string typed)
    {
        Assert.Null(WarmStandby.SnapshotAddress(typed, 8080, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    // --- Checking a copy --------------------------------------------------------

    [Fact]
    public void A_copy_of_stores_with_drivers_is_good()
    {
        var check = WarmStandby.Validate(new MemoryStream(Zip(("profiles.json", "[]"), ("race-rounds.json", "[]"))));

        Assert.True(check.Ok);
        Assert.Equal(2, check.Files);
    }

    [Theory]
    [InlineData("Startup/run.bat")]
    [InlineData("../../evil.json")]
    [InlineData("settings.json")]
    [InlineData("secrets.hgk")]
    public void A_copy_with_anything_but_stores_is_refused(string extra)
    {
        var check = WarmStandby.Validate(new MemoryStream(Zip(("profiles.json", "[]"), (extra, "x"))));

        Assert.False(check.Ok);
        Assert.Contains(extra, check.Problem);
    }

    [Fact]
    public void A_copy_without_drivers_is_refused() =>
        Assert.False(WarmStandby.Validate(new MemoryStream(Zip(("race-rounds.json", "[]")))).Ok);

    [Fact]
    public void A_damaged_copy_is_refused_without_throwing() =>
        Assert.False(WarmStandby.Validate(new MemoryStream(Encoding.UTF8.GetBytes("not a zip at all"))).Ok);

    // --- The marker -------------------------------------------------------------

    [Fact]
    public void The_taken_over_marker_round_trips_and_clears()
    {
        Assert.Null(WarmStandby.ReadTakenOver(AppPaths.Root));

        var at = new DateTime(2026, 11, 11, 20, 42, 0, DateTimeKind.Utc);
        WarmStandby.WriteTakenOver(AppPaths.Root, new WarmStandby.TakenOver(at, "TIMING-PC", at.AddSeconds(-20)));

        var read = WarmStandby.ReadTakenOver(AppPaths.Root);
        Assert.NotNull(read);
        Assert.Equal(at, read!.AtUtc);
        Assert.Equal("TIMING-PC", read.From);

        WarmStandby.ClearTakenOver(AppPaths.Root);
        Assert.Null(WarmStandby.ReadTakenOver(AppPaths.Root));
    }

    [Fact]
    public void A_damaged_marker_still_counts_as_taken_over()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf(WarmStandby.TakenOverMarkerName), "{ not json");

        Assert.NotNull(WarmStandby.ReadTakenOver(AppPaths.Root));
    }

    // --- The words --------------------------------------------------------------

    [Fact]
    public void The_standby_line_says_how_fresh_the_copy_is()
    {
        var now = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var status = new WarmStandby.MirrorStatus(true, now.AddSeconds(-12), now, 20, "TIMING-PC", null, null);

        Assert.Equal("Standby PC: copy of TIMING-PC from 12 s ago.", WarmStandby.Line(status, now));
        Assert.Contains("BEHIND", WarmStandby.Line(status with { LastCopyUtc = now.AddMinutes(-3), LastError = "Can't reach the timing PC." }, now));
        Assert.Contains("waiting for the first copy", WarmStandby.Line(status with { LastCopyUtc = null }, now));
        Assert.Contains("took over", WarmStandby.Line(status with { TakenOverUtc = now }, now));
        Assert.Null(WarmStandby.Line(WarmStandby.MirrorStatus.Off, now));
    }

    [Fact]
    public void Take_over_says_how_old_the_copy_is_and_what_happens()
    {
        var now = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var question = WarmStandby.TakeOverQuestion(now.AddMinutes(-2), now, "TIMING-PC", 18);

        Assert.Contains("2 min old", question);
        Assert.Contains("18 files", question);
        Assert.Contains("safety backup", question);
        Assert.Contains("connect the decoder", question);
    }

    // --- 6.3: the offline line --------------------------------------------------

    [Fact]
    public void Offline_says_racing_carries_on_and_pages_will_catch_up()
    {
        var offline = new RelayStatus(true, true, false, DateTime.UtcNow.AddMinutes(-5), 3, 0, "Can't reach the relay.");

        var line = OfflineFirst.RelayLine(offline);

        Assert.NotNull(line);
        Assert.Contains("racing carries on", line);
        Assert.Contains(OfflineFirst.CatchUp, line);
        Assert.Contains("3 updates waiting", line);
    }

    [Fact]
    public void Nothing_is_said_when_online_off_not_set_up_or_not_yet_tried()
    {
        Assert.Null(OfflineFirst.RelayLine(null));
        Assert.Null(OfflineFirst.RelayLine(RelayStatus.Off));
        Assert.Null(OfflineFirst.RelayLine(new RelayStatus(true, false, false, null, 0, 0, "x")));
        Assert.Null(OfflineFirst.RelayLine(new RelayStatus(true, true, true, DateTime.UtcNow, 4, 0, null)));
        Assert.Null(OfflineFirst.RelayLine(new RelayStatus(true, true, false, null, 0, 0, null)));
    }

    [Fact]
    public void The_bar_line_joins_the_website_and_the_standby()
    {
        var now = DateTime.UtcNow;
        var offline = new RelayStatus(true, true, false, null, 0, 0, "down");
        var standby = new WarmStandby.MirrorStatus(true, now.AddSeconds(-5), now, 3, "TIMING-PC", null, null);

        var line = OfflineFirst.BarLine(offline, standby, now);

        Assert.StartsWith("Website offline", line);
        Assert.Contains(" · Standby PC: copy of TIMING-PC", line);
        Assert.Null(OfflineFirst.BarLine(RelayStatus.Off, WarmStandby.MirrorStatus.Off, now));
    }

    // --- The timing PC's copy and Take Over's restore ---------------------------

    [Fact]
    public void The_copy_holds_the_stores_and_nothing_of_the_machines()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"drivers\"]");
        File.WriteAllText(_data.PathOf("race-rounds.json"), "[]");
        File.WriteAllText(_data.PathOf("settings.json"), "{\"secret\":1}");
        File.WriteAllText(_data.PathOf("voice-settings.json"), "{}");
        File.WriteAllText(_data.PathOf("secrets.hgk"), "locked");
        Directory.CreateDirectory(_data.PathOf("Music"));
        File.WriteAllText(Path.Combine(_data.PathOf("Music"), "index.json"), "{}");

        using var buffer = new MemoryStream();
        var count = BackupService.WriteStandbySnapshot(buffer);

        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();

        Assert.Equal(2, count);
        Assert.Equal(new[] { "profiles.json", "race-rounds.json" }, names);
    }

    [Fact]
    public void Take_over_restores_the_copy_and_keeps_this_pcs_own_files()
    {
        // The standby, as it stands: its own settings, voice and soundscape,
        // an old driver list, and a store the timing PC doesn't have.
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("settings.json"), "{\"mine\":true}");
        File.WriteAllText(_data.PathOf("voice-settings.json"), "{\"voice\":\"mine\"}");
        File.WriteAllText(_data.PathOf(ThemeStore.ActiveFileName), Theme.BuiltInId("Arcade"));
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"old\"]");
        File.WriteAllText(_data.PathOf("garage.json"), "[\"standby only\"]");

        var copy = Path.Combine(_data.Folder, "copy.zip");
        File.WriteAllBytes(copy, Zip(("profiles.json", "[\"new\"]"), ("race-in-progress.json", "{\"race\":1}")));

        var result = BackupService.RestoreBackup(copy, Path.Combine(_data.Folder, "backups"), null, WarmStandby.MachineOwnFiles);

        Assert.Empty(result.Failed);
        Assert.Equal("[\"new\"]", File.ReadAllText(_data.PathOf("profiles.json")));
        Assert.Equal("{\"race\":1}", File.ReadAllText(_data.PathOf("race-in-progress.json")));
        Assert.Equal("{\"mine\":true}", File.ReadAllText(_data.PathOf("settings.json")));
        Assert.Equal("{\"voice\":\"mine\"}", File.ReadAllText(_data.PathOf("voice-settings.json")));
        Assert.True(File.Exists(_data.PathOf(ThemeStore.ActiveFileName)));

        // A club store the copy doesn't have is the timing PC's night, not
        // this PC's: set aside, as every restore does.
        Assert.False(File.Exists(_data.PathOf("garage.json")));
    }

    // --- The mirror -------------------------------------------------------------

    private sealed class FakeTimingPc : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int Asked { get; private set; }

        public string? PinSeen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            PinSeen = request.Headers.TryGetValues("X-Pin", out var pins) ? pins.First() : null;
            return Task.FromResult(Answer(request));
        }
    }

    private static HttpResponseMessage CopyOf(byte[] zip, DateTime? takenUtc = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        response.Headers.Add(WarmStandby.TakenAtHeader, (takenUtc ?? DateTime.UtcNow).ToString("O"));
        response.Headers.Add(WarmStandby.PrimaryNameHeader, "TIMING-PC");
        return response;
    }

    private StandbyMirror MirrorWith(FakeTimingPc timingPc, string pin = "12345678") =>
        new(() => new StandbyMirror.Target(new Uri("http://192.168.1.20:8080" + WarmStandby.SnapshotPath), pin, null),
            Mirror, AppPaths.Root, timingPc);

    [Fact]
    public async Task A_good_copy_is_kept_beside_the_data_never_in_it()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "[\"standby's own\"]");

        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[\"timing\"]"))) };
        using var mirror = MirrorWith(timingPc);

        Assert.True(await mirror.AskOnceAsync());

        Assert.Equal("12345678", timingPc.PinSeen);
        Assert.True(mirror.HasCopy);
        Assert.Equal("TIMING-PC", mirror.Status.Primary);
        Assert.Equal(1, mirror.Status.Files);
        Assert.Null(mirror.Status.LastError);

        // The live data folder is untouched until Take Over.
        Assert.Equal("[\"standby's own\"]", File.ReadAllText(_data.PathOf("profiles.json")));
    }

    [Fact]
    public async Task A_refused_or_bad_answer_keeps_the_last_good_copy()
    {
        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[1]"))) };
        using var mirror = MirrorWith(timingPc);

        Assert.True(await mirror.AskOnceAsync());
        var good = File.ReadAllBytes(mirror.LatestPath);

        timingPc.Answer = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.False(await mirror.AskOnceAsync());
        Assert.Contains("PIN", mirror.Status.LastError);

        timingPc.Answer = _ => CopyOf(Zip(("profiles.json", "[]"), ("../escape.json", "x")));
        Assert.False(await mirror.AskOnceAsync());

        timingPc.Answer = _ => CopyOf(Encoding.UTF8.GetBytes("garbage"));
        Assert.False(await mirror.AskOnceAsync());

        Assert.Equal(good, File.ReadAllBytes(mirror.LatestPath));
        Assert.False(File.Exists(mirror.LatestPath + ".partial"));
    }

    [Fact]
    public async Task The_previous_copy_is_kept_when_a_new_one_arrives()
    {
        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[1]"))) };
        using var mirror = MirrorWith(timingPc);

        await mirror.AskOnceAsync();
        var first = File.ReadAllBytes(mirror.LatestPath);

        timingPc.Answer = _ => CopyOf(Zip(("profiles.json", "[2]")));
        await mirror.AskOnceAsync();

        Assert.Equal(first, File.ReadAllBytes(mirror.PreviousPath));
        Assert.NotEqual(first, File.ReadAllBytes(mirror.LatestPath));
    }

    [Fact]
    public async Task Once_taken_over_nothing_is_asked_or_copied()
    {
        WarmStandby.WriteTakenOver(AppPaths.Root, new WarmStandby.TakenOver(DateTime.UtcNow, "TIMING-PC", null));

        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[1]"))) };
        using var mirror = MirrorWith(timingPc);

        mirror.Start();
        Assert.False(mirror.Status.Enabled);
        Assert.NotNull(mirror.Status.TakenOverUtc);

        Assert.False(await mirror.AskOnceAsync());
        Assert.Equal(0, timingPc.Asked);
        Assert.False(mirror.HasCopy);
    }

    [Fact]
    public async Task No_usable_pin_means_no_ask()
    {
        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[1]"))) };
        using var mirror = MirrorWith(timingPc, pin: "1234");

        Assert.False(await mirror.AskOnceAsync());
        Assert.Equal(0, timingPc.Asked);
        Assert.Contains("PIN", mirror.Status.LastError);
    }

    [Fact]
    public async Task A_copy_from_the_timing_pcs_future_is_not_shown_as_newer_than_now()
    {
        var timingPc = new FakeTimingPc { Answer = _ => CopyOf(Zip(("profiles.json", "[1]")), DateTime.UtcNow.AddHours(1)) };
        using var mirror = MirrorWith(timingPc);

        await mirror.AskOnceAsync();

        Assert.True(mirror.Status.LastCopyUtc <= DateTime.UtcNow);
    }
}
