namespace HeatCaller;

/// <summary>
/// Setting up the night (12 Nov 2026, setup plan step 1, items D1, D2, D3
/// and D5 - see NightSetup.cs). This file connects the pieces that need the
/// whole app:
///
/// - the cars on tonight's heats the loop hasn't heard tonight (D3), from
///   every line pass and every race saved tonight;
/// - posting the heats (D2): the announcer tells the room, and Redo
///   Remaining Heats on Race Control asks first once they're posted;
/// - the planned breaks (D5), which Race Control's auto-start adds to the gap;
/// - the entries-close calls (D1), which the announcer makes ten minutes
///   before the close and as it passes, with the Next Steps card counting
///   down between.
/// </summary>
public partial class MainForm
{
    /// <summary>How often the entries close is looked at.</summary>
    private const int EntriesCloseTickMs = 15_000;

    private readonly System.Windows.Forms.Timer _entriesCloseTimer = new() { Interval = EntriesCloseTickMs };

    /// <summary>The entries-close calls already made, as "raceDayId|close ticks|call", so each is said once.</summary>
    private readonly HashSet<string> _entriesCloseSaid = new(StringComparer.Ordinal);

    /// <summary>The Next Steps card's minute, so the countdown is redrawn once a minute and not every tick.</summary>
    private long _entriesCloseMinuteShown = -1;

    private void WireNightSetup(RaceProgramControl raceProgram, RaceControlControl raceControl)
    {
        raceProgram.HeardTonight = HeardTonight;
        raceProgram.NightNotice += text =>
        {
            _announcer.SpeakReminder(text, showOnScreens: true, onAir: "Notice");
            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Announced: \"{text}\"");
        };
        raceProgram.HeatsPostedChanged += () =>
        {
            raceProgram.RefreshAll();
            _todayControl?.RefreshAll();
        };

        raceControl.PlannedBreaks = () => _raceEvents.Active is { IsLegacy: false } day ? day.Breaks : null;
        raceControl.ConfirmPostedChange = (owner, what) => raceProgram.ChangePostedHeatsOk(owner, what);

        // The Big Screen's "PROVISIONAL UNTIL 20:42" (setup plan step 8).
        raceControl.ProvisionalBoard = ProvisionalBoardLine;

        // Tonight's races off between a driver's races (setup plan step 8), for Auto start's rest.
        raceControl.RestGap = () => raceProgram.RestGapTonight;

        // Hand the desk to a phone (setup plan D8).
        raceControl.HandDeskRequested += HandTheDeskToAPhone;

        // A marshal who didn't turn up (setup plan D4), kept on the round for
        // the event report and the club's marshal rule (setup plan step 8 -
        // see MainForm.SetupStep8).
        raceControl.MarshalNoShow += LogMarshalNoShow;
        raceControl.MarshalNoShowTakenBack += (round, who) => TakeMarshalNoShowBack(round.Id, who, withUndo: true);

