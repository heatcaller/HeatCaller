using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A second transponder raced as a car of its own (22 Oct 2026): its races
/// go on the driver's own night, under their main badge, in a section of
/// their own - see NightOwner, DriverNightExtras.Merge, NightCar and the
/// night email's per-car blocks.
/// </summary>
public sealed class DriverExp_SecondCar_NightTests
{
    private const string SamMain = "3012345";
    private const string SamSecond = "3012399";
    private const string Kim = "3099999";
    private const string Ghost = "GHOST-1";

    private static readonly DateTime Tonight = new(2026, 10, 22, 21, 0, 0, DateTimeKind.Local);
    private static readonly DateTime At = new(2026, 10, 22, 21, 30, 0, DateTimeKind.Utc);

    // Sam owns both; everybody else owns their own.
    private static string MainOf(string transponder) =>
        string.Equals(transponder, SamSecond, StringComparison.OrdinalIgnoreCase) ? SamMain : transponder;

    // --- Who a night belongs to --------------------------------------------

    [Fact]
    public void A_second_transponder_is_put_with_its_driver_and_their_main_car_leads()
    {
        var tonight = new[]
        {
            // The second car raced first tonight; the main car still leads.
            Race("Heat 1", Tonight.AddHours(-2), Row(SamSecond, 1, car: "RC10 B7"), Row(Kim, 2)),
            Race("Heat 2", Tonight.AddHours(-1), Row(SamMain, 2, car: "B6.4"), Row(Kim, 1), Row(Ghost, 3)),
        };

        var drivers = NightOwner.Group(new[] { SamSecond, Kim, SamMain, Ghost }, tonight, MainOf);

        Assert.Equal(new[] { SamMain, Kim }, drivers.Select(d => d.Main));

        var sam = drivers[0];
        Assert.True(sam.Split);
        Assert.Equal(new[] { SamMain, SamSecond }, sam.Cars.Select(c => c.Transponder));
        Assert.Equal(new[] { "c1", "c2" }, sam.Cars.Select(c => c.Id));
        Assert.Equal(new[] { "Main car", "Second car" }, sam.Cars.Select(c => c.Label));
        Assert.Equal(new[] { "B6.4", "RC10 B7" }, sam.Cars.Select(c => c.Name));

        var kim = drivers[1];
        Assert.False(kim.Split);
        Assert.Equal(Kim, Assert.Single(kim.Cars).Transponder);
    }

    [Fact]
    public void A_race_for_the_main_car_alone_still_brings_in_the_second_cars_earlier_races()
    {
        var tonight = new[]
        {
            Race("Heat 1", Tonight.AddHours(-2), Row(SamSecond, 1)),
            Race("Heat 2", Tonight.AddHours(-1), Row(SamMain, 1)),
        };

        // What PublishDriverNightsFor hands in for Heat 2: only its rows.
        var sam = Assert.Single(NightOwner.Group(new[] { SamMain }, tonight, MainOf));

        Assert.Equal(new[] { SamMain, SamSecond }, sam.Cars.Select(c => c.Transponder));
        Assert.Null(sam.Cars[1].Name); // no car filed with that result
    }

    [Fact]
    public void Only_the_second_car_raced_is_still_one_section_saying_which_car()
    {
        var tonight = new[] { Race("Heat 1", Tonight.AddHours(-1), Row(SamSecond, 1, car: "RC10 B7")) };

        var sam = Assert.Single(NightOwner.Group(new[] { SamSecond }, tonight, MainOf));

        Assert.Equal(SamMain, sam.Main);
        Assert.True(sam.Split);
        var car = Assert.Single(sam.Cars);
        Assert.Equal(("c1", "Second car", false), (car.Id, car.Label, car.Main));
    }

    [Fact]
    public void Demo_races_and_practice_do_not_count_as_a_car_raced()
    {
        var demo = Race("Demo", Tonight.AddHours(-3), Row(SamSecond, 1));
        demo.IsDemo = true;
        var practice = Race("Practice", Tonight.AddHours(-2), Row(SamSecond, 1));
        practice.SessionType = RaceSessionType.Practice;

        var sam = Assert.Single(NightOwner.Group(
            new[] { SamMain },
            new[] { demo, practice, Race("Heat 1", Tonight.AddHours(-1), Row(SamMain, 1)) },
            MainOf));

        Assert.False(sam.Split);
        Assert.Equal(SamMain, Assert.Single(sam.Cars).Transponder);
    }

