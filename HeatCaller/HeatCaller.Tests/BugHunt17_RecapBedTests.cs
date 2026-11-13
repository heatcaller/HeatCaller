namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, #193: RecapBed.Stop closed the bed's wav from the stopping
/// thread even when its writer was still stuck in a write. The writer closes
/// its own file now, and a Stop that gave up waiting leaves it alone.
/// </summary>
public sealed class BugHunt17_RecapBedTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_writer_closes_the_file_itself_when_it_finishes()
    {
        // Started ten seconds ago and one sample long, so the writer is past
        // the end on its first turn and finishes without Stop being called.
        var bed = RecapBed.StartFor(_data.Folder, "Heat 1", DateTime.UtcNow.AddSeconds(-10), maxSamples: 1, _ => { });

        Assert.NotNull(bed);

        var closed = false;
        var until = Environment.TickCount64 + 20_000;

        while (!closed && Environment.TickCount64 < until)
        {
            try
            {
                // Nobody else may hold it: fails while the writer has it open.
                using var alone = new FileStream(bed!.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                closed = true;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }

        Assert.True(closed, "the writer thread should close the bed's file when it ends");

        // And Stop after it is still safe - closing twice does nothing.
        bed!.Stop();
        bed.Stop();
        Assert.Null(bed.Problem);
    }
}
