using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: the first-run wizard saves each step as it is left, and the
/// Classes and Track steps only ever added. Going Back and changing an answer
/// left the first answer behind: a race length changed later never reached
/// the classes already made (F134), and a corrected class or track name left
/// the misspelt one stored, with the Drivers step opening on it (F135).
/// </summary>
public sealed class BugHunt24_FirstRunWizardTests
{
    // --- The pure rule: which classes the wizard made that should go -------

    [Fact]
    public void A_class_the_wizard_made_and_no_longer_typed_is_dropped()
    {
        var drop = FirstRunWizard.ClassesToDrop(new[] { "Stok" }, new[] { "Stock" }, Array.Empty<string>());

        Assert.Equal(new[] { "Stok" }, drop);
    }

    [Fact]
    public void A_class_the_wizard_made_with_a_driver_saved_in_it_is_kept()
    {
        var drop = FirstRunWizard.ClassesToDrop(new[] { "Stok" }, new[] { "Stock" }, new[] { "stok" });

        Assert.Empty(drop);
    }

    [Fact]
    public void A_class_still_typed_is_kept_whatever_its_case()
    {
        var drop = FirstRunWizard.ClassesToDrop(new[] { "Stock" }, new[] { " stock " }, Array.Empty<string>());

        Assert.Empty(drop);
    }

    [Fact]
    public void A_class_the_wizard_did_not_make_is_never_dropped()
    {
        // "Mod" was there before the wizard, so it is not in what it made.
        var drop = FirstRunWizard.ClassesToDrop(Array.Empty<string>(), new[] { "Stock" }, Array.Empty<string>());

        Assert.Empty(drop);
    }

    // --- The wizard itself ----------------------------------------------

    [Fact]
    public void A_class_spelling_corrected_after_going_Back_leaves_only_the_corrected_class_and_drivers_default_into_it()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var stok = "BH24 Stok " + tag;
        var stock = "BH24 Stock " + tag;

