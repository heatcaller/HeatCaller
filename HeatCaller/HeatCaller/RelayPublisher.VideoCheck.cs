using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Race video, V7 (14 Nov 2026, race video plan): the relay checks each of
/// tonight's YouTube videos with YouTube, and drops one that is private,
/// removed or has embedding turned off. This asks the relay which, every ten
/// minutes while video is on, so the Race video card can say "YouTube won't
/// play this one" beside that piece.
/// </summary>
public sealed partial class RelayPublisher
{
    private static readonly TimeSpan VideoCheckEvery = TimeSpan.FromMinutes(10);

    private long _nextVideoCheckTick;

    /// <summary>The video ids the relay last said YouTube refused. Read from any thread.</summary>
    public IReadOnlyCollection<string> DeadVideos { get; private set; } = Array.Empty<string>();

    internal static IReadOnlyCollection<string> ParseDeadVideos(string json)
    {
        var dead = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("dead", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && StreamTonight.VideoIdFrom(item.GetString() ?? "") is { } id)
                    {
                        dead.Add(id);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Nothing readable: nothing known to be dead.
        }

        return dead;
    }

    partial void CollectVideoCheckPass(CancellationToken token)
    {
        if (!_settings.VideoEnabled || Environment.TickCount64 < _nextVideoCheckTick)
        {
            return;
        }

        _nextVideoCheckTick = Environment.TickCount64 + (long)VideoCheckEvery.TotalMilliseconds;
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/video/check");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            using var response = Send(request, token);

            if (response.IsSuccessStatusCode)
            {
                var dead = ParseDeadVideos(response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult());

                if (dead.Count > 0 && !dead.ToHashSet(StringComparer.Ordinal).SetEquals(DeadVideos))
                {
                    Log?.Invoke($"[relay] YouTube won't play {dead.Count} of tonight's videos: private, removed or with embedding off. See Race video on the Relay tab.");
                }

                DeadVideos = dead;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception)
        {
            // Asked again in ten minutes; the card keeps what it last knew.
        }
    }
}
