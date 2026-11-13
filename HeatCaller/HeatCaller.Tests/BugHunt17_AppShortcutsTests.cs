namespace HeatCaller.Tests;

/// <summary>
/// 13 Nov 2026, Bug Hunt 17.
///
/// The rehearsal shortcut is made by a line of PowerShell with the paths in
/// single quotes. PowerShell ends a single-quoted string at a curly quote as
/// well as a straight one, and only the straight one was doubled - so on a
/// Windows account whose folder is O’Brien the script did not parse, and the
/// shortcut was never made, with nothing to say so.
/// </summary>
public sealed class BugHunt17_AppShortcutsTests
{
    [Theory]
    [InlineData('\'')]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public void Every_quote_PowerShell_reads_as_a_single_quote_is_doubled(char quote)
    {
        var path = $@"C:\Users\O{quote}Brien\AppData\Roaming\HeatCaller Rehearsal.lnk";

        Assert.Equal(
            $@"'C:\Users\O{quote}{quote}Brien\AppData\Roaming\HeatCaller Rehearsal.lnk'",
            AppShortcuts.PowerShellString(path));
    }

    [Fact]
    public void A_path_with_no_quote_in_it_is_only_wrapped()
    {
        Assert.Equal(@"'C:\Users\Sam\x.lnk'", AppShortcuts.PowerShellString(@"C:\Users\Sam\x.lnk"));
        Assert.Equal("''", AppShortcuts.PowerShellString(string.Empty));
    }
}
