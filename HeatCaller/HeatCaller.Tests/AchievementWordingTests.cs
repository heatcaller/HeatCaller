namespace HeatCaller.Tests;

/// <summary>
/// One word for one thing (17 Oct 2026). The app used to call achievements
/// "badges" in some places and "honours" in others; everything a club reads
/// or hears says "achievements" now. The printed check-in badge with the QR
/// code is a different thing and keeps its name, and so does the
/// Milestones segment, which is not about achievements at all.
/// </summary>
public sealed class AchievementWordingTests
{
    private static readonly string Key = nameof(AnnouncementEventType.AchievementUnlocked);

    [Fact]
    public void The_built_in_achievement_lines_never_say_badge()
    {
        var lines = VoiceProfile.CreateDefault().Events[Key].Templates;

        Assert.NotEmpty(lines);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain("badge", line, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("{achievement}", line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Saved_lines_move_to_the_new_placeholder_and_keep_a_clubs_own_wording()
    {
        var profile = new VoiceProfile();
        profile.Events[Key] = new AnnouncementEventConfig
        {
            Templates = new List<string>
            {
                "[cheerful] Badge for {driver} - {badge}.",
                "Well done {driver}, {BADGE} is yours.",
                "My own line for {driver}.",
            },
        };

        Assert.True(VoiceSettingsStore.UpdateOldLines(profile));

        Assert.Equal(
            new[]
            {
                "[cheerful] Achievement for {driver} - {achievement}.",
                "Well done {driver}, {achievement} is yours.",
                "My own line for {driver}.",
            },
            profile.Events[Key].Templates);

        // Once moved, nothing to do - so the file is not saved on every launch.
        Assert.False(VoiceSettingsStore.UpdateOldLines(profile));
    }

    [Fact]
    public void Built_in_lines_saved_in_british_spelling_are_brought_up_to_date()
    {
        var profile = new VoiceProfile();
        profile.Events[nameof(AnnouncementEventType.Streak)] = new AnnouncementEventConfig
        {
            Templates = new List<string> { "[excited] {driver} is dialled in - {streak} straight!", "Dialled in, {driver}!" },
        };

        Assert.True(VoiceSettingsStore.UpdateOldLines(profile));

        // The built-in line moves; the club's own line is theirs to keep.
        Assert.Equal(
            new[] { "[excited] {driver} is dialed in - {streak} straight!", "Dialled in, {driver}!" },
            profile.Events[nameof(AnnouncementEventType.Streak)].Templates);
    }

    [Fact]
    public void The_built_in_lines_are_already_the_new_wording()
    {
        // So a new install never has anything for UpdateOldLines to do.
        Assert.False(VoiceSettingsStore.UpdateOldLines(VoiceProfile.CreateDefault()));
    }

    [Fact]
    public void A_profile_without_the_achievement_announcement_is_left_alone()
    {
        Assert.False(VoiceSettingsStore.UpdateOldLines(new VoiceProfile()));
    }

    [Fact]
    public void The_milestones_segment_is_captioned_by_its_own_name()
    {
        Assert.Equal("Milestones", ShowBlock.CaptionFor(SegmentKind.Honours));
        Assert.Equal("Achievements Earned", ShowBlock.CaptionFor(SegmentKind.BadgesEarned));
    }
}
