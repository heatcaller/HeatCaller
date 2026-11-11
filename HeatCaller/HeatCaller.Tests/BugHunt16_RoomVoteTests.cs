namespace HeatCaller.Tests;

/// <summary>
/// (10 Nov 2026, bug hunt 16) A hand-edited or restored room-vote.json
/// with nulls in it - a null option, a null WinnerName, a null DayId - is
/// repaired on load rather than throwing later on the Room Vote window's
/// UI timer or the announcer's tick.
/// </summary>
public sealed class BugHunt16_RoomVoteTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static void WriteFile(string json)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "room-vote.json"), json);
    }

    [Fact]
    public void Null_options_and_fields_in_the_file_do_not_throw()
    {
        var votes = string.Join(",", Enumerable.Range(0, 6).Select(at => $"\"phone-{at}\":\"t1\""));

        WriteFile($$"""
            {
              "DayId": null,
              "Current": {
                "Id": null,
                "RaceKey": "round-a",
                "RoundName": null,
                "ClassName": null,
                "Options": [ null, { "Id": "t1", "Name": "Bill Hargrove" }, { "Id": null, "Name": "Nobody" } ],
                "Votes": { {{votes}} },
                "OpenedUtc": "{{DateTime.UtcNow:O}}",
                "ClosedUtc": "{{DateTime.UtcNow:O}}",
                "WinnerId": "t1",
                "WinnerName": null,
                "Finished": true
              }
            }
            """);

        var vote = RoomVote.Load();

        Assert.False(vote.LoadedUnreadable);
        Assert.Equal(string.Empty, vote.DayId);

        var standing = Assert.Single(vote.Standing());
        Assert.Equal("Bill Hargrove", standing.Name);

        var waiting = vote.CallWaiting();
        Assert.NotNull(waiting);
        Assert.Null(waiting!.WinnerName);

        vote.StartUp(null, _ => null);
    }
}
