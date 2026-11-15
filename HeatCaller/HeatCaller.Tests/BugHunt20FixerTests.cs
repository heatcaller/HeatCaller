namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20 (15 Nov 2026): a lead-change line that already says "back
/// in front" is not followed by the same words again; a race with a club
/// rental in it is kept off public race video; a marshal no-show's time
/// penalty never lands on a practice round; and the curfew planner never
/// offers to run together two classes that share a driver.
/// </summary>
public sealed class BugHunt20FixerTests
{
    // ------------------------------------------------------------ lead-change tail

    [Fact]
    public void A_lead_change_line_saying_back_in_front_is_not_followed_by_the_same_words()
    {
        var change = new LeadChange("B", "A", 4, 3, 2);
        var line = $"Sam is {LeadTracker.BackInFront(change)}!";
        var tail = LeadTracker.Tail(change, pick: lines => lines.First(l => l.StartsWith("Back in front", StringComparison.Ordinal)));

        Assert.Equal(" Back in front for the second time.", tail);
        Assert.True(AnnouncementService.TailRepeatsLine(line, tail));

        // A tail that adds something new is still said.
        var count = LeadTracker.Tail(change, pick: lines => lines[0]);
        Assert.False(AnnouncementService.TailRepeatsLine(line, count));
        Assert.False(AnnouncementService.TailRepeatsLine("Sam takes the lead!", tail));

        Assert.Contains("!TailRepeatsLine(text, tail.Tail)", Source("AnnouncementService.VoiceQueue.cs"));
    }

    // ------------------------------------------------------------ rental juniors

    [Fact]
    public void A_race_with_a_club_rental_in_it_goes_on_the_junior_list()
    {
        var profiles = new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["R4"] = new() { Transponder = "R4", DriverName = "Rental 4", IsRental = true },
            ["J1"] = new() { Transponder = "J1", DriverName = "Jo", IsJunior = true },
            ["A1"] = new() { Transponder = "A1", DriverName = "Al" },
        };
        DriverProfile? Of(string t) => profiles.GetValueOrDefault(t);

        // The shared rental profile: nothing says the renter isn't a child.
        Assert.True(StreamTonight.HasJuniorOrRental(new[] { "A1", "R4" }, Of, _ => false));

        // A rental unit with no profile at all.
        Assert.True(StreamTonight.HasJuniorOrRental(new[] { "A1", "R9" }, Of, t => t == "R9"));

        Assert.True(StreamTonight.HasJuniorOrRental(new[] { "A1", "J1" }, Of, _ => false));
        Assert.False(StreamTonight.HasJuniorOrRental(new[] { "A1", "X" }, Of, _ => false));

        Assert.Contains("StreamTonight.HasJuniorOrRental(", Source("MainForm.Video.cs"));
    }

    // ------------------------------------------------------------ marshal penalty on practice

    [Fact]
    public void A_marshal_time_penalty_waits_past_a_practice_round_for_the_next_real_race()
    {
        var missed = new RoundDefinition
        {
            Id = "p1h2",
            ClassName = "Stock",
            RoundLabel = "Practice 1 Heat 2",
            Kind = RoundKind.Practice,
            NoShowMarshals = new List<MarshalNoShow>
            {
                MarshalRule.Log("Ann (Turn 1)", "A", "Stock", MarshalNoShowRule.TimePenalty, 10, new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc)),
            },
        };
        var practice = new RoundDefinition { Id = "p2h1", ClassName = "Stock", RoundLabel = "Practice 2 Heat 1", Kind = RoundKind.Practice };
        var qualifier = new RoundDefinition { Id = "q1h1", ClassName = "Stock", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        var rounds = new[] { missed, practice, qualifier };
        var results = new List<SessionResultEntry> { new() { Transponder = "A", DriverName = "Ann", Laps = 10, ElapsedMs = 300_000, Position = 1 } };

        Assert.Empty(MarshalRule.PendingPenalties(results, practice.Id, rounds, null));
        Assert.Null(missed.NoShowMarshals[0].AppliedToSessionId);

        Assert.Single(MarshalRule.PendingPenalties(results, qualifier.Id, rounds, null));
    }

    // ------------------------------------------------------------ curfew pairs

    [Fact]
    public void The_curfew_planner_never_pairs_two_classes_that_share_a_driver()
    {
        static RoundDefinition Heat(string className, string label, params string[] transponders) =>
            new() { ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier, AssignedTransponders = transponders.ToList() };

        // Jo races Stock and Mod; Buggy is free to share with either.
        var rounds = new List<RoundDefinition>
        {
            Heat("Stock", "Q1 Heat 1", "S1", "S2", "JO"),
            Heat("Mod", "Q1 Heat 1", "M1", "M2", "M3", "jo"),
            Heat("Buggy", "Q1 Heat 1", "B1", "B2", "B3", "B4"),
        };

        var (a, b, _) = Assert.Single(MixedHeats.ShareableClasses(rounds, 8));
        Assert.Equal("Stock", a);
        Assert.Equal("Buggy", b);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
