using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Setting up the night, step 4 (12 Nov 2026, setup plan B1, B2 and D6):
///  - Start tonight like last time: with no race day open, Next Steps' one
///    button starts tonight's with the last day's kind of night, classes and
///    settings (everything a new day carries forward).
///  - The heats build themselves when entries close.
///  - A driver who checks in after the heats are built goes into them by
///    Add a Late Entry's rule, which moves nobody else. Before posting this
///    happens by itself; after posting Next Steps asks first, unless the club
///    adds them without asking.
/// </summary>
public sealed partial class RaceProgramControl
{
    /// <summary>A day started this recently is tonight's, so "start tonight like last time" isn't offered after Close Day.</summary>
    internal static readonly TimeSpan TonightAlreadyStarted = TimeSpan.FromHours(12);

    /// <summary>Asks the Setup page to start tonight like the last race day - Next Steps' "Start tonight like last time".</summary>
    public event Action? StartLikeLastRequested;

    /// <summary>A class's recent best laps by transponder, for placing a late entry by pace. Null when nobody has one.</summary>
    private IReadOnlyDictionary<string, long>? RecentBestLaps(string className) =>
        HeatSeeding.BestLaps(className, _sessionHistory.All, _rounds.Get,
            DateTime.UtcNow - HeatSeeding.RecentForm, _profiles.PrimaryForAlternate) is { Count: > 0 } laps ? laps : null;

    /// <summary>
    /// Who was already on each class's roster once its heats were built, by
    /// "raceDayId|class". Only a driver who appears after that is a late
    /// arrival, so somebody the director took out of the heats on purpose
    /// isn't put straight back. Seeded when the heats are built, or, after a
    /// restart, the first time the class is looked at, when nobody counts as
    /// new.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _knownRoster = new(StringComparer.OrdinalIgnoreCase);

    private static string KnownKey(RaceEvent day, string className) => $"{day.Id}|{className}";

