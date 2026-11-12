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
    /// <param name="bestLaps">
    /// Recent best laps in the class by transponder (HeatSeeding.BestLaps), or
    /// null (12 Nov 2026, setup plan D6). With the late driver's own, the heat
    /// whose drivers' typical lap is nearest theirs is picked, so a fast late
    /// driver doesn't land in the slow heat of a round seeded by pace.
    /// Without, the smallest heat, as before.
    /// </param>
    /// <param name="heatSize">
    /// The night's drivers per heat, or 0 for no limit: heats already that big
    /// are passed over while one with room is left, so a late entry doesn't
    /// make a heat bigger than the track runs.
    /// </param>
    public static List<RoundDefinition> Place(
        IReadOnlyList<RoundDefinition> classDayRounds,
        string transponder,
        string? onTrackRoundId,
        IReadOnlyDictionary<string, long>? bestLaps = null,
        int heatSize = 0)
    {
        var qualifiers = classDayRounds.Where(r => r is not null && r.Kind == RoundKind.Qualifier).ToList();
        var places = new List<RoundDefinition>();

        long? mine = bestLaps is not null && bestLaps.TryGetValue(transponder, out var lap) && lap > 0 ? lap : null;

        foreach (var round in qualifiers.GroupBy(r => (object?)NightPlan.RoundNumber(r.RoundLabel) ?? r.Id))
        {
            if (round.Any(r => r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            var open = round
                .Where(r => !r.IsRecorded && !string.Equals(r.Id, onTrackRoundId, StringComparison.Ordinal))
                .ToList();

            if (open.Count == 0)
            {
                continue;
            }

            // Room first: a heat already at the night's size is the last resort.
            var withRoom = heatSize > 0 ? open.Where(r => r.AssignedTransponders.Count < heatSize).ToList() : open;
            var choices = withRoom.Count > 0 ? withRoom : open;

            var pick = mine is { } own && choices.Any(c => TypicalLap(c, bestLaps!) is not null)
                ? choices
                    .Select((r, i) => (r, i, typical: TypicalLap(r, bestLaps!)))
                    .OrderBy(x => x.typical is { } t ? Math.Abs(t - own) : long.MaxValue)
                    .ThenBy(x => x.r.AssignedTransponders.Count)
                    .ThenBy(x => x.i)
                    .First().r
                : choices.OrderBy(r => r.AssignedTransponders.Count).First();

            places.Add(pick);
        }

        return places;
    }

    /// <summary>The median recent best lap of a heat's drivers, or null when none of them has one.</summary>
    internal static long? TypicalLap(RoundDefinition heat, IReadOnlyDictionary<string, long> bestLaps)
    {
        var laps = heat.AssignedTransponders
            .Select(t => bestLaps.TryGetValue(t, out var l) ? l : 0)
            .Where(l => l > 0)
            .OrderBy(l => l)
            .ToList();

        return laps.Count == 0 ? null : laps[laps.Count / 2];
    }
}

/// <summary>
/// Pick one driver from a list - "Who's the late entry?", "Who covers this
/// post?". The words are the caller's.
/// </summary>
internal sealed class PickDriverForm : Form
{
    private const int LeastRows = 8;

    private readonly ListBox _lstDrivers = new();
    private readonly Button _btnAdd = new();

    /// <summary>The driver picked, by transponder. Set on OK.</summary>
    public string? Picked { get; private set; }

    private sealed record Choice(string Transponder, string Label)
    {
        public override string ToString() => Label;
    }

    public PickDriverForm(string title, string prompt, string buttonText, IEnumerable<(string Transponder, string Name)> drivers)
    {
        Text = title;
        _btnAdd.Text = buttonText;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblWhat = new Label
        {
            Text = prompt,
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
