using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Car photos from drivers' phones, collected from the relay (5 Nov 2026,
/// asked for by Asher: drivers manage their garage from the relay, "add car,
/// all the text fields, picture, etc").
///
/// THE ASKS' SHAPE AGAIN: collect, file, then confirm. Each photo is written
/// to <see cref="PhonePhotos.Folder"/> under its relay id and filed as a
/// <see cref="DriverAsk.Photo"/> request naming the car; only once that
/// request is on disk is the relay told it can let the photo go. The desk
/// adds it to the car by itself (DriverRequestDesk.ApplyPhoto), checking the
/// whole picture, that the car is the driver's and that it has room.
///
/// At most <see cref="PhotosPerPass"/> a pass, so a phone that sent six never
/// holds up the rest of the worker's pass for long.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>How many photos are fetched in one pass; the rest wait fifteen seconds.</summary>
    private const int PhotosPerPass = 4;

    /// <summary>
    /// Queues one driver's garage document under their badge id (5 Nov 2026)
    /// - checked here as a night is (PublishDriverNight), so a document under
    /// an id of the wrong shape never reaches the relay.
    /// </summary>
    public bool PublishDriverGarage(string? driverId, string? json)
    {
        if (!DriverNight.IsADriverId(driverId) || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        return Enqueue(DriverGarage.Channel, driverId, json);
    }

    /// <summary>Reads the photos waiting, files each, and confirms what was filed. Quiet when there are none.</summary>
    private void CollectDriverPhotos(CancellationToken token)
    {
        if (TonightsDriverRequests?.Invoke() is not { } store)
        {
            return;
        }

        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        try
        {
            using var list = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/driver/photos");
            list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var listed = Send(list, token);

            // An older relay has no photos route: nothing to collect, and
            // nothing worth saying - the asks' own trouble line covers it.
            if (!listed.IsSuccessStatusCode)
            {
                return;
            }

            var waiting = PhonePhotos.ParseList(listed.Content.ReadAsStringAsync(token).GetAwaiter().GetResult());
            var confirm = new List<string>();

            foreach (var item in waiting.Take(PhotosPerPass))
            {
                if (store.Get(item.Id) is not null)
                {
                    confirm.Add(item.Id);
                    continue;
                }

                using var one = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/driver/photo/{item.Id}");
                one.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

                using var got = Send(one, token);

                if (!got.IsSuccessStatusCode)
                {
                    continue;
                }

                var bytes = PhonePhotos.BytesOf(got.Content.ReadAsStringAsync(token).GetAwaiter().GetResult());

                if (bytes is null)
                {
                    // Not a picture: confirmed so it goes, and never filed.
                    confirm.Add(item.Id);
                    continue;
                }

                if (!PhonePhotos.Keep(item.Id, bytes))
                {
                    continue;
                }

                var request = new DriverRequest
                {
                    Id = item.Id,
                    Badge = item.Badge,
                    Kind = DriverAsk.Photo,
                    Proof = item.Proof,
                    AskedUtc = item.AskedUtc,
                };

                request.Texts["car"] = item.Car;

                confirm.AddRange(store.FileAll(new[] { request }, DateTime.UtcNow, out _));
            }

            if (confirm.Count == 0)
            {
                return;
            }

            using var clear = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/driver/photos/collected");
            clear.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            clear.Content = new StringContent(JsonSerializer.Serialize(new { ids = confirm.Distinct(StringComparer.Ordinal).ToList() }), Encoding.UTF8, "application/json");

            using var cleared = Send(clear, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down, or the pass was cancelled.
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[relay] Could not collect drivers' car photos: {Plain(ex)}");
        }
    }
}

/// <summary>
/// Where a phone's car photo waits between the relay and the car (5 Nov
/// 2026), and the reading of what the relay sends. Pure apart from the
/// folder, so the tests can check every shape.
/// </summary>
internal static class PhonePhotos
{
    /// <summary>The most a photo may be - the relay's own limit.</summary>
    internal const int MaxBytes = 450 * 1024;

    /// <summary>The most photos a car takes from the phone.</summary>
    internal const int MostPerCar = 6;

    /// <summary>A folder of its own beside the Garage's, so nothing else ever reads from it.</summary>
    internal static string Folder => Path.Combine(AppPaths.Root, "PhonePhotos");

    /// <summary>One photo the relay says is waiting.</summary>
    internal sealed record Waiting(string Id, string Badge, string Car, string Proof, DateTime AskedUtc);

    /// <summary>The waiting list, oldest first, with anything of the wrong shape left out.</summary>
    internal static List<Waiting> ParseList(string json)
    {
        var found = new List<Waiting>();

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var item in items.EnumerateArray())
        {
            var id = StringOf(item, "id");
            var badge = StringOf(item, "b");
            var car = StringOf(item, "car");

            if (!IsHex(id, 24) || !DriverNight.IsADriverId(badge) || !IsHex(car, 32))
            {
                continue;
            }

            var at = DateTime.TryParse(StringOf(item, "at"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : DateTime.UtcNow;

            found.Add(new Waiting(id, badge, car, StringOf(item, "v") == DriverAsk.ByPin ? DriverAsk.ByPin : DriverAsk.ByBadge, at));
        }

        return found;
    }

    /// <summary>The JPEG out of one photo's answer, or null when it isn't one or is too big.</summary>
    internal static byte[]? BytesOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = StringOf(doc.RootElement, "data");

            if (data.Length == 0 || data.Length > (MaxBytes / 3 + 2) * 4)
            {
                return null;
            }

            var bytes = Convert.FromBase64String(data);

            return bytes.Length is > 3 and <= MaxBytes && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Where a waiting photo is kept, by its relay id - or null for an id of the wrong shape.</summary>
    internal static string? PathOf(string? id) => IsHex(id, 24) ? Path.Combine(Folder, id + ".jpg") : null;

    /// <summary>Writes a photo down until the desk adds it. False when it couldn't be.</summary>
    internal static bool Keep(string id, byte[] bytes)
    {
        if (PathOf(id) is not { } path)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Best-effort removal once a photo is on its car, or refused.</summary>
    internal static void Forget(string? id)
    {
        if (PathOf(id) is not { } path)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string StringOf(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
