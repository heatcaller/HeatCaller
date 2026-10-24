namespace HeatCaller;

/// <summary>
/// Threaded mode: the UI's word that practice may start at the next line
/// pass made while the race is Idle, and with which targets - handed to
/// <see cref="RaceTiming.PracticeAutoStart"/>. Immutable: the UI makes a new
/// one whenever anything in it changes, and the timing thread reads it
/// without a lock.
/// </summary>
/// <remarks>
/// WHY (24 Oct 2026, timing layer, P7): practice used to be started on the
/// UI thread before the pass that triggered it was counted, so in threaded
/// mode every pass at Idle went round by the window. The UI now says
/// beforehand everything only it can know (the setting, the recovery offer
/// answered, Line Up shut, the Practice page there and not running, the
/// targets on that page) and the timing thread starts practice on the
/// crossing itself, then hands the window <see cref="PracticeAutoStarted"/>.
/// </remarks>
internal sealed class PracticeAutoStartPlan
{
    /// <param name="lapLimit">The Practice page's lap target, as StartPractice reads it: null for none.</param>
    /// <param name="timeLimit">Its time target, the same way: null for none.</param>
    /// <param name="adopt">
    /// Called on the UI thread, in order with the race's events, once practice
    /// has been started on this plan - for the Practice page to take the
    /// running session over (see PracticeModeControl.AdoptAutoStartedPractice).
    /// </param>
    public PracticeAutoStartPlan(int? lapLimit, TimeSpan? timeLimit, Action<PracticeAutoStarted> adopt)
    {
        ArgumentNullException.ThrowIfNull(adopt);
        LapLimit = lapLimit;
        TimeLimit = timeLimit;
        Adopt = adopt;
    }

    public int? LapLimit { get; }

    public TimeSpan? TimeLimit { get; }

    public Action<PracticeAutoStarted> Adopt { get; }

    /// <summary>True when <paramref name="other"/> would start practice with the same targets and tell the same page.</summary>
    public bool SameAs(PracticeAutoStartPlan? other) =>
        other is not null && other.LapLimit == LapLimit && other.TimeLimit == TimeLimit && other.Adopt == Adopt;
}

/// <summary>
/// Threaded mode: practice has been started at the gate on the timing
/// thread. Handed to the UI (PracticeAutoStartPlan.Adopt) in order with the
/// race's events, after the start's own and before the crossing's lap.
/// </summary>
internal sealed class PracticeAutoStarted
{
    public PracticeAutoStarted(int? lapLimitBefore, TimeSpan? timeLimitBefore, RaceStartType startTypeBefore, DirectLapEvent crossing, bool cancelled = false)
    {
        LapLimitBefore = lapLimitBefore;
        TimeLimitBefore = timeLimitBefore;
        StartTypeBefore = startTypeBefore;
        Crossing = crossing;
        Cancelled = cancelled;
    }

    /// <summary>
    /// True when a race start took this practice back before the window got
    /// to it (RaceTiming.TakeBackPracticeAutoStartForRace): the session has
    /// already been handed back, and there is nothing to take over - the page
    /// is only told, for one line in the console.
    /// </summary>
    public bool Cancelled { get; }

    /// <summary>The same notice, marked as taken back by a race start.</summary>
    public PracticeAutoStarted AsCancelled() => new(LapLimitBefore, TimeLimitBefore, StartTypeBefore, Crossing, cancelled: true);

    /// <summary>The session's lap limit before practice took it - handed back at Stop, as StartPractice keeps it.</summary>
    public int? LapLimitBefore { get; }

    /// <summary>Its time limit before, the same way.</summary>
    public TimeSpan? TimeLimitBefore { get; }

    /// <summary>Its start type before, the same way.</summary>
    public RaceStartType StartTypeBefore { get; }

    /// <summary>The crossing that started it - counted as the driver's start lap.</summary>
    public DirectLapEvent Crossing { get; }
}
