namespace HeatCaller.Tests;

/// <summary>
/// The recap show with no voice (6 Nov 2026, Asher: "there was no kokoro
/// voice for the recap show last run"). A recap is one line of 1,500
/// characters or so, and Kokoro had 30 s for any line: the render in advance
/// ran out of time, so did the live one, and the recap host never spoke.
///
/// - A long line rendered ahead gets the time its length needs; a line in
///   the voice queue keeps the flat 30 s, as it holds the voice loop.
/// - A render given up on keeps Kokoro to itself until it really stops, so
///   the next line is not rendered beside it - both slower for it.
/// - A line rendered ahead waits for the line before it as long as that one
///   is allowed, not a flat 40 s.
/// </summary>
public sealed class KokoroLongLineTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose()
    {
        KokoroSpeech.ModelForTests = null;
        KokoroSpeech.RenderTimeoutForTests = null;
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        _data.Dispose();
    }

    [Fact]
    public void A_recap_rendered_ahead_gets_far_longer_than_a_race_call()
    {
        var recap = new string('a', 1500);

        Assert.True(KokoroSpeech.RenderTimeoutFor("Green flag!", renderingAhead: true) < TimeSpan.FromSeconds(32));
        Assert.True(KokoroSpeech.RenderTimeoutFor(recap, renderingAhead: true) >= TimeSpan.FromMinutes(2));

        // In the voice queue it holds every call behind it, so it does not.
        Assert.Equal(TimeSpan.FromSeconds(30), KokoroSpeech.RenderTimeoutFor(recap, renderingAhead: false));

        // And never more than the most, however long.
        Assert.Equal(TimeSpan.FromMinutes(5), KokoroSpeech.RenderTimeoutFor(new string('a', 100_000), renderingAhead: true));
    }

    [Fact]
    public async Task A_render_given_up_on_keeps_Kokoro_until_it_really_stops()
    {
        FakeDownloadedModel();
        KokoroSpeech.RenderTimeoutForTests = TimeSpan.FromMilliseconds(200);

        var stuck = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = "The recap " + Guid.NewGuid().ToString("N");

        KokoroSpeech.ModelForTests = (
            name => name,
            (line, _, _) =>
            {
                if (line == first)
                {
                    return stuck.Task;
                }

                secondStarted.TrySetResult();
                return Task.FromResult(new byte[9600]);
            });

        var timedOut = await Assert.ThrowsAsync<InvalidOperationException>(
            () => KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), first, renderingAhead: true));
        Assert.Contains("took longer than", timedOut.Message);

        // The first is still rendering: the next line waits for it.
        var next = KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), "Sign-off " + Guid.NewGuid().ToString("N"), renderingAhead: true);
        await Task.Delay(300);
        Assert.False(secondStarted.Task.IsCompleted);

        // Once it stops, the next has Kokoro to itself.
        stuck.SetResult(new byte[9600]);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await next.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_line_rendered_ahead_waits_behind_a_long_render_still_allowed_to_finish()
    {
        FakeDownloadedModel();

        var recapDone = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recapStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recap = "The recap " + new string('x', 1500) + Guid.NewGuid().ToString("N");

        KokoroSpeech.ModelForTests = (
            name => name,
            (line, _, _) =>
            {
                if (line == recap)
                {
                    recapStarted.TrySetResult();
                    return recapDone.Task;
                }

                return Task.FromResult(new byte[9600]);
            });

        var recapRender = KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), recap, renderingAhead: true);
        await recapStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var interview = KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), "Interview " + Guid.NewGuid().ToString("N"), renderingAhead: true);
        await Task.Delay(300);
        Assert.False(interview.IsCompleted);

        recapDone.SetResult(new byte[9600]);
        await recapRender.WaitAsync(TimeSpan.FromSeconds(10));
        await interview.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>A file the size of the model where the model goes - see BugHunt4VoiceTests.</summary>
    private static void FakeDownloadedModel()
    {
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        Directory.CreateDirectory(KokoroSpeech.Folder);

        using var file = new FileStream(KokoroSpeech.ModelPath, FileMode.Create, FileAccess.Write);
        file.SetLength(60L * 1024 * 1024);
    }
}
