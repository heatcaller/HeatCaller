using System.Net.Http.Headers;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Samples of the voices drivers may pick for their crew, on their way to
/// the relay (19 Nov 2026, driver crew plan 2.1) - see CrewSamples. Each is
/// sent once, under the voice's crew key and nothing else: no voice id, no
/// name, nobody's words.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>Whether crew samples can be sent: the relay set up and switched on.</summary>
    internal bool CanSendCrewSamples => CanUpload;

    /// <summary>
    /// The keys of the samples the relay already has for this club, or null
    /// when it could not be asked (not set up, no network, an older relay).
    /// </summary>
    internal async Task<HashSet<string>?> CrewSampleKeysAsync(CancellationToken cancel = default)
    {
        if (!CanUpload)
        {
            return null;
        }

        var target = _target;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/crew/samples");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await UploadHttp.SendAsync(request, cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return KeysFrom(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The relay's {"keys": [...]}, crew keys only; null when it will not read.</summary>
    internal static HashSet<string>? KeysFrom(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return keys.EnumerateArray()
                .Where(k => k.ValueKind == JsonValueKind.String)
                .Select(k => k.GetString())
                .Where(CrewSamples.IsAKey)
                .Select(k => k!)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sends one voice's sample. Null when it landed; otherwise why not, in the club's words.</summary>
    internal async Task<string?> UploadCrewSampleAsync(string key, byte[] mp3, CancellationToken cancel = default)
    {
        if (!CanUpload)
        {
            return "the relay isn't set up";
        }

        if (!CrewSamples.IsAKey(key) || mp3.Length == 0 || mp3.Length > CrewSamples.MaxBytes)
        {
            return "that sample can't be sent";
        }

        var target = _target;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/crew/sample/{key}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new ByteArrayContent(mp3);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");

            using var response = await UploadHttp.SendAsync(request, cancel).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var said = await ErrorFrom(response, cancel).ConfigureAwait(false);
            return (int)response.StatusCode == 404 && said is null
                ? "the relay does not keep crew samples yet - redeploy it"
                : $"HTTP {(int)response.StatusCode}{(said is null ? string.Empty : " - " + said)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
        {
            return Plain(ex);
        }
    }
}
