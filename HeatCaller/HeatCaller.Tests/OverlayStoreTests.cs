namespace HeatCaller.Tests;

/// <summary>
/// The club's own stream overlays (14 Nov 2026, plans/obs-overlay-plan.md
/// section 4, Asher: "the ability to roll your own"). See OverlayStore and
/// OverlayEditorForm.
/// </summary>
public sealed class OverlayStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "hc-overlays-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
            // A test machine's temp folder.
        }
    }

    [Fact]
    public void A_copy_of_a_preset_is_saved_and_read_back_under_an_address_of_its_own()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "Friday Night Stream!");

        Assert.Matches("^friday-night-stream-[0-9a-f]{4}$", mine.Id);
        Assert.Null(OverlayLayouts.BuiltIn(mine.Id));

        var again = OverlayStore.Load(_path).Get(mine.Id)!;
        Assert.Equal("Friday Night Stream!", again.Name);
        Assert.Equal(OverlayLayouts.BuiltIn("corner")!.Elements.Count, again.Elements.Count);
    }

    [Fact]
    public void Renaming_keeps_the_address_so_an_obs_source_keeps_working()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "First name");

        var renamed = store.Get(mine.Id)!;
        renamed.Name = "Second name";
        store.Update(renamed);

        Assert.Equal("Second name", store.Get(mine.Id)!.Name);
        Assert.Single(store.Layouts);
    }

    [Fact]
    public void Every_change_moves_the_version_an_open_page_watches()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "Mine");
        var before = store.Version(mine.Id);

        var changed = store.Get(mine.Id)!;
        changed.Element(OverlayElementKind.Bug)!.Anchor = OverlayAnchor.BottomLeft;
        store.Update(changed);

        Assert.True(store.Version(mine.Id) > before);
        Assert.Equal(OverlayAnchor.BottomLeft, store.Get(mine.Id)!.Element(OverlayElementKind.Bug)!.Anchor);

        store.Remove(mine.Id);
        Assert.Null(store.Get(mine.Id));
        Assert.True(store.Version(mine.Id) > before + 1, "a deleted layout's page reloads to say it has gone");
    }

    [Fact]
    public void What_the_store_hands_out_is_a_copy_the_server_can_read_while_the_editor_changes_it()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "Mine");

        var held = store.Get(mine.Id)!;
        held.Name = "changed without saying so";

        Assert.Equal("Mine", store.Get(mine.Id)!.Name);
    }

    [Fact]
    public void The_mark_is_always_on_and_values_are_kept_in_range()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "Mine");

        var wild = store.Get(mine.Id)!;
        wild.Element(OverlayElementKind.Mark)!.On = false;
        wild.Element(OverlayElementKind.Bug)!.X = 900;
        wild.Element(OverlayElementKind.Bug)!.Scale = 0.01;
        wild.Glass = 5;
        store.Update(wild);

        var kept = store.Get(mine.Id)!;
        Assert.True(kept.Element(OverlayElementKind.Mark)!.On);
        Assert.Equal(50, kept.Element(OverlayElementKind.Bug)!.X);
        Assert.Equal(0.5, kept.Element(OverlayElementKind.Bug)!.Scale);
        Assert.Equal(1, kept.Glass);
    }

    [Fact]
    public void A_broken_file_leaves_the_presets_working_and_is_copied_aside_before_anything_replaces_it()
    {
        File.WriteAllText(_path, "{ this is not json");

        var store = OverlayStore.Load(_path);

        Assert.Empty(store.Layouts);
        Assert.NotEqual(string.Empty, store.Problem);

        // The app's rule (SafeFile.HoldWritesAfterUnreadable): damaged
        // content copied aside may be started afresh; a file that could not
        // be read at all, or copied, is never written over.
        var aside = Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*")
            .Where(p => p != _path)
            .ToList();
        Assert.True(store.LoadedUnreadable || aside.Any(p => File.ReadAllText(p) == "{ this is not json"));

        foreach (var p in aside)
        {
            File.Delete(p);
        }
    }

    [Fact]
    public void A_layout_with_a_preset_s_address_or_a_made_up_one_is_not_loaded()
    {
        File.WriteAllText(_path, "[{\"Id\":\"broadcast\",\"Name\":\"Sneaky\"},{\"Id\":\"../../x\",\"Name\":\"Bad\"},{\"Id\":\"fine-name-1a2b\",\"Name\":\"Fine\",\"Elements\":[{\"Kind\":\"Bug\"}]}]");

        var store = OverlayStore.Load(_path);

        Assert.Equal(new[] { "fine-name-1a2b" }, store.Layouts.Select(l => l.Id));
        // The mark is put back on a layout that had lost it.
        Assert.True(store.Get("fine-name-1a2b")!.Shows(OverlayElementKind.Mark));
    }

    [Fact]
    public void A_graphic_this_version_does_not_know_is_a_file_it_cannot_read_not_a_crash()
    {
        File.WriteAllText(_path, "[{\"Id\":\"fine-name-1a2b\",\"Name\":\"Fine\",\"Elements\":[{\"Kind\":\"Hologram\"}]}]");

        var store = OverlayStore.Load(_path);

        Assert.Empty(store.Layouts);
        Assert.NotEqual(string.Empty, store.Problem);
    }

    [Fact]
    public void Copying_the_results_table_gives_the_broadcast_graphics_to_start_from()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn(OverlayLayouts.ResultsId)!, "Mine");

        Assert.True(mine.Shows(OverlayElementKind.Tower));
    }

    [Theory]
    [InlineData("fine-name-1a2b", true)]
    [InlineData("broadcast", false)]
    [InlineData("../x-1a2b", false)]
    [InlineData("UPPER-1a2b", false)]
    [InlineData("", false)]
    public void Only_an_address_the_store_makes_is_a_custom_one(string id, bool expected) =>
        Assert.Equal(expected, OverlayStore.IsCustomId(id));

    [Fact]
    public void A_club_s_own_layout_is_drawn_and_watches_for_its_own_changes()
    {
        var store = OverlayStore.Load(_path);
        var mine = store.AddCopy(OverlayLayouts.BuiltIn("corner")!, "Mine");
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey })
        {
            CustomOverlay = store.Get,
            CustomOverlayVersion = store.Version,
        };

        var page = server.BuildBroadcastPage(server.OverlayLayoutFor(mine.Id));

        Assert.Contains("\"custom\":true", page);
        Assert.Contains("'/api/overlay-version?id=' + encodeURIComponent(LAYOUT.id)", page);
        Assert.Contains("\"custom\":false", server.BuildBroadcastPage());
    }

    [Fact]
    public void Every_graphic_has_words_a_club_would_use()
    {
        foreach (var kind in Enum.GetValues<OverlayElementKind>())
        {
            Assert.NotEqual(kind.ToString(), OverlayEditorForm.KindWords(kind));
        }
    }
}
