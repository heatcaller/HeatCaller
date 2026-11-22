namespace HeatCaller.Tests;

/// <summary>
/// Every track record call names the driver (22 Nov 2026, the owner, from the
/// Voice of the Night video). One built-in line, "That is the fastest lap this
/// track has ever seen!", had no name: when a second driver broke a record set a
/// few seconds before, the room heard it as more praise for the first driver.
/// </summary>
public sealed class TrackRecordNamesTheDriverTests
{
    private static readonly string Key = nameof(AnnouncementEventType.TrackRecord);

    [Fact]
    public void Every_built_in_track_record_line_names_the_driver()
    {
        var lines = VoiceProfile.CreateDefault().Events[Key].Templates;

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains("{driver}", line, StringComparison.Ordinal));
    }

    [Fact]
    public void A_club_with_the_old_nameless_line_saved_gets_the_named_one_and_keeps_its_own()
    {
        var profile = new VoiceProfile();
        profile.Events[Key] = new AnnouncementEventConfig
        {
            Templates = new List<string>
            {
                "[awe] That is the fastest lap this track has ever seen!",
                "Our own record line for {driver}.",
            },
        };

        Assert.True(VoiceSettingsStore.UpdateOldLines(profile));

        Assert.Equal(
            new[]
            {
                "[awe] {driver}, {time} - that is the fastest lap this track has ever seen!",
                "Our own record line for {driver}.",
            },
            profile.Events[Key].Templates);

        // Once moved, nothing to do - so the file is not saved on every launch.
        Assert.False(VoiceSettingsStore.UpdateOldLines(profile));
    }
}
