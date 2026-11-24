namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Start Tonight window's class list.
/// </summary>
public sealed class BugHunt24_NewRaceDayFormTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_last_day_whose_classes_are_all_gone_opens_with_every_class_ticked() => OnUiThread(() =>
    {
        using var form = new NewRaceDayForm(new[] { "Buggy", "Truck" }, new[] { "Stock", "Mod" }, RaceDayShape.RaceDay);

        Assert.Equal(new[] { "Buggy", "Truck" }, form.ClassNames);
    });

    [Fact]
    public void A_last_day_with_one_class_still_on_file_ticks_only_that_class() => OnUiThread(() =>
    {
        using var form = new NewRaceDayForm(new[] { "Buggy", "Truck" }, new[] { "Stock", "Buggy" }, RaceDayShape.RaceDay);

        Assert.Equal(new[] { "Buggy" }, form.ClassNames);
    });

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24 Start Tonight classes");
}
