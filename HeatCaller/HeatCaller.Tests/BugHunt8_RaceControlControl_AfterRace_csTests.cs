namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8 fixes in RaceControlControl.AfterRace.cs (20 Oct 2026).
///
/// The recap sequence needs a model, a session and a sound device to drive,
/// so - as HonoursSegmentTests and RecapBroadcastTests do - most of what is
/// pinned here is read off the source: where a line sits relative to
/// another. The broadcast floor's per-sequence names are driven for real.
/// </summary>
public sealed class BugHunt8_RaceControlControl_AfterRace_csTests : IDisposable
{
    public BugHunt8_RaceControlControl_AfterRace_csTests()
    {
        ShowFloor.ResetForTests();
    }

    public void Dispose()
    {
        ShowFloor.ResetForTests();
    }

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

    private static string AfterRaceSource => File.ReadAllText(
        Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "RaceControlControl.AfterRace.cs"));

    /// <summary>The text of one method, from its signature to the next member's doc comment.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {signature}.");

        var end = source.IndexOf("    /// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    [Fact]
    public void A_late_release_by_an_earlier_recap_sequence_leaves_the_newer_ones_claim_alone()
    {
        // #68: each sequence takes the floor under its own numbered name.
        ShowFloor.TakeOver("the race recap sequence #1", TimeSpan.FromMinutes(15));
        ShowFloor.TakeOver("the race recap sequence #2", TimeSpan.FromMinutes(15));

        ShowFloor.Release("the race recap sequence #1");

        Assert.Equal("the race recap sequence #2", ShowFloor.HeldBy);

        ShowFloor.Release("the race recap sequence #2");

        Assert.Null(ShowFloor.HeldBy);
    }

    [Fact]
    public void The_sequence_takes_and_releases_the_floor_under_its_own_name()
    {
        var source = AfterRaceSource;

        Assert.Contains("ShowFloor.TakeOver(floorOwner, TheProgrammeLongest);", source, StringComparison.Ordinal);
        Assert.Contains("ShowFloor.Release(floorOwner);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowFloor.Release(TheProgramme);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_recap_the_flag_bed_goes_before_the_honours_open_their_own_show()
    {
        // #20: the first HonoursOnTheirOwnAsync is the no-AI way out.
        var source = AfterRaceSource;
        var noRecap = source.IndexOf("if (!commentaryAvailable)\n", StringComparison.Ordinal);

        if (noRecap < 0)
        {
            noRecap = source.IndexOf("if (!commentaryAvailable)\r\n", StringComparison.Ordinal);
        }

        Assert.True(noRecap > 0, "The no-recap way out is no longer where this test reads it.");

        var honours = source.IndexOf("await HonoursOnTheirOwnAsync();", noRecap, StringComparison.Ordinal);
        var bedOff = source.IndexOf("flagBed?.Finish();", noRecap, StringComparison.Ordinal);

        Assert.True(honours > 0);
        Assert.True(bedOff > 0 && bedOff < honours,
            "The after-the-flag bed is still running when the honours open their own show - two beds at once.");
    }

    [Fact]
    public void The_principals_words_are_kept_only_after_the_booth_interview_has_aired()
    {
        // #70: not from WritePrincipalAsync, which runs as soon as they are written.
        var source = AfterRaceSource;
        var write = MethodBody(source, "private async Task<List<SpokenLine>?> WritePrincipalAsync(");

        Assert.DoesNotContain("KeepWhatThePrincipalSaid(", write, StringComparison.Ordinal);

        var aired = source.IndexOf("await SpeakInterviewAsync(principalTask, nextRace.Token", StringComparison.Ordinal);
        var kept = source.IndexOf("KeepWhatThePrincipalSaid(record, plan, heard", StringComparison.Ordinal);

        Assert.True(aired > 0 && kept > aired, "The diary is written before the interview has been heard.");
    }

    [Fact]
    public void The_principals_night_is_not_read_off_the_stores_on_the_thread_pool()
    {
        // #67 / #71: WritePrincipalAsync is handed the brief.
        var write = MethodBody(AfterRaceSource, "private async Task<List<SpokenLine>?> WritePrincipalAsync(");

        Assert.DoesNotContain("PrincipalNightBrief(", write, StringComparison.Ordinal);
        Assert.DoesNotContain("PrincipalTablePosition(", write, StringComparison.Ordinal);
        Assert.DoesNotContain("_profiles.", write, StringComparison.Ordinal);
        Assert.DoesNotContain("_rounds", write, StringComparison.Ordinal);
    }

    [Fact]
    public void A_demo_never_writes_the_principals_diary()
    {
        // #69.
        var keep = MethodBody(AfterRaceSource, "private void KeepWhatThePrincipalSaid(");
        var guard = keep.IndexOf("record.IsDemoSession()", StringComparison.Ordinal);
        var load = keep.IndexOf("PrincipalDiary.Load()", StringComparison.Ordinal);

        Assert.True(guard > 0 && load > guard, "KeepWhatThePrincipalSaid writes the diary for a demo race.");
        Assert.DoesNotContain("_profiles.", keep, StringComparison.Ordinal);
    }

    [Fact]
    public void A_two_voice_colour_segment_is_settled_only_after_its_render_wait()
    {
        // #73.
        var speak = MethodBody(AfterRaceSource, "private async Task SpeakColourAsync(");
        var renderWait = speak.IndexOf("(_, audio) = await WithinAsync(rendering, FollowOnWait, nextRace);", StringComparison.Ordinal);
        var firstSpoken = speak.IndexOf("director?.Spoken(said);", StringComparison.Ordinal);

        Assert.True(renderWait > 0 && firstSpoken > renderWait,
            "A two-hander is told Spoken before its turns' render is waited for, so a call-off there marks it said unheard.");
    }

    [Fact]
    public void Colour_never_reached_is_given_back_to_the_director_in_the_finally()
    {
        // #72.
        var source = AfterRaceSource;
        var release = source.IndexOf("ShowFloor.Release(floorOwner);", StringComparison.Ordinal);
        var giveBack = source.IndexOf("colourDirector.Dropped(unsaid);", StringComparison.Ordinal);

        Assert.True(release > 0 && giveBack > release,
            "Colour segments written but never reached are left awaiting for the director's 15-minute ceiling.");
    }
}
