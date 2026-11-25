namespace HeatCaller;

/// <summary>
/// Where every moving part of the Focus animation is at one moment. Each
/// value runs 0 (as before) to 1 (as after) unless it says otherwise.
/// </summary>
/// <param name="Pressed">The Focus button is drawn pressed in.</param>
/// <param name="Sidebar">The sidebar's width: 0 its width before, 1 its width after.</param>
/// <param name="NarrowAlpha">The narrow sidebar's picture over the wide one, 0 to 1.</param>
/// <param name="Glide">The cards: 0 where they were (riding the sidebar's edge), 1 in their places after.</param>
/// <param name="Cross">Inside a card: 0 its contents before, 1 its contents after.</param>
/// <param name="InboxIn">The inbox: 0 off the right edge, 1 in its place. Can run a little over 1 entering - its overshoot.</param>
/// <param name="InboxAlpha">The inbox's opacity (its rows', leaving), 0 to 1.</param>
/// <param name="Light">The landing light along the board's top: 0 at its left, 1 at its right.</param>
/// <param name="LightAlpha">The light's opacity, 0 to 1.</param>
/// <param name="StageAlpha">The stage itself: 1 while it plays, down to 0 as it hands over to the real window.</param>
/// <param name="Done">The stage has nothing left to show.</param>
internal readonly record struct FocusMoment(
    bool Pressed,
    double Sidebar,
    double NarrowAlpha,
    double Glide,
    double Cross,
    double InboxIn,
    double InboxAlpha,
    double Light,
    double LightAlpha,
    double StageAlpha,
    bool Done);

/// <summary>
/// THE FOCUS ANIMATION'S TIMINGS. Which part moves when, for how long, on
/// which curve - as numbers, so they are tested without a window. FocusStage
/// draws them; see plans/focus-motion-plan.md and the "Focus Motion" mockup.
///
/// WHY (25 Nov 2026, Asher: "would you pay $10k for that animation?" - no):
/// the 24 Nov motion was a cross-fade between two pictures with a ring, a
/// sweep and a glow over it, and it waited ~360 ms before the page moved.
/// Now every piece travels. What needs nothing from the window after the
/// change moves on the first frame: the button presses in, the sidebar
/// narrows with the page riding its edge, and the column's buttons fold away
/// one by one. What needs the window after - where each card ends up - moves
/// from the "arrival", the moment the view has changed underneath and its
/// picture is taken (measured 25 Nov 2026: about 300-550 ms at 1440x900).
///
/// Focus: press 0-70; sidebar 0-200; column parts from 30, 20 ms apart, 150
/// each; cards glide A' to A'+230 (A' = the arrival, no earlier than 90);
/// inbox A'+40 to A'+280; its rows from A'+120, 24 ms apart; the landing
/// light A'+240 to A'+350; then 60 ms of hand-over.
///
/// Restore: press 0-70; the inbox's rows fade 0-70 and it slides out 0-170;
/// from A' (no earlier than 60) the sidebar widens over 210 and the cards
/// glide over 230; the column parts come back from A'+80, 20 ms apart; then
/// the hand-over. No light: going back is quieter.
///
/// WHY (25 Nov 2026, Asher: "speed up the animation"): every part about 30%
/// shorter - Focus ran about 540 ms after the arrival, now about 380 - and
/// the pictures it waits for now take about 10 ms, not 150-700 (see
/// MainForm.ClientPicture), so the cards move sooner as well.
/// </summary>
internal static class FocusChoreography
{
    public const double PressMs = 70;
    public const double SidebarMs = 200;
    public const double RestoreSidebarMs = 210;
    public const double GlideMs = 230;
    public const double EnterEarliest = 90;
    public const double LeaveEarliest = 60;

    /// <summary>The column's parts, folding away or coming back: the first's start, the gap between starts, how long each takes.</summary>
    public const double FoldStart = 30;
    public const double FoldStagger = 20;
    public const double FoldMs = 150;
    public const double RestoreFoldDelay = 80;

    /// <summary>How far a column part drifts right as it goes (comes back from), in pixels at 100%.</summary>
    public const int FoldDrift = 24;

    public const double InboxDelay = 40;
    public const double InboxMs = 240;
    public const double LeaveInboxMs = 170;
    public const double RowsDelay = 120;
    public const double RowStagger = 24;
    public const double RowMs = 160;

    /// <summary>How far each inbox row rises as it comes in, in pixels at 100%.</summary>
    public const int RowRise = 6;

    public const double LightDelay = 240;
    public const double LightMs = 110;
    public const double HandOverMs = 60;

    /// <summary>When the cards start: at the arrival, but never before <paramref name="earliest"/>. Null until it has arrived.</summary>
    public static double? CardsStart(double? arrivedAt, bool entering) =>
        arrivedAt is { } at ? Math.Max(at, entering ? EnterEarliest : LeaveEarliest) : null;

    /// <summary>When the last part has finished, before the hand-over - null until the arrival.</summary>
    public static double? End(double? arrivedAt, bool entering, int foldParts)
    {
        if (CardsStart(arrivedAt, entering) is not { } a)
        {
            return null;
        }

        if (entering)
        {
            // The column folds on its own clock, from the press.
            var folded = FoldStart + Math.Max(0, foldParts - 1) * FoldStagger + FoldMs;
            var rows = a + RowsDelay + (StaggeredRows - 1) * RowStagger + RowMs;
            return Math.Max(Math.Max(a + LightDelay + LightMs, rows), folded);
        }

        var back = a + RestoreFoldDelay + Math.Max(0, foldParts - 1) * FoldStagger + FoldMs;
        return Math.Max(Math.Max(a + GlideMs, a + RestoreSidebarMs), Math.Max(back, LeaveInboxMs));
    }

