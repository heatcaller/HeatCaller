namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the race-day manager's Today answer: the Next Steps card's
/// words went out through the relay as the desk wrote them, so a late
/// check-in nobody had named was sent as his whole transponder number
/// ("7654321 checked in after the heats were posted."), and so was an
/// unnamed car on the post-the-heats step's not-heard list.
/// </summary>
public sealed class BugHunt24_RaceDayOpsTodayTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Tail = "Each goes into the heat nearest their pace that has room, in every round still to run, and nobody else moves. Or say not tonight.";

    private static ProfileStore Profiles()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "7654321" }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4440001", DriverName = "Bea" }, out _));
        return profiles;
    }

    private static ProgramStep LateStep(string headline, string list) =>
        new(ProgramStage.LateArrivals, headline, $"{list}. {Tail}", ProgramCaptions.LateArrivals);

    [Fact]
    public void BugHunt24_LateArrivalStep_through_the_relay_shows_only_the_end_of_an_unnamed_cars_transponder()
    {
        var profiles = Profiles();
        var late = new List<LateArrivals.Arrival> { new("Stock", "7654321") };
        var step = LateStep("7654321 checked in after the heats were posted.", "7654321 (Stock)");

        var shown = RaceDayService.StepForPhone(step, RaceDayChannel.Relay, profiles, late, Array.Empty<string>());

        Assert.DoesNotContain("7654321", shown.Headline);
        Assert.DoesNotContain("7654321", shown.Detail);
        Assert.Equal("...321 checked in after the heats were posted.", shown.Headline);
        Assert.Equal($"...321 (Stock). {Tail}", shown.Detail);
        Assert.Equal(ProgramStage.LateArrivals, shown.Stage);
        Assert.Equal(ProgramCaptions.LateArrivals, shown.ActionCaption);
    }

    [Fact]
    public void BugHunt24_LateArrivalStep_with_several_drivers_masks_each_unnamed_car_and_keeps_names()
    {
        var profiles = Profiles();
        var late = new List<LateArrivals.Arrival> { new("Stock", "4440001"), new("Stock", "7654321") };
        var step = LateStep("2 drivers checked in after the heats were posted.", "Bea (Stock), 7654321 (Stock)");

        var shown = RaceDayService.StepForPhone(step, RaceDayChannel.Relay, profiles, late, Array.Empty<string>());

        Assert.Equal("2 drivers checked in after the heats were posted.", shown.Headline);
        Assert.Equal($"Bea (Stock), ...321 (Stock). {Tail}", shown.Detail);
    }

    [Fact]
    public void BugHunt24_LateArrivalStep_on_the_wifi_is_left_as_the_desk_wrote_it()
    {
        var profiles = Profiles();
        var late = new List<LateArrivals.Arrival> { new("Stock", "7654321") };
        var step = LateStep("7654321 checked in after the heats were posted.", "7654321 (Stock)");

        var shown = RaceDayService.StepForPhone(step, RaceDayChannel.WiFi, profiles, late, Array.Empty<string>());

        Assert.Same(step, shown);
    }

    [Fact]
    public void BugHunt24_LateArrivalStep_a_named_driver_reads_the_same_through_the_relay()
    {
        var profiles = Profiles();
        var late = new List<LateArrivals.Arrival> { new("Stock", "4440001") };
        var step = LateStep("Bea checked in after racing started.", "Bea (Stock)");

        var shown = RaceDayService.StepForPhone(step, RaceDayChannel.Relay, profiles, late, Array.Empty<string>());

        Assert.Equal(step.Headline, shown.Headline);
        Assert.Equal(step.Detail, shown.Detail);
    }

    [Fact]
    public void BugHunt24_PostHeatsStep_not_heard_list_through_the_relay_masks_an_unnamed_car()
    {
        var profiles = Profiles();
        var notHeard = new[] { "4440001", "7654321" };
        var deskWords = HeatPosting.NotHeardWords(new[] { "Bea", "7654321" });
        var step = new ProgramStep(
            ProgramStage.PostHeats,
            "Tonight's heats are built - post them next.",
            $"Posting prints the heat sheets. First up: Q1 Heat 1. {deskWords}",
            ProgramCaptions.PostHeats);

        var relay = RaceDayService.StepForPhone(step, RaceDayChannel.Relay, profiles, Array.Empty<LateArrivals.Arrival>(), notHeard);

        Assert.DoesNotContain("7654321", relay.Detail);
        Assert.Contains("Bea and ...321", relay.Detail);
        Assert.StartsWith("Posting prints the heat sheets. First up: Q1 Heat 1. ", relay.Detail);

        var wifi = RaceDayService.StepForPhone(step, RaceDayChannel.WiFi, profiles, Array.Empty<LateArrivals.Arrival>(), notHeard);

        Assert.Contains("7654321", wifi.Detail);
    }
}
