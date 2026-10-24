namespace HeatCaller.Tests;

/// <summary>Driver experience P0 (22 Oct 2026): PublicPrivacy - the online privacy register.</summary>
public sealed class DriverExp_P0_PublicPrivacyTests
{
    private static DriverProfile Sam(bool initials = true, bool noPhoto = false, bool noVideo = false) => new()
    {
        Transponder = "T1",
        DriverName = "Sam Parker",
        AnnouncerName = "Zhock",
        InitialsOnline = initials,
        NoPhotoOnline = noPhoto,
        NoVideoOnline = noVideo,
    };

    private static DriverProfile Alex() => new() { Transponder = "T2", DriverName = "Alex Jones" };

    [Theory]
    [InlineData("Sam Parker", "S.P.")]
    [InlineData("  sam   parker ", "S.P.")]
    [InlineData("Mary-Jane \"MJ\" Watson", "M.M.W.")]
    [InlineData("Sam", "S.")]
    [InlineData("", "")]
    public void Initials_take_the_first_letter_of_each_word(string name, string expected)
    {
        Assert.Equal(expected, PublicPrivacy.Initials(name));
    }

    [Fact]
    public void Flags_are_read_by_transponder_and_name()
    {
        var register = PublicPrivacy.From(new[] { Sam(noPhoto: true), Alex() });

        Assert.True(register.InitialsFor("Sam Parker"));
        Assert.True(register.InitialsFor("SAM  PARKER"));
        Assert.True(register.InitialsFor("Zhock"));
        Assert.False(register.InitialsFor("Alex Jones"));
        Assert.False(register.InitialsFor(null));

        Assert.True(register.InitialsForTransponder("t1"));
        Assert.False(register.InitialsForTransponder("T2"));
        Assert.True(register.NoPhoto("T1"));
        Assert.False(register.NoVideo("T1"));
        Assert.False(register.NoPhoto("nobody"));
        Assert.True(register.AnyInitials);
    }

    [Fact]
    public void A_public_name_is_initials_only_when_asked_matching_a_renamed_row_by_transponder()
    {
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });

        Assert.Equal("S.P.", register.PublicName("Sam Parker"));
        Assert.Equal("A.J.", PublicPrivacy.Initials("Alex Jones"));
        Assert.Equal("Alex Jones", register.PublicName("Alex Jones", "T2"));

        // An old row carrying the name from before a rename still matches by transponder.
        Assert.Equal("S.P.", register.PublicName("Samantha Parker", "T1"));
    }

    [Fact]
    public void Redact_replaces_full_and_announcer_names_only()
    {
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });

        Assert.Equal(
            "S.P. takes the lead from Alex Jones! Go S.P.! S.P. again.",
            register.Redact("Sam Parker takes the lead from Alex Jones! Go Zhock! SAM PARKER again."));

        // A bare first name is not replaced (best effort - see the class).
        Assert.Equal("Sam is quick", register.Redact("Sam is quick"));

        // Not inside another word.
        Assert.Equal("Zhockingly good", register.Redact("Zhockingly good"));
    }

    [Fact]
    public void Mentions_withheld_errs_on_the_private_side()
    {
        var register = PublicPrivacy.From(new[] { Sam(), Alex() });

        Assert.True(register.MentionsWithheld("Great lap from Sam"));
        Assert.True(register.MentionsWithheld("Parker on the inside"));
        Assert.True(register.MentionsWithheld("Zhock wins"));
        Assert.False(register.MentionsWithheld("Alex Jones wins"));
        Assert.False(register.MentionsWithheld("Samuel wins"));

        // An email or a path in a line is not a name.
        Assert.False(register.MentionsWithheld(@"Send to a@b.com from C:\Users\someone\x"));
    }

    [Fact]
    public void Nobody_flagged_changes_nothing()
    {
        var register = PublicPrivacy.From(new[] { Sam(initials: false), Alex() });

        Assert.False(register.AnyInitials);
        Assert.Equal("Sam Parker wins", register.Redact("Sam Parker wins"));
        Assert.False(register.MentionsWithheld("Sam Parker wins"));
        Assert.Equal("Sam Parker", register.PublicName("Sam Parker", "T1"));
    }

    [Fact]
    public void Ghosts_are_never_anybody()
    {
        var ghost = new DriverProfile { Transponder = Ghosts.Prefix + "1", DriverName = "Ghost Rider", InitialsOnline = true };
        var register = PublicPrivacy.From(new[] { ghost });

        Assert.False(register.InitialsFor("Ghost Rider"));
    }

    [Fact]
    public void Current_starts_empty_and_is_swapped_whole()
    {
        var was = PublicPrivacy.Current;

        try
        {
            var register = PublicPrivacy.From(new[] { Sam() });
            PublicPrivacy.Current = register;
            Assert.Same(register, PublicPrivacy.Current);
            Assert.False(PublicPrivacy.Empty.AnyInitials);
        }
        finally
        {
            PublicPrivacy.Current = was;
        }
    }

    [Fact]
    public void Fingerprint_and_changed_see_flags_going_on_and_off()
    {
        var before = PublicPrivacy.From(new[] { Sam(initials: false), Alex() });
        var same = PublicPrivacy.From(new[] { Alex(), Sam(initials: false) });
        var after = PublicPrivacy.From(new[] { Sam(initials: false, noVideo: true), Alex() });

        Assert.Equal(before.Fingerprint, same.Fingerprint);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);

        Assert.Empty(PublicPrivacy.Changed(before, same));
        Assert.Equal(new[] { "T1" }, PublicPrivacy.Changed(before, after));
        Assert.Equal(new[] { "T1" }, PublicPrivacy.Changed(after, before));

        // A driver with no flags renamed changes nothing published.
        var renamed = Alex();
        renamed.DriverName = "Alexander Jones";
        Assert.Empty(PublicPrivacy.Changed(before, PublicPrivacy.From(new[] { Sam(initials: false), renamed })));

        // A flagged driver renamed does.
        var samRenamed = Sam();
        samRenamed.DriverName = "Samantha Parker";
        Assert.Equal(new[] { "T1" }, PublicPrivacy.Changed(PublicPrivacy.From(new[] { Sam() }), PublicPrivacy.From(new[] { samRenamed })));
    }
}
