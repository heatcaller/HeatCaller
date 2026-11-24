namespace HeatCaller;

/// <summary>
/// The winner named on the line (10 Nov 2026, voice speed plan).
/// </summary>
/// <remarks>
/// WHY. The finish call names nobody - "That's the finish - what a night!" -
/// and the unofficial result is read once the cheering has died down, which
/// in the 10 Nov races was fourteen seconds after the flag. The moment the
/// room most wants a name, it heard none. Asked for by Asher.
///
/// RENDERED BEFORE THE FLAG. When the leader starts the last lap (a lap
/// race) or the clock runs out (a timed race), the winner's line is made for
/// each of the top two - whoever crosses first is almost always one of them.
/// At the flag, the line for whoever won is put at the very FRONT of the
/// queue, so it is the first thing said once the finish call has been - with
/// its audio already made, it follows the call without a pause. A winner
/// who was not in the top two then is rendered on the spot, as any line.
///
/// QUEUED AS THE FINISH, SO THE FINISH'S CLEAR-OUT SPARES IT. The finish
/// call clears the decks when it speaks; a line queued as RaceCompleted
/// survives that (SurvivesAFlag), as the finish call's own words must. Its
/// early render is kept out of the render-ahead list for the same reason,
/// and handed over only as it is said.
///
/// Its own row on the Voice page, "Winner named" (WinnerCalled): the club's
/// lines, ticks, speed. Said only when the finish call is on as well: a club
/// that switched the finish call off for the fanfare alone has asked for no
/// words at the flag. The unofficial result is still read after the cheering,
/// with the places and the fastest lap.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>The Winner named row, when it is on along with the finish call - else null.</summary>
    private AnnouncementEventConfig? WinnerCallConfig()
    {
        var events = _voiceStore.Current.Events;

        return events.GetValueOrDefault(nameof(AnnouncementEventType.RaceCompleted)) is { } finish && CallOn(finish)
            && events.GetValueOrDefault(nameof(AnnouncementEventType.WinnerCalled)) is { } winner && CallOn(winner)
            && winner.EffectiveTemplates.Count > 0
                ? winner
                : null;
    }

    /// <summary>The finish call's lines that name the winner - "{winner}" in them (11 Nov 2026).</summary>
    internal static IReadOnlyList<string> FinishLinesNamingTheWinner(IReadOnlyList<string> templates) =>
        templates.Where(NamesTheWinner).ToList();

    private static bool NamesTheWinner(string template) => template.Contains("{winner}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The finish call's lines for a finish that names nobody - practice, or
    /// no leader - with any "{winner}" lines left out. A finish row written
    /// with nothing else says a plain line of its own.
    /// </summary>
    internal static IReadOnlyList<string> FinishLinesNamingNobody(IReadOnlyList<string> templates)
    {
        var plain = templates.Where(t => !NamesTheWinner(t)).ToList();

        return plain.Count > 0 ? plain : new[] { "[shouting] Checkered flag - that's the race!" };
    }

    /// <summary>
    /// Where the winner's name comes from at the flag: the finish call's own
    /// lines when some have "{winner}" in them (11 Nov 2026, asked for by
    /// Asher) - one line, said straight after the fanfare, instead of the
    /// finish and then the Winner named call - else the Winner named row.
    /// Null when neither is on.
    /// </summary>
    private (AnnouncementEventConfig Config, IReadOnlyList<string> Templates, bool AsTheFinish)? WinnerLines()
    {
        var finish = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.RaceCompleted));

        if (finish is not null && CallOn(finish) && FinishLinesNamingTheWinner(finish.EffectiveTemplates) is { Count: > 0 } named)
        {
            return (finish, named, true);
        }

        return WinnerCallConfig() is { } winner ? (winner, winner.EffectiveTemplates, false) : null;
    }

    /// <summary>
    /// A qualifier scored by best consecutive laps has no winner to name
    /// (bug hunt 17): the car in front on the road has the most laps, and the
    /// heat's top qualifier - the one called on provisional pole - is whoever
    /// has the quickest run, often somebody else. The finish then names
    /// nobody, as in practice.
    /// </summary>
    internal static bool LeaderIsNotTheTopQualifier(bool qualifying, QualifyingMethod method) =>
        qualifying && method == QualifyingMethod.BestConsecutiveLaps;

    private bool NoWinnerToName => LeaderIsNotTheTopQualifier(IsQualifyingMode, _qualifyingMethod) || ScoredOnHandicapNow();

    /// <summary>
    /// Whether the race on track is scored on handicap, for the cars given:
    /// a class with lap or time credits, at least one of these cars with a
    /// credit to apply, on a start that did not spend the credits on track.
    /// Set by MainForm from the Handicaps page. Null for never.
    /// </summary>
    /// <remarks>
    /// WHY (15 Nov 2026, bug hunt 20): the first car home is not the winner
    /// then, and the recap says who won on handicap (CommentaryOfficialFacts.
    /// Handicap) - seconds after the flag had given the win to the scratch
    /// driver. As for a best-consecutive qualifier, the finish names nobody.
    /// </remarks>
    public Func<IReadOnlyCollection<string>, bool>? ScoredOnHandicap { get; set; }

    private bool ScoredOnHandicapNow()
    {
        try
        {
            return ScoredOnHandicap is { } scored
                && scored(_standings.Keys.Where(t => !GhostService.IsGhost(t)).ToList());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The car to name as the winner at the flag: the leader, or - when a
    /// ghost is in front (bug hunt 20) - the first real car. A ghost never
    /// wins: the result leaves ghosts out (RaceFilingService), as the class
    /// winners line here does, so "Victory for Ghost Sam!" named a winner the
    /// result did not have.
    /// </summary>
    private string? TheWinnerToName()
    {
        var leader = _currentLeaderTransponder;

        // Nor does a black-flagged car (bug hunt 22): the engine still counts
        // its laps, so it can lead the announcer's order to the flag while
        // the result puts it last, disqualified.
        if (leader is null || !CannotWin(leader))
        {
            return leader;
        }

        return _standings.Values
            .Where(s => !GhostService.IsGhost(s.Transponder) && !s.BlackFlagged)
            .OrderByDescending(s => s.Laps)
            .ThenBy(s => s.ElapsedMs)
            .Select(s => s.Transponder)
            .FirstOrDefault();
    }

    /// <summary>A ghost, or a car shown the black flag: never named the winner.</summary>
    private bool CannotWin(string transponder) =>
        GhostService.IsGhost(transponder)
        || (_standings.TryGetValue(transponder, out var standing) && standing.BlackFlagged);

    /// <summary>Whether the lines made ahead are the finish call's own - see WinnerLines. Under _winnerCallsLock.</summary>
    private bool _winnerCallsAreTheFinish;

    /// <summary>The finish call named the winner this race, so the Winner named call is not said as well. Under _winnerCallsLock.</summary>
    private bool _finishNamedTheWinner;

    /// <summary>The winner's line made ahead for each contender, by transponder: its words and, when it could be rendered, its key and render. Under _winnerCallsLock.</summary>
    private readonly Dictionary<string, (string Text, string? Key, Task<SynthesisResult>? Render)> _winnerCalls = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _winnerCallsLock = new();

    /// <summary>How many of the top of the field have their winner's line made ahead.</summary>
    internal const int WinnerContenders = 2;

    /// <summary>
    /// The race is on its last lap or past its time: the winner's line is
    /// made now for each of the top <see cref="WinnerContenders"/>. Returns
    /// at once; the renders run in the background.
    /// </summary>
    private void PrepareTheWinnerCalls()
    {
        try
        {
            if (NoWinnerToName)
            {
                return;
            }

            if (IsPracticeMode || Muted || WinnerLines() is not { } lines
                || RunningOrderNow(WinnerContenders, noGhosts: true) is not { Count: > 0 } order)
            {
                return;
            }

            var config = lines.Config;
            var speed = config.SpeedFor(_voiceStore.Current.Speed);
            var toMake = new List<(string Transponder, string Text)>();

            lock (_winnerCallsLock)
            {
                // Lines made as the other kind are for a choice since changed.
                if (_winnerCallsAreTheFinish != lines.AsTheFinish)
                {
                    _winnerCalls.Clear();
                    _winnerCallsAreTheFinish = lines.AsTheFinish;
                }
            }

            foreach (var row in order.Take(WinnerContenders))
            {
                var transponder = row.Standing.Transponder;

                lock (_winnerCallsLock)
                {
                    if (_winnerCalls.ContainsKey(transponder))
                    {
                        continue;
                    }
                }

                // The words here, on the thread that owns the standings; the
                // render in the background.
                var text = WinnerLine(lines.Templates, SpokenNameFor(row.Standing));

                lock (_winnerCallsLock)
                {
                    _winnerCalls[transponder] = (text, null, null);
                }

                toMake.Add((transponder, text));
            }

            // One after the other, the likelier winner first (11 Nov 2026):
            // both at once took both of the two synthesis slots on the last
            // lap, just as the last lap's own calls wanted one.
            // None of the queue's AsyncLocals reach the render - see RenderAhead.
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(async () =>
                {
                    foreach (var (transponder, text) in toMake)
                    {
                        if (MakeTheWinnerCall(transponder, text, speed) is { } render)
                        {
                            try
                            {
                                await render;
                            }
                            catch
                            {
                                // Said where it is taken, or not at all.
                            }
                        }
                    }
                });
            }

            StatusMessage?.Invoke($"[voice] Winner's call made ready for {string.Join(" and ", order.Take(WinnerContenders).Select(r => DisplayNameFor(r.Standing.Transponder)))}.");
        }
        catch (Exception ex)
        {
            // A head start, never a reason the finish fails.
            StatusMessage?.Invoke($"[voice] The winner's call could not be made ready ({ex.Message}) - it will be made at the flag.");
        }
    }

    /// <summary>Starts one contender's render, and notes it against them if the line is still theirs. The render, or null when there is none.</summary>
    private Task<SynthesisResult>? MakeTheWinnerCall(string transponder, string text, double speed)
    {
        try
        {
            if (LineAsItWillBeAsked(text, AnnouncementEventType.RaceCompleted, speed) is not { } line)
            {
                return null;
            }

            var render = RenderLineAsync(line.Profile, line.Text);

            // Observed, so a render nobody comes to take is not reported unobserved.
            _ = render.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            lock (_winnerCallsLock)
            {
                if (_winnerCalls.TryGetValue(transponder, out var call) && call.Text == text)
                {
                    _winnerCalls[transponder] = (text, line.Key, render);
                }
            }

            Timings.NoteRenderedAhead();

            return render;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] The winner's call could not be made ready ({ex.Message}) - it will be made at the flag.");

            return null;
        }
    }

    /// <summary>One of the club's winner lines, with the name in - {winner}, or {driver} as every other call has it.</summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): any other placeholder - "{class}",
    /// "{car}", "{team}" - was left in, so the voice was sent the braces at
    /// the flag and the screens showed them, while the same row's lines
    /// without {winner} had the empty clause tidied away. The winner's line
    /// fills nothing else, so every other placeholder is now taken out with
    /// its lead-in, the way an empty one is on every other call. A line with
    /// none is left exactly as it was.
    /// </remarks>
    internal static string WinnerLine(IReadOnlyList<string> templates, string winner)
    {
        var template = TemplateBag.Pick(templates);
        var unfilled = false;

        template = OtherPlaceholder.Replace(template, match =>
        {
            if (match.Value.Equals("{winner}", StringComparison.OrdinalIgnoreCase)
                || match.Value.Equals("{driver}", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            unfilled = true;

            return AnnouncementText.Missing;
        });

        var line = template
            .Replace("{winner}", winner, StringComparison.OrdinalIgnoreCase)
            .Replace("{driver}", winner, StringComparison.OrdinalIgnoreCase);

        return unfilled ? line.TidyTemplate() : line;
    }

    /// <summary>A {placeholder} in a winner's line - see WinnerLine.</summary>
    private static readonly System.Text.RegularExpressions.Regex OtherPlaceholder =
        new(@"\{[a-z]+\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// At the flag: the winner's line, first in the queue - said the moment
    /// the finish call has been. See the remarks.
    /// </summary>
    private void QueueTheWinnersCall()
    {
        (string Text, string? Key, Task<SynthesisResult>? Render) call;

        // A shared heat's class winners, made before anything below can
        // return (bug hunt 17): the line used to be built only once the
        // Winner named call was, so a finish that named the winner itself
        // left the class winners unsaid. Whatever is not queued behind the
        // winner's own call goes out by itself - see the finally.
        QueuedVoiceItem? classWinners = null;

        try
        {
            classWinners = TheClassWinnersCall();

            lock (_winnerCallsLock)
            {
                // Named in the finish call itself - see TrySpeakTheFinishWithTheWinner.
                if (_finishNamedTheWinner)
                {
                    _finishNamedTheWinner = false;
                    _winnerCalls.Clear();
                    return;
                }

                // Made as finish lines, which the finish did not say after all.
                if (_winnerCallsAreTheFinish)
                {
                    _winnerCalls.Clear();
                    _winnerCallsAreTheFinish = false;
                }
            }

            var winner = TheWinnerToName();

            if (NoWinnerToName)
            {
                return;
            }

            if (winner is null || IsPracticeMode || Muted || WinnerCallConfig() is not { } config)
            {
                return;
            }

            var speed = config.SpeedFor(_voiceStore.Current.Speed);

            lock (_winnerCallsLock)
            {
                if (!_winnerCalls.TryGetValue(winner, out call))
                {
                    call = (WinnerLine(config.EffectiveTemplates, _standings.TryGetValue(winner, out var standing) ? SpokenNameFor(standing) : DisplayNameFor(winner)), null, null);
                }

                _winnerCalls.Clear();
            }

            var queued = new QueuedVoiceItem(AnnouncementEventType.RaceCompleted, winner, OnAir: "Winner", ShownText: call.Text, Work: async () =>
            {
                try
                {
                    // WHY (23 Nov 2026, bug hunt 24): put on the screens as it
                    // comes to be said. This line goes straight into the queue,
                    // past EnqueueVoiceWork - the only place a queued line's
                    // words reach the Big Screen and the Announcer feed - so
                    // the room heard "Victory for Sam!" and neither showed it.
                    // Raised here rather than at the insert so the caption
                    // does not jump ahead of the finish call still being said.
                    RaiseAnnouncementText(call.Text, winner);

                    // Handed over only now - a flag's clear-out empties the
                    // render-ahead list, and this line outlives the finish's.
                    if (call.Key is { } key && call.Render is { } render)
                    {
                        OfferRenderedAhead(key, render);
                    }

                    await SynthesizeAndPlayAsync(call.Text, "[voice] Winner", speedOverride: speed);
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Playback failed: {ex.Message}");
                }
            });

            lock (_voiceQueueLock)
            {
                _voiceQueue.Insert(0, queued);

                if (classWinners is not null)
                {
                    _voiceQueue.Insert(1, classWinners);
                }
            }

            _voiceQueueSignal.Release();

            if (classWinners is not null)
            {
                _voiceQueueSignal.Release();
            }

            classWinners = null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] The winner's call could not be queued ({ex.Message}).");
        }
        finally
        {
            // The finish call named the winner, so there is no winner's call
            // to follow: the class winners go first in the queue themselves,
            // and are said the moment the finish call has been.
            if (classWinners is not null)
            {
                lock (_voiceQueueLock)
                {
                    _voiceQueue.Insert(0, classWinners);
                }

                _voiceQueueSignal.Release();
            }
        }
    }

    /// <summary>
    /// A shared heat (11 Nov 2026, Phase 4.1, mixed-class heats): each
    /// class's winner, said straight after the overall winner - see
    /// MixedHeats.ClassWinnersLine. Null, and nothing extra, for every
    /// one-class race, and whenever the winner is not being named at the
    /// flag at all. Named either by the Winner named row or by the finish
    /// call that has just said the name itself (bug hunt 17).
    /// </summary>
    private QueuedVoiceItem? TheClassWinnersCall()
    {
        bool namedByTheFinish;

        lock (_winnerCallsLock)
        {
            namedByTheFinish = _finishNamedTheWinner;
        }

        var winner = TheWinnerToName();

        var config = WinnerCallConfig()
            ?? (namedByTheFinish ? _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.RaceCompleted)) : null);

        if (winner is null || IsPracticeMode || Muted || NoWinnerToName || config is null
            || SharedHeatClasses?.Invoke() is not { } classOf
            || MixedHeats.ClassWinnersLine(
                // WHY (23 Nov 2026, bug hunt 24): black-flagged cars left out
                // too, as TheWinnerToName does. The engine still counts their
                // laps, so a disqualified car still first of its class on laps
                // was said as "Mo wins Mod" while the result puts it last.
                _standings.Values
                    .Where(s => !GhostService.IsGhost(s.Transponder) && !s.BlackFlagged)
                    .OrderByDescending(s => s.Laps)
                    .ThenBy(s => s.ElapsedMs)
                    .Select(s => s.Transponder)
                    .ToList(),
                classOf,
                t => _standings.TryGetValue(t, out var s) ? SpokenNameFor(s) : DisplayNameFor(t)) is not { } line)
        {
            return null;
        }

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        return new QueuedVoiceItem(AnnouncementEventType.RaceCompleted, winner, OnAir: "Class winners", ShownText: line, Work: async () =>
        {
            try
            {
                // On the screens as it comes to be said (23 Nov 2026, bug
                // hunt 24) - see the winner's call in QueueTheWinnersCall.
                RaiseAnnouncementText(line, winner);

                await SynthesizeAndPlayAsync(line, "[voice] Class winners", speedOverride: speed);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Playback failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Which class each car of the race on track runs in, when it is a shared
    /// heat (11 Nov 2026, Phase 4.1) - null for a one-class race, which is
    /// called exactly as before. Set by MainForm from the round on track
    /// (MixedHeats.ClassMapFor).
    /// </summary>
    public Func<IReadOnlyDictionary<string, string>?>? SharedHeatClasses { get; set; }

    /// <summary>
    /// At the flag, the finish call with the winner's name in it, when the
    /// club's finish lines have "{winner}" - see WinnerLines. Made on the last
    /// lap for the top two; for anyone else it is made now, while the fanfare
    /// plays. False when the finish names nobody (practice, no leader, no
    /// such line), and the finish call goes on as it always did.
    /// </summary>
    private bool TrySpeakTheFinishWithTheWinner(AnnouncementEventConfig config)
    {
        var winner = TheWinnerToName();

        if (winner is null || IsPracticeMode || NoWinnerToName || FinishLinesNamingTheWinner(config.EffectiveTemplates) is not { Count: > 0 } named)
        {
            return false;
        }

        var profile = _voiceStore.Current;
        var speed = config.SpeedFor(profile.Speed);
        (string Text, string? Key, Task<SynthesisResult>? Render) call;

        lock (_winnerCallsLock)
        {
            if (!_winnerCallsAreTheFinish || !_winnerCalls.TryGetValue(winner, out call))
            {
                call = (WinnerLine(named, _standings.TryGetValue(winner, out var standing) ? SpokenNameFor(standing) : DisplayNameFor(winner)), null, null);
            }

            _winnerCalls.Clear();
            _winnerCallsAreTheFinish = false;
            _finishNamedTheWinner = true;
        }

        var text = call.Text;
        var how = "named the winner, made ahead";
        var take = call.Render;

        async Task<SynthesisResult> MakeItNow()
        {
            if (LineAsItWillBeAsked(text, AnnouncementEventType.RaceCompleted, speed) is not { } line)
            {
                throw new InvalidOperationException("the announcer can't speak just now");
            }

            return await RenderLineAsync(line.Profile, line.Text);
        }

        // None of the queue's AsyncLocals reach the render - see RenderAhead.
        using (ExecutionContext.SuppressFlow())
        {
            if (take is null)
            {
                how = "named the winner, made at the flag";
                take = Task.Run(() => MakeItNow());
            }
            else if (!take.IsCompletedSuccessfully)
            {
                // WHY (23 Nov 2026, bug hunt 24): an early render that failed
                // (a timeout, a 429, a blip, with no Kokoro standing in) was
                // handed on as it was, so the take replayed that old failure:
                // the fanfare, then no finish call and no winner named, though
                // ElevenLabs had been fine for seconds. It is made again now,
                // once, as every other line made ahead is (RenderAheadOrNowAsync).
                var early = take;
                take = Task.Run(() => MadeAgainIfItFailed(early, () => MakeItNow()));
            }
        }

        SpeakFlagTake(AnnouncementEventType.RaceCompleted, text, take, profile, config, how);

        return true;
    }

    /// <summary>
    /// The early render's result, or - when it failed or was cancelled - the
    /// line made again now, once (23 Nov 2026, bug hunt 24). A second failure
    /// is the take's failure, said in the log where it is taken.
    /// </summary>
    internal static async Task<T> MadeAgainIfItFailed<T>(Task<T> early, Func<Task<T>> again)
    {
        try
        {
            return await early;
        }
        catch
        {
            return await again();
        }
    }

    /// <summary>A new race: no line made for the last one's winner is about this one's.</summary>
    private void ForgetTheWinnerCalls()
    {
        lock (_winnerCallsLock)
        {
            _winnerCalls.Clear();
            _winnerCallsAreTheFinish = false;
            _finishNamedTheWinner = false;
        }
    }
}
