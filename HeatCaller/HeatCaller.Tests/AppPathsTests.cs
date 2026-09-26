namespace HeatCaller.Tests;

/// <summary>
/// The guard every other test relies on: under test, the app never reads or
/// writes the real %AppData%\HeatCaller.
/// </summary>
public sealed class AppPathsTests : IDisposable
{
    private readonly TempAppData _data = new();

    private static readonly string RealRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeatCaller");

    private static readonly string RealLocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeatCaller");

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Data_goes_to_the_test_folder_not_the_real_one()
    {
        Assert.True(AppPaths.IsRedirected);
        Assert.StartsWith(Path.GetFullPath(_data.Folder), Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(_data.Folder), Path.GetFullPath(AppPaths.LocalRoot), StringComparison.OrdinalIgnoreCase);

        Assert.False(AppPaths.Root.StartsWith(RealRoot, StringComparison.OrdinalIgnoreCase));
        Assert.False(AppPaths.LocalRoot.StartsWith(RealLocalRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Subfolder")]
    public void Redirecting_into_the_real_data_folder_is_refused(string below)
    {
        Assert.Throws<InvalidOperationException>(() => AppPaths.RedirectForTests(Path.Combine(RealRoot, below)));
        Assert.Throws<InvalidOperationException>(() => AppPaths.RedirectForTests(Path.Combine(RealLocalRoot, below)));

        // Still pointing where it was.
        Assert.StartsWith(Path.GetFullPath(_data.Folder), Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redirecting_to_a_folder_that_contains_the_real_data_is_refused()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Throws<InvalidOperationException>(() => AppPaths.RedirectForTests(appData));
    }

    [Fact]
    public void A_store_saves_into_the_test_folder()
    {
        var history = SessionHistoryStore.Load();

        Assert.True(history.Add(new SessionRecord { Label = "Q1 Heat 1" }));
        Assert.True(File.Exists(_data.PathOf("race-history.json")));
    }
}