    /// <summary>The moment <paramref name="ms"/> after the press.</summary>
    public static FocusMoment At(double ms, bool entering, double? arrivedAt, int foldParts)
    {
        var a = CardsStart(arrivedAt, entering);
        var end = End(arrivedAt, entering, foldParts);
        var pressed = ms < PressMs;
        var stage = end is { } e ? 1 - Phase(ms, e, HandOverMs) : 1;
        var done = end is { } e2 && ms >= e2 + HandOverMs;

        if (entering)
        {
            var sidebar = EaseOutCubic(Phase(ms, 0, SidebarMs));

            // Into the narrow sidebar's own picture once it is there - never
            // before the wide one has nearly closed.
            var narrow = a is { } at0 ? EaseOutCubic(Phase(ms, Math.Max(140, at0), 90)) : 0;

            if (a is not { } at)
            {
                return new FocusMoment(pressed, sidebar, narrow, 0, 0, 0, 0, 0, 0, stage, done);
            }

            var glide = EaseOutQuint(Phase(ms, at, GlideMs));
            var light = Phase(ms, at + LightDelay, LightMs);

            return new FocusMoment(
                pressed,
                sidebar,
                narrow,
                glide,
                Cross(glide),
                Overshoot(Phase(ms, at + InboxDelay, InboxMs)),
                1,
                EaseInOutSine(light),
                light <= 0 || light >= 1 ? 0 : Math.Sin(Math.PI * light),
                stage,
                done);
        }

        // Restore: the inbox leaves on its own clock; the rest waits for the arrival.
        var inbox = 1 - EaseInCubic(Phase(ms, 0, LeaveInboxMs));
        var rows = 1 - Phase(ms, 0, 70);

        if (a is not { } back)
        {
            return new FocusMoment(pressed, 0, 1, 0, 0, inbox, rows, 0, 0, stage, done);
        }

        var widen = EaseOutCubic(Phase(ms, back, RestoreSidebarMs));
        var cards = EaseOutQuint(Phase(ms, back, GlideMs));

        return new FocusMoment(
            pressed,
            widen,

            // The narrow sidebar (the picture before) fades out as the wide one opens.
            1 - Math.Clamp(widen / 0.35, 0, 1),
            cards,
            Cross(cards),
            inbox,
            rows,
            0,
            0,
            stage,
            done);
    }

    /// <summary>
    /// A column part's way out (Focus) or back (Restore), 0 shown to 1 gone:
    /// part <paramref name="index"/> from the top, one after another.
    /// </summary>
    public static double Fold(double ms, int index, bool entering, double? arrivedAt)
    {
        if (entering)
        {
            return EaseOutCubic(Phase(ms, FoldStart + index * FoldStagger, FoldMs));
        }

        return CardsStart(arrivedAt, entering: false) is { } a
            ? 1 - EaseOutCubic(Phase(ms, a + RestoreFoldDelay + index * FoldStagger, FoldMs))
            : 1;
    }

    /// <summary>How many of the inbox's bands come in one after another; the rest come with the last of them.</summary>
    public const int StaggeredRows = 5;

    /// <summary>
    /// One inbox band's way in, 0 to 1: band <paramref name="index"/> from the
    /// top, entering. Only the first few are staggered (25 Nov 2026, found in
    /// the frame strip): all sixteen 35 ms apart, the inbox's foot had not come
    /// in by the hand-over, and appeared at once as the stage went.
    /// </summary>
    public static double RowIn(double ms, int index, double? arrivedAt) =>
        CardsStart(arrivedAt, entering: true) is { } a ? EaseOutCubic(Phase(ms, a + RowsDelay + Math.Min(index, StaggeredRows - 1) * RowStagger, RowMs)) : 0;

    /// <summary>
    /// Inside a card, after-content under before-content: the before fades
    /// over the middle of the glide, so the two meet where the card is
    /// moving fastest and a re-flow reads as part of the move.
    /// </summary>
    public static double Cross(double glide) => SmoothStep(Math.Clamp((glide - 0.3) / 0.4, 0, 1));

    /// <summary>How far through a part that starts at <paramref name="start"/> and lasts <paramref name="length"/>, 0 to 1.</summary>
    internal static double Phase(double ms, double start, double length) => Math.Clamp((ms - start) / length, 0, 1);

    internal static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    internal static double EaseInCubic(double t) => t * t * t;

    internal static double EaseOutQuint(double t) => 1 - Math.Pow(1 - t, 5);

    internal static double EaseInOutSine(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

    internal static double SmoothStep(double t) => t * t * (3 - 2 * t);

    /// <summary>
    /// Ease out with a small overshoot - about 4 px on the inbox's 400 -
    /// exactly 0 at the start and 1 at the end (s = 0.55 on "ease out back",
    /// which peaks 1.03% past the end).
    /// </summary>
    internal static double Overshoot(double t)
    {
        const double s = 0.55;
        var u = t - 1;
        return t <= 0 ? 0 : t >= 1 ? 1 : 1 + (s + 1) * u * u * u + s * u * u;
    }
}
