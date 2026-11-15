namespace HeatCaller;

/// <summary>
/// Drivers' requests from their phones in the Desk Inbox - what Driver
/// Requests did (22 Oct 2026, driver experience, onwards), moved here with
/// the same dialogs and the same desk calls: DriverRequestDesk does the
/// work, and owns every answer that reaches a phone.
/// </summary>
internal sealed partial class DeskInboxForm
{
    /// <summary>The kinds Approve answers, as Driver Requests had it.</summary>
    internal static bool Approvable(string kind) =>
        kind is DriverAsk.Details or DriverAsk.Pack or DriverAsk.Late or DriverAsk.Skip or DriverAsk.Cover or DriverAsk.VideoHide or DriverAsk.Crew;

    /// <summary>The kinds Noted answers.</summary>
    internal static bool Notable(string kind) => kind is DriverAsk.Query or DriverAsk.VideoOff;

    /// <summary>
    /// The kinds several can be approved or noted at once: those whose answer
    /// needs no form of its own. Details (a form each, and Hear It) and crew
    /// (the words read first) are one at a time.
    /// </summary>
    internal static bool Batchable(string kind) =>
        kind is DriverAsk.Pack or DriverAsk.Late or DriverAsk.Skip or DriverAsk.Cover or DriverAsk.VideoHide or DriverAsk.Query or DriverAsk.VideoOff;

    /// <summary>Waiting requests, read from the desk, in the desk's own order (protest-window queries first) before the inbox's sort.</summary>
    private IEnumerable<InboxItem> RequestItems()
    {
        if (_requests is not { } desk)
        {
            yield break;
        }

        // Queries asked inside a protest window first (12 Nov 2026, setup
        // plan step 8): their answer can still change a result before it is
        // final - so they count as race-blocking here too.
        foreach (var request in ProtestWindow.DeskOrder(desk.Store.All.Where(r => r.IsWaiting), desk.InProtestWindow))
        {
            var blocking = DeskInbox.RaceBlockingKind(request.Kind)
                || (request.Kind == DriverAsk.Query && desk.InProtestWindow(request));

            yield return new InboxItem(
                InboxSource.Request,
                request.Id,
                request.Kind,
                DriverRequestDesk.KindWords(request.Kind),
                desk.NameFor(request),
                desk.Summary(request),
                desk.NoteFor(request),
                request.AskedUtc,
                blocking,
                request);
        }
    }

    private void UpdateRequestButtons(InboxItem item, List<InboxItem>? batch)
    {
        var request = (DriverRequest)item.Payload!;
        var known = _requests?.DriverOf(request) is not null;
        var many = batch is not null;

        _btnApprove.Text = "Approve";
        _btnDecline.Text = "Decline";
        _btnApprove.Enabled = known && Approvable(item.Kind) && (!many || Batchable(item.Kind));
        _btnNoted.Enabled = Notable(item.Kind);
        _btnDecline.Enabled = true;
        _btnDiscard.Enabled = !many && !known;
        _btnDiscard.Visible = true;
        _btnRemove.Visible = false;
        _btnNoted.Visible = true;
    }

