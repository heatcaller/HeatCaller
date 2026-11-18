using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Simple mode's hiding (1 Nov 2026, Simple and Advanced mode, round 2): the
/// tags and the one pass that shows or hides them, tabs going back where
/// they were, the "More settings" line, the search's Advanced row, the race
/// calls switches - and every card and tab in the app put in a group by
/// someone, so a new one fails here until it is.
/// </summary>
public sealed class SimpleModeTests : IDisposable
{
    // Declared first: the race call tests load their stores from where it points.
    private readonly TempAppData _data = new();

    // Every test leaves the mode as a window-less test expects it: Advanced.
    public void Dispose()
    {
        SimpleMode.Apply(true);
        SimpleMode.Reset();
        _data.Dispose();
    }

    // --- The tags -------------------------------------------------------------------

    [Fact]
    public void Without_a_window_everything_shows()
    {
        Assert.True(SimpleMode.ShowAll);
        Assert.True(SimpleMode.Hide(new Label()).Visible);
    }

    [Fact]
    public void A_tagged_part_hides_in_simple_and_comes_back_in_advanced()
    {
        var card = SimpleMode.Hide(new Panel());

        SimpleMode.Apply(false);
        Assert.False(card.Visible);

        SimpleMode.Apply(true);
        Assert.True(card.Visible);
    }

    [Fact]
    public void A_part_tagged_in_simple_starts_hidden()
    {
        SimpleMode.Apply(false);

        Assert.False(SimpleMode.Hide(new Button()).Visible);
    }

    [Fact]
    public void A_part_with_a_reason_of_its_own_shows_only_when_both_say_so()
    {
        var wanted = false;
        var tick = SimpleMode.Hide(new CheckBox(), own: () => wanted);

        Assert.False(tick.Visible);

        wanted = true;
        SimpleMode.Refresh(tick);
        Assert.True(tick.Visible);

        SimpleMode.Apply(false);
        Assert.False(tick.Visible);
    }

    [Fact]
    public void A_simple_only_part_is_the_other_way_round()
    {
        var card = SimpleMode.ShowInSimpleOnly(new Panel());

        Assert.False(card.Visible);

        SimpleMode.Apply(false);
        Assert.True(card.Visible);

        SimpleMode.Apply(true);
        Assert.False(card.Visible);
    }

    [Fact]
    public void The_more_settings_line_shows_in_simple_only()
    {
        var line = SimpleMode.MoreLine();

        Assert.False(line.Visible);
        Assert.Equal(SimpleMode.MoreLineText, line.Text);

        SimpleMode.Apply(false);
        Assert.True(line.Visible);

        SimpleMode.Apply(true);
        Assert.False(line.Visible);
    }

