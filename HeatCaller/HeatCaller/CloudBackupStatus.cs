namespace HeatCaller;

/// <summary>
/// Whether the club's backups are actually reaching the cloud, as one
/// green/amber/red answer with the words that go beside the colour.
///
/// Cloud copies used to be reported only as they happened - a line in the
/// console after each backup, and a list on the Backup &amp; Restore page of
/// where copies would go. So a club whose OneDrive folder had been unlinked
/// or moved saw nothing wrong: the page's line said what OneDrive needed in
/// the same muted grey as everything else, the console line had scrolled
/// away weeks ago, and the backups were quietly staying on the one PC they
/// exist to survive. This judges it in one place instead; the page and
/// Mission Control only colour the answer.
///
/// Decides nothing from the world itself - it is handed what the settings
/// say and what the last copy did - so every case is tested without a cloud
/// account, a window or a backup. The same three levels as Mission Control
/// (<see cref="MissionControl.Level"/>), because this is one of its tiles
/// too and a second set of colours would only have to be kept in step.
/// </summary>
internal static class CloudBackupStatus
{
    /// <summary>A cloud service the club switched on, and whether its sync folder was found on this PC just now.</summary>
    internal sealed record Service(string Name, string AppName, bool FolderFound);

    /// <param name="Services">The services switched on, each with whether its folder is on this PC now. Empty when the club asked for no cloud copies.</param>
    /// <param name="LastSuccessUtc">When a copy last reached every service switched on; null when none ever has.</param>
    /// <param name="LastProblem">Why the last copy didn't get there, in the words it was reported in; empty when it did.</param>
    /// <param name="NowUtc">Now.</param>
    internal sealed record Facts(
        IReadOnlyList<Service> Services,
        DateTime? LastSuccessUtc,
        string LastProblem,
        DateTime NowUtc);

    /// <summary>
    /// The answer: how worried to be, two or three words for beside the
    /// light, and a sentence saying what is wrong and what to do about it.
    /// Never a bare colour: a red light that doesn't say why is worse than
    /// no light at all on a race night run by volunteers.
    /// </summary>
    internal sealed record State(MissionControl.Level Level, string Headline, string Detail);

    /// <summary>
    /// A copy older than this is called out of date. Two weeks, because the
    /// automatic backup runs daily by default and keeps a fortnight of them:
    /// a club that has gone this long without a copy reaching the cloud has
    /// stopped backing up, whatever the switches still say.
    /// </summary>
    internal static readonly TimeSpan OutOfDateAfter = TimeSpan.FromDays(14);

    /// <summary>
    /// The facts as the settings and this PC have them now. Looks for each
    /// service's sync folder (CloudBackup.FindSyncRoot), so call it on the
    /// UI thread alongside the settings, as the Backup &amp; Restore page does.
    /// </summary>
    public static Facts Read(AppSettings settings, DateTime nowUtc)
    {
        var services = new List<Service>();

        foreach (var service in Enum.GetValues<CloudService>())
        {
            if (!CloudBackup.IsEnabled(settings, service))
            {
                continue;
            }

            services.Add(new Service(
                CloudBackup.NameOf(service),
                CloudBackup.AppNameOf(service),
                CloudBackup.FindSyncRoot(service) is not null));
        }

        return new Facts(services, settings.LastCloudBackupUtc, settings.LastCloudBackupProblem ?? string.Empty, nowUtc);
    }

