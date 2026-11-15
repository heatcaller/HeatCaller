using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 (15 Nov 2026), the race video / stream overlay slice:
/// juniors' names in the championship headline and Tonight's bests (#2),
/// most improved across classes (#18), the sponsor logo during a read when
/// the sponsor has a web name (#19), the corner bug's ghost leader (#20)
/// and Undo after a Rename in the overlay editor (#21).
/// </summary>
public sealed class BugHunt21_StreamOverlayTests
{
    private static WebServerService Server(ProfileStore profiles, RaceControlSession? race = null) =>
        new(new RaceTiming(race ?? new RaceControlSession()), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = car, DeviceTimeMs = raceTimeMs, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });

    [Fact]
    public void A_junior_named_only_in_the_championship_headline_or_tonights_bests_is_shortened()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "21001", DriverName = "Annabel Quickfoot", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "21002", DriverName = "Benedict Slowcoach", IsJunior = true });
        profiles.Upsert(new DriverProfile { Transponder = "21003", DriverName = "Terry Grownupson" });
        profiles.Upsert(new DriverProfile { Transponder = "21004", DriverName = "Cleo Notmentioned", IsJunior = true });

        // Only Terry is racing: neither junior is on the board or in a race to come.
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        Cross(race, "21003", 10_000);

        var server = Server(profiles, race);
        server.IfItEndedNow = () => new IfItEndedNow("Winter Series",
            new[] { new LiveChampionRow(1, "Terry Grownupson", 40, 1, false, true) },
            "Terry Grownupson would take the Winter Series lead from Benedict Slowcoach");
        server.Moments.Post(new BroadcastMoment("milestone", "First podium", "Annabel Quickfoot · On the Box"));

        var names = JsonDocument.Parse(server.BuildStateJson()).RootElement.GetProperty("streamNames");

        Assert.Equal("Annabel Q", names.GetProperty("Annabel Quickfoot").GetString());
        Assert.Equal("Benedict S", names.GetProperty("Benedict Slowcoach").GetString());
        Assert.False(names.TryGetProperty("Terry Grownupson", out _));

        // Nobody's name goes out who is not on the stream.
        Assert.False(names.TryGetProperty("Cleo Notmentioned", out _));
    }

    [Fact]
    public void The_championship_headline_goes_through_the_junior_rule_on_the_page()
    {
        var page = Server(ProfileStore.Load()).BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        Assert.Contains("esc(snText(d.champ.headline))", page);
        Assert.DoesNotContain("esc(d.champ.headline)", page);
    }

    [Fact]
    public void The_corner_bug_names_the_first_real_car_not_a_ghost()
    {
        var page = Server(ProfileStore.Load()).BuildBroadcastPage(OverlayLayouts.BuiltIn("corner"));

        Assert.Contains("const leader = rows.find(s => !s.ghost) || null;", page);
        Assert.DoesNotContain("const leader = rows.length ? rows[0] : null;", page);
    }

    private static readonly DateTime Seven = new(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string roundId, int minutesIn, params (string Tx, long BestMs, string? Class)[] drivers) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Oval",
        Label = roundId,
        RoundId = roundId,
        StartedAtUtc = Seven.AddMinutes(minutesIn),
        Results = drivers.Select((d, i) => new SessionResultEntry { Position = i + 1, Transponder = d.Tx, FastLapMs = d.BestMs, ClassName = d.Class }).ToList(),
    };

    [Fact]
    public void Changing_to_a_faster_class_on_one_transponder_is_not_most_improved()
    {
        var rounds = new Dictionary<string, RoundDefinition>
        {
            ["stock-q1"] = new() { Id = "stock-q1", ClassName = "Stock" },
            ["stock-q2"] = new() { Id = "stock-q2", ClassName = "Stock" },
            ["mod-q1"] = new() { Id = "mod-q1", ClassName = "Mod" },
        };

        var pick = MostImprovedTonight.From(new[]
        {
            Race("stock-q1", 0, ("dot", 10_200, null), ("sam", 10_000, null)),
            Race("stock-q2", 20, ("sam", 9_600, null)),
            Race("mod-q1", 40, ("dot", 9_400, null)),
        }, id => rounds.TryGetValue(id, out var r) ? r : null);

        // Dot's Mod lap is not compared with her Stock lap: Sam's 4% wins.
        Assert.NotNull(pick);
        Assert.Equal("sam", pick!.Transponder);
        Assert.Equal(400, pick.GainMs);
    }

    [Fact]
    public void In_a_shared_heat_each_entrys_own_class_is_the_one_compared()
    {
        var pick = MostImprovedTonight.From(new[]
        {
            Race("heat-1", 0, ("dot", 10_200, "Stock")),
            Race("heat-2", 20, ("dot", 9_400, "Mod")),
        }, _ => null);

        Assert.Null(pick);
    }

    [Fact]
    public void A_sponsor_with_a_web_name_gets_their_logo_while_their_read_is_on_air()
    {
        var logos = new[]
        {
            new SponsorStream.Logo("rival", "Rival Hobbies", "/sponsor-logo/sponsor-logo-aaaaaaaaaaaa.png", 1, "Rival Hobbies"),
            new SponsorStream.Logo("smith", "Smith & Co.", "/sponsor-logo/sponsor-logo-bbbbbbbbbbbb.png", 1, "Smith and co"),
        };

        // Every slot of a round of turns: always Smith while Smith's read is on.
        for (var s = 0; s < 4; s++)
        {
            var now = DateTime.UnixEpoch.AddSeconds(SponsorStream.TurnSeconds * s);
            Assert.Equal("smith", SponsorStream.Up(logos, now, "Smith and co")!.Id);
            Assert.Equal("smith", SponsorStream.Up(logos, now, "Smith & Co.")!.Id);
        }
    }

    [Fact]
    public void Undo_after_a_rename_keeps_the_new_name()
    {
        var path = Path.Combine(Path.GetTempPath(), "hc-bh21-overlays-" + Guid.NewGuid().ToString("N")[..8] + ".json");

        try
        {
            var store = OverlayStore.Load(path);
            var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "My stream");
            var x = mine.Elements[0].X;

            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, mine.Id);
            var edit = typeof(OverlayEditorForm).GetMethod("Edit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var undo = typeof(OverlayEditorForm).GetMethod("UndoLast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Move a graphic, then rename the layout (RenameLayout's save), then Undo.
            edit.Invoke(form, new object[] { (Action<OverlayLayout>)(l => l.Elements[0].X = x + 10) });
            Assert.Equal(x + 10, store.Get(mine.Id)!.Elements[0].X);

            var renamed = store.Get(mine.Id)!;
            renamed.Name = "Friday stream";
            store.Update(renamed);

            undo.Invoke(form, null);

            var after = store.Get(mine.Id)!;
            Assert.Equal(x, after.Elements[0].X);
            Assert.Equal("Friday stream", after.Name);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // A test machine's temp folder.
            }
        }
    }
}
