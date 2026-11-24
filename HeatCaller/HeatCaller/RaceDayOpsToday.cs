using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The race-day manager's "Today" ops: what the day looks like, and setting
/// it up - which race day, which classes, how many qualifying rounds, how
/// big the heats and the mains are. See raceday-protocol.md.
///
/// The Event tab (RaceDayEventControl) does these as plain store calls with
/// no dialogs, and so does this - but with undo captured, the way the
/// Calendar tab's event editing captures it, because a change made from a
/// phone is one the person at the desk did not see being made.
/// </summary>
public sealed partial class RaceDayService
{
    private void RegisterTodayOps()
    {
        Register("today.summary", OpKind.Read, TodaySummary);
        Register("day.list", OpKind.Read, DayList);
        Register("day.open", OpKind.Change, DayOpen);
        Register("day.create", OpKind.Change, DayCreate);
        Register("day.update", OpKind.Change, DayUpdate);
        Register("classes.list", OpKind.Read, ClassesList);
        Register("class.setFormat", OpKind.Change, ClassSetFormat);
    }

    /// <summary>The first letter lowered - how enum names go out ("RunNext" -> "runNext").</summary>
    private static string Camel(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private object? DayView(RaceEvent? day)
    {
        if (day is null || day.IsLegacy)
        {
            return null;
        }

        return new
        {
            id = day.Id,
            name = day.DisplayName,
            startedUtc = day.StartedUtc,
            open = day.IsOpen,
            current = string.Equals(day.Id, _host.Events.CurrentId, StringComparison.Ordinal),
            venue = day.VenueName,
            track = day.TrackLayoutName,
            qualifyingRounds = day.QualifyingRounds,

            // 0 means "whatever was used last" - the number the programme
            // will really build with is sent alongside it.
            driversPerHeat = day.DriversPerHeat,
            driversPerHeatUsed = _host.Events.HeatSizeFor(day),
            checkedInOnly = day.CheckedInDriversOnly,
            classNames = day.ClassNames.ToList(),
            rounds = day.RoundIds.Count,
            recorded = day.RoundIds.Count(id => _host.Rounds.Get(id)?.IsRecorded == true),
            checkedIn = _host.CheckIns.CheckedInAt(day.Id).Count,
        };
    }

    private OpResult TodaySummary(OpContext context)
    {
        var day = _host.Events.Active;
        var rounds = _host.Program.TodaysRounds();
        // WHY (23 Nov 2026, bug hunt 24): the step's words went out as the
        // desk wrote them, which for a car nobody has named is its whole
        // transponder - see NextStepFor.
        var step = NextStepFor(_host.Program.NextStep(), context.Channel);
        var active = _host.ActiveRound();
        var race = _host.Race;
        var next = NextRoundToStart();
        var runSheetNext = _host.Schedule.NextUp;

        return OpResult.Read(new
        {
            raceDay = DayView(day),
            classes = _host.Program.TodaysClassNames()
                .Select(name => new
                {
                    name,
                    drivers = _host.Program.ClassRoster(name).Count,
                    checkedIn = _host.Profiles.All.Count(p =>
                        p.RunsClass(name)
                        && _host.CheckIns.IsCheckedIn(p.Transponder)),
                })
                .ToList(),
            counts = new
            {
                drivers = _host.Profiles.All.Count,
                checkedIn = _host.CheckIns.Count,
                rounds = rounds.Count,
                recorded = rounds.Count(r => r.IsRecorded),
            },
            race = new
            {
                state = Camel(race.State.ToString()),
                flag = Camel(race.Flag.ToString()),

                // Which race this is. Sent back with a stage or countdown that
                // would abandon it, so the yes is tied to the race the phone
                // was shown - see RaceDayOpsRace.
                raceGeneration = race.SessionGeneration,
                practice = race.IsPracticeMode,
                demo = race.IsDemoMode,
                roundId = race.State == RaceState.Idle ? null : active?.Id,
                roundLabel = race.State == RaceState.Idle ? null : active?.RoundLabel,
                className = race.State == RaceState.Idle ? null : active?.ClassName,
            },
            nextStep = new
            {
                stage = Camel(step.Stage.ToString()),
                headline = step.Headline,
                detail = step.Detail,
                action = step.ActionCaption,
                roundId = step.Round?.Id,
                className = step.ClassName,
            },
            nextRound = next is null
                ? null
                : new
                {
                    id = next.Id,
                    label = next.RoundLabel,
                    className = next.ClassName,
                    drivers = next.AssignedTransponders.Count,
                },
            runSheetNext = runSheetNext is null
                ? null
                : new
                {
                    time = runSheetNext.ScheduledTime,
                    className = runSheetNext.ClassName,
                    description = runSheetNext.Description,
                },

            // The entries themselves only on the track's Wi-Fi; through the
            // relay, one line with the count - see UndoListFor.
            undo = UndoListFor(_host.Undo.PendingDescriptions, context.Channel),
            undoCount = _host.Undo.PendingDescriptions.Count,
            allowResultEditing = _host.Settings.AllowResultEditing,
            busy = LaptopBusy(),
        });
    }

    /// <summary>
    /// The Next Steps card's step as a phone on this channel may see it. See
    /// <see cref="StepForPhone"/>.
    /// </summary>
    private ProgramStep NextStepFor(ProgramStep step, RaceDayChannel channel)
    {
        if (channel == RaceDayChannel.WiFi || step.Stage is not (ProgramStage.LateArrivals or ProgramStage.PostHeats))
        {
            return step;
        }

        IReadOnlyList<LateArrivals.Arrival> late = step.Stage == ProgramStage.LateArrivals
            ? _host.Program.LateArrivalsTonight()
            : Array.Empty<LateArrivals.Arrival>();

        IReadOnlyList<string> notHeard = step.Stage == ProgramStage.PostHeats && _host.Program.HeardTonight is { } heard
            ? HeatPosting.NotHeard(_host.Program.TodaysRounds(), heard)
            : Array.Empty<string>();

        return StepForPhone(step, channel, _host.Profiles, late, notHeard);
    }

    /// <summary>
    /// The Next Steps card's step with every driver it names written as
    /// ShownNameOf writes them for <paramref name="channel"/>: as the desk
    /// wrote it on the track's Wi-Fi, and through the relay an unnamed car
    /// as the last three characters of its transponder.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): today.summary sent the step's headline
    /// and detail as the desk wrote them, and the desk names a car nobody
    /// has named by its whole transponder - "7654321 checked in after the
    /// heats were posted." and "7654321 (Stock). Each goes into...", and the
    /// "hasn't crossed the loop tonight" list on the post-the-heats step -
    /// so a full transponder number went out through the relay, which every
    /// other name in the same answer keeps to its last three characters.
    ///
    /// Only the names are swapped; the rest of the wording stays the desk's.
    /// <paramref name="lateArrivals"/> is RaceProgramControl.LateArrivalsTonight
    /// and <paramref name="notHeard"/> the transponders HeatPosting.NotHeard
    /// gives - what the desk built the text from. The desk's names are found
    /// the way the desk writes them (RaceProgramControl.NameOf and
    /// NotHeardTonight).
    /// </summary>
    internal static ProgramStep StepForPhone(
        ProgramStep step,
        RaceDayChannel channel,
        ProfileStore profiles,
        IReadOnlyList<LateArrivals.Arrival> lateArrivals,
        IReadOnlyList<string> notHeard)
    {
        if (channel == RaceDayChannel.WiFi)
        {
            return step;
        }

        string Shown(string transponder) =>
            profiles.Get(transponder) is { } profile ? ShownNameOf(profile, channel) : ShowTransponder(transponder, channel);

        if (step.Stage == ProgramStage.LateArrivals && lateArrivals.Count > 0)
        {
            string DeskName(string transponder) =>
                profiles.Get(transponder) is { DriverName.Length: > 0 } profile ? profile.DriverName.Trim() : transponder;

            var headline = step.Headline;
            var detail = step.Detail;

            // One name in the headline only when one driver is late; with
            // more it says how many, and names nobody.
            var deskNames = lateArrivals.Select(a => DeskName(a.Transponder)).Distinct().ToList();

            if (deskNames.Count == 1)
            {
                headline = headline.StartsWith(deskNames[0] + " checked in", StringComparison.Ordinal)
                    ? Shown(lateArrivals[0].Transponder) + headline[deskNames[0].Length..]
                    : "1 driver checked in late.";
            }

            var deskList = string.Join(", ", lateArrivals.Select(a => $"{DeskName(a.Transponder)} ({a.ClassName})"));
            var shownList = string.Join(", ", lateArrivals.Select(a => $"{Shown(a.Transponder)} ({a.ClassName})"));

            detail = detail.StartsWith(deskList + ".", StringComparison.Ordinal)
                ? shownList + detail[deskList.Length..]
                : $"{shownList}. Add them on the laptop's Program tab, or say not tonight.";

            return step with { Headline = headline, Detail = detail };
        }

        if (step.Stage == ProgramStage.PostHeats && notHeard.Count > 0)
        {
            var deskWords = HeatPosting.NotHeardWords(notHeard
                .Select(t => profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t)
                .ToList());

            if (deskWords.Length > 0 && step.Detail.Contains(deskWords, StringComparison.Ordinal))
            {
                var shownWords = HeatPosting.NotHeardWords(notHeard.Select(Shown).ToList());

                return step with { Detail = step.Detail.Replace(deskWords, shownWords, StringComparison.Ordinal) };
            }
        }

        return step;
    }

    private OpResult DayList(OpContext context) =>
        OpResult.Read(new
        {
            currentId = _host.Events.Active is { IsLegacy: false } active ? active.Id : null,
            days = _host.Events.Real.Select(DayView).ToList(),
        });

    /// <summary>
    /// Makes a race day the current one. Everything scoped to the day -
    /// check-in, tech, the run sheet, the programme - switches with it, so
    /// it is refused while anything is on track (the Event tab does not
    /// guard this; a phone, which cannot see the track, must).
    /// </summary>
    private OpResult DayOpen(OpContext context)
    {
        var id = context.RequireText("dayId");

        if (_host.Events.Get(id) is not { IsLegacy: false } day)
        {
            throw new OpRefused("That race day isn't on the laptop any more - reload the list.");
        }

        // WHY (20 Oct 2026): this used to answer "nothing changed" for the
        // current day even when it was closed - and a closed day can be
        // current, picked on the desk to be looked at. The phone has no other
        // way to reopen a day, so it was stuck. Now only a current day that
        // is also open is "nothing changed"; a closed one goes on to the
        // question and the reopen below like any other closed day.
        if (string.Equals(_host.Events.Active?.Id, day.Id, StringComparison.Ordinal) && day.IsOpen)
        {
            return OpResult.Read(new { dayId = day.Id, changed = false });
        }

        RefuseWhileLive("switch race days");

        if (!day.IsOpen && !context.Confirmed)
        {
            return OpResult.Ask($"\"{day.DisplayName}\" has been closed. Reopen it and make it the current race day?");
        }

        // What this changes - which race day is current, and whether this one
        // is closed - for the undo, which puts back only that, the way the
        // Calendar tab's undos do. It used to put back a snapshot of every
        // race day, which undid whatever had happened to any of them since
        // as well: a race day started since was deleted, and names, classes
        // and settings changed since went back to what they had been.
        var dayId = day.Id;
        var previousId = _host.Events.Active?.Id;
        var wasCurrent = string.Equals(previousId, dayId, StringComparison.Ordinal);

        // Whether that day was still open then. A closed day can be current
        // (being looked at), and going back to it is putting it back as it
        // was, not reopening it - so only a day closed SINCE is refused.
        var previousWasOpen = _host.Events.Active?.IsOpen ?? false;
        var reopening = !day.IsOpen;
        var closedAtUtc = day.FinishedUtc;

        _host.Undo.Capture($"Phone: Open race day '{day.DisplayName}'", () =>
        {
            // Made current since: another day, or none (this one closed or
            // deleted). That later choice isn't this undo's to take back.
            if (!string.Equals(_host.Events.Active?.Id, dayId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "it isn't the current race day any more - another has been made current since, or it has been closed or deleted. Nothing was changed.");
            }

            if (previousId is not null
                && !(_host.Events.Get(previousId) is { } dayBefore && (dayBefore.IsOpen || !previousWasOpen)))
            {
                throw new InvalidOperationException(
                    "the race day that was current before has been closed or deleted since, so it can't be made current again. Nothing was changed.");
            }

            // SetActive rather than a restore, so check-in, tech and the run
            // sheet follow; then closed again, with the time it really was.
            _host.Events.SetActive(previousId);

            if (reopening)
            {
                _host.Events.Close(dayId, closedAtUtc);

                // WHY (20 Oct 2026): closing the current day clears "current",
                // so undoing the reopen of a day that was already current (and
                // closed) left no day current at all. Put it back as current,
                // the way it was, as the Calendar tab's close/reopen undo does.
                if (wasCurrent && _host.Events.Active is null && _host.Events.Get(dayId) is not null)
                {
                    _host.Events.SetActive(dayId);
                }
            }

            _host.RefreshDesk();
        });

        if (!day.IsOpen)
        {
            _host.Events.Reopen(day.Id);
        }

        _host.Events.SetActive(day.Id);

        return OpResult.Done($"Opened race day \"{day.DisplayName}\"", $"\"{day.DisplayName}\" is now the current race day.",
            new { dayId = day.Id, changed = true });
    }

    /// <summary>
    /// How long after a race day is started a request to start one with the
    /// same name is taken to be that same request arriving again, rather
    /// than a new race day being wanted. See DayCreate.
    ///
    /// Fifteen minutes. The relay forgets a request nobody has answered ten
    /// minutes after it was queued (MANAGE_QUEUE_MAX_AGE_MS in the relay's
    /// server.ts), so a request it hands over again after this laptop has
    /// crashed or been restarted is never older than that; the other five
    /// are for the laptop's clock being put right as it starts up again. A
    /// phone asking again after it gave up waiting for the answer, or a
    /// double tap on the Wi-Fi page, comes well inside it. Short enough
    /// that a race day started earlier in the day - a morning practice,
    /// say - is never mistaken for the one being asked for now.
    /// </summary>
    private static readonly TimeSpan RepeatStartWindow = TimeSpan.FromMinutes(15);

    /// <summary>The most recently started real race day, or null on a club's first - what a new one copies its settings from, as on the Event tab (RaceDayEventControl.LastRealDay).</summary>
    private static RaceEvent? LastRealDay(RaceEventStore events) =>
        events.Real.OrderByDescending(e => e.StartedUtc).FirstOrDefault();

    /// <summary>
    /// Starts a race day called <paramref name="name"/> with everything else
    /// carried forward from <paramref name="previous"/>, as the Event tab's
    /// New Race Day does (RaceDayEventControl.StartDayFrom) - the kind of
    /// night and the classes as well, which the phone doesn't ask about.
    /// Only classes still on file; when that leaves none, every class, as
    /// New Race Day ticks them all on a club's first day.
    ///
    /// It used to start a bare race day: no classes, venue, track or
    /// championship, three qualifying rounds and check-in only, none of
    /// which the phone can change - and the next New Race Day on the laptop
    /// copied its blanks from it.
    /// </summary>
    internal static RaceEvent StartDayLike(RaceEventStore events, RaceClassStore classes, string name, RaceEvent? previous)
    {
        var onFile = classes.All.Select(c => c.Name).ToList();
        var racing = onFile.Where(c => previous?.ClassNames?.Contains(c, StringComparer.OrdinalIgnoreCase) == true).ToList();

        return EventSpine.StartEvent(
            events,
            name,
            previous?.VenueName ?? string.Empty,
            previous?.TrackLayoutName ?? string.Empty,
            previous?.ChampionshipId ?? string.Empty,
            previous?.Shape ?? RaceDayShape.RaceDay,
            racing.Count > 0 ? racing : onFile,
            previous?.QualifyingRounds ?? 0,
            previous?.DriversPerHeat ?? 0);
    }

    /// <summary>
    /// Whether a request to start a race day called <paramref name="requested"/>
    /// ("" for no name) names this one. Compared the way day.update compares
    /// names. With no name sent, the default is the one the day was given on
    /// the date IT was started - so a request made just before midnight and
    /// handed over again just after still matches - for either kind of
    /// night, as the default follows the kind (EventSpine.DefaultDayName)
    /// and the desk may have switched it since.
    /// </summary>
    internal static bool NamedAsAsked(RaceEvent day, string requested)
    {
        if (requested.Length > 0)
        {
            return string.Equals(day.Name, requested, StringComparison.Ordinal);
        }

        var started = day.StartedUtc.ToLocalTime();

        return string.Equals(day.Name, EventSpine.DefaultDayName(started, RaceDayShape.RaceDay), StringComparison.Ordinal)
               || string.Equals(day.Name, EventSpine.DefaultDayName(started, RaceDayShape.ClubNight), StringComparison.Ordinal);
    }

    /// <summary>
    /// A new race day, made current - the Event tab's New Race Day.
    ///
    /// Safe to ask twice, like every other change a phone makes. It used to
    /// start a new race day every time it was asked, and a request CAN
    /// arrive twice: a double tap on the Wi-Fi page, a phone asking again
    /// after it gave up waiting for an answer the laptop was still getting
    /// to, or the relay handing over again a request this laptop had
    /// already carried out when it crashed or was restarted before the
    /// answer went back - RelayPublisher only remembers what it has run in
    /// memory. Each made a second race day with the same name and made it
    /// current, leaving the first one's check-ins behind on a day nobody
    /// was looking at any more.
    ///
    /// So a request that turns out to be for the race day already current
    /// - see AlreadyStarted - is answered with that day, changed:false: no
    /// second day, and no second undo entry.
    /// </summary>
    private OpResult DayCreate(OpContext context)
    {
        var requested = context.Text("name")?.Trim() ?? string.Empty;

        if (requested.Length > 80)
        {
            throw new OpRefused("That name is too long - 80 characters at most.");
        }

        // Ahead of the live check: a repeat changes nothing, so there is
        // nothing on track to protect - and "start a new race day when it
        // has finished" would send the phone back to try again later, when
        // the day might no longer look new and a second one really would be
        // made.
        if (AlreadyStarted(requested) is { } already)
        {
            return OpResult.Done(
                $"Race day \"{already.DisplayName}\" was already started - asked again, nothing changed",
                $"\"{already.DisplayName}\" was already started and is the current race day. Nothing was changed.",
                new { dayId = already.Id, changed = false });
        }

        RefuseWhileLive("start a new race day");

        // What the new day copies from - see StartDayLike. Looked up before
        // it is started, as the new day is the most recent once it is; its
        // default name follows the kind of night, as the Event tab's does.
        var previous = LastRealDay(_host.Events);
        var name = requested.Length > 0 ? requested : EventSpine.DefaultDayName(DateTime.Now, previous?.Shape ?? RaceDayShape.RaceDay);

        // Which race day was current, and the new one as it started (filled
        // in below), for the undo: it takes back the new day, while nothing
        // has been done with it, and makes the old one current again, the
        // way the Calendar tab's undo of New Event does. It used to put back
        // a snapshot of every race day, which also undid anything changed on
        // any of them since - and a day used since was dropped with its
        // check-ins, programme and run sheet left behind under a day nobody
        // could see any more.
        var previousId = _host.Events.Active?.Id;

        // See DayOpen: a day that was already closed when it was current can
        // be current again; one closed since cannot.
        var previousWasOpen = _host.Events.Active?.IsOpen ?? false;
        RaceEvent? started = null;
        var asStarted = string.Empty;

        _host.Undo.Capture($"Phone: New race day '{name}'", () =>
        {
            if (started is null || _host.Events.Get(started.Id) is not { } live)
            {
                throw new InvalidOperationException("that race day isn't on the laptop any more. Nothing was changed.");
            }

            if (DescribeUseOf(live) is { } used)
            {
                throw new InvalidOperationException(
                    $"\"{live.DisplayName}\" has been used since it was started ({used}), and taking it away now would lose that. "
                    + "Close it with Close Day on Race Day > Setup if it isn't wanted. Nothing was changed.");
            }

            if (JsonSerializer.Serialize(live) != asStarted)
            {
                throw new InvalidOperationException(
                    $"\"{live.DisplayName}\" has been changed since it was started, and taking it away now would throw that away. Nothing was changed.");
            }

            var wasCurrent = string.Equals(_host.Events.Active?.Id, live.Id, StringComparison.Ordinal);

            _host.Events.Delete(live.Id);

            // Back to the day that was current before - if this one still was,
            // and that one can still take rounds.
            if (wasCurrent && (previousId is null
                || _host.Events.Get(previousId) is { } dayBefore && (dayBefore.IsOpen || !previousWasOpen)))
            {
                _host.Events.SetActive(previousId);
            }

            _host.RefreshDesk();
        });

        var day = StartDayLike(_host.Events, _host.Classes, name, previous);
        started = day;
        asStarted = JsonSerializer.Serialize(day);

        return OpResult.Done($"Started race day \"{day.DisplayName}\"", $"\"{day.DisplayName}\" is started and is now the current race day.",
            new { dayId = day.Id, changed = true });
    }

    /// <summary>
    /// The current race day, when a request to start one called
    /// <paramref name="requested"/> ("" for no name) is that same race day
    /// being asked for again. Null when a new race day is what is wanted.
    ///
    /// Only a day still being set up: open, started within
    /// RepeatStartWindow, and with nothing on its programme yet. Answering
    /// with such a day loses nothing, whichever the request really was -
    /// nothing is made, and nothing is left behind on another day.
    /// </summary>
    private RaceEvent? AlreadyStarted(string requested)
    {
        if (_host.Events.Active is not { IsLegacy: false, IsOpen: true } day)
        {
            return null;
        }

        // Named the way DayCreate names it - see NamedAsAsked.
        if (!NamedAsAsked(day, requested))
        {
            return null;
        }

        // Either side of now: a clock put back since the day was started
        // leaves it looking a few minutes in the future.
        if ((DateTime.UtcNow - day.StartedUtc).Duration() > RepeatStartWindow)
        {
            return null;
        }

        // Nothing on its programme: no heats built, so none run either - a
        // day's results are its rounds' results, the way DayView counts
        // them. A day with a programme is one somebody has started using,
        // and asking for a race day of the same name after that is asking
        // for a fresh one.
        //
        // Check-ins are left out on purpose. Drivers check THEMSELVES in,
        // from their phones and the Wi-Fi page, whenever they arrive - and
        // after a restart the relay's check-in taps are collected just
        // before its manager requests (RelayPublisher.Run). On a race
        // morning the very request this is here to catch would find a
        // driver already checked in on the day, and start the second day
        // anyway.
        return day.RoundIds.Count == 0 ? day : null;
    }

    /// <summary>
    /// What has been filed under a race day - rounds on its programme,
    /// drivers checked in, tech inspections, run sheet lines - in words, or
    /// null when there is none of it. All of those are filed under the day's
    /// id, so deleting the day leaves them behind where nothing shows them.
    /// </summary>
    private string? DescribeUseOf(RaceEvent day)
    {
        var used = new List<string>();

        if (day.RoundIds.Count > 0)
        {
            used.Add($"{Plural.Of(day.RoundIds.Count, "round")} on its program");
        }

        var checkedIn = _host.CheckIns.CheckedInAt(day.Id).Count;

        if (checkedIn > 0)
        {
            used.Add($"{Plural.Of(checkedIn, "driver")} checked in");
        }

        var inspections = _host.Tech.ForEvent(day.Id).Count;

        if (inspections > 0)
        {
            used.Add(Plural.Of(inspections, "tech inspection"));
        }

        var lines = _host.Schedule.Everything.Count(e => string.Equals(e.EventId, day.Id, StringComparison.Ordinal));

        if (lines > 0)
        {
            used.Add(Plural.Of(lines, "run sheet line"));
        }

        return used.Count == 0 ? null : string.Join(", ", used);
    }

    /// <summary>
    /// Changes the current race day's shape: its name, which classes race,
    /// how many qualifying rounds, how many drivers per heat, and whether
    /// only checked-in drivers are put in heats. Only what is sent changes.
    /// The same ranges as the Event tab.
    /// </summary>
    private OpResult DayUpdate(OpContext context)
    {
        if (_host.Events.Active is not { IsLegacy: false } day)
        {
            throw new OpRefused("No race day is open. Open or start one first.");
        }

        var name = context.Text("name")?.Trim();
        var qualifyingRounds = context.Int("qualifyingRounds", 1, 10);
        var driversPerHeat = context.Int("driversPerHeat", 2, 20);
        var checkedInOnly = context.Bool("checkedInOnly");
        var classNames = context.TextList("classNames", 100);

        if (name is { Length: > 80 })
        {
            throw new OpRefused("That name is too long - 80 characters at most.");
        }

        // Class names are matched to the real classes, and stored as the
        // class spells itself - every store matches class names ignoring
        // case, but the day's list is shown to people.
        List<string>? classes = null;

        if (classNames is not null)
        {
            classes = new List<string>();

            foreach (var wanted in classNames.Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                var match = _host.Classes.All.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
                            ?? throw new OpRefused($"There is no class called \"{wanted}\".");

                if (!classes.Contains(match.Name, StringComparer.OrdinalIgnoreCase))
                {
                    classes.Add(match.Name);
                }
            }
        }

        var changes = new List<string>();

        if (name is { Length: > 0 } && name != day.Name)
        {
            changes.Add($"name \"{name}\"");
        }

        if (qualifyingRounds is { } rounds && rounds != day.QualifyingRounds)
        {
            changes.Add(Plural.Of(rounds, "qualifying round"));
        }

        if (driversPerHeat is { } perHeat && perHeat != day.DriversPerHeat)
        {
            changes.Add($"{perHeat} per heat");
        }

        if (checkedInOnly is { } onlyIn && onlyIn != day.CheckedInDriversOnly)
        {
            changes.Add(onlyIn ? "checked-in drivers only" : "all entered drivers");
        }

        if (classes is not null && !classes.SequenceEqual(day.ClassNames, StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(classes.Count == 0 ? "no classes" : $"classes {string.Join(", ", classes)}");
        }

        if (changes.Count == 0)
        {
            return OpResult.Read(new { changed = false });
        }

        // The day's settings as they are and as this leaves them, for the
        // undo: it puts back only what this changes, on the day as it is by
        // then, and only while each of those is still as this left it. It
        // used to put back a snapshot of every race day - a race day started
        // since was deleted by it, and anything changed since on any day
        // (this one's venue, another one's classes, which day is current)
        // went back too.
        var dayId = day.Id;
        var before = (
            Name: day.Name,
            Qualifying: day.QualifyingRounds,
            PerHeat: day.DriversPerHeat,
            CheckedInOnly: day.CheckedInDriversOnly,
            Classes: day.ClassNames?.ToList() ?? new List<string>());
        var after = (
            Name: name is { Length: > 0 } ? name : before.Name,
            Qualifying: qualifyingRounds ?? before.Qualifying,
            PerHeat: driversPerHeat ?? before.PerHeat,
            CheckedInOnly: checkedInOnly ?? before.CheckedInOnly,
            Classes: classes?.ToList() ?? before.Classes);

        _host.Undo.Capture($"Phone: Change race day '{day.DisplayName}'", () =>
        {
            if (_host.Events.Get(dayId) is not { } target)
            {
                throw new InvalidOperationException("that race day has been deleted since. Nothing was changed.");
            }

            var classesChanged = !after.Classes.SequenceEqual(before.Classes, StringComparer.OrdinalIgnoreCase);

            if ((after.Name != before.Name && target.Name != after.Name)
                || (after.Qualifying != before.Qualifying && target.QualifyingRounds != after.Qualifying)
                || (after.PerHeat != before.PerHeat && target.DriversPerHeat != after.PerHeat)
                || (after.CheckedInOnly != before.CheckedInOnly && target.CheckedInDriversOnly != after.CheckedInOnly)
                || (classesChanged && !(target.ClassNames ?? new List<string>()).SequenceEqual(after.Classes, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"\"{target.DisplayName}\" has been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            if (after.Name != before.Name)
            {
                target.Name = before.Name;
            }

            if (after.Qualifying != before.Qualifying)
            {
                target.QualifyingRounds = before.Qualifying;
            }

            if (after.PerHeat != before.PerHeat)
            {
                target.DriversPerHeat = before.PerHeat;
            }

            if (after.CheckedInOnly != before.CheckedInOnly)
            {
                target.CheckedInDriversOnly = before.CheckedInOnly;
            }

            if (classesChanged)
            {
                target.ClassNames = before.Classes.ToList();
            }

            _host.Events.Upsert(target);
            _host.RefreshDesk();
        });

        if (name is { Length: > 0 })
        {
            day.Name = name;
        }

        if (qualifyingRounds is { } q)
        {
            day.QualifyingRounds = q;
        }

        if (driversPerHeat is { } h)
        {
            day.DriversPerHeat = h;
        }

        if (checkedInOnly is { } c)
        {
            day.CheckedInDriversOnly = c;
        }

        if (classes is not null)
        {
            day.ClassNames = classes;
        }

        _host.Events.Upsert(day);

        return OpResult.Done($"Changed \"{day.DisplayName}\": {string.Join(", ", changes)}", "Saved.", new { changed = true });
    }

    private object ClassView(RaceClass raceClass)
    {
        var racing = _host.Program.TodaysClassNames().Contains(raceClass.Name, StringComparer.OrdinalIgnoreCase);

        return new
        {
            name = raceClass.Name,
            racingToday = racing,
            drivers = _host.Profiles.All.Count(p => p.RunsClass(raceClass.Name)),
            rosterToday = _host.Program.ClassRoster(raceClass.Name).Count,
            qualifyingMethod = Camel(raceClass.QualifyingMethod.ToString()),
            consecutiveLaps = raceClass.ConsecutiveLapCount,
            countBestRounds = raceClass.CountBestRounds,
            mainSize = raceClass.MainSize,
            bumpUpCount = raceClass.BumpUpCount,
            aMainLegs = raceClass.AMainLegs,
            aMainLegsCounted = AMainLegs.LegsCounting(raceClass.AMainLegs, raceClass.AMainLegsCounted),
            raceFormat = raceClass.Format?.Describe(),
            qualifierFormat = raceClass.QualifierFormat?.Describe(),
        };
    }

    private OpResult ClassesList(OpContext context) =>
        OpResult.Read(new { classes = _host.Classes.All.Select(ClassView).ToList() });

    /// <summary>
    /// How a class qualifies and how its mains are built - the numbers on the
    /// Classes page's class editor, within the same ranges. The race length
    /// and start type (RaceFormat) stay on the laptop: their editor is a
    /// whole form of interlocking settings, not something to thumb in.
    /// </summary>
    private OpResult ClassSetFormat(OpContext context)
    {
        var wanted = context.RequireText("className");

        var raceClass = _host.Classes.All.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
                        ?? throw new OpRefused($"There is no class called \"{wanted}\".");

        QualifyingMethod? method = context.Text("qualifyingMethod")?.Trim() switch
        {
            null => null,
            var text when text.Equals("totalLaps", StringComparison.OrdinalIgnoreCase) => QualifyingMethod.TotalLaps,
            var text when text.Equals("bestConsecutiveLaps", StringComparison.OrdinalIgnoreCase) => QualifyingMethod.BestConsecutiveLaps,
            var text when text.Equals("roundPoints", StringComparison.OrdinalIgnoreCase) => QualifyingMethod.RoundPoints,
            _ => throw new OpRefused("\"qualifyingMethod\" has to be totalLaps, bestConsecutiveLaps or roundPoints."),
        };

        var consecutive = context.Int("consecutiveLaps", 2, 20);
        var countBest = context.Int("countBestRounds", 1, 20);
        var mainSize = context.Int("mainSize", 2, 40);
        var bumpUp = context.Int("bumpUpCount", 0, 10);

        var before = (raceClass.QualifyingMethod, raceClass.ConsecutiveLapCount, raceClass.CountBestRounds, raceClass.MainSize, raceClass.BumpUpCount);

        var after = (
            method ?? before.QualifyingMethod,
            consecutive ?? before.ConsecutiveLapCount,
            countBest ?? before.CountBestRounds,
            mainSize ?? before.MainSize,
            bumpUp ?? before.BumpUpCount);

        if (after == before)
        {
            return OpResult.Read(new { changed = false, @class = ClassView(raceClass) });
        }

        _host.Undo.Capture($"Phone: Change format for {raceClass.Name}", () =>
        {
            // Only what this changed goes back, onto the class as it is now,
            // and only while each of those is still as this left it. It used
            // to put all five numbers back as they were before it - so a
            // bump-up count or main size changed on the Classes page since
            // (which edits the class in place, with no undo of its own) went
            // back too - and it saved the class even when it had been deleted
            // since, bringing it back. Found by id, like the Classes page's
            // own undo: a cancelled edit there can swap in a copy of the class.
            if (_host.Classes.Get(raceClass.Id) is not { } live)
            {
                throw new InvalidOperationException($"the class {raceClass.Name} has been deleted since. Nothing was changed.");
            }

            if ((before.QualifyingMethod != after.Item1 && live.QualifyingMethod != after.Item1)
                || (before.ConsecutiveLapCount != after.Item2 && live.ConsecutiveLapCount != after.Item2)
                || (before.CountBestRounds != after.Item3 && live.CountBestRounds != after.Item3)
                || (before.MainSize != after.Item4 && live.MainSize != after.Item4)
                || (before.BumpUpCount != after.Item5 && live.BumpUpCount != after.Item5))
            {
                throw new InvalidOperationException(
                    $"{live.Name}'s format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
            }

            if (before.QualifyingMethod != after.Item1)
            {
                live.QualifyingMethod = before.QualifyingMethod;
            }

            if (before.ConsecutiveLapCount != after.Item2)
            {
                live.ConsecutiveLapCount = before.ConsecutiveLapCount;
            }

            if (before.CountBestRounds != after.Item3)
            {
                live.CountBestRounds = before.CountBestRounds;
            }

            if (before.MainSize != after.Item4)
            {
                live.MainSize = before.MainSize;
            }

            if (before.BumpUpCount != after.Item5)
            {
                live.BumpUpCount = before.BumpUpCount;
            }

            _host.Classes.Upsert(live);
            _host.RefreshDesk();
        });

        (raceClass.QualifyingMethod, raceClass.ConsecutiveLapCount, raceClass.CountBestRounds, raceClass.MainSize, raceClass.BumpUpCount) = after;
        _host.Classes.Upsert(raceClass);

        return OpResult.Done(
            $"Changed {raceClass.Name}: qualifying {Camel(after.Item1.ToString())}, mains of {after.Item4}, {after.Item5} bump up",
            "Saved.",
            new { changed = true, @class = ClassView(raceClass) });
    }

    /// <summary>
    /// What Start Next would start: the first round on today's programme
    /// that has not been run and is not the one already picked. The same
    /// answer as the Next Up bar's (RaceControlControl.NextRound) - the
    /// programme's day and Race Control's day are the same set of rounds;
    /// see RaceProgramControl.DayRounds and RaceControlControl.OrderedProgram.
    /// </summary>
    private RoundDefinition? NextRoundToStart()
    {
        var active = _host.ActiveRound();

        return _host.Program.TodaysRounds()
            .FirstOrDefault(r => !r.IsRecorded && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal));
    }
}
