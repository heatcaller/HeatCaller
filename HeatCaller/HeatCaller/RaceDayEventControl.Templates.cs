namespace HeatCaller;

/// <summary>
/// Setup's Templates tab (14 Nov 2026, direction A of the Setup rethink):
/// where a night starts. Tonight's choice on a banner at the top, with its
/// forecast and one Start button; under it a card for every way to start -
/// like last time, each template, something different - saying what it
/// starts. It replaces New Race Day beside the day picker and the templates
/// row half-way down the old page.
///
/// Starting from here is the one path every door uses (StartTonightAs, one
/// undo entry). While a night is open with something in it, nothing here
/// starts another: the banner says so and points to Close Day (Asher, 14 Nov
/// 2026 - a safeguard, so a second night is never started over a running one
/// by a stray click).
/// </summary>
public sealed partial class RaceDayEventControl
{
    private readonly Button _btnSaveTemplate = new() { AutoSize = true };

    private readonly Section _banner = new("Tonight");
    private readonly Label _bannerDate = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _bannerTitle = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _bannerDetail = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _bannerForecast = new() { AutoSize = true, UseMnemonic = false };
    private readonly Button _btnBannerStart = new() { Text = "Start Tonight", AutoSize = true };
    private readonly Button _btnGoToCloseDay = new() { Text = "Go to Close Day", AutoSize = true };
    private readonly CardGrid _cards = new();
    private readonly Button _btnNewTemplate = new() { Text = "New Template...", AutoSize = true };
    private readonly Button _btnPastNights = new() { Text = "Past Nights...", AutoSize = true };

    /// <summary>What the banner's Start does - tonight's choice.</summary>
    private StartTonightOption? _bannerChoice;

    /// <summary>
    /// Tonight's pre-entries from the phones (MainForm.ComingTonightList), for
    /// the forecast - the best guess at turnout there is. Null leaves them out.
    /// </summary>
    public Func<IReadOnlyList<PreEntry>>? ComingTonight { get; set; }

    /// <summary>Raised after a night is started from this tab, so the host can bring the other pages up to date.</summary>
    public event Action? NightStarted;

