using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// IS THE SHORT ADDRESS FREE? (19 Nov 2026, driver phone plan item 5.) A
/// club's short address (/c/upstateminiz) is set on the Club Website page,
/// and the relay gives it to the first club that claims it. It used to
/// ignore a taken one quietly, so a club could print posters with an address
/// that went to somebody else. Now the relay says (GET .../alias?name=), and
/// the page says so beside the box.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>What the relay said about a short address.</summary>
    internal enum AliasState
    {
        /// <summary>Couldn't ask: publishing isn't set up, or the relay didn't answer.</summary>
        Unknown,

        /// <summary>It already sends drivers to this club.</summary>
        Yours,

        /// <summary>Nobody has it: it will be this club's once published.</summary>
        Free,

        /// <summary>Another club's id, or another club claimed it first.</summary>
        Taken,

        /// <summary>Not a short address at all (letters, digits and hyphens, 3 to 32).</summary>
        Bad,
    }

    /// <summary>The relay's answer, read.</summary>
    internal static AliasState ParseAliasState(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("state", out var state)
                && state.ValueKind == JsonValueKind.String)
            {
                return state.GetString() switch
                {
                    "yours" => AliasState.Yours,
                    "free" => AliasState.Free,
                    "taken" => AliasState.Taken,
                    "bad" => AliasState.Bad,
                    _ => AliasState.Unknown,
                };
            }
        }
        catch (JsonException)
        {
            // Not an answer.
        }

        return AliasState.Unknown;
    }

    /// <summary>The words beside the box for each answer; empty for none.</summary>
    internal static string AliasStateText(AliasState state, string alias) => state switch
    {
        AliasState.Yours => $"/c/{alias} goes to this club's pages.",
        AliasState.Free => $"/c/{alias} is free - it's yours once the club's pages update.",
        AliasState.Taken => $"/c/{alias} is taken by another club. Pick another one - this one won't work.",
        AliasState.Bad => "Use 3 to 32 lower-case letters, digits and hyphens.",
        _ => string.Empty,
    };

    /// <summary>Asks the relay about a short address, off the UI thread. Never throws.</summary>
    internal async Task<AliasState> CheckAliasAsync(string alias, CancellationToken cancel = default)
    {
        var target = _target;

        if (string.IsNullOrWhiteSpace(alias) || !IsSafeForToken(target.Root) || string.IsNullOrEmpty(target.Token))
        {
            return AliasState.Unknown;
        }

        try
        {
            var club = Uri.EscapeDataString(target.Club);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{target.Root}/api/v1/c/{club}/alias?name={Uri.EscapeDataString(alias.Trim().ToLowerInvariant())}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await SendAsync(request, cancel).ConfigureAwait(false);

            // An older relay has no such route: nothing to say.
            if (!response.IsSuccessStatusCode)
            {
                return AliasState.Unknown;
            }

            return ParseAliasState(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch
        {
            return AliasState.Unknown;
        }
    }
}
