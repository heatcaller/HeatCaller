namespace HeatCaller.Tests;

/// <summary>
/// WHETHER THE CLUB MAY SPEAK IN A CLONED VOICE (12 Oct 2026) - see
/// <see cref="VoiceConsent"/>.
///
/// WHAT THIS FILE IS ABOUT
///
/// One question asked seven ways, and the answer has to be wrong in only
/// one direction. A cloned voice is a named person's likeness, so every
/// answer that is not an unambiguous yes has to be a no - and a no is not
/// a silence, it is the free stock voice reading the line and the console
/// saying which refusal it was.
///
/// So most of this file is the NOs. The one worth reading first is
/// <see cref="A_consent_for_one_voice_does_not_cover_another"/>: a club
/// that clones a second announcer, or switches which one it uses, must not
/// find the first person's agreement standing in for the second's. That is
/// the failure a reader would not think of, and the reason
/// <see cref="VoiceConsent.Record.VoiceId"/> exists.
/// </summary>
public sealed class VoiceConsentTests
{
    private static readonly DateTime Agreed = new(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc);

    private const string Clone = "acme-dave";

    private static VoiceConsent.Record Consent(
        string voiceId = Clone,
        string who = "Dave Bellamy",
        DateTime? revoked = null) =>
        new(voiceId, who, Agreed, revoked);

    // ------------------------------------------------------------------
    //  The yeses
    // ------------------------------------------------------------------

    [Fact]
    public void A_cloned_voice_with_a_named_consent_may_speak()
    {
        var verdict = VoiceConsent.MaySpeakWith(Clone, cloned: true, Consent());

        Assert.Equal(VoiceConsent.Verdict.MaySpeak, verdict);
        Assert.True(VoiceConsent.Allows(verdict));
    }

    /// <summary>
    /// The stock voices are nobody's likeness, which is the whole reason a
    /// catalogue of them exists. This is not a loophole - and note the
    /// flag comes from the RELAY, so an app running an old build cannot
    /// answer "not a clone" about one.
    /// </summary>
    [Fact]
    public void A_stock_voice_needs_no_consent_at_all()
    {
        var verdict = VoiceConsent.MaySpeakWith("clubhouse", cloned: false, consent: null);

        Assert.Equal(VoiceConsent.Verdict.NotACloneAtAll, verdict);
        Assert.True(VoiceConsent.Allows(verdict));
    }

    // ------------------------------------------------------------------
    //  The noes, which are the point
    // ------------------------------------------------------------------

    [Fact]
    public void A_cloned_voice_with_nothing_recorded_does_not_speak()
    {
        var verdict = VoiceConsent.MaySpeakWith(Clone, cloned: true, consent: null);

        Assert.Equal(VoiceConsent.Verdict.NothingRecorded, verdict);
        Assert.False(VoiceConsent.Allows(verdict));
    }

    /// <summary>THE ONE. See the class summary.</summary>
    [Fact]
    public void A_consent_for_one_voice_does_not_cover_another()
    {
        // The club cloned Dave in June, and has now cloned Sam as well -
        // or has simply switched which voice is set.
        var verdict = VoiceConsent.MaySpeakWith("acme-sam", cloned: true, Consent(voiceId: Clone));

        Assert.Equal(VoiceConsent.Verdict.ForAnotherVoice, verdict);
        Assert.False(VoiceConsent.Allows(verdict));
    }

    [Fact]
    public void A_consent_naming_nobody_is_not_a_consent()
    {
        foreach (var nobody in new[] { "", "   ", "\t" })
        {
            var verdict = VoiceConsent.MaySpeakWith(Clone, cloned: true, Consent(who: nobody));

            Assert.Equal(VoiceConsent.Verdict.NobodyNamed, verdict);
        }
    }

    [Fact]
    public void A_consent_taken_back_stops_the_voice()
    {
        var verdict = VoiceConsent.MaySpeakWith(Clone, cloned: true, Consent(revoked: Agreed.AddMonths(4)));

        Assert.Equal(VoiceConsent.Verdict.TakenBack, verdict);
        Assert.False(VoiceConsent.Allows(verdict));
    }

    [Fact]
    public void No_voice_at_all_is_nothing_to_ask_about()
    {
        foreach (var nothing in new string?[] { null, "", "   " })
        {
            Assert.Equal(VoiceConsent.Verdict.NoVoiceChosen, VoiceConsent.MaySpeakWith(nothing, cloned: true, Consent()));
        }
    }

