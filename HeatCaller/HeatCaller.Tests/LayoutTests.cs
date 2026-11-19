using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using Xunit.Abstractions;

namespace HeatCaller.Tests;

/// <summary>
/// Every page in the sidebar, and every tab inside each one, built at the
/// window and text sizes a club is likely to run the app at, and measured
/// for anything that does not fit (LayoutAudit): controls on top of each
/// other, text cut short, things past the edge of whatever holds them,
/// parts squashed to nothing.
///
/// It fails for anything found on any page. Every page has been rebuilt to
/// lay itself out (LayoutAudit.ConvertedPages), and each has to come through
/// every size with nothing to say - as does a page added later, whether or
/// not it made the list.
///
/// The report is written beside the test dll, in layout\report.txt. Run
/// layout.cmd (beside check.cmd) for pictures of every page at every size
/// as well, in layout\pictures.
///
/// The windows - dialogs - are checked the same way, in WindowLayoutTests.cs.
/// </summary>
public sealed partial class LayoutTests
{
    private readonly ITestOutputHelper _output;

    public LayoutTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryPageFitsAtEverySize()
    {
        // layout.cmd sets these. check.cmd sets neither: every page, no pictures.
        var only = Environment.GetEnvironmentVariable("HEATCALLER_LAYOUT_ONLY");
        var wantPictures = Environment.GetEnvironmentVariable("HEATCALLER_LAYOUT_PICTURES") == "1";

        var onlyPages = string.IsNullOrWhiteSpace(only)
            ? null
            : only.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // layout.cmd naming only windows: nothing here to check. Left
        // before the sweep, so it neither builds the main window five times
        // for nothing nor throws away the pages' pictures from the last run.
        if (onlyPages is not null && onlyPages.All(IsAWindow))
        {
            return;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "layout");
        var pictures = wantPictures ? Path.Combine(folder, "pictures") : null;

        Directory.CreateDirectory(folder);

        // Last run's pictures would otherwise sit among this run's, and a
        // page that has since been renamed would leave its old ones behind.
        if (pictures is not null && Directory.Exists(pictures))
        {
            Directory.Delete(pictures, recursive: true);
        }

        var sweep = LayoutSweep.Run(LayoutView.ForPages, onlyPages, pictures);

        File.WriteAllText(Path.Combine(folder, "report.txt"), LayoutReport.Write(sweep, onlyPages, pictures));
        _output.WriteLine(LayoutReport.Summary(sweep));

        if (onlyPages is not null)
        {
            // A name that is a window's is the window test's to check (EveryConvertedWindowFitsAtEverySize).
            Assert.True(sweep.Pages.Count > 0 || WindowCatalog.All.Any(window => WindowCatalog.Wanted(window.Name, onlyPages)),
                $"Nothing is called \"{only}\". The pages are: {string.Join(", ", sweep.AllPages)}. "
                + $"The windows are: {string.Join(", ", WindowCatalog.All.Select(window => WindowCatalog.BaseName(window.Name)).Distinct())}.");
        }
        else
        {
            // Fewer than this means the sweep lost its way - the pages were
            // not found, or not put on screen - rather than that the app
            // lost a score of pages.
            Assert.True(sweep.Pages.Count >= 20,
                $"Only {sweep.Pages.Count} pages were checked: {string.Join(", ", sweep.Pages)}.");
        }

        // Every finding counts, not only those on LayoutAudit.ConvertedPages:
        // every page is on it now, and a page added later without the layout
        // pieces has to fail here too rather than slip into the report.
        var mustBeClean = sweep.Findings;

        Assert.True(mustBeClean.Count == 0,
            "A page has something that does not fit:\n"
            + string.Join("\n", mustBeClean.Take(40).Select(found =>
                $"  {found.View.Name} | {found.Finding.Where} | {found.Finding.Kind}: {found.Finding.Detail}"))
            + (mustBeClean.Count > 40 ? $"\n  ...and {mustBeClean.Count - 40} more" : string.Empty)
            + $"\nThe whole report is in {Path.Combine(folder, "report.txt")}");
    }

