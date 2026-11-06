using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), relay app and auth: racer PINs and phone garage
/// writes over a profiles.json that couldn't be read at launch, sign-up desk
/// steps whose save failed, and setup-sheet values the phone sent back.
/// </summary>
public sealed class BugHunt11_RelayAppAuthTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A profile store loaded from a profiles.json that is there but can't be read - as when a sync tool holds it.</summary>
    private ProfileStore UnreadableProfiles()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("profiles.json"), "null");
        var profiles = ProfileStore.Load();
        Assert.True(profiles.LoadedUnreadable);
        return profiles;
    }

    // ------------------------------------------------------- racer PINs

    private static AppSettings PinSettings() => new()
    {
        RelayRacerPinsEnabled = true,
        RacerPinRotation = PinRotation.Daily,
        RacerPinsIssuedUtc = DateTime.UtcNow.AddDays(-10),
        RacerPins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["100"] = "482913", ["500"] = "730194" },
        RacerPinsEmailed = new List<string> { "100" },
    };

    [Fact]
    public void Every_racer_keeps_their_PIN_when_profiles_could_not_be_read()
    {
        var profiles = UnreadableProfiles();
        var settings = PinSettings();
        var issued = settings.RacerPinsIssuedUtc;

        Assert.False(RacerPins.EnsurePins(settings, profiles));
        Assert.False(RacerPins.RotateIfDue(settings, profiles, DateTime.UtcNow));

        Assert.Equal("482913", settings.RacerPins["100"]);
        Assert.Equal("730194", settings.RacerPins["500"]);
        Assert.Equal(new[] { "100" }, settings.RacerPinsEmailed);
        Assert.Equal(issued, settings.RacerPinsIssuedUtc);
    }

    [Fact]
    public void New_PINs_now_is_refused_when_profiles_could_not_be_read()
    {
        var profiles = UnreadableProfiles();
        var settings = PinSettings();

        Assert.Throws<InvalidOperationException>(() => RacerPins.RegenerateAll(settings, profiles));
        Assert.Equal(2, settings.RacerPins.Count);
        Assert.Equal("482913", settings.RacerPins["100"]);
    }

    // ------------------------------------------------- garage from phone

    private const string SamBadge = "0123456789abcdef0123";
    private const string PackId = "0123456789abcdef0123456789abcdef";
    private const string CarId = "33333333333333333333333333333333";

    private sealed class Garage
    {
        private int _next;

        public Garage(ProfileStore profiles)
        {
            Profiles = profiles;
            Desk = new DriverRequestDesk(Store, new AppSettings(), Profiles, Packs, History, Undo, _ => { },
                badge => badge == SamBadge ? "100" : null)
            {
                Setups = Setups,
                Cars = Cars,
                ChassisTypes = Chassis,
                Sheets = Sheets,
                TrackName = () => "Club Oval",
                CarPhotos = new PhotoStore("CarPhotos"),
            };
        }

        public ProfileStore Profiles { get; }
        public DriverRequestStore Store { get; } = DriverRequestStore.Load();
        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();
        public SetupLogStore Setups { get; } = SetupLogStore.Load();
        public CarStore Cars { get; } = CarStore.Load();
        public ChassisTypeStore Chassis { get; } = ChassisTypeStore.Load();
        public ChassisTemplateStore Sheets { get; } = ChassisTemplateStore.Load();
        public UndoService Undo { get; } = new();
        public SessionHistoryStore History { get; } = SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>());
        public DriverRequestDesk Desk { get; }

        public DriverRequest Ask(string kind, Dictionary<string, string> texts)
        {
            var ask = new DriverRequest
            {
                Id = (++_next).ToString("x24"),
                Badge = SamBadge,
                Kind = kind,
                AskedUtc = DateTime.UtcNow,
                Proof = DriverAsk.ByPin,
            };

            foreach (var (k, v) in texts)
            {
                ask.Texts[k] = v;
            }

            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            Desk.FileWaitingWrites();
            return Store.Get(ask.Id)!;
        }
    }

    [Fact]
    public void A_phone_garage_change_waits_rather_than_being_declined_when_profiles_could_not_be_read()
    {
        var g = new Garage(UnreadableProfiles());

        var ask = g.Ask(DriverAsk.PackEdit, new() { ["op"] = "add", ["pack"] = PackId, ["label"] = "Blue 2", ["chem"] = "LiPo" });

        Assert.Equal(DriverAsk.Waiting, ask.State);
        Assert.Null(g.Packs.Get(PackId));
    }

    // ---------------------------------------------------- setup values

    private static Garage WithSheet(out ChassisTemplate sheet)
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", ClassName = "Stock" }, out _));
        var g = new Garage(profiles);

        sheet = new ChassisTemplate { Name = "Club sheet" };
        sheet.Sections.Add(new SetupSection
        {
            Name = "Drive",
            Fields =
            {
                new SetupField { Id = "pinion", Label = "Pinion", Kind = SetupFieldKind.Number, Unit = "T" },
                new SetupField { Id = "ride", Label = "Ride height", Kind = SetupFieldKind.Number, Unit = "mm" },
                new SetupField { Id = "spring", Label = "Front spring", Kind = SetupFieldKind.Choice, Options = { "Soft", "Hard" } },
            },
        });
        g.Sheets.Upsert(sheet);

        var chassis = new ChassisType { Name = "MR-03", SetupSheetId = sheet.Id };
        g.Chassis.Upsert(chassis);
        g.Cars.Upsert(new Car { Id = CarId, Name = "Mine", OwnerTransponder = "100", ChassisTypeId = chassis.Id });
        return g;
    }

    [Fact]
    public void A_number_typed_with_its_unit_at_the_desk_survives_the_phone_sending_it_back()
    {
        var g = WithSheet(out var sheet);

        // As the phone sends it: every value the sheet started from, pinion
        // "23T" as the desk typed it, with only the spring changed.
        var ask = g.Ask(DriverAsk.Setup, new()
        {
            ["car"] = CarId, ["sheet"] = sheet.Id, ["v.pinion"] = "23T", ["v.ride"] = "4 mm", ["v.spring"] = "Hard",
        });

        Assert.Equal(DriverAsk.Approved, ask.State);
        var entry = Assert.Single(g.Setups.All);
        Assert.Equal("23T", entry.Values["pinion"]);
        Assert.Equal("4 mm", entry.Values["ride"]);
        Assert.Equal("Hard", entry.Values["spring"]);
    }

    [Fact]
    public void A_fix_from_the_phone_never_erases_a_value_it_could_not_take()
    {
        var g = WithSheet(out var sheet);
        var desk = new SetupEntry
        {
            DriverName = "Sam Parker",
            CarId = CarId,
            CarModel = "Mine",
            ChassisTemplateId = sheet.Id,
            RecordedAtUtc = DateTime.UtcNow,
            Values = new Dictionary<string, string> { ["pinion"] = "~23", ["spring"] = "Soft" },
        };
        g.Setups.Upsert(desk);

        var ask = g.Ask(DriverAsk.Setup, new()
        {
            ["car"] = CarId, ["sheet"] = sheet.Id, ["entry"] = desk.Id, ["v.pinion"] = "~23", ["v.spring"] = "Hard",
        });

        Assert.Equal(DriverAsk.Approved, ask.State);
        var fixedEntry = Assert.Single(g.Setups.All);
        Assert.Equal("Hard", fixedEntry.Values["spring"]);
        Assert.Equal("~23", fixedEntry.Values["pinion"]);
    }

    // ------------------------------------------------------ sign-up desk

    private static AppSettings SignupSettings() => new()
    {
        RelayClub = "acme-rc",
        RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
    };

    /// <summary>Runs <paramref name="test"/> with every swap into signups.json failing, as when a sync tool holds it.</summary>
    private static void WithSignupsHeld(Action test)
    {
        var real = SafeFile.ReplaceFile;
        SafeFile.ReplaceFile = (temp, path) =>
        {
            if (string.Equals(Path.GetFileName(path), "signups.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            real(temp, path);
        };

        try
        {
            test();
        }
        finally
        {
            SafeFile.ReplaceFile = real;
        }
    }

    private static int AnswersQueued(RelayPublisher publisher) =>
        ((Dictionary<string, string>)typeof(RelayPublisher)
            .GetField("_signupAnswers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(publisher)!).Count;

    private static RelaySignupDesk Desk(AppSettings settings, RelayPublisher publisher) =>
        new(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

    [Fact]
    public void A_turn_down_that_cannot_be_saved_is_put_back_and_nothing_is_sent()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);
        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        desk.Collected(new[] { request });
        Assert.Single(desk.Waiting);
        var onDisk = File.ReadAllText(_data.PathOf("signups.json"));

        var rejected = true;
        WithSignupsHeld(() => rejected = desk.Reject(request, "Wrong class"));

        Assert.False(rejected);
        Assert.Equal(SignupInviteState.Open, invite.State);
        Assert.Null(invite.AnsweredUtc);
        Assert.Single(desk.Waiting);
        Assert.Equal(0, AnswersQueued(publisher));
        Assert.Equal(onDisk, File.ReadAllText(_data.PathOf("signups.json")));

        // Once the file lets go, the same turn-down goes through.
        Assert.True(desk.Reject(request, "Wrong class"));
        Assert.Equal(SignupInviteState.Rejected, invite.State);
        Assert.Equal(1, AnswersQueued(publisher));
    }

    [Fact]
    public void A_cancel_that_cannot_be_saved_leaves_the_invite_open_and_sends_nothing()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);
        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        desk.Collected(new[] { request });

        var cancelled = true;
        WithSignupsHeld(() => cancelled = desk.Cancel(invite));

        Assert.False(cancelled);
        Assert.Equal(SignupInviteState.Open, invite.State);
        Assert.Null(invite.AnsweredUtc);
        Assert.Single(desk.Waiting);
        Assert.Equal(0, AnswersQueued(publisher));
    }

    [Fact]
    public void An_undo_whose_new_code_cannot_be_saved_keeps_the_old_code_and_reports_it_failed()
    {
        var settings = SignupSettings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = Desk(settings, publisher);
        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        Assert.True(desk.Reject(request, "Wrong class"));
        var code = invite.Code;
        var hash = invite.Hash;

        WithSignupsHeld(() => Assert.Throws<InvalidOperationException>(() => desk.UndoReject(invite.Id, "Sam")));

        Assert.Equal(SignupInviteState.Rejected, invite.State);
        Assert.Equal(code, invite.Code);
        Assert.Equal(hash, invite.Hash);
        Assert.Equal("Wrong class", invite.AnswerMessage);
    }
}
