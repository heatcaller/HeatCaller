namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the warm standby: Take Over no longer parts the published
/// results' ledger from the window it is the exceptions to, and its
/// question no longer says the copy holds "0 files" when the count is
/// simply not known.
/// </summary>
public sealed class BugHunt17_WarmStandbyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Jan10 = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb07 = new(2026, 2, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar01 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The club's relay, switched off so nothing ever tries the network.</summary>
    private static AppSettings ClubRelay(DateTime? window) => new()
    {
        RelayEnabled = false,
        RelayBaseUrl = "https://relay.example",
        RelayClub = "acme-rc",
        RelayToken = string.Empty,
        RelayResultsFromUtc = window,
    };

    // ------------------------------------------- the window travels (33)

    [Fact]
    public void The_results_ledger_is_in_the_standbys_copy()
    {
        Assert.True(WarmStandby.IsMirroredFile("relay-results.json"));
        Assert.False(WarmStandby.IsMirroredFile("settings.json"));
    }

    [Fact]
    public void A_ledger_with_no_window_of_its_own_takes_the_settings_one()
    {
        var ledger = new RelayResultsIndex.Ledger();

        Assert.Equal(Jan10, ledger.KeepWindow(Jan10, out var changed));
        Assert.True(changed);
        Assert.Equal(Jan10, ledger.From);

        Assert.Null(new RelayResultsIndex.Ledger().KeepWindow(null, out changed));
        Assert.False(changed);
    }

    [Fact]
    public void A_ledgers_own_window_wins_over_the_settings_one()
    {
        var ledger = new RelayResultsIndex.Ledger { From = Jan10 };

        Assert.Equal(Jan10, ledger.KeepWindow(null, out var changed));
        Assert.False(changed);

        Assert.Equal(Jan10, ledger.KeepWindow(Mar01, out changed));
        Assert.False(changed);
        Assert.Equal(Jan10, ledger.From);
    }

    [Fact]
    public void The_window_is_saved_with_the_ledger_and_goes_when_the_ledger_is_cleared_or_moved()
    {
        new RelayResultsIndex.Ledger { From = Jan10 }.Save();

        var loaded = RelayResultsIndex.Ledger.Load();
        Assert.Equal(Jan10, loaded.From);

        Assert.True(loaded.Clear());
        Assert.Null(loaded.From);

        var moved = new RelayResultsIndex.Ledger { From = Jan10, Relay = "https://old.example", Club = "old" };
        moved.MoveTo("https://relay.example", "acme-rc");
        Assert.Null(moved.From);
    }

    [Fact]
    public void The_timing_pc_writes_its_window_into_the_ledger_at_startup()
    {
        using (new RelayPublisher(ClubRelay(Jan10)))
        {
        }

        Assert.Equal(Jan10, RelayResultsIndex.Ledger.Load().From);
    }

    [Fact]
    public void After_take_over_the_standby_lists_what_the_timing_pc_published()
    {
        // What Take Over leaves: the timing PC's ledger, and this PC's own
        // settings, which have never published and so have no window.
        new RelayResultsIndex.Ledger { From = Jan10 }.Save();

        var settings = ClubRelay(window: null);
        using var publisher = new RelayPublisher(settings);

        Assert.Equal(Jan10, settings.RelayResultsFromUtc);
        Assert.True(publisher.ResultIsOnRelay("feb07", Feb07));
        Assert.False(publisher.ResultIsOnRelay("before", Jan10.AddDays(-1)));
    }

    // ------------------------------------------- the question (158)

    [Fact]
    public void Take_over_leaves_the_count_out_when_it_is_not_known()
    {
        var now = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

        var unknown = WarmStandby.TakeOverQuestion(now.AddMinutes(-4), now, null, 0);
        Assert.Contains("The newest copy is 4 min old. Anything", unknown);
        Assert.DoesNotContain("0 files", unknown);

        var known = WarmStandby.TakeOverQuestion(now.AddMinutes(-4), now, "TIMING-PC", 18);
        Assert.Contains("4 min old (18 files).", known);
    }
}