    [Fact]
    public void WithFirst_moves_a_car_to_the_front_and_numbers_them_again()
    {
        var tonight = new[]
        {
            Race("Heat 1", Tonight.AddHours(-2), Row(SamMain, 1)),
            Race("Heat 2", Tonight.AddHours(-1), Row(SamSecond, 1)),
        };
        var sam = Assert.Single(NightOwner.Group(new[] { SamMain }, tonight, MainOf));

        var moved = NightOwner.WithFirst(sam, 1);

        Assert.Equal(new[] { SamSecond, SamMain }, moved.Cars.Select(c => c.Transponder));
        Assert.Equal(new[] { "c1", "c2" }, moved.Cars.Select(c => c.Id));
        Assert.Equal(new[] { "Second car", "Main car" }, moved.Cars.Select(c => c.Label));
        Assert.Same(sam, NightOwner.WithFirst(sam, 0));
    }

    // --- races[] ------------------------------------------------------------

    [Fact]
    public void Races_merge_in_running_order_and_only_the_other_cars_are_tagged()
    {
        var heat1 = Race("Heat 1", Tonight.AddHours(-3), Row(SamMain, 1));
        var heat2 = Race("Heat 2", Tonight.AddHours(-2), Row(SamSecond, 2));

        // Both cars in the same race: Sam has two rows in it.
        var heat3 = Race("Heat 3", Tonight.AddHours(-1), Row(SamMain, 1), Row(SamSecond, 3));
        var tonight = new[] { heat1, heat2, heat3 };

        var merged = DriverNightExtras.Merge(new[]
        {
            ("c1", DriverNightExtras.Races(SamMain, tonight, null)),
            ("c2", DriverNightExtras.Races(SamSecond, tonight, null)),
        });

        Assert.Equal(new[] { heat1.Id, heat2.Id, heat3.Id, heat3.Id }, merged.Select(r => r.Sid));
        Assert.Equal(new string?[] { null, "c2", null, "c2" }, merged.Select(r => r.Car));
        Assert.Equal(new[] { 1, 2, 1, 3 }, merged.Select(r => r.Pos));
    }

    // --- The document -------------------------------------------------------

