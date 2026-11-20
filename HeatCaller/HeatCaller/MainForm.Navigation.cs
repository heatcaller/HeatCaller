using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's navigation: the sidebar's pages and their order, race-night
/// grouping, search, showing a page, building every page ahead of time, and
/// Manage Navigation.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Applies the same setup the Designer's page loop does, for pages
    /// declared outside it.
    /// </summary>
    private static void ConfigurePage(Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.BackColor = AppTheme.Background;
        page.Visible = false;
    }

    /// <summary>Re-runs the refresh hook for whichever page is currently showing, if it has one.</summary>
    private void RefreshCurrentPage()
    {
        // The page on screen, not the sidebar's highlight - they differ when
        // a button has opened a page hidden from the sidebar, and an undo
        // then refreshed the page behind it and left the one in front stale.
        if ((_shownPage ?? lstNav.SelectedItem) is string name && _navRefreshHooks.TryGetValue(name, out var refresh))
        {
            try
            {
                refresh();
            }
            catch
            {
                // A refresh failing must never take down the undo itself.
            }
        }
    }

    /// <summary>One entry in the sidebar nav: a display name and the Panel showing its content. "Connection" can't be hidden, but it is no longer pinned to the top.</summary>
    private sealed record NavItem(string Name, Panel Page);

    private List<NavItem> _navItems = new();

    /// <summary>
    /// Whether the sidebar is currently BUILT for race night - not whether it
    /// ought to be.
    ///
    /// The two are deliberately separate. OrderedNavNames sorts by this and
    /// SidebarNavList asks for each row's heading through it, and those two
    /// answers have to agree or the list draws a "TONIGHT" heading above rows
    /// that are not in that group. A race day can open from several places -
    /// the Today checklist, the banner on Who's Racing, the Day tab, a switch
    /// of current day - and any of them could land between a repaint and a
    /// rebuild. One flag, changed only by SyncRaceNightNav, immediately before
    /// the rebuild that acts on it.
    /// </summary>
    private bool _navRaceNight;

    /// <summary>
    /// Whether the sidebar OUGHT to be in race-night order: the club wants it,
    /// and there is a real race day open to be in the middle of.
    ///
    /// The legacy holder does not count. It is where everything that pre-dates
    /// race days is filed and it is never closed, so treating it as an open
    /// day would put the sidebar in race-night order permanently, for
    /// everybody, including a club that has never started a day at all.
    /// </summary>
    private bool RaceNightWanted =>
        _settings.RaceNightFocus && _raceEvents.Active is { IsLegacy: false, IsOpen: true };

    /// <summary>
    /// Rebuilds the sidebar when a race day has opened or closed, and does
    /// nothing at all otherwise.
    ///
    /// Guarded because this is called from the event that fires whenever the
    /// current race day changes, and RebuildNav re-selects and re-shows a page.
    /// Doing that on every switch between days - when the grouping is the same
    /// either way - would be work nobody asked for in the middle of an
    /// evening.
    /// </summary>
    private void SyncRaceNightNav()
    {
        var wanted = RaceNightWanted;

        if (wanted == _navRaceNight)
        {
            return;
        }

        _navRaceNight = wanted;
        RebuildNav();
    }

    private readonly Dictionary<string, Action> _navRefreshHooks = new();

    /// <summary>
    /// The page actually on screen, whichever way it got there. Not always
    /// the sidebar's selection: Race Mode hides the sidebar, and a page the
    /// user has hidden from the sidebar can still be brought up by a
    /// button. The flag keys go by this, so they follow what is showing.
    /// </summary>
    private string? _shownPage;


    /// <summary>
    /// Keywords that should surface a page even though they don't appear
    /// in its name - "where's the minimum lap time setting" shouldn't
    /// require already knowing it lives under Race Control.
    /// </summary>
    private static readonly Dictionary<string, string[]> NavSearchAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Race Control"] = new[] { "flag", "green", "yellow", "red", "checkered", "stage", "countdown", "penalty", "minimum lap", "min lap", "streak", "big screen", "clock", "timer", "race mode", "f11", "full screen", "fullscreen", "next up", "re-seed", "reseed",

            // The announcer's screen is opened from this page - it is
            // no longer a tab anywhere - so this is where searching for
            // it has to land.
            "announcer", "announcer screen", "spotlight", "mc", "microphone", "mic", "talking points",

            // On Air is the normal way into a race now - the jingle, the
            // opening and a bumper before staging - and Mission Control
            // opens from here as well as from Connection.
            "on air", "mission control" },
        ["Connection"] = new[] { "connect", "decoder", "easylap", "i-lap", "ilap", "com port", "serial", "baud", "auto-connect", "console", "quick race", "mission control" },
        ["Driver Hub"] = new[]
        {
            "transponder", "car", "name", "roster", "import", "export", "driver profiles", "profile", "setups", "transfer",
            "battery", "batteries", "pack", "packs", "cell", "nimh", "lipo", "rotation", "charge",
        },
        // Race Day swallowed four pages, so it answers for all four
        // pages' words - someone searching "check-in" is not wrong just
        // because it became a tab.
        ["Race Day"] = new[]
        {
            "heat", "qualifying", "main", "round", "grid", "print", "program",
            "reminder", "timeline", "running order", "on deck", "schedule",
            "qr", "badge", "scan", "present", "attendance", "check in", "check-in",
            "tech", "scrutineer", "scrutineering", "inspect", "inspection", "checklist",
            "event", "meeting", "weekend", "entries",
        },
        // "sanctioning" and the body names are in here deliberately: the
        // preset that encodes them lives two clicks inside this page, and
        // somebody looking for it searches for the thing, not for the page
        // that happens to hold it.
        // "relay", "cloud" and "publish" are all in here because nobody
        // calls it the same thing twice, and the page is named for what it
        // achieves rather than for the machinery behind it.
        ["Club Website"] = new[]
        {
            "online", "relay", "cloud", "publish", "publishing", "internet", "remote", "public", "url",
            "share", "token", "club id", "results page", "standings page", "spectator", "stream",
            "https", "hosted", "away", "off site", "offsite", "website", "site", "web site", "club website",

            // The Room & Web card, from the Commentary page (17 Oct 2026).
            "room & web", "room and web", "club notes", "notes", "shout-out", "shout-outs", "dedication",
            "vote", "votes", "room vote", "room's vote", "listen along", "listening along", "listen",

            // The Recording tab, from Sounds (26 Oct 2026, asked for by
            // Asher: "move it to the club website for now") - its words
            // came with it, so "recording" still finds it.
            "record", "recording", "record each race", "clips", "highlights", "highlight video", "videos",
            "night recap", "tonight's recap", "driver's night", "check a line", "proof", "night before", "preview",
        },
        ["Classes"] = new[]
        {
            "class", "classes", "rules", "rulebook", "sanctioning", "preset", "roar", "ifmar", "brca",
            "pn racing", "pnwc", "qualifying", "consecutive", "best of", "main", "mains", "bump up",
            "bump-up", "drivers per main", "tech", "scrutineering", "checklist", "colour", "color",
            "race format", "jump start",
        },
        ["Rentals"] = new[] { "rental", "transponder", "loan", "loaner", "borrow", "deposit", "hire", "equipment" },
        ["Handicaps"] = new[] { "handicap", "hcp", "credit", "even", "mixed", "ability", "novice", "stagger" },
        ["Track Layouts"] = new[] { "record", "photo", "map", "average lap", "length" },
        ["Race History"] = new[] { "results", "past", "notes", "export", "csv", "delete session" },
        // Personal Bests, Lap Times, Head to Head and Trends used to be
        // keyed here under their own names - but they are tabs of Race
        // Analytics now, and a key that is not a sidebar name is never
        // looked up, so typing "pb" found nothing. Their words live in
        // NavSearchTabAliases below, filed under the page that owns them.
        ["Championship"] = new[]
        {
            "season", "standings", "points", "export", "csv", "series",
            "calendar", "regional", "drop", "title", "events",
        },
        ["Practice"] = new[] { "lapping", "rolling average", "tuning", "open" },
        ["Demo Mode"] = new[] { "simulate", "fake", "test race" },
        ["Venues"] = new[] { "location", "shop", "preset", "profile" },
        ["Themes"] = new[]
        {
            "look", "looks", "colour", "color", "theme", "dark mode", "light mode", "synthwave",
            "logo", "club logo", "appearance", "24-hour", "military time",
        },
        ["Sponsors"] = new[] { "logo", "advert", "slide", "rotation" },
        ["Local Screens"] = new[]
        {
            "remote", "obs", "overlay", "stream", "server", "url", "browser",
            "phone", "phones", "web", "wifi", "wi-fi", "local", "network", "screen", "screens",
        },
        // Absorbs the terms from the former Announcer and Commentary nav
        // items - those are tabs here now, and searching for them must
        // still land somewhere useful.
        ["Voice"] = new[]
        {
            "elevenlabs", "speech", "tts", "api key", "rivalries", "rivalry",
            "commentary", "commentator", "llm", "ai", "ollama", "colour", "color",
            "narration", "local model",

            // The old name of this page, so anyone who learned it still
            // finds it, plus what its Race Calls tab holds.
            "voice announcements", "announcements", "announcement", "lines", "phrasing", "wording",
            "race calls", "per lap", "max per lap", "streak", "streak threshold",

            // The Cast tab (17 Oct 2026).
            "cast", "co-host", "cohost", "recap host", "mechanic", "technical lead", "tech lead", "interview", "honours", "badges reader", "achievements reader", "achievement reader",
            "badge reader", "announcer name", "announcer's name",
        },
        ["Ghost Cars"] = new[] { "ghost", "virtual", "opponent", "rival", "solo", "practice alone", "ai car", "pace" },
        ["Sounds"] = new[] { "soundscape", "soundscapes", "preset", "voice pack", "sound pack", "announcer style", "personality", "sound effects", "music", "soundtrack", "track", "song", "playlist", "beep", "beeps", "cue", "chime", "per-driver", "ambience", "crowd", "atmosphere", "track noise", "tone", "beep", "audio", "volume", "level", "duck", "ducking", "test", "imaging", "jingle", "sweeper", "bumper", "stinger", "music bed", "run of show", "radio", "hit the post", "post mark", "out mark", "on air", "show music", "show treatment", "opener", "club sting" },
        // The Default settings card: Restore Default Settings and its copies.
        ["Backup & Restore"] = new[] { "save", "restore", "export", "zip", "auto backup", "support", "help", "logs", "log file", "crash", "send logs", "license", "privacy", "terms", "eula", "default settings", "reset settings", "put back" },
        ["Garage"] = new[] { "car", "cars", "chassis", "shell", "bodyshell", "tuning", "setup log", "setup", "tires", "gearing", "ride height", "springs", "loan", "borrow", "club car" },
    };

    /// <summary>
    /// Words for the tabs inside a page, keyed by the sidebar page that
    /// owns them. A match here does two things: the owning page stays in
    /// the filtered sidebar, and when that page is opened the matching tab
    /// is brought to the front - someone who typed "trends" wants the
    /// Trends chart, not whichever Race Analytics tab was open last.
    ///
    /// An ordered list rather than a dictionary per page, so when a word
    /// fits two tabs the first one listed - the left-most - wins, every
    /// time. The tab names must match the TabPage captions exactly; the
    /// caption itself always counts as a keyword.
    /// </summary>
    private static readonly Dictionary<string, (string Tab, string[] Words)[]> NavSearchTabAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // The Tab name here is matched against TabPage.Text exactly, so
        // these move whenever a tab is renamed. The old names stay in the
        // word lists - somebody who learnt "entries" or "run sheet" should
        // still land on the right tab after they were renamed.
        ["Race Day"] = new[]
        {
            // Today absorbed the Heats & Mains tab, so its words land here
            // now. The old ones are kept: somebody who learnt "program" or
            // went looking for "mains" should still arrive at the grid,
            // wherever it has been moved to since.
            ("Next Steps", new[]
            {
                "next steps", "today", "next", "next step", "what next", "ready", "checklist", "overview",
                "heats", "mains", "heats and mains", "program", "programme", "qualifying",
                "main", "heat", "round", "grid", "bump", "bump up", "bump-up", "seed", "seeding",
                "build", "start", "running order", "standings",

                // The Timetable tab became a drawer off this page, so its
                // words land here now - searching "run sheet" should arrive
                // at the page with the button on it rather than at nothing.
                "timetable", "run sheet", "schedule", "reminder", "timeline", "on deck",
                "late", "running late", "drift", "times",
            }),
            ("Setup", new[] { "set up", "set up tonight", "details", "the day", "day", "event", "meeting", "weekend", "race day", "kind of night", "delete day", "template", "templates", "new race day", "start tonight", "club rules", "close day", "forecast" }),
            // Who's Racing absorbed the Scrutineering tab, so its words land
            // here. Kept, for the same reason as Today's: somebody who learnt
            // "tech log" should still arrive where the log now lives.
            ("Who's Racing", new[]
            {
                "who", "whos racing", "who's racing", "entries", "entry", "qr", "badge", "scan",
                "present", "attendance", "check in", "check-in",
                "scrutineering", "scrutineer", "tech", "tech log", "inspect", "inspection",
            }),
        },
        ["Championship"] = new[]
        {
            ("Standings", new[] { "standings", "points", "season", "drop" }),
            ("Calendar", new[] { "calendar", "race days", "race day", "all days", "events", "regional", "archive" }),
        },
        ["Race Analytics"] = new[]
        {
            ("Overview", new[] { "overview", "track record", "lap record" }),
            ("Personal Bests", new[] { "pb", "personal best", "best lap", "record" }),
            ("Lap Times", new[] { "lap times", "chart", "graph", "consistency", "std dev" }),
            ("Head to Head", new[] { "head to head", "compare", "versus", "vs", "rivalry" }),
            ("Trends", new[] { "trend", "progress", "improvement", "over time", "history chart" }),
            ("Pace", new[] { "pace" }),
        },
        // THE VOICE REGROUP (17 Oct 2026): Voice, Cast, Race Calls and AI
        // Commentary. The club's own show music and the recording of the
        // night moved to Sounds (Imaging's Show treatment card, and the
        // Recording tab - on the Club Website page since 26 Oct 2026), and
        // club notes, the room's vote and listening along to the Club
        // Website page, so their words are filed there.
        //
        // AI Commentary is listed BEFORE Cast although its tab is after it:
        // the first match wins, and Cast's "recap host" would otherwise take
        // a plain "recap" search away from the Race recap card.
        ["Voice"] = new[]
        {
            ("Voice", new[] { "voice settings", "elevenlabs", "speech", "tts", "api key", "kokoro", "stability", "similarity" }),
            ("AI Commentary", new[]
            {
                "ai commentary", "commentary", "commentator", "llm", "ai", "ollama", "narration", "local model",
                "relay", "recap", "teaser", "sign-off", "show name", "segments", "how chatty", "pacing",
                "writing style", "how it's written", "tone", "commercial break", "talking points", "intros",
            }),
            ("Cast", new[]
            {
                "cast", "co-host", "cohost", "co host", "two voices", "recap host", "voice delivery",
                "mechanic", "technical lead", "tech lead", "interview", "honours", "honours reader", "badges reader", "badge reader", "achievements reader", "achievement reader",
                "announcer name", "announcer's name", "load voices",
            }),
            ("Race Calls", new[]
            {
                "race calls", "announcements", "announcement", "lines", "phrasing", "templates", "wording", "weight",
                "caution", "how often", "per lap", "max per lap", "max spoken", "streak", "streak threshold",
                "rivalries", "rivalry",
            }),
        },
        ["Sounds"] = new[]
        {
            // Every level and duck is the Mixer's (17 Oct 2026), so its words
            // come first. Music is listed BEFORE Imaging although its tab is
            // after it: the first match wins, and Imaging's "music bed" and
            // "show music" would otherwise take a plain "music" search away
            // from the Music tab.
            ("Mixer", new[] { "mixer", "blend", "volume", "level", "levels", "duck", "ducking", "recap duck", "voice lift", "boost" }),
            ("Soundscapes", new[] { "soundscape", "soundscapes", "announcer style", "personality", "voice pack", "sound pack", "preset" }),
            ("Effects", new[] { "effects", "sound effects", "cue", "chime" }),
            ("Ambience", new[] { "ambience", "crowd", "atmosphere", "track noise" }),
            ("Music", new[] { "music", "soundtrack", "song", "playlist" }),
            ("Imaging", new[]
            {
                "broadcast imaging", "imaging", "jingle", "sweeper", "bumper", "stinger", "music bed", "bed", "run of show", "radio", "hit the post", "post mark", "out mark", "on air",
                "show music", "show treatment", "opener", "club sting", "voice comes in", "play it",
            }),
            ("Lap Beeps", new[] { "lap beeps", "beep", "per-driver", "tone" }),
        },

        // TWO TABS SINCE 26 OCT 2026, when Recording moved here from Sounds
        // (asked for by Asher: "the recording tab is in a bad location move
        // it to the club website for now."). Website is listed with the
        // page's own words so a search for the relay brings Website back to
        // the front when Recording was the tab left open.
        ["Club Website"] = new[]
        {
            ("Website", new[]
            {
                "website", "web site", "club website", "relay", "online", "cloud", "publish", "publishing", "token",
                "club id", "results page", "standings page", "spectator", "hosted", "url", "share",
                "room & web", "room and web", "club notes", "shout-out", "shout-outs", "dedication",
                "vote", "votes", "room vote", "room's vote", "listen along", "listening along",
            }),
            ("Recording", new[]
            {
                "recording", "record", "record each race", "clips", "highlights", "highlight video", "videos",
                "night recap", "tonight's recap", "driver's night", "check a line", "proof", "night before", "preview",
            }),
        },
    };

    private static bool TabMatchesSearch((string Tab, string[] Words) tab, string query) =>
        tab.Tab.Contains(query, StringComparison.OrdinalIgnoreCase)
        || tab.Words.Any(w => w.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesNavSearch(string navName, string query)
    {
        if (navName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (NavSearchTabAliases.TryGetValue(navName, out var tabs) && tabs.Any(t => TabMatchesSearch(t, query)))
        {
            return true;
        }

        return NavSearchAliases.TryGetValue(navName, out var aliases)
            && aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// With a search typed, brings forward the tab of this page that the
    /// search was for - see NavSearchTabAliases.
    ///
    /// Three letters at least for a partial match: one or two match a tab
    /// on almost every page, and the tabs flicking about under each
    /// keystroke would be worse than not choosing one at all. Shorter than
    /// that, only a whole keyword counts - "pb", "vs", "mc" - since those
    /// are words somebody types on purpose. A search that matched the page
    /// but none of its tabs leaves the tab alone.
    /// </summary>
    private void SelectSearchedTab(string navName)
    {
        var query = txtNavSearch.Text.Trim();

        if (query.Length == 0 || !NavSearchTabAliases.TryGetValue(navName, out var tabs))
        {
            return;
        }

        var match = query.Length >= 3
            ? tabs.FirstOrDefault(t => TabMatchesSearch(t, query))
            : tabs.FirstOrDefault(t => t.Words.Any(w => string.Equals(w, query, StringComparison.OrdinalIgnoreCase)));

        if (match.Tab is null)
        {
            return;
        }

        // Every page with tabs holds its TabControl directly - see where
        // each page is assembled.
        var tabControl = _navItems.FirstOrDefault(i => i.Name == navName)?.Page.Controls.OfType<TabControl>().FirstOrDefault();
        var tabPage = tabControl?.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == match.Tab);

        if (tabControl is not null && tabPage is not null && tabControl.SelectedTab != tabPage)
        {
            // Raises SelectedIndexChanged, which refreshes the tab.
            tabControl.SelectedTab = tabPage;
        }
    }

    /// <summary>Every nav item, hidden ones included, in the user's chosen order.</summary>
    private List<string> OrderedNavNames() => OrderedNavNames(_navRaceNight, NavTiers.HidingUntested);

    /// <param name="raceNight">
    /// False asks for the CLUB'S OWN order - the arrangement they dragged
    /// into place - without the race-night grouping laid over it.
    ///
    /// Anything about to SAVE an order has to ask for that one. The
    /// grouping is a view: it lifts Race Day and Race Control to the top
    /// while a day is open and drops them back when it closes. Written to
    /// NavOrder it stops being a view and becomes the arrangement, and the
    /// two of them stay at the top for ever - which is what happened by
    /// opening Manage Navigation during a race night and pressing OK
    /// without touching anything.
    /// </param>
    private List<string> OrderedNavNames(bool raceNight) => OrderedNavNames(raceNight, hidingUntested: false);

    /// <summary>
    /// The same, with the pages not yet tested at a real event grouped at
    /// the foot when <paramref name="hidingUntested"/> - a VIEW, like the
    /// race-night grouping, so anything about to save an order asks for
    /// it without (see the raceNight note above).
    /// </summary>
    private List<string> OrderedNavNames(bool raceNight, bool hidingUntested)
    {
        var ordered = _navItems.Select(i => i.Name).ToList();
        if (_settings.NavOrder.Count > 0)
        {
            var known = new HashSet<string>(ordered);
            var reordered = _settings.NavOrder.Where(known.Contains).ToList();
            reordered.AddRange(ordered.Where(n => !reordered.Contains(n)));
            ordered = reordered;
        }

        // Kept in their groups - Tonight (only while a race day is open),
        // Essentials, then club tools - whatever order was saved: the club's
        // own order within each group is kept (the sort is stable), but a page
        // can't be dragged into another group.
        //
        // The secondary key only does anything inside the Tonight group, where
        // Race Day comes before Race Control because that is the order of the
        // evening. It is zero everywhere else, and a stable sort leaves those
        // rows exactly as they were.
        ordered = ordered
            .OrderBy(n => NavTiers.TierOf(n, raceNight, hidingUntested))
            .ThenBy(n => NavTiers.TonightRank(n, raceNight))
            .ToList();

        return ordered;
    }

    /// <summary>Names of visible nav items in the user's chosen order - "Connection" can't be hidden but is no longer forced first.</summary>
    private List<string> VisibleOrderedNavNames() => VisibleOrderedNavNames(_navRaceNight);

    /// <summary>The same, in the club's own order when raceNight is false. See OrderedNavNames.</summary>
    private List<string> VisibleOrderedNavNames(bool raceNight)
    {
        var ordered = OrderedNavNames(raceNight, NavTiers.HidingUntested);

        // Connection is no longer pinned to the top - it sits low in the
        // canonical order with the other setup pages - but it still can't
        // be hidden, so it survives the visibility filter regardless.
        //
        // A page not yet tested at a real event is left out too while the
        // club hides those (22 Oct 2026) - RebuildNav still lets a search
        // turn one up, under its own heading. See NavTiers.UntestedPages.
        return ordered
            .Where(n => n == "Connection" || !_settings.HiddenTabs.Contains(n))
            .Where(n => !(NavTiers.HidingUntested && NavTiers.IsUntested(n)))
            .ToList();
    }

    /// <summary>
    /// The untested pages a search turns up while the club hides them - in
    /// the sidebar's order, so they land at its foot under their heading.
    /// A page the club hid by hand stays hidden, as it does from a search
    /// today.
    /// </summary>
    private List<string> UntestedPagesMatching(string query) =>
        !NavTiers.HidingUntested || query.Length == 0
            ? new List<string>()
            : OrderedNavNames(_navRaceNight, hidingUntested: true)
                .Where(NavTiers.IsUntested)
                .Where(n => !_settings.HiddenTabs.Contains(n))
                .Where(n => MatchesNavSearch(n, query))
                .ToList();

    /// <summary>Rebuilds the sidebar list from the current order/visibility settings and shows whichever item was selected before (or Race Control by default).</summary>
    private void RebuildNav()
    {
        var previouslySelected = lstNav.SelectedItem;

        // Before anything is ordered: SectionOf, which the list asks for each
        // row's heading, reads the same flag, and the two have to agree.
        // Always off (14 Nov 2026): the "keep untested pages out of the
        // sidebar" option is gone, so every page shows, whatever an older
        // settings file says.
        NavTiers.HidingUntested = false;

        var names = VisibleOrderedNavNames();

        var query = txtNavSearch.Text.Trim();
        if (query.Length > 0)
        {
            var matched = names.Where(n => MatchesNavSearch(n, query))
                .Concat(UntestedPagesMatching(query))
                .ToList();
            // Never filter down to nothing - an empty sidebar looks broken
            // and leaves no way back, so a query with no hits just shows
            // everything rather than a blank panel.
            if (matched.Count > 0)
            {
                names = matched;
            }
        }

        lstNav.SetItems(names);

        // The page on screen is not always the one under the highlight - see
        // _shownPage. A page hidden from the sidebar can be brought up by a
        // button, and Race Mode brings up Race Control even when it is hidden,
        // leaving the highlight on whatever was picked before. Rebuilding the
        // list (a race day opening or closing with race-night focus on, or
        // Ctrl+Z undoing a sidebar change) used to put the highlighted page
        // up instead, which in Race Mode swapped the full-screen race board
        // for another page mid-race, with no sidebar to get back with.
        // So the page on screen stays up: always in Race Mode, which is one
        // page on purpose, and otherwise when a button brought it up (it
        // differs from the highlight) and no page search is driving this
        // rebuild - a search still moves to what it found, and hiding the
        // page that was picked from the sidebar still moves off it, as
        // before.
        var keepShown = _shownPage is not null
            && _navItems.Any(i => i.Name == _shownPage)
            && (_raceMode || (query.Length == 0 && _shownPage != previouslySelected));

        var toSelect = NavRowToSelect(names, keepShown ? _shownPage : null, previouslySelected);

        // Moving the highlight raises SelectionChanged, which would show the
        // highlighted page by itself - held off here, so the page shown is
        // the one chosen just below, and it is shown once.
        _rebuildingNav = true;
        try
        {
            lstNav.SelectedItem = toSelect;
        }
        finally
        {
            _rebuildingNav = false;
        }

        var toShow = keepShown ? _shownPage! : toSelect ?? _shownPage ?? "Race Control";
        ShowPage(toShow);
        SelectSearchedTab(toShow);
    }

    /// <summary>
    /// The row RebuildNav highlights: the page kept on screen, else the one
    /// picked before, else Race Control, else the first row - or null, for
    /// nothing highlighted.
    /// </summary>
    internal static string? NavRowToSelect(IReadOnlyList<string> names, string? keptShown, string? previouslySelected)
    {
        bool Usable(string? n) => n is not null && names.Contains(n);

        if (Usable(keptShown))
        {
            return keptShown;
        }

        if (Usable(previouslySelected))
        {
            return previouslySelected;
        }

        if (Usable("Race Control"))
        {
            return "Race Control";
        }

        // An empty list (none in practice - a search never filters down to
        // nothing) keeps the old answer.
        return names.Count == 0 ? "Race Control" : names[0];
    }

    /// <summary>True while RebuildNav moves the highlight - see there.</summary>
    private bool _rebuildingNav;

    /// <summary>How long a page can take to open before the session log says so - see ShowPage.</summary>
    /// <summary>
    /// How slow a page has to be before it is written to the log.
    ///
    /// A fifth of a second rather than half, since 13 Oct 2026: half a
    /// second is already past what anybody would call quick, so a page
    /// sitting just under it was invisible to the one thing that could have
    /// said so.
    /// </summary>
    private const int SlowPageMs = 200;

    /// <summary>
    /// Which pages have been opened this run, so the log can say whether a
    /// slow open was the first - the one that builds and themes - or a
    /// later one, which should be nearly free and is the interesting case
    /// when it is not.
    /// </summary>
    private readonly HashSet<string> _pageShownBefore = new(StringComparer.Ordinal);

    /// <summary>Usage totals (AppUsageTracker): told each page as it comes on screen. Set in the constructor.</summary>
    private readonly AppUsageTracker _usageTracker;

    private void ShowPage(string name)
    {
        // The desk view keeps to Who's Racing (12 Nov 2026, setup plan D8).
        if (DeskViewRefusesPage(name))
        {
            return;
        }

        var item = _navItems.FirstOrDefault(i => i.Name == name);
        if (item is null)
        {
            return;
        }

        // The whole swap happens with the content area's painting off.
        //
        // Every page is a Dock=Fill child of the same panel, stacked in
        // z-order with one of them visible. Hiding the current page
        // UNCOVERS whichever page is behind it, and Windows paints that the
        // instant it is uncovered - so walking the list setting Visible put
        // a page nobody asked for on screen for as long as it took the loop
        // to reach the one they did. That is the wrong screen for a moment
        // before the right one.
        //
        // The refresh hook below repopulates grids, and that paints as well.
        // Nothing here is worth watching in progress. Only the result is.
        NoRedraw.Begin(pnlContent);
        pnlContent.SuspendLayout();

        // Laid out again inside, before the page is let go (see there), so
        // the finally must not do it again.
        var resumed = false;

        try
        {
            // Timed from here: a page slow to open is written to the session
            // log (below).
            var opening = System.Diagnostics.Stopwatch.StartNew();

            // At the text size in use, and themed at it: a page that was
            // away when the size changed (or since startup) takes it now,
            // while it is still hidden and held - so the skins are on before
            // it is shown, as they were when the whole window was themed at
            // once - see CatchUpTextSize.
            CatchUpTextSize(item.Page);
            var afterTextSize = opening.ElapsedMilliseconds;

            // The wanted page first, and in front: with it already covering
            // the area, nothing behind it is ever exposed to begin with.
            // This alone fixes the swap; the redraw suppression above is
            // what covers the refresh that follows.
            item.Page.Visible = true;
            item.Page.BringToFront();
            _shownPage = name;

            foreach (var nav in _navItems)
            {
                // Only the pages that are actually showing. Setting Visible
                // on all thirty-odd meant thirty-odd layout passes to
                // change one thing.
                if (nav != item && nav.Page.Visible)
                {
                    nav.Page.Visible = false;
                }
            }

            // Every page not on screen holds its layout (LayoutFreeze), so
            // what changes on it while it is away - a new text size, a list
            // filled again - is laid out once, when it is next shown, rather
            // than at each change.
            foreach (var nav in _navItems)
            {
                if (nav != item)
                {
                    LayoutFreeze.Freeze(nav.Page);
                }
            }

            // The content area laid out now, while painting is still off:
            // it gives this page the room it is shown in - the page is held,
            // so that only sizes it - and the page is then let go, laid out
            // once, at that size and in the faces it has now. Before its
            // refresh, so what the refresh reads is laid out.
            var afterSwap = opening.ElapsedMilliseconds;

            pnlContent.ResumeLayout(true);
            resumed = true;

            // Where a slow let-go says so. Set here rather than at startup
            // because this is the only place that knows where the console
            // is; setting it again costs nothing.
            LayoutFreeze.Trace ??= OnClientRawMessage;

            // CHILDREN BEFORE PARENTS (14 Oct 2026). The class notes on
            // LayoutFreeze have said since 1 Oct why this order matters -
            // let go parents first, "the column was laid out again for every
            // line in it, each time measuring them all", and the Setup
            // Sheet's lines were measured sixty thousand times. The
            // childrenFirst switch was added for that, and then only ever
            // passed by the window-building path: every page and every tab
            // went on being let go the slow way. The Voice page took 4.3s to
            // come back, and the Commentary tab inside it hung on the click.
            LayoutFreeze.Thaw(item.Page, childrenFirst: true);
            var afterLayout = opening.ElapsedMilliseconds;

            if (_navRefreshHooks.TryGetValue(name, out var refresh))
            {
                refresh();
            }

            // Usage totals (19 Nov 2026): after the refresh, so the tab it
            // reads is the one the page settled on. Counting must never stop
            // a page opening.
            try
            {
                if (txtNavSearch.Text.Length > 0)
                {
                    _usageTracker.Count("nav_search_used");
                }

                _usageTracker.PageShown(name, item.Page);
            }
            catch (Exception ex)
            {
                SessionLog.Write($"[usage] {ex.GetType().Name} counting a page; carrying on.");
            }

            // BROKEN DOWN, BECAUSE ONE NUMBER NEVER SAID WHERE IT WENT
            // (13 Oct 2026).
            //
            // This has reported "X took 1.4s to open" since it was written,
            // which tells you a page is slow and nothing whatever about
            // why - and the four things it could be want completely
            // different fixes:
            //
            //   text size   - the theme pass over a page opened at a size it
            //                 has not been themed at (CatchUpTextSize, which
            //                 does nothing at all on a repeat visit);
            //   swap        - showing this page, hiding the last one, and
            //                 holding the other thirty;
            //   layout      - the page laid out once as it is let go, which
            //                 for a page with hundreds of controls in it is
            //                 the one most likely to be the answer;
            //   refresh     - the page's own hook repopulating its lists.
            //
            // The same instrumented-rather-than-reasoned approach
            // AnnouncementService.WaitForAudioAsync records at length, and
            // for the same reason: three attempts at that timing were made
            // by reasoning about the code and each was wrong.
            //
            // Noted on every open, not only a slow one (6 Nov 2026): a page
            // whose first open was quick, or came while the window was being
            // built, was never recorded, so its first slow open later was
            // logged as ", first time" when it was a repeat visit.
            var firstTime = _pageShownBefore.Add(name);
            if (opening.ElapsedMilliseconds >= SlowPageMs && !LayoutFreeze.Building)
            {
                OnClientRawMessage(
                    $"{AppTime.LogStampNow()}  [ui] {name} took {opening.Elapsed.TotalSeconds:0.00}s to open - "
                    + $"text size {afterTextSize}ms, swap {afterSwap - afterTextSize}ms, "
                    + $"layout {afterLayout - afterSwap}ms, refresh {opening.ElapsedMilliseconds - afterLayout}ms"
                    + $"{(firstTime ? ", first time" : string.Empty)}.");
            }

            // The "one yellow button per page" pass used to run here, on a
            // page's first showing. It is gone, and this is the note
            // explaining why so it does not get reinvented.
            //
            // It picked the first button whose CAPTION read as a main action
            // and painted it yellow. But that reading is a substring match -
            // "export", "apply", "add" - over the order the controls happened
            // to be added in, and neither of those has anything to do with
            // which button matters. On Race History it landed on "Export
            // Session", which sat yellow next to a pink "Export Results" doing
            // the same kind of job; on Venues it turned the page's only main
            // action yellow and left nothing pink at all.
            //
            // The theme already says "this is the main one" in pink. A second
            // colour, chosen by keyword, was not a stronger signal - it was a
            // competing one.
        }
        finally
        {
            if (!resumed)
            {
                pnlContent.ResumeLayout(true);
            }

            // And the page let go, if something threw before it was: a page
            // on screen and held is never laid out. Letting go of one that
            // is not held does nothing.
            if (_shownPage == name)
            {
                LayoutFreeze.Thaw(item.Page, childrenFirst: true);
            }

            // In a finally: a panel left with its painting switched off
            // never draws again, and the app would sit showing whatever
            // page happened to be up when something threw.
            NoRedraw.End(pnlContent);
        }
    }

    /// <summary>
    /// The "Start a Race Day" button, wherever it is pressed.
    ///
    /// Before the Race Day tabs are built there is nothing to start one
    /// with, and that is not an error worth a dialog - the button cannot
    /// be on screen yet either.
    /// </summary>
    private void StartRaceDayFromBanner() => _startRaceDay?.Invoke();

    /// <summary>
    /// Offers the setup wizard, once ever.
    ///
    /// A question rather than the wizard itself, because this fires on an
    /// existing club's first launch after upgrading as well as on a fresh
    /// install, and a window nobody asked for is a poor way to meet one. A
    /// club that says no is not asked again - HasSeenRaceDaySetup means
    /// "has answered", not "has completed" - and the button on The Day is
    /// there for ever either way.
    ///
    /// Not offered while a race day is already open and under way: somebody
    /// mid-event who restarted the app is not setting up, and that is the
    /// moment it would be most unwelcome.
    ///
    /// An open day with nothing in it - no classes picked, no rounds - doesn't
    /// count (12 Nov 2026, setup plan A4). A fresh install used to open one by
    /// itself, so the old rule meant a new club was never offered the wizard
    /// at all. A fresh install no longer does (setup plan step 8), but an
    /// install that already has such a day keeps it, and the wizard takes it
    /// over rather than starting another.
    ///
    /// Also offered straight after the first-run wizard (setup plan step 8),
    /// which is how a new club's first real race day is started.
    /// </summary>
    /// <summary>A race day nobody has done anything with yet: no classes picked and no rounds.</summary>
    internal static bool IsEmptyDay(RaceEvent day) =>
        (day.ClassNames?.Count ?? 0) == 0 && (day.RoundIds?.Count ?? 0) == 0;

    private void OfferRaceDaySetupOnce()
    {
        if (_settings.HasSeenRaceDaySetup || IsDisposed || _openRaceDaySetup is null)
        {
            return;
        }

        if (_raceEvents.Active is { IsLegacy: false, IsOpen: true } open && !IsEmptyDay(open))
        {
            return;
        }

        _settings.HasSeenRaceDaySetup = true;
        _settings.Save();

        if (AppDialog.Show(this,
                "Would you like a hand setting up a race night?\r\n\r\n"
                + "It walks through the classes, the drivers, the day itself, who has turned up and the program - "
                + "and every step can be skipped. It is on Race Day > Setup as \"Set Up Tonight...\" whenever you want it.",
                "Set up tonight", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _openRaceDaySetup.Invoke(0);
    }

    /// <summary>
    /// Brings the timetable's generated half in line with the programme.
    ///
    /// Two rules keep it from being presumptuous. It only ever writes the
    /// ROUNDS' own lines - anything somebody typed, doors or lunch or the
    /// presentation, is left exactly where it was put, and SyncToProgram
    /// keeps the lines already there rather than rebuilding them, so a time
    /// set against a round survives. And it only runs for a real race day:
    /// with only the legacy holder open the programme is the whole store,
    /// and writing that onto the holder's sheet would file every round the
    /// club has ever run as tonight's running order.
    ///
    /// It fills an empty sheet as well as maintaining a filled one. That
    /// used to need "Fill from the Programme" pressed once, which was fine
    /// while the timetable was a tab somebody landed on and is not fine now
    /// that it is a drawer - the one button that made the page work would
    /// have been behind the door it opens.
    ///
    /// No undo entry: this is not a change the user made, it is the
    /// timetable following one they made somewhere else, and a stack full of
    /// "run sheet rebuilt" buries the thing they actually did.
    /// </summary>
    private void SyncTimetableToProgramme()
    {
        if (_raceEvents.Active is not { IsLegacy: false })
        {
            return;
        }

        // The sheet this writes to is whichever day the schedule store is
        // pointed at, and the programme below is the CURRENT day's. They
        // agree because EventSpine.Bind subscribes to CurrentChanged before
        // this form does, so the store has already followed by the time a
        // day switch reaches here - but that is an ordering two files
        // apart, and if it ever stopped holding, the line below would write
        // tonight's programme onto yesterday's run sheet and clear
        // yesterday's lines off it. Worth one comparison to make that
        // impossible rather than merely unlikely.
        if (!string.Equals(_daySchedule.CurrentEventId, _raceEvents.CurrentId, StringComparison.Ordinal))
        {
            return;
        }

        var programme = EventSpine.Programme(_raceEvents, _rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, MixedHeats.ClassLabel(r), r.RoundLabel))
            .ToList();

        // An empty programme is handed over rather than skipped. Deleting
        // the day's last round used to return here, leaving its generated
        // line on the sheet with no round behind it - still printed on the
        // running order, still counted by Next Up, still measured for
        // running late. Lines somebody typed are not generated and
        // SyncToProgram leaves those alone, so an empty programme clears
        // exactly the lines the programme put there and nothing else.
        _daySchedule.SyncToProgram(programme);

        // Only when it is open. Shut, it re-reads on the way in.
        _daySchedulePage?.RefreshAll();
    }

    /// <summary>
    /// Takes the user to a page the way clicking it in the sidebar would,
    /// so the sidebar's highlight, the page on screen and the keys that
    /// depend on the page all agree.
    ///
    /// ShowPage on its own swaps the content but leaves the sidebar
    /// pointing at the old page - which is how F11 used to put Race Control
    /// up while the flag keys still thought the user was somewhere else.
    ///
    /// A search that has filtered the page out of the sidebar is cleared
    /// first, since there is no other way to highlight it. A page the user
    /// has hidden from the sidebar altogether is still shown, just without
    /// a highlight - it has nothing to highlight.
    /// </summary>
    private void GoToPage(string name)
    {
        if (!lstNav.Items.Contains(name) && txtNavSearch.Text.Length > 0)
        {
            // Rebuilds the sidebar through TextChanged.
            txtNavSearch.Text = string.Empty;
        }

        if (!lstNav.Items.Contains(name))
        {
            ShowPage(name);
            return;
        }

        if (lstNav.SelectedItem == name)
        {
            // Already highlighted, so setting it again raises nothing - but
            // an earlier ShowPage may have put a different page up since.
            if (_shownPage != name)
            {
                ShowPage(name);
            }

            return;
        }

        // Raises SelectionChanged, which shows the page.
        lstNav.SelectedItem = name;
    }

    /// <summary>
    /// Builds every page, and every tab inside each one, once, off screen,
    /// as soon as the window is up.
    ///
    /// Making a page visible creates its handle and its controls' handles,
    /// and selecting a tab raises the SelectedIndexChanged refreshes a first
    /// click on it would. Done here, a page nobody has opened yet has been
    /// built and refreshed the same as one they have: code that checks
    /// IsHandleCreated before posting work to itself, or counts on a tab's
    /// first refresh having run, finds the same on both.
    ///
    /// It lays out nothing but the page on screen's tabs. Every other page
    /// holds its layout (LayoutFreeze) and is laid out when it is first
    /// opened - one page, at the size it is shown at - rather than all
    /// thirty here while the startup screen waits. It used to run again after every text size
    /// change too, and lay every page out again each time; it does not now
    /// (ApplyFontScale): the handles are made and the refreshes have run.
    ///
    /// This walk used to be the startup sweep of the layout repair pass,
    /// which put each page and tab on screen to measure it. The measuring
    /// went with the repair pass (30 Sep); the walk was kept, because pages
    /// may rely on having been built by it.
    ///
    /// Each page is made visible only for its own turn. Nothing is painted
    /// in between - no message is pumped inside this loop - so the pages
    /// flashing past is not something anybody sees.
    /// </summary>
    private void WarmPages()
    {
        // Only once the window is actually on screen. ApplyFontScale first
        // runs while the window is still being built, and then there is
        // nothing yet for the pages' handles to be made in.
        if (!Visible || _navItems.Count == 0)
        {
            return;
        }

        // Pass two resumes layout itself (it has to - see there), so the
        // finally must not do it again.
        var resumed = false;

        // Same reasoning as ShowPage: thirty pages flashing past on a
        // window that is up are not something to show.
        NoRedraw.Begin(pnlContent);
        pnlContent.SuspendLayout();

        try
        {
            // Pass one: the pages themselves, with layout suspended. Each
            // brings along whichever of its tabs happens to be showing.
            foreach (var nav in _navItems)
            {
                var wasVisible = nav.Page.Visible;

                // Shown and put straight back: becoming visible is what
                // makes its handle and its controls'.
                nav.Page.Visible = true;
                nav.Page.Visible = wasVisible;
            }

            // Layout back on before pass two, so each page is given the
            // content area's size as it is made visible. (This used to be
            // what kept a never-opened tab from being laid out at about
            // 192px wide, the size a tab control in a suspended parent gives
            // it. The pages are held now, and nothing in one is laid out
            // until it is opened - see LayoutFreeze - but a page is still
            // better given its real size.) Nothing is painted either way;
            // NoRedraw is still on.
            pnlContent.ResumeLayout(true);
            resumed = true;

            // Pass two: every OTHER tab, which pass one could not reach.
            // Inside a TabControl only the selected tab is visible, so only
            // that one came along with its page; a tab nobody has selected
            // yet has not been built at all.
            foreach (var nav in _navItems)
            {
                var wasVisible = nav.Page.Visible;

                nav.Page.Visible = true;

                foreach (var _ in SubTabsOf(nav.Page))
                {
                    // Nothing to do with the tab here: SubTabsOf selects each
                    // one in turn as this loop steps through them, and that
                    // is the whole point.
                }

                nav.Page.Visible = wasVisible;
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [layout] Building the pages ahead of time stopped early - {ex.Message}");
        }
        finally
        {
            if (!resumed)
            {
                pnlContent.ResumeLayout(true);
            }

            NoRedraw.End(pnlContent);
        }
    }

    private enum WarmKind { Page, Layout, Theme, Tab }

    /// <summary>A warm-up step longer than this means the PC is too busy: the rest of the themes and tabs wait for their first open - see WarmPagesInTurn.</summary>
    internal static readonly TimeSpan WarmStepTooSlow = TimeSpan.FromSeconds(3);

    /// <summary>One step of WarmPagesInTurn: a page, the content area, a page's theming, or one of its tabs.</summary>
    private sealed record WarmStep(WarmKind Kind, NavItem? Nav, TabControl? Tabs, TabPage? Tab);

    /// <summary>Every TabControl in <paramref name="parent"/>, outer ones first - nested ones too, as SubTabsOf finds them.</summary>
    private static void CollectTabControls(Control parent, List<TabControl> into)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is TabControl tabs)
            {
                into.Add(tabs);
            }

            CollectTabControls(child, into);
        }
    }
    /// <summary>
    /// WarmPages, a page at a time, between everything else the window has
    /// to do - for the real window. Calls <paramref name="done"/> with how
    /// long the steps themselves took once the last one has run.
    ///
    /// WHY (13 Nov 2026, Asher: "make the whole app quicker to load"): run in
    /// one go from Shown, the walk held the window's thread for 6 to 15
    /// seconds (the session log, 13 Nov: "Every page and tab made ready in
    /// 8.6s") - before the window had painted or could answer a click, and
    /// with auto-connect and everything else queued for after startup
    /// waiting behind it. HeatCaller was usable about 17s after it was
    /// started, half of that this walk.
    ///
    /// The same walk, the same two passes, in the same order: each page's
    /// handles first, with the content area's layout held, then each page's
    /// other tabs. Each step is one page, or one tab, and they are run from a timer,
    /// whose ticks Windows hands out only when there is nothing else to do -
    /// after clicks, keys and painting - so the window paints at once and
    /// answers between pages.
    ///
    /// One difference, on purpose: the page somebody has open is not walked
    /// through its tabs while they are looking at it (selecting a tab runs
    /// its refresh, and flips it in front of them). It is tried again at the
    /// end; still open then, its tabs are made as they are clicked, as any
    /// tab is.
    /// </summary>
    private void WarmPagesInTurn(Action<TimeSpan, string, string?> done)
    {
        if (!Visible || _navItems.Count == 0)
        {
            done(TimeSpan.Zero, string.Empty, null);
            return;
        }

        // Pass one: each page made once, with the content area's layout held.
        // Then the content area laid out, as WarmPages' ResumeLayout(true)
        // does. Pass two: each page themed, and each of its tabs made - ONE
        // TAB A STEP: a page at a time, Sounds took 5s in one go, which is a
        // window not answering for 5s.
        var steps = new Queue<WarmStep>();

        foreach (var nav in _navItems)
        {
            steps.Enqueue(new WarmStep(WarmKind.Page, nav, null, null));
        }

        steps.Enqueue(new WarmStep(WarmKind.Layout, null, null, null));

        foreach (var nav in _navItems)
        {
            steps.Enqueue(new WarmStep(WarmKind.Theme, nav, null, null));

            var tabControls = new List<TabControl>();
            CollectTabControls(nav.Page, tabControls);

            foreach (var tabs in tabControls)
            {
                foreach (TabPage tab in tabs.TabPages)
                {
                    steps.Enqueue(new WarmStep(WarmKind.Tab, nav, tabs, tab));
                }
            }
        }

        var retried = new HashSet<WarmStep>();
        var busy = TimeSpan.Zero;
        var slowest = new List<(TimeSpan Took, string What)>();
        string? stoppedEarly = null;
        var timer = new System.Windows.Forms.Timer { Interval = 1 };

        timer.Tick += (_, _) =>
        {
            if (IsDisposed || Disposing)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }

            if (steps.Count == 0)
            {
                timer.Stop();
                timer.Dispose();
                done(busy, string.Join(", ", slowest.OrderByDescending(s => s.Took).Take(5).Select(s => $"{s.What} {s.Took.TotalSeconds:0.00}s")), stoppedEarly);
                return;
            }

            var next = steps.Dequeue();
            var nav = next.Nav;

            // On screen: its tabs, and its theming (done as it was opened),
            // are left - see above. Tried once more at the end.
            if (next.Kind is WarmKind.Theme or WarmKind.Tab && nav is not null && nav.Page.Visible)
            {
                if (retried.Add(next))
                {
                    steps.Enqueue(next);
                }

                return;
            }

            // A tab already made - the one showing, or one somebody has
            // clicked since - has nothing to do.
            if (next.Kind == WarmKind.Tab && (next.Tab!.IsDisposed || ReferenceEquals(next.Tabs!.SelectedTab, next.Tab)))
            {
                return;
            }

            var step = System.Diagnostics.Stopwatch.StartNew();

            // Where the keys go now, put back after the step (bug hunt 21, 15
            // Nov 2026): a hidden page shown and hidden again, or a tab
            // selected, can take the focus - and someone typing in the first
            // seconds after startup found the rest of a word going to the
            // bare-letter shortcuts.
            var focused = DeepestFocus(this);
            NoRedraw.Begin(pnlContent);

            var hold = next.Kind == WarmKind.Page;

            if (hold)
            {
                pnlContent.SuspendLayout();
            }

            try
            {
                switch (next.Kind)
                {
                    case WarmKind.Layout:
                        pnlContent.PerformLayout();
                        break;

                    case WarmKind.Page:
                    {
                        // Shown and put straight back: becoming visible is
                        // what makes its handle and its controls'.
                        var wasVisible = nav!.Page.Visible;
                        nav.Page.Visible = true;
                        nav.Page.Visible = wasVisible;
                        break;
                    }

                    case WarmKind.Theme:
                        // Themed at the text size in use now, while it is
                        // still hidden and held, so its first open has none of
                        // that left to do - what ShowPage does first
                        // (CatchUpTextSize), and on a first open the biggest
                        // part of it: the Voice page spent 1.8s of its 3.0s
                        // there (session log, 13 Nov). A size changed later is
                        // caught up on the next open, as before.
                        CatchUpTextSize(nav!.Page);
                        break;

                    case WarmKind.Tab:
                    {
                        // Selected - which makes it, and runs its first
                        // refresh - and the one that was showing put back, so
                        // a page opened between steps is as it was left.
                        var tabs = next.Tabs!;
                        var previous = tabs.SelectedTab;

                        nav!.Page.Visible = true;

                        try
                        {
                            tabs.SelectedTab = next.Tab;
                        }
                        finally
                        {
                            if (previous is not null && !previous.IsDisposed)
                            {
                                tabs.SelectedTab = previous;
                            }

                            nav.Page.Visible = false;
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [layout] Building {nav?.Name ?? "the pages"} ahead of time stopped early - {ex.Message}");
            }
            finally
            {
                if (hold)
                {
                    pnlContent.ResumeLayout(false);
                }

                if (FocusWentAway(focused, DeepestFocus(this)))
                {
                    focused!.Focus();
                }

                NoRedraw.End(pnlContent);
                busy += step.Elapsed;

                var what = next.Kind switch
                {
                    WarmKind.Layout => "the content area",
                    WarmKind.Theme => $"theming {nav!.Name}",
                    WarmKind.Tab => $"{nav!.Name} > {next.Tab!.Text}",
                    _ => nav!.Name,
                };

                slowest.Add((step.Elapsed, what));

                // A PC too busy to warm up without freezing (17 Nov 2026):
                // the window can't answer while a step runs, and with three
                // test runs and a backup going a single tab took 13-17s,
                // where it takes about 1s on a quiet PC - the "not
                // responding" Asher saw, three times in ten minutes. Past one
                // slow step, pass two (theming and tabs - what a page does
                // anyway the first time it is opened) is left to each page's
                // first open. Pass one, every page's handles, is kept: code
                // reaching a page from another thread counts on them.
                if (stoppedEarly is null && step.Elapsed > WarmStepTooSlow)
                {
                    stoppedEarly = $"{what} took {step.Elapsed.TotalSeconds:0.0}s";
                    var keep = steps.Where(s => s.Kind is WarmKind.Page or WarmKind.Layout).ToList();
                    steps.Clear();

                    foreach (var s in keep)
                    {
                        steps.Enqueue(s);
                    }
                }
            }
        };

        timer.Start();
    }

    /// <summary>The control the keys go to: ActiveControl, followed down through any UserControl it is inside.</summary>
    internal static Control? DeepestFocus(ContainerControl container)
    {
        Control? focused = container.ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        return focused;
    }

    /// <summary>
    /// Whether a warm-up step took the focus from a control that can still
    /// have it back: one still there, on screen and able to take it (bug
    /// hunt 21). A control the step hid stays where the step left it.
    /// </summary>
    internal static bool FocusWentAway(Control? was, Control? now) =>
        was is { IsDisposed: false, Visible: true, CanFocus: true } && !ReferenceEquals(was, now);

    /// <summary>
    /// Every tab page inside a page, each one selected - and so made visible
    /// - while the caller's loop is on it.
    ///
    /// Selecting a tab is the only way to do this: a TabPage that has never
    /// been shown has never had its handles made or its children laid out,
    /// and asking it while it is hidden changes neither. The original
    /// selection is put back before this returns, and the whole walk runs
    /// inside NoRedraw, so none of it is painted.
    ///
    /// Selecting a tab can raise SelectedIndexChanged, and a few tabs
    /// refresh themselves on that - which is harmless here (they re-read
    /// their own stores) and is also exactly what would happen the first
    /// time somebody clicked the tab.
    /// </summary>
    private static IEnumerable<(string Name, Control Page)> SubTabsOf(Control page)
    {
        // Collected first, while everything is still as it was. Walking the
        // tree does not care what is visible, so a TabControl nested inside
        // another tab is found here even though it is not on screen.
        var controls = new List<TabControl>();
        Collect(page, controls);

        foreach (var tabs in controls)
        {
            var previous = tabs.SelectedTab;

            try
            {
                foreach (TabPage tab in tabs.TabPages)
                {
                    // The tab that was already showing came along with the
                    // page itself - it WAS the visible one - so it is not
                    // selected again.
                    if (ReferenceEquals(tab, previous))
                    {
                        continue;
                    }

                    tabs.SelectedTab = tab;

                    // YIELDED while the tab is still selected. Returning a
                    // finished list instead would hand back tab pages that
                    // are hidden again by the time the caller gets to them.
                    //
                    // Named by its caption, as it is on screen: "Race Day >
                    // Setup".
                    yield return (tab.Text, tab);
                }
            }
            finally
            {
                // Runs when the caller's foreach ends, however it ends.
                if (previous is not null)
                {
                    tabs.SelectedTab = previous;
                }
            }
        }

        static void Collect(Control parent, List<TabControl> into)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TabControl tabs)
                {
                    into.Add(tabs);
                }

                Collect(child, into);
            }
        }
    }

    private void LstNav_SelectionChanged()
    {
        if (_rebuildingNav)
        {
            return;
        }

        if (lstNav.SelectedItem is string name)
        {
            ShowPage(name);
            SelectSearchedTab(name);
        }
    }

    /// <summary>Persists a new nav order after a drag-and-drop reorder in the sidebar.</summary>
    private void LstNav_ItemsReordered(List<string> newOrder)
    {
        // Persist the FULL order, hidden items included, so hiding/showing
        // later doesn't lose this arrangement.
        //
        // The dragged list is only what the sidebar was SHOWING - with a page
        // search typed, a handful of pages. The rest used to be appended
        // after it in the app's default order, so one drag with a search
        // active threw away the club's own arrangement of every other page.
        // Now the shown pages take their new order in the slots they already
        // held, and every other page stays exactly where it was.
        // Mapped in the order the sidebar is actually SHOWING, because that
        // is what was dragged. It is put back into the club's own order just
        // below, before anything is saved.
        var current = OrderedNavNames();
        var shown = new HashSet<string>(newOrder);
        var inNewOrder = new Queue<string>(newOrder.Where(current.Contains));
        var fullOrder = current
            .Select(name => shown.Contains(name) && inNewOrder.Count > 0 ? inNewOrder.Dequeue() : name)
            .ToList();

        // A drag dropped back where it started changes nothing, and must not
        // push a do-nothing step onto Undo.
        var orderBefore = _settings.NavOrder.ToList();

        // Race Day and Race Control are at the top of a race-night sidebar
        // because of the grouping, not because the club put them there.
        // Saving the list as dragged would write that into NavOrder and
        // leave them pinned there after the day closed - the same fault as
        // Manage Navigation's. They keep the slot the saved order already
        // gives them; the drag rearranges everything else.
        //
        // The same for the untested pages kept at the foot while a beta club
        // hides them: a view, not the club's arrangement.
        //
        // WHY (22 Oct 2026): these used to be two passes, one after the
        // other. Putting pages back at their saved index only works when
        // every OTHER page is already in saved order, and during the first
        // pass the untested pages were still parked at the foot - so on a
        // race night with untested pages hidden, Race Day and Race Control
        // landed in the wrong slot and drifted further on every drag. Both
        // groupings now come out in one pass.
        var hidingUntested = NavTiers.HidingUntested;
        if (_navRaceNight || hidingUntested)
        {
            fullOrder = WithoutGrouping(fullOrder, orderBefore, n =>
                (_navRaceNight && NavTiers.TierOf(n, raceNight: true, hidingUntested: false) == NavTier.Tonight)
                || (hidingUntested && NavTiers.IsUntested(n)));
        }
        if (!orderBefore.SequenceEqual(fullOrder))
        {
            _undo.Capture("Reorder the sidebar", () =>
            {
                _settings.NavOrder = orderBefore.ToList();
                _settings.Save();
                RebuildNav();
            });
        }

        _settings.NavOrder = fullOrder;
        _settings.Save();

        // A page dropped into the other group goes back into its own.
        if (!OrderedNavNames().Where(newOrder.Contains).SequenceEqual(newOrder))
        {
            RebuildNav();
        }
    }

    /// <summary>
    /// A dragged order with the pages a view moved (<paramref name="moved"/>)
    /// put back wherever the club's saved order had them, everything else
    /// left exactly as dragged. Every grouping must be taken out in ONE call:
    /// the put-back is only right when the pages left behind are already in
    /// saved order.
    /// </summary>
    /// <param name="saved">
    /// The order as last saved. Empty on a club that has never rearranged
    /// anything, in which case the app's canonical order stands in - without
    /// that, the promoted pages would be appended at the BOTTOM, which
    /// is the opposite of the bug this is preventing.
    /// </param>
    private List<string> WithoutGrouping(List<string> dragged, List<string> saved, Func<string, bool> moved)
    {
        var promoted = dragged
            .Where(moved)
            .ToList();

        if (promoted.Count == 0)
        {
            return dragged;
        }

        var reference = saved.Count > 0 ? saved : _navItems.Select(i => i.Name).ToList();
        var rest = dragged.Where(n => !promoted.Contains(n)).ToList();

        // Lowest saved index first, so inserting one does not shift the slot
        // the next one is aiming for.
        foreach (var name in promoted.OrderBy(n => reference.IndexOf(n) is var at && at < 0 ? int.MaxValue : at))
        {
            var at = reference.IndexOf(name);

            rest.Insert(at < 0 || at > rest.Count ? rest.Count : at, name);
        }

        return rest;
    }

    private void BtnManageNav_Click(object? sender, EventArgs e)
    {
        AppUsageTracker.Note("manage_navigation_opened");
        var toggleable = _navItems.Where(i => i.Name != "Connection").Select(i => i.Name).ToList();
        // The club's own order, NOT what the sidebar is showing. During a
        // race night the sidebar leads with Race Day and Race Control
        // because of the grouping; seeding this dialog from that and then
        // saving what comes back is what wrote the grouping into NavOrder
        // permanently. See OrderedNavNames.
        //
        // Every page, hidden ones included. This used to be seeded from the
        // visible pages only, with the hidden ones tacked on at the end in
        // the app's own order - so pressing OK put each hidden page at the
        // bottom of the saved order, and a page the club had dragged near
        // the top came back at the bottom of its group when shown again.
        // The dialog lists hidden pages as unticked rows, so they can keep
        // their real places in it.
        var clubOrder = OrderedNavNames(raceNight: false);

        var orderedToggleable = clubOrder.Where(toggleable.Contains)
            .Concat(toggleable.Where(n => !clubOrder.Contains(n)))
            .ToList();

        // _navItems is the app's canonical order, which is exactly what
        // "defaults" should mean here.
        var defaultOrder = _navItems.Where(i => i.Name != "Connection").Select(i => i.Name).ToList();

        using var form = new TabVisibilityForm(
            orderedToggleable, _settings.HiddenTabs, defaultOrder, _settings.RaceNightFocus);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Connection is not in the dialog (it cannot be hidden), so it goes
        // back where it already was. This used to put it first every time,
        // left over from when the connection page was pinned to the top -
        // so pressing OK here quietly moved it above Race Control.
        var orderShownBefore = OrderedNavNames(raceNight: false);
        var connectionIndex = orderShownBefore.IndexOf("Connection");
        var newOrder = form.OrderedNames.ToList();
        newOrder.Insert(Math.Clamp(connectionIndex, 0, newOrder.Count), "Connection");

        // Restore Defaults in the dialog lands here too, so the sidebar as
        // it was is kept for Undo.
        var hiddenBefore = _settings.HiddenTabs.ToList();
        var orderBefore = _settings.NavOrder.ToList();
        var raceNightBefore = _settings.RaceNightFocus;

        // The order is compared with the one the sidebar actually goes by,
        // not with the raw saved NavOrder: that can be empty (a club that
        // never rearranged anything) or hold names the app no longer has,
        // and either made an untouched OK look like a change and push an
        // Undo step that did nothing. Saving newOrder below is still right
        // in that case - it is the same arrangement, written out in full.
        if (!form.HiddenTabs.SetEquals(hiddenBefore)
            || !orderShownBefore.SequenceEqual(newOrder)
            || form.RaceNightFocus != raceNightBefore)
        {
            _undo.Capture("Change the sidebar's pages", () =>
            {
                _settings.HiddenTabs = hiddenBefore.ToList();
                _settings.NavOrder = orderBefore.ToList();
                _settings.RaceNightFocus = raceNightBefore;
                _settings.Save();

                // Recomputed, not remembered: the undo can be pressed much
                // later, by which time the race day this depended on may have
                // been closed.
                _navRaceNight = RaceNightWanted;
                RebuildNav();
            });
        }

        _settings.HiddenTabs = form.HiddenTabs.ToList();
        _settings.NavOrder = newOrder;
        _settings.RaceNightFocus = form.RaceNightFocus;
        _settings.Save();

        _navRaceNight = RaceNightWanted;
        RebuildNav();
    }
}