        _entriesCloseTimer.Tick += (_, _) => EntriesCloseTick();
        _entriesCloseTimer.Start();
        FormClosed += (_, _) => _entriesCloseTimer.Dispose();
    }

    /// <summary>
    /// Whether a transponder has crossed the loop tonight (since 6am, as the
    /// night is counted): heard by the decoder in this run of the app, or
    /// counting a timed lap in a race saved tonight, practice included. In
    /// Demo Mode every car counts as heard, because there is no loop to hear it.
    /// </summary>
    private bool HeardTonight(string transponder)
    {
        if (_race.IsDemoMode)
        {
            return true;
        }

        var since = TonightsSightings.NightStartUtc(DateTime.Now);

        _savedTonight ??= (DateTime.MinValue, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // The saved races are read again at most every half a minute: the card
        // asks for every car on every heat, and history can be long.
        if (DateTime.UtcNow - _savedTonight.Value.ReadUtc > TimeSpan.FromSeconds(30))
        {
            _savedTonight = (DateTime.UtcNow, TonightsSightings.InSessions(_sessionHistory.All, since));
        }

        // Any of the driver's transponders (12 Nov 2026, setup plan step 7):
        // a heat carries the driver's main one, but their Mod car may run
        // their second, and that crossing the loop is the car heard.
        var owner = _profiles.Get(_profiles.PrimaryForAlternate(transponder) ?? transponder);
        var saved = _savedTonight.Value.Heard;

        return TonightsSightings.HeardAny(
            TonightsSightings.IdsOf(transponder, owner),
            id => TonightsSightings.HeardSince(id, since) || saved.Contains(id));
    }

    /// <summary>The transponders in races saved tonight, and when that was read. See HeardTonight.</summary>
    private (DateTime ReadUtc, HashSet<string> Heard)? _savedTonight;

    /// <summary>Notes a line pass for the not-heard list. A hand-added lap proves nothing about the transponder.</summary>
    private static void NoteTonightsSighting(DirectLapEvent lap)
    {
        if (!lap.IsSynthetic)
        {
            TonightsSightings.Note(lap.TransponderId, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Every fifteen seconds: makes the entries-close calls when they're due,
    /// and redraws the Next Steps countdown once a minute while the heats
    /// are still to build.
    /// </summary>
    private void EntriesCloseTick()
    {
        LateArrivalsTick();

        try
        {
            if (_raceEvents.Active is not { IsLegacy: false, IsOpen: true } day
                || EntriesClose.AtUtc(day, DateTime.UtcNow) is not { } closeAt)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var scope = _raceEvents.ActiveRoundScope();
            var dayRounds = _rounds.All.Where(r => scope is null || scope.Contains(r.Id)).ToList();
            var racingStarted = dayRounds.Any(r => r.IsRecorded && r.Kind != RoundKind.Practice);

            var key = $"{day.Id}|{closeAt.Ticks}|";
            var call = EntriesClose.Due(closeAt, now, racingStarted,
                _entriesCloseSaid.Contains(key + EntriesClose.Call.Soon),
                _entriesCloseSaid.Contains(key + EntriesClose.Call.Closed));

            if (call != EntriesClose.Call.None)
            {
                _entriesCloseSaid.Add(key + call);

                // Saying "closed" means the ten-minute call is past too.
                if (call == EntriesClose.Call.Closed)
                {
                    _entriesCloseSaid.Add(key + EntriesClose.Call.Soon);
                }

                var line = EntriesClose.Line(call, closeAt, now);
                _announcer.SpeakReminder(line, showOnScreens: true, onAir: "Notice");
                OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Entries close: \"{line}\"");
            }

            // The heats build themselves as entries close (setup plan B2) -
            // tried once per close, and only before the racing starts.
            if (now >= closeAt && !racingStarted && _entriesCloseSaid.Add(key + "built") && _raceProgram?.BuildWhenEntriesClose() is { } built)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Entries closed - heats built: {built}");
                _todayControl?.RefreshAll();
            }

            // The card counts down in minutes; redraw it as the minute turns,
            // while the countdown is still on it.
            var minute = now.Ticks / TimeSpan.TicksPerMinute;

            if (!racingStarted && minute != _entriesCloseMinuteShown && now < closeAt + TimeSpan.FromMinutes(2))
            {
                _entriesCloseMinuteShown = minute;
                _todayControl?.RefreshAll();
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] The entries-close call could not be worked out: {ex.Message}");
        }
    }

    /// <summary>
    /// Every fifteen seconds: drivers who checked in after the heats were
    /// built go into them, when that needs no question (setup plan D6), and
    /// Next Steps is told about any it should ask about.
    /// </summary>
    private void LateArrivalsTick()
    {
        try
        {
            if (_raceProgram is null || _raceEvents.Active is not { IsLegacy: false, IsOpen: true })
            {
                return;
            }

            if (_raceProgram.AbsorbLateArrivals() is { } added)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Checked in late: {added}");
                _todayControl?.RefreshAll();
                return;
            }

            // Late arrivals to ask about: redraw the card when the count changes.
            var waiting = _raceProgram.LateArrivalsTonight().Count;

            if (waiting != _lateArrivalsShown)
            {
                _lateArrivalsShown = waiting;
                _todayControl?.RefreshAll();
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] Late check-ins could not be worked out: {ex.Message}");
        }
    }

    /// <summary>How many late arrivals the card was last redrawn for.</summary>
    private int _lateArrivalsShown;
}
