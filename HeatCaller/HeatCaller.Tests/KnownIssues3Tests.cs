using System.Drawing;

namespace HeatCaller.Tests;

/// <summary>
/// The known issues closed on 22 Oct 2026 in the relay, the LAN pages, the
/// Track Designer, the championship and the voice page - the parts of them
/// that are plain logic.
/// </summary>
public class KnownIssues3Tests
{
    // --- LAN pages: a locked achievements file is read again soon ---------

    [Fact]
    public void A_locked_achievements_file_is_tried_again_in_seconds_not_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), WebServerService.AchievementRetryAfter(1));
        Assert.Equal(TimeSpan.FromSeconds(30), WebServerService.AchievementRetryAfter(2));
        Assert.Equal(TimeSpan.FromSeconds(60), WebServerService.AchievementRetryAfter(3));
    }

    [Fact]
    public void A_file_that_stays_locked_backs_off_to_the_old_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WebServerService.AchievementRetryAfter(6));
        Assert.Equal(TimeSpan.FromMinutes(5), WebServerService.AchievementRetryAfter(500));
    }

    // --- Room vote: the key a ballot went up under ------------------------

    [Fact]
    public void A_key_reissued_mid_poll_keeps_the_old_one_for_that_poll()
    {
        Assert.Equal("old", RelayPublisher.NextKeyBefore(null, "old", "new", "poll-1", "poll-1", open: true));

        // Republished again with nothing changed: still kept for that poll.
        Assert.Equal("old", RelayPublisher.NextKeyBefore("old", "new", "new", "poll-1", "poll-1", open: true));
    }

    [Fact]
    public void A_new_poll_or_a_close_lets_the_old_key_go()
    {
        Assert.Null(RelayPublisher.NextKeyBefore("old", "new", "new", "poll-1", "poll-2", open: true));
        Assert.Null(RelayPublisher.NextKeyBefore("old", "new", "new", "poll-1", "poll-1", open: false));

        // Even a key changed at the very moment the next poll goes up.
        Assert.Null(RelayPublisher.NextKeyBefore(null, "old", "new", "poll-1", "poll-2", open: true));
    }

    [Fact]
    public void An_unchanged_key_never_makes_an_old_one()
    {
        Assert.Null(RelayPublisher.NextKeyBefore(null, "same", "same", "poll-1", "poll-1", open: true));
        Assert.Null(RelayPublisher.NextKeyBefore(null, null, "first", null, "poll-1", open: true));
    }

    // --- Track Designer: cutting part of the racing line ------------------

    private static RcpTrackDesign WithALine()
    {
        var design = new RcpTrackDesign();

        for (var i = 0; i < 6; i++)
        {
            design.Route.Add(new RcpRoutePoint { X = i * 2, Y = 1 });
        }

        return design;
    }

    [Fact]
    public void Picking_part_of_the_line_is_told_apart_from_picking_all_of_it()
    {
        var design = WithALine();

        Assert.True(RcpSelection.In(design, new Rectangle(0, 0, 4, 4)).HoldsPartOfTheLine(design));
        Assert.False(RcpSelection.Everything(design).HoldsPartOfTheLine(design));
        Assert.False(new RcpSelection().HoldsPartOfTheLine(design));
    }

    [Fact]
    public void A_cut_of_part_of_the_line_leaves_every_point_of_it_on_the_plan()
    {
        var design = WithALine();
        var picked = RcpSelection.In(design, new Rectangle(0, 0, 4, 4));

        picked.DeleteFrom(design, keepTheLine: picked.HoldsPartOfTheLine(design));

        Assert.Equal(6, design.Route.Count);
    }

    [Fact]
    public void A_cut_of_the_whole_line_still_takes_it()
    {
        var design = WithALine();
        var picked = RcpSelection.Everything(design);

        picked.DeleteFrom(design, keepTheLine: picked.HoldsPartOfTheLine(design));

        Assert.Empty(design.Route);
    }

    // --- Championship: points never below zero ----------------------------

    [Fact]
    public void A_negative_row_on_file_scores_nothing_rather_than_taking_points_away()
    {
        var season = new Season
        {
            PointsScheme = new List<PointsSchemeEntry>
            {
                new() { Position = 1, Points = 10 },
                new() { Position = 2, Points = -5 },
            },
        };

        Assert.Equal(10, season.PointsForPosition(1));
        Assert.Equal(0, season.PointsForPosition(2));
        Assert.Equal(0, season.PointsForPosition(3));

        // Left as it is on disk, so the commentary stays as careful as it was.
        Assert.True(season.PenalisesAnyone());
    }

    // --- The voice page: what a line costs, and re-rolling it -------------

    [Fact]
    public void A_line_costs_its_characters_without_its_audio_tags()
    {
        Assert.Equal(0, VoiceSettingsControl.CharactersCharged(null));
        Assert.Equal(0, VoiceSettingsControl.CharactersCharged("   "));
        Assert.Equal("Green flag!".Length, VoiceSettingsControl.CharactersCharged("  Green flag!  "));
        Assert.Equal(
            VoiceProfile.StripAudioTags("[excited] Green flag!").Trim().Length,
            VoiceSettingsControl.CharactersCharged("[excited] Green flag!"));
    }

    [Fact]
    public void A_line_saved_with_other_settings_is_not_re_rolled_beside_itself()
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "m1" };
        var line = new VoiceLibrary.SavedLine("someone-elses-key", "Green flag!", "Voice", DateTime.UtcNow, 100);

        Assert.NotNull(VoiceSettingsControl.WhyNoReroll(profile, line, VoiceLibrary.KeyFor(profile, line.Text)));
    }

    [Fact]
    public void A_line_saved_with_the_voice_as_it_is_now_can_be_re_rolled()
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "m1" };
        var key = VoiceLibrary.KeyFor(profile, "Green flag!");
        var line = new VoiceLibrary.SavedLine(key, "Green flag!", "Voice", DateTime.UtcNow, 100);

        Assert.Null(VoiceSettingsControl.WhyNoReroll(profile, line, key));
    }

    [Fact]
    public async Task A_saved_line_lists_the_words_it_was_saved_under_so_re_roll_finds_its_take()
    {
        // With "Shorten published names" on, the announcer saves a cloud
        // voice's take under the SHORTENED line (AnnouncementService.
        // ForCloudVoice). The list must hand back those same words, or
        // Re-roll would key, send and save the full names instead and
        // never find the take the race plays.
        var folder = Path.Combine(Path.GetTempPath(), "hc-reroll-" + Guid.NewGuid().ToString("N"));

        try
        {
            var library = new VoiceLibrary(folder);
            var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "m1" };
            const string sent = "Sam S. leads from Bill T.";

            var audio = new byte[512];
            audio[0] = (byte)'I';
            audio[1] = (byte)'D';
            audio[2] = (byte)'3';

            library.Save(profile, sent, audio);

            IReadOnlyList<VoiceLibrary.SavedLine> recent = Array.Empty<VoiceLibrary.SavedLine>();

            // Saved in the background, audio first and its words after.
            for (var i = 0; i < 100 && (recent.Count == 0 || recent[0].Text != sent); i++)
            {
                await Task.Delay(50);
                recent = library.Recent(5);
            }

            var line = Assert.Single(recent);
            Assert.Equal(sent, line.Text);
            Assert.Equal(VoiceLibrary.KeyFor(profile, sent), line.Key);
            Assert.Null(VoiceSettingsControl.WhyNoReroll(profile, line, VoiceLibrary.KeyFor(profile, line.Text)));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void Kokoro_has_no_saved_take_to_re_roll()
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.Kokoro };
        var line = new VoiceLibrary.SavedLine("k", "Green flag!", "Kokoro", DateTime.UtcNow, 100);

        Assert.NotNull(VoiceSettingsControl.WhyNoReroll(profile, line, "k"));
    }

    [Fact]
    public void The_preview_cap_is_no_higher_than_the_relays()
    {
        // The relay's VOICE_DEFAULT_PREVIEW_MAX_CHARS.
        Assert.True(HeatCallerVoiceClient.MaxPreviewCharacters <= 200);
    }
}
