namespace HeatCaller.Tests;

/// <summary>
/// Remove photo when the photo can't be moved aside.
///
/// RemoveAttachment cleared the track's attachment name whether or not the
/// folder was set aside. With the PDF open in Acrobat the move failed, the
/// name went, the file stayed - and the edit's undo, reading "nothing set
/// aside" as "there was no photo before", moved the original photo into the
/// undo holding folder, which deletes it a week later.
/// </summary>
public sealed class BugHunt15RemoveAttachmentTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void RemoveAttachment_refuses_and_keeps_the_name_when_the_photo_cant_be_moved_aside()
    {
        var track = new TrackLayout { Name = "Club" };
        Directory.CreateDirectory(AppPaths.Root);
        var picked = Path.Combine(AppPaths.Root, "plan.pdf");
        File.WriteAllText(picked, "%PDF-1.4");
        TrackAttachmentStore.Attach(track, picked);

        var attached = TrackAttachmentStore.ResolvePath(track);
        Assert.NotNull(attached);

        // Open the way another program holds it: no delete sharing, so the
        // folder can't be moved.
        using (new FileStream(attached!, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<InvalidOperationException>(() => TrackAttachmentStore.RemoveAttachment(track));
        }

        Assert.Equal("plan.pdf", track.AttachmentFileName);
        Assert.Equal(attached, TrackAttachmentStore.ResolvePath(track));
    }

    [Fact]
    public void RemoveAttachment_sets_the_photo_aside_and_clears_the_name()
    {
        var track = new TrackLayout { Name = "Club" };
        Directory.CreateDirectory(AppPaths.Root);
        var picked = Path.Combine(AppPaths.Root, "plan.pdf");
        File.WriteAllText(picked, "%PDF-1.4");
        TrackAttachmentStore.Attach(track, picked);

        var stashed = TrackAttachmentStore.RemoveAttachment(track);

        Assert.NotNull(stashed);
        Assert.Null(track.AttachmentFileName);

        track.AttachmentFileName = "plan.pdf";
        TrackAttachmentStore.RestoreForTrack(track.Id, stashed);
        Assert.NotNull(TrackAttachmentStore.ResolvePath(track));
    }
}
