namespace HeatCaller.Tests;

/// <summary>
/// Security review (22 Oct 2026): a driver's clip and podium video go to the
/// badge made from their MAIN transponder - the one the check-in roster
/// gives them (MainForm.RebuildBadgeIds) - even when the race timed them on
/// a second transponder of their own.
/// </summary>
public sealed class DriverExp_SecReview_BadgeTests : IDisposable
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SessionResultEntry Row(string transponder, int position) =>
        new() { Transponder = transponder, Position = position, DriverName = "Row " + transponder };

    [Fact]
    public void A_row_on_a_second_transponder_gets_the_main_transponders_badge()
    {
        var profiles = ProfileStore.Load();
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        Assert.True(profiles.TryAdd(maya, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));

        var ids = ClipShare.DriverIdsFor(new[] { Row("200", 1), Row("500", 2) }, profiles.Get, Secret);

        // Keyed by the row, so the clip for that row finds it; the badge is Maya's own.
        Assert.Equal(RelayCheckIn.IdFor("100", Secret), ids["200"]);
        Assert.NotEqual(RelayCheckIn.IdFor("200", Secret), ids["200"]);
        Assert.Equal(RelayCheckIn.IdFor("500", Secret), ids["500"]);
    }

    [Fact]
    public void The_main_transponder_still_gets_its_own_badge()
    {
        var profiles = ProfileStore.Load();
        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.Transponders.Add(new OwnedTransponder { Id = "200" });
        Assert.True(profiles.TryAdd(maya, out _));

        var ids = ClipShare.DriverIdsFor(new[] { Row("100", 1) }, profiles.Get, Secret);

        Assert.Equal(RelayCheckIn.IdFor("100", Secret), Assert.Single(ids).Value);
    }
}
