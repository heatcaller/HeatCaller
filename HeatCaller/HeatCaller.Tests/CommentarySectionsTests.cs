namespace HeatCaller.Tests;

/// <summary>
/// AI Commentary's Simple view (1 Nov 2026, Simple and Advanced mode, round
/// 3): the six section switches over the tab's own settings - off remembers
/// and on puts back, on with nothing remembered is how HeatCaller ships, a
/// piece that needs a Cast voice waits for it, a section set by hand reads
/// as custom, and After the flag says when it needs the recaps.
/// </summary>
public sealed class CommentarySectionsTests
{
    private static bool NoVoices(CastNeed need) => need == CastNeed.None;

    private static bool AllVoices(CastNeed need) => true;

    // --- The map --------------------------------------------------------------------

    [Fact]
    public void Every_section_has_pieces_and_every_piece_is_in_one_section()
    {
        foreach (var section in Enum.GetValues<CommentarySection>())
        {
            Assert.NotEmpty(CommentarySections.In(section));
        }

        Assert.Equal(CommentarySections.All.Count, CommentarySections.All.Select(piece => piece.Key).Distinct().Count());
    }

    [Fact]
    public void The_pieces_are_the_plans()
    {
        string[] Keys(CommentarySection section) => CommentarySections.In(section).Select(piece => piece.Key).ToArray();

        Assert.Equal(
            new[] { "CommentaryRaceIntros", "CommentaryDriverIntros", "CommentaryQualifyingRecap", "PrincipalBeforeRace" },
            Keys(CommentarySection.BeforeTheRace));
        Assert.Equal(
            new[] { "BattleWatch", "PaceNote", "Horizon", "ComingUpNext", "ChampionshipCheck" },
            Keys(CommentarySection.DuringTheRace));
        Assert.Equal(
            new[] { "FormGuide", "QualifyingReport", "GridHistory", "ClubAside", "RoomVerdict" },
            Keys(CommentarySection.BetweenRaces));
        Assert.Equal(
            new[] { "FlagResultRead", "RecapBadgeHonours", "RecapMechanicInterview", "SummaryTeaserEnabled", "RecapAdBreak" },
            Keys(CommentarySection.AfterTheFlag));
        Assert.Equal(
            new[] { "CommentaryRaceSummaries", "CommentarySummaryMentionFastestLap", "RecapBroadcastOpening", "PrincipalAfterRace", "RecapBroadcastClosing" },
            Keys(CommentarySection.RecapShow));
        Assert.Equal(
            new[] { "ColdOpen", "ThisDayInHistory", "SignOffNumbers", "MidMeetingStandings" },
            Keys(CommentarySection.OpeningAndClosing));
    }

    [Fact]
    public void A_settings_piece_reads_and_writes_its_own_setting()
    {
        var settings = new AppSettings();
        var piece = CommentarySections.All.Single(p => p.Key == nameof(AppSettings.RecapAdBreak));

        piece.Set(settings, true);

        Assert.True(settings.RecapAdBreak);
        Assert.True(piece.Get(settings));
    }

