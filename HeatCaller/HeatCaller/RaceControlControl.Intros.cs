namespace HeatCaller;

/// <summary>
/// The broadcast's driver introductions at staging (18 Oct 2026): AI
/// commentary that rolls through the grid, pole first - each driver's
/// number, home club and a line of form - and, before a main, opens with
/// what qualifying decided.
///
/// The FACTS are worked out here, on the UI thread, exactly (DriverIntros,
/// QualifyingCalculator); the WORDS are the model's
/// (CommentaryService.BuildDriverIntroductionsAsync). Switched on, and held
/// for the mains, on the Commentary page. Spoken after the round intro and
/// before the Team Principal, and the automatic start waits for it the same
/// way it waits for the round intro - see SpeakRoundIntro.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>How long the model gets to write the introductions. Longer than the round intro's: it is a longer piece.</summary>
    private static readonly TimeSpan IntroductionsModelBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long the automatic start allows each introduction to be read before giving up on the audio and counting down anyway.</summary>
    private static readonly TimeSpan PerIntroduction = TimeSpan.FromSeconds(5);

    /// <summary>How long it allows the qualifying recap to be read.</summary>
    private static readonly TimeSpan ForTheRecap = TimeSpan.FromSeconds(12);

    /// <summary>The most the automatic start will wait at staging for the introductions, however big the grid.</summary>
    private static readonly TimeSpan MostForIntroductions = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Settles once this race's driver introductions have been queued or
    /// given up on - the Team Principal waits for it, so the room hears the
    /// field before the team.
    /// </summary>
    private Task _introductionsQueued = Task.CompletedTask;

    /// <summary>
    /// The round a demo was started for, from Race Day's "run as a demo"
    /// (18 Oct 2026) - set by MainForm, cleared once that demo's recap has
    /// been planned, or when a race that is not a demo is staged.
    ///
    /// WHY IT EXISTS. A demo of a round that has already been run - or any
    /// demo, without "Demo races fill the program" - leaves the Round box on
    /// freeform, so it is never filed against a heat that really ran. That
    /// is right for the results, and it also left the SHOW with no round:
    /// no Team Principal before or after, no Technical Lead after a main, no
    /// grid for the introductions. A demo is how a club hears the show
    /// before a real night, so the show reads the round from here. Nothing
    /// that files, scores or advances the program ever does.
    /// </summary>
    internal RoundDefinition? DemoRound { get; set; }

    /// <summary>
    /// The show before the start for a DEMO (18 Oct 2026), which stages the
    /// session itself and so never came through the Stage button: the round
    /// intro (unless On Air has just said it), the Team Principal and the
    /// driver introductions - the same three, in the same order, as
    /// RaceControlControl.Layout's Stage. Returns how long the demo's
    /// countdown should be prepared to wait for them, or null for nothing.
    /// </summary>
    /// <param name="demoCars">The demo's cars - the field, when the demo is not tied to a round.</param>
    internal TimeSpan? ShowAtDemoStaging(IReadOnlyList<string> demoCars)
    {
        if (_race.State != RaceState.Staging || _race.IsPracticeMode)
        {
            return null;
        }

        // STAGING FIRES NO BROADCAST SEGMENT (18 Oct 2026), a demo's no more
        // than the Stage button's: the race intro, the throw to pit lane and
        // the driver introductions are On Air's, said before the demo starts.
        // On Air's word that it said them is used up so none is left over.
        _ = demoCars;
        TakeOpeningSaidByOnAir();
        TakePrincipalSaidByOnAir();
        TakeIntroductionsSaidByOnAir();

        return null;
    }

    /// <summary>
    /// The round the race is run AS: the Round box's, or - for a demo with
    /// the Round box on freeform - the round the demo was started for, so a
    /// demo of a round already run is that race again: its format, its
    /// grid, its show. Never for filing - a demo is filed against no round
    /// (SessionRecord.RoundId is null for one) - and DemoRound is only set
    /// from a demo's start until its recap, or a real race staging.
    /// </summary>
    private RoundDefinition? ShowRound() => _activeRound?.Invoke() ?? DemoRound;

    /// <summary>
    /// The round a finished demo was run for, for its recap - only when some
    /// of that round's drivers actually raced in it, so a stale round from
    /// an earlier demo is never put on a different field.
    /// </summary>
    private RoundDefinition? DemoRoundFor(SessionRecord record)
    {
        if (DemoRound is not { } round || round.AssignedTransponders is not { Count: > 0 } roster)
        {
            return null;
        }

        var raced = new HashSet<string>(record.Results.Select(r => r.Transponder), StringComparer.OrdinalIgnoreCase);

        return roster.Any(raced.Contains) ? round : null;
    }

    /// <summary>
    /// Asks the AI commentary for the driver introductions of the race just
    /// staged, and speaks them once written - if the race is still staging,
    /// and still the race they were written for. Returns how long the
    /// automatic start should be prepared to wait for them, or null when
    /// none were asked for.
    /// </summary>
    /// <param name="boardBeforeStaging">
    /// Who was on the board before Stage cleared it - the only field a
    /// freeform race (no round selected) has to introduce. Read by the
    /// caller before staging, because staging empties the board.
    /// </param>
    /// <param name="stagedGeneration">The session they are for - _race.SessionGeneration straight after Stage.</param>
    private TimeSpan? SpeakIntroductions(IReadOnlyList<string> boardBeforeStaging, int stagedGeneration)
    {
        _introductionsQueued = Task.CompletedTask;

        // Already said as part of On Air's opening, rendered with it - see
        // IntroductionsForOpeningAsync. Taken first, so it is used up
        // whatever happens below.
        if (TakeIntroductionsSaidByOnAir())
        {
            return null;
        }

        // Demo races are introduced too, as the round intro and the Team
        // Principal are: a demo is how a club hears the show before a
        // real night. Practice has no field to introduce.
        if (_race.IsPracticeMode || _race.State != RaceState.Staging)
        {
            return null;
        }

        var commentary = _commentary;

        if (commentary is not { IsAvailable: true } || !_settings.CommentaryDriverIntros)
        {
            return null;
        }

        var round = ShowRound();

        if (ReadIntroductionFacts(round, boardBeforeStaging, quiet: false) is not { } facts)
        {
            return null;
        }

        var (grid, qualifying, raceTitle) = facts;

        // WRITTEN AHEAD? When the last result was filed, if they are still
        // true - then the start waits for no model, only the round intro.
        var held = TakeIntroductionsAhead(round, grid);

        // The sponsor, rationed and reserved now; counted only once the
        // introductions are queued to the announcer.
        var sponsor = held is not null ? held.Sponsor : _announcer.TakeSponsor(SponsoredMoment.DriverIntros);
        // After the round intro AND the Team Principal (18 Oct 2026): the
        // race is set up, the team is heard, and then the field.
        var queuedBefore = Task.WhenAll(_introQueued, _principalQueued);

        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _introductionsQueued = settled.Task;

        // Held from now, so the staging chain waits for the introductions -
        // and released however this ends, and in any case after the budget
        // and the round intro's own, or when a countdown starts.
        var writing = new SettlesWhenDisposed(
            _announcer.BeginLineBeingWritten(IntroductionsModelBudget + RoundIntroModelBudget + PrincipalModelBudget + TimeSpan.FromSeconds(40)),
            settled);

        StatusMessage?.Invoke(held is not null
            ? $"{AppTime.LogStampNow()}  [commentary] Driver introductions were written ahead, so they wait for no model."
            : $"{AppTime.LogStampNow()}  [commentary] Asking the model to introduce the {Plural.Of(grid.Count, "driver")}"
              + (qualifying is not null ? ", with the qualifying recap" : string.Empty) + "...");

        _ = Task.Run(async () =>
        {
            string? line;

            try
            {
                using var budget = new CancellationTokenSource(IntroductionsModelBudget);

                line = held?.Text ?? await commentary.BuildDriverIntroductionsAsync(
                    raceTitle, grid, qualifying, sponsor?.Name, budget.Token);

                if (string.IsNullOrWhiteSpace(line))
                {
                    StatusMessage?.Invoke(budget.IsCancellationRequested
                        ? $"{AppTime.LogStampNow()}  [commentary] Driver introductions skipped - the model took longer than {IntroductionsModelBudget.TotalSeconds:0}s, so the start went on without them."
                        : $"{AppTime.LogStampNow()}  [commentary] Driver introductions skipped - no line came back.");
                    writing.Dispose();
                    return;
                }

                // After the round intro, so the room hears the race set up
                // first and then the field.
                await Task.WhenAny(queuedBefore, Task.Delay(RoundIntroModelBudget + PrincipalModelBudget + TimeSpan.FromSeconds(40)));
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Driver introductions skipped - {ex.Message}");
                writing.Dispose();
                return;
            }

            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    writing.Dispose();
                    return;
                }

                // On the UI thread, where the race's state belongs. Released
                // AFTER SpeakReminder has queued the line, so the audio never
                // looks finished in between.
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsDisposed || Disposing)
                        {
                            return;
                        }

                        if (_race.State != RaceState.Staging || _race.SessionGeneration != stagedGeneration)
                        {
                            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Driver introductions dropped - the race had moved on from staging before they were written.");
                            return;
                        }

                        // The Big Screen's cards, armed as the line is
                        // handed over - see IntroOnAir.
                        ArmIntroCards(grid, line);

                        _announcer.SpeakReminder(line!);

                        // WHY (20 Oct 2026): held introductions were written
                        // ahead with remember: false, so they are recorded as
                        // the last line (and their wording as used) only now
                        // that they are said.
                        if (held is not null)
                        {
                            _commentary?.IntroSpoken(line);
                        }

                        // Counted only when the room actually hears it.
                        if (!_announcer.Muted)
                        {
                            sponsor?.Count();
                        }
                    }
                    finally
                    {
                        writing.Dispose();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The window handle went away between the check and the
                // post - the page is closing.
                writing.Dispose();
            }
        });

        return IntroWait(grid.Count, qualifying is not null);
    }

    /// <summary>
    /// Arms the Big Screen's driver cards for the introductions about to be
    /// handed to the announcer (1 Nov 2026, asked for by Asher) - each
    /// driver's name and photo, up as the voice reaches them (IntroOnAir).
    /// Switched off on the AI Commentary tab (AppSettings.BigScreenIntroCards);
    /// a driver whose profile keeps their photo off the Big Screen gets their
    /// name with their initials. Never the reason the introductions go unsaid.
    /// </summary>
    private void ArmIntroCards(IReadOnlyList<IntroDriver> grid, string? words)
    {
        try
        {
            if (!_settings.BigScreenIntroCards || grid is not { Count: > 0 })
            {
                IntroOnAir.Shared.Clear();
                return;
            }

            IntroOnAir.Shared.Arm(grid
                .Select(d => IntroCardsFromProfiles.CardFor(d, _profiles.Get(d.Transponder)))
                .ToList(), words);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No Big Screen cards for the driver introductions: {ex.Message}");
        }
    }

    /// <summary>What the introductions are written from: the grid, what qualifying decided, and the race's name.</summary>
    private sealed record IntroductionFacts(IReadOnlyList<IntroDriver> Grid, string? Qualifying, string RaceTitle);

    /// <summary>
    /// Everything the driver introductions for <paramref name="round"/> are
    /// written from, read on the UI thread - shared by Stage, On Air's
    /// opening and the write-ahead, so all three introduce the same field
    /// the same way. Null when none are due: switched off, lap times only,
    /// mains only and this is not one, or no field to introduce - said in
    /// the console unless <paramref name="quiet"/>.
    /// </summary>
    /// <param name="round">The round the race is run as, or null for a freeform race.</param>
    /// <param name="board">The field of a freeform race - who is on the board.</param>
    /// <param name="quiet">True for the write-ahead, which nobody is waiting to hear about.</param>
    private IntroductionFacts? ReadIntroductionFacts(RoundDefinition? round, IReadOnlyList<string>? board, bool quiet)
    {
        if (_commentary is not { IsAvailable: true } || !_settings.CommentaryDriverIntros)
        {
            return null;
        }

        // Said, because "why didn't I hear the introductions?" should be
        // answered by the console rather than by guesswork.
        void Skipped(string why)
        {
            if (!quiet)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No driver introductions - {why}.");
            }
        }

        if (HeatCaller.LapTimesOnly.IsOn(_settings))
        {
            Skipped("the sound is set to lap times only");
            return null;
        }

        try
        {
            var isMain = round is { Kind: RoundKind.Main };

            if (_settings.CommentaryDriverIntrosMainsOnly && !isMain)
            {
                Skipped(round is null
                    ? "they are set for mains only, and this race is not tied to a main on the program"
                    : $"they are set for mains only, and {round.RoundLabel} is not a main");
                return null;
            }

            var roundRoster = round?.AssignedTransponders?
                .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            // A round's roster is its grid, in order (GridForStagger reads
            // the same list). Without a round there is no grid, so the
            // drivers are introduced without a position rather than one
            // being made up.
            var hasGrid = roundRoster.Count > 0;
            // Without a round, only drivers with a profile: a demo's
            // made-up stand-in cars have nothing to introduce.
            var field = hasGrid
                ? roundRoster
                : (board ?? Array.Empty<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t) && _profiles.Get(t) is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            // One car is not a field to introduce.
            if (field.Count < 2)
            {
                Skipped(field.Count == 0 ? "nobody is on the grid to introduce" : "one car is not a field to introduce");
                return null;
            }

            // Read once: every driver's form comes from the same history.
            var history = _sessionHistory.All;
            var now = DateTime.UtcNow;

            var grid = field.Select((transponder, i) =>
            {
                var profile = _profiles.Get(transponder);

                // Never the transponder: it would be read out as a number.
                // A car with no profile takes the name the timing last
                // recorded for it (19 Oct 2026), as the qualifying recap
                // already did - the room heard "Terry Byte third" in the
                // qualifying line and then "the third spot belongs to an
                // unnamed driver".
                var screen = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName
                    : DriverIntros.NameFromHistory(history, transponder) ?? RelayDocuments.UnnamedDriver;
                var spoken = !string.IsNullOrWhiteSpace(profile?.AnnouncerName) ? profile!.AnnouncerName : screen;

                return new IntroDriver(
                    transponder,
                    hasGrid ? i + 1 : 0,
                    spoken,
                    screen,
                    profile?.RaceNumber ?? string.Empty,
                    profile?.HomeTrack ?? string.Empty,
                    DriverIntros.Stat(history, transponder, now));
            }).ToList();

            var qualifying = isMain && round is not null && _settings.CommentaryQualifyingRecap ? QualifyingRecapFor(round, field) : null;
            var raceTitle = round?.RoundLabel is { Length: > 0 } label ? label : "the next race";

            return new IntroductionFacts(grid, qualifying, raceTitle);
        }
        catch (Exception ex)
        {
            // Never the reason a race does not start.
            if (!quiet)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Driver introductions skipped: {ex.Message}");
            }

            return null;
        }
    }

    // ==================== Written ahead, and said in the opening (18 Oct 2026) ====================

    /// <summary>The driver introductions written ahead for one round, and the sponsor reserved for them.</summary>
    private sealed record IntroductionsAhead(
        string RoundId,
        string Grid,
        int AsOf,
        string Text,
        (string Name, Action Count)? Sponsor);

    /// <summary>Guards the held introductions and their version: written from a pool thread, taken on the UI thread.</summary>
    private readonly object _introductionsAheadLock = new();

    /// <summary>The introductions written ahead for the next round, or null.</summary>
    private IntroductionsAhead? _introductionsAhead;

    /// <summary>Moved on by every result filed or corrected, so introductions written before it are never used after.</summary>
    private int _introductionsAheadVersion;

    /// <summary>
    /// Set when On Air's opening carried the introductions, rendered with it
    /// - so the Stage it hands over to does not say them again. Cleared
    /// wherever On Air's own "opening said" is.
    /// </summary>
    private bool _introductionsSaidByOnAir;

    /// <summary>Takes On Air's word that the introductions have been said - see <see cref="_introductionsSaidByOnAir"/>.</summary>
    private bool TakeIntroductionsSaidByOnAir()
    {
        var said = _introductionsSaidByOnAir;
        _introductionsSaidByOnAir = false;
        return said;
    }

    /// <summary>
    /// Writes the NEXT round's driver introductions now, so they wait for no
    /// model - the round intro's write-ahead, for the introductions. Called
    /// from WriteTheNextIntroAhead, so when a result is filed: the form lines
    /// and the qualifying recap are true then, and a result filed or
    /// corrected since throws them away, as does a change of grid.
    ///
    /// Best-effort: never awaited, holding no start up, and a failure costs
    /// nothing - Stage, or On Air, writes its own as before.
    /// </summary>
    /// <param name="gate">Waited for before the model is asked - see _flagLinesAsked.</param>
    private void WriteTheIntroductionsAhead(SessionRecord? filed, bool corrected, Task gate)
    {
        int asOf;

        lock (_introductionsAheadLock)
        {
            asOf = ++_introductionsAheadVersion;
            _introductionsAhead = null;
        }

        if (corrected && _race.State is (RaceState.Staging or RaceState.Countdown or RaceState.Racing))
        {
            return;
        }

        if (_commentary is not { IsAvailable: true } commentary
            || NextRound(filed?.RoundId) is not { } next
            || string.IsNullOrWhiteSpace(next.Id))
        {
            return;
        }

        if (ReadIntroductionFacts(next, null, quiet: true) is not { } facts)
        {
            return;
        }

        var grid = WrittenAhead.Fingerprint(facts.Grid.Select(d => d.Transponder));
        var label = next.RoundLabel;

        // Reserved now, counted when spoken. Once a race, so reserving it
        // early never holds a mention back from anything else.
        var sponsor = _announcer.TakeSponsor(SponsoredMoment.DriverIntros);

        _ = Task.Run(async () =>
        {
            // Behind the flag lines, before the budget (20 Oct 2026) - see _flagLinesAsked.
            await WaitForFlagLinesAsync(gate);

            try
            {
                using var budget = new CancellationTokenSource(WriteAheadModelBudget);

                // WHY (20 Oct 2026): remember: false - written ahead, not yet
                // heard, so not the Announcer page's last line until it is
                // said (IntroSpoken, in SpeakIntroductions and On Air).
                var text = await commentary.BuildDriverIntroductionsAsync(
                    facts.RaceTitle, facts.Grid, facts.Qualifying, sponsor?.Name, budget.Token, remember: false);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                lock (_introductionsAheadLock)
                {
                    if (_introductionsAheadVersion != asOf)
                    {
                        return;
                    }

                    _introductionsAhead = new IntroductionsAhead(next.Id, grid, asOf, text, sponsor);
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Wrote the driver introductions for {label} ahead of time - "
                    + "they will wait for no model.");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Could not write the driver introductions for {label} ahead: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// The introductions written ahead for <paramref name="round"/> with
    /// this exact <paramref name="grid"/>, taken (so used once), or null when
    /// none are held, they are for another round or grid, or a result has
    /// been filed or corrected since.
    /// </summary>
    private IntroductionsAhead? TakeIntroductionsAhead(RoundDefinition? round, IReadOnlyList<IntroDriver> grid)
    {
        if (round is null)
        {
            return null;
        }

        lock (_introductionsAheadLock)
        {
            var held = _introductionsAhead;

            if (held is null || held.AsOf != _introductionsAheadVersion)
            {
                _introductionsAhead = null;
                return null;
            }

            // Another round's - a demo of a round already run, say - is left
            // for the round it was written for.
            if (!string.Equals(held.RoundId, round.Id, StringComparison.Ordinal))
            {
                return null;
            }

            _introductionsAhead = null;

            return string.Equals(held.Grid, WrittenAhead.Fingerprint(grid.Select(d => d.Transponder)), StringComparison.Ordinal)
                ? held
                : null;
        }
    }

    /// <summary>
    /// The driver introductions for On Air's opening (18 Oct 2026): written
    /// ahead when they are held and still true, otherwise written now -
    /// alongside the round intro, not after it - so they are RENDERED WITH
    /// THE OPENING and play straight on from it, with no wait at staging.
    /// The Stage On Air hands over to then leaves them out. Null when none
    /// are due or none came back in time; the Stage then writes its own.
    ///
    /// Called on the UI thread: the facts are read there, the model is not.
    /// </summary>
    private Task<(string Text, Action? Count)?> IntroductionsForOpeningAsync(RoundDefinition round, CancellationToken ct)
    {
        if (_commentary is not { IsAvailable: true } commentary
            || ReadIntroductionFacts(round, null, quiet: false) is not { } facts)
        {
            return Task.FromResult<(string Text, Action? Count)?>(null);
        }

        if (TakeIntroductionsAhead(round, facts.Grid) is { } held)
        {
            Action? count = held.Sponsor is { } heldSponsor ? heldSponsor.Count : null;
            return Task.FromResult<(string Text, Action? Count)?>((held.Text, count));
        }

        var sponsor = _announcer.TakeSponsor(SponsoredMoment.DriverIntros);

        return Task.Run<(string Text, Action? Count)?>(async () =>
        {
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(IntroductionsModelBudget);

                var text = await commentary.BuildDriverIntroductionsAsync(
                    facts.RaceTitle, facts.Grid, facts.Qualifying, sponsor?.Name, budget.Token);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                Action? count = sponsor is { } taken ? taken.Count : null;
                return (text, count);
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    /// <summary>How long staging may wait for <paramref name="introductions"/> introductions and, when there is one, the recap: the model's budget, then the reading.</summary>
    internal static TimeSpan IntroWait(int introductions, bool recap)
    {
        var wait = IntroductionsModelBudget
            + PerIntroduction * Math.Max(0, introductions)
            + (recap ? ForTheRecap : TimeSpan.Zero);

        return wait > MostForIntroductions ? MostForIntroductions : wait;
    }

    /// <summary>
    /// What tonight's qualifying decided for the cars in <paramref name="main"/>,
    /// in qualifying order, worded for the voice - the calculator's own
    /// result, with the class's own method and tonight's rounds only, exactly
    /// as the grid sheet ranks it. Null when there is nothing to say.
    /// </summary>
    private string? QualifyingRecapFor(RoundDefinition main, IReadOnlyList<string> field)
    {
        if (_rounds is null || string.IsNullOrWhiteSpace(main.ClassName))
        {
            return null;
        }

        var raceClass = _raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, main.ClassName, StringComparison.OrdinalIgnoreCase));

        // Tonight's rounds and no others. Without a race day there is no
        // "tonight", and a null filter would mean every heat the class has
        // ever run - the August-pole mistake MainForm.Commentary describes -
        // so an empty one is passed instead, and there is no recap.
        IReadOnlyCollection<string> tonight = _events?.QualifyingScope() ?? (IReadOnlyCollection<string>)Array.Empty<string>();

        var entries = QualifyingCalculator.Compute(
            main.ClassName, _rounds, _sessionHistory, _profiles, raceClass, tonight);

        var inThisMain = new HashSet<string>(field, StringComparer.OrdinalIgnoreCase);

        var order = entries
            .Where(e => inThisMain.Contains(e.Transponder))
            .ToList();

        if (order.Count == 0)
        {
            return null;
        }

        // The calculator's BestConsecutiveMs is a TOTAL over every heat that
        // counted (best two of three, say), so the recap is handed one run's
        // worth - the average lap times the run's length - or "averaging" a
        // lap would come out two or three times too slow.
        var lapCount = Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3);

        var cars = order.Select(e =>
        {
            var profile = _profiles.Get(e.Transponder);
            var screen = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName
                : !string.IsNullOrWhiteSpace(e.DriverName) ? e.DriverName
                : RelayDocuments.UnnamedDriver;
            var spoken = !string.IsNullOrWhiteSpace(profile?.AnnouncerName) ? profile!.AnnouncerName : screen;

            return new RecapCar(spoken, screen, e.BestLaps, e.BestElapsedMs, e.ConsecutiveAverageMs(lapCount) * lapCount, e.BestFastLapMs);
        }).ToList();

        var recap = DriverIntros.Recap(cars, raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps, lapCount).Spoken;

        return string.IsNullOrWhiteSpace(recap) ? null : recap;
    }
}
