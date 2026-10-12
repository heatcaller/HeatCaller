using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Shout-outs collected from the relay - the half of the room that is not
/// in the room.
///
/// WHY BOTH ROUTES EXIST (12 Oct 2026)
///
/// HeatCaller's own web server already takes a dedication from a phone on
/// the club Wi-Fi, which covers everybody standing in the building. This
/// covers the ones who are not: a driver's family at home, somebody who
/// could not make it, the member who wants their mate's first race
/// mentioned. The club asked for both, and they are genuinely different
/// groups of people.
///
/// THE SHAPE: COLLECT, FILE, THEN CONFIRM
///
/// The relay holds each note until this laptop says it has it. So a poll
/// whose answer never arrived loses nobody's message - it is collected
/// again next time, and ClubNotes refuses one it already holds. The one
/// thing never done is confirming before filing: that would turn a crash
/// between the two into somebody's dedication quietly gone.
///
/// AND NOTHING COLLECTED HERE CAN BE SAID
///
/// Every note from this route is filed UNAPPROVED
/// (<see cref="ClubNotes.AddFromTheRoom"/>) and nothing unapproved is ever
/// offered to the announcer. The race director ticks it off on Race
/// Control, or the room never hears it. That gate is what makes a public
/// route to a PA a thing that can exist at all.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// How often the notes inbox is read.
    ///
    /// Thirty seconds, where check-in is four and sign-up ten. A
    /// dedication is not urgent: half a minute between somebody pressing
    /// send and it appearing on Race Control is nothing, the relay holds
    /// it safely in the meantime, and this is a request that comes back
    /// empty on almost every poll of almost every evening.
    /// </summary>
    private static readonly TimeSpan NotesPollEvery = TimeSpan.FromSeconds(30);

    /// <summary>When the notes inbox is next due - its own tick, not shared with check-in's or sign-up's.</summary>
    private long _nextNotesPollTick;

    /// <summary>What went wrong collecting notes, for the page to show. Null while it is working.</summary>
    private volatile string? _notesProblem;

    /// <summary>Whether the club has told the relay it wants them - so the flag is sent once rather than every poll.</summary>
    private volatile bool _notesOpenTold;

    /// <summary>The state last told to the relay, so a club switching it off is actually sent the change.</summary>
    private volatile bool _notesOpenAsTold;

    /// <summary>What went wrong collecting shout-outs from the relay, or null.</summary>
    public string? NotesProblem => _notesProblem;

    /// <summary>
    /// Where a collected note is filed. Set by MainForm to the one
    /// ClubNotes instance the announcer reads from; null until then, and
    /// then nothing is collected - an unfiled note would be acknowledged
    /// to the relay and then lost.
    /// </summary>
    /// <remarks>
    /// INTERNAL because <see cref="ClubNotes"/> is: a public property on a
    /// public class may not name an internal type, and this shipped as
    /// public on 12 Oct 2026 - CS0053, the whole solution. It matches the
    /// same hook on <see cref="WebServerService"/>, on RaceControlControl
    /// and on RelayPublisher.Vote, every one of which got it right and
    /// says why.
    /// </remarks>
    internal Func<ClubNotes?>? TonightsNotes { get; set; }

    /// <summary>
    /// Tells the relay whether this club takes shout-outs, and collects
    /// whatever it is holding.
    ///
    /// On the publisher's own background pass, like every other collector
    /// here - so nothing on this path touches the UI thread, and the
    /// filing goes through ClubNotes, which writes its own file.
    /// </summary>
    private void CollectClubNotes(CancellationToken token)
    {
        // Nowhere to put them. Collecting now would acknowledge notes that
        // then go nowhere, and an acknowledged note is one the relay
        // forgets - the same trap CollectCheckIns guards at the top.
        if (TonightsNotes?.Invoke() is not { } notes)
        {
            return;
        }

        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            _notesProblem = UnsafeAddressMessage;
            return;
        }

        if (!TellTheRelayNotesAreOpen(target, club, wanted: true, token))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/notes/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                _notesProblem = (int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for shout-outs. Check the club id on the Club Website page "
                      + "matches the relay's; if it does, the relay predates them - redeploy it (fly deploy --ha=false)."
                    : $"Could not collect shout-outs: the relay answered HTTP {(int)response.StatusCode}.";

                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();

            var collected = new List<string>();
            var filed = 0;
            var refused = 0;

            using (var doc = JsonDocument.Parse(text))
            {
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                            || !item.TryGetProperty("m", out var message) || message.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var from = item.TryGetProperty("n", out var who) && who.ValueKind == JsonValueKind.String
                            ? who.GetString()
                            : null;

                        // FILED FIRST, acknowledged after. A note refused
                        // by the store - too long, the night's list full,
                        // one already held - is still collected: it will
                        // be refused again next poll for the same reason,
                        // and leaving it on the relay forever is how an
                        // inbox stops being emptied at all.
                        if (notes.AddFromTheRoom(message.GetString(), from) is not null)
                        {
                            refused++;
                        }
                        else
                        {
                            filed++;
                        }

                        collected.Add(id.GetString() ?? string.Empty);
                    }
                }
            }

            if (collected.Count == 0)
            {
                _notesProblem = null;
                return;
            }

            if (!ConfirmClubNotesCollected(target, club, collected, token))
            {
                // Left on the relay, so they come again. Already filed, so
                // the duplicate is refused - which is exactly what the
                // store's duplicate rule is for.
                return;
            }

            _notesProblem = null;

            // Through Log, which is this class's one way of saying
            // anything - it has no StatusMessage of its own, and the
            // console is where a club looks afterwards to find out why a
            // dedication never went out.
            Log?.Invoke(
                $"[show] Collected {Plural.Of(filed, "shout-out")} from the club's page"
                + (refused > 0 ? $", and passed over {refused} the night's list had no room for" : string.Empty)
                + ". They are waiting on Race Control for you to approve.");
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the pass was cancelled. Nothing to report.
        }
        catch (Exception ex)
        {
            _notesProblem = $"Could not collect shout-outs: {ex.Message}";
        }
    }

    /// <summary>
    /// Tells the relay whether this club takes shout-outs at all.
    ///
    /// SENT ONCE, not every poll: the relay keeps the flag, and a request
    /// per poll for a value that changes about twice a season would be
    /// most of what this feature cost. Sent again whenever the setting
    /// changes, which <see cref="CloseClubNotesOnTheRelay"/> handles for
    /// the off direction.
    ///
    /// False when it could not be sent, and then nothing is collected -
    /// the relay would answer 404 anyway.
    /// </summary>
    private bool TellTheRelayNotesAreOpen(RelayTarget target, string club, bool wanted, CancellationToken token)
    {
        if (_notesOpenTold && _notesOpenAsTold == wanted)
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/notes/open");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { open = wanted }), Encoding.UTF8, "application/json");

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                _notesProblem = (int)response.StatusCode == 404
                    ? "The relay does not know about shout-outs yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not open shout-outs on the relay: it answered HTTP {(int)response.StatusCode}.";

                return false;
            }

            _notesOpenAsTold = wanted;
            _notesOpenTold = true;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _notesProblem = $"Could not reach the relay about shout-outs: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Says the laptop has them, so the relay can let them go.
    ///
    /// False when it could not be said, and then they stay on the relay
    /// and come again - which is the right way round. The other way, a
    /// failed confirmation would be indistinguishable from a successful
    /// one and somebody's dedication would be gone.
    /// </summary>
    private bool ConfirmClubNotesCollected(
        RelayTarget target,
        string club,
        IReadOnlyList<string> ids,
        CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/notes/collected");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { ids }), Encoding.UTF8, "application/json");

            using var response = Send(request, token);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _notesProblem = $"Collected shout-outs but could not clear them: the relay answered HTTP {(int)response.StatusCode}.";

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _notesProblem = $"Collected shout-outs but could not clear them: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Tells the relay the club no longer wants shout-outs, which also
    /// throws away anything it was holding.
    ///
    /// Called when the setting is switched off rather than left to the
    /// poll loop, because the poll loop stops asking the moment it is off
    /// - so without this the relay would go on taking strangers' messages
    /// for a club that had declined them, and holding them.
    /// </summary>
    public void CloseClubNotesOnTheRelay()
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        // Nothing to close if the relay was never told it was open.
        if (!_notesOpenTold || !_notesOpenAsTold)
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        // On a thread of its own: this is called from a settings page, and
        // a page that waited for a network round trip to redraw a tick box
        // is a page that appears to have frozen.
        Task.Run(() => TellTheRelayNotesAreOpen(target, club, wanted: false, CancellationToken.None));
    }
}
