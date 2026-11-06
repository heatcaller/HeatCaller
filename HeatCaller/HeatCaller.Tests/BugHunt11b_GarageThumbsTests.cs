using System.Drawing;
using System.Drawing.Imaging;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b: the relay garage page's car thumbnails disposed the image
/// PhotoStore.Thumb hands back, which belongs to PhotoStore's cache - so the
/// next ask for the same photo at that size got a dead bitmap and the car
/// photo vanished from the page for the rest of the session.
/// </summary>
public sealed class BugHunt11b_GarageThumbsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static (PhotoStore Store, string Name) StoreWithOnePhoto()
    {
        var folderName = "bh11b-" + Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(AppPaths.Root, folderName);
        Directory.CreateDirectory(folder);

        var name = "car.png";

        using (var picture = new Bitmap(300, 200))
        {
            using (var g = Graphics.FromImage(picture))
            {
                g.Clear(Color.SteelBlue);
            }

            picture.Save(Path.Combine(folder, name), ImageFormat.Png);
        }

        return (new PhotoStore(folderName), name);
    }

    [Fact]
    public void A_garage_thumbnail_asked_for_again_after_the_memo_is_gone_still_comes_back()
    {
        var (store, name) = StoreWithOnePhoto();

        var first = new DriverGarage.Thumbs(store).Of(name);
        Assert.NotNull(first);

        // A fresh memo is what Of sees after it clears itself past 4000
        // entries: it asks PhotoStore again, and gets its cached image.
        var again = new DriverGarage.Thumbs(store).Of(name);
        Assert.NotNull(again);
        Assert.Equal(first, again);
    }

    [Fact]
    public void The_cached_thumbnail_is_still_usable_by_other_callers_after_the_garage_page_used_it()
    {
        var (store, name) = StoreWithOnePhoto();

        Assert.NotNull(new DriverGarage.Thumbs(store).Of(name));

        var image = store.Thumb(name, DriverGarage.ThumbSize);
        Assert.NotNull(image);
        Assert.Equal(DriverGarage.ThumbSize, image!.Width);

        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        Assert.True(stream.Length > 0);
    }
}
