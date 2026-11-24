namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, drivers' own requests (23 Nov 2026).
///
/// WHY. With driver-requests.json held by a backup or sync tool at start-up,
/// requests from phones were still taken into memory, applied and answered,
/// though none of it could be saved - and after the restart the app asks for
/// they all came back from the relay and were applied a second time. Now they
/// are left on the relay until the file can be read.
/// </summary>
public sealed class BugHunt24_DriverRequestsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";

    private static readonly DateTime Tonight = new(2026, 11, 20, 19, 0, 0, DateTimeKind.Utc);

    private static string IdOf(int n) => n.ToString("x6") + new string('0', 18);

    private static DriverRequest Ask(int n, string kind, DateTime atUtc) =>
        new() { Id = IdOf(n), Badge = SamBadge, Kind = kind, AskedUtc = atUtc, Proof = DriverAsk.ByPin };

    [Fact]
    public void Requests_are_left_on_the_relay_while_the_requests_file_was_unreadable_and_filed_once_after_the_restart()
    {
        // A file on disk to lock.
        var first = DriverRequestStore.Load();
        first.FileAll(new[] { Ask(1, DriverAsk.Pack, Tonight) }, Tonight, out var filedFirst);
        Assert.Equal(1, filedFirst);

        DriverRequestStore tonight;
        using (new FileStream(_data.PathOf("driver-requests.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = DriverRequestStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);

        var confirm = tonight.FileAll(new[] { Ask(2, DriverAsk.Setup, Tonight.AddMinutes(5)) }, Tonight.AddMinutes(5), out var filed);

        // Not confirmed, and not taken in - so nothing can apply or answer it.
        Assert.Empty(confirm);
        Assert.Equal(0, filed);
        Assert.Empty(tonight.All);
        Assert.Empty(tonight.Waiting);
        Assert.Null(tonight.Get(IdOf(2)));

        // After the restart the relay offers it again: filed once, as new.
        var restarted = DriverRequestStore.Load();
        Assert.False(restarted.LoadedUnreadable);

        var again = restarted.FileAll(new[] { Ask(2, DriverAsk.Setup, Tonight.AddMinutes(5)) }, Tonight.AddMinutes(30), out var filedAgain);

        Assert.Equal(1, filedAgain);
        Assert.Contains(IdOf(2), again);
        Assert.True(restarted.IsWaiting(IdOf(2)));
        Assert.NotNull(restarted.Get(IdOf(1)));
    }
}
