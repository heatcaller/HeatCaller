namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8, group FJ (17 Oct 2026): a ghost crossing queued before the
/// ghosts restarted (Practice Clear Stats) landing in the new run on the old
/// clock (A28-2), two filling parts on a FillPage shared out equally rather
/// than by their leasts (A33-1), RoundedRect throwing for a sliver under 1px
/// (A33-2), and a caption-sized tab row recreated on every text-size step
/// (A33-4).
/// </summary>
public sealed class BugHunt8FJTests : IDisposable
{
    private const string Ghost = "GHOST-AAAAAA";

    private static volatile string _now = "starting";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A28-2 ---

    /// <summary>A UI thread stand-in: holds what is posted to it until RunAll.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int Count
        {
            get { lock (_posted) { return _posted.Count; } }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted)
            {
                _posted.Add((d, state));
            }
        }

        public void RunAll()
        {
            List<(SendOrPostCallback Callback, object? State)> items;

            lock (_posted)
            {
                items = _posted.ToList();
                _posted.Clear();
            }

            foreach (var (callback, state) in items)
            {
                callback(state);
            }
        }
    }

    [Fact]
    public void A_crossing_queued_before_the_ghosts_restart_is_not_delivered_into_the_new_run()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            var pace = new GhostCar { Name = "Pace", Transponder = Ghost, FixedLapMs = 1000 };
            var ghosts = new GhostService(race, ProfileStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()))
            {
                Settings = new GhostSettings { Enabled = true, Ghosts = { pace } },
            };

            ghosts.Start();

            // Its first crossing, handed to the UI thread and not yet run.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (context.Count == 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            Assert.True(context.Count > 0);

            // The restart Clear Stats makes, with the same ghost, then the
            // queued pass - which belongs to the run before.
            ghosts.Stop();
            ghosts.Start();
            context.RunAll();
            ghosts.Stop();

            Assert.DoesNotContain(race.GetOrderedStandings(), s => s.TransponderId == Ghost && s.Laps > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // --- A33-2 ---

    [Fact]
    public void A_rounded_rectangle_under_a_pixel_across_is_a_plain_rectangle()
    {
        using var path = AppTheme.RoundedRect(new RectangleF(0, 0, 0.7f, 5), 0);

        Assert.True(path.PointCount > 0);
    }

    [Fact]
    public void A_rounded_rectangle_with_no_width_is_an_empty_path()
    {
        using var path = AppTheme.RoundedRect(new RectangleF(0, 0, -1, 5), 4);

        Assert.Equal(0, path.PointCount);
    }

    // --- A33-1 ---

    [Fact]
    public void Two_filling_parts_each_get_their_least_at_the_pages_least_height()
    {
        var problems = StaThread.Run(CheckTwoFills, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "FillPage did not give each filling part its least:\n" + string.Join("\n", problems));
    }

    private static List<string> CheckTwoFills()
    {
        _now = "a fill page with two filling parts";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, 900, 1200) };

            window.Controls.Add(page);

            var fill = new FillPage();
            page.Controls.Add(fill);

            var small = fill.AddFill(new Panel(), 100);
            var large = fill.AddFill(new Panel(), 400, gapBelow: 0);

            window.Show();
            StaThread.Settle();

            page.Height = fill.LeastHeight(page.Width);
            StaThread.Settle();

            _now = $"a fill page with two filling parts, 900x{page.Height}";

            if (small.Height < 100 - 1)
            {
                problems.Add($"{_now}: the 100px part is {small.Height}px");
            }

            if (large.Height < 400 - 1)
            {
                problems.Add($"{_now}: the 400px part is {large.Height}px - shared out equally, not by their leasts");
            }
        }
        finally
        {
            Application.ThreadException -= Threw;
        }

        return problems;
    }

    // --- A33-4 ---

    [Fact]
    public void A_text_size_step_does_not_recreate_a_caption_sized_tab_row()
    {
        var problems = StaThread.Run(CheckTabsKeepTheirHandle, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckTabsKeepTheirHandle()
    {
        _now = "a caption-sized tab row";

        var problems = new List<string>();

        try
        {
            using var window = new QuietWindow();
            var tabs = new TabControl { Bounds = new Rectangle(0, 0, 600, 300) };

            tabs.TabPages.Add("Voice");
            tabs.TabPages.Add("Soundscapes");
            window.Controls.Add(tabs);

            AppTheme.SizeTabsToCaptions(tabs);

            window.Show();
            StaThread.Settle();

            var handle = tabs.Handle;

            AppTheme.SetFontScale(2.0f);
            AppTheme.StyleTabControl(tabs);
            StaThread.Settle();

            if (tabs.Handle != handle)
            {
                problems.Add($"{_now}: the tab row's window was recreated by a text-size step");
            }
        }
        finally
        {
            AppTheme.SetFontScale(1f);
        }

        return problems;
    }

    /// <summary>A window nobody sees: off-screen, transparent, never activated, not on the taskbar.</summary>
    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Size = new Size(200, 200);
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
