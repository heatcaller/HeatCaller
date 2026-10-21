using HeatCaller;
using Xunit;

namespace HeatCaller.Tests;

public class BugHunt8_GhostModels_csTests
{
    private static GhostSettings Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<GhostSettings>(json)!;

    [Fact]
    public void Normalize_NullGhostsList_BecomesEmpty()
    {
        var s = Parse("""{"Enabled":true,"Ghosts":null}""");
        GhostStore.Normalize(s);
        Assert.NotNull(s.Ghosts);
        Assert.Empty(s.Ghosts);
    }

    [Fact]
    public void Normalize_DropsNullEntries_AndFillsNullStrings()
    {
        var s = Parse("""{"Ghosts":[null,{"Id":null,"Name":null,"Transponder":null,"SourceTransponder":null,"CarModel":null},null]}""");
        GhostStore.Normalize(s);
        var g = Assert.Single(s.Ghosts);
        Assert.False(string.IsNullOrEmpty(g.Id));
        Assert.Equal("Ghost", g.Name);
        Assert.StartsWith(Ghosts.Prefix, g.Transponder);
        Assert.Equal(string.Empty, g.SourceTransponder);
        Assert.Equal(string.Empty, g.CarModel);
    }

    [Fact]
    public void Normalize_LeavesGoodGhostAlone()
    {
        var s = new GhostSettings();
        s.Ghosts.Add(new GhostCar { Id = "abc", Name = "Phantom", Transponder = Ghosts.Prefix + "XYZ" });
        GhostStore.Normalize(s);
        var g = Assert.Single(s.Ghosts);
        Assert.Equal("abc", g.Id);
        Assert.Equal("Phantom", g.Name);
        Assert.Equal(Ghosts.Prefix + "XYZ", g.Transponder);
    }
}
