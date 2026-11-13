namespace HeatCaller;

/// <summary>
/// Marshals from their own phones (12 Nov 2026, setup plan step 8): what the
/// Driver Requests desk needs to file "I'm at my post", "Ask someone to cover"
/// and the yes that makes a swap - all on the UI thread, where the desk files
/// its writes (the roster tick).
/// </summary>
public partial class MainForm
{
    /// <summary>Joins the desk to Race Control and the program. Called from WireDriverRequests.</summary>
    private void WireMarshalsFromPhones(DriverRequestDesk desk)
    {
        desk.MarshalAtPost = (round, transponder) => _raceControlControl is { } raceControl
            ? raceControl.MarshalAtPost(round, transponder)
            : (false, "Race Control isn't open, so nothing was recorded.");

        desk.DutyOf = MarshalDutyOf;
        desk.CoverCandidates = CoverCandidatesFor;
        desk.ApplyMarshalCover = ApplyMarshalCoverFromPhones;

        // Swaps the phones make by themselves (13 Nov 2026): an early one is
        // in the log already; a late one, or a yes that couldn't be made, is
        // put in front of the director (MarshalSwapNotices).
        desk.MarshalSwappedByPhones = (roundId, cover, made) =>
        {
            if (_raceControlControl is { } raceControl && raceControl.MarshalSwapLateness(roundId, DateTime.UtcNow) is { } late)
            {
                var name = MarshalSwaps.ShortName(_profiles.Get(cover)?.DriverName);
                raceControl.ShowMarshalSwapNotice(MarshalSwapNotices.LateNotice(made, late, name));
            }
        };
        desk.MarshalSwapFailed = notice =>
        {
            if (_raceControlControl is { } raceControl)
            {
                raceControl.ShowMarshalSwapNotice(notice);
            }
            else
            {
                OnClientRawMessage($"[marshals] {notice}");
            }
        };
    }

    /// <summary>A driver's (main transponder's) marshal duty in a round, or null.</summary>
    private MarshalSwaps.Duty? MarshalDutyOf(string roundId, string main)
    {
        if (_rounds is null)
        {
            return null;
        }

        var duty = MarshalAssigner.DutiesForDay(_rounds, _raceEvents, _trackLayouts, _profiles)
            .FirstOrDefault(d => d.RoundId == roundId && string.Equals(MainOfTransponder(d.Transponder), main, StringComparison.OrdinalIgnoreCase));

        return duty is null ? null : new MarshalSwaps.Duty(duty.RoundTitle, duty.PostName);
    }

    /// <summary>Tonight's checked-in drivers with a badge who can cover a post in a round: not driving it, not marshalling it, not the asker.</summary>
    private IReadOnlyList<(string Main, string Badge)> CoverCandidatesFor(string roundId, string asker)
    {
        if (_rounds?.Get(roundId) is not { } round)
        {
            return Array.Empty<(string, string)>();
        }

        var badges = DriverLiveBadgeMap();
        var marshalling = MarshalAssigner.DutiesForDay(_rounds, _raceEvents, _trackLayouts, _profiles)
            .Where(d => d.RoundId == roundId)
            .Select(d => MainOfTransponder(d.Transponder));

        return MarshalSwaps.Candidates(
                _checkInStore.SnapshotCheckedIn().Select(MainOfTransponder),
                round.AssignedTransponders.Select(MainOfTransponder),
                marshalling,
                asker)
            .Where(badges.ContainsKey)
            .Select(t => (t, badges[t]))
            .ToList();
    }

    private string MainOfTransponder(string transponder) =>
        _profiles.Get(transponder)?.Transponder is { Length: > 0 } main ? main.Trim() : transponder.Trim();

    /// <summary>Makes a swap two phones agreed: the cover goes on the marshal's post for the round. One undo. Null when done, or why not.</summary>
    private string? ApplyMarshalCoverFromPhones(string roundId, string marshal, string cover)
    {
        if (_rounds?.Get(roundId) is not { } round)
        {
            return "That race isn't in tonight's program any more.";
        }

        if (!string.IsNullOrWhiteSpace(round.LinkedSessionId))
        {
            return $"{round.RoundLabel} has already run.";
        }

        // The desk knows the marshal by their main transponder, but the post
        // is held under the one they were gridded on in the heat before -
        // their second car's, perhaps - and covers are looked up by that one
        // (MarshalAssigner.Assign). Filed under the main, the swap would be
        // reported as made and change nothing.
        var held = MarshalAssigner.DutiesForDay(_rounds, _raceEvents, _trackLayouts, _profiles)
            .FirstOrDefault(d => d.RoundId == roundId && string.Equals(MainOfTransponder(d.Transponder), marshal, StringComparison.OrdinalIgnoreCase))
            ?.Transponder;

        if (held is null)
        {
            return $"{(_profiles.Get(marshal) is { DriverName.Length: > 0 } who ? who.DriverName : "The marshal")} isn't marshalling {round.RoundLabel} any more.";
        }

        var before = round.MarshalCovers is null ? null : new Dictionary<string, string>(round.MarshalCovers, StringComparer.OrdinalIgnoreCase);

        if (MarshalSwaps.Cover(round, held, cover) is { } refusal)
        {
            return refusal;
        }

        _rounds.Update(round);

        _undo.Capture($"Marshal swap in {round.RoundLabel} (from phones)", () =>
        {
            if (_rounds.Get(round.Id) is { } current)
            {
                current.MarshalCovers = before;
                _rounds.Update(current);
            }
        });

        string NameOf(string t) => _profiles.Get(t) is { DriverName.Length: > 0 } p ? p.DriverName : t;

        OnClientRawMessage($"[program] {NameOf(cover)} covers {NameOf(marshal)}'s marshal post for {round.RoundLabel} - agreed on their phones.");
        return null;
    }
}