    /// <summary>
    /// Next Steps is a split: the day's summary over tonight's program. The
    /// program's buttons are what a race night is run from, so at every size
    /// its half is kept tall enough for them, and the summary above scrolls
    /// instead (MainForm.Pages, KeepProgramInView).
    ///
    /// The test above cannot see this go wrong: a button scrolled below the
    /// fold of a half that scrolls is not cut off, it is only out of sight -
    /// which is how 1366x768, 1280x720 and the largest text size once had
    /// them. So it is checked here: every button under the program wholly
    /// inside what its half shows, with nothing to scroll to.
    /// </summary>
    [Fact]
    public void NextStepsKeepsTheProgramsButtonsInViewAtEverySize()
    {
        var wrong = new List<string>();

        // Sizes, not looks: each size is checked once, however often the
        // sweep looks at the page.
        var looked = new HashSet<string>(StringComparer.Ordinal);

        var sweep = LayoutSweep.Run(
            LayoutView.ForPages,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Race Day" },
            pictures: null,
            alsoCheck: (view, _, page) =>
            {
                wrong.AddRange(ProgramButtonsOutOfView(page).Select(what => $"  {view.Name}: {what}"));

                // After the check, so a check that threw - noted as a
                // problem by the sweep - counts as a size not looked at.
                looked.Add(view.Name);
            });

        Assert.True(looked.Count == LayoutView.ForPages.Count,
            $"The Race Day page was only checked at {looked.Count} of {LayoutView.ForPages.Count} sizes. "
            + "Problems: " + string.Join("; ", sweep.Problems.Select(problem => $"{problem.FirstAt}: {problem.What}")));

        Assert.True(wrong.Count == 0,
            "Next Steps has the program's buttons out of sight:\n" + string.Join("\n", wrong));
    }

