using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// THE RUN SHEET (17 Nov 2026, the Race Day redesign - mockup A+B in
/// plans/race-day-mockups.html, plan in plans/race-day-redesign-plan.md).
///
/// The program grid, drawn as the night's timetable: each race at the clock
/// time it should go, done races folded away, a "now" line, the out-by line,
/// the mains still to be built and the end of racing - and the next race
/// opened up in place as Next Steps' card (TodayControl hands it over: see
/// HostNowCard).
///
/// WHY. The old Next Steps page put the next race in a card at the top, the
/// running order in a grid at the bottom, and when the night would finish in
/// a sentence in between. A race director between races wants all three in
/// one line of sight: what time it is, what goes next, and what comes after
/// it. The grid is still the grid - the same rows, the right-click menu, drag
/// to reorder, the class filter - so nothing a club already does here moves.
///
/// Rows that aren't rounds (the fold, the card's row when the next race isn't
/// on show, a dropped round's preview, the mains to come, the end of racing)
/// carry a <see cref="SheetRow"/> as their Tag rather than a round id, so
/// every "row.Tag is string id" check already in this control passes them by.
/// </summary>
public sealed partial class RaceProgramControl
{
    /// <summary>What a row of the sheet that isn't a round is.</summary>
    private enum SheetRow
    {
        /// <summary>"▸ 3 earlier races recorded" - a click shows or hides them.</summary>
        Fold,

        /// <summary>The card's row when the next race isn't on the sheet: nothing to run yet, the filter on another class, or all done.</summary>
        Now,

        /// <summary>A round a ticked fix would drop, shown as one row with its time crossed out.</summary>
        Cut,

        /// <summary>The finals still to be built from qualifying.</summary>
        Mains,

        /// <summary>When racing should be done.</summary>
        End,
    }

    /// <summary>How one row is drawn. Kept on the row's dot cell (its Tag), made fresh on every RefreshGrid.</summary>
    private sealed class RowLook
    {
        public SheetRow? Kind { get; init; }

        public bool Recorded { get; init; }

        /// <summary>The next race: the card sits over this row.</summary>
        public bool HostsCard { get; init; }

        /// <summary>When it should start, for the out-by line. Null for a race already run.</summary>
        public DateTime? StartUtc { get; init; }

        /// <summary>Its start is after the out-by time: the time is drawn in red.</summary>
        public bool Late { get; set; }

        /// <summary>The crossed-out old time on a dropped round's row.</summary>
        public string? Struck { get; init; }

        public RunSheet.Status Status { get; init; }

        /// <summary>The "NOW 4:58" line along the top of this row.</summary>
        public bool NowLine { get; set; }

        /// <summary>The dashed out-by line along the top of this row.</summary>
        public bool OutLine { get; set; }

        /// <summary>For the fold row: what it says.</summary>
        public string? FoldWords { get; init; }

        /// <summary>The class's colour, for the race name.</summary>
        public Color? ClassColour { get; init; }
    }

    // --- The card Next Steps hands over ---

    /// <summary>Next Steps' card for the next race, sat over its row. Null until TodayControl hands it over.</summary>
    private Control? _nowCard;

    /// <summary>Whether the earlier recorded races are shown rather than folded into one row.</summary>
    private bool _showAllDone;

    /// <summary>The qualifying round a ticked fix would drop, shown on the sheet before it is applied - see PreviewFixes.</summary>
    private int? _previewDropRound;

    /// <summary>What the other ticked fixes would take off the end of the night.</summary>
    private TimeSpan _previewOther;

    /// <summary>Set while RefreshGrid is rebuilding the rows, so the selection it moves is not acted on.</summary>
    private bool _fillingSheet;

    /// <summary>The bold face the pills are drawn in, made for the small face it was made from.</summary>
    private (Font From, Font Bold)? _pillFont;

    /// <summary>The out-by time tonight, as a moment, from the last plan - for the line and the red times.</summary>
    private DateTime? _outByUtc;

    /// <summary>
    /// Raised when the sheet has been rebuilt - the next race may have
    /// changed, so Next Steps refreshes its card.
    /// </summary>
    public event Action? SheetRebuilt;

    /// <summary>The time and dot columns across: the card draws its time and dot in the same place, so they line up with the rows'.</summary>
    internal int SheetGutter =>
        (_grid.Columns["est"] is { Visible: true } est ? est.Width : 0)
        + (_grid.Columns["due"] is { Visible: true } due ? due.Width : 0)
        + (_grid.Columns["dot"]?.Width ?? 0);

    /// <summary>The time columns' width and the dot column's, for the card to line its time and dot up with the rows'.</summary>
    internal (int Time, int Dot) SheetGutterParts() =>
        ((_grid.Columns["est"] is { Visible: true } est ? est.Width : 0) + (_grid.Columns["due"] is { Visible: true } due ? due.Width : 0),
         _grid.Columns["dot"]?.Width ?? 0);

    /// <summary>Redraws the sheet without rebuilding it - the "now" line's clock, once a minute.</summary>
    internal void RepaintSheet() => _grid.Invalidate();

    /// <summary>The height of the band a "now" or out-by line takes along the top of a row.</summary>
    private static int BandHeight => AppTheme.SmallFont.Height + 8;

    /// <summary>Room round the card inside its row.</summary>
    private static int CardInset => UiLayout.AtTextSize(4);

