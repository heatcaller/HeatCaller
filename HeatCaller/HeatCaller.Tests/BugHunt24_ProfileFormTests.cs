using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 on the driver profile dialog: an extra transponder typed but
/// not yet Added survives an edit to the main Transponder box (F199), and a
/// crew name or "said as" the announcer can't say is refused at Save rather
/// than kept and silently never said (F200).
/// </summary>
public sealed class BugHunt24_ProfileFormTests : IDisposable
{
    /// <summary>Declared first: the stores the dialog reads load from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Field<T>(ProfileForm form, string name) =>
        (T)typeof(ProfileForm).GetField(name, Private)!.GetValue(form)!;

    // ------------------------------------------------------------ F199

    [Fact]
    public void BugHunt24_AddDriver_TypedExtraTransponder_SurvivesEditingTheMainTransponder()
    {
        var (typedAfter, saved) = StaThread.Run(() =>
        {
            using var form = new ProfileForm(null, RaceClassStore.Load());
            var main = Field<TextBox>(form, "_txtTransponder");
            var extras = Field<ComboBox>(form, "_cmbTransponders");

            main.Text = "1234567";
            extras.Text = "7654321";
            main.Text = "1234568";

            var typed = extras.Text;
            var added = (bool)typeof(ProfileForm).GetMethod("AddTransponder", Private)!.Invoke(form, null)!;
            Assert.True(added);
            return (typed, form.ProfileAsShown());
        }, TimeSpan.FromSeconds(60), () => "the add driver dialog with a typed extra transponder");

        Assert.Equal("7654321", typedAfter);
        Assert.Equal("1234568", saved.Transponder);
        Assert.Contains(saved.Transponders, t => t.Id == "7654321");
    }

    [Fact]
    public void BugHunt24_AddDriver_NothingTyped_EditingTheMainTransponderLeavesTheDropDownBlank()
    {
        var text = StaThread.Run(() =>
        {
            using var form = new ProfileForm(null, RaceClassStore.Load());
            var main = Field<TextBox>(form, "_txtTransponder");
            var extras = Field<ComboBox>(form, "_cmbTransponders");

            main.Text = "1234567";
            main.Text = "1234568";
            return extras.Text;
        }, TimeSpan.FromSeconds(60), () => "the add driver dialog with nothing typed");

        Assert.Equal(string.Empty, text);
    }

    // ------------------------------------------------------------ F200

    [Theory]
    [InlineData("Coach D2", false)]
    [InlineData("Big Dave & Sons", false)]
    [InlineData("Mum (Karen)", false)]
    [InlineData("Big Dave", true)]
    [InlineData("O'Neil-Smith", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void BugHunt24_CrewName_ThatCantBeSaid_IsRefusedAtSave(string typed, bool ok)
    {
        var problem = ProfileForm.CrewWordProblem(typed, string.Empty, saidAs: false);

        Assert.Equal(ok, problem is null);

        if (!ok)
        {
            Assert.Contains(typed, problem!);
            Assert.Contains("letters, spaces, hyphens and apostrophes", problem);
        }
    }

    [Theory]
    [InlineData("Dayv 2", false)]
    [InlineData("Dayv!", false)]
    [InlineData("Dayv", true)]
    [InlineData("Grah vel", true)]
    [InlineData("", true)]
    public void BugHunt24_CrewSaidAs_ThatCantBeReadOut_IsRefusedAtSave(string typed, bool ok) =>
        Assert.Equal(ok, ProfileForm.CrewWordProblem(typed, null, saidAs: true) is null);

    [Fact]
    public void BugHunt24_CrewName_SavedBeforeTheCheck_DoesNotBlockAnUnrelatedEdit()
    {
        Assert.Null(ProfileForm.CrewWordProblem("Coach D2", "Coach D2", saidAs: false));
        Assert.Null(ProfileForm.CrewWordProblem("Dayv 2", "Dayv 2", saidAs: true));
        Assert.NotNull(ProfileForm.CrewWordProblem("Coach D3", "Coach D2", saidAs: false));
    }

    [Fact]
    public void BugHunt24_CrewName_ThatIsRefused_IsOneTheAnnouncerWouldPassOver()
    {
        // Why the save must be refused: a name like this is never said.
        Assert.Equal("Club Boss", DriverCrew.SpokenNameFor("Coach D2", "Coach Dee", "Club Boss"));
    }
}
