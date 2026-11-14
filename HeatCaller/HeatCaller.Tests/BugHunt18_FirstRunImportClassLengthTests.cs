using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18: in the first-run wizard, a class made by the Drivers step's
/// Import a List... used the app default race length, not the length and
/// finish the club had just picked on the Classes step - so the classes they
/// typed and the classes the list brought raced different lengths.
/// </summary>
public sealed class BugHunt18_FirstRunImportClassLengthTests
{
    [Fact]
    public void A_class_an_imported_list_names_gets_the_length_and_finish_picked_on_the_Classes_step()
    {
        // Names of their own, so nothing another test put in the shared store is matched.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var stock = "BH18 Stock " + tag;
        var mod = "BH18 Mod " + tag;

        var seen = StaThread.Run(() =>
        {
            var raceClasses = RaceClassStore.Load();

            try
            {
                using var wizard = new FirstRunWizard(new AppSettings(), TrackLayoutStore.Load(), raceClasses, ProfileStore.Load(), startStep: 4);

                // The Classes step's picker: 9 minutes, stop when time's up.
                var picker = Field<ClassLengthPicker>(wizard, "_classLength");
                Field<NumericUpDown>(picker, "_numMinutes").Value = 9;
                Field<ComboBox>(picker, "_cmbFinish").SelectedIndex = 1;

                wizard.AddListedClasses(new[] { " " + stock + " ", mod });

                return raceClasses.All
                    .Where(c => c.Name == stock || c.Name == mod)
                    .Select(c => (c.Format?.TimeLimitMinutes, c.Format?.FinishOnNextCrossing))
                    .ToList();
            }
            finally
            {
                foreach (var made in raceClasses.All.Where(c => c.Name == stock || c.Name == mod).ToList())
                {
                    raceClasses.Delete(made.Id);
                }
            }
        }, TimeSpan.FromSeconds(30), () => "importing classes in the first-run wizard");

        Assert.Equal(2, seen.Count);

        foreach (var (minutes, finishTheLap) in seen)
        {
            Assert.Equal(9, minutes);
            Assert.False(finishTheLap);
        }
    }

    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
            ?? throw new InvalidOperationException($"{owner.GetType().Name} has no {name}."));
}
