namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (19 Nov 2026), usage totals: unticking the box must forget
/// what was counted even when HeatCaller closes before the next 15-second
/// tick, and a restore's frozen saving must not be undone by the closing
/// window writing the old totals back.
/// </summary>
public sealed class BugHunt22_UsageTotalsTests : IDisposable
{
    private static readonly AppUsage.Context Evening = new(RaceDay: false, DeskView: false);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hc-usage22-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, AppUsage.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private AppUsage New() => new(FilePath, () => [], "0.63.0");

    [Fact]
    public void Unticking_then_closing_before_the_next_tick_leaves_nothing_on_disk()
    {
        var settings = new AppSettings { ShareUsageStats = true };

        using (var tracker = new AppUsageTracker(New(), settings, () => Evening))
        {
            tracker.Count("warmup_flag");
            tracker.Save();
            Assert.NotEmpty(New().Days);

            tracker.Count("warmup_flag");
            settings.ShareUsageStats = false;
            tracker.Save(); // closing, before any tick ran
        }

        Assert.Empty(New().Days);
    }

    [Fact]
    public void Opening_with_the_box_unticked_forgets_totals_left_on_disk()
    {
        var earlier = New();
        earlier.Count("warmup_flag", DateTime.Now);
        earlier.Save(DateTime.Now);
        Assert.NotEmpty(New().Days);

        using (new AppUsageTracker(New(), new AppSettings { ShareUsageStats = false }, () => Evening))
        {
        }

        Assert.Empty(New().Days);
    }

    [Fact]
    public void Opening_with_the_box_ticked_keeps_the_totals()
    {
        var earlier = New();
        earlier.Count("warmup_flag", DateTime.Now);
        earlier.Save(DateTime.Now);

        using (new AppUsageTracker(New(), new AppSettings { ShareUsageStats = true }, () => Evening))
        {
        }

        Assert.NotEmpty(New().Days);
    }

    [Fact]
    public void A_save_while_saving_is_frozen_for_a_restore_leaves_the_restored_file_alone()
    {
        Directory.CreateDirectory(_folder);
        const string restored = "{\"days\":[]}";
        File.WriteAllText(FilePath, restored);

        var usage = new AppUsage(FilePath, () => [], "0.63.0") { SavingFrozen = () => true };
        usage.Count("warmup_flag", DateTime.Now);
        usage.Save(DateTime.Now);

        Assert.Equal(restored, File.ReadAllText(FilePath));
    }
}