    /// <summary>Whether a name layout.cmd was given is a window's (WindowCatalog) rather than a page's.</summary>
    private static bool IsAWindow(string name) =>
        WindowCatalog.All.Any(window =>
            string.Equals(window.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(WindowCatalog.BaseName(window.Name), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What is wrong with the program's buttons on the Race Day page on screen - nothing, when they all show.</summary>
    private static List<string> ProgramButtonsOutOfView(Control page)
    {
        var wrong = new List<string>();
        var program = Descendants(page).OfType<RaceProgramControl>().FirstOrDefault();

        if (program is null)
        {
            wrong.Add("the Race Day page has no program on it");
            return wrong;
        }

        // Its tab, which the measuring may have left showing another.
        for (Control? up = program; up is not null; up = up.Parent)
        {
            if (up is TabPage tab && tab.Parent is TabControl tabs && !ReferenceEquals(tabs.SelectedTab, tab))
            {
                tabs.SelectedTab = tab;
            }
        }

        // The splitter is moved from work the halves post to themselves
        // after their layouts - see TodayControl.ContentHeightChanged.
        for (var i = 0; i < 4; i++)
        {
            StaThread.Settle();
        }

        var half = program.RectangleToScreen(program.ClientRectangle);
        var shown = Rectangle.Intersect(half, page.RectangleToScreen(page.ClientRectangle));
        var buttons = Descendants(program).OfType<Button>().Where(button => button.Visible).ToList();

        if (buttons.Count == 0)
        {
            wrong.Add("the program shows no buttons at all");
        }

        foreach (var button in buttons)
        {
            var at = button.RectangleToScreen(button.ClientRectangle);

            if (!shown.Contains(at))
            {
                wrong.Add($"\"{button.Text}\" is at {at.Top - half.Top}-{at.Bottom - half.Top}px down a half that shows {shown.Top - half.Top}-{shown.Bottom - half.Top}px");
            }
        }

        return wrong;
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;

            foreach (var below in Descendants(child))
            {
                yield return below;
            }
        }
    }
}

/// <summary>A window and text size to check every page at.</summary>
internal sealed record LayoutView(string Name, string Heading, string Folder, Size Screen, float TextScale)
{
    /// <summary>
    /// A text size to change to once the window is up, the way Ctrl + and -
    /// do - null for a window built at its size and left there. See
    /// TextSizeChangedWhileRunning.
    /// </summary>
    public float? ChangedTo { get; init; }

    /// <summary>
    /// The sizes the test runs at.
    ///
    /// 1920x1080 at the three text sizes a director picks with Ctrl +/-.
    /// Then the two small screens: 1366x768, still common on a club's old
    /// laptop, and 1280x720, which is what this app sees on a new 1920x1080
    /// laptop at Windows' own 150% scaling - it runs without DPI awareness
    /// (see HeatCaller.csproj), so Windows gives it the screen at 96 dpi and
    /// stretches the picture.
    /// </summary>
    public static readonly IReadOnlyList<LayoutView> Standard = new[]
    {
        new LayoutView("1920x1080, text 100%", "1920 100%", "1920x1080 text 100", new Size(1920, 1080), 1.0f),
        new LayoutView("1920x1080, text 150%", "1920 150%", "1920x1080 text 150", new Size(1920, 1080), 1.5f),
        new LayoutView("1920x1080, text 200%", "1920 200%", "1920x1080 text 200", new Size(1920, 1080), 2.0f),
        new LayoutView("1366x768, text 100%", "1366 100%", "1366x768 text 100", new Size(1366, 768), 1.0f),
        new LayoutView("1280x720, text 100%", "1280 100%", "1280x720 text 100", new Size(1280, 720), 1.0f),
    };

    /// <summary>
    /// 1920x1080, built at the normal text size and changed to 200% with
    /// the window up, as Ctrl + does.
    ///
    /// The other sizes are each a window built at its text size. This one
    /// is here because a text size changed while the app runs takes another
    /// way to the screen: every page but the one showing holds its layout
    /// while the new size goes through the window, and is laid out when it
    /// is next opened (LayoutFreeze) - so each page is checked as it comes
    /// out of that, as well as built fresh.
    /// </summary>
    public static readonly LayoutView TextSizeChangedWhileRunning =
        new("1920x1080, text 100% changed to 200%", "1920 +200%", "1920x1080 text 100 changed to 200", new Size(1920, 1080), 1.0f) { ChangedTo = 2.0f };

    /// <summary>The sizes the pages are checked at: every one of Standard, and the text size changed while running.</summary>
    public static readonly IReadOnlyList<LayoutView> ForPages = Standard.Append(TextSizeChangedWhileRunning).ToList();

    /// <summary>The Windows 11 taskbar at 96 dpi.</summary>
    public const int TaskbarHeight = 48;

    /// <summary>What a maximised window has inside its frame on this screen: all of it, less the taskbar and the title bar.</summary>
    public Size Client => new(Screen.Width, Screen.Height - TaskbarHeight - SystemInformation.CaptionHeight);
}

internal sealed record SweepFinding(LayoutView View, string Page, LayoutAudit.Finding Finding);

internal sealed class SweepResult
{
    public List<SweepFinding> Findings { get; } = new();

    /// <summary>The pages checked, in sidebar-registry order.</summary>
    public List<string> Pages { get; } = new();

    /// <summary>Every page there is, checked or not - for saying what a mistyped page name should have been.</summary>
    public List<string> AllPages { get; } = new();

    /// <summary>Anything that threw along the way, each once, with where it first did.</summary>
    public List<(string What, string FirstAt)> Problems { get; } = new();

    private readonly Dictionary<string, int> _problemCounts = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes something that threw. The same failure again is counted, not
    /// listed again - a timer on a closed window's page can throw the same
    /// thing every half second for the rest of the run.
    /// </summary>
    public void Problem(string where, Exception ex, string? doing = null)
    {
        var frame = ex.StackTrace?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Contains("HeatCaller", StringComparison.Ordinal))
            ?? string.Empty;

        Problem(where, $"{doing}{ex.GetType().Name}: {ex.Message} {frame}".Trim());
    }

    public void Problem(string where, string what)
    {
        if (_problemCounts.TryGetValue(what, out var seen))
        {
            _problemCounts[what] = seen + 1;
            return;
        }

        _problemCounts[what] = 1;
        Problems.Add((what, where));
    }

    /// <summary>How many times a problem happened.</summary>
    public int TimesFor(string what) => _problemCounts.TryGetValue(what, out var count) ? count : 1;

    public Dictionary<LayoutView, TimeSpan> Timings { get; } = new();

    /// <summary>How long the text size change took, at a size that changes it with the window up (LayoutView.ChangedTo).</summary>
    public Dictionary<LayoutView, TimeSpan> TextSizeChanges { get; } = new();

    public int Pictures { get; set; }
}

/// <summary>
/// Builds the main window at each size, off-screen and never activated, and
/// measures every page in it.
/// </summary>
internal static class LayoutSweep
{
    /// <summary>What the sweep is doing, for the message if it never finishes.</summary>
    private static volatile string _now = "starting";

