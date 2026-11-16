using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Children's privacy (15 Nov 2026), the laptop's half: the private
/// "privacy" document the relay enforces - every form a junior's name can go
/// online in, and "unpublish results older than N years". None of these
/// touch PublicPrivacy.Current; each builds its own register.
/// </summary>
public sealed class ChildrensPrivacyRelayTests
{
    private static DriverProfile Driver(string transponder, string name, bool junior = false, bool initials = false) => new()
    {
        Transponder = transponder,
        DriverName = name,
        IsJunior = junior,
        InitialsOnline = initials,
    };

    [Fact]
    public void A_name_is_given_in_every_form_shortening_can_show_it()
    {
        Assert.Equal(new[] { "Mia K", "Mia Ke", "Mia Ken", "Mia Kent" }, PublicPrivacy.PublicFormsOf("  Mia   Kent "));
        Assert.Equal(new[] { "Mia" }, PublicPrivacy.PublicFormsOf("Mia"));
        Assert.Empty(PublicPrivacy.PublicFormsOf("  "));
    }

    [Fact]
    public void Only_juniors_names_are_listed_and_their_initials_when_shown_by_initials()
    {
        var register = PublicPrivacy.From(new[]
        {
            Driver("T1", "Mia Kent", junior: true),
            Driver("T2", "Sam Trent"),
            Driver("T3", "Ollie Park", junior: true, initials: true),
        });

        Assert.Contains("Mia K", register.JuniorPublicNames);
        Assert.Contains("Mia Kent", register.JuniorPublicNames);
        Assert.Contains("O.P.", register.JuniorPublicNames);
        Assert.DoesNotContain(register.JuniorPublicNames, n => n.StartsWith("Sam", StringComparison.Ordinal));
    }

    [Fact]
    public void A_removed_junior_still_counts()
    {
        var before = PublicPrivacy.From(new[] { Driver("T1", "Mia Kent", junior: true), Driver("T2", "Sam Trent") });
        var afterDelete = new[] { Driver("T2", "Sam Trent") };
        var retained = PublicPrivacy.Retain(before, afterDelete, null, out _);

        Assert.Contains("Mia K", PublicPrivacy.From(afterDelete, retained).JuniorPublicNames);
    }

    [Fact]
    public void The_document_carries_the_juniors_and_the_years_kept()
    {
        var register = PublicPrivacy.From(new[] { Driver("T1", "Mia Kent", junior: true) });
        using var doc = JsonDocument.Parse(RelayPrivacyDocument.Build(register, 2, new DateTime(2026, 11, 15)));

        Assert.Contains("Mia K", doc.RootElement.GetProperty("juniors").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(2, doc.RootElement.GetProperty("keepYears").GetInt32());
        Assert.Equal(2025, doc.RootElement.GetProperty("fromYear").GetInt32());

        using var keepAll = JsonDocument.Parse(RelayPrivacyDocument.Build(PublicPrivacy.Empty, 0, new DateTime(2026, 11, 15)));
        Assert.Equal(0, keepAll.RootElement.GetProperty("fromYear").GetInt32());
        Assert.Empty(keepAll.RootElement.GetProperty("juniors").EnumerateArray());
    }

    [Fact]
    public void Old_results_are_too_old_only_with_the_setting_on()
    {
        var now = new DateTime(2026, 11, 15);

        Assert.False(RelayPrivacyDocument.TooOld(new DateTime(2001, 1, 1), 0, now), "off by default: every year kept");
        Assert.True(RelayPrivacyDocument.TooOld(new DateTime(2024, 12, 31), 2, now));
        Assert.False(RelayPrivacyDocument.TooOld(new DateTime(2025, 1, 1), 2, now));
        Assert.False(RelayPrivacyDocument.TooOld(DateTime.MinValue, 2, now), "a session with no start is not old");
        Assert.True(RelayPrivacyDocument.YearTooOld("2024", 2, now));
        Assert.False(RelayPrivacyDocument.YearTooOld("2025", 2, now));
        Assert.False(RelayPrivacyDocument.YearTooOld("2024", 0, now));
    }

    [Fact]
    public void The_setting_is_off_by_default()
    {
        Assert.Equal(0, new AppSettings().RelayUnpublishAfterYears);
    }

    [Fact]
    public void The_privacy_channel_has_the_relays_name_for_it()
    {
        var nameOf = typeof(RelayPublisher).GetMethod("NameOf", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal("privacy", nameOf.Invoke(null, new object[] { RelayChannel.Privacy }));
    }
}
