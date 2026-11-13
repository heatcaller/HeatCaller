namespace HeatCaller;

/// <summary>
/// Marshals from their own phones (12 Nov 2026, setup plan step 8):
///
///  - "post": I'm at my post - Race Control records them in place for that
///    round, and when every marshal of the next race has said so the auto
///    start stops waiting for them (AutoStartClock.Posts).
///  - "swap": ask a named driver to cover my post - filed by itself, since it
///    only asks; who was asked, the race and the post are noted on it.
///  - "cover": the asked driver's yes or no. A no is filed by itself. A yes is
///    the swap itself (RoundDefinition.MarshalCovers): made by itself when
///    the club has turned "Marshal swaps from phones need the desk's OK" off,
///    otherwise left in Driver Requests for the desk to approve.
///
/// The badge (and the driver's own PIN, when they have one) has already been
/// checked by the relay, so the badge is who they are. See MarshalSwaps.
/// </summary>
internal sealed partial class DriverRequestDesk
{
    /// <summary>Race Control records a marshal in place: (round id, main transponder) to whether it did and what to tell them. Set by the host.</summary>
    public Func<string, string, (bool Ok, string Said)>? MarshalAtPost { get; set; }

    /// <summary>A driver's marshal duty in a round, by main transponder, or null when they have none there. Set by the host.</summary>
    public Func<string, string, MarshalSwaps.Duty?>? DutyOf { get; set; }

    /// <summary>Who may be asked to cover a marshal's post in a round: (round id, asker's main) to (main, badge) of each. Set by the host.</summary>
    public Func<string, string, IReadOnlyList<(string Main, string Badge)>>? CoverCandidates { get; set; }

    /// <summary>Makes a swap: (round id, the marshal's main, the cover's main) to null when done, or why not. Set by the host.</summary>
    public Func<string, string, string, string?>? ApplyMarshalCover { get; set; }

    /// <summary>
    /// A swap two phones made by themselves (desk approval off): the round id,
    /// the cover's main transponder and what it did ("Sam P. is covering Max
    /// P.'s post (Turn 3) in Q2 Heat 3"). The host tells Race Control when it
    /// was a late one (MarshalSwapNotices) - 13 Nov 2026.
    /// </summary>
    public Action<string, string, string>? MarshalSwappedByPhones { get; set; }

    /// <summary>A yes to covering a post that couldn't be made: the notice for Race Control (MarshalSwapNotices.FailedNotice) - 13 Nov 2026.</summary>
    public Action<string>? MarshalSwapFailed { get; set; }

    /// <summary>"Marshal swaps from phones need the desk's OK" - the Driver Requests window's tick.</summary>
    public bool SwapsNeedDesk
    {
        get => _settings.MarshalSwapsNeedDesk;
        set
        {
            if (_settings.MarshalSwapsNeedDesk != value)
            {
                _settings.MarshalSwapsNeedDesk = value;
                _settings.Save();
            }
        }
    }

    /// <summary>A yes to covering a post while the club wants the desk to OK swaps: it waits in Driver Requests rather than being made by itself.</summary>
    internal bool NeedsDesk(DriverRequest request) =>
        request.Kind == DriverAsk.Cover && request.Text("answer") == "yes" && _settings.MarshalSwapsNeedDesk;

    /// <summary>The "post" ask: Race Control has them at their post.</summary>
    internal bool ApplyPost(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var (ok, words) = MarshalAtPost!(request.Text("round"), profile.Transponder);
        said = words;

        if (ok)
        {
            _log("[driver requests] A marshal said from their phone they're at their post.");
        }

        return ok;
    }

    /// <summary>The "swap" ask: who they asked is worked out from the key and noted, and the asked driver's phone offers Accept or Decline.</summary>
    internal bool ApplySwap(DriverRequest request, out string said)
    {
        if (DriverOf(request) is not { } profile)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        var round = request.Text("round");

        if (DutyOf!(round, profile.Transponder) is not { } duty)
        {
            said = "You're not marshalling that race any more - have a look at your duty.";
            return false;
        }

        var key = request.Text("to");
        var asked = CoverCandidates!(round, profile.Transponder).FirstOrDefault(c => MarshalSwaps.KeyOf(c.Badge) == key);

        if (asked.Badge is null)
        {
            said = "They can't cover it - they're racing in it, marshalling it already, or not checked in tonight.";
            return false;
        }

        var name = MarshalSwaps.ShortName(_profiles.Get(asked.Main)?.DriverName);

        _store.Note(request.Id, new Dictionary<string, string>
        {
            ["toBadge"] = asked.Badge,
            ["title"] = duty.Title,
            ["post"] = duty.Post,
        });

        said = $"Asked {(name.Length > 0 ? name : "them")} to cover {PostWords(duty.Post)} - waiting for them to say yes.";
        _log("[driver requests] A marshal asked another driver from their phone to cover their post.");
        return true;
    }

    /// <summary>The "cover" ask, made by itself: a no, or a yes while the club doesn't need the desk's OK.</summary>
    internal bool ApplyCover(DriverRequest request, out string said)
    {
        if (OfferFor(request, out said) is not { } offer)
        {
            return false;
        }

        if (request.Text("answer") != "yes")
        {
            said = $"OK - you're not covering {PostWords(offer.Text("post"))} in {offer.Text("title")}.";
            return true;
        }

        if (!MakeCover(request, offer, out said, out var made, out var failed))
        {
            // Both drivers agreed and think it's sorted: the desk has to know
            // the original marshal is still on the post (13 Nov 2026).
            if (failed is not null)
            {
                MarshalSwapFailed?.Invoke(failed);
            }

            return false;
        }

        MarshalSwappedByPhones?.Invoke(offer.Text("round"), DriverOf(request)!.Transponder, made);
        return true;
    }

