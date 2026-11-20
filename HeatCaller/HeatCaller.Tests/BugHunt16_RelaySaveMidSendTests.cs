using System.Net;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): a Save that moves the relay or club while the
/// worker's close (or open, or ballot) to the OLD club is in flight.
///
/// The Save arms everything afresh for the new club; the old club's answer
/// then cleared or set it again as if the new club had been told. Bug hunt
/// 12 #25 guarded shout-outs' open the same way - these are the rest.
/// </summary>
public sealed class BugHunt16_RelaySaveMidSendTests : IDisposable
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

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    private static object? Field(object owner, string name) => FieldOf(owner, name).GetValue(owner);

    /// <summary>
    /// A publisher on "old-club" whose relay answers 200 to everything - and
    /// the first time it is asked anything about old-club, the operator
    /// saves "new-club" before the answer comes back.
    /// </summary>
    private static RelayPublisher SavedMidSend(AppSettings settings, List<string> asked)
    {
        settings.RelayEnabled = false;
        settings.RelayBaseUrl = "http://127.0.0.1:9";
        settings.RelayClub = "old-club";
        settings.RelayToken = "hg_test";

        var publisher = new RelayPublisher(settings);
        var saved = false;

        var answers = new Answers(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            asked.Add(path);

            if (!saved && path.Contains("/c/old-club/", StringComparison.Ordinal))
            {
                saved = true;
                settings.RelayClub = "new-club";
                publisher.SettingsChanged();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        // The worker's client and listening's own (19 Nov 2026).
        foreach (var name in new[] { "_http", "_listenHttp" })
        {
            var field = FieldOf(publisher, name);
            ((HttpClient)field.GetValue(publisher)!).Dispose();
            field.SetValue(publisher, new HttpClient(answers, disposeHandler: false));
        }

        return publisher;
    }

    private static RoomVote OpenVote()
    {
        var vote = RoomVote.Load();
        vote.FollowDay(new RaceEvent { Id = "tonight" });
        Assert.Null(vote.OpenFor("a-main", "Stock A-Main", "Stock", new[]
        {
            new VoteOption("A4821B", "Bill Hargrove"),
            new VoteOption("B9930C", "Sam Teague"),
        }));

        return vote;
    }

    // ------------------------------------------------ closes

    [Fact]
    public void A_shout_out_close_the_old_club_took_is_still_owed_to_the_new_one()
    {
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { ClubNotesFromTheRelay = false }, asked);

        Call(publisher, "SendNotesClose", CancellationToken.None);

        Assert.Contains(asked, p => p.Contains("/c/old-club/notes/open", StringComparison.Ordinal));
        Assert.Equal(true, Field(publisher, "_notesClosePending"));

        // And the next pass tells the new club.
        Call(publisher, "SendNotesClose", CancellationToken.None);
        Assert.Contains(asked, p => p.Contains("/c/new-club/notes/open", StringComparison.Ordinal));
        Assert.Equal(false, Field(publisher, "_notesClosePending"));
    }

    [Fact]
    public void A_drivers_nights_close_the_old_club_took_is_still_owed_to_the_new_one()
    {
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { PublishDriverNightsToTheRelay = false }, asked);

        Call(publisher, "SendNightsClose", CancellationToken.None);

        Assert.Contains(asked, p => p.Contains($"/c/old-club/{DriverNight.Channel}/open", StringComparison.Ordinal));
        Assert.Equal(true, Field(publisher, "_nightsClosePending"));
    }

    [Fact]
    public void A_driver_requests_close_the_old_club_took_is_still_owed_to_the_new_one()
    {
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { PublishDriverNightsToTheRelay = false }, asked);

        Assert.Equal(true, Field(publisher, "_asksClosePending"));

        Call(publisher, "SendAsksClose", CancellationToken.None);

        Assert.Contains(asked, p => p.Contains("/c/old-club/", StringComparison.Ordinal));
        Assert.Equal(true, Field(publisher, "_asksClosePending"));
    }

    [Fact]
    public void A_listening_close_the_old_club_took_is_still_owed_to_the_new_one()
    {
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { ListenAlongOnTheRelay = false }, asked);

        Call(publisher, "SendListenClose", CancellationToken.None);

        Assert.Contains(asked, p => p.Contains("/c/old-club/", StringComparison.Ordinal));
        Assert.Equal(true, Field(publisher, "_listenClosePending"));
    }

    [Fact]
    public void A_vote_close_the_old_club_took_is_still_owed_to_the_new_one()
    {
        var vote = OpenVote();
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { RoomVoteFromTheRelay = false }, asked);
        publisher.TonightsVote = () => vote;

        Call(publisher, "SendVoteClose", CancellationToken.None);

        Assert.Contains(asked, p => p.Contains("/c/old-club/vote/open", StringComparison.Ordinal));
        Assert.Equal(true, Field(publisher, "_voteClosePending"));
    }

    // ------------------------------------------------ opens

    [Fact]
    public void Drivers_nights_opened_on_the_old_club_are_not_marked_told_for_the_new_one()
    {
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { PublishDriverNightsToTheRelay = true }, asked);

        var old = Field(publisher, "_target");
        Assert.Equal(true, Call(publisher, "TellTheRelayNightsAreOpen", old, "old-club", true, CancellationToken.None));

        Assert.NotEqual(old, Field(publisher, "_target"));
        Assert.Equal(false, Field(publisher, "_nightsOpenTold"));
    }

    [Fact]
    public void A_ballot_published_to_the_old_club_is_published_again_to_the_new_one()
    {
        var vote = OpenVote();
        var asked = new List<string>();
        using var publisher = SavedMidSend(new AppSettings { RoomVoteFromTheRelay = true }, asked);

        var old = Field(publisher, "_target");
        Assert.Equal(true, Call(publisher, "PublishTheBallot", old, "old-club", vote, true, CancellationToken.None));

        Assert.Null(Field(publisher, "_ballotAsPublished"));

        var current = Field(publisher, "_target");
        Assert.Equal(true, Call(publisher, "PublishTheBallot", current, "new-club", vote, true, CancellationToken.None));
        Assert.Contains(asked, p => p.Contains("/c/new-club/vote/open", StringComparison.Ordinal));
        Assert.NotNull(Field(publisher, "_ballotAsPublished"));
    }

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }
}
