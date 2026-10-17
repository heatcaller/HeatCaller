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

    /// <summary>
    /// And whether the relay was last told the night's list is full, so a
    /// list that fills - or empties - is sent rather than inferred.
    ///
    /// SEPARATE FROM THE OPEN FLAG because it changes on a different
    /// clock. Open changes about twice a season; full changes several
    /// times an evening as the race director reads notes out. Both ride on
    /// the same request - there is only one flag document on the relay -
    /// but either changing has to send it.
    /// </summary>
    private volatile bool _notesFullAsTold;

    /// <summary>
    /// Whether the switch was turned off and the relay has not yet taken
    /// the close. Cleared only when it has - see <see cref="SendNotesClose"/>.
    /// </summary>
    private volatile bool _notesClosePending;

    /// <summary>What went wrong collecting shout-outs from the relay, or null.</summary>
    public string? NotesProblem => _notesProblem;

    /// <summary>The problem last said out loud, so it is said once rather than every pass.</summary>
    private volatile string? _notesProblemAsSaid;

    /// <summary>
    /// Records what went wrong and says it once, on a change - the pattern
    /// VoteTrouble set (17 Oct 2026, Bug Hunt 8, A17-2). The problem was
    /// recorded in a field nothing read, so a route that silently stopped
    /// working on a race night left no trace in the console or the log.
    /// </summary>
    private void NotesTrouble(string? problem)
    {
        _notesProblem = problem;

        if (string.Equals(_notesProblemAsSaid, problem, StringComparison.Ordinal))
        {
            return;
        }

        _notesProblemAsSaid = problem;

        if (problem is { Length: > 0 })
        {
            Log?.Invoke($"[relay] Shout-outs from the club's page: {problem}");
        }
        else
        {
            Log?.Invoke("[relay] Shout-outs from the club's page is working again.");
        }
    }

    /// <summary>
    /// Clears the problem without saying anything, for the club switching
    /// the route off - not a fault, so no "working again" on its way out.
    /// </summary>
    private void ForgetNotesTrouble()
    {
        _notesProblem = null;
        _notesProblemAsSaid = null;
    }

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
            NotesTrouble(UnsafeAddressMessage);
            return;
        }

        // FULL IS SENT WITH OPEN, every poll where either has changed.
        // Without it the relay takes a note the night's list has no room
        // for, answers the sender yes, and this method collects it and
        // throws it away - see ClubNotes.NoRoomFromTheRoom.
        if (!TellTheRelayNotesAreOpen(target, club, wanted: true, full: notes.NoRoomFromTheRoom, token))
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
                NotesTrouble((int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for shout-outs. Check the club id on the Club Website page "
                      + "matches the relay's; if it does, the relay predates them - redeploy it (fly deploy --ha=false)."
                    : $"Could not collect shout-outs: the relay answered HTTP {(int)response.StatusCode}.");

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
                NotesTrouble(null);
                return;
            }

            if (!ConfirmClubNotesCollected(target, club, collected, token))
            {
                // Left on the relay, so they come again. Already filed, so
                // the duplicate is refused - which is exactly what the
                // store's duplicate rule is for.
                return;
            }

            NotesTrouble(null);

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
            NotesTrouble($"Could not collect shout-outs: {ex.Message}");
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
    private bool TellTheRelayNotesAreOpen(
        RelayTarget target,
        string club,
        bool wanted,
        bool full,
        CancellationToken token)
    {
        if (_notesOpenTold && _notesOpenAsTold == wanted && _notesFullAsTold == full)
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/notes/open");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { open = wanted, full }), Encoding.UTF8, "application/json");

            using var response = Send(request, token);

            if (!wanted && IsGoneForAClose(response))
            {
                // Nothing there to close (17 Oct 2026, Bug Hunt 8, A16-1).
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                NotesTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about shout-outs yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not open shout-outs on the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            _notesOpenAsTold = wanted;
            _notesFullAsTold = full;
            _notesOpenTold = true;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            NotesTrouble($"Could not reach the relay about shout-outs: {ex.Message}");
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

            NotesTrouble($"Collected shout-outs but could not clear them: the relay answered HTTP {(int)response.StatusCode}.");

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            NotesTrouble($"Collected shout-outs but could not clear them: {ex.Message}");
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
    ///
    /// THE WORKER SENDS IT, AND KEEPS SENDING IT - the pattern listening
    /// along and drivers' nights already use. This used to send once on a
    /// thread of its own and forget the answer, so an untick during a
    /// Wi-Fi blip left the relay taking shout-outs all night. It also sent
    /// nothing when this run had not opened it yet, when a previous run
    /// had. Now it is marked here and the worker's pass tries it, on the
    /// notes' own thirty-second tick, until the relay takes it.
    /// </summary>
    public void CloseClubNotesOnTheRelay()
    {
        _notesClosePending = true;

        // Tried on the next pass rather than after the rest of the tick.
        _nextNotesPollTick = 0;
        _wake.Set();
    }

    /// <summary>
    /// The close, from the worker's pass while the switch is off. Clears
    /// <see cref="_notesClosePending"/> only once the relay has it.
    /// </summary>
    private void SendNotesClose(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        // full: false with it, because a closed club has no list to be
        // full - and leaving the old value behind would have the relay
        // holding "off and full", which is two reasons to refuse where
        // there is one.
        if (TellTheRelayNotesAreOpen(target, club, wanted: false, full: false, token))
        {
            _notesClosePending = false;
        }
    }
}