    [Fact]
    public void A_night_of_two_cars_carries_each_car_apart_and_no_transponder()
    {
        var heat1 = Race("Heat 1", Tonight.AddHours(-2), Row(SamMain, 1));
        var heat2 = Race("Heat 2", Tonight.AddHours(-1), Row(SamSecond, 2));
        var tonight = new[] { heat1, heat2 };

        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Main car facts."), At)
        {
            Coach = new[] { "Main car card." },
            Races = DriverNightExtras.Merge(new[]
            {
                ("c1", DriverNightExtras.Races(SamMain, tonight, null)),
                ("c2", DriverNightExtras.Races(SamSecond, tonight, null)),
            }),
            Cars = new[]
            {
                new NightCar { Id = "c1", Label = "Main car", Name = "B6.4" },
                new NightCar
                {
                    Id = "c2", Label = "Second car", Name = " RC10 B7 ",
                    Lines = new[] { "Second car facts.", "  " },
                    Coach = Array.Empty<string>(),
                    Improve = "Your middle lap is 0.40 s quicker than in your first race tonight",
                },
            },
        }));

        // The first car's own parts stay where a page from before cars reads them.
        Assert.Equal("Main car facts.", night.GetProperty("lines")[0].GetString());
        Assert.Equal("Main car card.", night.GetProperty("coach")[0].GetString());

        var cars = night.GetProperty("cars");
        Assert.Equal(2, cars.GetArrayLength());
        Assert.Equal("c1", cars[0].GetProperty("id").GetString());
        Assert.False(cars[0].TryGetProperty("lines", out _));
        Assert.Equal("RC10 B7", cars[1].GetProperty("name").GetString());
        Assert.Equal(new[] { "Second car facts." }, cars[1].GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.False(cars[1].TryGetProperty("coach", out _)); // empty is left out
        Assert.False(cars[1].TryGetProperty("progress", out _));

        var races = night.GetProperty("races");
        Assert.False(races[0].TryGetProperty("car", out _));
        Assert.Equal("c2", races[1].GetProperty("car").GetString());

        var text = night.GetRawText();
        Assert.DoesNotContain(SamMain, text);
        Assert.DoesNotContain(SamSecond, text);
    }

    [Fact]
    public void Without_cars_there_is_no_cars_field_and_no_car_tags()
    {
        var tonight = new[] { Race("Heat 1", Tonight.AddHours(-1), Row(SamMain, 1)) };

        var night = Read(DriverNight.Document(new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = DriverNightExtras.Races(SamMain, tonight, null),
        }));

        Assert.False(night.TryGetProperty("cars", out _));
        Assert.False(night.GetProperty("races")[0].TryGetProperty("car", out _));
    }

    [Fact]
    public void Over_the_limit_the_charts_go_first_then_the_other_cars_facts_then_their_cards()
    {
        var laps = Enumerable.Repeat(12345678L, DriverNightExtras.MostLaps).ToArray();
        var big = Enumerable.Range(0, 60).Select(i => new NightRace
        {
            Sid = new string('a', 32), Label = "Heat", At = At.ToString("O"), Pos = 1, Of = 6, Laps = laps.Length,
            BestMs = 12345678, LapsMs = laps.ToList(), Car = i % 2 == 0 ? null : "c2",
        }).ToList();
        var longLines = Enumerable.Range(0, 1200).Select(i => $"A long line of facts about the second car, number {i}.").ToArray();

        var parts = new NightParts("Sam Parker", Tonight, null, Facts("Raced."), At)
        {
            Races = big,
            Cars = new[]
            {
                new NightCar { Id = "c1", Label = "Main car" },
                new NightCar { Id = "c2", Label = "Second car", Lines = longLines, Coach = new[] { "Card." } },
            },
        };

        var document = DriverNight.Document(parts);

        Assert.NotNull(document);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(document!) <= DriverNight.MostDocumentBytes);

        var night = Read(document);
        Assert.False(night.GetProperty("races")[0].TryGetProperty("lapsMs", out _));
        Assert.Equal(60, night.GetProperty("races").GetArrayLength());
        Assert.False(night.GetProperty("cars")[1].TryGetProperty("lines", out _));
        Assert.Equal("Card.", night.GetProperty("cars")[1].GetProperty("coach")[0].GetString());
    }

    // --- The email ----------------------------------------------------------

    [Fact]
    public void The_email_keeps_each_car_under_its_own_heading()
    {
        var body = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), null,
            new[] { "Main facts." }, new[] { "Main card." }, nightLink: null,
            cars: new[]
            {
                new NightEmail.EmailCar(NightEmail.CarHeading("Main car", "B6.4"), new[] { "Main facts." }, new[] { "Main card." }),
                new NightEmail.EmailCar(NightEmail.CarHeading("Second car", null), new[] { "Second facts." }, null),
            });

        Assert.Contains("YOUR RACES - Main car (B6.4)", body);
        Assert.Contains("YOUR REPORT CARD - Main car (B6.4)", body);
        Assert.Contains("YOUR RACES - Second car" + Environment.NewLine + "Second facts.", body);
        Assert.DoesNotContain("YOUR REPORT CARD - Second car", body);
        Assert.True(body.IndexOf("Main facts.", StringComparison.Ordinal) < body.IndexOf("Second facts.", StringComparison.Ordinal));
    }

    [Fact]
    public void An_email_of_one_car_is_what_it_always_was()
    {
        var plain = NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), "Club Night",
            new[] { "Facts." }, new[] { "Card." }, nightLink: null);

        Assert.Contains("YOUR RACES" + Environment.NewLine + "Facts.", plain);
        Assert.Contains("YOUR REPORT CARD" + Environment.NewLine + "Card.", plain);
        Assert.Equal(plain, NightEmail.Build("Sam", "Acme RC", new DateTime(2026, 10, 22), "Club Night",
            new[] { "Facts." }, new[] { "Card." }, nightLink: null, cars: Array.Empty<NightEmail.EmailCar>()));
    }

    // --- helpers --------------------------------------------------------------

    private static PackedFacts Facts(params string[] lines) =>
        new(string.Join('\n', lines), Array.Empty<string>(), 0);

    private static JsonElement Read(string? document)
    {
        Assert.NotNull(document);

        return JsonDocument.Parse(document!).RootElement;
    }

    private static SessionResultEntry Row(string who, int pos, string car = "") => new()
    {
        Transponder = who,
        DriverName = who == Kim ? "Kim Lee" : "Sam Parker",
        CarModel = car,
        Position = pos,
        Laps = 10,
        FastLapMs = 8500,
    };

    private static SessionRecord Race(string label, DateTime startLocal, params SessionResultEntry[] rows) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        Label = label,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = startLocal.ToUniversalTime(),
        EndedAtUtc = startLocal.ToUniversalTime().AddMinutes(5),
        Results = rows.ToList(),
    };
}
