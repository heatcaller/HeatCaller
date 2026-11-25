namespace HeatCaller;

/// <summary>Where every moving part of the Focus animation is at one moment. Each value runs 0 (not started) to 1 (done) unless it says otherwise.</summary>
/// <param name="Curtain">The sidebar: 0 at its width before, 1 at its width after.</param>
/// <param name="Cross">The page: 0 the picture before, 1 the picture after.</param>
/// <param name="InboxIn">The inbox: 0 out of sight, 1 in its place. Can run a little over 1 entering - its spring.</param>
/// <param name="InboxAlpha">The inbox's opacity, 0 to 1.</param>
/// <param name="Edge">The pink glow down the inbox's left edge, 0 to 1.</param>
/// <param name="Sweep">The cyan light along the top of the board: 0 at the left, 1 at the right.</param>
/// <param name="SweepAlpha">The light's opacity, 0 to 1.</param>
/// <param name="Ring">The ring of light from the button: 0 at the button's edge, 1 grown out.</param>
/// <param name="RingAlpha">The ring's opacity, 0 to 1.</param>
/// <param name="StageAlpha">The stage itself: 1 while it plays, down to 0 as it hands over to the real window.</param>
internal readonly record struct FocusFrame(
    double Curtain,
    double Cross,
    double InboxIn,
    double InboxAlpha,
    double Edge,
    double Sweep,
    double SweepAlpha,
    double Ring,
    double RingAlpha,
    double StageAlpha);

/// <summary>
/// THE FOCUS ANIMATION'S TIMINGS (24 Nov 2026, asked for by Asher: "that
/// should be animated in a way that looks like it costs $10,000"). Which part
/// moves when, for how long, on which curve - as numbers, so they are tested
/// without a window. FocusStage draws them; see plans/race-control-foot-plan.md.
///
/// Entering, about 650 ms: the button's ring of light (0-380, done before
/// the page under it has moved); the sidebar
/// slides shut and the page turns into the wide board (60-360), a cyan light
/// running along the board's top (100-860, the last of it under the
/// hand-over); the inbox springs in from the right with a pink glow on its
/// edge (180-640), its rows dropping in one after another (300 on, 30 ms
/// apart); and the stage fades over the real window (560-650).
///
/// Leaving (Restore), about 420 ms: the inbox's rows fade and it slides out
/// (0-220), the sidebar opens and the page turns back (120-400), and the
/// stage fades (340-420).
/// </summary>
internal static class FocusChoreography
{
    /// <summary>How long Focus takes, in milliseconds.</summary>
    public const double EnterMs = 650;

    /// <summary>How long Restore takes, in milliseconds.</summary>
    public const double LeaveMs = 420;

    /// <summary>How far the inbox travels as it comes in or goes, in pixels at 100%.</summary>
    public const int InboxTravel = 48;

    /// <summary>How far each inbox row rises as it drops in, in pixels at 100%.</summary>
    public const int RowRise = 6;

    /// <summary>Each inbox row starts this long after the one above it, entering.</summary>
    public const double RowStagger = 30;

    /// <summary>The frame at <paramref name="ms"/> into Focus.</summary>
    public static FocusFrame Enter(double ms) => new(
        Curtain: EaseOutCubic(Phase(ms, 60, 300)),
        Cross: EaseInOutCubic(Phase(ms, 60, 300)),
        InboxIn: Spring(Phase(ms, 180, 460)),
        InboxAlpha: EaseOutCubic(Phase(ms, 180, 260)),
        Edge: Pulse(Phase(ms, 200, 450)),
        Sweep: EaseInOutSine(Phase(ms, 100, 760)),
        SweepAlpha: Pulse(Phase(ms, 100, 760)),
        Ring: EaseOutCubic(Phase(ms, 0, 380)),
        RingAlpha: 1 - EaseOutCubic(Phase(ms, 0, 380)),
        StageAlpha: 1 - Phase(ms, 560, 90));