    [Fact]
    public void The_more_settings_line_asks_for_advanced()
    {
        var asked = 0;
        SimpleMode.AskForAdvanced = () => asked++;

        var line = (LinkLabel)SimpleMode.MoreLine();
        typeof(LinkLabel)
            .GetMethod("OnLinkClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(line, new object[] { new LinkLabelLinkClickedEventArgs(new LinkLabel.Link()) });

        Assert.Equal(1, asked);
    }

    // --- A whole row ----------------------------------------------------------------

    [Fact]
    public void A_row_is_its_caption_its_field_and_its_hints()
    {
        var grid = new FieldGrid();
        var first = new TextBox();
        var second = new NumericUpDown();
        var third = new TextBox();

        grid.AddRow("First:", first);
        var caption = grid.AddRow("Second:", FieldGrid.Inline(second, new Label { Text = "laps" }));
        var hint = grid.AddHint("What the second one does.");
        grid.AddRow("Third:", third);

        var parts = grid.RowParts(second);

        Assert.Contains(caption, parts);
        Assert.Contains(second.Parent!, parts);
        Assert.Contains(hint, parts);
        Assert.DoesNotContain(first, parts);
        Assert.DoesNotContain(third, parts);
        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public void A_wide_row_is_just_itself()
    {
        var grid = new FieldGrid();
        var tick = grid.AddWide(new CheckBox { Text = "Leader takes the flag" }, stretch: false);
        grid.AddRow("After:", new TextBox());

        Assert.Equal(new Control[] { tick }, grid.RowParts(tick));
    }

    [Fact]
    public void A_field_in_another_grid_is_no_row_of_this_one()
    {
        Assert.Empty(new FieldGrid().RowParts(new TextBox()));
    }

    // --- Tabs -----------------------------------------------------------------------

    [Fact]
    public void A_hidden_tab_goes_and_comes_back_in_its_own_place()
    {
        using var tabs = new TabControl();
        var mixer = new TabPage("Mixer");
        var imaging = new TabPage("Imaging");
        var music = new TabPage("Music");
        var beeps = new TabPage("Lap Beeps");

        tabs.TabPages.AddRange(new[] { mixer, imaging, music, beeps });

        SimpleMode.HideTab(tabs, imaging);
        SimpleMode.HideTab(tabs, beeps);

        SimpleMode.Apply(false);
        Assert.Equal(new[] { mixer, music }, tabs.TabPages.Cast<TabPage>());

        SimpleMode.Apply(true);
        Assert.Equal(new[] { mixer, imaging, music, beeps }, tabs.TabPages.Cast<TabPage>());
    }

    [Fact]
    public void A_hidden_tab_is_disposed_with_its_control()
    {
        // Out of the control in Simple, so the window closing would not
        // reach it - the layout test ran out of window handles until it did.
        var tabs = new TabControl();
        var mixer = new TabPage("Mixer");
        var imaging = new TabPage("Imaging");

        tabs.TabPages.AddRange(new[] { mixer, imaging });
        SimpleMode.HideTab(tabs, imaging);
        SimpleMode.Apply(false);

        tabs.Dispose();

        Assert.True(imaging.IsDisposed);
        Assert.True(mixer.IsDisposed);
    }

    [Fact]
    public void The_tab_showing_is_kept_unless_it_goes()
    {
        using var tabs = new TabControl();
        var mixer = new TabPage("Mixer");
        var imaging = new TabPage("Imaging");
        var music = new TabPage("Music");

        tabs.TabPages.AddRange(new[] { mixer, imaging, music });
        SimpleMode.HideTab(tabs, imaging);

        tabs.SelectedTab = imaging;
        SimpleMode.Apply(false);

        Assert.Same(mixer, tabs.SelectedTab);
    }

    // --- The sidebar's search -------------------------------------------------------

    [Fact]
    public void A_search_for_a_hidden_setting_offers_advanced_in_simple_only()
    {
        Assert.False(SimpleMode.SearchFindsHidden("ducking"));

        SimpleMode.Apply(false);

        Assert.True(SimpleMode.SearchFindsHidden("ducking"));
        Assert.True(SimpleMode.SearchFindsHidden("Split Loop"));
        Assert.False(SimpleMode.SearchFindsHidden("du"));
        Assert.False(SimpleMode.SearchFindsHidden("leaderboard"));
    }

    [Fact]
    public void Venues_is_the_page_simple_hides()
    {
        Assert.Equal(new[] { "Venues" }, SimpleMode.HiddenPages);
    }

    // --- Race calls on or off -------------------------------------------------------

    [Fact]
    public void Race_calls_are_on_by_default_and_in_older_files()
    {
        Assert.True(new VoiceProfile().RaceCallsInRaces);
        Assert.True(new VoiceProfile().RaceCallsInPractice);

        var older = System.Text.Json.JsonSerializer.Deserialize<VoiceProfile>("{\"MaxAnnouncementsPerLap\":2}")!;

        Assert.True(older.RaceCallsInRaces);
        Assert.True(older.RaceCallsInPractice);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_switch_silences_a_race_call_and_on_again_it_is_back(bool on)
    {
        var said = SaidAfterAStreak(raceCallsInRaces: on);

        if (on)
        {
            Assert.Contains(said, t => t.Contains("Sam has the longest run going", StringComparison.Ordinal));
        }
        else
        {
            Assert.DoesNotContain(said, t => t.Contains("Sam has the longest run going", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// What an announcer with only the longest-run call on says when Sam
    /// takes it in a race, with the Race calls switch for races as given -
    /// as BugHunt4PracticeGhostTests builds its announcer. The row stays
    /// ticked either way: the switch is a gate over it.
    /// </summary>
    private static List<string> SaidAfterAStreak(bool raceCallsInRaces)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });

        var voice = VoiceSettingsStore.Load();

        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        voice.Current.Events[nameof(AnnouncementEventType.StreakLead)] =
            new AnnouncementEventConfig { Enabled = true, Templates = { "{driver} has the longest run going" } };
        voice.Current.RaceCallsInRaces = raceCallsInRaces;

        var sounds = SoundEffectsStore.Load();

        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var said = new List<string>();

        announcer.AnnouncementTextReady += text =>
        {
            lock (said)
            {
                said.Add(text);
            }
        };

        announcer.OnGatePassed(new GatePassEvent
        {
            Transponder = "101",
            Gate = "1",
            GateType = "finish",
            Valid = true,
            Streak = true,
            StreakCountOverride = 5,
            LapTimeMs = 10_000,
        });

        // The per-lap batch waits 300ms before it decides; a call that is on
        // arrives well inside this, and one that is off never does.
        var deadline = DateTime.UtcNow.AddSeconds(raceCallsInRaces ? 3 : 1.5);

        while (DateTime.UtcNow < deadline)
        {
            lock (said)
            {
                if (said.Count > 0)
                {
                    break;
                }
            }

            Thread.Sleep(50);
        }

        lock (said)
        {
            return said.ToList();
        }
    }

    [Fact]
    public void Every_race_call_asks_the_switch()
    {
        // The rows are read in one place per call; each of those reads goes
        // through CallOn, which asks the switch. One left asking the row
        // alone would be a call the switch cannot silence. Sound effects
        // (_soundStore) are not race calls and are left as they were.
        var folder = SourceFolder();

        var offenders = Directory.GetFiles(folder, "AnnouncementService*.cs")
            .Where(file => !file.EndsWith("AnnouncementService.Sounds.cs", StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file).Select((line, i) => (file, line, i)))
            .Where(x => x.line.Contains(".IsEnabledFor(IsPracticeMode)", StringComparison.Ordinal))
            // CallOn itself, which asks the row and then the switch.
            .Where(x => !x.line.Contains("RaceCallsOn(IsPracticeMode)", StringComparison.Ordinal))
            .Where(x => !IsSoundEffect(x.file, x.i))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0, "Asks a race call's row without the Race calls switch: " + string.Join(", ", offenders));
    }

    /// <summary>A line that reads a sound effect's config - within a few lines of where it was taken from _soundStore, or typed as one.</summary>
    private static bool IsSoundEffect(string file, int index)
    {
        var lines = File.ReadAllLines(file);
        var from = Math.Max(0, index - 8);

        return lines.Skip(from).Take(index - from + 1)
            .Any(line => line.Contains("_soundStore", StringComparison.Ordinal) || line.Contains("SoundEffectConfig", StringComparison.Ordinal));
    }

    // --- Every card and tab is in a group -------------------------------------------

    /// <summary>
    /// The cards and tabs Simple mode hides, as "File|Caption". Each is
    /// tagged where it is built (SimpleMode.Hide / HideTab).
    /// </summary>
    private static readonly string[] Hidden =
    {
        // Sounds.
        "MainForm.Pages.cs|Imaging", "MainForm.Pages.cs|Effects", "MainForm.Pages.cs|Ambience", "MainForm.Pages.cs|Lap Beeps",
        "MixerControl.cs|Imaging levels", "MixerControl.cs|Ducking", "MixerControl.cs|When a session starts", "MixerControl.cs|When it ends",
        "MusicControl.cs|Behavior",

        // Voice, Cast.
        "MainForm.Pages.cs|Race Calls",
        "VoiceSettingsControl.cs|Voice parameters", "VoiceSettingsControl.cs|Saved voice lines",
        "CastControl.cs|Recap host", "CastControl.cs|Technical Lead", "CastControl.cs|Team Principal", "CastControl.cs|Drivers' own crew",
        "CastControl.cs|Achievements reader",

        // Connection.
        "MainForm.ConnectionPage.cs|Developer console", "MainForm.ConnectionPage.cs|Split loop",

        // Sponsors, Local Screens, Club Website.
        "SponsorsControl.cs|Rotation timing", "SponsorsControl.cs|Video sound",
        "WebServerControl.cs|Race-day manager (phone)",
        "MainForm.Pages.cs|Recording",
        "RelaySettingsControl.cs|What gets published", "RelaySettingsControl.cs|Look", "RelaySettingsControl.cs|Personal PINs",
        "RelaySettingsControl.cs|Email (for racer PINs)", "RelaySettingsControl.cs|Race-day manager",
        "RelaySettingsControl.Video.cs|Race video (YouTube)",
        "RoomAndWebCard.cs|Room & Web",

        // AI Commentary (round 3): every card, in place of the section switches.
        "CommentaryControl.cs|How it's written", "CommentaryControl.cs|Who writes it", "CommentaryControl.cs|Before the start",
        "CommentaryControl.cs|Through the night", "CommentaryControl.cs|After the flag", "CommentaryControl.cs|Recap show",
        "BetweenRacesCard.cs|Between races",
    };

    /// <summary>
    /// The cards and tabs shown in Simple mode - on its own page, or on a
    /// tab, page or window that Simple hides as a whole (Ambience's cards,
    /// Imaging's, Venues', a dialog only Advanced opens), where the card
    /// itself needs no tag. AI Commentary's cards are round 3's.
    /// </summary>
    private static readonly string[] Shown =
    {
        // Race Day, Championship, Analytics and the race-night pages.
        "MainForm.Pages.cs|Next Steps", "MainForm.Pages.cs|Setup", "MainForm.Pages.cs|Who's Racing", "MainForm.Pages.cs|Standings",
        "MainForm.Pages.cs|Calendar", "MainForm.Pages.cs|Overview", "MainForm.Pages.cs|Personal Bests", "MainForm.Pages.cs|Lap Times",
        "MainForm.Pages.cs|Head to Head", "MainForm.Pages.cs|Trends", "MainForm.Pages.cs|Pace",
        "RaceDayEventControl.cs|Which race day", "RaceDayEventControl.cs|The night", "RaceDayEventControl.cs|Times",
        "RaceDayEventControl.cs|Heats", "RaceDayEventControl.cs|Money", "RaceDayEventControl.cs|Running the night", "RaceDayEventControl.cs|The heats",
        "RaceDayEventControl.cs|Marshals", "RaceDayEventControl.cs|Papers", "RaceDayEventControl.cs|End of the night", "RaceDayEventControl.cs|Commentary",
        "TodayControl.cs|Get back on time", "TodayControl.cs|Go / no-go", "RaceProgramControl.cs|Tonight's run sheet", "EventsControl.cs|Race days",
        "EventsControl.cs|Nights", "CheckInControl.cs|Drivers", "CheckInControl.Desk.cs|Scan to Check In", "CheckInControl.Desk.cs|Tonight at the Desk",
        "SeasonsControl.cs|Championships", "SeasonsControl.cs|Races counted", "SeasonsControl.cs|Standings", "SeasonsControl.cs|Points scheme", "SeasonsControl.cs|Can they still win?", "SeasonsControl.cs|Scoring rules", "SeasonsControl.cs|Points carried in",
        "AnalyticsOverviewControl.cs|From the last race night",
        "AnalyticsOverviewControl.cs|Track records", "PersonalBestsControl.cs|Personal bests", "LapChartControl.cs|Drivers",
        "LapChartControl.cs|Lap times", "LapChartControl.cs|Consistency", "LapChartControl.cs|Three things in this race", "HeadToHeadControl.cs|Matchup",
        "HeadToHeadControl.cs|Career comparison (shared sessions only)", "HeadToHeadControl.cs|Session by session",
        "HeadToHeadControl.cs|Who beats whom",
        "TrendChartControl.cs|Trend", "RaceHistoryControl.cs|Sessions", "RaceHistoryControl.cs|Results",
        "TrendChartControl.cs|Where you are on the curve", "PaceAnalysisControl.cs|Everyone's verdict", "PaceAnalysisControl.cs|All the numbers",
        "RaceControlControl.Layout.cs|Leaderboard", "MainForm.Designer.cs|Session",
        "PracticeModeControl.cs|Setup", "PracticeModeControl.cs|This session", "PracticeModeControl.cs|Ghost cars",
        "GhostControl.cs|Your ghosts", "DemoModeControl.cs|Settings", "DemoModeControl.cs|Racers", "DemoModeControl.cs|Log",
        "TrackLayoutForms.cs|Track layouts", "TrackLayoutForms.cs|Selected track",
        "HandicapControl.cs|Drivers", "RentalTransponderControl.cs|Transponders",
        "HandicapControl.cs|Class and mode",

        // Voice and Cast.
        "MainForm.Pages.cs|Voice", "MainForm.Pages.cs|Cast", "MainForm.Pages.cs|AI Commentary",
        "VoiceSettingsControl.cs|Speak with", "VoiceSettingsControl.cs|ElevenLabs connection",
        "CastControl.cs|Voices", "CastControl.cs|Announcer", "CastControl.cs|Co-host",

        // AI Commentary's Simple view: shown in Simple only (SimpleMode.ShowInSimpleOnly).
        "CommentaryControl.cs|What it talks about", "CommentaryControl.cs|Countdown",

        // On the Race Calls tab, which Simple hides.
        "VoiceSettingsControl.Announcements.cs|How often", "RivalriesControl.cs|Configured rivalries", "RivalriesControl.cs|Edit selected rivalry",

        // Sounds: on Mixer, Soundscapes and Music, or on the tabs Simple hides.
        "MainForm.Pages.cs|Mixer", "MainForm.Pages.cs|Soundscapes", "MainForm.Pages.cs|Music",
        "MusicControl.cs|Player", "MusicControl.cs|Library",
        "AmbienceControl.cs|Layers", "AmbienceControl.cs|Reactions", "LapBeepControl.cs|Drivers",
        "BroadcastImagingControl.cs|Soundscape", "BroadcastImagingControl.cs|Pieces", "BroadcastImagingControl.cs|Timing",
        "BroadcastImagingControl.cs|Run of show", "ShowTreatmentCard.cs|Show treatment",
        "MusicControl.cs|What plays", "AmbienceControl.cs|Live", "BroadcastImagingControl.cs|On air",
        "LapBeepControl.cs|Lap cues", "LapBeepControl.cs|Everyone at once",
        "SoundEffectsControl.cs|Every sound at once", "SoundEffectsControl.cs|Per lap",
        "SoundscapeEditorForm.cs|Sounds", "SoundscapeEditorForm.cs|Announcer",

        // Connection, Backup & Restore.
        "MainForm.ConnectionPage.cs|Timing decoder", "MainForm.ConnectionPage.cs|Start-up options", "MainForm.ConnectionPage.cs|Race tools",
        "BackupRestoreControl.cs|Manual backup and restore", "BackupRestoreControl.cs|Automatic backups",
        "BackupRestoreControl.cs|Backup password", "BackupRestoreControl.cs|Settings mode", "BackupRestoreControl.cs|Getting help",
        "BackupRestoreControl.cs|Updates", "BackupRestoreControl.cs|Cloud copies",
        "BackupRestoreControl.cs|Existing backups", "BackupRestoreControl.Standby.cs|Standby PC",

        // Themes, Sponsors, Local Screens, Club Website.
        "LooksControl.cs|Looks", "LooksControl.cs|Club logo", "LooksControl.cs|Appearance",
        "LooksControl.cs|Crowd screens", "AchievementsControl.cs|Whose achievements", "GarageControl.cs|Which cars", "GarageControl.cs|Garage",
        "SponsorsControl.cs|Slides (shown in this order)", "SponsorsControl.cs|Sponsor reads",
        "WebServerControl.cs|Server", "WebServerControl.cs|Open these on your phone, tablet, or in OBS",
        "MainForm.Pages.cs|Website", "RelaySettingsControl.cs|Relay", "RelaySettingsControl.cs|Online check-in",
        "RelaySettingsControl.cs|Share", "RelaySettingsControl.cs|Status",
        "RelaySettingsControl.cs|Your first night guide", "RelaySettingsControl.cs|Club updates",
        "RecordingControl.cs|Recording the night", "RecordingControl.cs|Written from the night",

        // Venues, which Simple hides as a page.
        "VenuesControl.cs|Venues", "VenuesControl.cs|The venue", "VenuesControl.cs|Race defaults", "VenuesControl.cs|Apply",

        // Windows of their own, not settings pages.
        "AnnouncerControl.cs|Running order", "AnnouncerControl.cs|Spotlight", "AnnouncerControl.cs|Talking points",
        "BroadcastWindow.cs|At a glance", "BroadcastWindow.cs|Controls", "ThisMonthForm.cs|This month", "ThisMonthForm.cs|Emails",
        "Shortcuts.cs|Anywhere", "Shortcuts.cs|Race Control", "Shortcuts.cs|Selected driver",
        "ChassisTemplateForm.cs|Other tuning fields (not on the diagram)", "ChassisTemplateForm.cs|Selected point",
        "RcpStockForm.cs|Boxed sets", "RcpStockForm.cs|Loose parts", "RcpPrintForm.cs|Parts", "RcpPrintForm.cs|Scale",
        "RcpTrackEditorForm.cs|Build me a track", "RcpTrackEditorForm.cs|Check against a kit", "RcpTrackEditorForm.cs|Keys",
        // Lanes, Plan file and Trace a photo went with the rethought window (8 Nov 2026), and Rail the edge,
        // which was a toolbar button, became a card of its own in the Auto section.
        "RcpTrackEditorForm.cs|Rail the edge", "RcpTrackEditorForm.cs|Start from a kit",
        "RcpTrackEditorForm.cs|The lap", "RcpTrackEditorForm.cs|The room it goes in", "RcpTrackEditorForm.cs|Tile size",
        "RcpTrackEditorForm.cs|Track name", "RcpTrackEditorForm.cs|What the club owns",
    };

    [Fact]
    public void Every_card_and_tab_is_in_a_group()
    {
        var found = Captions().ToHashSet(StringComparer.Ordinal);
        var grouped = Hidden.Concat(Shown).ToHashSet(StringComparer.Ordinal);

        var missing = found.Where(c => !grouped.Contains(c)).OrderBy(c => c).ToList();

        Assert.True(missing.Count == 0,
            "A card or tab Simple mode has not been told about - add it to SimpleModeTests.Hidden (and tag it with "
            + "SimpleMode.Hide / HideTab where it is built) or to Shown: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_grouped_card_and_tab_still_exists_and_is_in_one_group()
    {
        var found = Captions().ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Hidden.Concat(Shown), c => !found.Contains(c));
        Assert.Empty(Hidden.Intersect(Shown));
    }

    [Fact]
    public void Every_hidden_card_is_tagged_where_it_is_built()
    {
        var folder = SourceFolder();

        var untagged = Hidden
            .Select(entry => entry.Split('|')[0])
            .Distinct()
            .Where(file => !File.ReadAllText(Path.Combine(folder, file)).Contains("SimpleMode.Hide", StringComparison.Ordinal)
                           // Room & Web's and Between races' cards are tagged where their pages add them.
                           && file != "RoomAndWebCard.cs"
                           && file != "BetweenRacesCard.cs")
            .ToList();

        Assert.Empty(untagged);
    }

    /// <summary>
    /// Every literal card and tab caption in the app, as "File|Caption" -
    /// a Section or TabPage, or one of Tonight's folding cards (Fold, 17 Nov
    /// 2026), which is a Section whose caption sits in its header.
    /// </summary>
    private static IEnumerable<string> Captions()
    {
        var pattern = new Regex("(?:new (?:Section|TabPage)|\\bFold)\\(\"([^\"]+)\"");

        foreach (var file in Directory.GetFiles(SourceFolder(), "*.cs"))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                yield return $"{Path.GetFileName(file)}|{match.Groups[1].Value}";
            }
        }
    }

    /// <summary>The app's source folder, under the folder the repo is checked out in - found by check.cmd, as AudioLevelsTests finds it.</summary>
    private static string SourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
            {
                return Path.Combine(folder.FullName, "HeatCaller", "HeatCaller");
            }

            folder = folder.Parent;
        }

        throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
    }
}