    /// <summary>
    /// Puts Next Steps' card on the sheet, over the next race's row. Called
    /// once; the card stays a child of the grid and is moved with it.
    /// </summary>
    internal void HostNowCard(Control card)
    {
        _nowCard = card;
        card.Visible = false;
        _grid.Controls.Add(card);

        // After, not during: a row can't be resized while the grid is still
        // resizing its filling columns, which is what a resize or a column
        // change is in the middle of.
        _grid.Scroll += (_, _) => PostPlaceNowCard();
        _grid.SizeChanged += (_, _) => PostPlaceNowCard();
        _grid.ColumnWidthChanged += (_, _) => PostPlaceNowCard();
        _grid.RowHeightChanged += (_, _) => PostPlaceNowCard();

        // A right-click on the card is a right-click on its round.
        card.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && NowRow() is { } row)
            {
                SelectOnly(row);
                _grid.ContextMenuStrip?.Show(card, e.Location);
            }
        };

        RefreshGrid();
    }

    /// <summary>The card's height changed - its words wrapped, or a line came or went. Its row follows.</summary>
    internal void NowCardResized() => PostPlaceNowCard();

    /// <summary>Set while a PlaceNowCard is waiting to run, so a burst of resizes places the card once.</summary>
    private bool _placePosted;

    /// <summary>Places the card once whatever is laying the grid out has finished - see PlaceNowCard.</summary>
    private void PostPlaceNowCard()
    {
        if (_placePosted || IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            PlaceNowCard();
            return;
        }

        _placePosted = true;

        BeginInvoke(new Action(() =>
        {
            _placePosted = false;
            PlaceNowCard();
        }));
    }

    /// <summary>
    /// How much of the grid's width can be seen: up to the edge of the panel
    /// it is in, and up to the themed scrollbar parked over its right-hand
    /// side (ThinScrollBar), whichever comes first.
    /// </summary>
    private int SeenWidth()
    {
        var seen = _grid.ClientSize.Width;

        if (!_grid.IsHandleCreated)
        {
            return seen;
        }

        var left = _grid.PointToScreen(Point.Empty).X;
        var top = _grid.PointToScreen(Point.Empty).Y;

        for (Control? host = _grid.Parent; host is not null && host != this; host = host.Parent)
        {
            seen = Math.Min(seen, host.PointToScreen(new Point(host.ClientSize.Width, 0)).X - left);

            foreach (Control part in host.Controls)
            {
                if (part is ThinScrollBar { Visible: true } bar && bar.IsHandleCreated)
                {
                    var at = bar.RectangleToScreen(bar.ClientRectangle);

                    if (at.Bottom > top && at.Top < top + _grid.Height && at.Left > left)
                    {
                        seen = Math.Min(seen, at.Left - left);
                    }
                }
            }
        }

        return Math.Max(0, seen);
    }

    /// <summary>The row the card sits over, or null when there is none.</summary>
    private DataGridViewRow? NowRow()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Visible && LookOf(row) is { HostsCard: true })
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Sizes the card's row to the card, and the card to its row. Run after
    /// every rebuild, scroll and resize.
    /// </summary>
    private void PlaceNowCard()
    {
        if (_nowCard is null || _grid.IsDisposed)
        {
            return;
        }

        if (NowRow() is not { } row)
        {
            _nowCard.Visible = false;
            return;
        }

        var look = LookOf(row)!;
        var band = (look.NowLine ? BandHeight : 0) + (look.OutLine ? BandHeight : 0);

        // As wide as the row is SEEN: the columns' own width, but never past
        // the panel the grid is in - the grid runs on under the edge of it,
        // where its own scrollbar is kept out of sight for the themed one
        // (ThinScrollBar), and a card that wide lost its right-hand edge.
        var rowWidth = _grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
        var width = Math.Max(120, Math.Min(rowWidth, SeenWidth()) - 2 * CardInset);
        var wanted = band + _nowCard.GetPreferredSize(new Size(width, 0)).Height + 2 * CardInset;

        if (row.Height != wanted)
        {
            try
            {
                // MinimumHeight first: a row can't be set shorter than it.
                row.MinimumHeight = Math.Min(wanted, 2);
                row.Height = wanted;
            }
            catch (InvalidOperationException)
            {
                // The grid is in the middle of sizing its columns after all:
                // once it's done.
                PostPlaceNowCard();
                return;
            }
        }

        var rect = _grid.GetRowDisplayRectangle(row.Index, false);

        if (rect.Height <= 0)
        {
            _nowCard.Visible = false;
            return;
        }

        var bounds = new Rectangle(rect.X + CardInset, rect.Y + band + CardInset, width, rect.Height - band - 2 * CardInset);

        if (_nowCard.Bounds != bounds)
        {
            _nowCard.Bounds = bounds;
        }

        _nowCard.Visible = true;
        _nowCard.BringToFront();
    }

    // ------------------------------------------------------------------
    //  Building the rows
    // ------------------------------------------------------------------

    /// <summary>Whether drivers are being checked in tonight, so "4 of 5 here" means something.</summary>
    private bool CountingHere() =>
        ActiveDay?.CheckedInDriversOnly == true
        || _profiles.AllRaceable.Any(p => _checkIns.IsCheckedIn(p.Transponder));

    /// <summary>How many of a round's drivers are checked in.</summary>
    private int HereIn(RoundDefinition round) =>
        round.AssignedTransponders.Count(t => GhostService.IsGhost(t) || _checkIns.IsCheckedIn(t));

    /// <summary>The rounds a ticked "drop a round" would take off the night, not counting one already started.</summary>
    private List<RoundDefinition> RoundsDropped(IReadOnlyList<RoundDefinition> day, int? number)
    {
        if (number is not { } n)
        {
            return new List<RoundDefinition>();
        }

        var onTrack = RoundOnTrackId?.Invoke();

        return day
            .Where(r => !r.IsRecorded && r.Kind == RoundKind.Qualifier
                        && NightPlan.RoundNumber(r.RoundLabel) == n
                        && !string.Equals(r.Id, onTrack, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// The night planned with the ticked fixes on it, for the sheet and the
    /// strip: the dropped round taken out (so every later time moves up), and
    /// what the other fixes save taken off the finish. Null with nothing left
    /// to plan. The plan as it stands, and when racing would end with the
    /// fixes, both - Next Steps shows the one against the other.
    /// </summary>
    internal (NightPlan.Projection? Plan, DateTime? PreviewFinishUtc) SheetPlan()
    {
        var day = DayRounds();
        var plan = Plan(day);

        if (plan is null || (_previewDropRound is null && _previewOther <= TimeSpan.Zero))
        {
            return (plan, null);
        }

        var dropped = RoundsDropped(day, _previewDropRound).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var previewed = dropped.Count == 0 ? plan : Plan(day.Where(r => !dropped.Contains(r.Id)).ToList()) ?? plan;

        return (plan, previewed.FinishUtc - _previewOther);
    }

    /// <summary>
    /// Shows the ticked fixes on the sheet without making them (the "Get back
    /// on time" card's boxes): the dropped round crossed out, every later time
    /// moved up, and the end of racing earlier.
    /// </summary>
    internal void PreviewFixes(IReadOnlyList<RunSheet.Choice> ticked)
    {
        var drop = ticked.FirstOrDefault(c => c.Kind == NightPlan.FixKind.DropRound)?.Round;
        var other = RunSheet.Saving(ticked.Where(c => c.Kind != NightPlan.FixKind.DropRound));

        if (drop == _previewDropRound && other == _previewOther)
        {
            return;
        }

        _previewDropRound = drop;
        _previewOther = other;
        RefreshGrid();
    }

    /// <summary>
    /// Fills the sheet - see the summary at the top of this file. Called by
    /// RefreshGrid with the rounds it would show.
    /// </summary>
    private void FillSheet(List<RoundDefinition> day, List<RoundDefinition> shown, string filter, IReadOnlyDictionary<string, string> due)
    {
        var next = day.FirstOrDefault(r => !r.IsRecorded);
        var countHere = CountingHere();
        var realPlan = Plan(day);

        var droppedRounds = RoundsDropped(day, _previewDropRound);
        var dropped = droppedRounds.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var plan = dropped.Count == 0 ? realPlan : Plan(day.Where(r => !dropped.Contains(r.Id)).ToList()) ?? realPlan;

        _outByUtc = plan?.OutByUtc;

        var recorded = shown.Where(r => r.IsRecorded).ToList();
        var toRun = shown.Where(r => !r.IsRecorded).ToList();

        // --- The races already run, the earlier ones folded away ---
        //
        // The last one stays out, so the eye has the race just done next to
        // the one about to go. Folded, the next race is always the third row
        // or nearer, so the card - and its Start button - stays put through
        // the night instead of walking down the page.
        var folding = recorded.Count > 1;

        if (folding)
        {
            var earlier = recorded.Take(recorded.Count - 1).ToList();
            var times = earlier.Select(StartedLocal).OfType<DateTime>().ToList();
            var span = times.Count == 0
                ? string.Empty
                : times.Count == 1 || times.Min() == times.Max()
                    ? RunSheet.ShortClock(AppTime.TimeOfDay(times.Min()))
                    : $"{RunSheet.ShortClock(AppTime.TimeOfDay(times.Min()))}–{RunSheet.ShortClock(AppTime.TimeOfDay(times.Max()))}";

            AddSheetRow(SheetRow.Fold, span, string.Empty, string.Empty, string.Empty, default, new RowLook
            {
                Kind = SheetRow.Fold,
                FoldWords = _showAllDone
                    ? "▾ Hide the earlier races"
                    : $"▸ {Plural.Of(earlier.Count, "earlier race")} recorded",
            });
        }

        for (var i = 0; i < recorded.Count; i++)
        {
            var row = AddRoundRow(recorded[i], due, null, countHere, isNext: false);
            row.Visible = !folding || _showAllDone || i == recorded.Count - 1;
        }

        // --- The next race, with Next Steps' card over it ---
        //
        // When it isn't on show (the filter is on another class) or there is
        // none (nothing built yet, or everything run), the card still goes
        // here, on a row of its own, between what has run and what is to come.
        //
        // Nor when a ticked fix's preview drops it (bug hunt 22): its row is
        // then hidden under the "cut" line, and with no other row hosting
        // the card, ticking "Drop Q3" while Q3 was next took Start, Call and
        // Skip off the screen until it was unticked. It is only a preview,
        // so the card still offers the race that is next.
        var nextShown = next is not null
                        && toRun.Any(r => string.Equals(r.Id, next.Id, StringComparison.Ordinal))
                        && !dropped.Contains(next.Id);

        if (!nextShown)
        {
            AddSheetRow(SheetRow.Now, string.Empty, string.Empty, string.Empty, string.Empty, default, new RowLook
            {
                Kind = SheetRow.Now,
                HostsCard = true,
                NowLine = true,
            });
        }

        var cutAdded = false;

        foreach (var round in toRun)
        {
            // A round a ticked fix would drop: one row for all its heats,
            // where the first of them was, with its time crossed out.
            if (dropped.Contains(round.Id))
            {
                var row = AddRoundRow(round, due, plan, countHere, isNext: false);
                row.Visible = false;

                if (!cutAdded)
                {
                    cutAdded = true;
                    var heats = droppedRounds.Where(r => filter.Length == 0 || MixedHeats.RunsClass(r, filter)).ToList();
                    var name = NightPlan.RoundNumber(round.RoundLabel) is { } n ? (DayIsClubNight ? $"Race {n}" : $"Q{n}") : round.RoundLabel;
                    var saves = TimeSpan.FromTicks(heats.Sum(h => LengthOf(h).Ticks + (realPlan?.Gap ?? NightPlan.DefaultGap).Ticks));

                    AddSheetRow(SheetRow.Cut, "cut",
                        $"{name} · {(filter.Length == 0 ? "all classes" : filter)} ({Plural.Of(heats.Count, "race")})",
                        "preview: drop",
                        "−" + RunSheet.LengthWords(saves),
                        new RunSheet.Status(RunSheet.Pill.Dropped, "Dropped"),
                        new RowLook
                        {
                            Kind = SheetRow.Cut,
                            Struck = realPlan?.ShownStartOf(round.Id) is { } was ? RunSheet.ShortClock(AppTime.TimeOfDay(was.ToLocalTime())) : null,
                            Status = new RunSheet.Status(RunSheet.Pill.Dropped, "Dropped"),
                        });
                }

                continue;
            }

            AddRoundRow(round, due, plan, countHere, isNext: next is not null && string.Equals(round.Id, next.Id, StringComparison.Ordinal));
        }

        // --- The mains still to be built ---
        if (plan is not null)
        {
            var mains = plan.Slots
                .Where(s => s.Round is null && s.Kind == RoundKind.Main && (filter.Length == 0 || string.Equals(s.ClassName, filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (mains.Count > 0)
            {
                var classes = mains.Select(s => s.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var name = classes.Count == 1 ? $"{classes[0]} · Mains" : $"Mains · {classes.Count} classes";
                var start = mains.Min(s => s.StartUtc);

                AddSheetRow(SheetRow.Mains, RunSheet.ShortClock(AppTime.TimeOfDay(start.ToLocalTime())), name, "built from qualifying",
                    RunSheet.LengthWords(mains.Max(s => s.Length)),
                    new RunSheet.Status(RunSheet.Pill.NotBuilt, "Not built"),
                    new RowLook
                    {
                        Kind = SheetRow.Mains,
                        StartUtc = start,
                        Status = new RunSheet.Status(RunSheet.Pill.NotBuilt, "Not built"),
                    });
            }

            // --- When racing should be done ---
            var finish = plan.FinishUtc - _previewOther;
            TimeSpan? over = plan.OutByUtc is { } outBy && finish > outBy ? finish - outBy : null;
            var end = RunSheet.EndOf(over, plan.OutByUtc is not null);

            AddSheetRow(SheetRow.End, RunSheet.ShortClock(AppTime.TimeOfDay(finish.ToLocalTime())), "Racing done", string.Empty, string.Empty, end, new RowLook
            {
                Kind = SheetRow.End,
                StartUtc = finish,
                Status = end,
            });
        }

        MarkLines();
    }

    /// <summary>
    /// Where the "now" line and the out-by line go: now along the top of the
    /// card's row, the out-by line along the top of the first row after it
    /// that should start past the out-by time - or on the card's own row, if
    /// even the next race is late. And every time past the out-by in red.
    /// </summary>
    private void MarkLines()
    {
        var afterCard = false;
        var outPlaced = false;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (!row.Visible || LookOf(row) is not { } look)
            {
                continue;
            }

            if (look.HostsCard)
            {
                look.NowLine = true;
                afterCard = true;
            }

            var late = _outByUtc is { } outBy && look.StartUtc is { } start && start > outBy && !look.Recorded;
            look.Late = late && look.Kind != SheetRow.End;

            if (late && afterCard && !outPlaced && look.Kind != SheetRow.Cut)
            {
                look.OutLine = true;
                outPlaced = true;
            }

            var height = _grid.RowTemplate.Height + (look.OutLine ? BandHeight : 0) + (look.NowLine && !look.HostsCard ? BandHeight : 0);

            if (!look.HostsCard && row.Height != height)
            {
                row.Height = height;
            }
        }
    }

    /// <summary>When a raced round started, from its saved race, in local time; null when it can't be found.</summary>
    private DateTime? StartedLocal(RoundDefinition round) =>
        round.LinkedSessionId is { Length: > 0 } id && _sessionHistory.Get(id) is { } session && session.StartedAtUtc > DateTime.MinValue
            ? session.StartedAtUtc.ToLocalTime()
            : null;

    /// <summary>How long a round takes on track, as the plan counts it.</summary>
    private TimeSpan LengthOf(RoundDefinition round) =>
        NightPlan.LengthOf(round, _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase)), RaceFormat.FromSettings(_settings));

    /// <summary>Adds a round's row, as the sheet draws it.</summary>
    private DataGridViewRow AddRoundRow(RoundDefinition round, IReadOnlyDictionary<string, string> due, NightPlan.Projection? plan, bool countHere, bool isNext)
    {
        var drivers = round.AssignedTransponders.Count;
        var here = HereIn(round);
        var status = RunSheet.StatusOf(round.IsRecorded, drivers, here, countHere);
        var start = plan?.ShownStartOf(round.Id);
        var time = round.IsRecorded
            ? StartedLocal(round) is { } began ? RunSheet.ShortClock(AppTime.TimeOfDay(began)) : string.Empty
            : start is { } at ? RunSheet.ShortClock(AppTime.TimeOfDay(at.ToLocalTime())) : string.Empty;

        var index = _grid.Rows.Add(
            _grid.Rows.Count + 1,
            due.TryGetValue(round.Id, out var dueAt) ? dueAt : string.Empty,
            time,
            string.Empty,
            RunSheet.RaceName(MixedHeats.ClassLabel(round), round.RoundLabel),
            DescribeKind(round),
            MixedHeats.ClassLabel(round),
            RunSheet.DriversWords(round.IsRecorded, drivers, here, countHere),
            RunSheet.LengthWords(LengthOf(round)),
            string.IsNullOrWhiteSpace(round.TrackLayoutName) ? "-" : round.TrackLayoutName,
            round.IsRecorded ? "Recorded" : isNext ? "NEXT" : status.Words);

        var row = _grid.Rows[index];
        row.Tag = round.Id;
        row.Cells["drivers"].Tag = DriversTip(round);
        row.Cells["dot"].Tag = new RowLook
        {
            Recorded = round.IsRecorded,
            HostsCard = isNext,
            StartUtc = round.IsRecorded ? null : start,
            Status = status,
            ClassColour = round.IsRecorded ? null : ClassColourFor(round.ClassName),
        };

        return row;
    }

    /// <summary>Adds a row that isn't a round.</summary>
    private void AddSheetRow(SheetRow kind, string time, string race, string drivers, string length, RunSheet.Status status, RowLook look)
    {
        var index = _grid.Rows.Add(
            string.Empty, string.Empty, time, string.Empty, race, string.Empty, string.Empty, drivers, length, string.Empty, status.Words);

        var row = _grid.Rows[index];
        row.Tag = kind;
        row.Cells["dot"].Tag = look;
    }

    private static RowLook? LookOf(DataGridViewRow row) => row.Cells["dot"].Tag as RowLook;

    /// <summary>Selects one row and nothing else, with the current cell on it.</summary>
    private void SelectOnly(DataGridViewRow row)
    {
        _grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible) ?? row.Cells[0];
        _grid.ClearSelection();
        row.Selected = true;
    }

    /// <summary>
    /// The sheet's own mouse and keyboard: a click on the fold shows or hides
    /// the earlier races, and rows that aren't rounds can't be selected.
    /// Wired once, from BuildGrid.
    /// </summary>
    private void WireSheet()
    {
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _grid.Rows.Count && _grid.Rows[e.RowIndex].Tag is SheetRow.Fold)
            {
                _showAllDone = !_showAllDone;
                RefreshGrid();
            }
        };

        _grid.SelectionChanged += (_, _) =>
        {
            if (_fillingSheet)
            {
                return;
            }

            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                if (row.Tag is SheetRow)
                {
                    row.Selected = false;
                }
            }
        };

        _grid.CellPainting += PaintSheetCell;
        _grid.RowPostPaint += PaintSheetRow;
        _grid.AllowUserToResizeRows = false;

        _textSize.Keep(SizeSheetColumns);
    }

    /// <summary>The fixed columns at the text size in use; the race and drivers columns share what's left.</summary>
    private void SizeSheetColumns()
    {
        void Fixed(string name, int width)
        {
            if (_grid.Columns[name] is { } column)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = UiLayout.AtTextSize(width);
                column.Resizable = DataGridViewTriState.False;
            }
        }

        Fixed("due", 70);
        Fixed("est", 96);
        Fixed("dot", 26);
        Fixed("length", 84);
        Fixed("status", 140);
        PlaceNowCard();
    }

    // ------------------------------------------------------------------
    //  Drawing
    // ------------------------------------------------------------------

    private Font PillFont()
    {
        if (_pillFont is not { } made || !ReferenceEquals(made.From, AppTheme.SmallFont))
        {
            made = (AppTheme.SmallFont, new Font(AppTheme.SmallFont, FontStyle.Bold));
            _pillFont = made;
        }

        return made.Bold;
    }

    /// <summary>
    /// One cell of the sheet, drawn by hand: the row's own background, and
    /// the cell's words in the sheet's colours. The lines along a row's top
    /// and the fold's words, which run across cells, come after the cells, in
    /// PaintSheetRow.
    /// </summary>
    private void PaintSheetCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is not { } g || e.RowIndex >= _grid.Rows.Count)
        {
            return;
        }

        var row = _grid.Rows[e.RowIndex];

        if (LookOf(row) is not { } look)
        {
            return;
        }

        var bounds = e.CellBounds;
        var selected = row.Selected && row.Tag is string;

        using (var back = new SolidBrush(selected ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            g.FillRectangle(back, bounds);
        }

        var band = (look.NowLine && !look.HostsCard ? BandHeight : 0) + (look.OutLine ? BandHeight : 0);
        var content = new Rectangle(bounds.X + 8, bounds.Y + band, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - band - 1));
        var name = _grid.Columns[e.ColumnIndex].Name;
        var quiet = look.Recorded || look.Kind is SheetRow.Cut;
        var ink = quiet ? AppTheme.TextMuted : AppTheme.TextDark;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var text = e.FormattedValue as string ?? string.Empty;

        switch (name)
        {
            case "est":
            case "due":
                if (look.Struck is { } struck)
                {
                    var top = new Rectangle(content.X, content.Y, content.Width, content.Height / 2);
                    var bottom = new Rectangle(content.X, content.Y + content.Height / 2, content.Width, content.Height / 2);
                    TextRenderer.DrawText(g, struck, AppTheme.SmallFont, top, AppTheme.TextMuted, flags | TextFormatFlags.Right | TextFormatFlags.Bottom);
                    var size = TextRenderer.MeasureText(g, struck, AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding);
                    using var strike = new Pen(AppTheme.TextMuted);
                    var y = top.Bottom - size.Height / 2 - 1;
                    g.DrawLine(strike, top.Right - size.Width, y, top.Right, y);
                    TextRenderer.DrawText(g, text, AppTheme.SmallFont, bottom, AppTheme.TextMuted, flags | TextFormatFlags.Right | TextFormatFlags.Top);
                }
                else
                {
                    var colour = look.Kind == SheetRow.Fold ? AppTheme.TextMuted : look.Late ? AppTheme.Danger : AppTheme.TextMuted;
                    var font = look.Kind == SheetRow.Fold ? AppTheme.SmallFont : AppTheme.BoldFont;
                    TextRenderer.DrawText(g, text, font, content, colour, flags | TextFormatFlags.Right);
                }

                break;

            case "dot":
                PaintDot(g, content, look);
                break;

            case "round":
                if (look.Kind != SheetRow.Fold)
                {
                    var font = look.Kind is SheetRow.End || quiet ? AppTheme.BodyFont : AppTheme.BoldFont;
                    var colour = look.Kind is SheetRow.End or SheetRow.Mains ? AppTheme.TextMuted : ink;
                    TextRenderer.DrawText(g, text, font, content, colour, flags | TextFormatFlags.Left);
                }

                break;

            case "drivers":
            case "length":
                TextRenderer.DrawText(g, text, AppTheme.BodyFont, content, AppTheme.TextMuted, flags | TextFormatFlags.Left);
                break;

            case "status":
                PaintPill(g, content, look.Recorded ? new RunSheet.Status(RunSheet.Pill.Recorded, "Recorded") : look.Status);
                break;
        }

        // The rule under every row but the card's.
        if (!look.HostsCard && look.Kind != SheetRow.Fold)
        {
            using var rule = new Pen(AppTheme.Border);
            g.DrawLine(rule, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }

        e.Handled = true;
    }

    /// <summary>The lines along the top of a row, and the fold's words - drawn over the cells, since each runs across several.</summary>
    private void PaintSheetRow(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count || LookOf(_grid.Rows[e.RowIndex]) is not { } look)
        {
            return;
        }

        var g = e.Graphics;
        var row = e.RowBounds;
        var gutter = Math.Max(0, (_grid.Columns["est"]?.Width ?? 0) + (_grid.Columns["due"] is { Visible: true } d ? d.Width : 0));
        var y = row.Y;

        if (look.OutLine)
        {
            var band = new Rectangle(row.X, y, row.Width, BandHeight);
            var mid = band.Y + band.Height / 2;

            using (var pen = new Pen(AppTheme.Danger, 2) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(pen, row.X + gutter + 12, mid, row.Right - 8, mid);
            }

            var words = _outByUtc is { } outBy ? "OUT BY " + AppTime.TimeOfDay(outBy.ToLocalTime()).ToUpperInvariant() : "OUT BY";
            var size = TextRenderer.MeasureText(g, words, AppTheme.MicroFont);
            var label = new Rectangle(row.X + gutter + 24, mid - size.Height / 2, size.Width + 8, size.Height);

            using (var back = new SolidBrush(AppTheme.Surface))
            {
                g.FillRectangle(back, label);
            }

            TextRenderer.DrawText(g, words, AppTheme.MicroFont, label, AppTheme.Danger, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            y += BandHeight;
        }

        if (look.NowLine)
        {
            var band = new Rectangle(row.X, y, row.Width, BandHeight);
            var mid = band.Y + band.Height / 2;
            var words = "NOW " + RunSheet.ShortClock(AppTime.TimeOfDay(DateTime.Now));
            var clock = new Rectangle(row.X, band.Y, Math.Max(10, gutter - 8), band.Height);

            TextRenderer.DrawText(g, words, AppTheme.MicroFont, clock, AppTheme.SynthCyan, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            using var pen = new Pen(AppTheme.SynthCyan, 2);
            g.DrawLine(pen, row.X + gutter + 4, mid, row.Right - 8, mid);
        }

        if (look.Kind == SheetRow.Fold && look.FoldWords is { } fold)
        {
            var from = gutter + (_grid.Columns["dot"]?.Width ?? 0) + 8;
            var rect = new Rectangle(row.X + from, row.Y, Math.Max(0, row.Width - from - 8), row.Height);
            TextRenderer.DrawText(g, fold, AppTheme.SmallFont, rect, AppTheme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>A row's dot on the timeline: filled green once run, hollow to come, dashed amber when a fix would drop it.</summary>
    private static void PaintDot(Graphics g, Rectangle content, RowLook look)
    {
        if (look.Kind is SheetRow.Fold or SheetRow.Now || look.HostsCard)
        {
            return;
        }

        var side = Math.Max(8, Math.Min(14, content.Height - 6));
        var dot = new Rectangle(content.X + (content.Width - side) / 2, content.Y + (content.Height - side) / 2, side, side);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (look.Recorded)
        {
            using var fill = new SolidBrush(AppTheme.Success);
            g.FillEllipse(fill, dot);
        }
        else
        {
            using var pen = new Pen(look.Kind == SheetRow.Cut ? AppTheme.Warning : AppTheme.TextMuted, 2);

            if (look.Kind == SheetRow.Cut)
            {
                pen.DashStyle = DashStyle.Dash;
            }

            g.DrawEllipse(pen, dot);
        }

        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>A status pill: a tinted lozenge, its shape, and its words. Drawn on the left of the cell, as wide as its words.</summary>
    private void PaintPill(Graphics g, Rectangle content, RunSheet.Status status)
    {
        if (status.Pill == RunSheet.Pill.None || string.IsNullOrEmpty(status.Words))
        {
            return;
        }

        var (ink, tint) = PillColours(status.Pill);
        var font = PillFont();
        var words = TextRenderer.MeasureText(g, status.Words, font, Size.Empty, TextFormatFlags.NoPadding);
        var mark = status.Mark == RunSheet.Mark.None ? 0 : (int)(font.Height * 0.62f);
        var height = font.Height + UiLayout.AtTextSize(6);
        var width = words.Width + mark + (mark > 0 ? UiLayout.AtTextSize(6) : 0) + UiLayout.AtTextSize(20);
        var pill = new Rectangle(content.X, content.Y + (content.Height - height) / 2, Math.Min(width, content.Width), height);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = AppTheme.RoundedRect(pill, height / 2))
        using (var back = new SolidBrush(tint))
        {
            g.FillPath(back, path);
        }

        var x = pill.X + UiLayout.AtTextSize(10);

        if (mark > 0)
        {
            DrawMark(g, new Rectangle(x, pill.Y + (pill.Height - mark) / 2, mark, mark), status.Mark, ink);
            x += mark + UiLayout.AtTextSize(6);
        }

        g.SmoothingMode = SmoothingMode.Default;

        TextRenderer.DrawText(g, status.Words, font, new Rectangle(x, pill.Y, Math.Max(0, pill.Right - x), pill.Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    /// <summary>A pill's ink and the tint behind it.</summary>
    internal static (Color Ink, Color Tint) PillColours(RunSheet.Pill pill)
    {
        var ink = pill switch
        {
            RunSheet.Pill.Recorded or RunSheet.Pill.Ready or RunSheet.Pill.OnTime => AppTheme.Success,
            RunSheet.Pill.Missing or RunSheet.Pill.Empty => AppTheme.Warning,
            RunSheet.Pill.Late => AppTheme.Danger,
            _ => AppTheme.TextMuted,
        };

        return (ink, Blend(ink, AppTheme.Surface, 0.82f));
    }

    /// <summary>
    /// A shape that says the state as well as the colour does: a tick, a
    /// triangle, a square. Drawn rather than typed, so no font can turn it
    /// into a box.
    /// </summary>
    internal static void DrawMark(Graphics g, Rectangle box, RunSheet.Mark mark, Color ink)
    {
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        switch (mark)
        {
            case RunSheet.Mark.Tick:
                using (var pen = new Pen(ink, Math.Max(2f, box.Height / 5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + box.Width * 0.08f, box.Y + box.Height * 0.55f),
                        new PointF(box.X + box.Width * 0.38f, box.Y + box.Height * 0.85f),
                        new PointF(box.X + box.Width * 0.95f, box.Y + box.Height * 0.15f),
                    });
                }

                break;

            case RunSheet.Mark.Triangle:
                using (var brush = new SolidBrush(ink))
                {
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(box.X + box.Width / 2f, box.Y),
                        new PointF(box.Right, box.Bottom),
                        new PointF(box.X, box.Bottom),
                    });
                }

                break;

            case RunSheet.Mark.Square:
                using (var brush = new SolidBrush(ink))
                {
                    var inset = box.Width / 8;
                    g.FillRectangle(brush, Rectangle.Inflate(box, -inset, -inset));
                }

                break;
        }

        g.SmoothingMode = smoothing;
    }

    /// <summary><paramref name="a"/> mixed into <paramref name="b"/>: 0 is all a, 1 all b.</summary>
    internal static Color Blend(Color a, Color b, float towardB)
    {
        var t = Math.Clamp(towardB, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    // ------------------------------------------------------------------
    //  The commands Next Steps' card and footer run
    // ------------------------------------------------------------------

    /// <summary>When the next race should start, from tonight's plan; null with nothing to run.</summary>
    internal DateTime? StartOfNext()
    {
        var day = DayRounds();

        return day.FirstOrDefault(r => !r.IsRecorded) is { } next ? Plan(day)?.ShownStartOf(next.Id) : null;
    }

    /// <summary>The next race's length on track, and how many of its drivers are here (and whether that's being counted).</summary>
    internal (TimeSpan Length, int Drivers, int Here, bool Counting)? NextFacts(RoundDefinition round) =>
        (LengthOf(round), round.AssignedTransponders.Count, HereIn(round), CountingHere());

    /// <summary>Edit Drivers on the footer: the selected round's, or the next race's with nothing selected.</summary>
    internal void EditDriversFromFooter()
    {
        if (SelectedRound() is null && NextRoundRow() is { } row)
        {
            SelectOnly(row);
        }

        EditRoundRoster();
    }

    /// <summary>"+ Add Race...": a main by hand on a race day, the heats built on a club night.</summary>
    internal void AddRaceFromFooter()
    {
        if (DayIsClubNight)
        {
            BuildQualifyingProgramme();
        }
        else
        {
            AddMainByHand();
        }
    }

    internal void ShowStandingsFromFooter() => ShowStandings();

    internal void ShowMarshalsFromFooter() => ShowMarshalAssignments();

    /// <summary>The footer's "More" menu: the day tools that had their own buttons, and the program's day-level commands.</summary>
    internal void ShowMoreMenu(Control at)
    {
        var menu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };

        menu.Items.Add(new ToolStripMenuItem("Timetable...", null, (_, _) => TimetableRequested?.Invoke()));

        if (!DayIsClubNight)
        {
            menu.Items.Add(new ToolStripMenuItem("Move the Top Finishers Up...", null, (_, _) => ApplyBumpUps(null)));
        }

        menu.Items.Add(new ToolStripMenuItem("Glossary...", null, (_, _) => RacingWordsForm.ShowFor(FindForm())));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(DayIsClubNight ? "Build Tonight's Races..." : "Build Tonight's Heats...", null, (_, _) => BuildQualifyingProgramme()));

        if (!DayIsClubNight)
        {
            menu.Items.Add(new ToolStripMenuItem("Build the Finals from Qualifying...", null, (_, _) => SeedMains(string.Empty)));
        }

        menu.Items.Add(new ToolStripMenuItem("Add a Late Entry...", null, (_, _) => AddLateEntry()));
        menu.Items.Add(new ToolStripMenuItem("Move Drivers Between Heats...", null, (_, _) => BalanceHeats()));
        menu.Items.Add(new ToolStripMenuItem("Save Heat Sheet...", null, (_, _) => PrintHeatSheet()));
        menu.Items.Add(new ToolStripMenuItem("Print Heat Sheet...", null, (_, _) => PrintHeatSheetOnPaper()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("We're Done After This Round...", null, (_, _) => FinishAfterThisRoundFromMenu())
        {
            Enabled = NightPlan.AfterThisRound(DayRounds(), RoundOnTrackId?.Invoke()) is not null,
        });

        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(at, new Point(0, at.Height));
    }

    /// <summary>The card's "Skip ▾": put the next race back one place, or to the end of the night. One undo entry.</summary>
    internal void PutOffNext(bool toTheEnd)
    {
        var day = DayRounds();
        var toRun = day.Where(r => !r.IsRecorded).ToList();

        if (toRun.Count < 2)
        {
            return;
        }

        var next = toRun[0];
        var anchor = toTheEnd ? null : toRun.Count > 2 ? toRun[2].Id : null;

        MoveWithUndo(new[] { next.Id }, anchor, "Skip: ");
        RefreshAll();
        StatusMessage?.Invoke(toTheEnd
            ? $"[program] {next.RoundLabel} moved to the end of tonight's program."
            : $"[program] {next.RoundLabel} moved back one race.");
    }

    /// <summary>The next race's row, or null when it isn't on show.</summary>
    private DataGridViewRow? NextRoundRow()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is string && LookOf(row) is { HostsCard: true })
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// "Apply" on the "Get back on time" card: makes the ticked changes, all
    /// under one undo entry, and says what was done. The words come back for
    /// "Tell the drivers".
    /// </summary>
    internal (bool Ok, string Message, string Announcement) ApplyFixes(IReadOnlyList<RunSheet.Choice> ticked)
    {
        var day = DayRounds();
        var changes = new List<string>();
        var spoken = new List<string>();
        var putBack = new List<Action>();

        // --- A minute off the qualifiers still to run ---
        if (ticked.Any(c => c.Kind == NightPlan.FixKind.ShorterQualifiers))
        {
            var onTrack = RoundOnTrackId?.Invoke();
            var before = new Dictionary<string, RaceFormat?>(StringComparer.Ordinal);
            var appDefault = RaceFormat.FromSettings(_settings);

            foreach (var round in day.Where(r => !r.IsRecorded && r.Kind == RoundKind.Qualifier && !string.Equals(r.Id, onTrack, StringComparison.Ordinal)))
            {
                var raceClass = _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));
                var format = round.FormatOverride ?? raceClass?.QualifierFormat ?? raceClass?.Format ?? appDefault;

                if (NightPlan.Shortened(format, 1) is { } shorter)
                {
                    before[round.Id] = round.FormatOverride;
                    round.FormatOverride = shorter;
                    _rounds.Update(round);
                }
            }

            if (before.Count > 0)
            {
                changes.Add($"{Plural.Of(before.Count, "qualifier")} a minute shorter");
                spoken.Add("qualifiers will be a minute shorter");
                putBack.Add(() =>
                {
                    foreach (var (id, format) in before)
                    {
                        if (_rounds.Get(id) is { } round)
                        {
                            round.FormatOverride = format;
                            _rounds.Update(round);
                        }
                    }
                });
            }
        }

        // --- One main fewer, when the finals are built ---
        if (ticked.Any(c => c.Kind == NightPlan.FixKind.OneMainFewer) && ActiveDay is { FinalsOneMainFewer: false } raceDay && _events is not null)
        {
            var dayId = raceDay.Id;
            raceDay.FinalsOneMainFewer = true;
            _events.Upsert(raceDay);
            changes.Add("one main fewer per class");
            spoken.Add("there will be one main fewer");
            putBack.Add(() =>
            {
                if (_events.Get(dayId) is { } current)
                {
                    current.FinalsOneMainFewer = false;
                    _events.Upsert(current);
                }
            });
        }

        void PutAllBack()
        {
            foreach (var undo in Enumerable.Reverse(putBack))
            {
                undo();
            }
        }

        // --- Drop a qualifying round ---
        //
        // Last, through DeleteRounds, so the rounds come back with their
        // drivers on the same undo entry as the other changes.
        var drop = ticked.FirstOrDefault(c => c.Kind == NightPlan.FixKind.DropRound)?.Round;
        var dropped = RoundsDropped(day, drop);

        if (dropped.Count > 0)
        {
            var name = DayIsClubNight ? $"Race {drop}" : $"Q{drop}";
            var answer = DeleteRounds(dropped, "Get back on time: ", alsoUndo: putBack.Count > 0 ? PutAllBack : null);

            if (!answer.Ok)
            {
                PutAllBack();
                return (false, answer.Message, string.Empty);
            }

            changes.Insert(0, $"{name} dropped ({Plural.Of(dropped.Count, "race")})");
            spoken.Insert(0, $"{name} is cancelled");
        }
        else if (putBack.Count > 0)
        {
            _undo.Capture("Get back on time: " + string.Join(", ", changes), PutAllBack);
        }

        if (changes.Count == 0)
        {
            // WHY (23 Nov 2026, bug hunt 24): "a minute shorter" ticked on
            // heats that can't lose one (run to laps, or already at 3
            // minutes) used to answer "already in tonight's plan", which
            // was not true - nothing had been, or could be, taken off.
            return (false, NothingToChangeWords(ticked), string.Empty);
        }

        _previewDropRound = null;
        _previewOther = TimeSpan.Zero;
        RefreshAll();

        var finish = Plan() is { } plan ? $" Racing should now finish about {AppTime.TimeOfDay(plan.FinishUtc.ToLocalTime())}." : string.Empty;
        var message = "Changed tonight's program: " + string.Join("; ", changes) + "." + finish;

        StatusMessage?.Invoke("[program] " + message);

        var announcement = "A change to tonight's program: " + string.Join(", and ", spoken) + "." + finish;

        return (true, message, announcement);
    }

    /// <summary>
    /// What Apply says when none of the ticked fixes changed anything
    /// (23 Nov 2026, bug hunt 24): a shorter run ticked alone means the
    /// qualifiers still to run can't be cut any further.
    /// </summary>
    internal static string NothingToChangeWords(IReadOnlyList<RunSheet.Choice> ticked) =>
        ticked.Count > 0 && ticked.All(c => c.Kind == NightPlan.FixKind.ShorterQualifiers)
            ? "Nothing to change - the qualifiers still to run are already as short as they go."
            : "Nothing to change - those fixes are already in tonight's plan.";

    /// <summary>"Tell the drivers": says the last change on the PA and the screens, as "We're done after this round" does.</summary>
    internal void Announce(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            NightCutShort?.Invoke(text);
        }
    }
}
