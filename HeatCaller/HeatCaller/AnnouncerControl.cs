namespace HeatCaller;

/// <summary>
/// A view for whoever is on the microphone.
///
/// Deliberately not another leaderboard. The Big Screen already shows the
/// audience where everyone is; an announcer's problem is different - they
/// need things to SAY, and they need them without reading a grid mid-race.
///
/// So this leads with generated talking points (gaps closing, streaks
/// running, someone's personal best), keeps the running order to a glanceable
/// few, and lets one driver be pinned for a story to follow.
/// </summary>
public sealed class AnnouncerControl : UserControl
{
    private readonly RaceControlSession _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly DayScheduleStore _schedule;
    private readonly CommentaryService? _commentary;
    private readonly AppSettings? _settings;

    /// <summary>Latest LLM-suggested line, shown at the top of the list. Null until one arrives.</summary>
    private string? _suggestedLine;

    /// <summary>Guards against stacking requests - the refresh runs twice a second and a model call takes seconds.</summary>
    private bool _suggestionInFlight;

    private DateTime _lastSuggestionUtc = DateTime.MinValue;

    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    private readonly Label _lblState = new();
    private readonly Label _lblClock = new();
    private readonly DataGridView _gridOrder = new();
    private readonly ListBox _lstTalkingPoints = new();
    private readonly ComboBox _cmbSpotlight = new();
    private readonly Label _lblSpotlight = new();
    private readonly Label _lblOnDeck = new();
    private readonly Button _btnRepeat = new();
    private readonly Label _lblLastSummary = new();
    private readonly Label _lblSummaryCaption = new();

    /// <summary>Events worth mentioning, newest first. Capped so it stays readable at a glance.</summary>
    private readonly List<string> _feed = new();
    private const int MaxFeed = 40;
    private readonly AnnouncementService _announcer;

    /// <summary>For Clear Points. Optional so an older host still builds; the clear then isn't undoable.</summary>
    private readonly UndoService? _undo;

    public AnnouncerControl(RaceControlSession race, ProfileStore profiles,
        SessionHistoryStore history, DayScheduleStore schedule, AnnouncementService announcer,
        CommentaryService? commentary = null, AppSettings? settings = null, UndoService? undo = null)
    {
        _undo = undo;
        _commentary = commentary;
        _settings = settings;
        _race = race;
        _profiles = profiles;
        _history = history;
        _schedule = schedule;
        _announcer = announcer;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        Size = new Size(980, 620);
        MinimumSize = new Size(900, 560);

        BuildLayout();

        // The announcer service already decides what's noteworthy, so its
        // spoken lines double as the feed rather than duplicating that logic
        // with a second set of rules that could disagree.
        announcer.AnnouncementTextReady += OnAnnouncementText;

        _race.JumpStartDetected += (transponder, penaltyMs) =>
            AddFeedLine($"JUMP START - {NameFor(transponder)}"
                        + (penaltyMs > 0 ? $" (+{penaltyMs / 1000.0:0.0}s)" : " (flagged)"));

        _refresh.Tick += (_, _) => RefreshLive();
        _refresh.Start();

        RefreshAll();
    }

