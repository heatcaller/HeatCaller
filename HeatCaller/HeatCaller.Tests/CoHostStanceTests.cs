namespace HeatCaller.Tests;

/// <summary>
/// THE CO-HOST'S POINT OF VIEW (13 Oct 2026) - see
/// <see cref="CoHostStance"/>, which shipped on 12 Oct with no tests at
/// all and was recorded as such by the bug hunt the same day.
///
/// WHAT THIS FILE IS REALLY GUARDING
///
/// Not the wording. The wording is a writer's business and will change.
/// What must not change is <see cref="CoHostStance.NeverCruel"/> riding
/// out with every stance that has an edge, and the reason is in that
/// constant's own summary: a broadcast pair can disagree about a car, a
/// line, a strategy or a lap time all night, but they cannot say a driver
/// is not much good, "because this is a small room and that driver is
/// standing in it."
///
/// So the test worth reading first is
/// <see cref="Every_stance_with_an_edge_carries_the_kindness_rule"/>. It
/// is written over ALL of them rather than over the two that exist today,
/// so a stance added next year is covered by it the day it is added
/// rather than the day somebody remembers.
///
/// The second is <see cref="A_key_nobody_shipped_reads_as_the_plain_one"/>
/// - a settings file restored from a later build must give a club the
/// second voice it has always had, not a character nobody in the room
/// chose.
/// </summary>
public sealed class CoHostStanceTests
{
    // ------------------------------------------------------------------
    //  The rule that is not about wording
    // ------------------------------------------------------------------

    [Fact]
    public void Every_stance_with_an_edge_carries_the_kindness_rule()
    {
        foreach (var stance in CoHostStance.All)
        {
            if (stance.Brief.Length == 0)
            {
                continue;
            }

            Assert.Contains(CoHostStance.NeverCruel, CoHostStance.BriefFor(stance.Key));
        }
    }

    [Fact]
    public void There_is_at_least_one_stance_with_an_edge_to_carry_it()
    {
        // Without this the test above passes vacuously the day somebody
        // empties every Brief.
        Assert.Contains(CoHostStance.All, one => one.Brief.Length > 0);
    }

    [Fact]
    public void The_kindness_rule_says_the_thing_it_is_for()
    {
        // Needle the SITUATION, never the PERSON. Reworded freely; the
        // distinction is the point and must survive the rewording.
        Assert.Contains("driver", CoHostStance.NeverCruel);
        Assert.Contains("racing", CoHostStance.NeverCruel);
    }

    // ------------------------------------------------------------------
    //  The plain co-host spends nothing
    // ------------------------------------------------------------------

    [Fact]
    public void The_plain_co_host_puts_nothing_in_the_brief_at_all()
    {
        // A brief is a budget: every sentence in it is a sentence the model
        // reads instead of a fact. "These two have no particular character
        // and are never unkind" buys a club with no stance set nothing -
        // and that includes the kindness rule, because nothing with an
        // edge was asked for.
        Assert.Empty(CoHostStance.BriefFor(null));
        Assert.Empty(CoHostStance.BriefFor(string.Empty));
        Assert.Empty(CoHostStance.BriefFor("   "));
    }

    [Fact]
    public void The_even_handed_stance_is_the_one_with_no_brief()
    {
        Assert.Empty(CoHostStance.EvenHanded.Brief);
        Assert.Empty(CoHostStance.EvenHanded.Key);
    }

    [Fact]
    public void The_plain_one_is_offered_first_because_it_is_what_a_club_already_has()
    {
        Assert.Same(CoHostStance.EvenHanded, CoHostStance.All[0]);
    }

    // ------------------------------------------------------------------
    //  Reading a key off disk
    // ------------------------------------------------------------------

    [Fact]
    public void A_key_nobody_shipped_reads_as_the_plain_one()
    {
        // A settings file from a later build, restored from a backup. The
        // club gets the second voice it has always had rather than a
        // character nobody in the room chose - and NOT an exception, which
        // on this path is the announcer not starting.
        Assert.Same(CoHostStance.EvenHanded, CoHostStance.For("a-stance-from-2027"));
        Assert.Same(CoHostStance.EvenHanded, CoHostStance.For(null));
        Assert.Same(CoHostStance.EvenHanded, CoHostStance.For("   "));
    }

    [Fact]
    public void A_key_is_read_whatever_case_and_spacing_it_was_written_in()
    {
        var real = CoHostStance.All.First(one => one.Key.Length > 0);

        Assert.Same(real, CoHostStance.For(real.Key));
        Assert.Same(real, CoHostStance.For(real.Key.ToUpperInvariant()));
        Assert.Same(real, CoHostStance.For("  " + real.Key + "  "));
    }

    [Fact]
    public void Every_shipped_stance_can_be_read_back_by_its_own_key()
    {
        // The round trip the Commentary page depends on: it saves a key and
        // reads it back on the next start.
        foreach (var stance in CoHostStance.All.Where(one => one.Key.Length > 0))
        {
            Assert.Same(stance, CoHostStance.For(stance.Key));
        }
    }

    // ------------------------------------------------------------------
    //  The catalogue itself
    // ------------------------------------------------------------------

    [Fact]
    public void No_two_stances_share_a_key()
    {
        // A duplicate would make For() return whichever came first, and the
        // club's picker would quietly set the other one.
        var keys = CoHostStance.All.Select(one => one.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Only_the_plain_one_has_an_empty_key()
    {
        // An empty key is how "nothing set" is stored, so a second stance
        // carrying one would be unreachable - For() skips empty keys.
        Assert.Single(CoHostStance.All, one => one.Key.Length == 0);
    }

    [Fact]
    public void Every_stance_has_something_for_a_club_to_read()
    {
        foreach (var stance in CoHostStance.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(stance.Name), "a stance with no name cannot be picked");
            Assert.False(string.IsNullOrWhiteSpace(stance.Blurb), "a stance with no blurb explains nothing");
        }
    }

    [Fact]
    public void A_brief_is_short_enough_not_to_crowd_out_the_facts()
    {
        // The record's own note: "one or two sentences; any longer and it
        // crowds out the facts." A brief is read instead of a fact, so this
        // is a ceiling on what the stance costs the night.
        foreach (var stance in CoHostStance.All)
        {
            Assert.True(
                stance.Brief.Length <= 400,
                $"\"{stance.Name}\" briefs the model with {stance.Brief.Length} characters");
        }
    }

    // ------------------------------------------------------------------
    //  What the Commentary page shows
    // ------------------------------------------------------------------

    [Fact]
    public void Every_stance_describes_its_consequence_rather_than_its_label()
    {
        foreach (var stance in CoHostStance.All)
        {
            var said = CoHostStance.Describe(stance.Key);

            Assert.False(string.IsNullOrWhiteSpace(said));

            // The plain one says what the show has always sounded like; the
            // rest name themselves and then what they do.
            if (stance.Brief.Length > 0)
            {
                Assert.Contains(stance.Name, said);
            }
        }
    }

    [Fact]
    public void An_unknown_key_is_described_as_the_plain_one()
    {
        Assert.Equal(CoHostStance.Describe(null), CoHostStance.Describe("a-stance-from-2027"));
    }
}
