namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the privacy register: a second flagged driver
/// deleted after being handed a kept driver's transponder is kept too, and a
/// main transponder is its owner's even when an older file lists it among
/// somebody else's extras.
/// </summary>
public sealed class BugHunt17_PublicPrivacyTests
{
    private static DriverProfile Driver(string transponder, string name, bool initials = false, bool noPhoto = false, string? extra = null)
    {
        var profile = new DriverProfile
        {
            Transponder = transponder,
            DriverName = name,
            InitialsOnline = initials,
            NoPhotoOnline = noPhoto,
        };

        if (extra is not null)
        {
            profile.AlternateTransponder = extra;
        }

        return profile;
    }

    [Fact]
    public void A_second_flagged_driver_deleted_on_a_kept_drivers_transponder_is_kept_too()
    {
        // Sam (photo off) is deleted and kept.
        var withSam = PublicPrivacy.From(new[] { Driver("T1", "Sam Parker", noPhoto: true) });
        var kept = PublicPrivacy.Retain(withSam, Array.Empty<DriverProfile>(), null, out _);

        // T1 goes to Bob, shown by initials - and then Bob is deleted as well.
        var bob = new[] { Driver("T1", "Bob Kid", initials: true) };
        var before = PublicPrivacy.From(bob, kept);
        Assert.True(before.InitialsForTransponder("T1"));

        var retained = PublicPrivacy.Retain(before, Array.Empty<DriverProfile>(), kept, out var changed);

        Assert.True(changed);
        Assert.Equal(new[] { "Sam Parker", "Bob Kid" }, retained.Select(r => r.Name).ToArray());

        var after = PublicPrivacy.From(Array.Empty<DriverProfile>(), retained);

        // Still Bob's, by name and by transponder, so nothing is republished.
        Assert.True(after.InitialsFor("Bob Kid"));
        Assert.True(after.InitialsForTransponder("T1"));
        Assert.Equal("B.K.", after.PublicName("Bob Kid", "T1"));
        Assert.Empty(PublicPrivacy.Changed(before, after));

        // Nothing moves on the next pass.
        Assert.Same(retained, PublicPrivacy.Retain(after, Array.Empty<DriverProfile>(), retained, out var again));
        Assert.False(again);
    }

    [Fact]
    public void A_main_transponder_is_its_owners_even_when_another_driver_lists_it_as_an_extra()
    {
        // An older file's clash: X is Zoe's main and one of Aaron's extras.
        // Aaron comes first, as ProfileStore.All gives them (by name).
        var privacy = PublicPrivacy.From(new[]
        {
            Driver("A1", "Aaron Able", extra: "X"),
            Driver("X", "Zoe Young", initials: true, noPhoto: true),
        });

        Assert.True(privacy.NoPhoto("X"));
        Assert.True(privacy.InitialsForTransponder("X"));
        Assert.False(privacy.NoPhoto("A1"));
    }

    [Fact]
    public void An_extra_transponder_nobody_else_holds_is_still_its_drivers()
    {
        var privacy = PublicPrivacy.From(new[] { Driver("A1", "Aaron Able", noPhoto: true, extra: "A2") });

        Assert.True(privacy.NoPhoto("A1"));
        Assert.True(privacy.NoPhoto("A2"));
    }
}