    /// <summary>Notes everybody on tonight's rosters as known - after a build, so only later check-ins are late.</summary>
    private void SeedKnownRosters()
    {
        if (ActiveDay is not { IsLegacy: false } day)
        {
            return;
        }

        foreach (var name in RacingClassNames())
        {
            _knownRoster[KnownKey(day, name)] = RosterFor(name).Select(p => p.Transponder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Drivers in tonight's classes who checked in after its heats were built - see LateArrivals.</summary>
    internal List<LateArrivals.Arrival> LateArrivalsTonight()
    {
        if (ActiveDay is not { IsLegacy: false, IsOpen: true } day)
        {
            return new List<LateArrivals.Arrival>();
        }

        var classes = RacingClassNames()
            .Select(name => (name, (IReadOnlyList<string>)RosterFor(name).Select(p => p.Transponder).ToList()))
            .ToList();

        var found = LateArrivals.Find(classes, DayRoundsForClass, RoundOnTrackId?.Invoke(), day.LateEntriesDeclined);

        // A class seen for the first time since the app started: everybody
        // on it now counts as known, so a restart adds nobody.
        foreach (var (name, roster) in classes)
        {
            if (!_knownRoster.ContainsKey(KnownKey(day, name)) && DayRoundsForClass(name).Any(r => r.Kind == RoundKind.Qualifier))
            {
                _knownRoster[KnownKey(day, name)] = roster.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        return found
            .Where(a => !_knownRoster.TryGetValue(KnownKey(day, a.ClassName), out var known) || !known.Contains(a.Transponder))
            .ToList();
    }

    /// <summary>A late arrival has been dealt with - added, or not tonight - so it isn't raised again.</summary>
    private void MarkKnown(LateArrivals.Arrival arrival)
    {
        if (ActiveDay is not { } day)
        {
            return;
        }

        var key = KnownKey(day, arrival.ClassName);

        if (!_knownRoster.TryGetValue(key, out var known))
        {
            known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _knownRoster[key] = known;
        }

        known.Add(arrival.Transponder);
    }

    /// <summary>
    /// Whether a late check-in should be asked about rather than added: the
    /// heats have been posted, or a race has run (so they're out either way),
    /// and the club doesn't add them without asking.
    /// </summary>
    private bool AskAboutLateArrivals(IReadOnlyList<RoundDefinition> day) =>
        !_settings.AddLateCheckInsWithoutAsking
        && (HeatPosting.Posted(ActiveDay) || day.Any(r => r.IsRecorded && r.Kind != RoundKind.Practice));

    /// <summary>
    /// Adds tonight's late check-ins to the heats, when that needs no
    /// question: before the heats are posted, or when the club adds them
    /// without asking. Called every few seconds by MainForm. Returns what
    /// was done, for the console, or null when nothing was.
    /// </summary>
    internal string? AbsorbLateArrivals()
    {
        var day = DayRounds();

        if (day.Count == 0 || AskAboutLateArrivals(day))
        {
            return null;
        }

        // Not while Move Drivers Between Heats or Edit Drivers is open (this
        // is called from a timer, which still ticks then). The window saves
        // the heats as it showed them, so a driver added underneath it was
        // wiped by its Save and, already noted as known, never offered
        // again. They are picked up on the first tick after it closes.
        if (RosterEditOpen)
        {
            return null;
        }

        var added = new List<string>();

        foreach (var arrival in LateArrivalsTonight())
        {
            var answer = AddLateEntryFor(arrival.ClassName, arrival.Transponder, "Checked in late: ");
            MarkKnown(arrival);

            if (answer.Ok)
            {
                added.Add(answer.Message);
            }
        }

        return added.Count == 0 ? null : string.Join(" ", added);
    }

    /// <summary>The card's late-arrivals step, or null when nobody is waiting to be asked about.</summary>
    private ProgramStep? LateArrivalsStep(IReadOnlyList<RoundDefinition> day)
    {
        if (!AskAboutLateArrivals(day) || LateArrivalsTonight() is not { Count: > 0 } arrivals)
        {
            return null;
        }

        var names = arrivals.Select(a => NameOf(a.Transponder)).Distinct().ToList();

        return new ProgramStep(
            ProgramStage.LateArrivals,
            LateArrivals.Headline(names, DayIsClubNight),
            $"{string.Join(", ", arrivals.Select(a => $"{NameOf(a.Transponder)} ({a.ClassName})"))}. "
            + "Each goes into the heat nearest their pace that has room, in every round still to run, and nobody else moves. "
            + "Or say not tonight.",
            ProgramCaptions.LateArrivals);
    }

    private string NameOf(string transponder) =>
        _profiles.Get(transponder) is { DriverName.Length: > 0 } profile ? profile.DriverName.Trim() : transponder;

    /// <summary>The card's button: which late arrivals to add, and where each would go.</summary>
    private void ShowLateArrivals()
    {
        var arrivals = LateArrivalsTonight();

        if (arrivals.Count == 0 || ActiveDay is not { } raceDay || _events is null)
        {
            return;
        }

        var onTrack = RoundOnTrackId?.Invoke();
        var lines = arrivals
            .Select(a =>
            {
                var places = LateEntry.Place(DayRoundsForClass(a.ClassName), a.Transponder, onTrack, RecentBestLaps(a.ClassName), HeatSize());
                return (Arrival: a, Text: $"{NameOf(a.Transponder)} ({a.ClassName}): "
                                          + (places.Count == 0 ? "no heat left to go in" : string.Join(", ", places.Select(p => p.RoundLabel))));
            })
            .ToList();

        using var form = new LateArrivalsForm(lines.Select(l => l.Text).ToList(), DayIsClubNight);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AnswerLateArrivals(lines.Select(l => l.Arrival).ToList(), form.Ticked, raceDay);
    }

    /// <summary>The late arrivals window's answer: each ticked driver added, each unticked one not asked about again tonight.</summary>
    internal void AnswerLateArrivals(IReadOnlyList<LateArrivals.Arrival> arrivals, Func<int, bool> ticked, RaceEvent raceDay)
    {
        if (_events is null)
        {
            return;
        }

        var added = new List<string>();
        var declined = new List<string>();
        var declinedArrivals = new List<LateArrivals.Arrival>();

        for (var i = 0; i < arrivals.Count; i++)
        {
            var arrival = arrivals[i];
            MarkKnown(arrival);

            if (ticked(i))
            {
                var answer = AddLateEntryFor(arrival.ClassName, arrival.Transponder, string.Empty);

                if (answer.Ok)
                {
                    added.Add(answer.Message);
                }
            }
            else
            {
                declined.Add(LateArrivals.Key(arrival.ClassName, arrival.Transponder));
                declinedArrivals.Add(arrival);
            }
        }

        if (declined.Count > 0)
        {
            raceDay.LateEntriesDeclined = raceDay.LateEntriesDeclined.Concat(declined).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _events.Upsert(raceDay);

            var dayId = raceDay.Id;
            _undo.Capture("Not tonight: late check-ins", () =>
            {
                if (_events.Get(dayId) is { } current)
                {
                    current.LateEntriesDeclined = current.LateEntriesDeclined.Except(declined, StringComparer.OrdinalIgnoreCase).ToList();
                    _events.Upsert(current);

                    // Asked about again as well: being noted as known is
                    // what keeps a driver off the card, so with only the
                    // declined list cleared the undo brought nothing back.
                    foreach (var arrival in declinedArrivals)
                    {
                        if (_knownRoster.TryGetValue(KnownKey(current, arrival.ClassName), out var known))
                        {
                            known.Remove(arrival.Transponder);
                        }
                    }
                }

                RefreshAll();
            });
        }

        _lblStatus.Text = added.Count > 0
            ? string.Join(" ", added)
            : $"Not added tonight: {Plural.Of(declined.Count, "driver")}.";
        RefreshAll();
    }

    /// <summary>
    /// "Start tonight like last time", or null: no real day to go by, the
    /// last one had no classes, or it was started tonight already (after
    /// Close Day the card shouldn't offer a second night).
    /// </summary>
    private ProgramStep? StartLikeLastStep(ProgramStep step)
    {
        // A template set for tonight comes first (setup plan step 7).
        if (StartFromTemplateRequested is not null && TonightsTemplate(DateTime.UtcNow) is { } tonight)
        {
            return step with
            {
                Stage = ProgramStage.StartFromTemplate,
                Detail = $"Tonight is \"{tonight.Name}\": {NightTemplates.Describe(tonight)}. Opens Start Tonight on it - press Start Tonight to start the night with all of it, "
                         + "or pick something else there.",
                ActionCaption = $"Start tonight's {tonight.Name}...",
                ClassName = tonight.Name,
            };
        }

        if (StartLikeLastRequested is null || LastDayToCopy(DateTime.UtcNow) is not { } last)
        {
            return null;
        }

        var kind = last.IsClubNight ? "club night" : "race day";
        var at = string.IsNullOrWhiteSpace(last.VenueName) ? string.Empty : $" at {last.VenueName.Trim()}";
        var first = last.FirstRaceLocalTime is { } time ? $", first race {AppTime.TimeOfDay(DateTime.Today.Add(time))}" : string.Empty;

        return step with
        {
            Stage = ProgramStage.StartLikeLast,
            Detail = $"Start tonight's {kind} like \"{last.DisplayName}\": {string.Join(", ", last.ClassNames)}{at}{first}, "
                     + "with its heat sizes, fees, breaks and the rest. Opens Start Tonight on it, where something else can be picked instead.",
            ActionCaption = ProgramCaptions.StartLikeLast,
        };
    }

    /// <summary>The race day "start tonight like last time" copies, or null. See StartLikeLastStep.</summary>
    internal RaceEvent? LastDayToCopy(DateTime nowUtc)
    {
        var last = _events?.Real.OrderByDescending(e => e.StartedUtc).FirstOrDefault();

        return last is { ClassNames.Count: > 0 } && nowUtc - last.StartedUtc >= TonightAlreadyStarted ? last : null;
    }

    /// <summary>
    /// Builds the heats when entries close (setup plan B2), if the card is
    /// still waiting for them to be built and the build can be done. Returns
    /// what was done, or null when nothing was.
    /// </summary>
    internal string? BuildWhenEntriesClose()
    {
        if (!_settings.BuildHeatsWhenEntriesClose || NextStep().Stage != ProgramStage.BuildQualifying)
        {
            return null;
        }

        if (!PlanQualifyingBuild().Ok)
        {
            return null;
        }

        var answer = BuildQualifying("Entries closed: ");
        return answer.Ok ? answer.Message : null;
    }
}

/// <summary>The late arrivals window: each driver and where they'd go, ticked to add. An unticked one is "not tonight".</summary>
internal sealed class LateArrivalsForm : Form
{
    private readonly List<CheckBox> _ticks = new();

    public bool Ticked(int index) => index >= 0 && index < _ticks.Count && _ticks[index].Checked;

    public LateArrivalsForm(IReadOnlyList<string> lines, bool clubNight)
    {
        Text = "Checked In Late";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = $"These drivers checked in after tonight's {(clubNight ? "races" : "heats")} were posted. A ticked driver goes into the heat "
                   + "nearest their pace that has room, in every round still to run, and nobody else moves. "
                   + "An unticked one isn't added tonight, and Next Steps stops asking. Undo takes either back.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);

        foreach (var line in lines)
        {
            var tick = new ThemedCheckBox { Text = line, AutoSize = true, Checked = true, MaximumSize = new Size(520, 0) };
            _ticks.Add(tick);
            page.Add(tick, stretch: false, gapBelow: 0);
        }

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnOk = new Button { Text = "Add the Ticked", DialogResult = DialogResult.OK, AutoSize = true };

        page.Add(new Panel { Height = UiLayout.RowGap }, stretch: false, gapBelow: 0);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 580, height: 260 + 30 * lines.Count);
    }
}

/// <summary>Templates on dates (12 Nov 2026, setup plan step 7).</summary>
public sealed partial class RaceProgramControl
{
    /// <summary>Asks the Setup page to start tonight from the template of this name - Next Steps' "Start tonight's ...".</summary>
    public event Action<string>? StartFromTemplateRequested;

    /// <summary>
    /// The template set for tonight, or null: none is, or a race day was
    /// started in the last 12 hours (tonight's has begun, or been and gone).
    /// </summary>
    internal NightTemplate? TonightsTemplate(DateTime nowUtc)
    {
        var last = _events?.Real.OrderByDescending(e => e.StartedUtc).FirstOrDefault();

        if (last is not null && nowUtc - last.StartedUtc < TonightAlreadyStarted)
        {
            return null;
        }

        return NightTemplates.ForDay(_settings.NightTemplates, nowUtc.ToLocalTime().Date);
    }
}
