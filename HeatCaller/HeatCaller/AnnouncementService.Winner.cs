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
/// Said only when the finish call is on: a club that switched the finish
/// call off for the fanfare alone has asked for no words at the flag. The
/// unofficial result is still read after the cheering, with the places and
/// the fastest lap.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>The winner's line. {winner} is the driver's name as the announcer says it.</summary>
    internal static readonly string[] WinnerLines =
    {
        "[excited] {winner} wins it!",
        "[excited] And it's {winner} who takes the win!",
        "[excited] {winner} takes the checkered flag first!",
    };

    /// <summary>The winner's line made ahead for each contender, by transponder: its words and, when it could be rendered, its key and render. Under _winnerCallsLock.</summary>
    private readonly Dictionary<string, (string Text, string? Key, Task<SynthesisResult>? Render)> _winnerCalls = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _winnerCallsLock = new();

    /// <summary>How many of the top of the field have their winner's line made ahead.</summary>
    internal const int WinnerContenders = 2;

    /// <summary>Whether the finish call is on, which the winner's line goes with.</summary>
    private bool FinishCallOn() =>
        _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.RaceCompleted)) is { } finish && CallOn(finish);

    /// <summary>
    /// The race is on its last lap or past its time: the winner's line is
    /// made now for each of the top <see cref="WinnerContenders"/>. Returns
    /// at once; the renders run in the background.
    /// </summary>
    private void PrepareTheWinnerCalls()
    {
        try
        {
            if (IsPracticeMode || Muted || !FinishCallOn() || RunningOrderNow(WinnerContenders) is not { Count: > 0 } order)
            {
                return;
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
                var text = WinnerLine(SpokenNameFor(row.Standing));

                lock (_winnerCallsLock)
                {
                    _winnerCalls[transponder] = (text, null, null);
                }

                // None of the queue's AsyncLocals reach the render - see RenderAhead.
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(() => MakeTheWinnerCall(transponder, text));
                }
            }

            StatusMessage?.Invoke($"[voice] Winner's call made ready for {string.Join(" and ", order.Take(WinnerContenders).Select(r => DisplayNameFor(r.Standing.Transponder)))}.");
        }
        catch (Exception ex)
        {
            // A head start, never a reason the finish fails.
            StatusMessage?.Invoke($"[voice] The winner's call could not be made ready ({ex.Message}) - it will be made at the flag.");
        }
    }

    /// <summary>Starts one contender's render, and notes it against them if the line is still theirs.</summary>
    private void MakeTheWinnerCall(string transponder, string text)
    {
        try
        {
            if (LineAsItWillBeAsked(text, AnnouncementEventType.RaceCompleted, speed: null) is not { } line)
            {
                return;
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
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] The winner's call could not be made ready ({ex.Message}) - it will be made at the flag.");
        }
    }

    /// <summary>One of the winner's lines, with the name in.</summary>
    internal static string WinnerLine(string winner) =>
        TemplateBag.Pick(WinnerLines).Replace("{winner}", winner, StringComparison.OrdinalIgnoreCase);

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

            if (winner is null || IsPracticeMode || Muted || !FinishCallOn())
            {
                return;
            }

            lock (_winnerCallsLock)
            {
                if (!_winnerCalls.TryGetValue(winner, out call))
                {
                    call = (WinnerLine(_standings.TryGetValue(winner, out var standing) ? SpokenNameFor(standing) : DisplayNameFor(winner)), null, null);
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

                    await SynthesizeAndPlayAsync(call.Text, "[voice] Winner");
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
