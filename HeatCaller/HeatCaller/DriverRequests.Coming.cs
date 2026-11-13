namespace HeatCaller;

/// <summary>
/// "I'm coming" from the Me page (12 Nov 2026, setup plan C1): a driver says
/// they'll be at the club's next race night, in these classes, and the
/// laptop writes it down by itself, with no desk in between. The badge (and
/// the driver's own PIN, when they have one) has already been checked by the
/// relay, so the badge is who they are.
/// </summary>
internal sealed partial class DriverRequestDesk
{
    /// <summary>Who said they're coming, night by night. Set by the host; without it a "coming" waits.</summary>
    public PreEntryStore? PreEntries { get; set; }

    /// <summary>The night the club has announced, or null. Set by the host.</summary>
    public Func<AnnouncedNight?>? AnnouncedNight { get; set; }

    internal bool ApplyComing(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var night = AnnouncedNight?.Invoke();

        if (night is null || !string.Equals(night.DayKey, request.Text("day"), StringComparison.Ordinal))
        {
            said = "That night isn't the club's next one any more - have a look at Home for the next.";
            return false;
        }

        if (night.Off)
        {
            said = $"Racing's off on {NextNight.When(night.Day)}.";
            return false;
        }

        if (!night.TakingEntries)
        {
            said = $"The club isn't taking entries from phones for {NextNight.When(night.Day)} - just turn up and check in.";
            return false;
        }

        var classes = ClassesOf(request.Text("classes"), night.Classes);

        if (request.Text("classes").Length > 0 && classes.Count == 0)
        {
            said = $"None of those classes is on {NextNight.When(night.Day)}.";
            return false;
        }

        PreEntries!.Set(night.DayKey, profile.Transponder, classes, request.AskedUtc);
        said = ComingTonight.Said(night.Day, classes);
        _log(classes.Count == 0
            ? "[driver requests] A driver said from their phone they're not coming after all."
            : "[driver requests] A driver said from their phone they're coming to the next race night.");

        return true;
    }

    /// <summary>
    /// The classes a driver ticked, kept to those on the night (spelled the
    /// night's way), each once. With no classes announced, whatever they ticked.
    /// </summary>
    internal static List<string> ClassesOf(string ticked, IReadOnlyList<string> onTheNight)
    {
        var chosen = new List<string>();

        foreach (var raw in ticked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = onTheNight.Count == 0
                ? raw
                : onTheNight.FirstOrDefault(c => string.Equals(OnTheWire(c), OnTheWire(raw), StringComparison.OrdinalIgnoreCase))?.Trim();

            if (name is { Length: > 0 } && !chosen.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                chosen.Add(name);
            }
        }

        return chosen;
    }

    /// <summary>
    /// A class name as the Me page sends it: the ticked classes come as one
    /// comma-separated line, so the page turns a comma inside a name
    /// ("Buggy, 2WD") into a space and runs of spaces into one.
    /// </summary>
    private static string OnTheWire(string name) =>
        string.Join(' ', name.Replace(',', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
