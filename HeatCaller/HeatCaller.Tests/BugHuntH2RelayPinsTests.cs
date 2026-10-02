namespace HeatCaller.Tests;

/// <summary>
/// Reissue All QR check-ins when the new key cannot be saved, and the
/// sign-up invite hashes that used to be made on the UI thread.
/// </summary>
public sealed class BugHuntH2RelayPinsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Reissue_that_saves_replaces_the_key_and_says_so()
    {
        var settings = new AppSettings { RelayCheckInSecret = new string('a', 64) };

        Assert.True(RelayCheckIn.Reissue(settings));
        Assert.NotEqual(new string('a', 64), settings.RelayCheckInSecret);
        Assert.Equal(64, settings.RelayCheckInSecret.Length);
    }

    [Fact]
    public void Reissue_that_cannot_be_saved_keeps_the_old_key()
    {
        var settings = new AppSettings { RelayCheckInSecret = new string('a', 64) };

        // A folder where settings.json should be: every write to it fails,
        // as a locked or unwritable file would.
        Directory.CreateDirectory(_data.PathOf("settings.json"));

        Assert.False(RelayCheckIn.Reissue(settings));
        Assert.Equal(new string('a', 64), settings.RelayCheckInSecret);
    }

    [Fact]
    public void An_invite_is_found_by_hash_only_for_its_own_club()
    {
        var settings = new AppSettings
        {
            RelayClub = "acme-rc",
            RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
        };

        var store = SignupStore.Load();
        var first = store.Create(settings, "sam@example.org", "Sam");
        var second = store.Create(settings, "alex@example.org", "Alex");

        Assert.NotEqual(first.Code, second.Code);
        Assert.Equal(SignupStore.InviteHash("acme-rc", first.Code, first.Salt), first.Hash);
        Assert.Same(first, store.ByHash(first.Hash, "acme-rc"));
        Assert.Same(second, store.ByHash(second.Hash, "acme-rc"));
        Assert.Null(store.ByHash(first.Hash, "other-club"));
    }
}
