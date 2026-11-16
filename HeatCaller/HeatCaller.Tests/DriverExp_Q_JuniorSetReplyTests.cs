namespace HeatCaller.Tests;

/// <summary>
/// JUNIORS GET A SET REPLY (15 Nov 2026, plan 1.4): the desk can't send a
/// junior's phone its own typed words - a declined request's reason is one
/// of HeatCaller's fixed quick replies, whatever was asked for, and the
/// club's edited replies aren't offered. Enforced in DriverRequestDesk.Decline,
/// below the inbox. Adults are as they were. No ShowDialog is pressed.
/// </summary>
public sealed partial class DriverExp_Q_DriverRequestsTests
{
    private static void MakeJunior(World w, string transponder)
    {
        var profile = w.Profiles.Get(transponder)!;
        profile.IsJunior = true;
        w.Profiles.Upsert(profile);
        Assert.True(w.Profiles.Get(transponder)!.IsJunior);
    }

    [Fact]
    public void JuniorSetReply_typed_words_to_a_junior_become_a_fixed_reply_and_are_kept_on_record()
    {
        var w = new World();
        MakeJunior(w, "100");
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));

        Assert.True(w.Desk.SetRepliesOnly(ask));
        Assert.True(w.Desk.Decline(ask, "Come and see me after, just you"));

        // The phone is told a fixed reply, and the 30-day answered list has it.
        var answered = w.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Declined, answered.State);
        Assert.Equal(DeskInbox.DefaultQuickReplies[0], answered.Said);
        Assert.Equal(DeskInbox.DefaultQuickReplies[0], Assert.Single(w.Store.AnswersFor(SamBadge, DateTime.UtcNow)).Said);
        Assert.Contains(w.Log, l => l.Contains("set reply", StringComparison.Ordinal));
    }

    [Fact]
    public void JuniorSetReply_a_fixed_reply_goes_through_as_HeatCaller_spells_it()
    {
        var w = new World();
        MakeJunior(w, "100");
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));

        Assert.True(w.Desk.Decline(ask, "  not tonight "));
        Assert.Equal("Not tonight", w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void JuniorSetReply_an_adult_still_gets_the_desk_s_own_words()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));

        Assert.False(w.Desk.SetRepliesOnly(ask));
        Assert.True(w.Desk.Decline(ask, "Please pick another name."));
        Assert.Equal("Please pick another name.", w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void JuniorSetReply_a_badge_nobody_here_has_gets_a_set_reply_too()
    {
        var w = new World();
        var ask = w.File(Ask(1, NobodyBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none"));

        Assert.True(w.Desk.SetRepliesOnly(ask));
        Assert.True(w.Desk.Decline(ask, "Who are you?"));
        Assert.Equal(DeskInbox.DefaultQuickReplies[0], w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void JuniorSetReply_the_inbox_offers_a_junior_only_HeatCaller_s_replies_and_can_t_send_the_club_s()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            MakeJunior(w, "100");
            var junior = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));
            var adult = w.File(Ask(2, DevBadge, DriverAsk.Details, Tonight.AddMinutes(1), r => r.Texts["announcer"] = "Devster"));
            var settings = new AppSettings { InboxQuickReplies = new() { "See me in the pits", "Not tonight" } };

            using var inbox = InboxOver(w, settings);
            InboxItem Of(DriverRequest r) => inbox.Items.Single(i => i.Id == r.Id);

            Assert.True(inbox.SetRepliesOnly(new[] { Of(junior) }));
            Assert.False(inbox.SetRepliesOnly(new[] { Of(adult) }));
            Assert.True(inbox.SetRepliesOnly(new[] { Of(junior), Of(adult) }));

            Assert.Equal(DeskInbox.DefaultQuickReplies, inbox.DeclineReplies(setOnly: true));
            Assert.Equal(new[] { "See me in the pits", "Not tonight" }, inbox.DeclineReplies(setOnly: false));

            // The club's own reply, sent anyway: the junior is told a fixed one.
            inbox.Pick(Of(junior));
            inbox.Decline("See me in the pits");
            Assert.Equal(DeskInbox.DefaultQuickReplies[0], w.Store.Get(junior.Id)!.Said);

            // The adult gets the club's words, as before.
            inbox.Pick(Of(adult));
            inbox.Decline("See me in the pits");
            Assert.Equal("See me in the pits", w.Store.Get(adult.Id)!.Said);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's set replies for a junior");
    }
}

/// <summary>
/// The same for sign-ups (15 Nov 2026, plan 1.4): a sign-up from a parent or
/// guardian, or from a driver already here as a junior, can only be turned
/// down with one of RelaySignupDesk.JuniorRejectReplies. Adults unchanged.
/// </summary>
public sealed class JuniorSetReply_SignupTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AppSettings Settings() => new()
    {
        RelayClub = "acme-rc",
        RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
    };

    private static (RelaySignupDesk Desk, SignupInvite Invite, ProfileStore Profiles, RelayPublisher Publisher) Make()
    {
        var profiles = ProfileStore.Load();
        var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(Settings(), profiles, CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("kid@example.org", "Kid") }).Single();
        return (desk, invite, profiles, publisher);
    }

    private static RelaySignupRequest Signup(string hash, bool guardian, string transponder = "") =>
        new(hash, "Alex", "Stock", transponder.Length > 0 ? "own" : "unknown", transponder, string.Empty, string.Empty, guardian, DateTime.UtcNow);

    [Fact]
    public void A_guardian_s_signup_can_t_be_turned_down_with_typed_words()
    {
        var (desk, invite, _, publisher) = Make();
        using var _p = publisher;

        Assert.True(desk.SetRepliesOnly(Signup(invite.Hash, guardian: true)));
        Assert.True(desk.Reject(Signup(invite.Hash, guardian: true), "Message me on Insta and I'll sort it"));

        Assert.Equal(SignupInviteState.Rejected, invite.State);
        Assert.Equal(RelaySignupDesk.DefaultRejectMessage, invite.AnswerMessage);
    }

    [Fact]
    public void A_guardian_s_signup_keeps_a_set_line()
    {
        var (desk, invite, _, publisher) = Make();
        using var _p = publisher;

        Assert.True(desk.Reject(Signup(invite.Hash, guardian: true), "please PICK a different class."));
        Assert.Equal("Please pick a different class.", invite.AnswerMessage);
    }

    [Fact]
    public void A_junior_already_here_gets_a_set_line_even_without_the_guardian_flag()
    {
        var (desk, invite, profiles, publisher) = Make();
        using var _p = publisher;
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "4455667", DriverName = "Alex", IsJunior = true }, out _));

        var request = Signup(invite.Hash, guardian: false, transponder: "4455667");
        Assert.True(desk.SetRepliesOnly(request));
        Assert.True(desk.Reject(request, "Typed words"));
        Assert.Equal(RelaySignupDesk.DefaultRejectMessage, invite.AnswerMessage);
    }

    [Fact]
    public void An_adult_s_signup_still_gets_the_desk_s_own_words()
    {
        var (desk, invite, _, publisher) = Make();
        using var _p = publisher;

        Assert.False(desk.SetRepliesOnly(Signup(invite.Hash, guardian: false)));
        Assert.True(desk.Reject(Signup(invite.Hash, guardian: false), "  Wrong class - try Mod Buggy  "));
        Assert.Equal("Wrong class - try Mod Buggy", invite.AnswerMessage);
    }
}
