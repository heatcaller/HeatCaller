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
    public void A_file_that_cannot_be_decoded_gives_way_to_the_generated_tone(string name)
    {
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = FileOf(name, WriteRubbish) };

        Assert.Null(SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
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

    [Fact]
    public void A_file_replaced_with_a_good_one_is_looked_at_again()
    {
        var path = FileOf("start.wav", WriteRubbish);
        var config = new SoundEffectConfig { Enabled = true, CustomFilePath = path };

        Assert.Null(SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));

        WriteTone(path);

        Assert.Equal(path, SoundPacks.ResolveFile(config, SoundEffectKind.GoodLap));
    }
}
