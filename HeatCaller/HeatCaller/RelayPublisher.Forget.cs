using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Forget this driver, on the relay (15 Nov 2026, children's-privacy plan
/// 2.5): POST driver/forget with their badge codes and names, under the
/// club's publish token like every other laptop-to-relay call. The relay
/// deletes their night, live and details entries, garage, phone photos,
/// asks, family links, push sign-ups and clips by badge code, and the
/// follower sign-ups for each name. See relay/src/server.ts forgetDriver.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>What the relay takes at most of each, so one call is always enough.</summary>
    internal const int ForgetMaxBadges = 20;

    internal const int ForgetMaxNames = 10;

    /// <summary>The body of a driver/forget call.</summary>
    internal static string ForgetBody(IEnumerable<string> badges, IEnumerable<string> names) =>
        JsonSerializer.Serialize(new
        {
            badges = badges.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).Take(ForgetMaxBadges).ToList(),
            names = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(ForgetMaxNames).ToList(),
        });

    /// <summary>
    /// Tells the relay to forget one driver. Ok only when the relay said it
    /// had; a relay that isn't set up, or can't be reached, is not Ok, and
    /// the caller keeps it owed (AppSettings.RelayForgetsOwed).
    /// </summary>
    public async Task<RelayProbe> ForgetDriverAsync(IReadOnlyList<string> badges, IReadOnlyList<string> names, CancellationToken cancel = default)
    {
        var target = _target;

        if (!target.Configured)
        {
            return new RelayProbe(false, "The club website isn't set up, so there is nothing online to remove.");
        }

        if (!IsSafeForToken(target.Root))
        {
            return new RelayProbe(false, UnsafeAddressMessage);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{Uri.EscapeDataString(target.Club)}/driver/forget");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(ForgetBody(badges, names), Encoding.UTF8, "application/json");

            using var response = await SendAsync(request, cancel).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? new RelayProbe(true, "Removed from the club website too.")
                : new RelayProbe(false, $"The club website refused to remove them (HTTP {(int)response.StatusCode}). HeatCaller will keep trying.");
        }
        catch (Exception ex)
        {
            return new RelayProbe(false, $"Couldn't reach the club website ({Plain(ex)}). HeatCaller will keep trying.");
        }
    }
}
