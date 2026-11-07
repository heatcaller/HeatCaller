namespace HeatCaller.Tests;

/// <summary>
/// The known issues closed on 7 Nov 2026: Re-roll and the shortened names,
/// who can still win a championship after the flag, the visitor tick and the
/// season's own note at the night's bookends, the record watch's segment, and
/// the relay board's hand-counted mark.
/// </summary>
public sealed class KnownIssues4Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Stock = "Box Stock";

    private static string Id(string name) => name.ToLowerInvariant();

    // --- Re-roll refuses a take the race no longer asks for ---------------

    [Fact]
    public void A_take_with_full_names_the_race_now_shortens_is_not_re_rolled()
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "m1" };
        var key = VoiceLibrary.KeyFor(profile, "Sam Smith leads.");
        var line = new VoiceLibrary.SavedLine(key, "Sam Smith leads.", "Voice", DateTime.UtcNow, 100);

        var why = VoiceSettingsControl.WhyNoReroll(profile, line, key, textTheRaceSends: "Sam S. leads.");

        Assert.NotNull(why);
        Assert.Contains("Forget Line", why);
    }

    [Fact]
    public void A_take_whose_words_the_shortening_leaves_alone_can_still_be_re_rolled()
    {
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "m1" };
        var key = VoiceLibrary.KeyFor(profile, "Green flag!");
        var line = new VoiceLibrary.SavedLine(key, "Green flag!", "Voice", DateTime.UtcNow, 100);

        Assert.Null(VoiceSettingsControl.WhyNoReroll(profile, line, key, textTheRaceSends: "Green flag!"));
    }

    [Fact]
    public void The_voice_guard_names_the_radio_button_as_it_reads()
    {
        var source = File.ReadAllText(Path.Combine(SourceFolder(), "AnnouncementService.Synthesis.cs"));

        Assert.DoesNotContain("\\\"My own ElevenLabs account\\\"", source);
        Assert.Contains("ElevenLabs - natural voices from your own ElevenLabs account", source);
    }

    // --- Who can still win it, after the flag -----------------------------

    private sealed record Club(
        SeasonStore Seasons,
        RoundDefinitionStore Rounds,
        RaceEventStore Days,
        SessionHistoryStore History,
        ProfileStore Profiles,
        RoundDefinition Tonight)
    {
        /// <summary>Tonight's race finishing Bill, Sam, Carl, Dot - asked as the after-race check asks.</summary>
        public IReadOnlyList<ChampionshipStanding> After() =>
            CommentaryReaders.ChampionshipAfter(
                new[] { "Bill", "Sam", "Carl", "Dot" }
                    .Select((name, place) => new SessionResultEntry
                    {
                        Transponder = Id(name),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
                Tonight, Seasons, Rounds, Days, History, Profiles);
    }

    /// <summary>
    /// Four rounds of Bill, Sam, Carl, Dot on the app's 10-down-to-1 ladder,
    /// one race day each, and tonight's A-main on a day of its own.
    /// </summary>
    private Club Setup(int planned, bool bMainTonight = false)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Bill", "Sam", "Carl", "Dot" })
        {
            profiles.Upsert(new DriverProfile { Transponder = Id(name), DriverName = name });
        }

        var rounds = RoundDefinitionStore.Load();
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 4; i++)
        {
            var past = new RoundDefinition { ClassName = Stock, RoundLabel = "A-Main", Kind = RoundKind.Main };
            var race = new SessionRecord
            {
                Id = "r" + i,
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = past.Id,
                StartedAtUtc = when.AddDays(7 * i),
                EndedAtUtc = when.AddDays(7 * i).AddMinutes(8),
                Results = new[] { "Bill", "Sam", "Carl", "Dot" }
                    .Select((name, place) => new SessionResultEntry
                    {
                        Transponder = Id(name),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
            };

            past.LinkedSessionId = race.Id;
            rounds.Add(past);
            races.Add(race);

            events.Upsert(new RaceEvent
            {
                Name = "Round " + (i + 1),
                StartedUtc = when.AddDays(7 * i),
                FinishedUtc = when.AddDays(7 * i).AddHours(4),
                RoundIds = new List<string> { past.Id },
                ClassNames = new List<string> { Stock },
            });
        }

        var tonight = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            AssignedTransponders = new[] { "Bill", "Sam", "Carl", "Dot" }.Select(Id).ToList(),
        };

        rounds.Add(tonight);

        var tonightsRounds = new List<string> { tonight.Id };

        if (bMainTonight)
        {
            var bMain = new RoundDefinition { ClassName = Stock, RoundLabel = "B-Main", Kind = RoundKind.Main, MainTier = 1 };
            rounds.Add(bMain);
            tonightsRounds.Add(bMain.Id);
        }

        events.Upsert(new RaceEvent
        {
            Name = "Tonight",
            StartedUtc = when.AddDays(28),
            RoundIds = tonightsRounds,
            ClassNames = new List<string> { Stock },
        });

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            PlannedRounds = planned,
            PointsScheme = Enumerable.Range(1, 10)
                .Select(p => new PointsSchemeEntry { Position = p, Points = 11 - p })
                .ToList(),
        });

        return new Club(seasons, rounds, events, SessionHistoryStore.FromSessions(races), profiles, tonight);
    }

    [Fact]
    public void After_the_last_round_the_leader_is_the_only_one_still_in_it()
    {
        // Bill 50, Sam 45 after tonight, and tonight was round five of five.
        var table = Setup(planned: 5).After();

        Assert.Equal("Bill", table[0].Name);
        Assert.True(table[0].StillAlive);
        Assert.All(table.Skip(1), row => Assert.False(row.StillAlive));

        var said = CommentaryReportFacts.ChampionshipCheck(table, Stock, "heat-1");

        Assert.Contains(said, block => block.Text.Contains("nobody can catch Bill", StringComparison.Ordinal));
    }

    [Fact]
    public void With_a_round_left_the_ones_who_can_reach_the_leader_are_still_in_it()
    {
        // Bill 50, Sam 45, Carl 40, Dot 35, a win worth ten: Sam can pass
        // him, Carl can draw level - and level is not beaten - and Dot cannot.
        var table = Setup(planned: 6).After();

        Assert.Equal(new[] { true, true, true, false }, table.Select(row => row.StillAlive).ToArray());

        var said = CommentaryReportFacts.ChampionshipCheck(table, Stock, "heat-1");

        Assert.Contains(said, block => block.Text.StartsWith("3 drivers can still win", StringComparison.Ordinal));
        Assert.DoesNotContain(said, block => block.Key.StartsWith("champdecided", StringComparison.Ordinal));
    }

    [Fact]
    public void A_season_with_no_length_typed_claims_nothing_after_the_flag()
    {
        var table = Setup(planned: 0).After();

        Assert.NotEmpty(table);
        Assert.All(table, row => Assert.True(row.StillAlive));

        var said = CommentaryReportFacts.ChampionshipCheck(table, Stock, "heat-1");

        Assert.DoesNotContain(said, block => block.Key.StartsWith("champdecided", StringComparison.Ordinal)
                                          || block.Key.StartsWith("champalive", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_round_tonight_the_season_counts_leaves_it_unproved()
    {
        // A B-main still to run tonight would also count, and the proof
        // prices each round as one race - so it says nothing rather than a
        // false "that settles it".
        var table = Setup(planned: 5, bMainTonight: true).After();

        Assert.All(table, row => Assert.True(row.StillAlive));
    }

    // --- The night's bookends: visitors and the season's own note ---------

    private (RoundDefinitionStore Rounds, ProfileStore Profiles, SeasonStore Seasons, RaceEventStore Days) Tonight(
        string note, string seasonClass = Stock)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "a", DriverName = "Ann" });
        profiles.Upsert(new DriverProfile { Transponder = "b", DriverName = "Bob", Visitor = true });
        profiles.Upsert(new DriverProfile { Transponder = "c", DriverName = "Cat", Visitor = true });
        profiles.Upsert(new DriverProfile { Transponder = "d", DriverName = "Dan" });

        var rounds = RoundDefinitionStore.Load();
        rounds.Add(new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            AssignedTransponders = new List<string> { "a", "b", "c", "d" },
        });

        var seasons = SeasonStore.Load();
        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = seasonClass,
            AutoInclude = true,
            MainsOnly = true,
            AnnouncerNote = note,
        });

        return (rounds, profiles, seasons, RaceEventStore.Load());
    }

    [Fact]
    public void The_cold_open_counts_the_drivers_the_club_ticked_as_visiting()
    {
        var (rounds, profiles, seasons, days) = Tonight(note: string.Empty);

        var night = CommentaryReaders.NightAhead(rounds, null, null, profiles, seasons, days);

        Assert.Equal(2, night.TravelledFrom);
        Assert.Contains("2 of them visiting", CommentaryShowFacts.ColdOpen(night, "day-1")!.Text);
    }

    [Fact]
    public void The_cold_open_reads_the_seasons_own_note_as_typed()
    {
        var (rounds, profiles, seasons, days) = Tonight(note: "Two rounds left and four points in it.");

        var night = CommentaryReaders.NightAhead(rounds, null, null, profiles, seasons, days);

        Assert.Equal("Two rounds left and four points in it.", night.ChampionshipNote);
        Assert.EndsWith("Two rounds left and four points in it.", CommentaryShowFacts.ColdOpen(night, "day-1")!.Text);
    }

    [Fact]
    public void A_note_on_a_season_tonight_does_not_count_towards_says_nothing()
    {
        var (rounds, profiles, seasons, days) = Tonight(note: "Two rounds left.", seasonClass: "Open Mod");

        Assert.Null(CommentaryReaders.NightAhead(rounds, null, null, profiles, seasons, days).ChampionshipNote);
    }

    [Fact]
    public void The_sign_off_takes_the_note_of_a_season_one_of_tonights_races_counts_towards()
    {
        var (rounds, _, seasons, days) = Tonight(note: "The title goes to the last round.");
        var main = rounds.All.Single();

        var race = new SessionRecord { Id = "tonight", SessionType = RaceSessionType.Race, RoundId = main.Id };

        Assert.Equal("The title goes to the last round.", CommentaryReaders.SeasonNoteFor(seasons, rounds, days, new[] { race }));
    }

    [Fact]
    public void A_season_note_fits_what_the_announcer_keeps()
    {
        var note = new string('x', Season.AnnouncerNoteMaxLength);

        Assert.Equal(note, CommentaryFacts.CleanLong(note));
    }

    // --- The record watch has a segment of its own ------------------------

    [Fact]
    public void The_record_watch_is_a_live_segment_with_a_tick_box()
    {
        Assert.True(CommentaryDirector.InRace(SegmentKind.RecordWatch));
        Assert.Equal("Record in danger", CommentaryBudget.Spec(SegmentKind.RecordWatch).Title);
    }

    [Fact]
    public void The_record_watch_fact_is_the_consoles_own_sentence()
    {
        var threat = new RecordThreat("bob", RecordThreatLevel.OneMoreLikeThat, 12.0, 12.0, "Jen", 0, 2, 6, true);

        var block = CommentaryFacts.RecordThreat(threat, _ => "Bob", "heat-1");

        Assert.NotNull(block);
        Assert.Equal(RecordWatch.Say(threat, "Bob"), block!.Text);
        Assert.Contains(":OneMoreLikeThat:", block.Key);
    }

    // --- The relay's board says what the * means --------------------------

    [Fact]
    public void The_relay_copy_carries_the_mark_and_the_relay_draws_the_legend()
    {
        var kpi = File.ReadAllText(Path.Combine(SourceFolder(), "LeaderboardKpi.cs"));
        var server = File.ReadAllText(Path.Combine(SourceFolder(), "WebServerService.cs"));
        var pages = File.ReadAllText(Path.Combine(SourceFolder(), "..", "..", "relay", "src", "pages.ts"));

        Assert.DoesNotContain("ctx.PublicCopy ? 0 : HandCountedLaps.During(s)", kpi);
        Assert.Contains("relayNode[\"handCounted\"] = true;", server);
        Assert.Contains("state.handCounted === true ? '<p class=\"hint\">* includes laps counted by hand</p>'", pages);
    }

    /// <summary>The app's source folder, found by walking up to check.cmd.</summary>
    private static string SourceFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }
}