    /// <summary>The desk approves a yes to covering a post: the swap is made, and both phones follow it.</summary>
    public bool ApproveCover(DriverRequest request, out string refusal)
    {
        if (request.Kind != DriverAsk.Cover || !_store.IsWaiting(request.Id))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        if (ApplyMarshalCover is null)
        {
            refusal = "The program isn't open, so the swap can't be made yet.";
            return false;
        }

        if (OfferFor(request, out refusal) is not { } offer)
        {
            return false;
        }

        if (!MakeCover(request, offer, out var said, out _, out _))
        {
            // Why not, or the desk's "Not swapped" box opens with nothing in it.
            refusal = said;
            return false;
        }

        if (!_store.Answer(request.Id, DriverAsk.Approved, said, DateTime.UtcNow))
        {
            refusal = "That request has already been answered.";
            return false;
        }

        refusal = string.Empty;
        return true;
    }

    /// <summary>The swap a cover answers, still open and asked of this driver - or null, with what to tell them.</summary>
    private DriverRequest? OfferFor(DriverRequest request, out string said)
    {
        var offer = _store.Get(request.Text("offer"));

        if (offer is not { Kind: DriverAsk.Swap, State: DriverAsk.Approved }
            || offer.Text("toBadge") != request.Badge
            || request.AskedUtc - offer.AskedUtc > MarshalSwaps.OffersLast)
        {
            said = "That ask to cover isn't open any more.";
            return null;
        }

        // A newer ask by the same marshal for the same race replaced this one
        // (MarshalSwaps.Offers): a yes from a page that hasn't caught up must
        // not take the post from whoever was asked instead.
        var round = offer.Text("round");

        if (_store.All.Any(r => r.Kind == DriverAsk.Swap && r.State == DriverAsk.Approved
                                && r.Badge == offer.Badge && r.Text("round") == round
                                && r.Text("toBadge").Length > 0 && r.AskedUtc > offer.AskedUtc))
        {
            said = "That ask to cover isn't open any more.";
            return null;
        }

        said = string.Empty;
        return offer;
    }

    /// <param name="made">What the swap did, for the desk (MarshalSwapNotices.Made) - empty unless made.</param>
    /// <param name="failed">The desk's notice when both drivers agreed but the swap couldn't be made - null otherwise.</param>
    private bool MakeCover(DriverRequest request, DriverRequest offer, out string said, out string made, out string? failed)
    {
        made = string.Empty;
        failed = null;

        if (DriverOf(request) is not { } cover)
        {
            said = "HeatCaller doesn't know your badge - ask at the desk.";
            return false;
        }

        if (DriverOf(offer) is not { } owner)
        {
            said = "HeatCaller doesn't know who asked you any more - ask at the desk.";
            return false;
        }

        // No full stop of its own: the short name ends in one ("Max P.").
        var ownerName = MarshalSwaps.ShortName(owner.DriverName);
        var coverName = MarshalSwaps.ShortName(cover.DriverName);

        var round = offer.Text("round");

        // The ask was checked when it was sent, but the yes can come a good
        // while later - and after a yes to somebody else's post in the same
        // race - so who marshals and who is free are read again here.
        if (DutyOf is { } dutyOf && dutyOf(round, owner.Transponder) is null)
        {
            said = $"The swap can't be made - {(ownerName.Length > 0 ? ownerName : "the marshal")} isn't marshalling that race any more.";
            return false;
        }

        var refusal = CoverCandidates is { } candidates
                      && !candidates(round, owner.Transponder).Any(c => string.Equals(c.Main, cover.Transponder, StringComparison.OrdinalIgnoreCase))
            ? "The swap can't be made - the cover is racing in that race, marshalling it already, or not checked in tonight."
            : ApplyMarshalCover!(round, owner.Transponder, cover.Transponder);

        if (refusal is not null)
        {
            said = refusal;
            failed = MarshalSwapNotices.FailedNotice(coverName, ownerName, offer.Text("post"), offer.Text("title"), refusal);
            return false;
        }

        made = MarshalSwapNotices.Made(coverName, ownerName, offer.Text("post"), offer.Text("title"));
        said = $"You're covering {PostWords(offer.Text("post"))} in {offer.Text("title")} for {ownerName}" + (ownerName.EndsWith('.') ? string.Empty : ".");
        _log("[driver requests] A marshal swap from two phones was made.");
        return true;
    }

    private static string PostWords(string post) => string.IsNullOrWhiteSpace(post) ? "their post" : post.Trim();

    /// <summary>The desk's list line for the marshals' kinds.</summary>
    internal string MarshalWords(DriverRequest request)
    {
        switch (request.Kind)
        {
            case DriverAsk.Post:
                return "At their marshal post";

            case DriverAsk.Swap:
                var asked = request.Text("toBadge") is { Length: > 0 } badge && _transponderOf(badge) is { } t && _profiles.Get(t) is { } p
                    ? NameOf(p)
                    : "another driver";
                return $"Asked {asked} to cover {PostWords(request.Text("post"))}" + (request.Text("title").Length > 0 ? $" in {request.Text("title")}" : string.Empty);

            default:
                var offer = _store.Get(request.Text("offer"));
                var from = offer is not null && DriverOf(offer) is { } owner ? NameOf(owner) : "another driver";
                var where = offer is null ? "their post" : $"{PostWords(offer.Text("post"))} in {offer.Text("title")}";
                return request.Text("answer") == "yes"
                    ? $"Covers {from}'s post: {where}"
                    : $"Said no to covering {from}'s post: {where}";
        }
    }
}
