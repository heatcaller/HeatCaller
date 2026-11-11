namespace HeatCaller;

/// <summary>
/// Race Control's side of decoder-down mode (11 Nov 2026, race-night build
/// plan 6.1) - the rules are HandScoring's.
///
/// WHY HERE. A hand lap is the drawer's +1 Lap, and +1 Lap lives here: the
/// same EditStanding wrapper, so the app's Undo takes back one tap; the same
/// board redraw; the same console line. The desk's full-screen grid
/// (HandScoringForm) and the phone's (race.handLap in RaceDayOpsRace) both
/// call <see cref="HandLap"/>, so a tap from either is the same lap.
///
/// The mode belongs to ONE race - the generation it was switched on in - so
/// the next race starts with the decoder trusted and the announcer talking,
/// without anyone having to remember to switch it off.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The race hand scoring was switched on in, or null. See HandScoringOn.</summary>
    private int? _handScoringGeneration;

    /// <summary>The desk's full-screen grid, while it is open.</summary>
    private HandScoringForm? _handScoringForm;

    /// <summary>When each car's last +1 was counted (Environment.TickCount64), for HandScoring.IsDoubleTap.</summary>
    private readonly Dictionary<string, long> _lastHandLapMs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Score by Hand on the Next Up bar - shown only while it is on offer, or on.</summary>
    private readonly Button _btnHandScore = new();

    /// <summary>
    /// TimingWatch's problem KEY now, or null - which of its problems the
    /// TIMING line is, so the offer is made for the right ones. Set by the
    /// host next to <see cref="TimingProblem"/>.
    /// </summary>
    public Func<string?>? TimingProblemKey { get; set; }

    /// <summary>
    /// True while the race on track is being scored by hand: switched on
    /// for this race, and the race still running. Any thread (the race is
    /// read through RaceTiming).
    /// </summary>
    public bool HandScoringOn =>
        _handScoringGeneration is { } generation
        && _race.State == RaceState.Racing
        && _race.IsSameRace(generation);

    /// <summary>Puts Score by Hand on the Next Up bar, hidden until it is wanted. Called once from the layout.</summary>
    private void BuildHandScoreButton()
    {
        _btnHandScore.Text = HandScoring.OfferCaption;
        _btnHandScore.Click += (_, _) => OpenHandScoring();
        _nextUpBar.AddButton(_btnHandScore, HandScoring.OfferCaption);
        _nextUpBar.ShowButton(_btnHandScore, false);
    }

    /// <summary>Shows Score by Hand while the decoder is down mid-race, or while the mode is on (to get the grid back). Called with the Next Up bar.</summary>
    private void UpdateHandScoreOffer()
    {
        var on = HandScoringOn;
        var offer = HandScoring.ShouldOffer(TimingProblemKey?.Invoke(), _race.State, _race.IsPracticeMode, on);

        _nextUpBar.ShowButton(_btnHandScore, on || offer);
    }

    /// <summary>
    /// Switches hand scoring on for the race running now, if it isn't, and
    /// opens the full-screen grid on the desk.
    /// </summary>
    public void OpenHandScoring()
    {
        if (StartHandScoring("the desk") is { } refusal)
        {
            AppDialog.Show(this, refusal, "Can't score by hand now", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_handScoringForm is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        var form = new HandScoringForm(
            cars: HandScoringCars,
            lap: (transponder, delta) => HandLap(transponder, delta, "the desk"),
            isOn: () => HandScoringOn,
            stop: StopHandScoring,
            addable: HandScoringAddable,
            add: transponder => HandAddCar(transponder, "the desk"));

        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_handScoringForm, form))
            {
                _handScoringForm = null;
            }
        };

        _handScoringForm = form;
        form.Show(FindForm());
    }

    /// <summary>
    /// Switches hand scoring on for the race running now. Null when it is on
    /// (already, or now), else why not. Said once in the console and, so the
    /// drivers know why the lap calls have stopped, once to the room.
    /// </summary>
    public string? StartHandScoring(string from)
    {
        if (HandScoring.StartRefusal(_race.State, _race.IsPracticeMode) is { } refusal)
        {
            return refusal;
        }

        if (HandScoringOn)
        {
            return null;
        }

        _handScoringGeneration = _race.SessionGeneration;
        _lastHandLapMs.Clear();

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Scoring by hand from {from}: every tap is a lap, counted by hand (\"12*\" in the result). The announcer won't call laps from the loop until this race is over.");

        try
        {
            _announcer.SpeakReminder(HandScoring.SpokenWhenOn);
        }
        catch
        {
            // Saying so is a courtesy - never worth the mode over.
        }

        UpdateHandScoreOffer();
        return null;
    }

    /// <summary>Switches hand scoring off: the loop is trusted again and the announcer calls its laps. Laps already counted by hand stay.</summary>
    public void StopHandScoring()
    {
        if (_handScoringGeneration is null)
        {
            return;
        }

        var wasOn = HandScoringOn;
        _handScoringGeneration = null;

        if (wasOn)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Hand scoring off - laps are counted from the loop again. Laps already counted by hand stay.");
        }

        UpdateHandScoreOffer();
    }

    /// <summary>The cars on the hand-scoring grid, in an order that never moves - see HandScoring.CarsFor.</summary>
    public IReadOnlyList<HandScoring.Car> HandScoringCars() =>
        HandScoring.CarsFor(
            _race.GetOrderedStandings(),
            transponder => _profiles.Get(transponder)?.DriverName,
            transponder => GhostService.IsGhost(transponder));

    /// <summary>
    /// The cars that can be put on the grid (11 Nov 2026): this round's
    /// roster first, then every known transponder - see HandScoring.AddableCars.
    /// </summary>
    public IReadOnlyList<HandScoring.Addable> HandScoringAddable() =>
        HandScoring.AddableCars(
            _race.Grid,
            _profiles.All.Select(p => p.Transponder),
            _race.GetOrderedStandings().Select(s => s.TransponderId),
            transponder => _profiles.Get(transponder)?.DriverName,
            transponder => _profiles.PrimaryForAlternate(transponder),
            transponder => GhostService.IsGhost(transponder));

    /// <summary>
    /// Puts a car the decoder never saw on the board (11 Nov 2026), so its
    /// laps can be counted by hand - RaceControlSession.AddStandingByHand.
    /// Hand scoring is switched on for the race if it isn't. Null when it
    /// is on the board now, else why not. Undo takes it off again while it
    /// still has no laps.
    /// </summary>
    public string? HandAddCar(string transponder, string from)
    {
        var id = (_profiles.PrimaryForAlternate(transponder ?? string.Empty) ?? transponder ?? string.Empty).Trim();
        var name = DriverNameFor(id);
        var onBoard = _race.GetOrderedStandings().Any(s =>
            string.Equals(s.TransponderId, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_profiles.PrimaryForAlternate(s.TransponderId), id, StringComparison.OrdinalIgnoreCase));

        if (id.Length == 0)
        {
            return "Pick a car to add.";
        }

        if (HandScoring.AddRefusal(_race.State, _race.IsPracticeMode, _race.IsFinishing, onBoard, GhostService.IsGhost(id), name) is { } refusal)
        {
            return refusal;
        }

        if (StartHandScoring(from) is { } notOn)
        {
            return notOn;
        }

        if (!_race.AddStandingByHand(id))
        {
            return $"{name} couldn't be put on the board - the race didn't take it.";
        }

        var generation = _race.SessionGeneration;

        _undo.Capture($"Put {name} on the hand-scoring grid", () =>
        {
            if (!_race.IsSameRace(generation) || !_race.RemoveHandAddedStanding(id))
            {
                throw new InvalidOperationException(
                    $"{name} has laps counted now, or that race is over - take their laps off with -1 first. Nothing was changed.");
            }

            RenderLeaderboard();
        });

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {name} put on the board by hand from {from} - the decoder never saw them. Their laps are counted by hand.");

        RenderLeaderboard();
        return null;
    }

    /// <summary>
    /// One tap: a lap added (+1) or taken back (-1) for one car, through the
    /// drawer's own path so Undo takes back just this. Null when it was
    /// counted, else why not. <paramref name="from"/> is only for the
    /// console - "the desk", "a phone".
    /// </summary>
    public string? HandLap(string transponder, int delta, string from)
    {
        var standing = _race.GetOrderedStandings()
            .FirstOrDefault(s => string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        var name = DriverNameFor(transponder);

        if (HandScoring.LapRefusal(_race.State, _race.IsPracticeMode, standing is not null, name) is { } refusal)
        {
            return refusal;
        }

        if (delta is not (1 or -1))
        {
            return "A tap is one lap, on or off.";
        }

        var id = standing!.TransponderId;
        var nowMs = Environment.TickCount64;

        if (delta > 0)
        {
            if (HandScoring.IsDoubleTap(_lastHandLapMs.TryGetValue(id, out var last) ? last : null, nowMs))
            {
                return $"{name}'s lap is already counted - that was a second tap within a second.";
            }

            _lastHandLapMs[id] = nowMs;
        }
        else
        {
            // Taking one back clears the guard, so the real next lap counts.
            _lastHandLapMs.Remove(id);
        }

        var edit = _race.EditStanding(id, () => _race.AdjustLaps(id, delta));

        if (edit is null)
        {
            return delta > 0
                ? $"{name}'s lap wasn't counted - the race didn't take it."
                : $"{name} has no lap to take off.";
        }

        CaptureStandingEditUndo(delta > 0 ? $"Hand lap for {name}" : $"Take a hand lap off {name}", edit);

        if (!string.Equals(from, "the desk", StringComparison.Ordinal))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Hand lap {(delta > 0 ? "+1" : "-1")} for {name} from {from}.");
        }

        RenderLeaderboard();
        return null;
    }
}