    private void BuildTemplatesPage()
    {
        var intro = new Label
        {
            Text = "Start tonight from one of the club's nights. A template keeps a night's classes, heats, times, breaks and fees - each card says what it starts.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _btnNewTemplate.Click += (_, _) => NewTemplate();
        _btnPastNights.Click += (_, _) => ShowPastNights();
        _btnBannerStart.Click += (_, _) =>
        {
            if (_bannerChoice is { } choice)
            {
                StartFromShelf(choice);
            }
        };
        _btnGoToCloseDay.Click += (_, _) =>
        {
            // The open night, when a past night being looked at has taken
            // the day picker off it.
            if (OpenNight() is { } open && !string.Equals(Selected?.Id, open.Id, StringComparison.Ordinal))
            {
                ShowDay(open.Id);
            }

            ShowTab(TonightTab);
            _tonightPage.ScrollControlIntoView(_btnCloseDay);
            _btnCloseDay.Focus();
        };

        AppTheme.StyleButton(_btnBannerStart, AppTheme.ButtonKind.Primary);

        _textSize.Wear(_bannerTitle, () => AppTheme.HeadingFont);
        _bannerDate.ForeColor = AppTheme.TextMuted;
        _bannerDetail.ForeColor = AppTheme.TextMuted;

        _banner.Add(_bannerDate, gapBelow: UiLayout.HintGap);
        _banner.Add(_bannerTitle, gapBelow: UiLayout.HintGap);
        _banner.Add(_bannerDetail, gapBelow: UiLayout.HintGap);
        _banner.Add(_bannerForecast, gapBelow: UiLayout.RowGap);
        _banner.Add(new ButtonBar().With(_btnBannerStart, _btnWizard, _btnGoToCloseDay), gapBelow: 0);

        _templatesPage.Add(intro, gapBelow: UiLayout.RowGap);
        _templatesPage.Add(new ButtonBar().With(_btnNewTemplate, _btnPastNights), gapBelow: UiLayout.RowGap);
        _templatesPage.Add(_banner);
        _templatesPage.Add(_cards, gapBelow: 0);

        // The cards are built in the fonts of the text size in use, so a new
        // size builds them again.
        _textSize.Keep(RefreshShelf);
    }

    /// <summary>
    /// The night open now with something in it - classes ticked, or rounds -
    /// which nothing on this tab starts a night over. Null when none is. An
    /// open day with nothing in it at all (older installs opened one by
    /// themselves) is taken over by the next start, as it always was.
    ///
    /// The current day first, then any other open one: looking at a past
    /// night (Past Nights..., the day picker) makes that closed day current,
    /// and tonight's still counts as open then (bug hunt 19 #9).
    /// </summary>
    private RaceEvent? OpenNight() => OpenNightOf(_events.Active, _events.Real);

    /// <summary>
    /// OpenNight's rule on its own, so it can be tested without the page
    /// (bug hunt 24: the Start Tonight window and the tab choice use it too).
    /// </summary>
    internal static RaceEvent? OpenNightOf(RaceEvent? active, IEnumerable<RaceEvent> real)
    {
        static bool InUse(RaceEvent day) =>
            day is { IsLegacy: false, IsOpen: true } && !(day.RoundIds.Count == 0 && day.ClassNames.Count == 0);

        return active is not null && InUse(active)
            ? active
            : real.Where(InUse).OrderByDescending(d => d.StartedUtc).FirstOrDefault();
    }

    /// <summary>The banner and the cards, from the templates and race days as they are now.</summary>
    private void RefreshShelf()
    {
        if (IsDisposed)
        {
            return;
        }

        var previous = LastRealDay();
        var templates = _settings?.NightTemplates ?? new List<NightTemplate>();
        var options = StartTonight.Options(previous, templates);
        var tonights = NightTemplates.ForDay(templates, DateTime.Today);
        var picked = options[Math.Clamp(StartTonight.Preselect(options, tonights), 0, options.Count - 1)];
        var open = OpenNight();

        _bannerDate.Text = DateTime.Today.ToString("dddd d MMMM");

        if (open is not null)
        {
            _bannerChoice = null;
            _bannerTitle.Text = $"{open.DisplayName} is open";
            _bannerDetail.Text = TemplateCardText.OpenNightWords;
            _bannerForecast.Visible = false;
            _btnBannerStart.Visible = false;
            _btnGoToCloseDay.Visible = true;
        }
        else
        {
            _bannerChoice = picked;
            _bannerTitle.Text = TemplateCardText.BannerTitle(picked, tonights);
            _bannerDetail.Text = string.Join("  ·  ", Lines(picked).Where(l => l.Length > 0).Take(3));
            _btnBannerStart.Text = picked.Kind == StartTonightKind.Different ? "Choose..." : "Start Tonight";
            _btnBannerStart.Visible = true;
            _btnGoToCloseDay.Visible = false;
            ShowForecast(_bannerForecast, picked, tonight: true);
        }

        _cards.SuspendLayout();

        try
        {
            _cards.Clear();

            foreach (var option in options)
            {
                var isTonight = option.Kind == StartTonightKind.Template && ReferenceEquals(option.Template, tonights);
                _cards.Controls.Add(Card(option, isTonight, blocked: open is not null));
            }
        }
        finally
        {
            _cards.ResumeLayout(true);
        }

        _templatesPage.PerformLayout();
    }

    /// <summary>The banner's heading as it stands - for the tests.</summary>
    internal string BannerTitle => _bannerTitle.Text;

    /// <summary>Whether the banner offers Start - for the tests.</summary>
    internal bool BannerOffersStart => _btnBannerStart.Visible;

    /// <summary>The cards' captions and whether each one's Start is on, in order - for the tests.</summary>
    internal IReadOnlyList<(string Caption, bool CanStart)> ShelfCards =>
        _cards.Controls.OfType<Section>()
            .Select(card => (
                card.Controls.OfType<Label>().FirstOrDefault()?.AccessibleName ?? string.Empty,
                card.Controls.OfType<ButtonBar>().SelectMany(bar => bar.Controls.OfType<Button>()).FirstOrDefault()?.Enabled == true))
            .ToList();

    /// <summary>What a card says about a choice, line by line.</summary>
    private IReadOnlyList<string> Lines(StartTonightOption option) => option switch
    {
        { Kind: StartTonightKind.Template, Template: { } t } => TemplateCardText.Lines(
            t.Shape == RaceDayShape.ClubNight, t.ClassNames, Rounds(t.QualifyingRounds), HeatSize(t.DriversPerHeat), t.FirstRoundSeeding,
            t.EntriesCloseLocalTime, t.FirstRaceLocalTime, t.OutByLocalTime, t.Breaks, t.EntryFee, t.ExtraClassFee),
        { Kind: StartTonightKind.LikeLast, LastDay: { } d } => TemplateCardText.Lines(
            d.IsClubNight, d.ClassNames, Rounds(d.QualifyingRounds), _events.HeatSizeFor(d), d.FirstRoundSeeding,
            d.EntriesCloseLocalTime, d.FirstRaceLocalTime, d.OutByLocalTime, d.Breaks, d.EntryFee, d.ExtraClassFee),
        _ => new[] { option.Detail },
    };

    /// <summary>
    /// A template's rounds, or for one that never set its own, what a night
    /// from it would race: last night's, which StartDayFrom carries forward
    /// (3 on a club's first). Bug hunt 21: it said a flat 3.
    /// </summary>
    private int Rounds(int rounds) => rounds > 0 ? rounds : LastRealDay() is { QualifyingRounds: > 0 } last ? last.QualifyingRounds : 3;

    private int HeatSize(int perHeat) => perHeat > 0 ? perHeat : _events.HeatSizeFor(LastRealDay());

    /// <summary>One card: what kind of night and when, its name, what it starts, what it would come to, and its buttons.</summary>
    private Section Card(StartTonightOption option, bool isTonight, bool blocked)
    {
        var card = new Section(TemplateCardText.Caption(option, isTonight));

        var title = new Label
        {
            Text = option.Kind switch
            {
                StartTonightKind.LikeLast => option.LastDay?.DisplayName ?? option.Caption,
                StartTonightKind.Different => "Something different...",
                _ => option.Caption,
            },
            AutoSize = true,
            UseMnemonic = false,
            Font = AppTheme.BoldFont,
        };

        card.Add(title, gapBelow: UiLayout.HintGap);

        var lines = Lines(option).Where(l => l.Length > 0).ToList();

        if (option is { Kind: StartTonightKind.LikeLast, LastDay: { } last })
        {
            lines.Add(TemplateCardText.RanLine(
                _checkIns.CheckedInAt(last.Id).Count,
                last.RoundIds.Count(id => _rounds.Get(id)?.IsRecorded == true)));
        }

        card.Add(new Label { Text = string.Join(Environment.NewLine, lines), AutoSize = true, UseMnemonic = false, ForeColor = AppTheme.TextMuted },
            gapBelow: UiLayout.HintGap);

        if (option.Kind != StartTonightKind.Different)
        {
            var forecast = new Label { AutoSize = true, UseMnemonic = false, Font = AppTheme.SmallFont };
            ShowForecast(forecast, option, isTonight);
            card.Add(forecast, gapBelow: UiLayout.RowGap);
        }

        var start = new Button { Text = option.Kind == StartTonightKind.Different ? "Choose..." : "Start Tonight", AutoSize = true, Enabled = !blocked };
        start.Click += (_, _) => StartFromShelf(option);

        var buttons = new ButtonBar().With(start);

        switch (option)
        {
            case { Kind: StartTonightKind.Template, Template: { } template }:
                var edit = new Button { Text = "Edit...", AutoSize = true, Enabled = _settings is not null };
                edit.Click += (_, _) => EditTemplate(template);

                var more = new Button { Text = "More...", AutoSize = true, Enabled = _settings is not null };
                more.Click += (_, _) => TemplateMenu(template).Show(more, new Point(0, more.Height));

                buttons.With(edit, more);
                break;

            case { Kind: StartTonightKind.LikeLast, LastDay: { } day }:
                var save = new Button { Text = "Save as Template...", AutoSize = true, Enabled = _settings is not null };
                save.Click += (_, _) => SaveAsTemplate(day);
                buttons.With(save);
                break;
        }

        card.Add(buttons, gapBelow: 0);

        AppTheme.ApplyRecursively(card);

        if (isTonight && !blocked)
        {
            AppTheme.StyleButton(start, AppTheme.ButtonKind.Primary);
        }

        return card;
    }

    /// <summary>A forecast line for a choice, or the label hidden when there's nothing to go by.</summary>
    private void ShowForecast(Label label, StartTonightOption option, bool tonight)
    {
        var said = Forecast(option, tonight);

        label.Visible = said is not null;
        label.Text = said?.Line ?? string.Empty;
        label.ForeColor = said is { Late: true } ? AppTheme.Warning : AppTheme.TextMuted;
    }

    /// <summary>What starting a choice tonight would come to - see TemplateForecast. Null when there's nothing to go by.</summary>
    private (string Line, bool Late)? Forecast(StartTonightOption option, bool tonight) => option switch
    {
        { Kind: StartTonightKind.Template, Template: { } t } => Forecast(t, tonight),
        { Kind: StartTonightKind.LikeLast, LastDay: { } d } => Forecast(NightTemplates.From(d, d.TemplateName ?? string.Empty), tonight),
        _ => null,
    };

    /// <summary>What a night from <paramref name="template"/> would come to. Its name finds the nights like it.</summary>
    private (string Line, bool Late)? Forecast(NightTemplate template, bool tonight)
    {
        var turnout = TemplateForecast.Turnout(
            template.Name,
            template.ClassNames,
            _events.Real,
            day => day.RoundIds.Select(id => _rounds.Get(id)).OfType<RoundDefinition>(),
            tonight ? ComingTonight?.Invoke() : null);

        if (turnout is null)
        {
            return null;
        }

        var appDefault = _settings is null ? new RaceFormat() : RaceFormat.FromSettings(_settings);

        RaceClass? ClassNamed(string name) =>
            _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        var forecast = TemplateForecast.For(
            template.Shape == RaceDayShape.ClubNight,
            turnout.PerClass,
            Rounds(template.QualifyingRounds),
            HeatSize(template.DriversPerHeat),
            name => NightPlan.LengthOf(ClassNamed(name)?.QualifierFormat ?? ClassNamed(name)?.Format ?? appDefault),
            name => NightPlan.LengthOf(ClassNamed(name)?.Format ?? appDefault),
            template.Breaks,
            template.FirstRaceLocalTime,
            template.OutByLocalTime);

        return forecast is null ? null : (TemplateForecast.Line(forecast, turnout.Source), TemplateForecast.RunsLate(forecast));
    }

    /// <summary>
    /// Starts tonight the way a card or the banner says - refused while a
    /// night is open with something in it (see the class summary).
    /// </summary>
    private void StartFromShelf(StartTonightOption option)
    {
        FlushPendingEdits();

        if (OpenNight() is { } open)
        {
            AppDialog.Show(this, TemplateCardText.RefusedWords(open.DisplayName), "Start tonight", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshShelf();
            return;
        }

        if (option.Kind == StartTonightKind.Different)
        {
            // The two questions still need asking, so the window opens for them.
            if (!StartTonightWindow(StartTonightKind.Different, null))
            {
                return;
            }
        }
        else
        {
            var previous = LastRealDay();
            StartTonightAs(option, previous?.Shape ?? RaceDayShape.RaceDay, previous?.ClassNames ?? new List<string>());
        }

        RefreshAll();
        NightStarted?.Invoke();
    }

    /// <summary>When..., Duplicate and Delete, for a template's card.</summary>
    private ContextMenuStrip TemplateMenu(NightTemplate template)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("When...", null, (_, _) => EditTemplateDays(template));
        menu.Items.Add("Duplicate", null, (_, _) => DuplicateTemplate(template));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete...", null, (_, _) => DeleteTemplate(template));
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        AppTheme.StyleContextMenu(menu);
        return menu;
    }

    /// <summary>The last few race days, newest first: picking one shows it on Tonight (and makes it current, as the day picker always has).</summary>
    private void ShowPastNights()
    {
        var menu = new ContextMenuStrip();

        foreach (var day in _events.Real.OrderByDescending(d => d.StartedUtc).Take(10))
        {
            var state = day.IsOpen ? "open" : "closed";
            menu.Items.Add($"{day.DisplayName}  ({day.StartedUtc.ToLocalTime():ddd d MMM}, {state})", null, (_, _) => ShowDay(day.Id));
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("No race days yet") { Enabled = false });
        }

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        AppTheme.StyleContextMenu(menu);
        menu.Show(_btnPastNights, new Point(0, _btnPastNights.Height));
    }

