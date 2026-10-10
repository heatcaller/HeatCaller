using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// The audio services' lock (bug hunt 5, 9 Oct 2026).
///
/// Both services opened and closed their sound device while holding the lock
/// the UI thread polls several times a second. MusicPlayer.AdvanceTrack runs
/// on the thread pool - the next track at the natural end of one, and the
/// retry after a file that would not play - and held _lock across
/// AudioFileReader, new WaveOutEvent, Init and Play, while the UI thread read
/// IsPlaying from the Music page and from the end of every broadcast.
/// AmbienceService.Stop runs on the thread pool at the end of a session's
/// fade and held _lock across waveOutReset and waveOutClose, while the UI
/// thread read IsRunning on every change of race state. AudioBus says what
/// opening an output device costs: it exists as one warm shared output
/// because every line and effect opening its own made the music skip.
///
/// Now the device is opened, started and disposed with the lock let go, and
/// the ready output is published under it. The risk that creates is two moves
/// opening a device at once, so each takes a claim (_loadToken) and only the
/// one still holding it publishes; the other stops and disposes what it
/// built, because two outputs both playing would be two tracks at once for
/// the rest of the evening.
///
/// What these tests cannot do is open a device, so the stall itself is not
/// reproducible here - the same bind InstallerTests is in, and the same
/// answer: the shape is pinned by reading the source. Everything else is
/// driven through files that cannot be opened as audio, so no sound device is
/// ever touched and no track ever plays.
/// </summary>
public sealed class BugHunt5AudioLockTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The device calls are outside the lock ------------------------------

    [Fact]
    public void The_music_opens_its_file_and_its_sound_device_with_the_lock_let_go()
    {
        var source = SourceOf("MusicPlayer.cs");

        // Still there to be found - otherwise the checks below would pass on
        // a file that had simply stopped opening a device at all.
        Assert.Contains("new WaveOutEvent", source, StringComparison.Ordinal);
        Assert.Contains("new AudioFileReader", source, StringComparison.Ordinal);

        var locked = LockedSectionOf(source, "private void AdvanceTrack(");

        Assert.DoesNotContain("new WaveOutEvent", locked, StringComparison.Ordinal);
        Assert.DoesNotContain("new AudioFileReader", locked, StringComparison.Ordinal);
        Assert.DoesNotContain(".Init(", locked, StringComparison.Ordinal);
    }

    [Fact]
    public void The_music_lets_the_old_track_go_with_the_lock_let_go()
    {
        var source = SourceOf("MusicPlayer.cs");
        var locked = LockedSectionOf(source, "private void AdvanceTrack(");

        // waveOutReset and waveOutClose, which is the other half of the
        // stall: the move to the next track disposes the device the last one
        // played out of. It is taken out of the fields under the lock and
        // disposed after it (TakePlaybackLocked, then DisposeRetired).
        Assert.DoesNotContain(".Dispose()", locked, StringComparison.Ordinal);
        Assert.Contains("TakePlaybackLocked(out retiredOutput, out retiredReader);", locked, StringComparison.Ordinal);
    }

    [Fact]
    public void The_crowd_lets_its_device_go_with_the_lock_let_go()
    {
        var source = SourceOf("AmbienceService.cs");
        var locked = LockedSectionOf(source, "public void Stop()");

        Assert.DoesNotContain(".Dispose()", locked, StringComparison.Ordinal);
        Assert.Contains("TakeAudioLocked(out retiredOutput, out retiredLoops);", locked, StringComparison.Ordinal);

        // The loops hold the open audio files, and they must not be closed
        // until the device reading them has stopped - so they come out of
        // the fields with the output and are disposed after it, not under
        // the lock on their own.
        Assert.Contains("DisposeRetired(retiredOutput, retiredLoops);", source, StringComparison.Ordinal);
    }

    // --- A move that cannot open its track ----------------------------------

    [Fact]
    public void A_playlist_whose_files_will_not_play_tries_each_one_once_and_then_stops()
    {
        // The whole failing path now runs with the lock let go and comes back
        // for it: the open throws, the track is dropped from the pass, and
        // the retry for the next one is set going under the same generation.
        // Each track must be tried exactly once - a move that advanced twice
        // would be the shape that leaves two outputs playing when the files
        // are real.
        var messages = new ConcurrentQueue<string>();
        var announced = new ConcurrentQueue<MusicTrack?>();

        using var player = new MusicPlayer { Settings = SettingsWithUnplayableTracks("Alpha", "Bravo", "Charlie") };
        player.StatusMessage += messages.Enqueue;
        player.NowPlayingChanged += announced.Enqueue;

        player.Play();

        Assert.True(
            WaitUntil(() => messages.Any(m => m.Contains("Stopped - none of the tracks", StringComparison.Ordinal))),
            "The playlist never came to a stop: " + string.Join(" | ", messages));

        Assert.False(player.IsPlaying);
        Assert.Null(player.NowPlaying);

        var tried = messages.Where(m => m.Contains("Couldn't play", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, tried.Count);
        Assert.Equal(1, tried.Count(m => m.Contains("\"Alpha\"", StringComparison.Ordinal)));
        Assert.Equal(1, tried.Count(m => m.Contains("\"Bravo\"", StringComparison.Ordinal)));
        Assert.Equal(1, tried.Count(m => m.Contains("\"Charlie\"", StringComparison.Ordinal)));

        // The Music page is told once that nothing is on, not once per
        // failed track.
        Assert.Single(announced);
        Assert.True(announced.TryPeek(out var last));
        Assert.Null(last);
    }

    [Fact]
    public void A_player_with_nothing_to_play_never_says_it_is_playing()
    {
        var messages = new ConcurrentQueue<string>();

        using var player = new MusicPlayer();
        player.StatusMessage += messages.Enqueue;

        Assert.False(player.IsPlaying);

        player.Play();

        Assert.False(player.IsPlaying);
        Assert.Null(player.NowPlaying);
        Assert.Contains(messages, m => m.Contains("Nothing to play", StringComparison.Ordinal));
    }

    // --- The state query, while a move is in flight -------------------------

    [Fact]
    public void Reading_what_the_music_is_doing_from_another_thread_never_hangs_or_throws()
    {
        // IsPlaying is what the UI thread polls, and it is the one that used
        // to wait out a device open on the thread pool. It cannot be made
        // slow here, but it can be read flat out while moves are running, to
        // show that nothing in the new shape deadlocks or sees a half-built
        // track.
        using var player = new MusicPlayer { Settings = SettingsWithUnplayableTracks("Alpha", "Bravo", "Charlie") };

        var stop = false;
        Exception? failure = null;

        var reading = Task.Run(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    _ = player.IsPlaying;
                    _ = player.IsPaused;
                    _ = player.NowPlaying;
                    _ = player.CurrentPosition;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        for (var i = 0; i < 200; i++)
        {
            player.Play();
            player.Pause();
            player.Resume();
            player.Stop();
        }

        Volatile.Write(ref stop, true);

        // xUnit1031 wants this awaited. The reader is a pool thread
        // hammering the player's state while this one drives it, and the
        // test is the pair running at once - so this waits for it rather
        // than handing its own continuation to the scheduler. No
        // synchronisation context under xunit, so no deadlock to guard.
#pragma warning disable xUnit1031
        Assert.True(reading.Wait(TimeSpan.FromSeconds(30)), "Reading the player's state did not come back.");
#pragma warning restore xUnit1031
        Assert.Null(failure);
    }

    [Fact]
    public void A_stop_in_the_middle_of_a_move_leaves_the_player_stopped()
    {
        // Stop bumps the generation, so a move that is part-way through -
        // which now includes one with the lock let go, opening a file - finds
        // itself no longer the move being played and lets go of everything it
        // built. What it must never do is leave a flag up that has IsPlaying
        // saying the playlist is still on with nothing to stop it.
        using var player = new MusicPlayer { Settings = SettingsWithUnplayableTracks("Alpha", "Bravo", "Charlie") };

        for (var i = 0; i < 50; i++)
        {
            player.Play();
            player.Stop();
        }

        Assert.True(WaitUntil(() => !player.IsPlaying), "The player still says it is playing after a Stop.");
        Assert.Null(player.NowPlaying);
    }

    // --- The crowd ----------------------------------------------------------

    [Fact]
    public void The_crowd_can_be_stopped_and_disposed_when_it_was_never_started()
    {
        // Stop now takes the bed out of the fields under the lock and lets it
        // go after, so the "nothing is running" return has to come first or
        // it would hand DisposeRetired a bed that is still in use. With
        // nothing configured there is no bed at all, which is the case that
        // gets this wrong loudest.
        using var crowd = new AmbienceService();

        Assert.False(crowd.IsRunning);

        crowd.Stop();
        crowd.Stop();

        Assert.False(crowd.IsRunning);
    }

    [Fact]
    public void The_crowd_with_nothing_configured_never_says_it_is_running()
    {
        var messages = new ConcurrentQueue<string>();

        using var crowd = new AmbienceService();
        crowd.StatusMessage += messages.Enqueue;

        crowd.Start(ignoreEnabledSetting: true);

        // No layer has a file, so no device is opened and nothing is running
        // - the only state in which the rest of these tests are safe to run
        // on a machine with no speaker.
        Assert.False(crowd.IsRunning);

        crowd.Stop();

        Assert.False(crowd.IsRunning);
        Assert.Contains(messages, m => m.Contains("Nothing to play", StringComparison.Ordinal));
    }

    // --- Helpers ------------------------------------------------------------

    /// <summary>
    /// A library of tracks whose files exist but are not audio, so the open
    /// throws before any sound device is asked for. That is the only way to
    /// drive a real move through the player in a test: a track that plays
    /// would need a speaker.
    /// </summary>
    private MusicSettings SettingsWithUnplayableTracks(params string[] titles)
    {
        var settings = new MusicSettings { Volume = 0.4f, Shuffle = false };

        foreach (var title in titles)
        {
            // .wav, so NAudio's own WaveFileReader is what refuses it ("no
            // RIFF header") rather than a Windows codec - nothing outside
            // the process is involved.
            var path = Path.Combine(_data.Folder, title + ".wav");
            File.WriteAllText(path, "this is not a wave file");

            settings.Tracks.Add(new MusicTrack { Title = title, FilePath = path, Enabled = true });
        }

        return settings;
    }

    /// <summary>
    /// The app's copy of one of its source files, found by walking up from
    /// the test binary. Read rather than reproduced, because the thing being
    /// checked is where a device call sits relative to a lock, and the
    /// alternative is opening a sound device in a unit test (see
    /// InstallerTests, which reads sources for the same kind of reason).
    /// </summary>
    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// The text of the first "lock (_lock)" block inside the named method,
    /// braces matched from the one that opens it.
    ///
    /// Not a parser: it counts braces and nothing else, which is enough for
    /// the two methods it is pointed at because every brace in them is part
    /// of a matched pair - the interpolated strings' holes included. A
    /// comment with a lone brace in it, added later, would throw this off,
    /// and the test would say so rather than quietly pass.
    /// </summary>
    private static string LockedSectionOf(string source, string methodSignature)
    {
        var method = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(method >= 0, $"{methodSignature} is not in the source any more.");

        var taken = source.IndexOf("lock (_lock)", method, StringComparison.Ordinal);
        Assert.True(taken >= 0, $"No lock block after {methodSignature}.");

        var open = source.IndexOf('{', taken);
        Assert.True(open >= 0, $"The lock block after {methodSignature} has no body.");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"The lock block after {methodSignature} never closes.");
    }

    /// <summary>
    /// Waits for something the thread pool will get to - the retry after a
    /// track that would not play. Generous, because a loaded build agent is
    /// not a race night.
    /// </summary>
    private static bool WaitUntil(Func<bool> condition, int seconds = 10)
    {
        var deadline = Environment.TickCount64 + seconds * 1000L;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }
}
