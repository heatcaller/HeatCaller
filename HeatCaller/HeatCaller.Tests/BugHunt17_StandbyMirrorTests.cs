using System.Net;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the standby's mirror: a timing PC that goes silent part-way
/// through a copy (power cut, a dropped switch) must not stop the standby
/// copying for the rest of the night. The read used to wait for good, so
/// no later ask ever ran and nothing said why.
/// </summary>
public sealed class BugHunt17_StandbyMirrorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>An answer whose headers come and whose body never does - and never closes.</summary>
    private sealed class SilentBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var gone = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellationToken.Register(() => gone.TrySetException(new OperationCanceledException(cancellationToken))))
            {
                return await gone.Task;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeTimingPc : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(Answer());
        }
    }

    [Fact]
    public async Task A_copy_that_stops_arriving_is_given_up_and_the_next_ask_still_runs()
    {
        var timingPc = new FakeTimingPc
        {
            Answer = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SilentBody()) },
        };

        using var mirror = new StandbyMirror(
            () => new StandbyMirror.Target(new Uri("http://192.168.1.20:8080" + WarmStandby.SnapshotPath), "12345678", null),
            Path.Combine(_data.Folder, "mirror"), AppPaths.Root, timingPc)
        {
            StalledAfter = TimeSpan.FromMilliseconds(200),
        };

        var ask = mirror.AskOnceAsync();
        Assert.Same(ask, await Task.WhenAny(ask, Task.Delay(TimeSpan.FromSeconds(20))));

        Assert.False(await ask);
        Assert.Contains("didn't answer in time", mirror.Status.LastError);
        Assert.False(mirror.HasCopy);

        // Not latched: the next ask reaches the timing PC and reports what it said.
        timingPc.Answer = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.False(await mirror.AskOnceAsync());

        Assert.Equal(2, timingPc.Asked);
        Assert.Contains("PIN", mirror.Status.LastError);
    }
}
