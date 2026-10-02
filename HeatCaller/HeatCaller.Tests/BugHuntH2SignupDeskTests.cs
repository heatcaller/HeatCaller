namespace HeatCaller.Tests;

/// <summary>
/// Undoing an answer to an online sign-up.
///
/// Once the desk has answered a sign-up (approved, turned down, or turned
/// down by cancelling its invite), the relay keeps that answer against the
/// invite's code and refuses every new sign-up made with it. Undo used to
/// reopen the invite under the same code and tell the desk it worked again,
/// when the relay would never take it. Now the reopened invite has a new
/// code, so the next signup document carries a hash the relay has no
/// answer for.
/// </summary>
public sealed class BugHuntH2SignupDeskTests : IDisposable
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
    public void New_code_replaces_the_code_and_hash_and_marks_it_not_emailed()
    {
        var settings = Settings();
        var store = SignupStore.Load();
        var invite = store.Create(settings, "sam@example.org", "Sam");
        invite.EmailedUtc = DateTime.UtcNow;

        var id = invite.Id;
        var oldCode = invite.Code;
        var oldHash = invite.Hash;

        store.NewCode(invite, settings);

        Assert.Equal(id, invite.Id);
        Assert.Equal(SignupStore.CodeLength, invite.Code.Length);
        Assert.NotEqual(oldCode, invite.Code);
        Assert.NotEqual(oldHash, invite.Hash);
        Assert.Null(invite.EmailedUtc);
        Assert.Same(invite, store.ByHash(invite.Hash, "acme-rc"));
        Assert.Null(store.ByHash(oldHash, "acme-rc"));
    }

    [Fact]
    public void Undoing_a_turn_down_reopens_the_invite_under_a_new_code()
    {
        var settings = Settings();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        var oldHash = invite.Hash;

        var request = new RelaySignupRequest(oldHash, "Sam", "Stock", "unknown", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        desk.Reject(request, "Wrong class");
        Assert.Equal(SignupInviteState.Rejected, invite.State);

        desk.UndoReject(invite.Id, "Sam");

        Assert.Equal(SignupInviteState.Open, invite.State);
        Assert.NotEqual(oldHash, invite.Hash);

        var document = desk.Store.Document(settings, string.Empty, Array.Empty<string>());
        Assert.Contains(invite.Hash, document);
        Assert.DoesNotContain(oldHash, document);
    }
}

