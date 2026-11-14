namespace HeatCaller.Tests;

/// <summary>
/// The Add a driver window takes more than one class (13 Nov 2026): a tick
/// box per class in place of the drop-down that took one. The class it was
/// opened on stays the main one while it is ticked; the rest are saved as
/// classes the driver races too.
/// </summary>
public sealed class QuickDriverFormClassesTests
{
    private static readonly string[] Classes = { "50%", "Box Stock", "Mod", "Stock" };

    [Fact]
    public void It_opens_with_the_filter_s_class_ticked_and_nothing_else() =>
        StaThread.Run(() =>
        {
            using var form = new QuickDriverForm(Classes, "Stock", _ => null);

            Assert.Equal(new[] { "Stock" }, form.ClassNames);
            Assert.Equal("Stock", form.ClassName);
            Assert.Empty(form.ExtraClassNames);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "the window opening");

    [Fact]
    public void The_class_it_was_opened_on_stays_the_main_one_with_others_ticked() =>
        StaThread.Run(() =>
        {
            using var form = new QuickDriverForm(Classes, "Stock", _ => null);

            form.Tick("50%", true);
            form.Tick("Mod", true);

            Assert.Equal("Stock", form.ClassName);
            Assert.Equal(new[] { "50%", "Mod" }, form.ExtraClassNames);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "ticking more classes");

    [Fact]
    public void With_its_class_unticked_the_first_ticked_is_the_main_one() =>
        StaThread.Run(() =>
        {
            using var form = new QuickDriverForm(Classes, "Stock", _ => null);

            form.Tick("Stock", false);
            form.Tick("Mod", true);
            form.Tick("Box Stock", true);

            Assert.Equal("Box Stock", form.ClassName);
            Assert.Equal(new[] { "Mod" }, form.ExtraClassNames);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "unticking the opening class");

    [Fact]
    public void The_new_driver_races_every_class_ticked() =>
        StaThread.Run(() =>
        {
            using var form = new QuickDriverForm(Classes, null, _ => null);

            form.Tick("Mod", true);
            form.Tick("50%", true);

            var profile = QuickDriverForm.NewProfile(form);

            Assert.Equal("50%", profile.ClassName);
            Assert.True(profile.RunsClass("Mod"));
            Assert.True(profile.RunsClass("50%"));
            Assert.False(profile.RunsClass("Stock"));
            Assert.Equal("50% + Mod", CheckInControl.ClassCellText(profile));
            return 0;
        }, TimeSpan.FromMinutes(1), () => "saving the classes");

    [Fact]
    public void No_class_ticked_saves_a_driver_with_no_class() =>
        StaThread.Run(() =>
        {
            using var form = new QuickDriverForm(Classes, null, _ => null);

            var profile = QuickDriverForm.NewProfile(form);

            Assert.Equal(string.Empty, profile.ClassName);
            Assert.Empty(form.ClassNames);
            return 0;
        }, TimeSpan.FromMinutes(1), () => "no class ticked");
}
