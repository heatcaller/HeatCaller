namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a figure kept past its plan's reset said it
/// was waiting for this month's "from the relay" whatever the meter - wrong
/// for the club's own ElevenLabs key, whose figure only ElevenLabs gives.
/// </summary>
public sealed class BugHunt24_UsageMetersTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static Meter ExpiredMeter(MeterKind kind)
    {
        var now = new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);
        var meters = new UsageMeters(AppPaths.Root, () => now);

        meters.Report(kind, 100, 1000, now.AddMinutes(1));
        now = now.AddMinutes(2);

        var meter = meters.Get(kind);
        Assert.NotNull(meter);
        Assert.False(meter!.Known);
        return meter;
    }

    [Fact]
    public void The_club_own_ElevenLabs_key_past_its_reset_waits_on_ElevenLabs_not_the_relay()
    {
        var meter = ExpiredMeter(MeterKind.ElevenLabsKey);

        Assert.Contains("ElevenLabs", meter.Note);
        Assert.DoesNotContain("relay", meter.Note);
    }

    [Fact]
    public void Relay_meters_past_their_reset_still_wait_on_the_relay()
    {
        Assert.Contains("from the relay", ExpiredMeter(MeterKind.Commentary).Note);
        Assert.Contains("from the relay", ExpiredMeter(MeterKind.HeatCallerVoice).Note);
    }
}
