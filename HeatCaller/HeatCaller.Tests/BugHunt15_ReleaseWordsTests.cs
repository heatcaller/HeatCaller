using System.Collections;
using System.Reflection;
using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B9: a prepared release clip was kept, and played, on its
/// voice alone. One primed for transponder 1234 as "Sam Smith, go!" and
/// never taken (the race reset first) was still played at the next
/// staggered green after the desk fixed the Announcer name or handed the
/// club transponder to Bill. The clip now carries the driver's words and is
/// dropped when they have changed.
/// </summary>
public class BugHunt15_ReleaseWordsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

    private static object? Take(AnnouncementService announcer, string transponder, AnnouncementEventConfig config) =>
        typeof(AnnouncementService).GetMethod("TakePreparedRelease", Private)!.Invoke(announcer, new object[] { transponder, config });

    private static void Prime(AnnouncementService announcer, VoiceProfile profile, AnnouncementEventConfig config, string transponder, string text)
    {
        var signature = (string)typeof(AnnouncementService).GetMethod("VoiceSignature", Private)!.Invoke(null, new object[] { profile, config })!;
        var words = (string)typeof(AnnouncementService).GetMethod("ReleaseWordsFor", Private)!.Invoke(announcer, new object[] { transponder })!;
        var clipType = typeof(AnnouncementService).GetNestedType("PreparedClip", BindingFlags.NonPublic)!;
        var sourceType = typeof(AnnouncementService).GetNestedType("LineSource", BindingFlags.NonPublic)!;

        var clip = Activator.CreateInstance(clipType,
            text, new byte[16], new WaveFormat(24000, 16, 1), signature, Enum.Parse(sourceType, "ElevenLabs"), words)!;

        var lines = (IDictionary)typeof(AnnouncementService).GetField("_releaseLines", Private)!.GetValue(announcer)!;
        lines[transponder] = clip;
    }

    [Fact]
    public void A_release_clip_prepared_for_another_driver_or_name_is_not_played()
    {
        using var data = new TempAppData();

        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "1234", DriverName = "Sam Smith" });

        var voice = VoiceSettingsStore.Load();
        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load());
        var config = voice.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.DriverRelease))
                     ?? new AnnouncementEventConfig();

        // Still the same driver: the prepared clip plays.
        Prime(announcer, voice.Current, config, "1234", "Sam Smith, go!");
        Assert.NotNull(Take(announcer, "1234", config));

        // Primed, never taken - then the transponder goes to Bill.
        Prime(announcer, voice.Current, config, "1234", "Sam Smith, go!");
        profiles.Upsert(new DriverProfile { Transponder = "1234", DriverName = "Bill Jones" });

        Assert.Null(Take(announcer, "1234", config));

        // And thrown away, not left to be found again.
        var lines = (IDictionary)typeof(AnnouncementService).GetField("_releaseLines", Private)!.GetValue(announcer)!;
        Assert.False(lines.Contains("1234"));
    }
}
