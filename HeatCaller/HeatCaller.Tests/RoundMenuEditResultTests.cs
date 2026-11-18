using HeatCaller;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Edit Result on the Race Day round menu (17 Nov 2026, Asher: "add a right
/// click option to go to race history from race day menu", "call it edit
/// result"): offered only for a round that was raced and whose race is still
/// in Race History.
/// </summary>
public class RoundMenuEditResultTests
{
    private static readonly SessionRecord Saved = new() { Id = "s1", SessionType = RaceSessionType.Race };

    private static SessionRecord? Lookup(string id) => id == Saved.Id ? Saved : null;

    [Fact]
    public void A_raced_round_gives_its_saved_race()
    {
        var round = new RoundDefinition { LinkedSessionId = "s1" };

        Assert.Equal("s1", RaceProgramControl.RecordedSessionOf(round, Lookup));
    }

    [Fact]
    public void A_round_not_raced_yet_gives_nothing()
    {
        Assert.Null(RaceProgramControl.RecordedSessionOf(new RoundDefinition(), Lookup));
        Assert.Null(RaceProgramControl.RecordedSessionOf(new RoundDefinition { LinkedSessionId = "" }, Lookup));
    }

    [Fact]
    public void A_race_deleted_from_history_gives_nothing()
    {
        var round = new RoundDefinition { LinkedSessionId = "gone" };

        Assert.Null(RaceProgramControl.RecordedSessionOf(round, Lookup));
    }

    [Fact]
    public void No_round_selected_gives_nothing()
    {
        Assert.Null(RaceProgramControl.RecordedSessionOf(null, Lookup));
    }
}
