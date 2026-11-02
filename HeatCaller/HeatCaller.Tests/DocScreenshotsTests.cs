using System.Drawing.Imaging;

namespace HeatCaller.Tests;

/// <summary>One-off: user guide pictures from a copy of the club's own data. Not committed.</summary>
public sealed class DocScreenshotsTests
{
    private static readonly string Out = Environment.GetEnvironmentVariable("HEATCALLER_DOC_SHOTS") ?? "";
    private static readonly string Data = Environment.GetEnvironmentVariable("HEATCALLER_DOC_DATA") ?? "";
    private static readonly string Only = Environment.GetEnvironmentVariable("HEATCALLER_DOC_ONLY") ?? "";

    private static readonly (string File, string Page, string? Tab)[] Shots =
    {
        ("race-control-idle", "Race Control", null),
        ("race-day-next-steps", "Race Day", "Next Steps"),
        ("race-day-setup", "Race Day", "Setup"),
        ("race-day-whos-racing", "Race Day", "Who's Racing"),
        ("championship-standings", "Championship", "Standings"),
        ("championship-calendar", "Championship", "Calendar"),
        ("driver-hub", "Driver Hub", null),
        ("classes", "Classes", null),
        ("rentals", "Rentals", null),
        ("handicaps", "Handicaps", null),
        ("track-layouts", "Track Layouts", null),
        ("race-history", "Race History", null),
        ("race-analytics-overview", "Race Analytics", "Overview"),
        ("achievements", "Achievements", null),
        ("garage", "Garage", null),
        ("practice", "Practice", null),
        ("ghost-cars", "Ghost Cars", null),
        ("demo-mode", "Demo Mode", null),
        ("venues", "Venues", null),
        ("themes", "Themes", null),
        ("sponsors", "Sponsors", null),
        ("local-screens", "Local Screens", null),
        ("club-website", "Club Website", null),
        ("voice-voice", "Voice", "Voice"),
        ("voice-cast", "Voice", "Cast"),
        ("voice-race-calls", "Voice", "Race Calls"),
        ("voice-ai-commentary", "Voice", "AI Commentary"),
        ("sounds-mixer", "Sounds", "Mixer"),
        ("sounds-soundscapes", "Sounds", "Soundscapes"),
        ("sounds-effects", "Sounds", "Effects"),
        ("sounds-ambience", "Sounds", "Ambience"),
        ("connection", "Connection", null),
        ("backup-restore", "Backup & Restore", null),
    };

    [Fact]
    public void Guide_pictures()
    {
        if (Out.Length == 0 || Data.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(Out);

        StaThread.Run(() =>
        {
            AppPaths.RedirectForTests(Data);
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.ClientSize = new Size(1280, 800);
                window.Show();
                StaThread.Settle();

                foreach (var (file, page, tab) in Shots)
                {
                    if (Only.Length > 0 && !Only.Split(';').Contains(file))
                    {
                        continue;
                    }

                    Show(window, page, tab);
                    Save(window, file);
                }

                if (Only.Length == 0 || Only.Contains("race-control-racing"))
                {
                    // A demo race from Demo Mode, then Race Control part-way in.
                    Show(window, "Demo Mode", null);
                    var demo = PageOf(window, "Demo Mode");
                    var drivers = All(demo).OfType<CheckedListBox>().First();

                    for (var i = 0; i < Math.Min(7, drivers.Items.Count); i++)
                    {
                        drivers.SetItemChecked(i, true);
                    }

                    All(demo).OfType<Button>().First(b => b.Text == "Start Demo Race").PerformClick();
                    Pump(TimeSpan.FromSeconds(90));
                    Save(window, "demo-after");
                    {
                        var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                        var demoControl = All(demo).First(c => c.GetType().Name == "DemoModeControl");
                        var sim = demoControl.GetType().GetField("_simulator", bf)!.GetValue(demoControl)!;
                        var t = sim.GetType();
                        var timer = (System.Windows.Forms.Timer)t.GetField("_tickTimer", bf)!.GetValue(sim)!;
                        var race = t.GetField("_race", bf)!.GetValue(sim)!;
                        File.WriteAllText(Path.Combine(Out, "debug.txt"), $"timer={timer.Enabled} dev={t.GetField("_deviceTimeMs", bf)!.GetValue(sim)} running={t.GetProperty("IsRunning")!.GetValue(sim)} owns={t.GetMethod("OwnsSession", bf)!.Invoke(sim, null)} state={race.GetType().GetProperty("State")!.GetValue(race)} demo={race.GetType().GetProperty("IsDemoMode")!.GetValue(race)} gen={race.GetType().GetProperty("SessionGeneration")!.GetValue(race)} owned={t.GetField("_ownedGeneration", bf)!.GetValue(sim)} ctx={SynchronizationContext.Current?.GetType().Name}");
                    }
                    Show(window, "Race Control", null);
                    Pump(TimeSpan.FromSeconds(1));
                    Save(window, "race-control-racing");

                    // The windows Race Control opens, during the race.
                    var control = PageOf(window, "Race Control");

                    foreach (var (caption, file) in new[] { ("Big Screen", "big-screen-racing"), ("Announcer Screen", "announcer-screen"), ("Mission Control", "mission-control") })
                    {
                        var before = Application.OpenForms.Cast<Form>().ToHashSet();
                        All(control).OfType<Button>().First(b => b.Text == caption && b.Visible).PerformClick();
                        Pump(TimeSpan.FromSeconds(3));

                        foreach (var opened in Application.OpenForms.Cast<Form>().Where(f => !before.Contains(f)).ToList())
                        {
                            opened.WindowState = FormWindowState.Normal;
                            opened.Location = new Point(-20000, -20000);

                            if (caption == "Big Screen")
                            {
                                opened.ClientSize = new Size(1280, 720);
                            }

                            Pump(TimeSpan.FromSeconds(2));
                            using var bitmap = new Bitmap(opened.Width, opened.Height);
                            opened.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(Out, file + ".png"), ImageFormat.Png);

                            if (caption != "Big Screen")
                            {
                                opened.Close();
                            }
                        }
                    }
                }
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }

            return 0;
        }, TimeSpan.FromMinutes(10), () => "guide pictures");
    }

    private static Control PageOf(MainForm window, string name) =>
        window.LayoutCheckPages.First(p => p.Name == name).Page;

    private static void Show(MainForm window, string page, string? tab)
    {
        typeof(MainForm).GetMethod("GoToPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, new object[] { page });
        StaThread.Settle();

        if (tab is not null)
        {
            foreach (var tabs in All(PageOf(window, page)).OfType<TabControl>())
            {
                var wanted = tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text == tab);

                if (wanted is not null)
                {
                    tabs.SelectedTab = wanted;
                    break;
                }
            }
        }

        Pump(TimeSpan.FromMilliseconds(600));
        StaThread.Settle();
    }

    private static void Pump(TimeSpan time)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (waited.Elapsed < time)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Save(MainForm window, string file)
    {
        window.WindowState = FormWindowState.Normal;
        window.ClientSize = new Size(1280, 800);
        StaThread.Settle();
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(Out, file + ".png"), ImageFormat.Png);
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var below in All(child))
            {
                yield return below;
            }
        }
    }
}
