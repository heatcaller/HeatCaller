using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the next-night card went out "taking entries"
/// whenever Driver requests was ticked - which stays ticked when drivers'
/// nights are switched off. Home then offered "Coming? Put yourself down",
/// which the relay refuses while nights are closed.
/// </summary>
public sealed class BugHunt17_MainForm_NextNightTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void PhonesCanSayComingOnlyWithRequestsAndNights(bool requests, bool nights, bool expected)
    {
        var settings = new AppSettings { RelayDriverRequests = requests, PublishDriverNightsToTheRelay = nights };

        Assert.Equal(expected, MainForm.PhonesCanSayComing(settings));
    }

    [Fact]
    public void WithNightsOffTheCardIsNotTakingEntries()
    {
        var settings = new AppSettings { RelayDriverRequests = true, PublishDriverNightsToTheRelay = false };
        var night = new AnnouncedNight { Day = new DateTime(2026, 11, 18), Title = "Club Night" };

        using var doc = JsonDocument.Parse(NextNight.Document(night, MainForm.PhonesCanSayComing(settings)));

        Assert.False(doc.RootElement.GetProperty("takingEntries").GetBoolean());
    }
}
