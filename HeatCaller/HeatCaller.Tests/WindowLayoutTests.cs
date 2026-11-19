using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The windows - dialogs - held to what the pages are held to: every one on
/// LayoutAudit.ConvertedWindows built at every size the pages are checked at
/// (LayoutView.Standard) and measured, with nothing overlapping, cut off or
/// past an edge, and no bigger than the screen it opens on.
///
/// A window is built with made-up data (WindowCatalog) - enough rows to
/// fill its list, names long enough to wrap - and its inside is sized the
/// way DialogFit sizes it for a screen of that size, then measured. Its
/// inside, not the window: Windows will not let a window - even one put
/// inside another - grow past the screen the test runs on, which on
/// GitHub's machines, and on a laptop at Windows' 150%, is smaller than
/// 1920x1080. So DialogFit.FitForLayoutCheck moves the window's page into a
/// plain panel, which can be any size, and says how big the window would
/// be (see MainForm.SizeContentForLayoutCheck, the pages' version of the
/// same thing). The window's frame is not in the pictures.
///
/// The report is written beside the page report, in layout\windows.txt, and
/// layout.cmd adds pictures of every window at every size, in
/// layout\windows.
/// </summary>
public sealed partial class LayoutTests
{
    /// <summary>
    /// Every window on LayoutAudit.ConvertedWindows is built by the window
    /// sweep. One on the list with no builder in WindowCatalog was never
    /// measured at all, and nothing noticed: it produced no findings, so it
    /// passed. "Line Up the Split Loop" sat on the list like that, its
    /// comment saying the test held it to its size while nothing did.
    /// </summary>
    [Fact]
    public void Every_guarded_window_is_built_by_the_sweep()
    {
        var built = WindowCatalog.All.Select(entry => entry.Name).ToList();

        var never = LayoutAudit.ConvertedWindows
            .Where(window => !built.Any(name =>
                string.Equals(name, window, StringComparison.Ordinal)
                || name.StartsWith(window + " (", StringComparison.Ordinal)
                || name.StartsWith(window + LayoutAudit.Separator, StringComparison.Ordinal)))
            .ToList();

        Assert.True(never.Count == 0,
            "On LayoutAudit.ConvertedWindows but never built by the window sweep - give each a WindowCatalog entry: "
            + string.Join(", ", never));
    }

    [Fact]
    public void EveryConvertedWindowFitsAtEverySize()
    {
        var only = Environment.GetEnvironmentVariable("HEATCALLER_LAYOUT_ONLY");
        var wantPictures = Environment.GetEnvironmentVariable("HEATCALLER_LAYOUT_PICTURES") == "1";

        var onlyNames = string.IsNullOrWhiteSpace(only)
            ? null
            : only.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // layout.cmd naming only pages: nothing here to check.
        if (onlyNames is not null && !WindowCatalog.All.Any(entry => WindowCatalog.Wanted(entry.Name, onlyNames)))
        {
            return;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "layout");
        var pictures = wantPictures ? Path.Combine(folder, "windows") : null;

        Directory.CreateDirectory(folder);

        if (pictures is not null && Directory.Exists(pictures))
        {
            Directory.Delete(pictures, recursive: true);
        }

        var sweep = WindowSweep.Run(LayoutView.Standard, onlyNames, pictures);

        File.WriteAllText(Path.Combine(folder, "windows.txt"), WindowReport.Write(sweep, onlyNames, pictures));
        _output.WriteLine(WindowReport.Summary(sweep));

        // Every window at every size - fewer means one threw before it got
        // as far as being measured.
        var checkedEverywhere = sweep.Pages.Count * LayoutView.Standard.Count;

        Assert.True(sweep.Measured == checkedEverywhere,
            $"Only {sweep.Measured} of {checkedEverywhere} windows-at-a-size were measured. What threw:\n"
            + string.Join("\n", sweep.Problems.Select(problem => $"  {problem.FirstAt}: {problem.What}")));

        var mustBeClean = sweep.Findings
            .Where(found => LayoutAudit.IsConvertedWindow(found.Page))
            .ToList();

        Assert.True(mustBeClean.Count == 0,
            "A window rebuilt to lay itself out has something that does not fit:\n"
            + string.Join("\n", mustBeClean.Take(40).Select(found =>
                $"  {found.View.Name} | {found.Finding.Where} | {found.Finding.Kind}: {found.Finding.Detail}"))
            + (mustBeClean.Count > 40 ? $"\n  ...and {mustBeClean.Count - 40} more" : string.Empty)
            + $"\nThe whole report is in {Path.Combine(folder, "windows.txt")}");
    }
}

