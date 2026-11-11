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
            if (IsPracticeMode || Muted || WinnerCallConfig() is not { } config
                || RunningOrderNow(WinnerContenders) is not { Count: > 0 } order)
            {
                return;
            }

            var speed = config.SpeedFor(_voiceStore.Current.Speed);
            var toMake = new List<(string Transponder, string Text)>();

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
                var text = WinnerLine(config.EffectiveTemplates, SpokenNameFor(row.Standing));

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

    /// <summary>A new race: no line made for the last one's winner is about this one's.</summary>
    private void ForgetTheWinnerCalls()
    {
        lock (_winnerCallsLock)
        {
            _winnerCalls.Clear();
        }
    }
}