    private void BuildLayout()
    {
        _lblState.Location = new Point(12, 12);
        _lblState.AutoSize = true;
        _lblState.Font = new Font(AppTheme.BodyFont.FontFamily, 16F, FontStyle.Bold);

        _lblClock.Location = new Point(12, 44);
        _lblClock.AutoSize = true;
        _lblClock.Font = new Font("Consolas", 26F, FontStyle.Bold);

        _lblOnDeck.Location = new Point(300, 20);
        _lblOnDeck.AutoSize = true;
        _lblOnDeck.MaximumSize = new Size(588, 0);  // capped to the page's own minimum width, not a wider guess
        _lblOnDeck.ForeColor = AppTheme.Accent;
        _lblOnDeck.Font = AppTheme.BoldFont;

        // --- Running order, kept short on purpose ---
        var lblOrder = new Label { Text = "RUNNING ORDER", Location = new Point(12, 96), AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.Primary };

        _gridOrder.Location = new Point(12, 120);
        _gridOrder.Size = new Size(470, 300);
        // Top/Left/Right only - NOT Bottom. Bottom anchoring stretches the
        // grid downward on a taller window, painting over whatever sits
        // below it and making those controls invisible.
        _gridOrder.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _gridOrder.ReadOnly = true;
        _gridOrder.AllowUserToAddRows = false;
        _gridOrder.RowHeadersVisible = false;
        _gridOrder.MultiSelect = false;
        _gridOrder.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridOrder.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridOrder.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _gridOrder.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "", FillWeight = 18 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Laps", FillWeight = 28 },
            new DataGridViewTextBoxColumn { Name = "gap", HeaderText = "Gap", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "last", HeaderText = "Last", FillWeight = 40 }
        );
        _gridOrder.SelectionChanged += (_, _) =>
        {
            if (_gridOrder.CurrentRow?.Tag is string transponder)
            {
                SelectSpotlight(transponder);
            }
        };

        // --- Talking points ---
        var lblTalk = new Label { Text = "TALKING POINTS", Location = new Point(498, 96), AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.Primary };

        _lstTalkingPoints.Location = new Point(498, 120);
        _lstTalkingPoints.Size = new Size(470, 300);
        _lstTalkingPoints.Anchor = AnchorStyles.Top | AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _lstTalkingPoints.BackColor = AppTheme.Surface;
        _lstTalkingPoints.IntegralHeight = false;

        // A ListBox truncates anything wider than the control - it has no
        // wrapping of its own. Owner-drawing with variable height lets each
        // row measure and wrap its own text, which matters most for the
        // generated commentary line at the top: that's the one someone's
        // meant to read out loud, and it's also the longest.
        _lstTalkingPoints.DrawMode = DrawMode.OwnerDrawVariable;
        _lstTalkingPoints.MeasureItem += TalkingPoints_MeasureItem;
        _lstTalkingPoints.DrawItem += TalkingPoints_DrawItem;

        // Variable-height rows are measured once, when added - so a resize
        // would otherwise leave every existing row at its old height and
        // re-truncate the text at the new width. Re-running the refresh
        // re-adds the items, which re-measures them.
        _lstTalkingPoints.Resize += (_, _) => RefreshTalkingPoints();

        // Everything from here down used to be anchored to the PAGE's
        // bottom edge while the running-order grid above is Top|Left at a
        // fixed height - so the grid's bottom stays at y=420 and this
        // whole block slid up over it on any window shorter than the 620
        // these literals were measured against. The page's own minimum is
        // 560, so it did not even need resizing to happen.
        //
        // Top-anchored now, like the grid. If the content does not fit,
        // the page scrolls - which is what LayoutDoctor turns on, and is a
        // far better answer than two things drawn on top of each other.
        // --- Spotlight ---
        var lblSpot = new Label { Text = "SPOTLIGHT", Location = new Point(12, 436), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Font = AppTheme.BoldFont, ForeColor = AppTheme.Primary };

        _cmbSpotlight.Location = new Point(110, 432);
        _cmbSpotlight.Width = 250;
        _cmbSpotlight.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _cmbSpotlight.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSpotlight.SelectedIndexChanged += (_, _) => RefreshSpotlight();

        _lblSpotlight.Location = new Point(12, 464);
        _lblSpotlight.Size = new Size(460, 120);
        _lblSpotlight.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _lblSpotlight.ForeColor = AppTheme.TextDark;

        // The summary text sits directly under the Repeat button that
        // speaks it, so it's obvious what pressing that button will say.
        _lblSummaryCaption.Text = "LAST RACE SUMMARY";
        _lblSummaryCaption.Location = new Point(488, 470);
        _lblSummaryCaption.AutoSize = true;
        _lblSummaryCaption.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _lblSummaryCaption.Font = AppTheme.BoldFont;
        _lblSummaryCaption.ForeColor = AppTheme.Primary;

