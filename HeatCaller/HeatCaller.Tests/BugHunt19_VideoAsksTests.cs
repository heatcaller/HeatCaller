using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, #1: a driver's "video starts in the wrong place" and "hide
/// this video" asks were thrown away when the inbox was read, because
/// DriverAsk.IsKind didn't know "videooff" or "videohide" - confirmed to the
/// relay as unusable, never shown at the desk, and the desk's answer never
/// went back to the phone.
/// </summary>
public sealed class BugHunt19_VideoAsksTests
{
    private const string SamBadge = "0123456789abcdef0123";
    private static readonly string Race = new('a', 32);

    private static string IdOf(int n) => n.ToString("x6") + new string('0', 18);

    [Fact]
    public void Video_asks_from_the_inbox_reach_the_desk()
    {
        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            items = new object[]
            {
                new { id = IdOf(1), b = SamBadge, k = "videooff", v = "badge", at = "2026-11-14T19:40:10Z", f = new { race = Race, d = "early", s = "10" } },
                new { id = IdOf(2), b = SamBadge, k = "videohide", v = "badge", at = "2026-11-14T19:41:00Z", f = new { race = Race } },
            },
        });

        var asks = DriverRequestStore.ParseInbox(json, DateTime.UtcNow, out var unusable);

        Assert.Empty(unusable);
        Assert.Equal(2, asks.Count);
        Assert.Equal(DriverAsk.VideoOff, asks[0].Kind);
        Assert.Equal(Race, asks[0].Text("race"));
        Assert.Equal("early", asks[0].Text("d"));
        Assert.Equal("10", asks[0].Text("s"));
        Assert.Equal(DriverAsk.VideoHide, asks[1].Kind);
        Assert.Equal(Race, asks[1].Text("race"));

        // Both wait for the desk; the laptop doesn't apply them by itself.
        Assert.False(DriverAsk.IsWrite(DriverAsk.VideoOff));
        Assert.False(DriverAsk.IsWrite(DriverAsk.VideoHide));
    }

    [Fact]
    public void The_desks_answer_to_a_video_ask_goes_back_to_the_phone()
    {
        var profile = new DriverProfile { Transponder = "1001", DriverName = "Sam Parker" };
        var badge = 1.ToString("x20");

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = new DateTime(2026, 11, 14, 19, 45, 0, DateTimeKind.Utc),
            CheckedIn = new[] { "1001" },
            ProfileOf = t => t == "1001" ? profile : null,
            BadgeOf = t => t == "1001" ? badge : null,
            AsksOf = _ => new[]
            {
                new DriverAskAnswer("A1B2C3", DriverAsk.VideoOff, DriverAsk.Noted, ""),
                new DriverAskAnswer("D4E5F6", DriverAsk.VideoHide, DriverAsk.Approved, ""),
            },
        });

        var asks = JsonDocument.Parse(json).RootElement.GetProperty("drivers").GetProperty(badge).GetProperty("asks").EnumerateArray().ToList();

        Assert.Equal(new[] { DriverAsk.VideoOff, DriverAsk.VideoHide }, asks.Select(a => a.GetProperty("kind").GetString()));
    }
}
