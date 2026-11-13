using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Every time a person reads follows the 24-hour tick on Themes (12 Nov 2026,
/// after Driver Requests' "sent" time was found still fixed at 24-hour). A
/// fixed "HH:mm", or the Windows locale's own short time (":g", "t"), in the
/// app's code is a screen that ignores the tick - unless it is data, a file,
/// or a log, listed here with why.
/// </summary>
public sealed class NoFixedClockOnScreenTests
{
    // A line is fine when it is one of these.
    private static readonly string[] Allowed =
    {
        "InvariantCulture",            // written for a file, the relay or a parser
        "yyyy-MM-dd HH:mm:ss",         // log and crash-report stamps
        "TryParseExact",               // reading a time, not writing one
    };

    private static readonly string[] AllowedFiles =
    {
        "AppTime.cs",                  // where the formats live
        "DayScheduleControl.cs",       // the stored schedule time is HH:mm data
        "SupportBundle.cs",            // a file sent to support, read by people who need one clock
    };

    [Fact]
    public void No_screen_writes_a_fixed_clock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var source = Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
        var fixedClock = new Regex(@"HH:mm|:g\}|ToShortTimeString\(\)|ToString\(""t""\)");
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || AllowedFiles.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.StartsWith("//", StringComparison.Ordinal) || !fixedClock.IsMatch(line) || Allowed.Any(line.Contains))
                {
                    continue;
                }

                found.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
            }
        }

        Assert.True(found.Count == 0, "Use AppTime for these:\n" + string.Join("\n", found));
    }
}