        var seen = StaThread.Run(() =>
        {
            var raceClasses = RaceClassStore.Load();

            try
            {
                using var wizard = new FirstRunWizard(new AppSettings(), TrackLayoutStore.Load(), raceClasses, ProfileStore.Load(), startStep: 4);
                var box = Field<TextBox>(wizard, "_txtClasses");

                box.Text = stok;
                Leave(wizard, toStep: 5);
                var firstPick = Field<ComboBox>(wizard, "_cmbDriverClass").SelectedItem as string;

                Leave(wizard, toStep: 4);
                box.Text = stock;
                Leave(wizard, toStep: 5);

                var names = raceClasses.All.Select(c => c.Name).Where(n => n == stok || n == stock).ToList();
                var pick = Field<ComboBox>(wizard, "_cmbDriverClass").SelectedItem as string;
                var done = Field<List<string>>(wizard, "_doneClasses").ToList();

                return (firstPick, names, pick, done);
            }
            finally
            {
                foreach (var made in raceClasses.All.Where(c => c.Name == stok || c.Name == stock).ToList())
                {
                    raceClasses.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "correcting a class name in the first-run wizard");

        Assert.Equal(stok, seen.firstPick);
        Assert.Equal(new[] { stock }, seen.names);
        Assert.Equal(stock, seen.pick);
        Assert.DoesNotContain(stok, seen.done);
        Assert.Contains(stock, seen.done);
    }

    [Fact]
    public void A_race_length_changed_after_going_Back_reaches_the_classes_already_made()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var stock = "BH24 Stock " + tag;
        var before = "BH24 Before " + tag;

        var seen = StaThread.Run(() =>
        {
            var raceClasses = RaceClassStore.Load();

            try
            {
                // A class the club already had, with a length of its own.
                raceClasses.Upsert(new RaceClass { Name = before, Format = new RaceFormat { TimeLimitMinutes = 3, FinishOnNextCrossing = true } });

                using var wizard = new FirstRunWizard(new AppSettings(), TrackLayoutStore.Load(), raceClasses, ProfileStore.Load(), startStep: 4);
                var box = Field<TextBox>(wizard, "_txtClasses");
                var picker = Field<ClassLengthPicker>(wizard, "_classLength");

                // First pass: the length as it opens.
                box.Text = stock + Environment.NewLine + before;
                Leave(wizard, toStep: 5);
                Leave(wizard, toStep: 4);

                // Second look: 8 minutes, stop when time's up.
                Field<NumericUpDown>(picker, "_numMinutes").Value = 8;
                Field<ComboBox>(picker, "_cmbFinish").SelectedIndex = 1;
                Leave(wizard, toStep: 5);

                var stocks = raceClasses.All.Where(c => c.Name == stock).ToList();
                var old = raceClasses.All.Single(c => c.Name == before);

                return (count: stocks.Count,
                        minutes: stocks.FirstOrDefault()?.Format?.TimeLimitMinutes,
                        finish: stocks.FirstOrDefault()?.Format?.FinishOnNextCrossing,
                        oldMinutes: old.Format?.TimeLimitMinutes,
                        oldFinish: old.Format?.FinishOnNextCrossing);
            }
            finally
            {
                foreach (var made in raceClasses.All.Where(c => c.Name == stock || c.Name == before).ToList())
                {
                    raceClasses.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "changing the race length in the first-run wizard");

        Assert.Equal(1, seen.count);
        Assert.Equal(8, seen.minutes);
        Assert.False(seen.finish);

        // The class that was there before keeps its own length.
        Assert.Equal(3, seen.oldMinutes);
        Assert.True(seen.oldFinish);
    }

    [Fact]
    public void A_class_an_imported_list_made_follows_a_later_change_of_race_length()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var mod = "BH24 Mod " + tag;

        var seen = StaThread.Run(() =>
        {
            var raceClasses = RaceClassStore.Load();

            try
            {
                using var wizard = new FirstRunWizard(new AppSettings(), TrackLayoutStore.Load(), raceClasses, ProfileStore.Load(), startStep: 4);
                var picker = Field<ClassLengthPicker>(wizard, "_classLength");

                wizard.AddListedClasses(new[] { mod });

                Field<NumericUpDown>(picker, "_numMinutes").Value = 7;
                Field<ComboBox>(picker, "_cmbFinish").SelectedIndex = 1;
                Leave(wizard, toStep: 5);

                var made = raceClasses.All.Single(c => c.Name == mod);
                return (made.Format?.TimeLimitMinutes, made.Format?.FinishOnNextCrossing);
            }
            finally
            {
                foreach (var made in raceClasses.All.Where(c => c.Name == mod).ToList())
                {
                    raceClasses.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "an imported class in the first-run wizard");

        Assert.Equal(7, seen.Item1);
        Assert.False(seen.Item2);
    }

    [Fact]
    public void A_track_renamed_after_going_Back_leaves_one_layout_under_the_new_name()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var clubb = "BH24 Clubb " + tag;
        var club = "BH24 Club " + tag;

        var seen = StaThread.Run(() =>
        {
            var layouts = TrackLayoutStore.Load();
            var settings = new AppSettings();

            try
            {
                using var wizard = new FirstRunWizard(settings, layouts, RaceClassStore.Load(), ProfileStore.Load(), startStep: 3);
                var box = Field<TextBox>(wizard, "_txtTrackName");

                box.Text = clubb;
                Leave(wizard, toStep: 4);
                Leave(wizard, toStep: 3);
                box.Text = club;
                Leave(wizard, toStep: 4);

                return (names: layouts.All.Select(t => t.Name).Where(n => n == clubb || n == club).ToList(),
                        last: settings.LastTrackLayoutName);
            }
            finally
            {
                foreach (var made in layouts.All.Where(t => t.Name == clubb || t.Name == club).ToList())
                {
                    layouts.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "renaming the track in the first-run wizard");

        Assert.Equal(new[] { club }, seen.names);
        Assert.Equal(club, seen.last);
    }

    [Fact]
    public void A_track_renamed_to_a_layout_already_there_drops_the_one_the_wizard_made()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var clubb = "BH24 Clubb " + tag;
        var club = "BH24 Club " + tag;

        var seen = StaThread.Run(() =>
        {
            var layouts = TrackLayoutStore.Load();

            try
            {
                var existing = new TrackLayout { Name = club };
                layouts.Upsert(existing);

                using var wizard = new FirstRunWizard(new AppSettings(), layouts, RaceClassStore.Load(), ProfileStore.Load(), startStep: 3);
                var box = Field<TextBox>(wizard, "_txtTrackName");

                box.Text = clubb;
                Leave(wizard, toStep: 4);
                Leave(wizard, toStep: 3);
                box.Text = club;
                Leave(wizard, toStep: 4);

                var left = layouts.All.Where(t => t.Name == clubb || t.Name == club).ToList();
                return (names: left.Select(t => t.Name).ToList(), keptId: left.Any(t => t.Id == existing.Id));
            }
            finally
            {
                foreach (var made in layouts.All.Where(t => t.Name == clubb || t.Name == club).ToList())
                {
                    layouts.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "renaming the track to an existing layout in the first-run wizard");

        Assert.Equal(new[] { club }, seen.names);
        Assert.True(seen.keptId);
    }

    /// <summary>What Next, Back and the rail all do: save the step being left, then show the other.</summary>
    private static void Leave(FirstRunWizard wizard, int toStep)
    {
        Invoke(wizard, "SaveCurrentStep");
        Invoke(wizard, "ShowStep", toStep);
    }

    private static void Invoke(object owner, string name, params object[] args) =>
        (owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{owner.GetType().Name} has no {name}."))
        .Invoke(owner, args);

    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
            ?? throw new InvalidOperationException($"{owner.GetType().Name} has no {name}."));
}
