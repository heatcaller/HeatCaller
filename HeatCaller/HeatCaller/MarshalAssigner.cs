namespace HeatCaller;

/// <summary>
/// Works out who marshals which corner for each round.
///
/// The convention this follows is the near-universal club one: the drivers
/// from the PREVIOUS heat marshal the current one. They're already off the
/// track, already trackside, and it spreads the duty evenly without anyone
/// keeping a tally.
///
/// Two cases have to be handled honestly rather than fudged, because both
/// happen at every event:
///
///  - The FIRST round has no previous heat to draw from. There's no correct
///    answer the app can compute, so it says so instead of inventing one.
///  - The previous heat may have FEWER drivers than there are posts. The
///    posts that can't be covered are reported as unfilled rather than
///    silently dropped, since an uncovered corner is exactly the thing a
///    race director needs to know about before the race starts.
/// </summary>
public static class MarshalAssigner
{
    public sealed record Post(int Index, string Name, string? Transponder, string? DriverName)
    {
        public bool IsFilled => !string.IsNullOrWhiteSpace(Transponder);
    }

    public sealed record Assignment(
        RoundDefinition Round,
        RoundDefinition? MarshalledBy,
        List<Post> Posts,
        string? Problem)
    {
        public int FilledCount => Posts.Count(p => p.IsFilled);
        public int UnfilledCount => Posts.Count(p => !p.IsFilled);
    }

    /// <summary>
    /// Builds the marshal roster for every round of a class, in running
    /// order.
    /// </summary>
    public static List<Assignment> Assign(
        IReadOnlyList<RoundDefinition> rounds,
        TrackLayout? layout,
        ProfileStore profiles)
    {
        var postCount = Math.Max(0, layout?.MarshalPostCount ?? 0);
        var results = new List<Assignment>();

        if (postCount == 0)
        {
            return results;
        }

        var ordered = rounds.OrderBy(r => r.Order).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var round = ordered[i];
            var previous = i > 0 ? ordered[i - 1] : null;

            if (previous is null)
            {
                results.Add(new Assignment(round, null, EmptyPosts(layout!, postCount),
                    "First round of the day - no previous heat to marshal it. Assign by hand."));
                continue;
            }

            // Nobody marshals a race they are driving in. Back-to-back heats
            // often share a driver - a late addition, a two-class entrant, a
            // re-run - and posting them to a corner of the heat they are on
            // the grid for leaves that corner empty the moment it matters.
            var driving = round.AssignedTransponders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var marshals = previous.AssignedTransponders.Where(t => !driving.Contains(t)).ToList();

            // Swaps (11 Nov 2026): a post whose marshal arranged cover goes to
            // the driver covering it - never one driving in this round.
            var covered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (round.MarshalCovers is { Count: > 0 } covers)
            {
                for (var m = 0; m < marshals.Count; m++)
                {
                    if (covers.TryGetValue(marshals[m], out var cover) && !string.IsNullOrWhiteSpace(cover) && !driving.Contains(cover))
                    {
                        covered[cover] = marshals[m];
                        marshals[m] = cover;
                    }
                }
            }
            var alsoDriving = previous.AssignedTransponders.Count - marshals.Count;
            var posts = new List<Post>();

            for (var p = 0; p < postCount; p++)
            {
                var transponder = p < marshals.Count ? marshals[p] : null;
                var name = transponder is null
                    ? null
                    : NameFor(transponder, profiles);

                if (transponder is not null && covered.TryGetValue(transponder, out var coveringFor))
                {
                    name = $"{name} (covering {NameFor(coveringFor, profiles)})";
                }

                posts.Add(new Post(p, layout!.PostName(p), transponder, name));
            }

            var shortfall = postCount - marshals.Count;
            var problem = shortfall > 0
                ? $"{previous.RoundLabel} has {Plural.Of(marshals.Count, "driver")} free for {Plural.Of(postCount, "post")} - "
                  + $"{Plural.Of(shortfall, "corner")} uncovered."
                  + (alsoDriving > 0 ? $" ({alsoDriving} more {(alsoDriving == 1 ? "is" : "are")} driving in {round.RoundLabel}.)" : string.Empty)
                : null;

            results.Add(new Assignment(round, previous, posts, problem));
        }

