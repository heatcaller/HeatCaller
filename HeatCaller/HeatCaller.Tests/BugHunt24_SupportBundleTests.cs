using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Save Support Info: the README inside the zip
/// no longer says every decoder capture came from the I-Lap.
/// </summary>
public sealed class BugHunt24_SupportBundleTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Now = new(2026, 9, 27, 21, 0, 0, DateTimeKind.Local);

    private static void WriteLog(string folder, string name, DateTime lastWrite, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text + "\r\n");
        File.SetLastWriteTime(path, lastWrite);
    }

    private string ReadmeFor(string decoderLogName)
    {
        var logFolder = Path.Combine(_data.Folder, "Logs");
        Directory.CreateDirectory(logFolder);
        WriteLog(logFolder, "console-2026-09-27_20-00-00.log", Now, "[race] Green flag - racing begins.");

        var dataFolder = Path.Combine(_data.Folder, "Data");
        Directory.CreateDirectory(dataFolder);
        WriteLog(dataFolder, decoderLogName, Now.AddHours(-1), "8E 02 33 00");

        var zipPath = Path.Combine(_data.Folder, "support.zip");

        SupportBundle.Write(new SupportBundle.Inputs
        {
            LogFolder = logFolder,
            CrashLogPaths = Array.Empty<string>(),
            DecoderLogFolder = dataFolder,
            Summary = string.Empty,
            Redactor = Redactor.None,
            Now = Now,
        }, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var readme = zip.GetEntry("README.txt");
        Assert.NotNull(readme);
        using var reader = new StreamReader(readme!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void BugHunt24_A_P3_capture_is_not_called_the_I_Lap_in_the_readme()
    {
        var readme = ReadmeFor("decoder-raw-p3-2026-09-27.log");

        Assert.Contains("Everything the decoder sent, one file a day: decoder-raw-p3-2026-09-27.log", readme);
        Assert.DoesNotContain("I-Lap", readme);
    }

    [Fact]
    public void BugHunt24_An_I_Lap_capture_is_still_listed_in_the_readme()
    {
        var readme = ReadmeFor("ilap-raw-2026-09-27.log");

        Assert.Contains("Everything the decoder sent, one file a day: ilap-raw-2026-09-27.log", readme);
    }
}