    /// <summary>Picks a day in Tonight's day picker - which switches to it, with its undo entry - and shows Tonight.</summary>
    private void ShowDay(string id)
    {
        var index = _cmbEvent.Items.Cast<Choice>().ToList().FindIndex(c => string.Equals(c.Event.Id, id, StringComparison.Ordinal));

        if (index >= 0)
        {
            _cmbEvent.SelectedIndex = index;
        }

        ShowTab(TonightTab);
    }

    private void ShowTemplateButtons() =>
        _btnSaveTemplate.Enabled = _settings is not null && Selected is { IsLegacy: false };

    /// <summary>
    /// Next Steps' "Start tonight's ..." (setup plan step 7): the Start Tonight
    /// window, opened on that template (setup plan step 8). False when there
    /// is no such template or the window was cancelled.
    /// </summary>
    public bool StartFromTemplateNamed(string name) =>
        _settings?.NightTemplates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) == true
        && StartTonightWindow(StartTonightKind.Template, name);

    /// <summary>
    /// Starts a race day from a template: New Race Day's own path, then the
    /// template's settings over it, and the template's name kept on the day
    /// for the forecast's "nights like it".
    /// </summary>
    private void StartFromTemplate(NightTemplate template)
    {
        StartDayFrom(LastRealDay(), template.Shape, template.ClassNames.ToList());

        if (_events.Active is { IsLegacy: false } day)
        {
            NightTemplates.ApplyTo(template, day);
            day.TemplateName = template.Name.Trim();
            _events.Upsert(day);
        }

        RefreshAll();
        NightSetupChanged?.Invoke();
    }

    /// <summary>Tonight's Save as Template...: the day on show.</summary>
    private void SaveAsTemplate()
    {
        if (Selected is { IsLegacy: false } day)
        {
            FlushPendingEdits();
            SaveAsTemplate(day);
        }
    }

    private void SaveAsTemplate(RaceEvent day)
    {
        if (_settings is null || day.IsLegacy)
        {
            return;
        }

        var name = AskForName(day.TemplateName ?? (day.IsClubNight ? "Club night" : "Race day"));

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // A template saved again keeps the nights it was set for (setup plan step 7).
        var fresh = NightTemplates.From(day, name);

        if (_settings.NightTemplates.FirstOrDefault(t => string.Equals(t.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            fresh.Weekdays = (existing.Weekdays ?? new List<DayOfWeek>()).ToList();
            fresh.Dates = (existing.Dates ?? new List<DateTime>()).ToList();
        }

        _settings.NightTemplates = NightTemplates.Saved(_settings.NightTemplates, fresh);
        _settings.Save();
        RefreshShelf();
        NightSetupChanged?.Invoke();
    }

    /// <summary>New Template...: the editor, starting from the last race day's settings (or the defaults on a club's first).</summary>
    private void NewTemplate()
    {
        if (_settings is null)
        {
            return;
        }

        var start = LastRealDay() is { } last ? NightTemplates.From(last, string.Empty) : new NightTemplate();
        start.Weekdays = new List<DayOfWeek>();
        start.Dates = new List<DateTime>();

        if (OpenEditor(start, isNew: true) is { } made)
        {
            _settings.NightTemplates = NightTemplates.Saved(_settings.NightTemplates, made);
            _settings.Save();
            RefreshShelf();
            NightSetupChanged?.Invoke();
        }
    }

    /// <summary>Edit...: the template's fields, saved over it - under its new name, when it was renamed.</summary>
    private void EditTemplate(NightTemplate template)
    {
        if (_settings is null || OpenEditor(template, isNew: false) is not { } edited)
        {
            return;
        }

        _settings.NightTemplates = NightTemplates.Saved(
            _settings.NightTemplates.Where(t => !ReferenceEquals(t, template)),
            edited);
        _settings.Save();

        // The nights started from it follow a rename, so its forecast keeps
        // their history (bug hunt 21) - see RaceEventStore.RenameTemplate.
        _events.RenameTemplate(template.Name, edited.Name);
        RefreshShelf();
        NightSetupChanged?.Invoke();
    }

    private NightTemplate? OpenEditor(NightTemplate template, bool isNew)
    {
        var others = (_settings?.NightTemplates ?? new List<NightTemplate>())
            .Where(t => !ReferenceEquals(t, template))
            .Select(t => t.Name)
            .ToList();

        using var form = new TemplateEditForm(
            template,
            isNew,
            others,
            _raceClasses.All.Select(c => c.Name).ToList(),
            _venues.All.Select(v => v.Name),
            _layouts.All.Select(l => l.Name),
            _seasons.All,
            edited => Forecast(edited, tonight: ReferenceEquals(NightTemplates.ForDay(_settings?.NightTemplates, DateTime.Today), template)),

            // What a night from it would use where it sets nothing (bug hunt 21).
            roundsUnset: Rounds(0),
            heatSizeUnset: HeatSize(0));

        return form.ShowDialog(this) == DialogResult.OK ? form.Result : null;
    }

    private void DuplicateTemplate(NightTemplate template)
    {
        if (_settings is null)
        {
            return;
        }

        var copy = System.Text.Json.JsonSerializer.Deserialize<NightTemplate>(System.Text.Json.JsonSerializer.Serialize(template))!;
        copy.Name = TemplateCardText.CopyName(template.Name, _settings.NightTemplates.Select(t => t.Name));

        // A copy is for a different night, so it isn't set for any yet.
        copy.Weekdays = new List<DayOfWeek>();
        copy.Dates = new List<DateTime>();

        _settings.NightTemplates = NightTemplates.Saved(_settings.NightTemplates, copy);
        _settings.Save();
        RefreshShelf();
    }

    private void DeleteTemplate(NightTemplate template)
    {
        if (_settings is null
            || AppDialog.Show(this, $"Delete the template \"{template.Name}\"? Race days started from it aren't changed.", "Delete template",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _settings.NightTemplates = _settings.NightTemplates.Where(t => !ReferenceEquals(t, template)).ToList();
        _settings.Save();
        RefreshShelf();
        NightSetupChanged?.Invoke();
    }

    /// <summary>A small window asking for the template's name. Null when cancelled.</summary>
    private string? AskForName(string suggested)
    {
        using var form = new Form
        {
            Text = "Save as Template",
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
        };

        var lbl = new Label
        {
            Text = "What to call it - \"Tuesday club night\", \"Points round\". It keeps this day's kind of night, classes and running order, "
                   + "rounds, heat size, seeding, fees, times, breaks and between-race talk. A template of the same name is replaced.",
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(420, 0),
        };

        var box = new TextBox { Text = suggested, Width = 300 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lbl, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(box, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        form.Controls.Add(page);
        form.AcceptButton = btnSave;
        form.CancelButton = btnCancel;

        AppTheme.ApplyRecursively(form);
        DialogFit.Keep(form, page, width: 480, height: 240);

        return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

/// <summary>Which nights a template is (12 Nov 2026, setup plan step 7): days of the week, and particular dates.</summary>
public sealed partial class RaceDayEventControl
{
    private void EditTemplateDays(NightTemplate template)
    {
        if (_settings is null)
        {
            return;
        }

        using var form = new TemplateDaysForm(template);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        template.Weekdays = form.Weekdays.ToList();
        template.Dates = form.Dates.ToList();
        _settings.Save();
        RefreshShelf();
        NightSetupChanged?.Invoke();
    }
}

/// <summary>What the Templates tab's banner and cards say. Pure.</summary>
public static class TemplateCardText
{
    /// <summary>The banner's words while a night is open.</summary>
    public const string OpenNightWords =
        "Close it on the Tonight tab before starting another night. Started by mistake? Ctrl+Z takes it back while nothing has been done with it.";

    /// <summary>What a Start pressed while a night is open says.</summary>
    public static string RefusedWords(string openNight) =>
        $"\"{openNight}\" is still open, so another night can't start over it. Close it first: Go to Close Day on this tab, or Close Day at the bottom of Tonight.";

    /// <summary>The banner's heading for tonight's choice.</summary>
    public static string BannerTitle(StartTonightOption choice, NightTemplate? tonights) => choice switch
    {
        { Kind: StartTonightKind.Template, Template: { } t } when ReferenceEquals(t, tonights) => $"{t.Name.Trim()} is set for tonight",
        { Kind: StartTonightKind.Template } => choice.Caption,
        { Kind: StartTonightKind.LikeLast } => $"Like last time - {choice.LastDay?.DisplayName}",
        _ => "Your first night: pick the kind of night and the classes",
    };

    /// <summary>A card's caption: the kind of night and when it's on - "TONIGHT · RACE DAY · SATURDAYS".</summary>
    public static string Caption(StartTonightOption option, bool tonight)
    {
        var parts = new List<string>();

        if (tonight)
        {
            parts.Add("Tonight");
        }

        switch (option)
        {
            case { Kind: StartTonightKind.Template, Template: { } t }:
                parts.Add(t.Shape == RaceDayShape.ClubNight ? "Club night" : "Race day");
                parts.Add(NightTemplates.WhenWords(t) is { Length: > 0 } when ? when : "No nights set");
                break;

            case { Kind: StartTonightKind.LikeLast, LastDay: { } d }:
                parts.Add("Like last time");
                parts.Add(d.StartedUtc.ToLocalTime().ToString("ddd d MMM"));
                break;

            default:
                parts.Add("Something different");
                break;
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// What a night would start, a line each: the classes; heats (or races),
    /// heat size and seeding; the times set; the breaks; the fees. A line
    /// with nothing to say is left out.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        bool clubNight,
        IReadOnlyList<string> classes,
        int rounds,
        int perHeat,
        FirstRoundSeeding seeding,
        TimeSpan? entriesClose,
        TimeSpan? firstRace,
        TimeSpan? outBy,
        IReadOnlyList<PlannedBreak>? breaks,
        decimal entryFee,
        decimal extraFee)
    {
        var lines = new List<string>
        {
            classes.Count == 0 ? "No classes ticked" : string.Join(", ", classes),
            clubNight
                ? $"{Plural.Of(rounds, "race")} each, up to {perHeat} a race"
                : $"{Plural.Of(rounds, "heat")} each, up to {perHeat} a heat, first round {FirstRound.Words(seeding).ToLowerInvariant()}",
        };

        var times = new List<string>();

        if (entriesClose is { } close)
        {
            times.Add($"entries close {AppTime.TimeOfDay(close)}");
        }

        if (firstRace is { } first)
        {
            times.Add($"first race {AppTime.TimeOfDay(first)}");
        }

        if (outBy is { } by)
        {
            times.Add($"out by {AppTime.TimeOfDay(by)}");
        }

        if (times.Count > 0)
        {
            var said = string.Join(" · ", times);
            lines.Add(char.ToUpperInvariant(said[0]) + said[1..]);
        }

        if (breaks is { Count: > 0 })
        {
            lines.Add(NightBreaks.Summary(breaks, clubNight));
        }

        if (entryFee > 0 || extraFee > 0)
        {
            lines.Add(extraFee > 0
                ? $"{DeskMoney.Money(entryFee)} entry, {DeskMoney.Money(extraFee)} each extra class"
                : $"{DeskMoney.Money(entryFee)} entry");
        }

        return lines;
    }

    /// <summary>How the last night went, for its card: "Ran: 31 checked in · 22 races".</summary>
    public static string RanLine(int checkedIn, int racesRun) =>
        $"Ran: {checkedIn} checked in · {Plural.Of(racesRun, "race")}";

    /// <summary>A name for a template's copy no other template has: "Points round (copy)", then "(copy 2)".</summary>
    public static string CopyName(string name, IEnumerable<string> taken)
    {
        var names = new HashSet<string>(taken.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        var candidate = $"{name.Trim()} (copy)";

        for (var n = 2; names.Contains(candidate); n++)
        {
            candidate = $"{name.Trim()} (copy {n})";
        }

        return candidate;
    }
}


/// <summary>A template's nights: a tick per day of the week, and a list of dates.</summary>
internal sealed class TemplateDaysForm : Form
{
    private readonly List<(DayOfWeek Day, CheckBox Box)> _days = new();
    private readonly ListBox _lstDates = new() { Height = 110, Width = 200, IntegralHeight = false };
    private readonly DateTimePicker _dtpDate = new() { Format = DateTimePickerFormat.Short, Width = 130 };

    public IEnumerable<DayOfWeek> Weekdays => _days.Where(d => d.Box.Checked).Select(d => d.Day);

    public IEnumerable<DateTime> Dates => _lstDates.Items.Cast<DateTime>();

    public TemplateDaysForm(NightTemplate template)
    {
        Text = $"When - {template.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        var week = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new System.Drawing.Size(460, 0), Tag = AppTheme.KeepBackColor };

        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var box = new ThemedCheckBox { Text = format.GetAbbreviatedDayName(day), AutoSize = true, Checked = (template.Weekdays ?? new List<DayOfWeek>()).Contains(day) };
            _days.Add((day, box));
            week.Controls.Add(box);
        }

        foreach (var date in (template.Dates ?? new List<DateTime>()).Select(d => d.Date).Where(d => d >= DateTime.Today).Distinct().OrderBy(d => d))
        {
            _lstDates.Items.Add(date);
        }

        _lstDates.FormatString = "ddd d MMM yyyy";
        _lstDates.FormattingEnabled = true;
        _dtpDate.MinDate = DateTime.Today;

        var btnAdd = new Button { Text = "Add Date", AutoSize = true };
        btnAdd.Click += (_, _) =>
        {
            var date = _dtpDate.Value.Date;

            if (!_lstDates.Items.Cast<DateTime>().Contains(date))
            {
                var at = _lstDates.Items.Cast<DateTime>().Count(d => d < date);
                _lstDates.Items.Insert(at, date);
            }
        };

        var btnRemove = new Button { Text = "Remove", AutoSize = true };
        btnRemove.Click += (_, _) =>
        {
            if (_lstDates.SelectedIndex >= 0)
            {
                _lstDates.Items.RemoveAt(_lstDates.SelectedIndex);
            }
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        // Two cards (18 Nov 2026, dialog pass 4): the days of the week, and
        // the dates with their picker under them, each with a line on what
        // it does - the note that ran across the top shared out between
        // them. The list takes whatever height the window has to spare.
        var weekCard = new Section("Every week");
        weekCard.Add(NightSetupDialog.CardHint("On these days, with no race day open, Next Steps offers to start tonight from this template."), gapBelow: UiLayout.HintGap);
        weekCard.Add(week, stretch: false, gapBelow: 0);

        var datesCard = new Section("On these dates");
        datesCard.Add(NightSetupDialog.CardHint("A date wins over a day of the week, so a monthly points round can fall on a club night's Tuesday."), gapBelow: UiLayout.HintGap);
        datesCard.AddFill(_lstDates, () => UiLayout.AtTextSize(110), gapBelow: UiLayout.HintGap);
        datesCard.Add(_lblCount, gapBelow: UiLayout.RowGap);
        datesCard.Add(new ButtonBar().With(_dtpDate, btnAdd, btnRemove), gapBelow: 0);

        ShowCount();
        btnAdd.Click += (_, _) => ShowCount();
        btnRemove.Click += (_, _) => ShowCount();

        var page = new FillPage();
        page.Add(weekCard, gapBelow: UiLayout.RowGap);
        page.AddFill(datesCard, datesCard.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(btnAdd, AppTheme.ButtonKind.Secondary);

        // The date picker as wide as the text size needs: at a fixed 130px
        // the date was cut to "11/..." at 200%.
        _dtpDate.Width = UiLayout.AtTextSize(130);

        DialogFit.Keep(this, page, width: 560, height: 520);
    }

    /// <summary>Under the list: how many dates it holds, or what fills it when it holds none.</summary>
    private readonly Label _lblCount = new() { AutoSize = true, ForeColor = AppTheme.TextMuted, Font = AppTheme.SmallFont, UseMnemonic = false };

    private void ShowCount() =>
        _lblCount.Text = _lstDates.Items.Count switch
        {
            0 => "No dates yet: pick one below and Add Date.",
            1 => "1 date to come.",
            var n => $"{n} dates to come.",
        };
}
