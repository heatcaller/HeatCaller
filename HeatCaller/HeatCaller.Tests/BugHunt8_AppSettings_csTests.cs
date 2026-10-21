using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8 (20 Oct 2026), AppSettings.cs #39: a relay address left as
/// null in settings.json threw inside Load, which then took the whole file
/// for unreadable and started the app on defaults.
/// </summary>
public sealed class BugHunt8_AppSettings_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData("RelayBaseUrl")]
    [InlineData("RelayLastGoodBaseUrl")]
    [InlineData("VoiceRelayBaseUrl")]
    public void A_null_relay_address_keeps_every_other_setting(string name)
    {
        Assert.True(new AppSettings { RaceWarmUpSeconds = 45, HasCompletedFirstRun = true }.Save());

        var path = _data.PathOf("settings.json");
        var json = File.ReadAllText(path);
        var edited = Regex.Replace(json, "\"" + name + "\"\\s*:\\s*\"[^\"]*\"", "\"" + name + "\": null");
        Assert.NotEqual(json, edited);
        File.WriteAllText(path, edited);

        var settings = AppSettings.Load();

        Assert.False(settings.LoadedUnreadable);
        Assert.Equal(45, settings.RaceWarmUpSeconds);
        Assert.True(settings.HasCompletedFirstRun);
        Assert.Equal(string.Empty, settings.RelayBaseUrl);
        Assert.Equal(string.Empty, settings.RelayLastGoodBaseUrl);
        Assert.Equal(string.Empty, settings.VoiceRelayBaseUrl);
    }

    [Fact]
    public void Moving_a_null_address_off_the_old_relay_reads_as_blank()
    {
        Assert.Equal(string.Empty, AppSettings.MovedOffLegacyRelay(null));
    }
}