    /// <summary>
    /// Ten minutes. Five windows of twenty-five pages take a minute or two
    /// on a laptop; the limit is for GitHub's slower machines, which run
    /// check.cmd on every push (.github\workflows\check.yml).
    /// </summary>
    /// <param name="alsoCheck">
    /// Run on each page after it has been measured, for a test that holds a
    /// page to something the general rules cannot see. What it throws is
    /// noted as a problem, as anything the measuring throws is.
    /// </param>
    public static SweepResult Run(
        IReadOnlyList<LayoutView> views,
        IReadOnlySet<string>? onlyPages,
        string? pictures,
        Action<LayoutView, string, Control>? alsoCheck = null) =>
        StaThread.Run(() => Sweep(views, onlyPages, pictures, alsoCheck), TimeSpan.FromMinutes(10), () => _now);

    private static SweepResult Sweep(IReadOnlyList<LayoutView> views, IReadOnlySet<string>? onlyPages, string? pictures, Action<LayoutView, string, Control>? alsoCheck)
    {
        var result = new SweepResult();

        // An exception inside a window's message handling - a paint at a size
        // nobody has tried before - would otherwise put up WinForms' own
        // error dialog, on this hidden window, and hang the test. Noted and
        // carried on from instead.
        void Threw(object? sender, ThreadExceptionEventArgs e) => result.Problem(_now, e.Exception);

        Application.ThreadException += Threw;

        try
        {
            foreach (var view in views)
            {
                SweepView(view, onlyPages, pictures, alsoCheck, result);
            }
        }
        finally
        {
            Application.ThreadException -= Threw;

            // Shared by every window in the process, and left at whatever the
            // last window built here was built at.
            AppTheme.SetFontScale(1f);
        }

        return result;
    }

    private static void SweepView(LayoutView view, IReadOnlySet<string>? onlyPages, string? pictures, Action<LayoutView, string, Control>? alsoCheck, SweepResult result)
    {
        var clock = Stopwatch.StartNew();

        // A new install of its own for every size: the window saves as it
        // closes, and the next size must not start from what this one left.
        using var data = new TempAppData();

        // The text size is read once, before a single control is built - the
        // way the app starts after the size has been changed and it has been
        // restarted. Nothing the Ready handlers would ask about is left to
        // ask: no first-run wizard, no race-night offer, no backup to make.
        new AppSettings
        {
            FontScale = view.TextScale,
            StartMaximized = false,
            HasCompletedFirstRun = true,
            HasSeenRaceDaySetup = true,
            AutoBackupEnabled = false,
        }.Save();

        _now = $"{view.Name}: building the window";

        var window = new MainForm();

        try
        {
            window.LayoutCheckMode = true;
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(-20000, -20000);
            window.Show();
            Settle();

            window.SizeContentForLayoutCheck(view.Client);
            Settle();

            // Changed with the window up, as Ctrl + does - see
            // LayoutView.TextSizeChangedWhileRunning. Timed for the report.
            if (view.ChangedTo is { } changedTo)
            {
                _now = $"{view.Name}: changing the text size";

                var changing = Stopwatch.StartNew();
                window.ChangeTextSizeForLayoutCheck(changedTo);
                result.TextSizeChanges[view] = changing.Elapsed;

                Settle();
            }

            var viewPictures = pictures is null ? null : Path.Combine(pictures, view.Folder);

            if (viewPictures is not null)
            {
                Directory.CreateDirectory(viewPictures);
            }

            MeasurePages(window, view, onlyPages, viewPictures, alsoCheck, result);
        }
        finally
        {
            _now = $"{view.Name}: closing the window";

            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                result.Problem(_now, ex);
            }

            window.Dispose();
            Settle();

            result.Timings[view] = clock.Elapsed;
        }
    }

