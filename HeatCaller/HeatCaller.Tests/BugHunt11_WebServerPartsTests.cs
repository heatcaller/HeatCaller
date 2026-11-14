namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026): the web server's parts - the Wi-Fi phone pages.
///
/// A race deleted from Race History (or with its whole race day) left its
/// penalty lines on the follow card and its report-card lines on /coach until
/// the next race was filed: both are copies MainForm hands the web server,
/// rebuilt only when a race was filed or edited, and a delete told nobody.
/// The store now raises SessionDeleted, and MainForm rebuilds both from it.
/// </summary>
public sealed class BugHunt11_WebServerPartsTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Deleting_a_session_raises_SessionDeleted_after_it_has_left_the_history()
    {
        var history = SessionHistoryStore.Load();
        var race = new SessionRecord { Label = "Heat 1", StartedAtUtc = DateTime.UtcNow };
        Assert.True(history.Add(race));

        SessionRecord? heard = null;
        var stillListed = true;

        history.SessionDeleted += record =>
        {
            heard = record;
            stillListed = history.All.Any(s => s.Id == record.Id);
        };

        Assert.True(history.Delete(race.Id));

        Assert.Same(race, heard);
        Assert.False(stillListed, "A listener rebuilding the phone pages must read a history the race is already gone from.");
    }

    [Fact]
    public void Deleting_an_id_that_is_not_there_raises_nothing()
    {
        var history = SessionHistoryStore.Load();
        var raised = 0;
        history.SessionDeleted += _ => raised++;

        history.Delete("not-a-session");

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_listener_that_throws_does_not_come_back_out_through_Delete()
    {
        var history = SessionHistoryStore.Load();
        var race = new SessionRecord { Label = "Heat 1", StartedAtUtc = DateTime.UtcNow };
        Assert.True(history.Add(race));

        history.SessionDeleted += _ => throw new InvalidOperationException("boom");

        Assert.True(history.Delete(race.Id));
    }
}
