namespace HeatCaller.Tests;

/// <summary>
/// Setting up the night, step 3 (12 Nov 2026, setup plan D4 and D7): Auto
/// start waits for Marshals in place, and tonight's classes run in the race
/// day's order.
/// </summary>
public sealed class NightSetupMarshalsAndOrderTests : IDisposable
{
    private static readonly DateTime Flag = new(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<string> Nobody = Array.Empty<string>();

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DateTime At(int seconds) => Flag.AddSeconds(seconds);

    private static AutoStartClock Opened()
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));
        return clock;
    }

    // --- D4: marshals in place -------------------------------------------------

    [Fact]
    public void A_club_that_waits_for_marshals_holds_at_zero_until_they_are_in_place()
    {
        var clock = Opened();

        Assert.Equal(AutoStartState.Counting, clock.Step(At(100), false, Nobody, waitForMarshals: true));
        Assert.Equal(AutoStartState.WaitingForMarshals, clock.Step(At(150), false, Nobody, waitForMarshals: true));
        Assert.Equal(AutoStartState.WaitingForMarshals, clock.Step(At(200), false, Nobody, waitForMarshals: true));

        clock.ConfirmMarshals();

        Assert.True(clock.MarshalsConfirmed);
        Assert.Equal(AutoStartState.Start, clock.Step(At(201), false, Nobody, waitForMarshals: true));
    }

    [Fact]
    public void Marshals_confirmed_before_zero_never_stop_the_start()
    {
        var clock = Opened();

        clock.ConfirmMarshals();

        Assert.Equal(AutoStartState.Start, clock.Step(At(150), false, Nobody, waitForMarshals: true));
    }

    [Fact]
    public void Without_the_wait_the_start_goes_as_it_always_did()
    {
        Assert.Equal(AutoStartState.Start, Opened().Step(At(150), false, Nobody));
    }

    [Fact]
    public void Start_now_does_not_wait_for_the_marshals()
    {
        var clock = Opened();

        clock.StartNow();

        Assert.Equal(AutoStartState.Start, clock.Step(At(10), false, Nobody, waitForMarshals: true));
    }

    [Fact]
    public void Missing_cars_are_asked_about_before_the_marshals()
    {
        var clock = Opened();
        var frank = new[] { "Frank" };

        Assert.Equal(AutoStartState.WaitingForCars, clock.Step(At(150), false, frank, waitForMarshals: true));

        clock.ApproveMissingCars();

        Assert.Equal(AutoStartState.WaitingForMarshals, clock.Step(At(151), false, frank, waitForMarshals: true));
    }

    [Fact]
    public void A_new_gap_asks_for_the_marshals_again()
    {
        var clock = Opened();
        clock.ConfirmMarshals();

        clock.Open("8", "heat3", At(400), TimeSpan.FromSeconds(150));

        Assert.False(clock.MarshalsConfirmed);
    }

    [Fact]
    public void The_marshal_words_name_each_post()
    {
        var marshals = new List<(string Name, string Post)> { ("Hugh Turn", "Turn 1"), ("Rhoda Rage", "Turn 2") };

        Assert.Equal("Marshals for Q2 Heat 3: Hugh Turn (Turn 1), Rhoda Rage (Turn 2). In place?",
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: false));
        Assert.Equal("Q2 Heat 3 is waiting for its marshals: Hugh Turn (Turn 1), Rhoda Rage (Turn 2).",
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: true));
        Assert.Equal("Marshals for Q2 Heat 3, to your posts please: Hugh Turn on Turn 1 and Rhoda Rage on Turn 2. We start when you're in place.",
            MarshalsInPlace.Call("Q2 Heat 3", marshals));
        Assert.Equal("Axel Grease", MarshalsInPlace.Who("Axel Grease", ""));
        Assert.Equal("NEXT RACE SOON", AutoStartClock.Band(AutoStartState.WaitingForMarshals, TimeSpan.Zero));
    }

    // --- D7: class running order ------------------------------------------------

    [Fact]
    public void Tonight_s_order_puts_the_named_classes_first_and_keeps_the_rest_in_page_order()
    {
        var classes = new[] { "Stock", "Mod", "Truck", "Juniors" };

        Assert.Equal(new[] { "Juniors", "Stock", "Mod", "Truck" },
            ClassRunningOrder.Arrange(classes, n => n, new[] { "Juniors" }, rotate: false, round: 0));

        Assert.Equal(classes, ClassRunningOrder.Arrange(classes, n => n, null, rotate: false, round: 2));
    }

    [Fact]
    public void Turned_round_each_round_the_next_class_leads_off()
    {
        var classes = new[] { "Stock", "Mod", "Truck" };

        Assert.Equal(new[] { "Stock", "Mod", "Truck" }, ClassRunningOrder.Arrange(classes, n => n, null, rotate: true, round: 0));
        Assert.Equal(new[] { "Mod", "Truck", "Stock" }, ClassRunningOrder.Arrange(classes, n => n, null, rotate: true, round: 1));
        Assert.Equal(new[] { "Truck", "Stock", "Mod" }, ClassRunningOrder.Arrange(classes, n => n, null, rotate: true, round: 2));
        Assert.Equal(new[] { "Stock", "Mod", "Truck" }, ClassRunningOrder.Arrange(classes, n => n, null, rotate: true, round: 3));
    }

    [Fact]
    public void The_summary_says_the_order_plainly()
    {
        var tonight = new[] { "Stock", "Mod" };

        Assert.Equal("As on the Classes page", ClassRunningOrder.Summary(tonight, null, rotate: false));
        Assert.Equal("Mod, Stock", ClassRunningOrder.Summary(tonight, new[] { "Mod" }, rotate: false));
        Assert.Equal("Stock, Mod (turned round each round)", ClassRunningOrder.Summary(tonight, null, rotate: true));
    }

    [Fact]
    public void A_new_day_keeps_last_week_s_class_order()
    {
        var store = RaceEventStore.Load();
        var lastWeek = EventSpine.StartEvent(store, "Last week");
        lastWeek.ClassRunningOrder = new List<string> { "Juniors", "Stock" };
        lastWeek.RotateClassOrder = true;
        store.Upsert(lastWeek);

        var tonight = EventSpine.StartEvent(store, "Tonight");

        Assert.Equal(new[] { "Juniors", "Stock" }, tonight.ClassRunningOrder);
        Assert.True(tonight.RotateClassOrder);
    }
}