    /// <summary>
    /// Opens each page and measures it.
    /// </summary>
    private static void MeasurePages(
        MainForm window,
        LayoutView view,
        IReadOnlySet<string>? onlyPages,
        string? viewPictures,
        Action<LayoutView, string, Control>? alsoCheck,
        SweepResult result)
    {
        foreach (var (name, page) in window.LayoutCheckPages)
        {
            if (!result.AllPages.Contains(name))
            {
                result.AllPages.Add(name);
            }

            if (onlyPages is not null && !onlyPages.Contains(name))
            {
                continue;
            }

            if (!result.Pages.Contains(name))
            {
                result.Pages.Add(name);
            }

            _now = $"{view.Name}: {name}";

            try
            {
                window.ShowPageForLayoutCheck(name);
            }
            catch (Exception ex)
            {
                result.Problem(_now, ex, "opening the page threw ");
                continue;
            }

            try
            {
                var found = LayoutAudit.Check(page, name, Settle, where =>
                {
                    _now = $"{view.Name}: {where}";

                    if (viewPictures is not null)
                    {
                        Picture(page, where, viewPictures, result);
                    }
                });

                result.Findings.AddRange(found.Select(finding => new SweepFinding(
                    view,
                    name,
                    finding)));

                alsoCheck?.Invoke(view, name, page);
            }
            catch (Exception ex)
            {
                result.Problem(_now, ex, "measuring threw ");
            }
        }
    }

    private static void Settle() => StaThread.Settle();

    private static void Picture(Control page, string where, string folder, SweepResult result)
    {
        try
        {
            using var bitmap = new Bitmap(Math.Max(1, page.Width), Math.Max(1, page.Height));

            page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(folder, FileNameFor(where) + ".png"), ImageFormat.Png);

            result.Pictures++;
        }
        catch (Exception ex)
        {
            result.Problem(where, ex, "no picture - ");
        }
    }

    private static string FileNameFor(string where)
    {
        var name = where.Replace(LayoutAudit.Separator, " - ");

        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        return name;
    }
}

/// <summary>The report the sweep leaves behind, and the table from it the test prints.</summary>
internal static class LayoutReport
{
    /// <summary>Details listed for one page at one size before the rest are counted instead.</summary>
    private const int DetailsPerPage = 80;

    private const int NumberWidth = 11;

