using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Sponsors page (23 Nov 2026).
///
/// The "Picture for their read" drop-down offers the same folder's pictures
/// as the Slides card above it, but was only filled when a sponsor was
/// picked. A picture just added with Add Image... was missing from it, and a
/// slide just removed was still offered.
/// </summary>
public sealed class BugHunt24_SponsorsControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static T FieldOf<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(owner.GetType().Name, name)).GetValue(owner)!;

    private static List<string> Choices(SponsorsControl page) =>
        FieldOf<ComboBox>(page, "_cmbSponsorPicture").Items.Cast<object>().Select(o => o.ToString() ?? string.Empty).ToList();

    private sealed record Seen(List<string> Choices, string? Selected, string StoredPicture);

    /// <summary>
    /// A page over one sponsor pointed at "used.png", with "spare.png" in the
    /// rotation too; <paramref name="change"/> changes the slides the way Add
    /// Image... or Remove does, and RefreshSlides runs as they do after it.
    /// </summary>
    private static Seen AfterSlidesChange(Action<SponsorStore> change)
    {
        return StaThread.Run(() =>
        {
            var store = SponsorStore.Load();
            store.Current.SlideFileNames.Add("used.png");
            store.Current.SlideFileNames.Add("spare.png");

            var sponsor = store.AddSponsor("Clarke and Sons");
            sponsor.PictureFileName = "used.png";

            using var page = new SponsorsControl(store, new UndoService());

            change(store);
            Call(page, "RefreshSlides");

            var combo = FieldOf<ComboBox>(page, "_cmbSponsorPicture");

            return new Seen(Choices(page), combo.SelectedItem as string, store.Sponsor(sponsor.Id)?.PictureFileName ?? string.Empty);
        }, TimeSpan.FromSeconds(30), () => "the Sponsors page's picture list");
    }

    [Fact]
    public void A_picture_just_added_is_offered_for_the_read()
    {
        var seen = AfterSlidesChange(store => store.Current.SlideFileNames.Add("new.png"));

        Assert.Contains("new.png", seen.Choices);

        // Refilling the list does not change what the sponsor is pointed at.
        Assert.Equal("used.png", seen.Selected);
        Assert.Equal("used.png", seen.StoredPicture);
    }

    [Fact]
    public void A_video_just_added_is_still_not_offered_for_the_read()
    {
        var seen = AfterSlidesChange(store => store.Current.SlideFileNames.Add("advert.mp4"));

        Assert.DoesNotContain("advert.mp4", seen.Choices);
    }

    [Fact]
    public void A_slide_just_removed_is_no_longer_offered_for_the_read()
    {
        var seen = AfterSlidesChange(store => store.RemoveSlide("spare.png"));

        Assert.DoesNotContain("spare.png", seen.Choices);
        Assert.Equal("used.png", seen.StoredPicture);
    }

    [Fact]
    public void Removing_the_sponsors_own_picture_still_shows_it_and_keeps_it_stored()
    {
        var seen = AfterSlidesChange(store => store.RemoveSlide("used.png"));

        // Still named, so the club can see what it was pointed at - see
        // FillPictureChoices - and not blanked by the refill.
        Assert.Equal("used.png", seen.Selected);
        Assert.Equal("used.png", seen.StoredPicture);
    }
}
