namespace HeatCaller.Tests;

/// <summary>
/// Driver experience PV (22 Oct 2026): the pure parts of MainForm.Privacy -
/// when the register needs rebuilding, who is owed a republish, which saved
/// sessions go again. (Whose podium videos came down too, until 28 Oct
/// 2026, when the videos stopped going online.)
/// </summary>
public sealed class DriverExp_PV_PrivacyRepublishTests
{
    private static DriverProfile Sam(bool initials = false, bool noPhoto = false, bool noVideo = false) => new()
    {
        Transponder = "T1",
        DriverName = "Sam Parker",
        InitialsOnline = initials,
        NoPhotoOnline = noPhoto,
        NoVideoOnline = noVideo,
    };

    private static DriverProfile Alex() => new() { Transponder = "T2", DriverName = "Alex Jones" };

    private static SessionRecord Session(string id, DateTime startedUtc, bool demo = false, params string[] transponders) => new()
    {
        Id = id,
        StartedAtUtc = startedUtc,
        EndedAtUtc = startedUtc.AddMinutes(5),
        IsDemo = demo,
        Results = transponders.Select(t => new SessionResultEntry { Transponder = t, DriverName = t }).ToList(),
    };

    [Fact]
    public void The_key_moves_with_any_name_or_flag_and_only_then()
    {
        var plain = MainForm.PrivacyKeyOf(new[] { Sam(), Alex() });

        Assert.Equal(plain, MainForm.PrivacyKeyOf(new[] { Sam(), Alex() }));
        Assert.NotEqual(plain, MainForm.PrivacyKeyOf(new[] { Sam(initials: true), Alex() }));
        Assert.NotEqual(plain, MainForm.PrivacyKeyOf(new[] { Sam(noPhoto: true), Alex() }));
        Assert.NotEqual(plain, MainForm.PrivacyKeyOf(new[] { Sam(noVideo: true), Alex() }));

        var renamed = Sam();
        renamed.AnnouncerName = "Zhock";
        Assert.NotEqual(plain, MainForm.PrivacyKeyOf(new[] { renamed, Alex() }));
    }

    [Fact]
    public void The_owed_list_takes_each_transponder_once_in_the_order_first_owed()
    {
        var merged = MainForm.MergePrivacyOwed(new List<string> { "T1", "t3" }, new[] { "t1", " T2 ", "", "T3" });

        Assert.Equal(new[] { "T1", "t3", "T2" }, merged);
        Assert.Equal(new[] { "T2" }, MainForm.MergePrivacyOwed(null, new[] { "T2" }));
    }

    [Fact]
    public void Only_their_real_sessions_already_on_the_relay_go_again_oldest_first()
    {
        var day = new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc);
        var saved = new[]
        {
            Session("late", day.AddHours(2), false, "T1", "T2"),
            Session("early", day, false, "t1"),
            Session("demo", day.AddHours(1), true, "T1"),
            Session("not-theirs", day.AddHours(1), false, "T2"),
            Session("never-sent", day.AddHours(3), false, "T1"),
        };

        var owed = new HashSet<string>(new[] { "T1" }, StringComparer.OrdinalIgnoreCase);
        var again = MainForm.PrivacySessionsToRepublish(saved, owed, s => s.Id != "never-sent");

        Assert.Equal(new[] { "early", "late" }, again.Select(s => s.Id));
    }

    // Videos_come_down_for_a_flag_turned_on_and_never_without_a_badge_key
    // (PrivacyBadgesToForget) went on 28 Oct 2026 with forget-media: the
    // podium videos stay on the laptop now, so there is nothing online to
    // take down.

    [Fact]
    public void A_flag_change_is_owed_for_every_transponder_the_driver_has()
    {
        var before = PublicPrivacy.From(new[] { Sam(), Alex() });
        var after = PublicPrivacy.From(new[] { Sam(initials: true), Alex() });

        var owed = MainForm.MergePrivacyOwed(new List<string>(), PublicPrivacy.Changed(before, after));

        Assert.Equal(new[] { "T1" }, owed);
    }
}