    /// <summary>The light, from the facts. Pure: same facts, same answer.</summary>
    public static State Assess(Facts facts)
    {
        if (facts.Services.Count == 0)
        {
            // Not an error, and deliberately not red: plenty of clubs keep
            // their backups on the one PC and have decided that. Dimmed,
            // like Mission Control's other "not in use tonight" tiles.
            return new State(MissionControl.Level.Off, "Off",
                "Backups stay on this PC only. Turn on a cloud service on the Backup & Restore page to keep a copy somewhere else.");
        }

        var missing = facts.Services.Where(s => !s.FolderFound).ToList();
        var found = facts.Services.Where(s => s.FolderFound).ToList();

        // Checked before the last copy's own result, because a folder that
        // has gone is the thing to fix: the last copy may well have worked,
        // back when the folder was still linked.
        if (missing.Count > 0)
        {
            var what = $"{Names(missing)} {(missing.Count == 1 ? "isn't" : "aren't")} installed or signed in on this PC";
            var todo = $" Install and sign in to {Names(missing.Select(s => s.AppName))}, then press Check Again.";

            // All of them gone and nothing is reaching the cloud at all;
            // one of several and the club still has a copy off this PC,
            // which is the part that matters - worth a look, not an alarm.
            return found.Count == 0
                ? new State(MissionControl.Level.Problem, "Can't copy", $"No backup is reaching the cloud: {what}.{todo}")
                : new State(MissionControl.Level.Watch, "Partly working", $"Copies still reach {Names(found)}, but {what}.{todo}");
        }

        if (!string.IsNullOrWhiteSpace(facts.LastProblem))
        {
            return new State(MissionControl.Level.Problem, "Copy failed",
                $"The last backup didn't reach the cloud. {facts.LastProblem.Trim()} Press Back Up Now once that is sorted out.");
        }

        if (facts.LastSuccessUtc is not { } last)
        {
            // Amber, not red: the switches may have been ticked a minute
            // ago, and the next backup will copy one up by itself.
            return new State(MissionControl.Level.Watch, "Not yet",
                $"No backup has reached {Names(facts.Services)} yet. The next backup copies one there - or press Back Up Now.");
        }

        var age = facts.NowUtc - last;

        if (age >= OutOfDateAfter)
        {
            // Amber, not red: there IS a copy off this PC, it is just old.
            return new State(MissionControl.Level.Watch, "Out of date",
                $"The last copy reached {Names(facts.Services)} {Plural.Of((long)age.TotalDays, "day")} ago. Check backups are still being made - press Back Up Now.");
        }

        return new State(MissionControl.Level.Good, "Up to date",
            $"The last backup reached {Names(facts.Services)} on {last.ToLocalTime():yyyy-MM-dd HH:mm}.");
    }

    /// <summary>
    /// Writes down what a round of cloud copies did, so the light can say it
    /// later - after the console line has scrolled away and the club has
    /// gone home. Does NOT save the settings; the caller does, as it saves
    /// LastAutoBackupUtc in the same breath.
    /// </summary>
    public static void Record(AppSettings settings, IReadOnlyList<CloudBackup.CopyOutcome> outcomes, DateTime nowUtc)
    {
        if (outcomes.Count == 0)
        {
            return;
        }

        var failed = outcomes.Where(o => !o.Copied).ToList();

        if (failed.Count == 0)
        {
            settings.LastCloudBackupUtc = nowUtc;
            settings.LastCloudBackupProblem = string.Empty;
            return;
        }

        // Only the lines of the ones that did NOT get there, and the time of
        // the last fully good copy left as it was: the light has to say what
        // is wrong, and "Copied to OneDrive." beside a red light says
        // nothing. A partly good round is not recorded as a success either -
        // one service having a copy is not the copy the club asked for.
        settings.LastCloudBackupProblem = string.Join(" ", failed.Select(o => o.Line));
    }

    /// <summary>
    /// Keeps the written-down result true when the club ticks or unticks a
    /// service, because Assess reads it as being about the services ticked
    /// now. A service ticked on has never had a copy, so no copy has reached
    /// every service switched on yet: the light reads "Not yet" rather than
    /// green on the strength of the others. The last problem is kept, as it
    /// is about services that are all still ticked. A service ticked off
    /// leaves the time true (that copy reached the rest too), but the last
    /// problem may have been about the one just switched off, and saying it
    /// beside a red light for up to a day would be wrong - so it goes, and
    /// with it the time, since a round that had a problem was not a full
    /// success for the services left either; the next backup settles it.
    /// Does NOT save the settings; the caller does.
    /// </summary>
    public static void ServiceSwitched(AppSettings settings, bool switchedOn)
    {
        if (switchedOn)
        {
            settings.LastCloudBackupUtc = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.LastCloudBackupProblem))
        {
            settings.LastCloudBackupUtc = null;
            settings.LastCloudBackupProblem = string.Empty;
        }
    }

    /// <summary>"OneDrive", "OneDrive and Dropbox", "Google Drive, iCloud Drive and OneDrive".</summary>
    private static string Names(IEnumerable<Service> services) => Names(services.Select(s => s.Name));

    private static string Names(IEnumerable<string> names)
    {
        var list = names.ToList();

        return list.Count switch
        {
            0 => "the cloud",
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}