        _lblLastSummary.Location = new Point(488, 494);
        _lblLastSummary.Size = new Size(480, 90);
        _lblLastSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _lblLastSummary.ForeColor = AppTheme.TextMuted;

        _btnRepeat.Text = "Repeat Race Summary";
        _btnRepeat.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _btnRepeat.Enabled = false;
        _btnRepeat.Click += (_, _) =>
        {
            // Repeats the stored text rather than asking the model again -
            // regenerating could come back with different wording, or fail
            // outright if the model's since gone offline, neither of which
            // is what "repeat that" means.
            //
            // Prefers the race summary over whatever was generated most
            // recently: talking points regenerate every 20 seconds during a
            // race, so "most recent" would almost never be the summary by
            // the time anyone wants it repeated.
            var summary = _commentary?.LastRaceSummary;
            if (summary is { Length: > 0 } && _commentary is not null)
            {
                // Same tags and delivery as the automatic recap. The repeat
                // used to send the bare text with the profile's normal
                // voice settings, so it sounded different from the original.
                (double, double)? delivery = _settings?.SummaryUseBroadcastVoice == true
                    ? (_settings.SummaryVoiceStability, _settings.SummaryVoiceStyle)
                    : null;

                _announcer.SpeakReminder(_commentary.PrepareSummaryForSpeech(summary), delivery);
                return;
            }

            var line = _commentary?.LastLine;
            if (line is { Length: > 0 })
            {
                _announcer.SpeakReminder(line);
            }
        };

        // The other half of the pair - see RaceControlControl.
        var btnRaceControl = new Button { Text = "Race Control", Anchor = AnchorStyles.Top | AnchorStyles.Left };
        btnRaceControl.Click += (_, _) => RaceControlRequested?.Invoke();

        var btnClear = new Button { Text = "Clear Points", Anchor = AnchorStyles.Top | AnchorStyles.Left };
        btnClear.Click += (_, _) =>
        {
            if (_feed.Count == 0)
            {
                return;
            }

            // The feed lives only on this page - there is no store behind
            // it - so the undo has to put it back here. It says so plainly if
            // the page has gone rather than appearing to work.
            var cleared = _feed.ToList();
            _undo?.Capture($"Clear {Plural.Of(cleared.Count, "talking point")}", () =>
            {
                if (IsDisposed || Disposing)
                {
                    throw new InvalidOperationException("The Announcer screen has been closed, so its talking points can't be put back.");
                }

                // Behind anything said since the clear - they're older.
                _feed.AddRange(cleared);
                if (_feed.Count > MaxFeed)
                {
                    _feed.RemoveRange(MaxFeed, _feed.Count - MaxFeed);
                }

                RefreshTalkingPoints();
            });

            _feed.Clear();
            RefreshTalkingPoints();
        };
        AppTheme.LayoutButtonRow(380, 430, 8, _btnRepeat, btnClear, btnRaceControl);

        // Everything under that row sat at a flat 464 / 470 / 494, which
        // was clear of buttons 30 high and two pixels inside buttons 36
        // high - so the doctor dropped the spotlight panel ten pixels every
        // startup. Measured from the row itself now, and shifted as a block
        // so the three keep the spacing they were given.
        var drop = UiLayout.Below(_btnRepeat, UiLayout.RowGap) - _lblSpotlight.Top;

        if (drop > 0)
        {
            _lblSpotlight.Top += drop;
            _lblSummaryCaption.Top += drop;
            _lblLastSummary.Top += drop;
        }

