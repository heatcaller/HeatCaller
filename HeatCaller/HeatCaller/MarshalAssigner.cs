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
            var alsoDriving = previous.AssignedTransponders.Count - marshals.Count;
            var posts = new List<Post>();

            for (var p = 0; p < postCount; p++)
            {
                var transponder = p < marshals.Count ? marshals[p] : null;
                var name = transponder is null
                    ? null
                    : NameFor(transponder, profiles);

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