/// <summary>
/// The windows the layout test builds, each with made-up data that fills
/// it: the converted ones, and each again filled in another way where that
/// changes what it shows (a second column set, nothing to list). The name
/// is the one on LayoutAudit.ConvertedWindows, with how it is filled in
/// after it in brackets.
///
/// Partial: a window's builders can live in a file of their own beside
/// this one (WindowCatalog.*.cs), each file a method listing its windows,
/// called from <see cref="Collect"/>. Methods only - no static fields, whose
/// order across files is not defined.
/// </summary>
internal static partial class WindowCatalog
{
    public static IReadOnlyList<(string Name, Func<Form> Build)> All { get; } = Collect();

    /// <summary>Every window, in the order the report lists them.</summary>
    private static List<(string Name, Func<Form> Build)> Collect()
    {
        var all = new List<(string Name, Func<Form> Build)>(FirstWindows);

        all.AddRange(MessageWindows());
        all.AddRange(NewDayAndRosterWindows());
        all.AddRange(FormatAndResultWindows());
        all.AddRange(HeatBalanceWindows());
        all.AddRange(SignupWindows());

        // The windows a new club meets first (30 Sep).
        all.AddRange(FirstRunWindows());
        all.AddRange(DriverProfileWindows());

        // Hearing a driver's name before it is read out (10 Oct).
        all.AddRange(NameCheckWindows());
        all.AddRange(ClassWindows());
        all.AddRange(ClassRuleWindows());
        all.AddRange(RentalWindows());
        all.AddRange(TechInspectionWindows());

        // The rest of race night, and its paperwork (30 Sep).
        all.AddRange(RaceNightToolWindows());
        all.AddRange(EventWindows());
        all.AddRange(PointsAndPinsWindows());
        all.AddRange(ReportWindows());
        all.AddRange(RaceControlPopupWindows());

        // Mission Control and Photo Finish, which sit beside Race Control (4 Oct).
        all.AddRange(RaceNightScreenWindows());

        // Line Up, from the Connection page (4 Oct).
        all.AddRange(SplitLoopWindows());

        // The garage, setup sheets and batteries (30 Sep).
        all.AddRange(GarageWindows());
        all.AddRange(ChassisSheetWindows());
        all.AddRange(SetupWindows());
        all.AddRange(BatteryWindows());

        // The club tools (30 Sep).
        all.AddRange(SeasonWindows());
        all.AddRange(GhostWindows());
        all.AddRange(PickerWindows());

        // Pick a driver, and which car a setup was for (18 Nov 2026, dialog jazz-up).
        all.AddRange(PickOneWindows());

        all.AddRange(ClubToolWindows());
        all.AddRange(CsvImportWindows());
        all.AddRange(EntryImportWindows());
        all.AddRange(CloseNightWindows());
        all.AddRange(DeskAndMeetingWindows());
        all.AddRange(PasteResultWindows());

        // The big editors, and the pages shown in windows of their own (30 Sep).
        all.AddRange(LookWindows());
        all.AddRange(SoundWindows());
        all.AddRange(ChartWindows());
        all.AddRange(TrackDesignerWindows());
        all.AddRange(TrackWindows());
        all.AddRange(PageHostWindows());
        all.AddRange(AnnouncerWindows());

        // Last: some of its windows close the day that is open, which the
        // windows above do not expect to find closed.
        all.AddRange(SetupWizardWindows());

        return all;
    }

