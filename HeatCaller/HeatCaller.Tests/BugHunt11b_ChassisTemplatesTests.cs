namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b, chassis-templates.json: an explicit JSON null in a restored
/// or hand-edited file overrode the property initialisers, and AllFields,
/// Get and SetupField.Clone then threw NullReferenceException.
/// </summary>
public sealed class BugHunt11b_ChassisTemplatesTests : IDisposable
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

    private static void Exercise(ChassisTemplateStore store)
    {
        foreach (var template in store.All)
        {
            Assert.NotNull(store.Get(template.Id));

            foreach (var field in template.AllFields)
            {
                var copy = field.Clone();
                Assert.NotNull(copy.Options);
                Assert.DoesNotContain(null, copy.Options);
            }

            _ = template.DiagramFields.Count();
            _ = template.Clone();
        }

        Assert.Null(store.Get("not-there"));
    }

    [Fact]
    public void Null_sections_load_and_are_usable()
    {
        var store = LoadFrom("""[ { "Id": "t1", "Name": "MR-03", "Sections": null } ]""");

        Exercise(store);
        var template = Assert.Single(store.All);
        Assert.Equal("t1", template.Id);
        Assert.Empty(template.Sections);
    }

    [Fact]
    public void Null_fields_load_and_are_usable()
    {
        var store = LoadFrom("""[ { "Id": "t1", "Name": "MR-03", "Sections": [ { "Id": "s1", "Name": "Front", "Fields": null } ] } ]""");

        Exercise(store);
        Assert.Empty(Assert.Single(Assert.Single(store.All).Sections).Fields);
    }

    [Fact]
    public void Null_options_and_strings_load_and_are_usable()
    {
        var store = LoadFrom("""
            [ { "Id": "t1", "Name": null, "Sections": [ { "Id": "s1", "Name": null, "Fields": [
                { "Id": "f1", "Label": null, "Unit": null, "Options": null },
                { "Id": "f2", "Label": "Spring", "Options": [ "Soft", null, "Hard" ] } ] } ] } ]
            """);

        Exercise(store);
        var template = Assert.Single(store.All);
        Assert.Equal(string.Empty, template.Name);
        var fields = template.AllFields.ToList();
        Assert.Equal(new[] { "f1", "f2" }, fields.Select(f => f.Id));
        Assert.Empty(fields[0].Options);
        Assert.Equal(string.Empty, fields[0].Label);
        Assert.Equal(string.Empty, fields[0].Unit);
        Assert.Equal(new[] { "Soft", "Hard" }, fields[1].Options);
    }

    [Fact]
    public void Null_elements_are_dropped_and_missing_ids_given_new_ones()
    {
        var store = LoadFrom("""
            [ null,
              { "Id": null, "Name": "No id", "Sections": [ null,
                  { "Id": null, "Name": "S", "Fields": [ null, { "Id": null, "Label": "L" } ] } ] },
              { "Id": "keep", "Name": "Kept", "Sections": [] } ]
            """);

        Exercise(store);
        Assert.Equal(2, store.All.Count);
        var repaired = store.All[0];
        Assert.False(string.IsNullOrWhiteSpace(repaired.Id));
        var section = Assert.Single(repaired.Sections);
        Assert.False(string.IsNullOrWhiteSpace(section.Id));
        var field = Assert.Single(section.Fields);
        Assert.False(string.IsNullOrWhiteSpace(field.Id));
        Assert.Equal("L", field.Label);
        Assert.Same(store.All[1], store.Get("keep"));
    }

    [Fact]
    public void A_file_of_only_null_templates_falls_back_to_the_seeded_default()
    {
        var store = LoadFrom("[ null, null ]");

        Exercise(store);
        Assert.Equal(ChassisTemplate.CreateKyoshoDefault().Name, Assert.Single(store.All).Name);
    }

    [Fact]
    public void A_good_file_loads_unchanged()
    {
        var original = ChassisTemplate.CreateKyoshoDefault();
        original.ImageFileName = "mr03.png";
        var json = System.Text.Json.JsonSerializer.Serialize(new List<ChassisTemplate> { original });

        var store = LoadFrom(json);

        Exercise(store);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(store.All));
    }
}
