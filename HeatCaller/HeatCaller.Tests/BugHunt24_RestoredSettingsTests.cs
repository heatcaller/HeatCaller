namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F287: a restored backup could point the warm
/// standby at a new address on the track's network, and this laptop's own
/// sealed Standby PIN went there from the first tick. The standby's address
/// is now held like the relay's and the mail server's.
/// </summary>
public sealed class BugHunt24_RestoredSettingsTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    private const string ClubTimingPc = "192.168.1.20";
    private const string OtherDevice = "192.168.1.66";

    public void Dispose() => _data.Dispose();

    [Fact]
    public void BugHunt24_A_standby_address_this_laptop_wasn_t_using_is_held()
    {
        var parts = RestoredSettings.WhatNeedsConfirming(
            Standby(ClubTimingPc, mode: true), Standby(OtherDevice, mode: true));

        Assert.Equal(RestoredSettings.Part.Standby, parts);
    }

    [Fact]
    public void BugHunt24_A_standby_turned_on_by_the_backup_with_a_new_address_is_held_even_on_a_timing_PC()
    {
        Assert.Equal(RestoredSettings.Part.Standby,
            RestoredSettings.WhatNeedsConfirming(Standby(string.Empty, mode: false), Standby(OtherDevice, mode: true)));

        Assert.Equal(RestoredSettings.Part.Standby,
            RestoredSettings.WhatNeedsConfirming(null, Standby(OtherDevice, mode: true)));
    }

    [Fact]
    public void BugHunt24_The_same_standby_address_written_differently_is_not_new()
    {
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Standby(ClubTimingPc, mode: true), Standby("http://192.168.1.20:8080/", mode: true)));

        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Standby("TIMING-PC", mode: true), Standby("timing-pc", mode: true)));
    }

    [Fact]
    public void BugHunt24_A_standby_address_that_would_send_nothing_holds_nothing()
    {
        // Standby mode off: nothing is copied until somebody ticks it on the card.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Standby(ClubTimingPc, mode: true), Standby(OtherDevice, mode: false)));

        // Blank, or off the track's network: the copier refuses it before any request.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Standby(ClubTimingPc, mode: true), Standby(string.Empty, mode: true)));

        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Standby(ClubTimingPc, mode: true), Standby("standby.evil.invalid", mode: true)));
    }

    [Fact]
    public void BugHunt24_A_held_standby_is_kept_in_the_record_and_named_at_startup()
    {
        RestoredSettings.Hold(RestoredSettings.Part.Standby);

        Assert.True(File.Exists(_data.PathOf(RestoredSettings.FileName)));
        Assert.Contains("Standby", File.ReadAllText(_data.PathOf(RestoredSettings.FileName)), StringComparison.Ordinal);
        Assert.True(RestoredSettings.IsHeld(RestoredSettings.Part.Standby));
        Assert.Equal(RestoredSettings.StandbyMessage, RestoredSettings.StandbyHeldProblem());
        Assert.Contains("Timing PC address", RestoredSettings.StartupNote(), StringComparison.Ordinal);

        RestoredSettings.Confirm(RestoredSettings.Part.Standby);

        Assert.False(RestoredSettings.IsHeld(RestoredSettings.Part.Standby));
        Assert.Null(RestoredSettings.StandbyHeldProblem());
        Assert.False(File.Exists(_data.PathOf(RestoredSettings.FileName)));
    }

    [Fact]
    public void BugHunt24_A_standby_held_in_the_record_file_reads_back_as_held()
    {
        File.WriteAllText(_data.PathOf(RestoredSettings.FileName),
            "{\"RestoredUtc\":\"2026-11-23T00:00:00Z\",\"Held\":[\"Standby\"]}");

        Assert.Equal(RestoredSettings.Part.Standby, RestoredSettings.Held);
    }

    private static string Standby(string address, bool mode) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [nameof(AppSettings.StandbyPrimaryAddress)] = address,
            [nameof(AppSettings.StandbyModeEnabled)] = mode,
        });
}