    [Fact]
    public void A_segment_piece_is_on_while_it_is_not_switched_off()
    {
        var settings = new AppSettings();
        var piece = CommentarySections.All.Single(p => p.Key == nameof(SegmentKind.FormGuide));

        Assert.True(piece.Get(settings));

        piece.Set(settings, false);

        Assert.Contains(SegmentKind.FormGuide, CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff));
        Assert.False(piece.Get(settings));
    }

    // --- Off and on -----------------------------------------------------------------

    [Fact]
    public void Off_turns_every_piece_off()
    {
        var settings = new AppSettings();

        CommentarySections.Set(settings, CommentarySection.DuringTheRace, on: false, NoVoices);

        Assert.False(CommentarySections.IsOn(settings, CommentarySection.DuringTheRace));
        Assert.All(CommentarySections.In(CommentarySection.DuringTheRace), piece => Assert.False(piece.Get(settings)));

        // The other sections are left alone.
        Assert.True(CommentarySections.IsOn(settings, CommentarySection.BetweenRaces));
    }

    [Fact]
    public void Off_and_on_again_puts_back_exactly_what_was_on()
    {
        var settings = new AppSettings { CommentaryRaceIntros = false, CommentaryQualifyingRecap = true, CommentaryDriverIntros = true };

        CommentarySections.Set(settings, CommentarySection.BeforeTheRace, on: false, NoVoices);
        CommentarySections.Set(settings, CommentarySection.BeforeTheRace, on: true, NoVoices);

        Assert.False(settings.CommentaryRaceIntros);
        Assert.True(settings.CommentaryDriverIntros);
        Assert.True(settings.CommentaryQualifyingRecap);
        Assert.Empty(settings.CommentarySectionsRemembered);
    }

    [Fact]
    public void Off_twice_does_not_forget()
    {
        var settings = new AppSettings { RecapBroadcastOpening = true };

        CommentarySections.Set(settings, CommentarySection.RecapShow, on: false, NoVoices);
        CommentarySections.Set(settings, CommentarySection.RecapShow, on: false, NoVoices);
        CommentarySections.Set(settings, CommentarySection.RecapShow, on: true, NoVoices);

        Assert.True(settings.RecapBroadcastOpening);
        Assert.True(settings.CommentaryRaceSummaries);
    }

    [Fact]
    public void On_with_nothing_remembered_is_how_heatcaller_ships_it()
    {
        // Every piece off, as an Advanced user might leave a section.
        var settings = new AppSettings
        {
            FlagResultRead = false,
            SummaryTeaserEnabled = false,
            RecapAdBreak = true,
        };

        CommentarySections.Set(settings, CommentarySection.AfterTheFlag, on: true, NoVoices);

        var shipped = new AppSettings();

        Assert.Equal(shipped.FlagResultRead, settings.FlagResultRead);
        Assert.Equal(shipped.SummaryTeaserEnabled, settings.SummaryTeaserEnabled);
        Assert.Equal(shipped.RecapAdBreak, settings.RecapAdBreak);
    }

    [Fact]
    public void A_piece_that_needs_a_cast_voice_waits_for_it()
    {
        var settings = new AppSettings { PrincipalAfterRace = false, CommentaryRaceSummaries = false, CommentarySummaryMentionFastestLap = false };

        CommentarySections.Set(settings, CommentarySection.RecapShow, on: true, NoVoices);
        Assert.False(settings.PrincipalAfterRace);
        Assert.True(settings.CommentaryRaceSummaries);

        settings.CommentarySectionsRemembered.Clear();
        CommentarySections.Set(settings, CommentarySection.RecapShow, on: false, NoVoices);
        settings.CommentarySectionsRemembered.Clear();
        CommentarySections.Set(settings, CommentarySection.RecapShow, on: true, AllVoices);

        Assert.True(settings.PrincipalAfterRace);
    }

    [Fact]
    public void A_piece_waiting_for_a_voice_is_left_as_it_was()
    {
        // On by hand, with no voice set: the switch does not turn it off.
        var settings = new AppSettings { PrincipalBeforeRace = true };

        CommentarySections.Set(settings, CommentarySection.BeforeTheRace, on: true, NoVoices);

        Assert.True(settings.PrincipalBeforeRace);
    }

    // --- Custom, and After the flag --------------------------------------------------

    [Fact]
    public void A_new_settings_file_reads_as_shipped_not_custom()
    {
        var settings = new AppSettings();

        foreach (var section in Enum.GetValues<CommentarySection>().Where(s => CommentarySections.IsOn(new AppSettings(), s)))
        {
            Assert.False(CommentarySections.IsCustom(settings, section, NoVoices), section.ToString());
        }
    }

    [Fact]
    public void A_section_set_piece_by_piece_reads_as_custom_and_comes_back_custom()
    {
        var settings = new AppSettings();
        var off = CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff).Append(SegmentKind.Horizon);
        settings.CommentarySegmentsOff = CommentaryBudget.SaveSwitches(off);

        Assert.True(CommentarySections.IsCustom(settings, CommentarySection.DuringTheRace, NoVoices));

        CommentarySections.Set(settings, CommentarySection.DuringTheRace, on: false, NoVoices);
        CommentarySections.Set(settings, CommentarySection.DuringTheRace, on: true, NoVoices);

        Assert.Contains(SegmentKind.Horizon, CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff));
        Assert.True(CommentarySections.IsCustom(settings, CommentarySection.DuringTheRace, NoVoices));
    }

    [Fact]
    public void After_the_flag_says_when_it_needs_the_recaps()
    {
        var settings = new AppSettings();

        Assert.False(CommentarySections.AfterTheFlagNeedsRecaps(settings));

        CommentarySections.Set(settings, CommentarySection.RecapShow, on: false, NoVoices);

        Assert.True(CommentarySections.AfterTheFlagNeedsRecaps(settings));
    }

    [Fact]
    public void What_is_remembered_survives_a_save_and_load()
    {
        var settings = new AppSettings();

        CommentarySections.Set(settings, CommentarySection.OpeningAndClosing, on: false, NoVoices);

        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;

        CommentarySections.Set(back, CommentarySection.OpeningAndClosing, on: true, NoVoices);

        Assert.True(CommentarySections.IsOn(back, CommentarySection.OpeningAndClosing));
        Assert.All(CommentarySections.In(CommentarySection.OpeningAndClosing), piece => Assert.True(piece.Get(back)));
    }
}