    /// <summary>Approves one, or several of one kind, through the desk - with the question each always had.</summary>
    private void ApproveRequests(List<InboxItem> items)
    {
        if (_requests is not { } desk)
        {
            return;
        }

        // Read again: the list is up to a second old, and a newer request
        // may have replaced one since.
        var current = items
            .Select(i => desk.Store.Get(i.Id))
            .OfType<DriverRequest>()
            .Where(r => r.IsWaiting)
            .ToList();

        if (current.Count == 0)
        {
            return;
        }

        if (current.Count > 1)
        {
            ApproveManyRequests(desk, current);
            return;
        }

        var one = current[0];
        var before = NewestUndo();
        string refusal;

        switch (one.Kind)
        {
            case DriverAsk.Details:
                if (desk.DriverOf(one) is not { } profile)
                {
                    return;
                }

                using (var form = new ApproveDriverRequestForm(desk, one, profile, _profiles, _speakName, _checkIns))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.Result is not { } choice)
                    {
                        return;
                    }

                    if (!desk.ApplyDetails(one, choice, out refusal))
                    {
                        AppDialog.Show(this, refusal, "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                break;

            // Their own crew and notes (15 Nov 2026): the words, all at
            // once - the voices have applied by themselves already.
            case DriverAsk.Crew:
                if (desk.DriverOf(one) is not { } crewDriver)
                {
                    return;
                }

                if (AppDialog.Show(this,
                        $"{desk.NameFor(one)} asks for:{Environment.NewLine}{Environment.NewLine}"
                        + string.Join(Environment.NewLine, desk.Describe(one))
                        + Environment.NewLine + Environment.NewLine
                        + (crewDriver.IsJunior ? DriverRequestDesk.JuniorCrewWarning + Environment.NewLine + Environment.NewLine : string.Empty)
                        + "These are words the announcer will say. Approve them?",
                        "Crew and notes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                if (!desk.ApplyCrew(one, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;

            // No "Approve it?" since 15 Nov 2026: the desk can undo a pack, so
            // the toast offers Undo instead. (Late, skip, a marshal swap and
            // hiding a video have no undo, and still ask.)
            case DriverAsk.Pack:
                if (desk.DriverOf(one) is null)
                {
                    return;
                }

                if (!desk.ApprovePack(one, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;

            // Race video (14 Nov 2026, V5, V6).
            case DriverAsk.VideoHide:
                if (!desk.ApproveVideoHide(one, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not hidden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;

            // More time, or skip my race (11 Nov 2026): Race Control acts on it.
            case DriverAsk.Late:
            case DriverAsk.Skip:
                if (desk.DriverOf(one) is null)
                {
                    return;
                }

                if (AppDialog.Show(this, $"{desk.Summary(one)} - for {desk.NameFor(one)}. Approve it?",
                        DriverRequestDesk.KindWords(one.Kind), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                if (!desk.ApproveRaceAsk(one, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;

            // A marshal swap two drivers agreed on their phones (12 Nov 2026,
            // setup plan step 8): approved, the program makes it.
            case DriverAsk.Cover:
                if (desk.DriverOf(one) is null)
                {
                    return;
                }

                if (AppDialog.Show(this, $"{desk.NameFor(one)}: {desk.Summary(one)}. Make the swap?",
                        "Marshal swap", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                if (!desk.ApproveCover(one, out refusal))
                {
                    AppDialog.Show(this, refusal, "Not swapped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                break;

            default:
                return;
        }

        Toast($"Approved: {desk.NameFor(one)} - {DriverRequestDesk.KindWords(one.Kind)}. Their phone has been told.", UndoForSteps(StepsSince(before)));
    }

    /// <summary>
    /// Several of one kind at once: one question naming every one of them,
    /// then each through the desk exactly as if approved alone.
    /// </summary>
    private void ApproveManyRequests(DriverRequestDesk desk, List<DriverRequest> current)
    {
        var kind = current[0].Kind;
        var nl = Environment.NewLine;
        var lines = string.Join(nl, current.Select(r => $"  {desk.NameFor(r)}: {desk.Summary(r)}"));

        // Packs can be undone, so they don't ask; the rest ask once for all of them.
        if (kind != DriverAsk.Pack
            && AppDialog.Show(this, $"Approve all {current.Count}?{nl}{nl}{lines}",
                DriverRequestDesk.KindWords(kind), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var refused = new List<string>();
        var done = 0;
        var before = NewestUndo();

        foreach (var one in current)
        {
            if (desk.DriverOf(one) is null)
            {
                refused.Add($"{desk.NameFor(one)}: not a driver on this laptop");
                continue;
            }

            string refusal;
            var ok = kind switch
            {
                DriverAsk.Pack => desk.ApprovePack(one, out refusal),
                DriverAsk.Late or DriverAsk.Skip => desk.ApproveRaceAsk(one, out refusal),
                DriverAsk.Cover => desk.ApproveCover(one, out refusal),
                DriverAsk.VideoHide => desk.ApproveVideoHide(one, out refusal),
                _ => Refuse(out refusal),
            };

            if (ok)
            {
                done++;
            }
            else
            {
                refused.Add($"{desk.NameFor(one)}: {refusal}");
            }
        }

        if (refused.Count > 0)
        {
            AppDialog.Show(this, $"{Plural.Of(done, "request")} approved. Not these:{nl}{nl}{string.Join(nl, refused)}",
                "Not all approved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            Toast($"Approved {Plural.Of(done, "request")}: {DriverRequestDesk.KindWords(kind)}. Their phones have been told.", UndoForSteps(StepsSince(before)));
        }
    }

    private static bool Refuse(out string refusal)
    {
        refusal = "That kind can only be approved one at a time.";
        return false;
    }

    /// <summary>Noted: a result query or a "no video of me" - seen, and the phone is told so.</summary>
    private void NoteRequests(List<InboxItem> items)
    {
        if (_requests is not { } desk)
        {
            return;
        }

        var done = 0;
        var before = NewestUndo();

        foreach (var request in items.Select(i => desk.Store.Get(i.Id)).OfType<DriverRequest>().Where(r => r.IsWaiting))
        {
            string refusal;
            var ok = request.Kind switch
            {
                DriverAsk.Query => desk.Note(request, out refusal),
                DriverAsk.VideoOff => desk.NoteVideoOff(request, out refusal),
                _ => Refuse(out refusal),
            };

            if (!ok)
            {
                AppDialog.Show(this, refusal, "Not noted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            done++;
        }

        if (done > 0)
        {
            Toast(done == 1 ? "Noted. Their phone has been told." : $"{Plural.Of(done, "request")} noted. Their phones have been told.", UndoForSteps(StepsSince(before)));
        }
    }

    /// <summary>
    /// Declines one or several, with a quick reply or (null) the desk's own
    /// reason asked for - the same reason form Driver Requests had.
    /// </summary>
    private void DeclineRequests(List<InboxItem> items, string? reply)
    {
        if (_requests is not { } desk)
        {
            return;
        }

        var requests = items.Select(i => desk.Store.Get(i.Id)).OfType<DriverRequest>().Where(r => r.IsWaiting).ToList();

        if (requests.Count == 0)
        {
            return;
        }

        var reason = reply;

        if (reason is null)
        {
            using var prompt = new DriverRequestReasonForm(requests.Count == 1 ? desk.NameFor(requests[0]) : $"{requests.Count} drivers");

            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            reason = prompt.Reason;
        }

        var before = NewestUndo();
        var done = requests.Count(r => desk.Decline(r, reason));

        if (done < requests.Count)
        {
            AppDialog.Show(this, "That request has already been answered, or replaced by a newer one.", "Not declined",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        if (done > 0)
        {
            Toast(done == 1 ? $"Declined: {desk.NameFor(requests[0])}. Their phone has been told." : $"{Plural.Of(done, "request")} declined.", UndoForSteps(StepsSince(before)));
        }
    }

    /// <summary>Throws away a request from a badge nobody on this laptop has - nothing is sent to any phone.</summary>
    private void Discard()
    {
        if (_requests is not { } desk || Selected() is not { Source: InboxSource.Request, Payload: DriverRequest request }
            || desk.DriverOf(request) is not null)
        {
            return;
        }

        if (AppDialog.Show(this,
                "Nobody on this laptop has the badge this request came from - a driver deleted since, or a badge from before "
                + "the check-in key was changed. Throw the request away? Nothing is sent to anyone's phone.",
                "Discard request", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            desk.Discard(request);
        }

        RefreshAll(force: true);
    }
}
