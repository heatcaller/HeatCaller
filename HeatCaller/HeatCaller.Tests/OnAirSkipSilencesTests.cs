namespace HeatCaller.Tests;

/// <summary>
/// O pressed while On Air is talking stops the talking (1 Nov 2026, asked
/// for by Asher: "hitting o during voice still doesnt end the voice it
/// keeps talking").
///
/// WHY IT KEPT TALKING. On Air hands its whole run to the announcer's queue
/// as it goes: the opening, then the driver introductions, then the throw to
/// pit lane and the Team Principal - every one a Reminder, which the queue
/// treats as priority. The second press of O only cancelled On Air's own
/// token, so On Air stopped adding to the queue, but everything it had
/// already queued played on. The Stage it then handed over to queued the
/// staging call BEHIND all of it (NotifyRaceStaging only enqueues), and
/// staging's purge keeps priority lines - so the room heard the rest of the
/// opening, the introductions and pit lane before "drivers to the line".
///
/// THE FIX. Every way On Air is cut short silences the announcer first -
/// the line in the speakers, every queued line and a line still being
/// written - through the same cut Reset uses (AnnouncementService.SilenceNow),
/// and only then stages. On Air only runs between races, so there is no flag
/// call in the queue for that cut to swallow.
///
/// Source checks, as the rest of Race Control's are (RaceControlMorphTests):
/// the page cannot be built in a test.
/// </summary>
public sealed class OnAirSkipSilencesTests
{
    [Fact]
    public void Pressing_O_again_silences_the_announcer_before_skipping_to_Stage()
    {
        var onAir = WithoutComments(Source("RaceControlControl.OnAir.cs"));

        // The "pressed again" branch of RunOnAirAsync.
        var pressedAgain = MethodBody(onAir, "_onAirStageNow = true;\n            running.Cancel();", "return;");

        Assert.Contains("SilenceOnAir(", pressedAgain, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flag_or_Stage_that_skips_a_demos_On_Air_silences_it_too()
    {
        var skip = WithoutComments(MethodBody(Source("RaceControlControl.OnAir.cs"),
            "private bool SkipOnAirToTheDemo(", "private static readonly Color OnAirRed"));

        var cancelled = skip.IndexOf("running.Cancel();", StringComparison.Ordinal);
        var silenced = skip.IndexOf("SilenceOnAir(", StringComparison.Ordinal);

        Assert.True(cancelled > 0 && silenced > cancelled,
            "Skipping a demo's On Air must cancel On Air and then silence what it had queued.");
    }

    [Fact]
    public void On_Air_called_off_by_Stage_Reset_or_a_start_silences_what_it_queued()
    {
        // Stage pressed by hand, Countdown, Green, the checkered, Reset or a
        // warm-up opened while On Air runs all come through here - and the
        // staging call they queue must not wait behind On Air's opening.
        var cancel = WithoutComments(MethodBody(Source("RaceControlControl.StartSequence.cs"),
            "private void CancelAutoStart()", "// ==================== Warm-up"));

        var onAirBranch = cancel[cancel.IndexOf("if (_onAirCts is { } onAir)", StringComparison.Ordinal)..];

        Assert.Contains("SilenceOnAir(", onAirBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_silence_is_the_same_cut_Reset_uses()
    {
        var silence = WithoutComments(MethodBody(Source("RaceControlControl.OnAir.cs"),
            "private void SilenceOnAir(", "\n    }\n"));

        Assert.Contains("_announcer.SilenceNow(", silence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_staging_call_only_queues_so_anything_left_in_front_of_it_plays_first()
    {
        // The reason the cut has to come BEFORE Stage: staging adds its call
        // to the back of the queue rather than cutting in, and the opening's
        // lines are Reminders, which are priority and survive staging.
        var staging = WithoutComments(MethodBody(Source("AnnouncementService.RaceEvents.cs"),
            "public void NotifyRaceStaging()", "public void NotifyYellowFlag()"));

        Assert.Contains("EnqueueVoice(AnnouncementEventType.RaceStaging, null, null);", staging, StringComparison.Ordinal);
        Assert.DoesNotContain("SilenceNow", staging, StringComparison.Ordinal);

        var priority = MethodBody(Source("AnnouncementService.VoiceQueue.cs"),
            "private static bool IsPriorityAnnouncement(", ";\n");

        Assert.Contains("AnnouncementEventType.Reminder", priority, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    // Line endings made LF, so a test that names a line break works on a
    // checkout that turned them into CRLF.
    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");

    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, from.Length, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
