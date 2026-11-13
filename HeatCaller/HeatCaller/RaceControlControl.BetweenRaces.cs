namespace HeatCaller;

/// <summary>What Call Up says - the button's press, and its right-click menu.</summary>
internal enum CallUpChoice
{
    /// <summary>The next race's call-up, with its marshals when Marshals to their posts is ticked.</summary>
    Next,

    /// <summary>The race after next: "Heat 4 drivers, you're two races away."</summary>
    TwoOut,

    /// <summary>The next race's marshals alone.</summary>
    MarshalsOnly,

    /// <summary>Who in the next race has not checked in.</summary>
    NotCheckedIn,
}

/// <summary>
/// THE GAP BETWEEN RACES, CALLED FROM RACE CONTROL (31 Oct 2026, asked for by
/// Asher - see GapRules for the whole of it).
///
/// The Announcer screen's director fills the gap with what it writes or
/// picks - Coming up next, the segments, a sponsor read, the hand-over. What
/// it never did was call the next race: "Heat 3, you're up next, head to the
/// drivers stand" was said only by somebody starting the Timetable countdown
/// by hand, while the phone in the driver's pocket had been flashing "You're
/// up next" for minutes. This page knows the program, the check-in desk, the
/// marshals and the run sheet, so it makes those calls itself, once a
/// second on the clock tick, by the same GapState the director asks - so the
/// two count toward one cap and keep one spacing.
///
/// Everything here is written by HeatCaller, not a model, and costs nothing.
/// It goes out through SpeakReminder with the captions on, which is the path
/// the Timetable's reminders take to the Big Screen and Listen Along.
///
/// And the flag row's three buttons after the flag - Skip, Hold Talk and Call
/// Up (F9, F10, F12) - which replaced Next Heat and Results there.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The night's gap, shared with the Announcer screen's director.</summary>
    private static GapState Gap => GapState.Shared;

    /// <summary>Hold Talk pressed - kept here as well as on the announcer strip, which lets go of a hold when commentary is off.</summary>
    private bool _talkHeld;

    /// <summary>The next race's facts, worked out at most every few seconds - the marshals are a roster to build.</summary>
    private (string? NextId, DateTime ReadUtc, GapFacts Facts)? _gapFacts;

    /// <summary>What the Broadcast window last said about the gap, so it is only redrawn on a change.</summary>
    private string? _gapSaid;

    /// <summary>Hold Talk is on, from the flag row, F10, the phone or the Broadcast window's Hold.</summary>
    internal bool TalkHeld => _talkHeld || _announcerStrip.Holding;

    /// <summary>The Between races settings, with tonight's race day's preset over them when it has one.</summary>
    private GapRules GapRulesNow() => GapRules.From(_settings, _events?.Current?.BetweenRacesPreset);

    /// <summary>
    /// Once a second, from the clock tick: opens and closes the gap with the
    /// race, tells it whether the floor is free, and says the next of Race
    /// Control's lines when one is due - a countdown mark first, then the
    /// call-up and the rest in GapState.CallOrder.
    /// </summary>
    private void BetweenRacesTick()
    {
        var now = DateTime.UtcNow;

        // The gap is after a race and before the next is called. Not in
        // practice, a warm-up (free laps are before a Stage, not a gap), or
        // while On Air is opening the next race.
        if (_race.State != RaceState.Finished || _race.IsPracticeMode || _race.IsWarmingUp || OnAirRunning)
        {
            Gap.Close();
            CloseAutoStart();
            ShowGapInBroadcast(string.Empty);
            return;
        }

        Gap.Open(_race.SessionGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), now);

        var rules = GapRulesNow();
        var facts = GapFactsNow(now);
        Gap.NextRace(facts.NextId);

        // The recap show counts as on air from the flag - between its parts
        // too, while the next part is written - so the quiet after it is
        // after the show, not after the finish call.
        var busy = _speaking || _announcer.IsTalking || ShowFloor.Busy
                   || Volatile.Read(ref _recapShow) is { Stop.IsCancellationRequested: false };

        if (busy)
        {
            Gap.FloorBusy();
        }
        else
        {
            Gap.FloorFree(now);
        }

        var held = TalkHeld;
        var onAirHasTheGap = OnAirFollowsTheRecap;

        // For the Announcer screen's director, judged by the same reading.
        Gap.Publish(rules, facts, held, onAirHasTheGap);

        ShowGapInBroadcast(Gap.DueNext(rules, now, facts, held, onAirHasTheGap));

        // Auto start (11 Nov 2026): counts the gap down and starts the next
        // race. Before the muted return: a muted announcer still runs races.
        AutoStartTick(now, facts, busy);

        // A welcome for drivers new to racing, once a night (setup plan D12).
        WelcomeNewDrivers(busy);

        if (_race.State != RaceState.Finished)
        {
            // It started the race.
            return;
        }

        if (_announcer.Muted || busy)
        {
            return;
        }

        if (Gap.MarkDue(rules, now, facts, held) is { } mark)
        {
            Gap.SaidMark(mark, now);
            SayGapLine(GapWords.Mark(mark, facts.Round), "Countdown");
            return;
        }

        var call = Gap.NextCall(rules, now, facts, held, onAirHasTheGap, out var heldBack);

        if (call is not { } kind)
        {
            TellWhyNoCallUp(heldBack, facts);
            return;
        }

        SayCall(kind, rules, facts, now);
    }

    /// <summary>Says one of Race Control's lines and books it with the gap.</summary>
    private void SayCall(GapLine kind, GapRules rules, GapFacts facts, DateTime now)
    {
        var text = GapPreview.TextFor(kind, rules, facts, now, Gap);

        if (kind == GapLine.TwoOut && facts.TwoOutId is { } twoOut)
        {
            Gap.SaidTwoOut(twoOut, now);
        }
        else
        {
            Gap.Said(kind, now);
        }

        if (text.Length > 0)
        {
            SayGapLine(text, GapPreview.Name(kind) switch
            {
                "call-up" => "Call-up",
                var other => char.ToUpperInvariant(other[0]) + other[1..],
            });
        }
    }

    /// <summary>A line of the gap, out through the announcer with the captions on - the Timetable reminders' path.</summary>
    private void SayGapLine(string text, string onAir)
    {
        // A call-up is booked as waiting until the announcer takes it, so
        // Call Up pressed meanwhile doesn't stack another (WhyNotCallUpAgain).
        Action? dequeued = null;

        if (onAir == "Call-up")
        {
            Gap.CallUpQueued(DateTime.UtcNow);
            dequeued = Gap.CallUpDequeued;
        }

        _announcer.SpeakReminder(text, showOnScreens: true, onAir: onAir, dequeued: dequeued);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] {onAir}: \"{text}\"");
    }

    /// <summary>
    /// The console hears why the call-up is not going, once a gap per
    /// reason (the plan's "Call-up held - inside the quiet before the
    /// call"). Not the waits that end by themselves, which change every
    /// second, and not "something is on air", which is the normal state of
    /// half of every gap.
    /// </summary>
    private void TellWhyNoCallUp(string? heldBack, GapFacts facts)
    {
        if (heldBack is null || !facts.HasNext
            || heldBack.StartsWith("only", StringComparison.Ordinal)
            || heldBack.StartsWith("next repeat", StringComparison.Ordinal)
            || heldBack.StartsWith("something is on air", StringComparison.Ordinal)
            || heldBack.StartsWith("inside the quiet after", StringComparison.Ordinal))
        {
            return;
        }

        if (Gap.FirstTimeTold(heldBack))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] Call-up for {facts.Round} held - {heldBack}.");
        }
    }

    /// <summary>
    /// The next race as the gap needs it: its name and class, its drivers,
    /// marshals and who has not checked in, the race after it, and when it
    /// is due by the run sheet (pushed back by the race director). The same
    /// next race Start Next would start (NextRound), so the announcer never
    /// calls one race while the button starts another.
    /// </summary>
    private GapFacts GapFactsNow(DateTime now)
    {
        var next = NextRound();

        if (next is null)
        {
            _gapFacts = null;
            return new GapFacts();
        }

        // The phone and the PA agree (the plan's section 7): the drivers'
        // phones flash "You're up next" for the first race still to run
        // that has cars on its grid (UpcomingRaces, PhoneFollow). When Start
        // Next would start a different one - a main not seeded yet, a round
        // with nobody in it - the announcer calls nothing, rather than send
        // drivers to the stand the phone says is someone else's turn.
        // The round just run is passed over, as NextRound passes it: its
        // result can take a moment to be linked to it after the flag.
        var justRun = _activeRound?.Invoke()?.Id;
        var upcoming = UpcomingRaces(4)
            .Where(r => !r.OnTrack && !string.Equals(r.RoundId, justRun, StringComparison.Ordinal))
            .ToList();
        var phoneNext = upcoming.Count > 0 ? upcoming[0].RoundId : null;

        if (!string.Equals(phoneNext, next.Id, StringComparison.Ordinal))
        {
            var why = GapWords.PhoneAndPaDisagree(next.RoundLabel, phoneNext is null ? null : upcoming[0].Title);

            if (Gap.FirstTimeTold(why))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] Call-up held - {why}.");
            }

            _gapFacts = null;
            return new GapFacts();
        }

        if (_gapFacts is { } kept && kept.NextId == next.Id && now - kept.ReadUtc < TimeSpan.FromSeconds(5))
        {
            return kept.Facts with { DueUtc = DueFor(next, now) };
        }

        var roster = next.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Not checked in: only when the desk is being used tonight - somebody
        // has been ticked in. A club with no desk would otherwise hear its
        // whole field named as missing.
        var notIn = _checkIns is { Count: > 0 } desk
            ? roster.Where(t => !desk.IsCheckedIn(t)).Select(SpokenNameOf).ToList()
            : new List<string>();

        // Two races out the way the phone counts it too: the race after
        // next that has drivers ("1 race before yours" on their phones).
        var twoOutId = upcoming.Count > 1 ? upcoming[1].RoundId : null;
        var twoOut = twoOutId is null
            ? null
            : OrderedProgram().FirstOrDefault(r => string.Equals(r.Id, twoOutId, StringComparison.Ordinal));

        var facts = new GapFacts
        {
            NextId = next.Id,
            Round = next.RoundLabel,
            ClassName = next.ClassName ?? string.Empty,
            Drivers = roster.Select(SpokenNameOf).ToList(),
            Marshals = MarshalsFor(next),
            NotCheckedIn = notIn,
            TwoOutRound = twoOut?.RoundLabel ?? string.Empty,
            TwoOutId = twoOut?.Id,
            Track = _events?.Current is { } day
                ? (day.TrackLayoutName.Trim().Length > 0 ? day.TrackLayoutName : day.VenueName).Trim()
                : string.Empty,
            DueUtc = DueFor(next, now),
        };

        _gapFacts = (next.Id, now, facts);
        return facts;
    }

    /// <summary>When the round is due by the run sheet and how late the day is running, plus any push-back - null when the sheet gives no time, or it passed more than a countdown mark's window ago (DueStill).</summary>
    private DateTime? DueFor(RoundDefinition round, DateTime now)
    {
        // Auto start's own start, when it is counting for this race: the
        // call-ups and countdown marks are then timed to the race actually
        // starting, run sheet or not (11 Nov 2026).
        if (AutoStartsAtUtc(round.Id, now) is { } autoStart)
        {
            return DueStill(autoStart, now);
        }

        if (_daySchedule is null)
        {
            return null;
        }

        var due = RaceEta.ScheduledUtc(round.Id, _daySchedule.All, _daySchedule.MinutesLate(), now);

        if (due is not { } at)
        {
            return null;
        }

        return DueStill(at.AddMinutes(Gap.PushedBackMinutes), now);
    }

    /// <summary>
    /// The due time while it is ahead, and for GapState.MarkWindow after it
    /// passes; null after that.
    /// </summary>
    /// <remarks>
    /// 2026-11-05, Bug hunt 11: this returned null the moment the due time
    /// came, so MarkDue never saw the "Time's up" mark (0) become due and the
    /// ticked box said nothing on the night, though Hear the gap played it.
    /// The time is kept through the mark's window. Every other reader (the
    /// quiet before the call, the long break, {minutes}) checks due > now
    /// itself, so a passed time changes nothing for them.
    /// </remarks>
    internal static DateTime? DueStill(DateTime due, DateTime now) =>
        now - due < GapState.MarkWindow ? due : null;

    /// <summary>A driver as the announcer says them - the announcer name where one is set.</summary>
    private string SpokenNameOf(string transponder)
    {
        var profile = _profiles.Get(transponder);
        var name = profile?.SpokenName;

        return string.IsNullOrWhiteSpace(name)
            ? (string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName!)
            : name;
    }

    /// <summary>
    /// Who marshals the round and on which post, worked out the way the
    /// Marshals dialog and the grid call work it out (MarshalAssigner), so
    /// all three name the same people. Empty when nobody is posted.
    /// </summary>
    private IReadOnlyList<(string Name, string Post)> MarshalsFor(RoundDefinition round) =>
        MarshalPostsFor(round).Select(p => (p.Name, p.Post)).ToList();

    /// <summary>
    /// <see cref="MarshalsFor"/> with each marshal's main transponder and the
    /// transponder they were put down on (12 Nov 2026, setup plan step 8): the
    /// main matches a marshal saying from their phone they're at their post;
    /// the transponder finds their results for the club's marshal rule.
    /// </summary>
    private IReadOnlyList<(string Main, string Name, string Post, string Transponder)> MarshalPostsFor(RoundDefinition round)
    {
        if (_rounds is null)
        {
            return Array.Empty<(string, string, string, string)>();
        }

        try
        {
            var classRounds = MarshalAssigner.ClassRoundsFor(round, _rounds, _events);
            var layout = MarshalAssigner.LayoutFor(classRounds, _trackLayouts);
            var assignment = MarshalAssigner.Assign(classRounds.ToList(), layout, _profiles)
                .FirstOrDefault(a => a.Round.Id == round.Id);

            if (assignment is null)
            {
                return Array.Empty<(string, string, string, string)>();
            }

            return assignment.Posts
                .Where(p => p.IsFilled)
                .Select(p => (MainOf(p.Transponder!), SpokenNameOf(p.Transponder!), p.Name ?? string.Empty, p.Transponder!))
                .ToList();
        }
        catch (Exception ex)
        {
            // A call-up without marshals beats no call-up.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] Couldn't work out the marshals for {round.RoundLabel}: {ex.Message}");
            return Array.Empty<(string, string, string, string)>();
        }
    }

    /// <summary>Tonight's next race, for Hear the gap on the Between races card - null with nothing next.</summary>
    internal GapFacts? TonightsGapFacts()
    {
        var facts = GapFactsNow(DateTime.UtcNow);
        return facts.HasNext ? facts : null;
    }

    // ==================== The flag row's buttons ====================

    /// <summary>
    /// Call Up, F12 and the phone: the next race's call-up now, in the card's
    /// wording, with the marshals straight after when they are ticked.
    /// Counted toward Call-ups per race and restarting the repeat, so the
    /// next automatic one is a full repeat away. Said whatever the caps and
    /// quiet zones - the race director asked for it - but not while muted,
    /// not over a race on track, and not as a second call-up while the last
    /// is still waiting or under GapState.CallUpAgainAfterSeconds old.
    /// </summary>
    internal string CallUpNow(CallUpChoice choice = CallUpChoice.Next)
    {
        if (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing && !_race.IsPracticeMode)
        {
            return Refuse("Call Up waits until the race on track is finished.");
        }

        if (_announcer.Muted)
        {
            return Refuse("The announcer is muted on the Mixer.");
        }

        var now = DateTime.UtcNow;
        var rules = GapRulesNow();
        var facts = GapFactsNow(now);
        Gap.NextRace(facts.NextId);

        switch (choice)
        {
            case CallUpChoice.TwoOut:
                if (facts.TwoOutId is not { } twoOut)
                {
                    return Refuse("Nothing is two races out in the program.");
                }

                Gap.SaidTwoOut(twoOut, now);
                SayGapLine(GapWords.TwoOut(facts.TwoOutRound), "Two races out");
                return $"Called {facts.TwoOutRound} - two races out.";

            case CallUpChoice.MarshalsOnly:
                if (!facts.HasNext || facts.Marshals.Count == 0)
                {
                    return Refuse("Nobody is posted to marshal the next race.");
                }

                Gap.Said(GapLine.Marshals, now);
                SayGapLine(GapWords.Marshals(facts, rules, now), "Marshals");
                return $"Called the marshals for {facts.Round}.";

            case CallUpChoice.NotCheckedIn:
                if (!facts.HasNext || facts.NotCheckedIn.Count == 0)
                {
                    return Refuse("Everyone in the next race is checked in.");
                }

                Gap.Said(GapLine.NotCheckedIn, now);
                SayGapLine(GapWords.NotCheckedIn(facts), "Not checked in");
                return $"Called who is not checked in for {facts.Round}.";
        }

        if (!facts.HasNext)
        {
            return Refuse("The program has nothing next to call up.");
        }

        // The same press again, not a second call - see WhyNotCallUpAgain.
        if (Gap.WhyNotCallUpAgain(facts.Round, now) is { } again)
        {
            return Refuse(again);
        }

        // Said in the console, so a call-up somebody pressed can be told
        // from the automatic one (both are "[gap] Call-up:" lines).
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] Call Up pressed{(_runningRemoteCommand ? " on the phone" : string.Empty)} - calling up {facts.Round}.");

        var repeat = Gap.CallUps > 0;
        Gap.Said(GapLine.CallUp, now);
        SayGapLine(GapWords.CallUp(facts, rules, repeat, now), "Call-up");

        if (rules.Marshals && facts.Marshals.Count > 0)
        {
            Gap.Said(GapLine.Marshals, now);
            SayGapLine(GapWords.Marshals(facts, rules, now), "Marshals");
        }

        return $"Called up {facts.Round}.";

        string Refuse(string why)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] Call Up: {why}");
            _remoteRefusal ??= _runningRemoteCommand ? why : null;
            return why;
        }
    }

    /// <summary>Call Up's right-click: Push the next race back a minute - its run-sheet time, the countdown and the next call-up with it.</summary>
    private void PushNextRaceBack()
    {
        var now = DateTime.UtcNow;
        var facts = GapFactsNow(now);
        Gap.NextRace(facts.NextId);
        Gap.PushBack(now);
        _gapFacts = null;

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] {(facts.HasNext ? facts.Round : "The next race")} pushed back a minute "
                              + $"({Plural.Of(Gap.PushedBackMinutes, "minute")} in all) - the call-ups and countdown move with it.");
    }

    /// <summary>
    /// Hold Talk, F10 and the phone: stops everything between races except
    /// the race itself - the director's filler, sponsor reads, the hand-over,
    /// call-ups and the automatic countdown - and lets it go again. The same
    /// hold as the Broadcast window's Hold when commentary is on, so either
    /// shows on both; with commentary off it is this page's alone.
    /// </summary>
    internal bool ToggleTalkHold()
    {
        var hold = !TalkHeld;
        _talkHeld = hold;

        if (!hold)
        {
            Gap.Resumed(DateTime.UtcNow);
        }

        // The strip keeps a hold only while commentary is on - it lets one go
        // when it hides - so it is only told then.
        if (Commentary?.Invoke() is not null && _settings.CommentaryEnabled)
        {
            _announcerStrip.SetHolding(hold);
        }
        else if (hold)
        {
            EndRecapShow("the talk was held from Race Control");
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] " + (hold
            ? "Between-race talk held - press Resume Talk or F10."
            : "Between-race talk resumed."));

        ShowTalkHold();
        UpdateOnAirBar();
        _nextUpDueUtc = DateTime.MinValue;
        return hold;
    }

    /// <summary>
    /// Skip, F9 and the phone, pressed: the line on air now stops and the
    /// show moves on to its next part - a recap goes on to the honours, the
    /// gap to its next line. Only between races: during a race the line on
    /// air can be a flag call, which nothing here may cut.
    /// </summary>
    internal string SkipPart()
    {
        if (_race.State is not (RaceState.Finished or RaceState.Idle) || _race.IsPracticeMode)
        {
            return "Skip works between races - during one, the line on air can be a flag call.";
        }

        var what = _announcer.OnAirNow;

        if (StopTheReplay() | AnnouncementService.StopCurrentPlayback())
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [show] Skipped {(what is { Length: > 0 } ? what : "the line on air")} - on to the next part.");
            return $"Skipped {(what is { Length: > 0 } ? what : "the line on air")}.";
        }

        return "Nothing is on air to skip.";
    }

    /// <summary>Skip held for a second: the whole recap show ends, as the Broadcast window's Skip ends it.</summary>
    private void SkipWholeShow() => SkipWhatIsOnAir();

    /// <summary>The flag row's Hold Talk as the hold stands - amber "Resume Talk" while held.</summary>
    private void ShowTalkHold()
    {
        var held = TalkHeld;
        var caption = held ? ResumeTalkCaption : HoldTalkCaption;

        if (!string.Equals(_btnBarHoldTalk.Text, caption, StringComparison.Ordinal))
        {
            _btnBarHoldTalk.Text = caption;
        }

        if (_btnBarHoldTalk.Tag is not ValueTuple<bool, Color> styled || styled.Item1 != held || styled.Item2 != _btnBarHoldTalk.BackColor)
        {
            AppTheme.StyleButton(_btnBarHoldTalk, held ? AppTheme.ButtonKind.Warning : AppTheme.ButtonKind.Auto);
            _btnBarHoldTalk.Tag = (held, _btnBarHoldTalk.BackColor);
        }
    }

    private const string HoldTalkCaption = "Hold Talk";
    private const string ResumeTalkCaption = "Resume Talk";

    /// <summary>Skip greyed out when nothing is on air, Call Up when the program has nothing next - on the clock tick.</summary>
    private void UpdateGapButtons()
    {
        var onAir = _speaking || _replayCut is not null || Volatile.Read(ref _recapShow) is { Stop.IsCancellationRequested: false };

        if (_btnBarSkip.Enabled != onAir)
        {
            _btnBarSkip.Enabled = onAir;
        }

        var next = _nextWaiting;

        if (_btnBarCallUp.Enabled != next)
        {
            _btnBarCallUp.Enabled = next;
        }

        ShowTalkHold();
    }

    /// <summary>The Broadcast window's Between races row, when it has changed.</summary>
    private void ShowGapInBroadcast(string said)
    {
        if (string.Equals(_gapSaid, said, StringComparison.Ordinal))
        {
            return;
        }

        _gapSaid = said;
        _broadcastWindow?.ShowGap(said);
    }

    /// <summary>The Broadcast window's skip for the next call-up: counted as said, and the next is a full repeat away.</summary>
    private void SkipNextCallUp()
    {
        var now = DateTime.UtcNow;
        var facts = GapFactsNow(now);

        if (!facts.HasNext)
        {
            return;
        }

        Gap.NextRace(facts.NextId);
        Gap.SkipCallUp(now);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [gap] The next call-up for {facts.Round} was skipped from the Broadcast window.");
    }

    /// <summary>The right-click menu on Call Up.</summary>
    private ContextMenuStrip BuildCallUpMenu()
    {
        var menu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };
        var pushBack = new ToolStripMenuItem("Push the next race back 1 minute", null, (_, _) => PushNextRaceBack());
        var twoOut = new ToolStripMenuItem("Call up two races out", null, (_, _) => CallUpNow(CallUpChoice.TwoOut));
        var marshals = new ToolStripMenuItem("Marshals only", null, (_, _) => CallUpNow(CallUpChoice.MarshalsOnly));
        var notIn = new ToolStripMenuItem("Not checked in", null, (_, _) => CallUpNow(CallUpChoice.NotCheckedIn));

        menu.Items.AddRange(new ToolStripItem[] { pushBack, twoOut, marshals, notIn });

        // What each can do, read as the menu opens rather than kept up to
        // date every second for a menu almost never opened.
        menu.Opening += (_, _) =>
        {
            var facts = GapFactsNow(DateTime.UtcNow);
            pushBack.Enabled = facts.HasNext;
            twoOut.Enabled = facts.TwoOutId is not null;
            marshals.Enabled = facts.HasNext && facts.Marshals.Count > 0;
            notIn.Visible = facts.HasNext && facts.NotCheckedIn.Count > 0;
        };

        AppTheme.StyleContextMenu(menu);
        return menu;
    }

    /// <summary>
    /// Skip, Hold Talk and Call Up on the flag row after the flag. Skip is a
    /// click and a hold: a click (a hold let go early) stops the line on air,
    /// and held for the second the red flag takes, it ends the whole recap
    /// show - so the bigger of the two is never done by a stray tap.
    /// </summary>
    private void BuildTalkButtons()
    {
        var skip = new HoldToAct(_btnBarSkip, "end the recap show", SkipWholeShow);

        skip.Changed += h => _holdStrip.Follow(h);
        skip.Changed += _ => MoveDeskIfOwed();
        skip.CalledOff += h =>
        {
            _holdStrip.Follow(h);

            // Let go over the button: the click. Dragged off it, nothing -
            // as for any button.
            if (_btnBarSkip.ClientRectangle.Contains(_btnBarSkip.PointToClient(Cursor.Position)))
            {
                SkipPart();
            }
        };

        _btnBarSkip.Paint += (_, e) => skip.PaintOver(e.Graphics, _btnBarSkip.ClientRectangle, _btnBarSkip.ForeColor, null);
        _btnBarSkip.AccessibleDescription = "Click to skip the line on air; press and hold for about a second to end the recap show.";
        _holds.Add(skip);

        _btnBarHoldTalk.Click += (_, _) => ToggleTalkHold();

        _btnBarCallUp.Click += (_, _) => CallUpNow();
        _btnBarCallUp.ContextMenuStrip = BuildCallUpMenu();
    }
}
