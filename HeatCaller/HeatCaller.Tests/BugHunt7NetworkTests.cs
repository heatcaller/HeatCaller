using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The "Set up phone pages" button (bug hunt 7, Oct 2026): the script cmd
/// runs is ASCII whatever the user's name, the result says the Wi-Fi has to
/// be Private, and what netsh printed is read in the console's code page.
/// Strings only - nothing here elevates or changes the machine.
/// </summary>
public sealed class BugHunt7NetworkTests
{
    [Fact]
    public void The_script_is_pure_ASCII_and_finds_its_own_transcript()
    {
        // cmd reads the script in the console's code page, so a non-ASCII
        // path written into it (a profile called "José") named a folder that
        // did not exist and both "add" commands were skipped.
        var script = NetworkSetup.ScriptText(8080);

        Assert.All(script, c => Assert.True(c < 128, $"Non-ASCII character U+{(int)c:X4} in the script."));
        Assert.Contains("set \"T=%~dp0" + NetworkSetup.TranscriptFileName + "\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Success_says_the_WiFi_must_be_set_as_Private()
    {
        // The rule is private-profile only, and a newly joined Wi-Fi is Public.
        var done = NetworkSetup.Read(NetworkSetup.ExitBothDone, "ok", 8080);

        Assert.True(done.Done);
        Assert.Contains(NetworkSetup.PrivateNetworkNote, done.Message, StringComparison.Ordinal);
        Assert.Contains("profile=private", NetworkSetup.FirewallCommand(8080), StringComparison.Ordinal);
    }

    [Fact]
    public void Both_failing_does_not_claim_nothing_was_altered()
    {
        // The deletes run before the adds, so an old working setup may be gone.
        var result = NetworkSetup.Read(NetworkSetup.ExitBothFailed, "x", 8080);

        Assert.False(result.Done);
        Assert.DoesNotContain("nothing was altered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_code_page_850_transcript_keeps_its_letters()
    {
        // "Für" as netsh writes it redirected on German Windows: 0x81 is ü in
        // code page 850 and is not valid UTF-8.
        var cp850 = CodePagesEncodingProvider.Instance.GetEncoding(850);
        Assert.NotNull(cp850);

        var bytes = new byte[] { (byte)'F', 0x81, (byte)'r', (byte)'\r', (byte)'\n' };

        Assert.Equal("Für", NetworkSetup.ReadTranscript(bytes, cp850));
    }

    [Fact]
    public void A_UTF8_or_English_transcript_reads_as_it_is()
    {
        var cp850 = CodePagesEncodingProvider.Instance.GetEncoding(850);
        Assert.NotNull(cp850);

        Assert.Equal("Ok.", NetworkSetup.ReadTranscript(Encoding.ASCII.GetBytes("  Ok.\r\n"), cp850));
        Assert.Equal("Für", NetworkSetup.ReadTranscript(Encoding.UTF8.GetBytes("Für"), cp850));
    }
}