    /// <summary>The frame at <paramref name="ms"/> into Restore. Curtain and Cross run 0 to 1 as entering does; InboxIn runs back from 1 to 0.</summary>
    public static FocusFrame Leave(double ms) => new(
        Curtain: EaseInOutCubic(Phase(ms, 120, 280)),
        Cross: EaseInOutCubic(Phase(ms, 120, 280)),
        InboxIn: 1 - EaseInCubic(Phase(ms, 0, 220)),
        InboxAlpha: 1 - Phase(ms, 0, 160),
        Edge: 0,
        Sweep: 0,
        SweepAlpha: 0,
        Ring: EaseOutCubic(Phase(ms, 0, 320)),
        RingAlpha: 1 - EaseOutCubic(Phase(ms, 0, 320)),
        StageAlpha: 1 - Phase(ms, 340, 80));

    /// <summary>
    /// One inbox row's way in, 0 to 1: row <paramref name="index"/> from the
    /// top, entering. Leaving, the rows go together, with the inbox's own
    /// opacity.
    /// </summary>
    public static double RowIn(double ms, int index) => EaseOutCubic(Phase(ms, 300 + index * RowStagger, 260));

    /// <summary>
    /// The two pages' opacities at <paramref name="cross"/> (0 to 1): the
    /// page before fading out over the first 60%, the page after fading up
    /// over the last 60%, so they meet only faintly in the middle - a dip
    /// through the window's background rather than a cross-fade, which showed
    /// the same words twice, offset.
    /// </summary>
    public static (double From, double To) Dip(double cross) =>
        (Math.Clamp(1 - cross / 0.6, 0, 1), Math.Clamp((cross - 0.4) / 0.6, 0, 1));

    /// <summary>
    /// The wide sidebar's opacity over the narrow one at <paramref name="curtain"/>
    /// (0 to 1): closing, solid until 60% of the way, then fading so it has
    /// gone as it reaches the narrow one's width; opening, fading up over the
    /// first 40%. Without it the narrow sidebar's own icons jumped in at the end.
    /// </summary>
    public static double CurtainAlpha(double curtain, bool narrowing) =>
        narrowing ? Math.Clamp((1 - curtain) / 0.4, 0, 1) : Math.Clamp(curtain / 0.4, 0, 1);

    /// <summary>How long one run of the light lasts while Focus waits for the window to change underneath.</summary>
    public const double WaitingRunMs = 800;

    /// <summary>
    /// The light along the board while Focus is still waiting for the picture
    /// after (24 Nov 2026: docking a closed inbox takes about 700 ms, measured):
    /// from 360 ms - once the sidebar has closed - a run every 800 ms, and
    /// null before then. Where it is (0 to 1) and how bright.
    /// </summary>
    public static (double At, double Alpha)? Waiting(double ms)
    {
        if (ms < 360)
        {
            return null;
        }

        var t = (ms - 360) % WaitingRunMs / WaitingRunMs;
        return (EaseInOutSine(t), Pulse(t) * 0.8);
    }

    /// <summary>Whether the animation is over at <paramref name="ms"/>.</summary>
    public static bool Done(double ms, bool entering) => ms >= (entering ? EnterMs : LeaveMs);

    /// <summary>How far through a part that starts at <paramref name="start"/> and lasts <paramref name="length"/>, 0 to 1.</summary>
    internal static double Phase(double ms, double start, double length) => Math.Clamp((ms - start) / length, 0, 1);

    internal static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    internal static double EaseInCubic(double t) => t * t * t;

    internal static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    internal static double EaseInOutSine(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

    /// <summary>Up and back down: 0, 1 at a third of the way, then down to 0.</summary>
    internal static double Pulse(double t) => t <= 0 || t >= 1 ? 0 : t < 1 / 3d ? t * 3 : 1 - (t - 1 / 3d) * 1.5;

    /// <summary>
    /// A soft spring: past 1 by about 3% near the end, then settled at
    /// exactly 1. Overshoot s = 0.9 on the usual "ease out back".
    /// </summary>
    internal static double Spring(double t)
    {
        const double s = 0.9;
        var u = t - 1;
        return t <= 0 ? 0 : t >= 1 ? 1 : 1 + (s + 1) * u * u * u + s * u * u;
    }
}
