namespace HeatCaller;

/// <summary>
/// A twenty-second pretend race in one soundscape: staging, countdown, the
/// start, a few laps, a fast lap, a lead change, a streak, the last lap and
/// the flag - each moment's sound, and (optionally) a line in that
/// soundscape's words.
///
/// Nothing here touches the club's settings or the race. Sounds are played
/// straight from the soundscape's own SoundEffectsProfile; the lines go
/// through AnnouncementService.SpeakReminder, so they are spoken by
/// whichever voice this PC already uses (Windows, the HeatCaller voice, or
/// the club's own ElevenLabs key - which is why voice is optional: on a
/// paid key every preview spends characters).
///
/// On the HeatCaller voice a soundscape's own lines are never applied (see
/// ThemeStore.Apply), so the preview speaks the lines the club would really
/// hear - the standard ones - rather than promising words it won't get.
/// </summary>
public static class SoundscapePreview
{
    private static readonly Random Rng = new();

    private sealed record Beat(SoundEffectKind? Sound, AnnouncementEventType? Line, int PauseAfterMs);

    /// <summary>The pretend race, in order.</summary>
    private static readonly Beat[] Script =
    {
        new(SoundEffectKind.RaceStaging, AnnouncementEventType.RaceStaging, 400),
        new(SoundEffectKind.CountdownStarted, null, 300),
        new(SoundEffectKind.RaceStarted, AnnouncementEventType.RaceStarted, 700),
        new(SoundEffectKind.GoodLap, null, 500),
        new(SoundEffectKind.GoodLap, null, 500),
        new(SoundEffectKind.PersonalBest, null, 500),
        new(SoundEffectKind.InvalidLap, null, 600),
        new(SoundEffectKind.FastLap, AnnouncementEventType.FastLap, 600),
        new(SoundEffectKind.LeaderChange, AnnouncementEventType.LeaderChange, 600),

        // A caution and the restart, so the flag sounds are heard in the
        // pack's style too. The countdown ticks are left out: five seconds
        // of beeping is a long time in a preview.
        new(SoundEffectKind.YellowFlag, AnnouncementEventType.YellowFlag, 600),
        new(SoundEffectKind.GreenFlagResumed, AnnouncementEventType.GreenFlagResumed, 600),
        new(SoundEffectKind.Streak, AnnouncementEventType.Streak, 600),
        new(SoundEffectKind.TrackRecord, AnnouncementEventType.TrackRecord, 600),
        new(SoundEffectKind.RaceTimeOver, AnnouncementEventType.RaceTimeOver, 700),
        new(SoundEffectKind.RaceCompleted, AnnouncementEventType.RaceCompleted, 0),
    };

    /// <summary>
    /// Plays the pretend race. <paramref name="progress"/> gets a short
    /// description of each moment as it plays, for a status line.
    /// </summary>
    public static async Task RunAsync(
        Theme theme,
        VoiceProfile currentVoice,
        float volume,
        double leadInSeconds,
        AnnouncementService? announcer,
        bool withVoice,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        // The words the club would actually hear - see the class summary.
        var lineSource = currentVoice.EffectiveEngine == VoiceEngine.HeatCaller ? currentVoice : theme.Voice;

        foreach (var beat in Script)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (beat.Sound is { } kind)
            {
                progress?.Invoke(SoundEffectsProfile.Describe(kind));
                var config = theme.SoundEffects.Effects.GetValueOrDefault(kind.ToString());

                try
                {
                    await SoundPacks.PlayAsync(config, kind, volume, streakCount: 6, streakThreshold: 3,
                        leadInSeconds: leadInSeconds, cancellationToken: cancellationToken);
                }
                catch
                {
                    // A preview sound that fails is skipped, not reported.
                }
            }

            if (withVoice && announcer is not null && beat.Line is { } type && !cancellationToken.IsCancellationRequested)
            {
                var line = PickLine(lineSource, type);
                if (line is not null)
                {
                    progress?.Invoke($"\"{VoiceProfile.StripAudioTags(line)}\"");
                    announcer.SpeakReminder(line, (theme.Voice.Stability, theme.Voice.Style));
                    await WaitForVoiceAsync(announcer, cancellationToken);
                }
            }

            try
            {
                await Task.Delay(beat.PauseAfterMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>A random line for the moment with made-up race details filled in, or null if there are none.</summary>
    private static string? PickLine(VoiceProfile voice, AnnouncementEventType type)
    {
        // A soundscape file can carry a moment with no settings at all
        // ("RaceStarted": null), so the entry itself may be missing too.
        if (!voice.Events.TryGetValue(type.ToString(), out var config)
            || config?.Templates is not { Count: > 0 })
        {
            return null;
        }

        var usable = config.Templates.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        return usable[Rng.Next(usable.Count)]
            .Replace("{driver}", "Sam")
            .Replace("{time}", "9.84")
            .Replace("{laptime}", "9.84")
            .Replace("{lap}", "7")
            .Replace("{streak}", "6");
    }

    /// <summary>Waits for a line to be spoken - generously, since a first render on a slow connection takes a while - and gives up rather than hang.</summary>
    private static async Task WaitForVoiceAsync(AnnouncementService announcer, CancellationToken cancellationToken)
    {
        try
        {
            // Give the queue a moment to pick the line up.
            await Task.Delay(250, cancellationToken);

            var waited = 0;
            while (announcer.IsAudioPending && waited < 12000)
            {
                await Task.Delay(100, cancellationToken);
                waited += 100;
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }
}
