using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the sound packs: a club's own file that is there but can't
/// be decoded (an .ogg picked through "All files", a download that isn't
/// audio) falls back to the pack or the generated tone, as a missing file
/// does. It used to be chosen anyway, and the effect was silent all night.
/// </summary>
public sealed class BugHunt17_SoundPacksTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string FileOf(string name, Action<string> write)
    {
        Directory.CreateDirectory(_data.Folder);
        var path = Path.Combine(_data.Folder, name);
        write(path);
        return path;
    }

    private static void WriteTone(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1));
        writer.Write(new byte[44100 / 5 * 2], 0, 44100 / 5 * 2);
    }

    private static void WriteRubbish(string path) =>
        File.WriteAllText(path, "This is a web page, not a sound. " + new string('x', 4000));

    [Theory]
    [InlineData("start.ogg")]
    [InlineData("start.wav")]
    public void A_file_that_cannot_be_decoded_gives_way_to_the_classic_sound(string name)
    {
        // The generated tone until 25 Nov 2026, when every sound got
        // Classic's file to fall back to (see SoundPacks.EffectsKey).
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = FileOf(name, WriteRubbish) };

        Assert.Equal(SoundPacks.FileFor(SoundPacks.EffectsKey, SoundEffectKind.GoodLap), SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
    }

    [Fact]
    public void A_file_that_cannot_be_decoded_gives_way_to_the_pack_it_is_set_to()
    {
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = FileOf("start.ogg", WriteRubbish), BuiltInSound = "Podium" };

        Assert.Equal(SoundPacks.FileFor("Podium", SoundEffectKind.GoodLap), SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
    }

    [Fact]
    public void A_file_that_plays_is_still_the_clubs_own()
    {
        var path = FileOf("start.wav", WriteTone);
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = path, BuiltInSound = "Podium" };

        Assert.Equal(path, SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));

        // Asked again, as every lap does: the same answer.
        Assert.Equal(path, SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
    }

    // Test and Test All on the Sounds page ask this, so they can say the
    // sound just heard was not the club's own.

    [Fact]
    public void A_file_that_cannot_be_decoded_is_named_as_passed_over()
    {
        var path = FileOf("start.ogg", WriteRubbish);
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = path, BuiltInSound = "Podium" };

        Assert.Equal(path, SoundPacks.OwnFilePassedOver(config, SoundEffectKind.GoodLap));

        // A file that isn't there is passed over as well.
        var gone = new SoundEffectConfig { Enabled = true, CustomFilePath = Path.Combine(_data.Folder, "gone.wav") };
        Assert.Equal(gone.CustomFilePath, SoundPacks.OwnFilePassedOver(gone, SoundEffectKind.GoodLap));
    }

    [Fact]
    public void A_file_that_plays_or_no_file_at_all_is_not_passed_over()
    {
        var own = new SoundEffectConfig { Enabled = true, CustomFilePath = FileOf("start.wav", WriteTone), BuiltInSound = "Podium" };

        Assert.Null(SoundPacks.OwnFilePassedOver(own, SoundEffectKind.GoodLap));
        Assert.Null(SoundPacks.OwnFilePassedOver(new SoundEffectConfig { Enabled = true, BuiltInSound = "Podium" }, SoundEffectKind.GoodLap));
        Assert.Null(SoundPacks.OwnFilePassedOver(null, SoundEffectKind.GoodLap));
    }

    [Fact]
    public void A_file_replaced_with_a_good_one_is_looked_at_again()
    {
        var path = FileOf("start.wav", WriteRubbish);
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = path };

        Assert.NotEqual(path, SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));

        WriteTone(path);

        Assert.Equal(path, SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
    }
}
