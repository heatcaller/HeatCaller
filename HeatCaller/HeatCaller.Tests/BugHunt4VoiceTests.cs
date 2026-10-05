using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The Kokoro voice, around the model rather than the model itself - the
/// 310 MB model can't be loaded in a test, so KokoroSpeech.ModelForTests
/// stands in for it and KokoroSpeech.Http serves a made-up download.
///
/// - A line's voice is found and the line said on a pool thread, not on the
///   caller's: the voice queue and the Test buttons call from the UI thread,
///   and the first line of the night froze the window while Kokoro loaded
///   its voices and pronunciation dictionary.
/// - A download that is canceled or fails leaves no .part file behind.
/// - The download belongs to KokoroSpeech, not the Voice Settings page, so a
///   rebuilt page can follow and cancel it, and a second Download joins it.
/// - A model that wouldn't load is offered for download again, and stays
///   "can't speak" between tries.
/// </summary>
public sealed class BugHunt4VoiceTests : IDisposable
{
    /// <summary>KokoroSpeech's smallest real model, plus a byte.</summary>
    private const long WholeModel = 50L * 1024 * 1024 + 1;

    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = KokoroSpeech.Http;

    public void Dispose()
    {
        KokoroSpeech.CancelDownload();

        try
        {
            KokoroSpeech.RunningDownload?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Canceled - as asked.
        }

        KokoroSpeech.ModelForTests = null;
        KokoroSpeech.Http = _realHttp;
        KokoroSpeech.RecordLoadFailureForTests(null, default);
        _data.Dispose();
    }

    // --- voice-1: off the UI thread -------------------------------------------

