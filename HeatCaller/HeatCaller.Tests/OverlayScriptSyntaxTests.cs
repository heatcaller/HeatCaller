using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// EVERY OVERLAY PAGE'S SCRIPT PARSES (14 Nov 2026). The stream pages are
/// JavaScript inside C# strings, so a slip - a name declared twice, a regex
/// that lost its backslashes on the way in - compiles fine here and leaves
/// the whole overlay blank in OBS. Three such slips were caught by hand on
/// the day the overlays were built; this catches the next one. Each page's
/// scripts go through Node's own syntax check, and the test says so and
/// passes on a machine without Node (the relay's tests need it anyway).
/// </summary>
public sealed class OverlayScriptSyntaxTests
{
    private static string? NodePath()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (probe is null)
            {
                return null;
            }

            probe.WaitForExit(10_000);
            return probe.ExitCode == 0 ? "node" : null;
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public void Every_overlay_page_s_script_parses()
    {
        if (NodePath() is not { } node)
        {
            return;
        }

        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey, StreamBasics = "Ours | \"quoted\" and </b> words" });

        var pages = new List<(string Name, string Html)> { ("results", server.BuildOverlayPage()) };

        foreach (var layout in OverlayLayouts.BuiltIns.Where(l => l.Id != OverlayLayouts.ResultsId))
        {
            var copy = layout.Copy();
            copy.TextScale = 1.2;
            pages.Add((layout.Id, server.BuildBroadcastPage(copy)));
        }

        var folder = Path.Combine(Path.GetTempPath(), "hc-overlay-js-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        try
        {
            var problems = new List<string>();

            foreach (var (name, html) in pages)
            {
                var scripts = Regex.Matches(html, "<script>([\\s\\S]*?)</script>").Select(m => m.Groups[1].Value).ToList();
                Assert.NotEmpty(scripts);

                for (var i = 0; i < scripts.Count; i++)
                {
                    var file = Path.Combine(folder, $"{name}.{i}.js");
                    File.WriteAllText(file, scripts[i]);

                    using var check = Process.Start(new ProcessStartInfo(node, $"--check \"{file}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    })!;

                    var error = check.StandardError.ReadToEnd();
                    check.WaitForExit(30_000);

                    if (check.ExitCode != 0)
                    {
                        problems.Add($"{name} script {i}: {error.Split('\n').Take(5).Aggregate((a, b) => a + "\n" + b)}");
                    }
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n\n", problems));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // A test machine's temp folder.
            }
        }
    }
}
