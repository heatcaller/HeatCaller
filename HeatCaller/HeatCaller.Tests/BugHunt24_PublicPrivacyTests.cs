namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), PublicPrivacy: an initials-only junior who
/// shares initials with another initials-only driver goes online as
/// "M.K. 2", and that form must be in the junior list the relay is told,
/// or anybody could follow the child by name.
/// </summary>
[Collection(PublicPrivacyCurrentCollection.Name)]
public sealed class BugHunt24_PublicPrivacyTests
{
    private static DriverProfile Driver(string transponder, string name, bool junior = false, bool initials = false) => new()
    {
        Transponder = transponder,
        DriverName = name,
        IsJunior = junior,
        InitialsOnline = initials,
    };

    [Fact]
    public void BugHunt24_Two_initials_only_juniors_with_the_same_initials_are_both_listed_as_juniors()
    {
        var register = PublicPrivacy.From(new[]
        {
            Driver("T1", "Mia Kent", junior: true, initials: true),
            Driver("T2", "Max Kent", junior: true, initials: true),
        });

        var shown = RelayPublisher.DisplayNames(new[] { "Mia Kent", "Max Kent" }, true, null, register);

        // Pin the published forms, so the rest of the test checks what really goes online.
        Assert.Contains("M.K. 2", shown);

        foreach (var name in shown)
        {
            Assert.Contains(register.JuniorPublicNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void BugHunt24_A_junior_numbered_after_an_adult_with_the_same_initials_is_listed_as_a_junior()
    {
        var register = PublicPrivacy.From(new[]
        {
            Driver("T1", "Mark King", initials: true),
            Driver("T2", "Mia Kent", junior: true, initials: true),
        });

        var shown = RelayPublisher.DisplayNames(new[] { "Mark King", "Mia Kent" }, false, null, register);

        Assert.Equal("M.K. 2", shown[1]);
        Assert.Contains(register.JuniorPublicNames, n => string.Equals(n, shown[1], StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BugHunt24_Numbered_initials_run_from_2_and_are_given_only_for_initials_only_juniors()
    {
        var numbered = PublicPrivacy.NumberedInitials("M.K.");
        Assert.Equal("M.K. 2", numbered[0]);
        Assert.Equal($"M.K. {PublicPrivacy.MostInitialsNumber}", numbered[^1]);

        // A junior shown as "Mia K" (not initials only) gets no numbered initials,
        // and an initials-only adult is not a junior at all.
        var register = PublicPrivacy.From(new[]
        {
            Driver("T1", "Mia Kent", junior: true),
            Driver("T2", "Sam Parker", initials: true),
        });

        Assert.DoesNotContain(register.JuniorPublicNames, n => n.StartsWith("M.K.", StringComparison.Ordinal) || n.StartsWith("S.P.", StringComparison.Ordinal));
    }
}
