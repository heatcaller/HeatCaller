namespace HeatCaller.Tests;

/// <summary>
/// The fifth batch of race-night features: the webcam photo finish
/// (PhotoFinish) - which crossings the camera page is asked to keep, and
/// how the frames it sends back are kept and read back.
/// </summary>
public sealed class PhotoFinishTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>The smallest thing that passes for a JPEG: its start-of-image marker, then anything.</summary>
    private static byte[] Jpeg(byte fill = 1) => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, fill, fill, fill };

    private static readonly long Nine = new DateTimeOffset(2026, 10, 3, 21, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Fact]
    public void The_camera_is_asked_for_each_crossing_after_the_last_it_saw()
    {
        var photo = new PhotoFinish { NameOf = t => t == "4" ? "Frank" : t };

        var lap = photo.Crossed("4", 1, 9, Nine, finish: false);
        var finish = photo.Crossed("7", 2, 9, Nine + 400, finish: true);

        Assert.Equal("Frank", lap.Name);
        Assert.False(lap.Finish);
        Assert.True(finish.Finish);

        Assert.Equal(new[] { finish.Id }, photo.After(lap.Id).Select(s => s.Id));
        Assert.Contains($"\"latest\":{finish.Id}", photo.AfterJson(0));
        Assert.True(photo.CameraConnected);
    }

    [Fact]
    public void A_car_taking_the_flag_is_a_finish_including_the_one_that_brings_it_out()
    {
        // Under the checkered, and while finishing with a yellow over it.
        Assert.True(PhotoFinish.IsFinishCrossing(false, FlagState.Checkered, null, 9));
        Assert.True(PhotoFinish.IsFinishCrossing(true, FlagState.Yellow, null, 9));

        // The crossing that reaches the lap limit is counted before the flag comes out.
        Assert.True(PhotoFinish.IsFinishCrossing(false, FlagState.Green, 10, 10));

        // An ordinary lap.
        Assert.False(PhotoFinish.IsFinishCrossing(false, FlagState.Green, 10, 9));
        Assert.False(PhotoFinish.IsFinishCrossing(false, FlagState.Green, null, 40));
    }

    [Fact]
    public void Only_the_latest_shots_wait_for_the_camera()
    {
        var photo = new PhotoFinish();

        for (var i = 0; i < PhotoFinish.ShotsKept + 20; i++)
        {
            photo.Crossed("4", 1, i, Nine + i * 1_000, finish: false);
        }

        Assert.Equal(PhotoFinish.ShotsKept, photo.After(0).Count);
    }

    [Fact]
    public void Frames_are_kept_a_folder_a_shot_and_read_back_in_time_order()
    {
        var photo = new PhotoFinish { NameOf = _ => "Sam: Smith" };
        var said = new List<string>();
        photo.Saved += said.Add;

        var shot = photo.Crossed("4", 1, 12, Nine, finish: true);

        Assert.True(photo.SaveFrame(shot.Id, 120, Jpeg(2)));
        Assert.True(photo.SaveFrame(shot.Id, -400, Jpeg(3)));
        Assert.True(photo.SaveFrame(shot.Id, 0, Jpeg(4)));

        // Not a picture, not a shot asked for, or too far from the moment.
        Assert.False(photo.SaveFrame(shot.Id, 40, new byte[] { 1, 2, 3, 4 }));
        Assert.False(photo.SaveFrame(shot.Id + 99, 40, Jpeg()));
        Assert.False(photo.SaveFrame(shot.Id, 9_000, Jpeg()));

        // Said once a shot, not once a frame.
        Assert.Single(said);
        Assert.Contains("Photo finish kept for Sam: Smith (P1, lap 12)", said[0]);

        var night = Assert.Single(PhotoFinish.Nights());
        var saved = Assert.Single(PhotoFinish.ShotsOn(night));

        Assert.Equal("Sam: Smith", saved.Name);
        Assert.True(saved.Finish);
        Assert.StartsWith(PhotoFinish.Root, saved.Folder);
        Assert.DoesNotContain(':', Path.GetFileName(saved.Folder));
        Assert.Equal(new[] { -400, 0, 120 }, saved.Frames().Select(f => f.OffsetMs));
    }

    [Fact]
    public void A_frame_name_sorts_in_time_order_and_reads_back()
    {
        Assert.Equal("-0400.jpg", PhotoFinish.FrameName(-400));
        Assert.Equal("+0120.jpg", PhotoFinish.FrameName(120));
        Assert.Equal(-400, PhotoFinish.OffsetOf(@"C:\x\-0400.jpg"));
        Assert.Equal(120, PhotoFinish.OffsetOf("+0120.jpg"));
        Assert.Null(PhotoFinish.OffsetOf("shot.json"));
        Assert.Null(PhotoFinish.OffsetOf("frame.jpg"));

        Assert.True(PhotoFinish.LooksLikeJpeg(Jpeg()));
        Assert.False(PhotoFinish.LooksLikeJpeg(new byte[PhotoFinish.LargestFrame + 1]));
    }
}
