namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, built-in model: a line that runs out of time part-way
/// through is no longer spoken cut off mid-sentence (LLamaSharp ends the
/// stream quietly on a cancel rather than throwing), and a warm-up with no
/// model to load no longer claims it "warmed up in 0s but wrote nothing".
/// Real weights cannot be loaded in a test, so the token stream is faked.
/// </summary>
public sealed class BugHunt24_BundledLlmClientTests
{
    /// <summary>
    /// Yields the words given, then - as LLamaSharp 0.27.0 does when its
    /// token is cancelled during a decode - cancels and simply ends.
    /// </summary>
    private static async IAsyncEnumerable<string> StreamThatStopsQuietly(
        CancellationTokenSource cancelAfterWords,
        params string[] words)
    {
        foreach (var word in words)
        {
            await Task.Yield();
            yield return word;
        }

        cancelAfterWords.Cancel();
    }

    private static async IAsyncEnumerable<string> Stream(params string[] words)
    {
        foreach (var word in words)
        {
            await Task.Yield();
            yield return word;
        }
    }

    [Fact]
    public async Task BugHunt24_A_line_cut_off_by_its_deadline_is_a_cancel_not_a_half_sentence()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BundledLlmClient.CollectAsync(
                StreamThatStopsQuietly(cts, "And", " Sam", " Smith", " is"),
                maxTokens: 40,
                cts.Token));
    }

    [Fact]
    public async Task BugHunt24_A_finished_line_comes_back_whole_and_not_at_its_limit()
    {
        var (text, hitLimit) = await BundledLlmClient.CollectAsync(
            Stream("Here", " we", " go."),
            maxTokens: 40,
            CancellationToken.None);

        Assert.Equal("Here we go.", text);
        Assert.False(hitLimit);
    }

    [Fact]
    public async Task BugHunt24_A_line_that_used_every_token_still_reports_its_limit()
    {
        var (_, hitLimit) = await BundledLlmClient.CollectAsync(
            Stream("a", " b", " c"),
            maxTokens: 3,
            CancellationToken.None);

        Assert.True(hitLimit);
    }

    [Fact]
    public void BugHunt24_A_warm_up_with_no_model_loaded_has_nothing_to_report()
    {
        Assert.False(BundledLlmClient.WarmUpRanTheModel(unavailable: true));
        Assert.True(BundledLlmClient.WarmUpRanTheModel(unavailable: false));
    }

    [Fact]
    public async Task BugHunt24_Warm_up_with_no_model_file_says_why_once_and_never_claims_it_warmed_up()
    {
        using var client = new BundledLlmClient
        {
            ModelPathOverride = Path.Combine(Path.GetTempPath(), "no-such-model-" + Guid.NewGuid().ToString("N") + ".gguf"),
        };

        // Only meaningful on a PC with no model installed. Where one is (the
        // bundled copy or a downloaded one in LocalAppData), warming up would
        // load real weights, which a test must not do.
        if (client.ResolveModelPath() is not null)
        {
            return;
        }

        var lines = new List<string>();
        client.StatusMessage += lines.Add;

        await client.WarmUpAsync();

        Assert.Single(lines, l => l.Contains("is not being used"));
        Assert.DoesNotContain(lines, l => l.Contains("Warmed up") || l.Contains("wrote nothing"));
        Assert.NotNull(client.Unavailable);
        Assert.Equal(TimeSpan.Zero, client.Measured);
    }
}
