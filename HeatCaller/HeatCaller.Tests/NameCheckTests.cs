namespace HeatCaller.Tests;

/// <summary>
/// Checking pronunciation: the name the announcer will read for a driver
/// (DriverProfile.SpokenName), the roster NameCheckForm walks, and the Hear
/// It beside a driver's Announcer name box (ProfileForm.NameAsAnnounced).
///
/// Nothing here listens. AudioBus.Silent is true for the whole test run, so
/// what is tested is which words would be handed to the voice and in what
/// order - which is the part that can be wrong.
/// </summary>
public sealed class NameCheckTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DriverProfile Driver(string transponder, string name, string announcer = "") =>
        new() { Transponder = transponder, DriverName = name, AnnouncerName = announcer };

    private static NameCheckForm.NameToCheck Only(params DriverProfile?[] drivers) =>
        Assert.Single(NameCheckForm.NamesToCheck(drivers));

    // ---- The name itself -------------------------------------------------

    [Fact]
    public void The_announcer_name_is_what_gets_read()
    {
        var row = Only(Driver("4000137", "Jacques Dupont", "Zhock Doo-pon"));

        Assert.Equal("Zhock Doo-pon", row.Spoken);
        Assert.Equal("Jacques Dupont", row.DriverName);
        Assert.True(row.Respelled);
    }

    [Fact]
    public void Without_one_the_real_name_gets_read()
    {
        var row = Only(Driver("4000137", "Sam Bell"));

        Assert.Equal("Sam Bell", row.Spoken);
        Assert.False(row.Respelled);
    }

    /// <summary>
    /// A box with nothing but spaces in it is not a respelling. It used to
    /// be possible to "set" an announcer name by leaving a space in the box,
    /// and SpokenName has always treated that as unset - the row has to
    /// agree, or the list says a driver has been dealt with when the voice
    /// will read their real name.
    /// </summary>
    [Fact]
    public void Spaces_in_the_box_are_not_a_spelling()
    {
        var row = Only(Driver("4000137", "Sam Bell", "   "));

        Assert.Equal("Sam Bell", row.Spoken);
        Assert.False(row.Respelled);
    }

    /// <summary>
    /// The announcer reads the transponder number out for a driver nobody
    /// has named - which is exactly the sort of thing this window exists to
    /// catch, so the row shows it rather than hiding it.
    /// </summary>
    [Fact]
    public void A_driver_with_no_name_is_read_as_their_transponder()
    {
        var row = Only(Driver("4000137", string.Empty));

        Assert.Equal("4000137", row.Spoken);
        Assert.Equal("(4000137)", row.Who);
    }

    [Fact]
    public void A_nickname_counts_even_for_a_driver_with_no_name()
    {
        var row = Only(Driver("4000137", string.Empty, "The Rocket"));

        Assert.Equal("The Rocket", row.Spoken);
        Assert.True(row.Respelled);
    }

    /// <summary>
    /// The point of the whole feature: what the check reads out is the same
    /// string the race calls read out. The voice queue fills {driver} from
    /// DriverProfile.SpokenName (AnnouncementService.VoiceQueue.cs), so if
    /// these two ever disagree the check is lying.
    /// </summary>
    [Fact]
    public void What_is_read_is_what_the_announcer_reads()
    {
        var drivers = new[]
        {
            Driver("4000137", "Jacques Dupont", "Zhock Doo-pon"),
            Driver("4000274", "Sam Bell"),
            Driver("4000411", string.Empty),
            Driver("4000548", string.Empty, "The Rocket"),
            Driver("4000685", "Siobhan Murphy", "Shiv-awn Murphy"),
        };

        var rows = NameCheckForm.NamesToCheck(drivers);

        Assert.Equal(drivers.Select(d => d.SpokenName).ToList(), rows.Select(r => r.Spoken).ToList());
    }

    // ---- The list ---------------------------------------------------------

    [Fact]
    public void The_roster_order_is_kept()
    {
        var rows = NameCheckForm.NamesToCheck(new[]
        {
            Driver("4000274", "Zoe Winters"),
            Driver("4000137", "Adam Clarke"),
            Driver("4000411", "Mia Hall"),
        });

        Assert.Equal(new[] { "Zoe Winters", "Adam Clarke", "Mia Hall" }, rows.Select(r => r.Spoken));
    }

    [Fact]
    public void A_driver_listed_twice_is_heard_once()
    {
        var sam = Driver("4000137", "Sam Bell");

        var rows = NameCheckForm.NamesToCheck(new[] { sam, Driver("4000274", "Mia Hall"), sam });

        Assert.Equal(new[] { "Sam Bell", "Mia Hall" }, rows.Select(r => r.Spoken));
    }

    /// <summary>The roster matches a transponder however it is typed, so the sifting has to as well.</summary>
    [Fact]
    public void The_same_transponder_in_another_case_is_the_same_driver()
    {
        var rows = NameCheckForm.NamesToCheck(new[]
        {
            Driver("ab12", "Sam Bell"),
            Driver("AB12", "Sam Bell again"),
        });

        Assert.Single(rows);
    }

    [Fact]
    public void A_profile_with_nothing_to_read_is_left_out()
    {
        Assert.Empty(NameCheckForm.NamesToCheck(new[] { Driver(string.Empty, string.Empty) }));
    }

    /// <summary>ProfileStore.Get answers null for a transponder that has gone since the roster was drawn.</summary>
    [Fact]
    public void A_driver_who_has_gone_is_skipped()
    {
        var rows = NameCheckForm.NamesToCheck(new DriverProfile?[] { null, Driver("4000137", "Sam Bell"), null });

        Assert.Equal(new[] { "Sam Bell" }, rows.Select(r => r.Spoken));
    }

    [Fact]
    public void Nothing_to_check_is_an_empty_list_not_a_crash()
    {
        Assert.Empty(NameCheckForm.NamesToCheck(null));
        Assert.Empty(NameCheckForm.NamesToCheck(Array.Empty<DriverProfile?>()));
    }

    // ---- The window -------------------------------------------------------

    /// <summary>Every control inside <paramref name="root"/>, however deep.</summary>
    private static IEnumerable<Control> Inside(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var deeper in Inside(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>What the window is showing in its "what the announcer says" column, in the order it shows it.</summary>
    private static List<string> Listed(NameCheckForm form)
    {
        using (form)
        {
            return Inside(form)
                .OfType<DataGridView>()
                .Single()
                .Rows
                .Cast<DataGridViewRow>()
                .Select(row => row.Cells[1].Value as string ?? string.Empty)
                .ToList();
        }
    }

    private static readonly DriverProfile[] Three =
    {
        Driver("4000137", "Jacques Dupont", "Zhock Doo-pon"),
        Driver("4000274", "Sam Bell"),
        Driver("4000411", string.Empty),
    };

    [Fact]
    public void The_window_lists_the_real_name_beside_what_is_read()
    {
        var shown = StaThread.Run(() =>
        {
            using var form = new NameCheckForm(Three);
            var grid = Inside(form).OfType<DataGridView>().Single();

            return grid.Rows
                .Cast<DataGridViewRow>()
                .Select(row => string.Join(" | ", row.Cells.Cast<DataGridViewCell>().Select(c => c.Value as string ?? string.Empty)))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "building the name check");

        Assert.Equal(new[]
        {
            "Jacques Dupont | Zhock Doo-pon | Yes",
            "Sam Bell | Sam Bell | ",
            "(4000411) | 4000411 | ",
        }, shown);
    }

    /// <summary>
    /// The five-minute pass: one button pressed over and over works down
    /// every name and comes back round to the top, so a race director never
    /// has to find their place again.
    /// </summary>
    [Fact]
    public void Say_the_next_one_works_down_the_list_and_round_again()
    {
        var said = new List<string>();

        StaThread.Run(() =>
        {
            using var form = new NameCheckForm(Three, said.Add);

            for (var press = 0; press < 4; press++)
            {
                form.SayTheNextOne();
            }

            return true;
        }, TimeSpan.FromSeconds(30), () => "working down the name check");

        // It opens on the first driver, so the first press moves to the
        // second - and the fourth has come back round to it.
        Assert.Equal(new[] { "Sam Bell", "4000411", "Zhock Doo-pon", "Sam Bell" }, said);
    }

    [Fact]
    public void Say_it_reads_the_driver_the_window_opened_on()
    {
        var said = new List<string>();

        StaThread.Run(() =>
        {
            using var form = new NameCheckForm(Three, said.Add);
            form.SayThisOne();
            return true;
        }, TimeSpan.FromSeconds(30), () => "reading one name");

        Assert.Equal(new[] { "Zhock Doo-pon" }, said);
    }

    /// <summary>
    /// With a check-in sheet, the window starts on whoever is actually
    /// racing - the names that are going to be read tonight. A club with a
    /// season's worth of saved drivers would otherwise be handed all of them.
    /// </summary>
    [Fact]
    public void With_a_check_in_sheet_it_starts_on_tonights_drivers()
    {
        var checkIns = CheckInStore.Load();
        checkIns.SetCheckedIn("4000274", true);

        var listed = StaThread.Run(
            () => Listed(new NameCheckForm(Three, checkIns: checkIns)),
            TimeSpan.FromSeconds(30),
            () => "building the name check for tonight");

        Assert.Equal(new[] { "Sam Bell" }, listed);
    }

    [Fact]
    public void Without_a_check_in_sheet_everyone_given_is_listed()
    {
        var listed = StaThread.Run(
            () => Listed(new NameCheckForm(Three)),
            TimeSpan.FromSeconds(30),
            () => "building the name check without a sheet");

        Assert.Equal(new[] { "Zhock Doo-pon", "Sam Bell", "4000411" }, listed);
    }

    // ---- The profile dialog's Hear It -------------------------------------

    /// <summary>
    /// What Hear It reads is built from the boxes as they stand, not from
    /// the saved profile: trying a spelling before saving it is the entire
    /// point of the button.
    /// </summary>
    [Fact]
    public void Hear_it_reads_the_box_as_it_stands()
    {
        var read = StaThread.Run(() =>
        {
            // With a voice, so the dialog is in the shape a club sees it in.
            using var form = new ProfileForm(
                Driver("4000137", "Jacques Dupont"),
                RaceClassStore.Load(),
                speakName: _ => { });

            var box = Inside(form)
                .OfType<TextBox>()
                .Single(b => b.PlaceholderText == "optional - a nickname, or how the name sounds");

            // Typed and not saved, as a club trying a spelling would.
            box.Text = "Zhock Doo-pon";

            return form.NameAsAnnounced();
        }, TimeSpan.FromSeconds(30), () => "opening a driver profile");

        Assert.Equal("Zhock Doo-pon", read);
    }

    [Fact]
    public void Hear_it_falls_back_to_the_driver_name()
    {
        var read = StaThread.Run(() =>
        {
            using var form = new ProfileForm(Driver("4000137", "Sam Bell"), RaceClassStore.Load());
            return form.NameAsAnnounced();
        }, TimeSpan.FromSeconds(30), () => "opening a driver profile with no announcer name");

        Assert.Equal("Sam Bell", read);
    }

    /// <summary>
    /// The button is left off rather than shown doing nothing when the
    /// dialog was opened without a voice to read through - see
    /// ProfileManagerControl.SpeakName.
    /// </summary>
    [Fact]
    public void Hear_it_is_only_there_when_there_is_a_voice()
    {
        var (withVoice, without) = StaThread.Run(() =>
        {
            using var voiced = new ProfileForm(
                Driver("4000137", "Sam Bell"), RaceClassStore.Load(), speakName: _ => { });

            using var silent = new ProfileForm(Driver("4000137", "Sam Bell"), RaceClassStore.Load());

            static bool HasHearIt(Control form) =>
                Inside(form).OfType<Button>().Any(b => b.Text == "Hear It");

            return (HasHearIt(voiced), HasHearIt(silent));
        }, TimeSpan.FromSeconds(30), () => "comparing driver profiles with and without a voice");

        Assert.True(withVoice);
        Assert.False(without);
    }
}
