namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, chassis-templates.json values a restored or hand-edited file
/// can carry: diagram positions far off the diagram (the canvas threw on
/// every paint), and sheet ids that are not the app's own 32 hex characters
/// (a short one stopped pictures being set; one holding "..\" copied the
/// picture outside the ChassisImages folder).
/// </summary>
public sealed class BugHunt24_ChassisModelsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private ChassisTemplateStore LoadFrom(string json)
    {
        var path = _data.PathOf("chassis-templates.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return ChassisTemplateStore.Load();
    }

    private string Picture()
    {
        var source = Path.Combine(_data.Folder, "pick", "mr03.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
        return source;
    }

    private static string ImageFolder => Path.GetFullPath(Path.Combine(AppPaths.Root, "ChassisImages"));

    [Fact]
    public void Load_HugeDiagramPositions_AreKeptOnTheDiagram()
    {
        var store = LoadFrom("""
            [ { "Id": "t1", "Name": "MR-03", "Sections": [ { "Id": "s1", "Name": "Front", "Fields": [
                { "Id": "f1", "Label": "Spring", "OnDiagram": true, "X": 1e30, "Y": -1e30, "LabelOffsetX": 1e30, "LabelOffsetY": -1e30 },
                { "Id": "f2", "Label": "Arm", "OnDiagram": true, "X": 0.3, "Y": 0.7, "LabelOffsetX": 0.2, "LabelOffsetY": -0.1 } ] } ] } ]
            """);

        var fields = Assert.Single(store.All).AllFields.ToList();

        Assert.Equal(1f, fields[0].X);
        Assert.Equal(0f, fields[0].Y);
        Assert.Equal(1f, fields[0].LabelOffsetX);
        Assert.Equal(-1f, fields[0].LabelOffsetY);

        // An ordinary field is left exactly as it was.
        Assert.Equal(0.3f, fields[1].X);
        Assert.Equal(0.7f, fields[1].Y);
        Assert.Equal(0.2f, fields[1].LabelOffsetX);
        Assert.Equal(-0.1f, fields[1].LabelOffsetY);
    }

    [Fact]
    public void KeepIn_NotANumber_GoesBackToTheFallback()
    {
        Assert.Equal(0.5f, ChassisTemplateStore.KeepIn(float.NaN, 0f, 1f, 0.5f));
        Assert.Equal(0.20f, ChassisTemplateStore.KeepIn(float.PositiveInfinity, -1f, 1f, 0.20f));
        Assert.Equal(0.25f, ChassisTemplateStore.KeepIn(0.25f, 0f, 1f, 0.5f));
    }

    [Fact]
    public void SetImage_SheetWithShortId_StillStoresThePicture()
    {
        var store = LoadFrom("""[ { "Id": "t1", "Name": "MR-03", "Sections": [] } ]""");
        var sheet = Assert.Single(store.All);

        Assert.True(store.SetImage(sheet, Picture()));

        var resolved = store.ResolveImagePath(sheet);
        Assert.NotNull(resolved);
        Assert.Equal(ImageFolder, Path.GetDirectoryName(Path.GetFullPath(resolved!)));
        Assert.Equal("t1", sheet.Id);
    }

    [Fact]
    public void SetImage_SheetIdWithPathSeparators_NeverWritesOutsideTheImageFolder()
    {
        // "..\zz_ab" is exactly 8 characters, so the old Id[..8] prefix was
        // the whole path, and the picture landed in the data folder itself.
        var store = LoadFrom("""[ { "Id": "..\\zz_ab", "Name": "MR-03", "Sections": [] } ]""");
        var sheet = Assert.Single(store.All);

        Assert.True(store.SetImage(sheet, Picture()));

        Assert.False(File.Exists(Path.Combine(AppPaths.Root, "zz_ab")));
        Assert.True(PlainFileName.IsSafeLeaf(sheet.ImageFileName));

        var resolved = store.ResolveImagePath(sheet);
        Assert.NotNull(resolved);
        Assert.Equal(ImageFolder, Path.GetDirectoryName(Path.GetFullPath(resolved!)));
    }

    [Fact]
    public void ImagePrefix_AppMadeId_IsItsFirstEight()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.Equal(id[..8], ChassisTemplateStore.ImagePrefix(id));
    }

    [Fact]
    public void ImagePrefix_PlantedOrShortId_IsEightLettersAndDigits()
    {
        foreach (var id in new string?[] { "t1", "..\\..\\ab", "C:\\x\\abc", "", null })
        {
            var prefix = ChassisTemplateStore.ImagePrefix(id);
            Assert.Equal(8, prefix.Length);
            Assert.All(prefix, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }
}
