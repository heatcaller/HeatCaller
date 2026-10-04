using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug-hunt fixes that can be pinned without a window: publishing switched
/// off again in a rehearsal copy that is carried on from.
/// </summary>
public sealed class BugHuntMiscTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Real => Path.Combine(_data.Folder, "real");

    private string Copy => Path.Combine(_data.Folder, "rehearsal", "Data");

    private string CopySettings => Path.Combine(Copy, "settings.json");

    [Fact]
    public void A_later_rehearsal_switches_publishing_off_again_in_its_kept_copy()
    {
        Directory.CreateDirectory(Real);
        File.WriteAllText(Path.Combine(Real, "settings.json"), """{ "RelayEnabled": true, "RelayClub": "acme-rc" }""");
        Assert.True(Rehearsal.Prepare(Real, Copy));

        // The first rehearsal turned publishing on to rehearse online check-in.
        var turnedOn = JsonNode.Parse(File.ReadAllText(CopySettings))!.AsObject();
        turnedOn["RelayEnabled"] = true;
        turnedOn["RacerPinsEmailAutomatically"] = true;
        File.WriteAllText(CopySettings, turnedOn.ToJsonString());

        Assert.False(Rehearsal.Prepare(Real, Copy));

        var copied = JsonNode.Parse(File.ReadAllText(CopySettings))!.AsObject();
        Assert.False(copied["RelayEnabled"]!.GetValue<bool>());
        Assert.False(copied["RacerPinsEmailAutomatically"]!.GetValue<bool>());
        Assert.Equal("acme-rc", copied["RelayClub"]!.GetValue<string>());
    }

    [Fact]
    public void A_kept_copy_with_publishing_already_off_is_not_written_again()
    {
        Directory.CreateDirectory(Copy);
        const string text = """{ "RelayEnabled": false, "LastHeatSize": 6 }""";
        File.WriteAllText(CopySettings, text);

        Assert.False(Rehearsal.Prepare(Real, Copy));

        // Byte for byte as it was: not re-serialised.
        Assert.Equal(text, File.ReadAllText(CopySettings));
    }

    [Fact]
    public void A_kept_copy_whose_settings_cannot_be_read_stops_the_rehearsal()
    {
        Directory.CreateDirectory(Copy);
        File.WriteAllText(CopySettings, "{ not json");

        Assert.ThrowsAny<Exception>(() => Rehearsal.Prepare(Real, Copy));
    }
}
