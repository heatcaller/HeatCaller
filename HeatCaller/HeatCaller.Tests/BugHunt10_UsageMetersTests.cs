namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a usage-meters.json holding the literal
/// "null" is read as unreadable, so the month's 80%/100% emails that may
/// already have gone are not sent again.
/// </summary>
public sealed class BugHunt10_UsageMetersTests : IDisposable
{
    private readonly TempAppData _data = new();

    private static readonly DateTime NovemberResets = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_file_holding_null_sends_no_email_that_may_have_gone_already()
    {
        File.WriteAllText(_data.PathOf("usage-meters.json"), "null");

        var logged = new List<string>();
        var meters = new UsageMeters(AppPaths.Root, () => new DateTime(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc));
        meters.Initialise(new AppSettings(), logged.Add);

        // 85% used: the 80% email would go if the record were trusted.
        meters.Report(MeterKind.Commentary, 383, 450, NovemberResets);

        Assert.Equal(383, meters.Get(MeterKind.Commentary)!.Used);
        Assert.True(meters.NoticesUnknown);
        Assert.Contains(logged, line => line.Contains("not emailed: the record of which emails have already gone", StringComparison.Ordinal));
    }
}
