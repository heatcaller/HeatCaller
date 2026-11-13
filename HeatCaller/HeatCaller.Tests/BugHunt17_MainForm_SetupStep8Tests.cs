namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the marshal rule. A no-show by a driver who had
/// not qualified yet and was not of the marshalled heat's class was filed
/// under that heat's class, where they had nothing to lose. And undoing
/// "Take off the marshal no-show" pushed a new undo step from inside the
/// undo, which the next undo then took straight back off.
/// </summary>
public sealed class BugHunt17_MainForm_SetupStep8Tests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Round(int order, string className, string label, RoundKind kind, params string[] drivers) => new()
    {
        Order = order,
        ClassName = className,
        RoundLabel = label,
        Kind = kind,
        AssignedTransponders = drivers.ToList(),
    };

    [Fact]
    public void A_cover_from_another_class_is_filed_under_the_class_they_qualify_in_tonight()
    {
        var stockQ1 = Round(1, "Stock", "Q1 Heat 1", RoundKind.Qualifier, "S1", "S2");
        var modQ1 = Round(2, "Mod", "Q1 Heat 1", RoundKind.Qualifier, "M1", "M2");

        // M1 covers a post on the Stock heat, before Mod's first qualifier.
        Assert.Equal("Mod", MainForm.NoShowClassOf(stockQ1, "M1", new[] { stockQ1, modQ1 }, null, _profiles));
    }

    [Fact]
    public void With_no_qualifier_tonight_their_profile_decides()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "M1", DriverName = "Alex", ClassName = "Mod" });
        var stockQ1 = Round(1, "Stock", "Q1 Heat 1", RoundKind.Qualifier, "S1");

        Assert.Equal("Mod", MainForm.NoShowClassOf(stockQ1, "M1", new[] { stockQ1 }, null, _profiles));
        Assert.Equal("Stock", MainForm.NoShowClassOf(stockQ1, "X", new[] { stockQ1 }, null, _profiles)); // nobody known: the round's
    }

    [Fact]
    public void A_driver_of_the_rounds_class_is_still_filed_under_it()
    {
        // Races Mod as well, and Mod qualifies first - but they marshal a Stock heat as a Stock driver.
        _profiles.Upsert(new DriverProfile { Transponder = "P", DriverName = "Pat", ClassName = "Mod", Classes = new() { "Stock" } });

        var practice = Round(0, "Stock", "Practice", RoundKind.Practice, "P");
        var stockQ1H1 = Round(1, "Stock", "Q1 Heat 1", RoundKind.Qualifier, "S1");
        var modQ1 = Round(2, "Mod", "Q1 Heat 1", RoundKind.Qualifier, "P");
        var stockQ1H2 = Round(3, "Stock", "Q1 Heat 2", RoundKind.Qualifier, "P");

        Assert.Equal("Stock", MainForm.NoShowClassOf(stockQ1H1, "P", new[] { practice, stockQ1H1, modQ1, stockQ1H2 }, null, _profiles));
        Assert.Equal("Stock", MainForm.NoShowClassOf(stockQ1H1, "P", new[] { practice, stockQ1H1 }, null, _profiles));
    }

    [Fact]
    public void The_heat_they_last_raced_before_it_still_comes_first()
    {
        var modQ1 = Round(1, "Mod", "Q1 Heat 1", RoundKind.Qualifier, "M1");
        var stockQ1 = Round(2, "Stock", "Q1 Heat 1", RoundKind.Qualifier, "S1");
        var stockQ2 = Round(3, "Stock", "Q2 Heat 1", RoundKind.Qualifier, "M1"); // later, and not tonight's scope below

        Assert.Equal("Mod", MainForm.NoShowClassOf(stockQ1, "M1", new[] { modQ1, stockQ1, stockQ2 }, null, _profiles));

        // A qualifier outside tonight's rounds is not looked at.
        var tonight = new HashSet<string> { stockQ1.Id, stockQ2.Id };
        Assert.Equal("Stock", MainForm.NoShowClassOf(stockQ1, "M1", new[] { modQ1, stockQ1, stockQ2 }, tonight, _profiles));
    }

    [Fact]
    public void Undoing_the_take_back_logs_the_no_show_again_without_a_new_undo_step()
    {
        var source = Source("MainForm.SetupStep8.cs");

        Assert.Contains("LogMarshalNoShow(round, who, transponder, withUndo: false);", source);

        // The one capture in LogMarshalNoShow is behind withUndo.
        var capture = source.IndexOf("_undo.Capture($\"Marshal no-show: {who}\"", StringComparison.Ordinal);
        var guard = source.LastIndexOf("if (withUndo)", capture, StringComparison.Ordinal);
        Assert.True(capture > 0 && guard > 0 && capture - guard < 200);
    }

    /// <summary>The app source, for checks on how the pieces are wired in.</summary>
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
