namespace HeatCaller.Tests;

/// <summary>
/// Play Everything while muted, mid-race (13 Nov 2026, bug hunt 17).
///
/// The sidebar's mute stops the race's crowd and its unmute brings it back.
/// Play Everything used to note "no crowd running", start one of its own,
/// and only then unmute - so Stop Everything, Mute Everything or leaving the
/// page stopped that crowd as the audition's, and the race ran on without
/// one. It unmutes first now, so the crowd the unmute brings back is seen
/// as already running and left alone.
///
/// The audition needs a window and sound devices, so this reads the
/// source, as BugHuntMixerTests does for Mute Everything.
/// </summary>
public sealed class BugHunt17_MixerControlTests
{
    [Fact]
    public void Play_everything_unmutes_before_it_notes_what_was_already_running()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "MixerControl.cs"));

        var start = source.IndexOf("private void ToggleAudition()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ToggleAudition has moved - this test needs its bounds updated.");

        var end = source.IndexOf("public void SessionAdoptedAudio(", start, StringComparison.Ordinal);
        Assert.True(end > start, "SessionAdoptedAudio no longer follows ToggleAudition - this test needs its bounds updated.");

        var audition = source[start..end];

        var unmute = audition.IndexOf("SetAnnouncerMuted(false);", StringComparison.Ordinal);
        var snapshot = audition.IndexOf("_ambienceWasRunning = _ambience.IsRunning;", StringComparison.Ordinal);

        Assert.True(unmute >= 0, "Play Everything no longer unmutes.");
        Assert.True(snapshot >= 0, "Play Everything no longer notes whether the crowd was running.");
        Assert.True(unmute < snapshot, "Play Everything must unmute before it notes what was running.");
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