    /// <summary>The first windows converted (29 Sep).</summary>
    private static (string Name, Func<Form> Build)[] FirstWindows => new (string, Func<Form>)[]
    {
        ("Corner Marshals", () => new MarshalAssignmentsForm("Stock Buggy", Marshals(rounds: 4))),
        ("Corner Marshals (none set)", () => new MarshalAssignmentsForm("Stock Buggy", new List<MarshalAssigner.Assignment>())),
        ("Add Main", () => new MainRoundForm("Stock Buggy", Drivers(14), new List<string>(), TrackLayoutStore.Load(), "A-Main")),
        ("Qualifying Standings", () => new QualifyingStandingsForm("Stock Buggy", Standings(10))),
        ("Qualifying Standings (none yet)", () => new QualifyingStandingsForm("Stock Buggy", new List<QualifyingCalculator.Entry>())),
        ("Qualifying Standings (best 2, consecutive laps)", () => new QualifyingStandingsForm(
            "Stock Buggy",
            Standings(10),
            new RaceClass { Name = "Stock Buggy", QualifyingMethod = QualifyingMethod.BestConsecutiveLaps, CountBestRounds = 2 })),
        ("Pre-race Check", () => PreRaceCheck()),
    };

    /// <summary>
    /// Whether a window is one layout.cmd asked for: by its name, or by the
    /// name of the window it is one filling-in of ("Qualifying Standings"
    /// asks for all three of its own).
    /// </summary>
    public static bool Wanted(string name, IReadOnlySet<string> only) =>
        only.Contains(name) || only.Contains(BaseName(name));

    /// <summary>The window's name without how it is filled in.</summary>
    public static string BaseName(string name)
    {
        var bracket = name.IndexOf(" (", StringComparison.Ordinal);
        return bracket > 0 ? name[..bracket] : name;
    }

    /// <summary>Drivers with names of every length, the longest long enough to wrap in a narrow column.</summary>
    private static List<DriverProfile> Drivers(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new DriverProfile
            {
                Transponder = (4_000_000 + i * 137).ToString(),
                DriverName = i % 4 == 0 ? $"Alexandria Montgomery-Fitzwilliam {i}" : $"Driver {i}",
                CarModel = i % 3 == 0 ? "Kyosho MR-03 EVO Sports" : "RWD",
            })
            .ToList();

    private static List<MarshalAssigner.Assignment> Marshals(int rounds)
    {
        var drivers = Drivers(12);

        return Enumerable.Range(1, rounds)
            .Select(round => new MarshalAssigner.Assignment(
                new RoundDefinition { ClassName = "Stock Buggy", RoundLabel = $"Qualifier 1 Heat {round}" },
                round == rounds ? null : new RoundDefinition { ClassName = "Stock Buggy", RoundLabel = $"Qualifier 1 Heat {round + 1}" },
                Enumerable.Range(1, 4)
                    .Select(post =>
                    {
                        // One uncovered post in the last round, as a real sheet has.
                        var driver = round == rounds && post == 4 ? null : drivers[(round * 4 + post) % drivers.Count];
                        return new MarshalAssigner.Post(post, $"Corner {post}", driver?.Transponder, driver?.DriverName);
                    })
                    .ToList(),
                round == rounds ? "One post has nobody to cover it." : null))
            .ToList();
    }

    private static List<QualifyingCalculator.Entry> Standings(int count) =>
        Drivers(count)
            .Select((driver, i) => new QualifyingCalculator.Entry(
                driver.Transponder,
                driver.DriverName,
                driver.CarModel,
                BestLaps: 24 - i,
                BestElapsedMs: 300_000 + i * 1_234,
                BestFastLapMs: 11_800 + i * 95,
                HeatsRun: 3,
                BestConsecutiveMs: 36_100 + i * 310,
                ConsecutiveStartLap: 4 + i % 5,
                SourceRoundLabel: $"Qualifier {1 + i % 3} Heat {1 + i % 2}",
                SourceSessionStartedUtc: new DateTime(2026, 9, 26, 19, 5, 0, DateTimeKind.Utc).AddMinutes(i * 7),
                HeatsCounted: 2,
                DroppedSummary: i % 2 == 0 ? "Qualifier 3 Heat 1 (19 laps)" : string.Empty,
                WindowsCounted: 2))
            .ToList();

    /// <summary>A race of eight nobody is hearing yet - the longest verdict, and a row for each.</summary>
    private static ReadinessCheckForm PreRaceCheck()
    {
        var drivers = Drivers(8);
        var profiles = ProfileStore.Load();

        profiles.UpsertMany(drivers);

        return new ReadinessCheckForm(
            drivers.Select(driver => driver.Transponder),
            new TransponderWatch(),
            profiles,
            "round's roster");
    }
}