        Controls.AddRange(new Control[]
        {
            _lblState, _lblClock, _lblOnDeck,
            lblOrder, _gridOrder, lblTalk, _lstTalkingPoints,
            lblSpot, _cmbSpotlight, _lblSpotlight, _lblSummaryCaption, _lblLastSummary, _btnRepeat, btnClear, btnRaceControl,
        });
    }

    /// <summary>
    /// Raised when Race Control is wanted. MainForm owns the navigation and
    /// wires this to it - this control does not know what pages exist.
    /// </summary>
    public event Action? RaceControlRequested;

    public void RefreshAll()
    {
        RefreshSpotlightList();
        RefreshLive();
    }

    private void OnAnnouncementText(string text)
    {
        if (InvokeRequired)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => AddFeedLine(text));
            }
            return;
        }

        AddFeedLine(text);
    }

    private void AddFeedLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _feed.Insert(0, $"{AppTime.TimeOfDay(DateTime.Now)}   {text}");

        if (_feed.Count > MaxFeed)
        {
            _feed.RemoveRange(MaxFeed, _feed.Count - MaxFeed);
        }

        RefreshTalkingPoints();
    }

    /// <summary>
    /// Measures each row against the list's real width so its height fits
    /// the wrapped text. Without this the ListBox gives every row one line
    /// and silently cuts off anything longer - which hit the generated
    /// commentary line hardest, since that's both the longest and the one
    /// actually meant to be read aloud.
    /// </summary>
    private void TalkingPoints_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstTalkingPoints.Items.Count)
        {
            return;
        }

        var text = _lstTalkingPoints.Items[e.Index]?.ToString() ?? string.Empty;

        // Leave room for the scrollbar, which would otherwise overlap the
        // last few characters of every wrapped line.
        var width = _lstTalkingPoints.ClientSize.Width - 24;

        var size = TextRenderer.MeasureText(
            e.Graphics, text, _lstTalkingPoints.Font,
            new Size(Math.Max(40, width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        e.ItemHeight = Math.Max(_lstTalkingPoints.Font.Height + 4, size.Height + 6);
        e.ItemWidth = width;
    }

    private void TalkingPoints_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstTalkingPoints.Items.Count)
        {
            return;
        }

        var text = _lstTalkingPoints.Items[e.Index]?.ToString() ?? string.Empty;

        e.DrawBackground();

        // Generated commentary is quoted and sits first; computed
        // observations are prefixed with "*". Colouring them differently
        // keeps the distinction visible now that rows are variable height
        // and no longer line up as a uniform list.
        var color = text.StartsWith('\u201C') ? AppTheme.Primary
            : text.StartsWith('*') ? AppTheme.TextDark
            : AppTheme.TextMuted;

        var bounds = Rectangle.Inflate(e.Bounds, -6, -3);
        TextRenderer.DrawText(
            e.Graphics, text, e.Font ?? _lstTalkingPoints.Font, bounds, color,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        e.DrawFocusRectangle();
    }

    private void RefreshTalkingPoints()
    {
        _lstTalkingPoints.BeginUpdate();
        _lstTalkingPoints.Items.Clear();

        // The LLM's suggested phrasing goes first when there is one - it's
        // the thing most likely to be read out verbatim. Marked distinctly
        // so it's never mistaken for a computed fact: the observations below
        // are measured, this one is written.
        if (!string.IsNullOrWhiteSpace(_suggestedLine))
        {
            _lstTalkingPoints.Items.Add($"\u201C{_suggestedLine}\u201D");
        }

        // Generated observations first - they're the ones that need saying
        // NOW, where the feed below is a record of what already happened.
        foreach (var point in GenerateObservations())
        {
            _lstTalkingPoints.Items.Add($"* {point}");
        }

        foreach (var line in _feed)
        {
            _lstTalkingPoints.Items.Add(line);
        }

        _lstTalkingPoints.EndUpdate();
    }

    /// <summary>
    /// Reads the current standings for things worth mentioning right now.
    ///
    /// Kept to a handful: an announcer scanning a list of twenty
    /// observations mid-race is no better off than reading the grid.
    /// </summary>
    private List<string> GenerateObservations()
    {
        var points = new List<string>();

        if (_race.State != RaceState.Racing)
        {
            return points;
        }

        var ordered = _race.GetOrderedStandings();
        if (ordered.Count == 0)
        {
            return points;
        }

        // A close fight for the lead is the single most useful thing to know.
        // Gaps measured at the line (see RaceGaps), not by comparing lap
        // counts - which differ for everyone the moment the leader crosses.
        if (ordered.Count >= 2 && RaceGaps.Between(ordered[1], ordered[0]) is { LapsDown: 0, GapMs: { } leadGapMs } && leadGapMs < 2000)
        {
            points.Add($"{NameFor(ordered[0].TransponderId)} leads {NameFor(ordered[1].TransponderId)} by only {leadGapMs / 1000.0:0.00}s");
        }

        // Anyone actually lapped.
        if (ordered.Count >= 2 && RaceGaps.Between(ordered[^1], ordered[0]).LapsDown is var lapsDown and >= 1)
        {
            points.Add($"{NameFor(ordered[^1].TransponderId)} is {lapsDown} lap{(lapsDown == 1 ? "" : "s")} down");
        }

        // Who holds the race's fastest lap.
        if (_race.RaceFastestLapMs > 0 && !string.IsNullOrWhiteSpace(_race.RaceFastestLapTransponder))
        {
            points.Add($"Fastest lap: {NameFor(_race.RaceFastestLapTransponder)} at {_race.RaceFastestLapMs / 1000.0:0.000}s");
        }

        // Close battles further down, which are easy to miss.
        for (var i = 1; i < ordered.Count - 1 && points.Count < 6; i++)
        {
            if (RaceGaps.Between(ordered[i + 1], ordered[i]) is not { LapsDown: 0, GapMs: { } battleGapMs })
            {
                continue;
            }

            var gap = battleGapMs / 1000.0;
            if (gap < 1.0)
            {
                points.Add($"P{i + 1} battle: {NameFor(ordered[i].TransponderId)} and {NameFor(ordered[i + 1].TransponderId)} split by {gap:0.00}s");
            }
        }

        return points;
    }

    private void RefreshLive()
    {
        if (IsDisposed || Disposing || !Visible)
        {
            return;
        }

        _lblState.Text = _race.State switch
        {
            RaceState.Racing => $"{_race.Flag.ToString().ToUpperInvariant()} FLAG",
            RaceState.Countdown => "COUNTDOWN",
            RaceState.Staging => "STAGING",
            RaceState.Finished => "FINISHED",
            _ => "IDLE",
        };
        _lblState.ForeColor = _race.Flag switch
        {
            FlagState.Green => AppTheme.Success,
            FlagState.Yellow => AppTheme.Accent,
            FlagState.Red => AppTheme.Danger,
            _ => AppTheme.TextMuted,
        };

        var elapsed = _race.ElapsedRaceTime;
        _lblClock.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        var next = _schedule.All.FirstOrDefault(e => !e.IsComplete);
        _lblOnDeck.Text = next is null
            ? string.Empty
            : $"ON DECK:  {next.ScheduledTime}  {next.ClassName}  {next.Description}".TrimEnd();

        RefreshOrder();
        RefreshTalkingPoints();
        RefreshSpotlight();
        MaybeRequestSuggestion();
        RefreshRepeatButton();
    }

    /// <summary>Keeps the Repeat button's state, label and displayed text matched to whatever CommentaryService last generated.</summary>
    private void RefreshRepeatButton()
    {
        // Mirrors the click handler's own preference exactly, so the text
        // shown below can never differ from the line that gets spoken.
        var summary = _commentary?.LastRaceSummary;
        var line = summary ?? _commentary?.LastLine;

        _btnRepeat.Enabled = !string.IsNullOrWhiteSpace(line);

        // The label tracks what it will actually say - falling back to the
        // generic wording when no summary exists yet avoids promising a
        // race summary it can't deliver.
        _btnRepeat.Text = summary is { Length: > 0 } ? "Repeat Race Summary" : "Repeat Last Line";

        // Shows the same line the button speaks, so what you'll hear is
        // visible at a glance. The caption tracks which KIND of line
        // it is, since the button falls back to the last talking point when
        // no summary exists - and a heading saying "race summary" over a
        // talking point would be quietly wrong.
        if (line is { Length: > 0 } shown)
        {
            _lblLastSummary.Text = $"\u201C{shown}\u201D";
            _lblLastSummary.ForeColor = summary is { Length: > 0 } ? AppTheme.TextDark : AppTheme.TextMuted;
            _lblSummaryCaption.Text = summary is { Length: > 0 }
                ? "LAST RACE SUMMARY"
                : $"LAST LINE ({_commentary?.LastLineKind?.ToUpperInvariant()})";
        }
        else
        {
            _lblSummaryCaption.Text = "LAST RACE SUMMARY";
            _lblLastSummary.Text = _commentary?.IsAvailable == true
                ? "No race summary yet - one is generated after the checkered flag."
                : "Commentary is off. Enable it on the Commentary page.";
            _lblLastSummary.ForeColor = AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// Asks the model for a suggested line, at most every 20 seconds and
    /// never while one is already in flight.
    ///
    /// Both guards matter: this method runs twice a second, and a local
    /// model takes seconds to answer - without them, requests would pile up
    /// far faster than they complete. Fire-and-forget by design, since
    /// nothing on screen should wait on it.
    /// </summary>
    private void MaybeRequestSuggestion()
    {
        if (_commentary is null
            || !_commentary.IsAvailable
            || _settings?.CommentaryTalkingPoints != true
            || _race.State != RaceState.Racing
            || _suggestionInFlight
            || (DateTime.UtcNow - _lastSuggestionUtc).TotalSeconds < Math.Max(5, _settings?.CommentaryTalkingPointIntervalSeconds ?? 20))
        {
            return;
        }

        var observations = GenerateObservations();
        if (observations.Count == 0)
        {
            return;
        }

        _suggestionInFlight = true;
        _lastSuggestionUtc = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                var line = await _commentary.BuildTalkingPointAsync(observations);

                if (line is not null && IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(() =>
                    {
                        _suggestedLine = line;
                        RefreshTalkingPoints();
                    });
                }
            }
            catch
            {
                // Suggestions are decoration. The client already swallows
                // its own failures and returns null; anything reaching here
                // is unexpected, and still must not disturb the page.
            }
            finally
            {
                _suggestionInFlight = false;
            }
        });
    }

    private void RefreshOrder()
    {
        var ordered = _race.GetOrderedStandings();

        // Rebuilding the whole grid twice a second would fight the user's
        // selection, so rows are only added or removed when the count
        // changes and cell values updated in place otherwise.
        if (_gridOrder.Rows.Count != ordered.Count)
        {
            _gridOrder.Rows.Clear();
            foreach (var _ in ordered)
            {
                _gridOrder.Rows.Add();
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var row = _gridOrder.Rows[i];

            var gap = i == 0
                ? "-"
                : RaceGaps.Format(RaceGaps.Between(s, ordered[0]));

            row.Cells[0].Value = i + 1;
            row.Cells[1].Value = NameFor(s.TransponderId);
            row.Cells[2].Value = s.Laps;
            row.Cells[3].Value = gap;
            row.Cells[4].Value = s.LapTimesMs.Count > 0 ? $"{s.LapTimesMs[^1] / 1000.0:0.000}" : "-";
            row.Tag = s.TransponderId;
        }
    }

    private void RefreshSpotlightList()
    {
        var previous = (_cmbSpotlight.SelectedItem as SpotlightItem)?.Transponder;

        _cmbSpotlight.Items.Clear();
        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName))
        {
            _cmbSpotlight.Items.Add(new SpotlightItem(profile.Transponder, profile.DriverName));
        }

        if (previous is not null)
        {
            SelectSpotlight(previous);
        }
        else if (_cmbSpotlight.Items.Count > 0)
        {
            _cmbSpotlight.SelectedIndex = 0;
        }
    }

    private void SelectSpotlight(string transponder)
    {
        for (var i = 0; i < _cmbSpotlight.Items.Count; i++)
        {
            if (_cmbSpotlight.Items[i] is SpotlightItem item
                && string.Equals(item.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                if (_cmbSpotlight.SelectedIndex != i)
                {
                    _cmbSpotlight.SelectedIndex = i;
                }
                return;
            }
        }
    }

    /// <summary>
    /// A short story about one driver: how they're running now, and how that
    /// compares with their history. The comparison is the point - "4 tenths
    /// off his personal best" is worth saying, a bare lap time isn't.
    /// </summary>
    private void RefreshSpotlight()
    {
        if (_cmbSpotlight.SelectedItem is not SpotlightItem item)
        {
            _lblSpotlight.Text = string.Empty;
            return;
        }

        var profile = _profiles.Get(item.Transponder);
        var lines = new List<string>();

        var standing = _race.GetOrderedStandings()
            .Select((s, i) => (Standing: s, Position: i + 1))
            .FirstOrDefault(x => string.Equals(x.Standing.TransponderId, item.Transponder, StringComparison.OrdinalIgnoreCase));

        if (standing.Standing is not null)
        {
            var s = standing.Standing;
            lines.Add($"P{standing.Position}   {Plural.Of(s.Laps, "lap")}"
                      + (s.FastestLapMs > 0 ? $"   best this race {s.FastestLapMs / 1000.0:0.000}s" : ""));

            if (s.LapTimesMs.Count >= 3)
            {
                var recent = s.LapTimesMs.TakeLast(3).ToList();
                lines.Add($"Last three: {string.Join(", ", recent.Select(l => $"{l / 1000.0:0.000}"))}");
            }
        }

        // All-time best across recorded sessions, for context. Saved demo
        // races are left out - their lap times are invented, so quoting
        // one as somebody's personal best would be quoting a made-up lap.
        var allTime = _history.All
            .Where(sess => !sess.IsDemoSession())
            .SelectMany(sess => sess.Results.Where(r =>
                string.Equals(r.Transponder, item.Transponder, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r.FastLapMs > 0)
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        if (allTime > 0)
        {
            var delta = standing.Standing?.FastestLapMs > 0
                ? standing.Standing.FastestLapMs - allTime
                : 0;

            lines.Add(delta switch
            {
                < 0 => $"Personal best {allTime / 1000.0:0.000}s - and they've just beaten it.",
                0 => $"Personal best {allTime / 1000.0:0.000}s.",
                _ => $"Personal best {allTime / 1000.0:0.000}s - running {delta / 1000.0:0.000}s off it today.",
            });
        }

        if (!string.IsNullOrWhiteSpace(profile?.CarModel))
        {
            lines.Add($"Car: {profile!.CarModel}"
                      + (string.IsNullOrWhiteSpace(profile.ClassName) ? "" : $"   Class: {profile.ClassName}"));
        }

        if (!string.IsNullOrWhiteSpace(profile?.Notes))
        {
            lines.Add($"Note: {profile!.Notes.Replace('\r', ' ').Replace('\n', ' ')}");
        }

        _lblSpotlight.Text = lines.Count == 0
            ? "No data for this driver yet."
            : string.Join(Environment.NewLine, lines);
    }

    private string NameFor(string transponder)
    {
        var profile = _profiles.Get(transponder);
        return string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refresh.Stop();
            _refresh.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record SpotlightItem(string Transponder, string DriverName)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(DriverName) ? Transponder : DriverName;
    }
}