    [Fact]
    public async Task A_Kokoro_line_finds_its_voice_and_is_said_on_the_pool_not_the_calling_thread()
    {
        FakeDownloadedModel();

        bool? foundOnPool = null;
        bool? saidOnPool = null;
        var said = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        KokoroSpeech.ModelForTests = (
            name =>
            {
                foundOnPool = Thread.CurrentThread.IsThreadPoolThread;
                return name;
            },
            (_, _, _) =>
            {
                saidOnPool = Thread.CurrentThread.IsThreadPoolThread;
                said.TrySetResult();
                return finish.Task;
            });

        // As the voice queue and the Test buttons call it: from a thread of
        // their own (the UI thread), with the model loaded and the gate free.
        Task<byte[]>? render = null;
        var caller = new Thread(() => render = KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), "Grid call " + Guid.NewGuid().ToString("N")));
        caller.Start();
        caller.Join();

        Assert.NotNull(render);
        Assert.False(render.IsCompleted);

        await said.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(foundOnPool);
        Assert.True(saidOnPool);

        finish.SetResult(new byte[9600]);
        var wav = await render.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
    }

    [Fact]
    public async Task A_Kokoro_voice_that_cannot_be_found_off_the_UI_thread_still_says_why()
    {
        FakeDownloadedModel();

        KokoroSpeech.ModelForTests = (
            _ => throw new InvalidOperationException("Kokoro's voices folder is missing from beside HeatCaller.exe - reinstall HeatCaller."),
            (_, _, _) => Task.FromResult(new byte[9600]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => KokoroSpeech.RenderAsync(VoiceProfile.CreateDefault(), "Lap time " + Guid.NewGuid().ToString("N")));

        Assert.StartsWith("Kokoro's voices folder is missing", ex.Message);
    }

    // --- voice-2: no .part left behind ----------------------------------------

    [Fact]
    public async Task A_Kokoro_download_cut_off_part_way_leaves_no_part_file()
    {
        var source = new FakeModelStream(WholeModel, failAt: 1024 * 1024);

        await Assert.ThrowsAsync<IOException>(() => KokoroSpeech.SaveModelAsync(source, WholeModel, null, CancellationToken.None));

        Assert.False(File.Exists(KokoroSpeech.ModelPath + ".part"));
        Assert.False(File.Exists(KokoroSpeech.ModelPath));
    }

    [Fact]
    public async Task A_canceled_Kokoro_download_leaves_no_part_file()
    {
        using var cancel = new CancellationTokenSource();
        var source = new FakeModelStream(long.MaxValue);

        // Canceled as soon as the first piece is written, as Cancel Download does.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => KokoroSpeech.SaveModelAsync(source, WholeModel, new ReportsAtOnce(_ => cancel.Cancel()), cancel.Token));

        Assert.False(File.Exists(KokoroSpeech.ModelPath + ".part"));
        Assert.False(File.Exists(KokoroSpeech.ModelPath));
    }

    [Fact]
    public void A_Kokoro_download_left_when_HeatCaller_was_closed_is_deleted_at_startup()
    {
        Directory.CreateDirectory(KokoroSpeech.Folder);
        File.WriteAllBytes(KokoroSpeech.ModelPath + ".part", new byte[4096]);

        KokoroSpeech.DeleteLeftoverDownload();

        Assert.False(File.Exists(KokoroSpeech.ModelPath + ".part"));
    }

    // --- voice-3: one download, whoever shows it ------------------------------

    [Fact]
    public async Task A_second_Kokoro_download_joins_the_one_running_and_cancel_stops_it()
    {
        var source = new FakeModelStream(WholeModel, stallAt: 81920);
        KokoroSpeech.Http = new HttpClient(new Serves(source, WholeModel));

        var first = KokoroSpeech.DownloadModelAsync();
        await source.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Download pressed on a page rebuilt while the first runs.
        var second = KokoroSpeech.DownloadModelAsync();

        Assert.Same(first, second);
        Assert.True(KokoroSpeech.IsDownloading);
        Assert.Same(first, KokoroSpeech.RunningDownload);

        KokoroSpeech.CancelDownload();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(KokoroSpeech.IsDownloading);
        Assert.Null(KokoroSpeech.RunningDownload);
        Assert.False(File.Exists(KokoroSpeech.ModelPath + ".part"));
    }

    [Fact]
    public async Task A_finished_Kokoro_download_is_moved_into_place_and_heard_by_whoever_is_listening()
    {
        KokoroSpeech.RecordLoadFailureForTests("the Kokoro model wouldn't load (test)", DateTime.UtcNow);
        KokoroSpeech.Http = new HttpClient(new Serves(new FakeModelStream(WholeModel), WholeModel));

        var heard = new List<double>();
        void Listen(double fraction)
        {
            lock (heard)
            {
                heard.Add(fraction);
            }
        }

        KokoroSpeech.DownloadProgressed += Listen;
        try
        {
            await KokoroSpeech.DownloadModelAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            KokoroSpeech.DownloadProgressed -= Listen;
        }

        Assert.Equal(WholeModel, new FileInfo(KokoroSpeech.ModelPath).Length);
        Assert.False(File.Exists(KokoroSpeech.ModelPath + ".part"));
        Assert.False(KokoroSpeech.IsDownloading);
        Assert.NotEmpty(heard);
        Assert.Equal(1.0, heard[^1]);

        // A new file: what failed to load before is forgotten.
        Assert.Null(KokoroSpeech.LoadFailure);
        Assert.False(KokoroSpeech.ShouldOfferDownload);
    }

    // --- voice-4: a model that won't load -------------------------------------

    [Fact]
    public void A_Kokoro_model_that_would_not_load_is_offered_again_and_stays_unable_between_tries()
    {
        FakeDownloadedModel();
        const string reason = "the Kokoro model wouldn't load (InvalidProtobuf: test) - downloading it again on Voice Settings usually fixes this";

        KokoroSpeech.RecordLoadFailureForTests(reason, DateTime.UtcNow);

        Assert.Equal(reason, KokoroSpeech.UnavailableReason);
        Assert.Equal(reason, KokoroSpeech.LoadFailure);
        Assert.True(KokoroSpeech.ShouldOfferDownload);

        // Past the wait between tries: a line may try loading it again...
        KokoroSpeech.RecordLoadFailureForTests(reason, DateTime.UtcNow - TimeSpan.FromMinutes(2));
        Assert.Null(KokoroSpeech.UnavailableReason);

        // ...but until one works, the page and the Mixer don't call it ready,
        // and the download is still offered.
        Assert.Equal(reason, KokoroSpeech.LoadFailure);
        Assert.True(KokoroSpeech.ShouldOfferDownload);
    }

    [Fact]
    public void A_Kokoro_model_that_loads_is_not_offered_again()
    {
        Assert.True(KokoroSpeech.ShouldOfferDownload);

        FakeDownloadedModel();

        Assert.Null(KokoroSpeech.LoadFailure);
        Assert.False(KokoroSpeech.ShouldOfferDownload);
    }

    // --- Helpers ----------------------------------------------------------------

    /// <summary>A file the size of the model where the model goes - empty, but all KokoroSpeech checks before loading.</summary>
    private static void FakeDownloadedModel()
    {
        Directory.CreateDirectory(KokoroSpeech.Folder);

        using var file = new FileStream(KokoroSpeech.ModelPath, FileMode.Create, FileAccess.Write);
        file.SetLength(60L * 1024 * 1024);
    }

    /// <summary>Reports straight away, on the thread reporting - Progress&lt;T&gt; posts.</summary>
    private sealed class ReportsAtOnce(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Answers every request with the stream given, as a download of that length.</summary>
    private sealed class Serves(Stream body, long length) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    /// <summary>
    /// Zeros, up to the length given, read a buffer at a time. Throws an
    /// IOException once failAt bytes are read (a dropped connection), or
    /// waits until canceled once stallAt bytes are read (a slow hotspot).
    /// </summary>
    private sealed class FakeModelStream(long length, long? failAt = null, long? stallAt = null) : Stream
    {
        private long _read;

        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stallAt is { } stall && _read >= stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (failAt is { } fail && _read >= fail)
            {
                throw new IOException("The response ended prematurely.");
            }

            var count = (int)Math.Min(buffer.Length, length - _read);
            buffer.Span[..count].Clear();
            _read += count;
            FirstRead.TrySetResult();
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