/// <summary>What the window sweep found, on top of the page sweep's record: how many windows-at-a-size got as far as being measured.</summary>
internal sealed class WindowSweepResult
{
    public SweepResult Sweep { get; } = new();

    public int Measured { get; set; }

    public List<SweepFinding> Findings => Sweep.Findings;

    /// <summary>The windows checked, in catalog order.</summary>
    public List<string> Pages => Sweep.Pages;

    public List<(string What, string FirstAt)> Problems => Sweep.Problems;

    /// <summary>How long each window took at each size - its building, sizing, checking and putting away - for the report's slowest, with how long each of those parts took.</summary>
    public List<(string Where, TimeSpan Took, string Parts)> Timed { get; } = new();

    /// <summary>The windows that had to scroll at a size - their page taller than the screen leaves them, so their buttons open below the fold.</summary>
    public List<string> Scrolled { get; } = new();
}

/// <summary>Builds each window at each size, inside a screen-sized panel of an off-screen window, and measures it.</summary>
internal static class WindowSweep
{
    private static volatile string _now = "starting";

    /// <summary>Which part of a window's turn the sweep is in, for the message if it sticks: a hang in building points somewhere else than one in sizing or checking.</summary>
    private static volatile string _doing = string.Empty;

    /// <summary>
    /// Every window at every size. Caught as stuck when one part of one
    /// window's turn - building it, sizing it, checking one of its tabs,
    /// putting it away - takes more than a minute and a half, and named with
    /// that part; the whole sweep, which grows with every window added, may
    /// take up to two hours. (The last full sweep before this, 29 Sep:
    /// 50 windows at five sizes in about two minutes. The next, with 40 more,
    /// was still going at five - the report's Slowest line says which take
    /// the time.)
    ///
    /// 18 Nov 2026: the sweep passed 45 minutes on a PC running three other
    /// test runs, and failed at that limit half way through - read as a hang,
    /// though no step was stuck for the 90 seconds. Two hours, so only a
    /// stuck step fails it; that is what the 90 seconds is for.
    /// </summary>
    public static WindowSweepResult Run(IReadOnlyList<LayoutView> views, IReadOnlySet<string>? only, string? pictures) =>
        StaThread.Run(
            () => Sweep(views, only, pictures),
            TimeSpan.FromMinutes(120),
            () => _doing.Length == 0 ? _now : $"{_now}, {_doing}",
            stuckAfter: TimeSpan.FromSeconds(90));

    private static WindowSweepResult Sweep(IReadOnlyList<LayoutView> views, IReadOnlySet<string>? only, string? pictures)
    {
        var result = new WindowSweepResult();

        // As in LayoutSweep: an exception inside a window's messages is
        // noted and carried on from, rather than hanging the test on
        // WinForms' own error dialog.
        void Threw(object? sender, ThreadExceptionEventArgs e) => result.Sweep.Problem(_now, e.Exception);

        Application.ThreadException += Threw;

        try
        {
            foreach (var view in views)
            {
                SweepView(view, only, pictures, result);
            }
        }
        finally
        {
            Application.ThreadException -= Threw;

            // The whole process's, and left at whatever the last size was.
            AppTheme.SetFontScale(1f);
        }

        return result;
    }