    [Fact]
    public void The_voice_is_matched_without_regard_to_case_or_spacing()
    {
        Assert.Equal(
            VoiceConsent.Verdict.MaySpeak,
            VoiceConsent.MaySpeakWith("  ACME-DAVE  ", cloned: true, Consent(voiceId: "acme-dave")));
    }

    /// <summary>
    /// The order of the checks is itself the rule, so it is pinned: a
    /// consent for ANOTHER voice is reported as that rather than as a
    /// missing name or a revocation, because it tells us nothing about
    /// this voice either way.
    /// </summary>
    [Fact]
    public void A_consent_for_another_voice_is_reported_as_that_and_not_as_something_else()
    {
        var wrongVoiceAndNoName = new VoiceConsent.Record("acme-sam", string.Empty, Agreed, Agreed);

        Assert.Equal(
            VoiceConsent.Verdict.ForAnotherVoice,
            VoiceConsent.MaySpeakWith(Clone, cloned: true, wrongVoiceAndNoName));
    }

    // ------------------------------------------------------------------
    //  Taking it back
    // ------------------------------------------------------------------

    [Fact]
    public void Taking_it_back_keeps_everything_about_the_original()
    {
        var took = VoiceConsent.TakeBack(Consent(), Agreed.AddMonths(4));

        Assert.Equal(Clone, took.VoiceId);
        Assert.Equal("Dave Bellamy", took.Who);
        Assert.Equal(Agreed, took.AtUtc);
        Assert.Equal(Agreed.AddMonths(4), took.RevokedAtUtc);
    }

    /// <summary>
    /// A second press of a button is not a second revocation: the day it
    /// stopped being allowed is the first one.
    /// </summary>
    [Fact]
    public void Taking_back_something_already_taken_back_keeps_the_first_date()
    {
        var once = VoiceConsent.TakeBack(Consent(), Agreed.AddMonths(4));
        var twice = VoiceConsent.TakeBack(once, Agreed.AddMonths(9));

        Assert.Equal(Agreed.AddMonths(4), twice.RevokedAtUtc);
    }

    /// <summary>
    /// A revoked consent is not the same as one that never existed. A club
    /// asked whether it ever had permission for something it posted in
    /// March needs to be able to answer yes, until June - so the row is
    /// kept rather than deleted.
    /// </summary>
    [Fact]
    public void A_revoked_consent_still_records_that_it_once_existed()
    {
        var took = VoiceConsent.TakeBack(Consent(), Agreed.AddMonths(4));

        Assert.Equal("Dave Bellamy", took.Who);
        Assert.NotEqual(default, took.AtUtc);
    }

    // ------------------------------------------------------------------
    //  What the club is told
    // ------------------------------------------------------------------

    [Fact]
    public void Every_refusal_names_its_fix()
    {
        foreach (var verdict in Enum.GetValues<VoiceConsent.Verdict>())
        {
            var said = VoiceConsent.Because(verdict);

            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("does not have a word for", said);
        }

        // The one a club will actually hit, and the only one whose fix is
        // something they have to go and do.
        Assert.Contains("Voice Settings", VoiceConsent.Because(VoiceConsent.Verdict.NothingRecorded));
    }

    [Fact]
    public void Nothing_recorded_says_so_rather_than_saying_nothing()
    {
        var said = VoiceConsent.Describe(null);

        Assert.Contains("No consent is recorded", said);
        Assert.Contains("will not be used", said);
    }

    [Fact]
    public void A_held_consent_says_who_and_when_and_what_it_does_not_hold()
    {
        var said = VoiceConsent.Describe(Consent());

        Assert.Contains("Dave Bellamy", said);
        Assert.Contains("2026", said);

        // The club keeps the recording. HeatCaller holding one would be
        // the thing this whole design avoids.
        Assert.Contains("the club's to keep", said);
    }

    [Fact]
    public void A_taken_back_consent_says_both_dates()
    {
        var said = VoiceConsent.Describe(VoiceConsent.TakeBack(Consent(), Agreed.AddMonths(4)));

        Assert.Contains("Dave Bellamy", said);
        Assert.Contains("taken back", said);
        Assert.Contains("Nothing is spoken in that voice", said);
    }

    [Fact]
    public void A_consent_naming_nobody_is_described_as_that()
    {
        var said = VoiceConsent.Describe(Consent(who: "   "));

        Assert.Contains("somebody not named", said);
    }
}
