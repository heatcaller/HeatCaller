using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A driver's own night, as the document the relay holds under their badge
/// id.
///
/// WHAT IS WORTH TESTING HERE is narrower than it looks, and deliberately.
/// DriverNight writes no sentences: the lines in a night are
/// DriverCut.FactsFor's own, split where FactPack put its line breaks and
/// not touched otherwise, and FactsFor has its own tests. So what is tested
/// is the three things this file actually decides - which ids may key a
/// night, what the document's shape is, and when there is no night to send -
/// plus the one promise the whole feature rests on: that a driver can check
/// every word, because nothing between the facts and the page rewrote any
/// of them.
///
/// The two to read first are
/// `An_id_of_the_wrong_shape_is_not_a_night_to_publish` - because a
/// document under one would sit in a club's collection forever, unreadable
/// by anybody, rolling somebody else's night off the end when it filled -
/// and `Every_line_is_the_facts_own_words`.
/// </summary>
public sealed class DriverNightTests
{
    /// <summary>A badge id as RelayCheckIn.IdFor makes them: twenty lowercase hex characters.</summary>
    private const string Bill = "a1b2c3d4e5f60718293a";

    private static readonly DateTime Night = new(2026, 10, 10, 19, 42, 0, DateTimeKind.Local);
    private static readonly DateTime At = new(2026, 10, 10, 21, 30, 0, DateTimeKind.Utc);

    // --- Which ids may key a night -----------------------------------------

    [Fact]
    public void A_badge_id_is_twenty_lowercase_hex_characters()
    {
        Assert.True(DriverNight.IsADriverId(Bill));
        Assert.True(DriverNight.IsADriverId("00000000000000000000"));
        Assert.True(DriverNight.IsADriverId("ffffffffffffffffffff"));
    }

    [Fact]
    public void An_id_of_the_wrong_shape_is_not_a_night_to_publish()
    {
        // The relay validates a badge id on the way OUT - it is what tells
        // a real id from a guess - and stores whatever it is given on the
        // way in. So a document under one of these would sit in the club's
        // collection, count against its budget, roll somebody else's night
        // off the end when it filled, and never be readable by anybody.
        Assert.False(DriverNight.IsADriverId(null));
        Assert.False(DriverNight.IsADriverId(string.Empty));
        Assert.False(DriverNight.IsADriverId("a1b2c3d4e5f60718293"));      // nineteen
        Assert.False(DriverNight.IsADriverId("a1b2c3d4e5f60718293ab"));    // twenty-one
        Assert.False(DriverNight.IsADriverId("A1B2C3D4E5F60718293A"));     // upper case
        Assert.False(DriverNight.IsADriverId("a1b2c3d4e5f60718293g"));     // not hex
        Assert.False(DriverNight.IsADriverId("a1b2c3d4e5f6071829 a"));     // a space
        Assert.False(DriverNight.IsADriverId("../../etc/passwd0000"));
    }

    [Fact]
    public void A_transponder_is_never_a_badge_id()
    {
        // The rule the whole feature is built around: the transponder does
        // not leave the laptop. A document keyed by one would put a number
        // that follows somebody from meeting to meeting onto a relay, next
        // to their name.
        Assert.False(DriverNight.IsADriverId("3012345"));
        Assert.False(DriverNight.IsADriverId("TRANS-3012345"));
    }

    // --- The document -------------------------------------------------------

