using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// A VOTE ON ANYTHING (15 Nov 2026, Asher: "be able to launch a vote from
/// inbox and set up a custom vote on anything"). The desk types a question
/// and its answers in the Desk Inbox; the room votes on the same pages as a
/// race vote; no race's start or result touches it, and the announcer never
/// reads a verdict on it.
/// </summary>
public sealed class CustomRoomVoteTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoomVote Tonight()
    {
        var vote = RoomVote.Load();
        vote.NewMeeting("tonight");
        return vote;
    }

    [Fact]
    public void It_opens_on_the_desks_question_and_answers()
    {
        var vote = Tonight();

        Assert.Null(vote.OpenCustom("  Best paint job tonight? ", new[] { "Sam's blue Mod", "", "Dev's flames", "sam's blue mod", "The yellow truck" }));

        Assert.True(vote.IsCustom);
        Assert.True(vote.Taking);
        Assert.Equal("Best paint job tonight?", vote.OnRace);
        Assert.StartsWith(RoomVote.CustomKeyPrefix, vote.OnRaceKey);
        // Blanks and repeats (any case) are dropped; the words stay as typed.
        Assert.Equal(new[] { "Sam's blue Mod", "Dev's flames", "The yellow truck" }, vote.Options.Select(o => o.Name));
        Assert.Equal(new[] { "choice-1", "choice-2", "choice-3" }, vote.Options.Select(o => o.Id));
    }

    [Theory]
    [InlineData("", "Type the question first.")]
    [InlineData("Pizza or burgers?", "Give the room at least two choices, one a line.")]
    public void It_needs_a_question_and_two_answers(string question, string why)
    {
        var vote = Tonight();

        Assert.Equal(why, vote.OpenCustom(question, new[] { "Pizza", "pizza", " " }));
        Assert.Null(vote.OnPollId);
    }

    [Fact]
    public void A_race_starting_or_finishing_leaves_it_alone_and_no_verdict_is_offered()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenCustom("Track of the month?", new[] { "Here", "There" }));

        for (var i = 0; i < RoomVote.LeastVotes + 2; i++)
        {
            Assert.Null(vote.Cast($"phone-{i}", "choice-1"));
        }

        vote.StartedRace("round-7");
        vote.Finished("round-7", "100", "Sam Parker");

        Assert.True(vote.Taking);
        Assert.Null(vote.CallWaiting());
        Assert.Equal(RoomVote.LeastVotes + 2, vote.Votes);
        Assert.Equal("That isn't one of the choices.", vote.Cast("phone-x", "100"));
    }

    [Fact]
    public void It_comes_back_after_a_restart_as_a_custom_vote()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenCustom("Pizza or burgers?", new[] { "Pizza", "Burgers" }));

        var again = RoomVote.Load();

        Assert.True(again.IsCustom);
        Assert.Equal("Pizza or burgers?", again.OnRace);
    }

    [Fact]
    public void A_race_vote_after_it_is_a_race_vote_again()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenCustom("Pizza or burgers?", new[] { "Pizza", "Burgers" }));
        Assert.Null(vote.OpenFor("round-7", "Mod A-Main", "Mod", new[] { new VoteOption("100", "Sam"), new VoteOption("200", "Dev") }));

        Assert.False(vote.IsCustom);
        Assert.Equal("That driver is not on the grid for this race.", vote.Cast("phone", "choice-1"));
    }

    [Fact]
    public void The_relay_gets_the_answers_as_typed_never_shortened_like_a_name()
    {
        var options = new[] { new VoteOption("choice-1", "Sam Parker's blue Mod"), new VoteOption("choice-2", "choice-2") };

        using var custom = JsonDocument.Parse(JsonSerializer.Serialize(RelayPublisher.RelayBallotDrivers(options, "poll", "key-key-key-key-key", abbreviate: true, custom: true)));
        var names = custom.RootElement.EnumerateArray().Select(one => one.GetProperty("name").GetString()).ToList();

        Assert.Equal(new[] { "Sam Parker's blue Mod", "choice-2" }, names);
    }

    [Fact]
    public void The_wifi_ballot_says_it_is_custom_and_the_page_can_ask_the_question()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenCustom("Pizza or burgers?", new[] { "Pizza", "Burgers" }));
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings());

        using var json = JsonDocument.Parse(server.BuildVoteBallotJson(vote, "phone"));
        Assert.True(json.RootElement.GetProperty("custom").GetBoolean());
        Assert.Equal("Pizza or burgers?", json.RootElement.GetProperty("race").GetString());

        var page = server.BuildVotePage();
        Assert.Contains("<span id=\"head\">WHO WINS?</span>", page);
        Assert.Contains("custom ? 'ROOM VOTE' : 'WHO WINS?'", page);
        Assert.Contains("custom ? 'Tap your answer.' : 'Tap who you reckon wins.'", page);
    }

    // ------------------------------------------------------------ the inbox

    private static T Field<T>(DeskInboxForm form, string name) =>
        (T)typeof(DeskInboxForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    [Fact]
    public void The_inbox_opens_a_custom_vote_and_shows_its_question_and_answers()
    {
        var vote = Tonight();
        var target = new VoteTarget(null, null, null, Array.Empty<VoteOption>(), "/vote on this PC's address");

        StaThread.Run(() =>
        {
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, null, vote, () => target, null, new AppSettings(), null, null, null);

            var button = Field<Button>(inbox, "_btnCustomVote");
            Assert.Equal("New vote", button.Text);
            Assert.True(button.Enabled, "no race needed");

            Assert.Null(inbox.StartCustomVote("Pizza or burgers?", new[] { "Pizza", "Burgers" }));

            Assert.True(vote.IsCustom);
            Assert.StartsWith("Voting is OPEN: \"Pizza or burgers?\"", Field<Label>(inbox, "_lblVote").Text);
            Assert.Contains($"stops itself after {(int)RoomVote.LongestOpen.TotalMinutes} minutes", Field<Label>(inbox, "_lblVoteWhere").Text);
            var tally = Field<DataGridView>(inbox, "_tally");
            Assert.Equal("Answer", tally.Columns[0].HeaderText);
            Assert.Equal(2, tally.Rows.Count);
            Assert.True(Field<Button>(inbox, "_btnStopVote").Enabled);

            return true;
        }, TimeSpan.FromSeconds(30), () => "a custom vote from the inbox");
    }

    [Fact]
    public void A_new_vote_asks_before_throwing_away_votes_already_in()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenFor("round-7", "Mod A-Main", "Mod", new[] { new VoteOption("100", "Sam"), new VoteOption("200", "Dev") }));
        Assert.Null(vote.Cast("phone", "100"));
        var target = new VoteTarget(null, null, null, Array.Empty<VoteOption>(), null);

        StaThread.Run(() =>
        {
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, null, vote, () => target, null, new AppSettings(), null, null, null);

            string? asked = null;
            Assert.Null(inbox.StartCustomVote("Pizza or burgers?", new[] { "Pizza", "Burgers" }, q => { asked = q; return false; }));
            Assert.Contains("1 vote already in on Mod A-Main", asked);
            Assert.False(vote.IsCustom, "kept the race vote on a no");

            Assert.Equal("Give the room at least two choices, one a line.", inbox.StartCustomVote("Pizza or burgers?", new[] { "Pizza" }, _ => true));

            return true;
        }, TimeSpan.FromSeconds(30), () => "replacing a vote with a custom one");
    }

    [Fact]
    public void The_new_vote_window_reads_the_question_and_one_answer_a_line()
    {
        var (question, answers) = StaThread.Run(() =>
        {
            using var form = new CustomVoteForm();
            form.Boxes.Question.Text = "  Best paint job tonight? ";
            form.Boxes.Answers.Text = "Sam's blue Mod\r\n\r\n  Dev's flames  \r\n";
            return (form.Question, form.Answers);
        }, TimeSpan.FromSeconds(30), () => "the New vote window");

        Assert.Equal("Best paint job tonight?", question);
        Assert.Equal(new[] { "Sam's blue Mod", "Dev's flames" }, answers);
    }
}
