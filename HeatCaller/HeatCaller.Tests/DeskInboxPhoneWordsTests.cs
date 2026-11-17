namespace HeatCaller.Tests;

/// <summary>
/// THE INBOX'S "THEIR PHONE WILL SAY" (16 Nov 2026): the desk is shown the
/// headline the driver's phone will show before it answers, so the words
/// have to be the phone's own. These hold DeskInbox.PhoneHeadline in step
/// with the Me page's meReplyWords (relay/src/mespaces.ts) and the night
/// page's stateWords (relay/src/pages.ts).
/// </summary>
public sealed class DeskInboxPhoneWordsTests
{
    private static string Relay(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "relay", "src", file));
    }

    [Theory]
    [InlineData(DriverAsk.Late)]
    [InlineData(DriverAsk.Skip)]
    [InlineData(DriverAsk.Cover)]
    [InlineData(DriverAsk.Details)]
    [InlineData(DriverAsk.Query)]
    public void Each_headline_is_one_the_phone_shows(string kind)
    {
        var me = Relay("mespaces.ts");

        foreach (var state in new[] { DriverAsk.Approved, DriverAsk.Noted, DriverAsk.Declined })
        {
            var words = DeskInbox.PhoneHeadline(kind, state, "Kind");

            // The page writes them with either quote mark.
            Assert.True(me.Contains($"'{words}'", StringComparison.Ordinal) || me.Contains($"\"{words}\"", StringComparison.Ordinal),
                $"{kind} {state}: \"{words}\" is not in meReplyWords");
        }
    }

    [Fact]
    public void Other_kinds_say_the_night_pages_state_words()
    {
        var pages = Relay("pages.ts");

        Assert.Equal("Battery pack - approved", DeskInbox.PhoneHeadline(DriverAsk.Pack, DriverAsk.Approved, "Battery pack"));
        Assert.Equal("Battery pack - noted by the desk", DeskInbox.PhoneHeadline(DriverAsk.Pack, DriverAsk.Noted, "Battery pack"));
        Assert.Equal("Battery pack - declined", DeskInbox.PhoneHeadline(DriverAsk.Pack, DriverAsk.Declined, "Battery pack"));

        foreach (var words in new[] { "approved", "noted by the desk", "declined" })
        {
            Assert.Contains($"return '{words}';", pages, StringComparison.Ordinal);
        }
    }
}
