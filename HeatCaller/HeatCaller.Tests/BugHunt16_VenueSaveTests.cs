namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 16 (10 Nov 2026): an unrelated edit on the Venues page must not turn a
/// decoder number this version does not offer into EasyLap, or save an
/// out-of-range race default clamped.
/// </summary>
public sealed class BugHunt16_VenueSaveTests
{
    [Fact]
    public void UnknownDecoderIsKeptUntilOneIsPicked()
    {
        // A newer version's number shows at the fallback position, EasyLap.
        var shown = DecoderConnection.IndexOf(8);
        Assert.Equal(8, VenuesControl.DecoderToSave(shown, 8, picked: false));
        Assert.Equal(DecoderConnection.RetiredNeonApi,
            VenuesControl.DecoderToSave(DecoderConnection.IndexOf(DecoderConnection.RetiredNeonApi), DecoderConnection.RetiredNeonApi, picked: false));
    }

    [Fact]
    public void PickedDecoderIsSaved()
    {
        var ilap = DecoderConnection.IndexOf(DecoderConnection.ILap);
        Assert.Equal(DecoderConnection.ILap, VenuesControl.DecoderToSave(ilap, 8, picked: true));
        Assert.Equal(DecoderConnection.ILap, VenuesControl.DecoderToSave(ilap, DecoderConnection.EasyLap, picked: false));
    }

    [Fact]
    public void OutOfRangeRaceDefaultIsKeptWhileShownClamped()
    {
        Assert.Equal(45000, VenuesControl.SpinnerToSave(30000, 45000, 0, 30000));
        Assert.Equal(240, VenuesControl.SpinnerToSave(180, 240, 0, 180));
        Assert.Equal(7.5, VenuesControl.SpinnerToSave(5.0, 7.5, 1.0, 5.0));
    }

    [Fact]
    public void ChangedOrInRangeRaceDefaultIsSaved()
    {
        Assert.Equal(20000, VenuesControl.SpinnerToSave(20000, 45000, 0, 30000));
        Assert.Equal(12, VenuesControl.SpinnerToSave(12, 10, 0, 9999));
        Assert.Equal(1.5, VenuesControl.SpinnerToSave(1.5, 1.5, 1.0, 5.0));
    }
}
