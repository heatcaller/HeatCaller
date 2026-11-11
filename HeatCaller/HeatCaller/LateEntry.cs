namespace HeatCaller;

/// <summary>
/// A late entry without reposting the heats (11 Nov 2026, race-night build
/// plan, Phase 3).
///
/// WHY: Redo Remaining Heats re-deals every heat still to run, so one driver
/// turning up after Q1 moves a dozen others who had already looked at the
/// heat sheet on the wall and their phones. The usual fix at the track is to
/// put the late driver in the smallest heat. This does that, for every
/// qualifying round they haven't raced yet, and touches no other driver.
/// </summary>
public static class LateEntry
{
    /// <summary>
    /// Where a late driver goes: for each qualifying round of the class with
    /// a heat still to run, and that they aren't in already, the smallest
    /// heat still to run (the first of equals). Never a heat on track or
    /// run. Rounds by the number the program gave them (Q2 Heat 1 is round
    /// 2); a heat renamed by hand counts as a round of its own.
    /// </summary>
    /// <param name="classDayRounds">The class's rounds on today's program, in running order.</param>
    /// <param name="transponder">The late driver.</param>
    /// <param name="onTrackRoundId">The round on track now, if any.</param>
    public static List<RoundDefinition> Place(IReadOnlyList<RoundDefinition> classDayRounds, string transponder, string? onTrackRoundId)
    {
        var qualifiers = classDayRounds.Where(r => r is not null && r.Kind == RoundKind.Qualifier).ToList();
        var places = new List<RoundDefinition>();

        foreach (var round in qualifiers.GroupBy(r => (object?)NightPlan.RoundNumber(r.RoundLabel) ?? r.Id))
        {
            if (round.Any(r => r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var open = round
                .Where(r => !r.IsRecorded && !string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal))
                .OrderBy(r => r.AssignedTransponders.Count)
                .FirstOrDefault();

            if (open is not null)
            {
                places.Add(open);
            }
        }

        return places;
    }
}

/// <summary>"Who's the late entry?" - the class's drivers who aren't in any of its heats still to run.</summary>
internal sealed class LateEntryForm : Form
{
    private const int LeastRows = 8;

    private readonly ListBox _lstDrivers = new();
    private readonly Button _btnAdd = new() { Text = "Add to Heats" };

    /// <summary>The driver picked, by transponder. Set on OK.</summary>
    public string? Picked { get; private set; }

    private sealed record Choice(string Transponder, string Label)
    {
        public override string ToString() => Label;
    }

    public LateEntryForm(string className, IEnumerable<(string Transponder, string Name)> drivers)
    {
        Text = $"Late entry - {className}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label
        {
            Text = "Pick the driver who's arrived. They go into the smallest heat of each round still to run, "
                   + "and nobody else in the heats moves. Undo takes them out again.",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
        };

        foreach (var (transponder, name) in drivers)
        {
            _lstDrivers.Items.Add(new Choice(transponder, name));
        }

        _lstDrivers.IntegralHeight = false;
        _lstDrivers.DoubleClick += (_, _) => Add();
        _lstDrivers.SelectedIndexChanged += (_, _) => _btnAdd.Enabled = _lstDrivers.SelectedItem is Choice;
        _btnAdd.Enabled = false;
        _btnAdd.Click += (_, _) => Add();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(_lstDrivers, _ => LeastRows * _lstDrivers.ItemHeight + 4, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnAdd), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnCancel;
        AcceptButton = _btnAdd;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 520, height: 460);
    }

    private void Add()
    {
        if (_lstDrivers.SelectedItem is not Choice choice)
        {
            return;
        }

        Picked = choice.Transponder;
        DialogResult = DialogResult.OK;
        Close();
    }
}
