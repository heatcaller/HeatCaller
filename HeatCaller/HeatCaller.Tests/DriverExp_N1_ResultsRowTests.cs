namespace HeatCaller.Tests;

/// <summary>
/// Driver experience N1 (22 Oct 2026): the public results document's
/// penalty numbers - `penS`, `penLaps`, `warn`, `jump` - and the rule that
/// the reasons never go into it.
///
/// A source scan for the second, because PublishSessionToRelay needs a
/// whole MainForm to run and the rule is about what it does NOT reference:
/// the reasons are free text about a named person, who may be a child, and
/// belong only in that driver's own night.
/// </summary>
public sealed class DriverExp_N1_ResultsRowTests
{
    [Fact]
    public void Penalty_seconds_are_the_race_and_after_race_time_rounded()
    {
        Assert.Equal(0, MainForm.PenaltySecondsOf(new SessionResultEntry()));
        Assert.Equal(5, MainForm.PenaltySecondsOf(new SessionResultEntry { RacePenaltyMs = 5000 }));
        Assert.Equal(13, MainForm.PenaltySecondsOf(new SessionResultEntry { RacePenaltyMs = 5000, TimePenaltyMs = 7500 }));
        Assert.Equal(0, MainForm.PenaltySecondsOf(new SessionResultEntry { RacePenaltyMs = -3000 }));
    }

    [Fact]
    public void The_public_results_row_carries_numbers_and_never_reasons()
    {
        var publish = Between(SourceOf("MainForm.Relay.cs"), "private void PublishSessionToRelay(", "private IReadOnlyList<SessionRecord> RacesTonight()");

        Assert.Contains("PenS = ", publish);
        Assert.Contains("PenLaps = ", publish);
        Assert.Contains("Warn = ", publish);
        Assert.Contains("Jump = ", publish);

        Assert.DoesNotContain("PenaltyWords", publish);
        Assert.DoesNotContain(".Reason", publish);
        Assert.DoesNotContain("Edits", publish);
    }

    [Fact]
    public void A_clean_row_leaves_every_penalty_field_out()
    {
        var row = Between(SourceOf("MainForm.Relay.cs"), "private sealed class RelayResultRow", "private readonly Dictionary<string, string> _relayIndexSessionsSeen");

        foreach (var name in new[] { "penS", "penLaps", "warn", "jump" })
        {
            var at = row.IndexOf($"[JsonPropertyName(\"{name}\")]", StringComparison.Ordinal);

            Assert.True(at >= 0, name);
            Assert.Contains("WhenWritingNull", row.Substring(at, 160));
        }
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, from);

        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, to);

        return source[start..end];
    }

    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }
}