        return results;
    }

    /// <summary>
    /// One driver's corner for one round: what a phone says as "You marshal
    /// Q2 Heat 2 (Stock Buggy) - Turn 3".
    /// </summary>
    /// <param name="RoundId">The round marshalled.</param>
    /// <param name="RoundTitle">Its title the way the phone pages name a race: "Q2 Heat 2 (Stock Buggy)".</param>
    /// <param name="Transponder">Who marshals it. Never leaves the laptop - see DriverLive.</param>
    /// <param name="PostName">The post's name, or "Corner 3".</param>
    public sealed record MarshalDuty(string RoundId, string RoundTitle, string Transponder, string PostName);

    /// <summary>
    /// Every filled corner of every round on the race day now open - or, on
    /// the legacy holder, of every round, exactly as Race Control's program
    /// reads it.
    ///
    /// WHY (22 Oct 2026, driver experience): a driver's phone now says which
    /// corner they marshal and when, and the announcer's grid call names
    /// the same people out loud. The two must never disagree, so the class,
    /// day and layout scoping the grid call worked out inline
    /// (AnnouncementService.SpokenMarshalsFor) is here, in
    /// <see cref="ClassRoundsFor"/> and <see cref="LayoutFor"/>, and this
    /// reads them the same way round by round. Nobody is given a corner of a
    /// race they are driving - <see cref="Assign"/> already sees to that.
    /// </summary>
    public static IReadOnlyList<MarshalDuty> DutiesForDay(
        RoundDefinitionStore rounds,
        RaceEventStore? events,
        TrackLayoutStore layouts,
        ProfileStore profiles)
    {
        var all = rounds.All;

        // The day, the way RaceControlControl.OrderedProgram bounds it.
        var day = events?.Current is { IsLegacy: false } current
            ? all.Where(r => current.RoundIds.Contains(r.Id)).ToList()
            : all.ToList();

        var duties = new List<MarshalDuty>();

        // Each class-and-day group worked out once, not once per round in it.
        var done = new Dictionary<string, List<Assignment>>(StringComparer.OrdinalIgnoreCase);

        foreach (var round in day)
        {
            var classRounds = ClassRoundsFor(round, rounds, events);
            var dayKey = events?.EventForRound(round.Id) is { IsLegacy: false } owner ? owner.Id : string.Empty;
            var key = $"{round.ClassName}|{dayKey}";

            if (!done.TryGetValue(key, out var assignments))
            {
                assignments = Assign(classRounds, LayoutFor(classRounds, layouts), profiles);
                done[key] = assignments;
            }

            var assignment = assignments.FirstOrDefault(a => a.Round.Id == round.Id);

            if (assignment is null)
            {
                continue;
            }

            foreach (var post in assignment.Posts.Where(p => p.IsFilled))
            {
                duties.Add(new MarshalDuty(round.Id, TitleOf(round), post.Transponder!.Trim(), post.Name));
            }
        }

        return duties;
    }

    /// <summary>
    /// The rounds a round's marshals are drawn from: its class's, kept to
    /// the race day the round is on. The legacy holder is not a day, so it
    /// means "no scoping", exactly as ActiveDay treats it on the Program tab
    /// - without this the first heat of tonight would be "marshalled" by
    /// whoever drove the last heat of last week. Moved here from the grid
    /// call (22 Oct 2026, driver experience).
    /// </summary>
    public static IReadOnlyList<RoundDefinition> ClassRoundsFor(RoundDefinition round, RoundDefinitionStore rounds, RaceEventStore? events)
    {
        IReadOnlyList<RoundDefinition> classRounds = rounds.ForClass(round.ClassName);

        if (events?.EventForRound(round.Id) is { IsLegacy: false } day)
        {
            var dayIds = day.RoundIds.ToHashSet(StringComparer.Ordinal);
            classRounds = classRounds.Where(r => dayIds.Contains(r.Id)).ToList();
        }

        return classRounds;
    }

    /// <summary>
    /// The track whose posts a class's rounds use, found the way the
    /// Marshals dialog finds it: the first round of the class that names
    /// one. Null when none does, or the layout has gone.
    /// </summary>
    public static TrackLayout? LayoutFor(IReadOnlyList<RoundDefinition> classRounds, TrackLayoutStore layouts)
    {
        var layoutName = classRounds
            .Select(r => r.TrackLayoutName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        return layoutName is null
            ? null
            : layouts.All.FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A round's title as the phone pages give it - "Q2 Heat 2 (Stock
    /// Buggy)" - the same words as RaceControlControl.RoundTitle, so the
    /// race a driver marshals reads like the race they drive.
    /// </summary>
    public static string TitleOf(RoundDefinition round) =>
        string.IsNullOrWhiteSpace(round.ClassName)
            ? round.RoundLabel
            : $"{round.RoundLabel} ({MixedHeats.ClassLabel(round)})"; // both classes of a shared heat (Phase 4.1)

    private static List<Post> EmptyPosts(TrackLayout layout, int postCount) =>
        Enumerable.Range(0, postCount)
            .Select(p => new Post(p, layout.PostName(p), null, null))
            .ToList();

    private static string NameFor(string transponder, ProfileStore profiles)
    {
        var profile = profiles.Get(transponder);
        return string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;
    }

    /// <summary>
    /// Plain-text roster, for printing or pasting into a group chat. Kept
    /// separate from the on-screen view so the text can be copied without
    /// a screenshot.
    /// </summary>
    public static string ToText(string className, IReadOnlyList<Assignment> assignments)
    {
        var lines = new List<string> { $"CORNER MARSHALS - {className}", string.Empty };

        foreach (var assignment in assignments)
        {
            lines.Add(assignment.MarshalledBy is null
                ? $"{assignment.Round.RoundLabel}  (marshals: not assigned)"
                : $"{assignment.Round.RoundLabel}  (marshaled by {assignment.MarshalledBy.RoundLabel})");

            foreach (var post in assignment.Posts)
            {
                lines.Add($"   {post.Name}: {(post.IsFilled ? post.DriverName : "- UNCOVERED -")}");
            }

            if (assignment.Problem is not null)
            {
                lines.Add($"   ! {assignment.Problem}");
            }

            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
