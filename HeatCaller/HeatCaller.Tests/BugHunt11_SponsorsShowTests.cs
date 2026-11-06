using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11, the sponsors on the relay (5 Nov 2026): a sponsors.json held
/// by a backup tool, sync tool or virus scanner at launch loads an empty store
/// that refuses to save. The relay refresh still published that empty list,
/// so the relay's good sponsors snapshot became {"sponsors":[]} and every
/// phone's Live page lost the club's sponsor cards for the night. The relay
/// now gets nothing from a store that was never read, as with the badges.
/// </summary>
public sealed class BugHunt11_SponsorsShowTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Sponsors_locked_at_startup_are_not_published_to_the_relay_as_an_empty_list()
    {
        var club = SponsorStore.Load();
        club.AddSponsor("Hobby Hut");
        var before = File.ReadAllText(_data.PathOf("sponsors.json"));

        SponsorStore tonight;
        using (new FileStream(_data.PathOf("sponsors.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = SponsorStore.Load();
        }

        Assert.True(tonight.LoadedUnreadable);
        Assert.Empty(tonight.Sponsors);
        Assert.False(RelaySponsors.Publishable(tonight));

        // And the file itself is still the club's.
        Assert.Equal(before, File.ReadAllText(_data.PathOf("sponsors.json")));
    }

    [Fact]
    public void Sponsors_read_normally_are_still_published()
    {
        var club = SponsorStore.Load();
        club.AddSponsor("Hobby Hut");

        var tonight = SponsorStore.Load();

        Assert.False(tonight.LoadedUnreadable);
        Assert.True(RelaySponsors.Publishable(tonight));

        var doc = JsonNode.Parse(RelaySponsors.Document(tonight.Sponsors, _ => null))!;
        Assert.Equal("Hobby Hut", doc["sponsors"]!.AsArray().Single()!["name"]!.GetValue<string>());
    }

    [Fact]
    public void A_fresh_install_with_no_sponsors_file_is_published()
    {
        var store = SponsorStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.True(RelaySponsors.Publishable(store));
    }
}
