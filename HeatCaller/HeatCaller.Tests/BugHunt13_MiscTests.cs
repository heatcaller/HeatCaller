using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13 (7 Nov 2026): three odds and ends.
///
/// A relay too slow to answer was taken for the pass being cancelled, so
/// the room's vote (and shout-outs, requests and the rest) never said it
/// was in trouble; a sponsor slide whose phone moved on mid-send tried to
/// answer 500 after its headers had gone; and Play Everything left on as a
/// race started cut the race's crowd and music dead when it was stopped.
/// </summary>
public sealed class BugHunt13_MiscTests : IDisposable
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

    // ------------------------------------------------ relay time limits

    /// <summary>
    /// A publisher whose relay answers through <paramref name="answer"/>, in
    /// place of the network. Throwing TaskCanceledException there with the
    /// caller's token untouched is exactly what Send's own time limit does.
    /// </summary>
    private static RelayPublisher Publisher(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var publisher = new RelayPublisher(new AppSettings
        {
            RelayEnabled = false,
            RelayBaseUrl = "http://127.0.0.1:9",
            RelayClub = "acme-rc",
            RelayToken = "hg_test",
            ClubNotesFromTheRelay = true,
            RoomVoteFromTheRelay = true,
        });

        var field = FieldOf(publisher, "_http");
        ((HttpClient)field.GetValue(publisher)!).Dispose();
        field.SetValue(publisher, new HttpClient(new Answers(answer)));

        return publisher;
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TimedOut() =>
        throw new TaskCanceledException("The request was canceled due to the configured timeout.");

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

    [Fact]
    public void A_vote_inbox_that_times_out_is_reported()
    {
        var vote = OpenVote();

        using var publisher = Publisher(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/vote/inbox", StringComparison.Ordinal) ? TimedOut() : Ok("{}"));
        publisher.TonightsVote = () => vote;

        Call(publisher, "CollectRoomVotes", CancellationToken.None);

        Assert.Contains("did not answer in time", publisher.VoteProblem ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ballot_that_times_out_is_reported()
    {
        var vote = OpenVote();

        using var publisher = Publisher(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/vote/open", StringComparison.Ordinal) ? TimedOut() : Ok("{\"items\":[]}"));
        publisher.TonightsVote = () => vote;

        Call(publisher, "CollectRoomVotes", CancellationToken.None);

        Assert.Contains("did not answer in time", publisher.VoteProblem ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pass_that_really_was_cancelled_still_says_nothing()
    {
        var vote = OpenVote();

        using var publisher = Publisher(_ => Ok("{\"items\":[]}"));
        publisher.TonightsVote = () => vote;

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Call(publisher, "CollectRoomVotes", cancelled.Token);

        Assert.Null(publisher.VoteProblem);
    }

    [Fact]
    public void A_shout_out_inbox_that_times_out_is_reported()
    {
        var notes = ClubNotes.Load();

        using var publisher = Publisher(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/notes/inbox", StringComparison.Ordinal) ? TimedOut() : Ok("{}"));
        publisher.TonightsNotes = () => notes;

        Call(publisher, "CollectClubNotes", CancellationToken.None);

        Assert.Contains("did not answer in time", publisher.NotesProblem ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer(request));
        }
    }

    // ------------------------------------------------ the Mixer's audition

    [Fact]
    public void Stopping_an_audition_a_race_took_over_leaves_the_race_its_crowd_mood()
    {
        var voice = VoiceSettingsStore.Load();
        var sounds = SoundEffectsStore.Load();
        var ambienceStore = AmbienceStore.Load();
        var musicStore = MusicStore.Load();
        var profiles = ProfileStore.Load();

        var seen = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var music = new MusicPlayer();
            var announcer = new AnnouncementService(profiles, voice, sounds);
            using var mixer = new MixerControl(voice, sounds, ambienceStore, crowd, musicStore, music, announcer, new AppSettings());

            // Play Everything pressed between races, with nothing on: it
            // started the crowd and the music itself and set race level.
            FieldOf(mixer, "_auditioning").SetValue(mixer, true);
            FieldOf(mixer, "_ambienceWasRunning").SetValue(mixer, false);
            FieldOf(mixer, "_musicWasPlaying").SetValue(mixer, false);
            FieldOf(mixer, "_ambienceMoodBefore").SetValue(mixer, AmbienceService.Intensity.Idle);
            FieldOf(mixer, "_auditionStartedAmbience").SetValue(mixer, true);
            FieldOf(mixer, "_auditionStartedMusic").SetValue(mixer, true);
            var levelCheck = (System.Windows.Forms.Timer)FieldOf(mixer, "_auditionVoice").GetValue(mixer)!;
            levelCheck.Start();
            crowd.SetIntensity(AmbienceService.Intensity.Racing);

            // A race starts from a phone and keeps both as its own.
            mixer.SessionAdoptedAudio(ambience: true, music: true);

            var levelCheckAfterStart = levelCheck.Enabled;
            var startedCrowd = (bool)FieldOf(mixer, "_auditionStartedAmbience").GetValue(mixer)!;
            var startedMusic = (bool)FieldOf(mixer, "_auditionStartedMusic").GetValue(mixer)!;

            // Mid-race, the operator leaves the page or presses Stop Everything.
            Call(mixer, "StopAudition");

            return (levelCheckAfterStart, startedCrowd, startedMusic, mood: crowd.CurrentIntensity);
        }, TimeSpan.FromSeconds(30), () => "stopping an audition a race took over");

        Assert.False(seen.levelCheckAfterStart);
        Assert.False(seen.startedCrowd);
        Assert.False(seen.startedMusic);
        Assert.Equal(AmbienceService.Intensity.Racing, seen.mood);
    }

    [Fact]
    public void A_session_start_tells_the_mixer_what_it_kept()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);

        var source = File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", "MainForm.Audio.cs"));
        var start = source.IndexOf("private void StartSessionAudio()", StringComparison.Ordinal);
        Assert.True(start >= 0, "StartSessionAudio has moved - this test needs its bounds updated.");

        var end = source.IndexOf("_ambience.Start(ignoreEnabledSetting: true);", start, StringComparison.Ordinal);
        Assert.True(end > start, "StartSessionAudio no longer starts the crowd itself - this test needs its bounds updated.");

        Assert.Contains("_mixerControl?.SessionAdoptedAudio(", source[start..end]);
    }
}
