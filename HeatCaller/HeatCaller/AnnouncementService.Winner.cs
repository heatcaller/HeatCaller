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
            if (IsPracticeMode || Muted || WinnerLines() is not { } lines
                || RunningOrderNow(WinnerContenders) is not { Count: > 0 } order)
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
    internal static string WinnerLine(IReadOnlyList<string> templates, string winner) =>
        TemplateBag.Pick(templates)
            .Replace("{winner}", winner, StringComparison.OrdinalIgnoreCase)
            .Replace("{driver}", winner, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// At the flag: the winner's line, first in the queue - said the moment
    /// the finish call has been. See the remarks.
    /// </summary>
    private void QueueTheWinnersCall()
    {
        (string Text, string? Key, Task<SynthesisResult>? Render) call;

        try
        {
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

            var winner = _currentLeaderTransponder;

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
            }

            _voiceQueueSignal.Release();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] The winner's call could not be queued ({ex.Message}).");
        }
    }

    /// <summary>
    /// At the flag, the finish call with the winner's name in it, when the
    /// club's finish lines have "{winner}" - see WinnerLines. Made on the last
    /// lap for the top two; for anyone else it is made now, while the fanfare
    /// plays. False when the finish names nobody (practice, no leader, no
    /// such line), and the finish call goes on as it always did.
    /// </summary>
    private bool TrySpeakTheFinishWithTheWinner(AnnouncementEventConfig config)
    {
        var winner = _currentLeaderTransponder;

        if (winner is null || IsPracticeMode || FinishLinesNamingTheWinner(config.EffectiveTemplates) is not { Count: > 0 } named)
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

        if (take is null)
        {
            how = "named the winner, made at the flag";

            // None of the queue's AsyncLocals reach the render - see RenderAhead.
            using (ExecutionContext.SuppressFlow())
            {
                take = Task.Run(async () =>
                {
                    if (LineAsItWillBeAsked(text, AnnouncementEventType.RaceCompleted, speed) is not { } line)
                    {
                        throw new InvalidOperationException("the announcer can't speak just now");
                    }

                    return await RenderLineAsync(line.Profile, line.Text);
                });
            }
        }

        SpeakFlagTake(AnnouncementEventType.RaceCompleted, text, take, profile, config, how);

        return true;
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
