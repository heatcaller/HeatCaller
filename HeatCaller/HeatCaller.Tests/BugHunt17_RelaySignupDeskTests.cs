namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: undoing an answer reopened the invite under a new code but
/// left its expiry alone. A sign-up can be answered after its invite's two
/// weeks have run out (the relay holds it for two weeks of its own), so the
/// invite came back already expired - never published, Email Again greyed
/// out - while the console said to send the new code.
/// </summary>
public sealed class BugHunt17_RelaySignupDeskTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AppSettings Settings() => new()
    {
        RelayClub = "acme-rc",

        // Already made, so making a code doesn't make one and save settings.
        RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
    };

    [Fact]
    public void Undoing_an_answer_given_after_the_invite_expired_reopens_it_usable()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();

        // Signed up on its last days, answered two days after it ran out.
        invite.ExpiresUtc = DateTime.UtcNow.AddDays(-2);

        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        desk.Reject(request, "Wrong class");
        Assert.Equal(SignupInviteState.Rejected, invite.State);

        desk.UndoReject(invite.Id, "Sam");

        Assert.True(invite.IsUsable);
        Assert.True(invite.ExpiresUtc > DateTime.UtcNow + SignupStore.InviteLifetime - TimeSpan.FromMinutes(5));
        Assert.Contains(invite.Hash, desk.Store.Document(settings, string.Empty, Array.Empty<string>()));
    }

    [Fact]
    public void An_undo_never_shortens_an_invite_s_time()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        var later = DateTime.UtcNow.AddDays(40);
        invite.ExpiresUtc = later;

        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        desk.Reject(request, "Wrong class");
        desk.UndoReject(invite.Id, "Sam");

        Assert.Equal(later, invite.ExpiresUtc);
    }
}