    private static void SweepView(LayoutView view, IReadOnlySet<string>? only, string? pictures, WindowSweepResult result)
    {
        var clock = Stopwatch.StartNew();

        // A data folder of its own for every size, as the page sweep has.
        using var data = new TempAppData();

        AppTheme.SetFontScale(view.TextScale);

        // What a dialog has to fit in on this screen: all of it but the taskbar.
        var screen = new Rectangle(0, 0, view.Screen.Width, view.Screen.Height - LayoutView.TaskbarHeight);

        using var host = new OffScreenWindow();
        var desk = new Panel { Bounds = screen };

        host.Controls.Add(desk);
        host.Show();
        Settle();

        var viewPictures = pictures is null ? null : Path.Combine(pictures, view.Folder);

        if (viewPictures is not null)
        {
            Directory.CreateDirectory(viewPictures);
        }

        foreach (var (name, build) in WindowCatalog.All)
        {
            if (only is not null && !WindowCatalog.Wanted(name, only))
            {
                continue;
            }

            if (!result.Pages.Contains(name))
            {
                result.Pages.Add(name);
            }

            _now = $"{view.Name}: {name}";
            _doing = "building";

            var took = Stopwatch.StartNew();

            // Each part of the turn, for the report's Slowest line: a window
            // slow to build is slow in the app; one slow to check is only
            // slow in this test.
            var parts = new List<string>();
            var part = Stopwatch.StartNew();

            void PartDone(string what)
            {
                parts.Add($"{what} {part.Elapsed.TotalSeconds:0.0}");
                part.Restart();
            }

            Form? window = null;
            Panel? inside = null;

            try
            {
                window = build();
                PartDone("building");
                _doing = "sizing";

                // On screen when it is sized, as the window counts as shown
                // when DialogFit sizes it on Load (see DialogFit's notes):
                // its parts have their handles, a drop-down its real height.
                inside = new Panel();
                desk.Controls.Add(inside);
                Settle();

                var size = DialogFit.FitForLayoutCheck(window, inside, screen, out var scrolls);

                Settle();

                // A window can never come out bigger than the screen here -
                // DialogFit clamps it to the screen first - so a check for
                // that could never fire. What can happen is that its page
                // needs more height than the screen leaves it, and the
                // window scrolls: DialogFit's deliberate fallback, so not a
                // finding, but its buttons then open below the fold, which
                // LayoutAudit.Check does not see inside a scrolling page.
                // Listed in the report, so a window that should have room
                // and still scrolls is noticed.
                if (scrolls)
                {
                    result.Scrolled.Add($"{view.Name}: {name} ({size.Width}x{size.Height} on {screen.Width}x{screen.Height})");
                }

                var shown = inside;

                PartDone("sizing");
                _doing = "checking";

                var found = LayoutAudit.Check(shown, name, Settle, where =>
                {
                    _now = $"{view.Name}: {where}";

                    if (viewPictures is not null)
                    {
                        Picture(shown, where, viewPictures, result);
                    }
                });

                result.Findings.AddRange(found.Select(finding => new SweepFinding(view, name, finding)));
                result.Measured++;
                PartDone(viewPictures is null ? "checking" : "checking and pictures");
            }
            catch (Exception ex)
            {
                result.Sweep.Problem(_now, ex, "building or measuring threw ");
            }
            finally
            {
                _doing = "putting away";

                // The panel first: the window's page is in it now.
                inside?.Dispose();
                window?.Dispose();
                Settle();

                PartDone("putting away");

                // A window that times its own opening (WhereTimeGoes) says
                // where its building and sizing went, part by part.
                if (WhereTimeGoes.Take() is { } opening)
                {
                    parts.Add(opening);
                }

                result.Timed.Add(($"{view.Name}: {name}", took.Elapsed, string.Join(", ", parts)));
                _doing = string.Empty;
            }
        }

        result.Sweep.Timings[view] = clock.Elapsed;
    }

    private static void Settle() => StaThread.Settle();

    private static void Picture(Control window, string where, string folder, WindowSweepResult result)
    {
        try
        {
            using var bitmap = new Bitmap(Math.Max(1, window.Width), Math.Max(1, window.Height));

            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

            var file = where.Replace(LayoutAudit.Separator, " - ");

            foreach (var bad in Path.GetInvalidFileNameChars())
            {
                file = file.Replace(bad, '_');
            }

            bitmap.Save(Path.Combine(folder, file + ".png"), ImageFormat.Png);
            result.Sweep.Pictures++;
        }
        catch (Exception ex)
        {
            result.Sweep.Problem(where, ex, "no picture - ");
        }
    }

    /// <summary>A window nobody sees, holding the screen-sized panel: off-screen, transparent, never activated, not on the taskbar.</summary>
    private sealed class OffScreenWindow : Form
    {
        public OffScreenWindow()
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

/// <summary>The windows' report: what each window came to at each size.</summary>
internal static class WindowReport
{
    private const int NumberWidth = 11;

