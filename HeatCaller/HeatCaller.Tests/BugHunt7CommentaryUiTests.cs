namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the Commentary and Voice Settings pages. What the hunt found:
///
///  - The Commentary page chose between "relay, then Ollama" and "Ollama
///    alone" once, when it built the client, from whether the Club Website
///    page was set up at that moment. A club that filled that page in
///    afterwards was told the relay writes first and got Ollama alone all
///    night. The page now always puts the relay in front when its box is
///    ticked, which leans on two things pinned down here: an unset relay
///    hands the line straight to Ollama, and the relay client reads the
///    settings afresh rather than once.
///  - The page's Play It, night recap and driver's night buttons were put
///    back up mid-run by any other change on the card, and the free voice
///    samples never reached the Voice Settings picker when they answered
///    before the catalogue. Both are window code and are not driven here.
/// </summary>
public sealed class BugHunt7CommentaryUiTests : IDisposable
{
    // Declared first: the relay client asks RestoredSettings, which reads
    // a file where this points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task A_relay_that_is_not_set_up_hands_the_line_to_ollama()
    {
        var relay = new RelayCommentaryClient(new AppSettings());
        var ollama = new Brain("Nose to tail into the hairpin!");
        var brain = new FallbackCommentaryBrain(relay, ollama);

        Assert.False(relay.RelayConfigured);

        var line = await brain.GenerateAsync("system", "facts", maxTokens: 40);

        Assert.Equal("Nose to tail into the hairpin!", line);
        Assert.Equal(1, ollama.Asked);
    }

    [Fact]
    public void The_relay_client_sees_the_club_website_page_filled_in_after_it_was_built()
    {
        var settings = new AppSettings();
        var relay = new RelayCommentaryClient(settings);

        Assert.False(relay.RelayConfigured);

        settings.RelayBaseUrl = "https://relay.club.invalid";
        settings.RelayClub = "myclub";
        settings.RelayToken = "token";

        Assert.True(relay.RelayConfigured);
    }

    /// <summary>Ollama, as far as the fallback can tell: answers every line with the same words.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly string _answer;

        public Brain(string answer) => _answer = answer;

        public int Asked { get; private set; }

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Asked++;
            StatusMessage?.Invoke("[test] Written.");

            return Task.FromResult<string?>(_answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>(_answer);
    }
}
