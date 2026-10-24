namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026), approving an online sign-up when signups.json
/// can't be written. The save's result used to be ignored: the phone was
/// told it was in while the approval lived only in memory. Now a failed
/// save puts everything the approval did back and refuses, with nothing
/// sent to the relay.
/// </summary>
public sealed class BugHunt10_RelaySignupDeskTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static AppSettings Settings() => new()
    {
        RelayClub = "acme-rc",

        // Already made, so making a code doesn't make one and save settings.
        RelayPinSalt = new string('5', PinKdf.SaltBytes * 2),
    };

    /// <summary>Runs <paramref name="test"/> with every swap into signups.json failing, as when a sync tool holds it.</summary>
    private static void WithSignupsHeld(Action test)
    {
        var real = SafeFile.ReplaceFile;
        SafeFile.ReplaceFile = (temp, path) =>
        {
            if (string.Equals(Path.GetFileName(path), "signups.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            real(temp, path);
        };

        try
        {
            test();
        }
        finally
        {
            SafeFile.ReplaceFile = real;
        }
    }

    [Fact]
    public void An_approval_that_cannot_be_saved_is_put_back_and_refused()
    {
        var settings = Settings();
        var profiles = ProfileStore.Load();
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(settings, profiles, CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), RentalTransponderStore.Load(), publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("sam@example.org", "Sam") }).Single();
        Assert.True(File.Exists(_data.PathOf("signups.json")));

        var request = new RelaySignupRequest(invite.Hash, "Sam", "Stock", "own", "4401", string.Empty, string.Empty, false, DateTime.UtcNow);
        var profile = new DriverProfile { Transponder = "4401", DriverName = "Sam", ClassName = "Stock" };

        var approved = true;
        var refusal = string.Empty;
        WithSignupsHeld(() => approved = desk.Approve(request, profile, null, false, out refusal));

        Assert.False(approved);
        Assert.Contains("couldn't be saved", refusal, StringComparison.Ordinal);
        Assert.Equal(SignupInviteState.Open, invite.State);
        Assert.Null(invite.AnsweredUtc);
        Assert.Equal(string.Empty, invite.Transponder);
        Assert.Null(profiles.Get("4401"));
        Assert.Null(desk.LastApproval);

        // Once the file lets go, the same approval goes through.
        Assert.True(desk.Approve(request, new DriverProfile { Transponder = "4401", DriverName = "Sam", ClassName = "Stock" }, null, false, out _));
        Assert.Equal(SignupInviteState.Approved, invite.State);
        Assert.NotNull(profiles.Get("4401"));
    }

    [Fact]
    public void A_rental_lent_by_an_approval_that_cannot_be_saved_goes_back_on_the_shelf()
    {
        var settings = Settings();
        var rentals = RentalTransponderStore.Load();
        rentals.Upsert(new RentalTransponder { Transponder = "9001", AssetTag = "R1" });
        using var publisher = new RelayPublisher(new AppSettings { RelayEnabled = false });
        var desk = new RelaySignupDesk(settings, ProfileStore.Load(), CheckInStore.Load(), RaceEventStore.Load(),
            RaceClassStore.Load(), rentals, publisher, _ => { });

        desk.Store.Enabled = true;
        var invite = desk.CreateInvites(new[] { ("ria@example.org", "Ria") }).Single();

        var request = new RelaySignupRequest(invite.Hash, "Ria", "Stock", "rental", string.Empty, string.Empty, string.Empty, false, DateTime.UtcNow);
        var profile = new DriverProfile { Transponder = string.Empty, DriverName = "Ria", ClassName = "Stock" };

        var approved = true;
        WithSignupsHeld(() => approved = desk.Approve(request, profile, "9001", false, out _));

        Assert.False(approved);
        Assert.Equal(SignupInviteState.Open, invite.State);
        Assert.False(rentals.Get("9001")!.IsOut);
        Assert.Equal(string.Empty, rentals.Get("9001")!.CurrentHolderName);
    }
}
