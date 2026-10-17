using System.Drawing;
using System.Drawing.Imaging;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, drivers and garage: a setup log with nulls in it, a stored
/// driver photo that can't be decoded, and a chassis diagram saved as .jfif.
/// </summary>
public sealed class BugHunt8FITests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- A27-2: setup log nulls repaired on load ---------------------------

    [Fact]
    public void A_setup_log_with_null_entries_and_fields_loads_repaired()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(
            _data.PathOf("setup-log.json"),
            "[null, {\"Id\":\"a\",\"DriverName\":null,\"Values\":null,\"CarModel\":null}]");

        var store = SetupLogStore.Load();

        Assert.Single(store.All);
        var entry = store.Get("a");
        Assert.NotNull(entry);
        Assert.Empty(entry!.Values);
        Assert.Equal(string.Empty, entry.DriverName);
        Assert.Equal(string.Empty, entry.CarModel);
        Assert.True(entry.IsLegacy);
        Assert.Equal(0, entry.FilledCount);
    }

    // --- A27-5: an undecodable stored photo is refused once ----------------

    [Fact]
    public void A_stored_photo_that_will_not_decode_is_refused_rather_than_retried()
    {
        var folder = Path.Combine(AppPaths.Root, "DriverPhotos");
        Directory.CreateDirectory(folder);
        var name = "bh8-" + Guid.NewGuid().ToString("N")[..8] + ".png";
        var path = Path.Combine(folder, name);

        // A PNG signature and an IHDR claiming 10 x 10, then garbage: through
        // the header check, but not something GDI+ can decode.
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 10, 0, 0, 0, 10 };
        bytes.AddRange(Enumerable.Repeat((byte)0x5A, 64));
        File.WriteAllBytes(path, bytes.ToArray());

        Assert.Null(DriverPhotos.Thumb(name, 32));

        // Had it not been refused, a good picture under the same name would
        // now be decoded. It is not even read again.
        using (var good = new Bitmap(10, 10))
        {
            good.Save(path, ImageFormat.Png);
        }

        Assert.Null(DriverPhotos.Thumb(name, 32));
    }

    // --- A27-6: a JPEG named .jfif is taken as a chassis diagram -----------

    [Fact]
    public void SetImage_takes_a_jfif_file()
    {
        var store = ChassisTemplateStore.Load();
        var sheet = store.All[0];

        Directory.CreateDirectory(_data.Folder);
        var source = Path.Combine(_data.Folder, "diagram.jfif");
        File.WriteAllBytes(source, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });

        Assert.True(store.SetImage(sheet, source));
        Assert.EndsWith(".jfif", sheet.ImageFileName, StringComparison.OrdinalIgnoreCase);
    }
}