    public static string Write(WindowSweepResult sweep, IReadOnlySet<string>? only, string? pictures)
    {
        var text = new StringBuilder();

        text.AppendLine("HEATCALLER WINDOW LAYOUT REPORT");
        text.AppendLine($"Written {DateTime.Now:yyyy-MM-dd HH:mm} by the layout test (HeatCaller.Tests\\WindowLayoutTests.cs).");
        text.AppendLine();
        text.AppendLine("Every window in the test's catalog, filled in with made-up data, built at each size the pages are");
        text.AppendLine("checked at and measured - and checked against the screen: all of it but a "
            + $"{LayoutView.TaskbarHeight}px taskbar.");

        if (only is not null)
        {
            text.AppendLine($"Only: {string.Join(", ", only)}.");
        }

        text.AppendLine();
        text.AppendLine("Windows that must be clean (LayoutAudit.ConvertedWindows): " + string.Join(", ", LayoutAudit.ConvertedWindows) + ".");

        // Not failures - DialogFit scrolls on purpose when a screen can't
        // hold a window - but each one opens with its buttons out of sight.
        text.AppendLine(sweep.Scrolled.Count == 0
            ? "Scrolled: none - every window showed all of itself at every size."
            : $"Scrolled ({sweep.Scrolled.Count}, buttons below the fold until scrolled): " + string.Join("; ", sweep.Scrolled) + ".");
        text.AppendLine("Took: " + string.Join(", ", sweep.Sweep.Timings.Select(t => $"{t.Key.Heading} {t.Value.TotalSeconds:0.0}s")) + ".");

        if (sweep.Timed.Count > 0)
        {
            text.AppendLine("Slowest: " + string.Join(", ", sweep.Timed
                .OrderByDescending(timed => timed.Took)
                .Take(8)
                .Select(timed => $"{timed.Where} {timed.Took.TotalSeconds:0.0}s ({timed.Parts})")) + ".");
        }

        if (pictures is not null)
        {
            text.AppendLine($"Pictures: {sweep.Sweep.Pictures}, in {pictures}");
        }

        if (sweep.Problems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("THINGS THAT THREW");

            foreach (var (what, firstAt) in sweep.Problems)
            {
                text.AppendLine($"  {what}");
                text.AppendLine($"      at {firstAt}");
            }
        }

        text.AppendLine();
        text.Append(Summary(sweep));
        text.AppendLine();
        text.AppendLine("DETAILS");

        foreach (var view in LayoutView.Standard)
        {
            var atThisSize = sweep.Findings.Where(found => found.View == view).ToList();

            text.AppendLine();
            text.AppendLine($"=== {view.Name} - {atThisSize.Count} found ===");

            foreach (var window in sweep.Pages)
            {
                var onWindow = atThisSize.Where(found => found.Page == window).Select(found => found.Finding).ToList();

                if (onWindow.Count == 0)
                {
                    continue;
                }

                text.AppendLine();
                text.AppendLine($"--- {window} ({onWindow.Count}) ---");

                foreach (var finding in onWindow)
                {
                    text.AppendLine($"  [{finding.Kind}] {finding.Detail}");
                }
            }
        }

        return text.ToString();
    }

    public static string Summary(WindowSweepResult sweep)
    {
        var text = new StringBuilder();
        var nameWidth = Math.Max(24, sweep.Pages.Select(window => window.Length).DefaultIfEmpty(0).Max() + 4);

        text.AppendLine("FOUND, BY WINDOW AND SIZE");
        text.Append("Window".PadRight(nameWidth));

        foreach (var view in LayoutView.Standard)
        {
            text.Append(view.Heading.PadLeft(NumberWidth));
        }

        text.AppendLine();

        foreach (var window in sweep.Pages)
        {
            text.Append((LayoutAudit.IsConvertedWindow(window) ? window + " *" : window).PadRight(nameWidth));

            foreach (var view in LayoutView.Standard)
            {
                text.Append(sweep.Findings.Count(found => found.View == view && found.Page == window).ToString().PadLeft(NumberWidth));
            }

            text.AppendLine();
        }

        text.AppendLine();
        text.AppendLine("* rebuilt to lay itself out - must be 0 everywhere");

        return text.ToString();
    }
}
