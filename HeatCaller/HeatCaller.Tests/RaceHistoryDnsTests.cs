using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>Race History lists who did not start (12 Nov 2026, setup plan step 8 - see SkipDns).</summary>
public sealed class RaceHistoryDnsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_race_s_results_end_with_its_DNS_drivers_greyed_and_with_no_report_card()
    {
        var history = SessionHistoryStore.Load();
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "200", DriverName = "Max Power" });

        var start = new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc);
        history.Add(new SessionRecord
        {
            Id = "s1",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = start,
            EndedAtUtc = start.AddMinutes(5),
            Results = { new SessionResultEntry { Position = 1, Transponder = "100", DriverName = "Sam Price", Laps = 20 } },
            DidNotStart = new List<string> { "200", "300" },
        });

        var rows = StaThread.Run(() =>
        {
            using var control = new RaceHistoryControl(history, new UndoService(), new AppSettings(), profiles: profiles);
            _ = control.Handle;

            var list = (ListView)typeof(RaceHistoryControl).GetField("_sessionList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
            list.Items[0].Selected = true;
            typeof(RaceHistoryControl).GetMethod("ShowDetail", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, null);

            var grid = (DataGridView)typeof(RaceHistoryControl).GetField("_detailGrid", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;

            return grid.Rows.Cast<DataGridViewRow>()
                .Select(r => (Pos: r.Cells["pos"].FormattedValue as string, Driver: r.Cells["driver"].Value as string, Tag: r.Tag, Muted: r.DefaultCellStyle.ForeColor == AppTheme.TextMuted))
                .ToList();
        }, TimeSpan.FromSeconds(60), () => "Race History with a DNS");

        Assert.Equal(3, rows.Count);
        Assert.Equal(("1", "Sam Price"), (rows[0].Pos, rows[0].Driver));
        Assert.Equal((RaceHistoryControl.DnsMark, "Max Power"), (rows[1].Pos, rows[1].Driver));
        Assert.Equal((RaceHistoryControl.DnsMark, "300"), (rows[2].Pos, rows[2].Driver));
        Assert.All(rows.Skip(1), r => Assert.Null(r.Tag));
        Assert.All(rows.Skip(1), r => Assert.True(r.Muted));
        Assert.False(rows[0].Muted);
    }
}