    public static string Write(SweepResult sweep, IReadOnlySet<string>? onlyPages, string? pictures)
    {
        var text = new StringBuilder();

        text.AppendLine("HEATCALLER LAYOUT REPORT");
        text.AppendLine($"Written {DateTime.Now:yyyy-MM-dd HH:mm} by the layout test (HeatCaller.Tests\\LayoutTests.cs).");
        text.AppendLine();
        text.AppendLine("Every page in the sidebar, and every tab inside each one, built at each size below and measured.");
        text.AppendLine("This is each page's own layout, as the app shows it: nothing in the app nudges a page into shape");
        text.AppendLine("afterwards, so every page has to get its layout right by itself.");
        text.AppendLine();
        text.AppendLine($"Sizes: the room inside a maximised window on that screen, less a {LayoutView.TaskbarHeight}px taskbar and the title bar.");
        text.AppendLine("1280x720 is what the app sees on a 1920x1080 laptop at Windows' 150% scaling (it runs without DPI awareness).");
        text.AppendLine("Data: a new install - no drivers, classes or results - so lists and grids are empty.");
        text.AppendLine("The window is off-screen and never painted, so a layout a control only works out while painting is not seen.");

        if (onlyPages is not null)
        {
            text.AppendLine($"Only: {string.Join(", ", onlyPages)}.");
        }

        text.AppendLine();
        // Every page must be clean, on the list or not - see EveryPageFitsAtEverySize.
        text.AppendLine("Every page must be clean. Rebuilt on the layout pieces (LayoutAudit.ConvertedPages): "
            + (LayoutAudit.ConvertedPages.Count == 0 ? "none yet." : string.Join(", ", LayoutAudit.ConvertedPages) + "."));
        text.AppendLine("Took: " + string.Join(", ", sweep.Timings.Select(t => $"{t.Key.Heading} {t.Value.TotalSeconds:0.0}s")) + ".");

        foreach (var (view, took) in sweep.TextSizeChanges)
        {
            text.AppendLine($"{view.Heading}: built at {view.TextScale * 100:0}%, then changed to {view.ChangedTo * 100:0}% with the window up, as Ctrl + does - which took {took.TotalSeconds:0.00}s.");
        }

        if (pictures is not null)
        {
            text.AppendLine($"Pictures: {sweep.Pictures}, in {pictures}");
        }

        if (sweep.Problems.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("THINGS THAT THREW");

            foreach (var (what, firstAt) in sweep.Problems)
            {
                var times = sweep.TimesFor(what);

                text.AppendLine($"  {what}");
                text.AppendLine($"      at {firstAt}" + (times > 1 ? $", and {times - 1} more times after" : string.Empty));
            }
        }

        text.AppendLine();
        text.Append(Summary(sweep));

        text.AppendLine();
        text.AppendLine("DETAILS");

        foreach (var view in LayoutView.ForPages)
        {
            var atThisSize = sweep.Findings.Where(found => found.View == view).ToList();

            text.AppendLine();
            text.AppendLine($"=== {view.Name} (inside the window: {view.Client.Width}x{view.Client.Height}) - {atThisSize.Count} found ===");

            foreach (var page in sweep.Pages)
            {
                var onPage = atThisSize
                    .Where(found => found.Page == page)
                    .Select(found => found.Finding)
                    .OrderBy(finding => finding.Where, StringComparer.Ordinal)
                    .ThenBy(finding => finding.Kind, StringComparer.Ordinal)
                    .ToList();

                if (onPage.Count == 0)
                {
                    continue;
                }

                text.AppendLine();
                text.AppendLine($"--- {page} ({onPage.Count}) ---");

                foreach (var finding in onPage.Take(DetailsPerPage))
                {
                    // The tab it is on, when it is on one: "Setup", or "Sounds › Music".
                    var tab = finding.Where.StartsWith(page + LayoutAudit.Separator, StringComparison.Ordinal)
                        ? $"({finding.Where[(page.Length + LayoutAudit.Separator.Length)..]}) "
                        : string.Empty;

                    text.AppendLine($"  {tab}[{finding.Kind}] {finding.Detail}");
                }

                if (onPage.Count > DetailsPerPage)
                {
                    text.AppendLine($"  ...and {onPage.Count - DetailsPerPage} more");
                }
            }
        }

        return text.ToString();
    }

    /// <summary>How much was found on each page at each size, then by kind.</summary>
    public static string Summary(SweepResult sweep)
    {
        var text = new StringBuilder();
        var views = LayoutView.ForPages;
        var nameWidth = Math.Max(24, sweep.Pages.Select(page => page.Length).DefaultIfEmpty(0).Max() + 2);

        text.AppendLine("FOUND, BY PAGE AND SIZE");
        text.Append("Page".PadRight(nameWidth));

        foreach (var view in views)
        {
            text.Append(view.Heading.PadLeft(NumberWidth));
        }

        text.AppendLine();

        foreach (var page in sweep.Pages)
        {
            text.Append((LayoutAudit.IsConverted(page) ? page + " *" : page).PadRight(nameWidth));

            foreach (var view in views)
            {
                text.Append(sweep.Findings.Count(found => found.View == view && found.Page == page).ToString().PadLeft(NumberWidth));
            }

            text.AppendLine();
        }

        text.Append("Total".PadRight(nameWidth));

        foreach (var view in views)
        {
            text.Append(sweep.Findings.Count(found => found.View == view).ToString().PadLeft(NumberWidth));
        }

        text.AppendLine();
        text.AppendLine();
        text.AppendLine("FOUND, BY KIND");

        var kinds = new[]
        {
            LayoutAudit.Overlap, LayoutAudit.CutOff, LayoutAudit.PastEdge,
            LayoutAudit.Squashed, LayoutAudit.TooNarrow, LayoutAudit.Tabs,
        };

        foreach (var kind in kinds)
        {
            text.Append(kind.PadRight(nameWidth));

            foreach (var view in views)
            {
                text.Append(sweep.Findings.Count(found => found.View == view && found.Finding.Kind == kind).ToString().PadLeft(NumberWidth));
            }

            text.AppendLine();
        }

        if (LayoutAudit.ConvertedPages.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("* rebuilt to lay itself out - every page, starred or not, must be 0 everywhere");
        }

        return text.ToString();
    }
}