    [Fact]
    public void A_night_carries_the_drivers_name_their_lines_and_when_it_was()
    {
        var night = Read(DriverNight.Document(
            "Bill Hargrove", Night, "Club Championship Round 4", Facts(
                "Bill Hargrove raced Heat 2 and finished sixth of nineteen, up from ninth on the grid.",
                "Their best lap of it was 8.412s, on lap 9."),
            At));

        Assert.Equal("Bill Hargrove", night.GetProperty("driver").GetString());
        Assert.Equal("Club Championship Round 4", night.GetProperty("title").GetString());
        Assert.Equal("2026-10-10", night.GetProperty("night").GetString());
        Assert.Equal(2, night.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public void Every_line_is_the_facts_own_words()
    {
        // The promise the feature rests on. A driver was standing there, so
        // a document that paraphrased would be wrong in front of the one
        // reader in the world who knows exactly what happened.
        const string said = "Bill Hargrove raced Heat 2 and finished sixth of nineteen, up from ninth on the grid.";

        var night = Read(DriverNight.Document("Bill Hargrove", Night, null, Facts(said), At));

        Assert.Equal(said, night.GetProperty("lines")[0].GetString());
    }

    [Fact]
    public void The_date_a_machine_reads_is_not_the_one_a_person_reads()
    {
        // `night` is invariant yyyy-MM-dd, so nothing downstream has to
        // parse the written line; `when` is the club's own culture, because
        // it is the one field here written to be read by somebody.
        var night = Read(DriverNight.Document("Bill Hargrove", Night, null, Facts("Raced."), At));

        var when = night.GetProperty("when").GetString();

        Assert.Equal("2026-10-10", night.GetProperty("night").GetString());
        Assert.False(string.IsNullOrWhiteSpace(when));

        // Not the same string, and not asserted any harder than that: the
        // written line is in the club's own culture, and a test that
        // expected an English weekday would fail on a Spanish laptop.
        Assert.NotEqual("2026-10-10", when);
    }

    [Fact]
    public void When_it_was_built_goes_with_it()
    {
        var night = Read(DriverNight.Document("Bill Hargrove", Night, null, Facts("Raced."), At));

        Assert.Equal(At, DateTime.Parse(
            night.GetProperty("at").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void A_race_day_the_club_never_named_leaves_the_title_empty()
    {
        // Rather than inventing one. "Race Day" would be the app telling a
        // driver what their club calls its own evening.
        var night = Read(DriverNight.Document("Bill Hargrove", Night, "   ", Facts("Raced."), At));

        Assert.Equal(string.Empty, night.GetProperty("title").GetString());
    }

    [Fact]
    public void The_name_is_trimmed_and_otherwise_left_alone()
    {
        var night = Read(DriverNight.Document("  Bill Hargrove  ", Night, null, Facts("Raced."), At));

        Assert.Equal("Bill Hargrove", night.GetProperty("driver").GetString());
    }

    // --- When there is no night ---------------------------------------------

    [Fact]
    public void A_driver_who_did_not_race_has_no_night()
    {
        // Most of what this answers on a quiet evening, and not a fault.
        // Publishing an empty one would put somebody's name on a relay for
        // nothing.
        Assert.Null(DriverNight.Document("Bill Hargrove", Night, null, Facts(), At));
        Assert.Null(DriverNight.Document("Bill Hargrove", Night, null, null, At));
    }

    [Fact]
    public void A_car_nobody_named_has_no_night_either()
    {
        // The results page publishes those under their transponder number,
        // because a results table with a hole in it is worse. A document
        // whose only subject is a number is not somebody's night.
        Assert.Null(DriverNight.Document(null, Night, null, Facts("Raced."), At));
        Assert.Null(DriverNight.Document("   ", Night, null, Facts("Raced."), At));
    }

    [Fact]
    public void A_pack_of_nothing_but_blank_lines_is_no_night()
    {
        var facts = new PackedFacts("\n\n   \n", Array.Empty<string>(), 0);

        Assert.Null(DriverNight.Document("Bill Hargrove", Night, null, facts, At));
    }

    [Fact]
    public void A_night_bigger_than_the_relay_takes_is_dropped_rather_than_sent()
    {
        // Unreachable from FactsFor, which is capped at DriverCut.MostChars
        // - so this is only reachable through a name of absurd length.
        // Dropped here, because the relay would refuse it anyway and a 413
        // on the publisher's background thread is a worse way to find out.
        var huge = new string('x', DriverNight.MostDocumentBytes + 1);

        Assert.Null(DriverNight.Document(huge, Night, null, Facts("Raced."), At));
    }

    // --- The lines ----------------------------------------------------------

    [Fact]
    public void The_lines_are_split_where_the_pack_broke_them()
    {
        var lines = DriverNight.Lines(Facts("First.", "Second.", "Third."));

        Assert.Equal(new[] { "First.", "Second.", "Third." }, lines);
    }

    [Fact]
    public void Blank_lines_are_dropped_and_each_line_is_trimmed()
    {
        var facts = new PackedFacts("  First.  \n\n\n   \n  Second.", Array.Empty<string>(), 0);

        Assert.Equal(new[] { "First.", "Second." }, DriverNight.Lines(facts));
    }

    [Fact]
    public void An_empty_pack_has_no_lines()
    {
        Assert.Empty(DriverNight.Lines(null));
        Assert.Empty(DriverNight.Lines(Facts()));
    }

    [Fact]
    public void A_line_the_pack_already_cleaned_is_not_cleaned_again()
    {
        // FactPack's own Safe takes out the brackets and braces that could
        // close a prompt block, so by the time a line reaches here there is
        // nothing left to strip - and stripping anything else would be this
        // file editing a driver's evening. Escaping for the page is the
        // page's job; the relay never parses this at all.
        var said = "Bill passed Sam on lap 4 - 8.412s, their best - and held it.";

        Assert.Equal(new[] { said }, DriverNight.Lines(Facts(said)));
    }

    [Fact]
    public void A_quote_in_a_name_does_not_break_the_document()
    {
        // Through JsonSerializer rather than built by hand, which is the
        // whole reason this cannot go wrong - asserted because a club
        // really does have a driver called O'Brien and somebody will one
        // day have one with a quotation mark in it.
        var night = Read(DriverNight.Document("Dave \"Smithy\" O'Brien", Night, null, Facts("Raced."), At));

        Assert.Equal("Dave \"Smithy\" O'Brien", night.GetProperty("driver").GetString());
    }

    // --- The channel this goes to -------------------------------------------

    [Fact]
    public void The_channel_and_the_ceiling_match_the_relays_own()
    {
        // If these drifted apart, every night would be refused with a 413
        // on a background thread - or worse, accepted here and silently
        // over the relay's per-document limit.
        var channel = DriverNight.Channel;
        var most = DriverNight.MostDocumentBytes;

        Assert.Equal("nights", channel);
        Assert.Equal(64 * 1024, most);
    }

    // --- Scaffolding --------------------------------------------------------

    /// <summary>A pack holding exactly these lines, the way FactPack lays them out.</summary>
    private static PackedFacts Facts(params string[] lines) =>
        new(string.Join('\n', lines), Array.Empty<string>(), 0);

    private static JsonElement Read(string? document)
    {
        Assert.NotNull(document);

        return JsonDocument.Parse(document!).RootElement;
    }
}
