namespace HeatCaller.Tests;

/// <summary>
/// Driver experience PV (22 Oct 2026): initials only, enforced inside
/// RelayPublisher.DisplayNames - decided on the name as given (before any
/// shortening), matched by transponder where one is known, and kept
/// distinct when two drivers share initials. The room's form
/// (DisplayNamesForTheRoom) is unchanged.
/// </summary>
[Collection(PublicPrivacyCurrentCollection.Name)]
public sealed class DriverExp_PV_DisplayNamesTests
{
    private static DriverProfile Driver(string transponder, string name, bool initials = false, string announcer = "") => new()
    {
        Transponder = transponder,
        DriverName = name,
        AnnouncerName = announcer,
        InitialsOnline = initials,
    };

    private static PublicPrivacy Register(params DriverProfile[] profiles) => PublicPrivacy.From(profiles);

    private static IReadOnlyList<string> Shown(PublicPrivacy register, bool abbreviate, IReadOnlyList<string?>? transponders, params string[] names) =>
        RelayPublisher.DisplayNames(names, abbreviate, transponders, register);

    [Fact]
    public void With_nobody_asking_the_answer_is_exactly_the_old_one()
    {
        var lists = new[]
        {
            new[] { "Ghost of Christmas Past", "Ghost of Christmas Present" },
            new[] { "Alex Kent", "Alex Kemp", "Bob", "  Sam Jones " },
            new[] { "Bob Smith", "Bob Smith" },
            Array.Empty<string>(),
        };

        var nobody = Register(Driver("T9", "Alex Kent"));

        foreach (var list in lists)
        {
            foreach (var abbreviate in new[] { true, false })
            {
                var room = RelayPublisher.DisplayNamesForTheRoom(list, abbreviate);

                Assert.Equal(room, RelayPublisher.DisplayNames(list, abbreviate, null, PublicPrivacy.Empty));
                Assert.Equal(room, RelayPublisher.DisplayNames(list, abbreviate, null, nobody));
                Assert.Equal(room, RelayPublisher.DisplayNames(list, abbreviate, list.Select(_ => (string?)"T9").ToList(), nobody));
            }
        }
    }

    [Fact]
    public void A_flagged_driver_is_initials_whether_or_not_the_club_shortens_names()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true), Driver("T2", "Alex Jones"));

        Assert.Equal(new[] { "S.P.", "Alex J" }, Shown(register, true, null, "Sam Parker", "Alex Jones"));
        Assert.Equal(new[] { "S.P.", "Alex Jones" }, Shown(register, false, null, "Sam Parker", "Alex Jones"));

        // Any spacing or case the row was saved with.
        Assert.Equal(new[] { "S.P." }, Shown(register, true, null, "  sam   PARKER "));
    }

    [Fact]
    public void Initials_are_decided_on_the_full_name_before_shortening_and_give_nothing_away()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true), Driver("T2", "Sam Pike"));

        // "Sam P" is nobody in the register; "Sam Parker" is - so the check
        // is on the name as given. And Sam Pike is shortened among the
        // visible names only: had Sam Parker counted, Sam Pike would have
        // grown to "Sam Pi", telling a reader there is another Sam P. here.
        Assert.Equal(new[] { "S.P.", "Sam P" }, Shown(register, true, null, "Sam Parker", "Sam Pike"));
    }

    [Fact]
    public void Two_drivers_with_the_same_initials_are_numbered_the_same_way_in_any_order()
    {
        var register = Register(
            Driver("T1", "Sam Parker", initials: true),
            Driver("T2", "Steve Pike", initials: true));

        Assert.Equal(new[] { "S.P.", "S.P. 2" }, Shown(register, true, null, "Sam Parker", "Steve Pike"));
        Assert.Equal(new[] { "S.P. 2", "S.P." }, Shown(register, true, null, "Steve Pike", "Sam Parker"));
        Assert.Equal(new[] { "S.P.", "S.P. 2" }, Shown(register, false, null, "Sam Parker", "Steve Pike"));

        // No letters given back: "S.Pa." would be part of the name they asked to keep off.
        Assert.DoesNotContain(Shown(register, true, null, "Sam Parker", "Steve Pike"), n => n.Contains("Pa") || n.Contains("Pi"));
    }

    [Fact]
    public void The_same_driver_twice_keeps_one_name()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true));

        Assert.Equal(new[] { "S.P.", "S.P." }, Shown(register, true, null, "Sam Parker", "sam parker"));
    }

    [Fact]
    public void A_visible_driver_who_already_reads_as_the_initials_pushes_the_hidden_one_to_a_number()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true));

        Assert.Equal(new[] { "S.P.", "S.P. 2" }, Shown(register, true, null, "S.P.", "Sam Parker"));
    }

    [Fact]
    public void A_row_from_before_a_rename_is_caught_by_its_transponder()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true), Driver("T2", "Alex Jones"));

        // The old row says "Samantha Parker": by name alone that is nobody.
        Assert.Equal(new[] { "Samantha P", "Alex J" }, Shown(register, true, null, "Samantha Parker", "Alex Jones"));
        Assert.Equal(new[] { "S.P.", "Alex J" }, Shown(register, true, new string?[] { "t1", "T2" }, "Samantha Parker", "Alex Jones"));

        // A shorter transponder list, or a null in it, falls back to the name.
        Assert.Equal(new[] { "Alex J", "S.P." }, Shown(register, true, new string?[] { null }, "Alex Jones", "Sam Parker"));
    }

    [Fact]
    public void The_announcer_name_counts_but_an_unnamed_car_is_never_initialled()
    {
        var register = Register(Driver("T1", "Sam Parker", initials: true, announcer: "Zhock"));

        Assert.Equal(new[] { "Z." }, Shown(register, true, null, "Zhock"));
        Assert.Equal(
            new[] { RelayDocuments.UnnamedDriver },
            Shown(register, false, new string?[] { "T1" }, RelayDocuments.UnnamedDriver));
    }

    [Fact]
    public void The_public_form_reads_the_live_register_and_the_room_form_never_does()
    {
        var was = PublicPrivacy.Current;

        try
        {
            PublicPrivacy.Current = Register(Driver("PV-T1", "Quentin Parkerson", initials: true));

            Assert.Equal(new[] { "Q.P." }, RelayPublisher.DisplayNames(new[] { "Quentin Parkerson" }, abbreviate: true));
            Assert.Equal(new[] { "Q.P." }, RelayPublisher.DisplayNames(new[] { "Quentin Parkerson" }, abbreviate: false));
            Assert.Equal(new[] { "Quentin P" }, RelayPublisher.DisplayNamesForTheRoom(new[] { "Quentin Parkerson" }, abbreviate: true));

            // The cloud voices' short names are said over the PA, in the room.
            var map = AnnouncementService.ShortNameMap(new[] { "Quentin Parkerson", "Ann Lee" });
            Assert.Equal("Quentin P", map["Quentin Parkerson"]);
        }
        finally
        {
            PublicPrivacy.Current = was;
        }
    }
}
