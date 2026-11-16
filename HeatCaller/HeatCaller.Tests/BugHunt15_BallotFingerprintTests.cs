namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B17: the public vote ballot is only re-sent
/// when its fingerprint changes, and the fingerprint did not cover the names
/// on it. A driver ticked "initials only online" (or renamed) while a vote
/// was open stayed on the relay's ballot under the old full name.
/// </summary>
[Collection(PublicPrivacyCurrentCollection.Name)]
public sealed class BugHunt15_BallotFingerprintTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private static string Fingerprint(VoteOption[] grid) =>
        RelayPublisher.BallotFingerprint(
            enabled: true,
            open: true,
            raceKey: "round-1",
            race: "A-Main",
            ballot: "poll-1",
            key: Key,
            options: grid,
            abbreviate: false,
            drivers: RelayPublisher.RelayBallotDrivers(grid, "poll-1", Key));

    [Fact]
    public void Initials_only_ticked_mid_vote_changes_the_ballot_fingerprint()
    {
        var grid = new[] { new VoteOption("BH15-T1", "Sam Parker"), new VoteOption("BH15-T2", "Ann Lee") };
        var was = PublicPrivacy.Current;

        try
        {
            PublicPrivacy.Current = PublicPrivacy.From(new[]
            {
                new DriverProfile { Transponder = "BH15-T1", DriverName = "Sam Parker" },
            });

            var before = Fingerprint(grid);
            Assert.Equal(before, Fingerprint(grid));

            PublicPrivacy.Current = PublicPrivacy.From(new[]
            {
                new DriverProfile { Transponder = "BH15-T1", DriverName = "Sam Parker", InitialsOnline = true },
            });

            var after = Fingerprint(grid);

            Assert.NotEqual(before, after);
            Assert.Contains("S.P.", after, StringComparison.Ordinal);
            Assert.DoesNotContain("Sam Parker", after, StringComparison.Ordinal);
        }
        finally
        {
            PublicPrivacy.Current = was;
        }
    }

    [Fact]
    public void A_driver_renamed_mid_vote_changes_the_ballot_fingerprint()
    {
        var before = Fingerprint(new[] { new VoteOption("BH15-T3", "Sam Parker") });
        var after = Fingerprint(new[] { new VoteOption("BH15-T3", "Samuel Parker") });

        Assert.NotEqual(before, after);
    }
}
