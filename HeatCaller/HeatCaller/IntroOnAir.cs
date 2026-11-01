namespace HeatCaller;

/// <summary>
/// Which driver the introductions are on right now, for the Big Screen's
/// card (1 Nov 2026, asked for by Asher: the name and photo of each driver
/// on the big screen during the driver intros, with a switch for all of them
/// and one for each driver - a kid's photo can be kept off).
///
/// HOW IT KNOWS. Three things happen in order for every line the announcer
/// says, and this hears all three:
///
///   1. ARMED. Race Control, as it hands the introductions to the announcer
///      (Stage, or On Air's opening), says whose cards they are - see
///      <see cref="Arm"/>.
///   2. HEARD. The moment a line is about to be heard, its words come here
///      (MainForm.WireCaptions, beside the captions). The introductions are
///      told from any other line by naming the armed drivers; their words
///      are planned (IntroCards.Plan) into where each card goes up.
///   3. PLAYING. The voice reports the line's real length as it starts
///      (AnnouncementService.PlayPcmAsync), and each card's place in the
///      words becomes a time: that share of the way through the audio.
///
/// TIMED BY THE AUDIO, NOT BY A GUESS. The captions time a long line at an
/// estimated fifteen characters a second (CaptionLog), which drifts several
/// seconds over a minute of introductions - the sixth driver's face would go
/// up during the fifth driver's line. The audio's own length pins both ends,
/// so the drift is only how unevenly the voice reads in between.
///
/// AND STOPPED BY THE AUDIO. A flag, the countdown or the mute that cuts the
/// line off takes the card down at once; a line played to its end leaves the
/// last driver up for <see cref="Hold"/> and then gives the screen back.
/// Nothing depends on being told: a show is never up longer than its line.
///
/// STATIC, like SponsorOnAir and CaptionLog beside it: the Big Screen may not
/// be open, and the introductions being on air is a fact about the night, not
/// a message between two objects.
/// </summary>
internal sealed class IntroOnAir
{
    /// <summary>The one the app shares.</summary>
    public static IntroOnAir Shared { get; } = new();

    /// <summary>How long the last driver stays up once the line has finished.</summary>
    internal static readonly TimeSpan Hold = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How early a card goes up before its place in the audio - the Big
    /// Screen looks a few times a second, and a face a beat early reads as
    /// the screen keeping up; a beat late reads as it lagging.
    /// </summary>
    internal static readonly TimeSpan Early = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long armed cards wait to be heard - longer than the start ever
    /// waits for the introductions (RaceControlControl.MostForIntroductions)
    /// plus the opening before them, so a later line naming the same drivers
    /// (a recap) can never set them off.
    /// </summary>
    internal static readonly TimeSpan ArmedFor = TimeSpan.FromMinutes(6);

    private readonly object _gate = new();

    private Armed? _armed;
    private Planned? _heard;
    private Show? _show;
    private int _nextToken;

    private sealed record Armed(IReadOnlyList<IntroCard> Cards, string Words, DateTime AtUtc);

    private sealed record Planned(IReadOnlyList<IntroCard> Cards, IReadOnlyList<IntroCards.Placement> Plan, int Length);

    /// <summary>A show on air: each card's time, and when it all ends.</summary>
    private sealed record Show(int Token, IReadOnlyList<(IntroCard Card, DateTime FromUtc)> Cards, DateTime UntilUtc);

    /// <summary>
    /// Race Control is handing the driver introductions to the announcer:
    /// these are the cards, in grid order, and the words being handed over.
    /// Replaces any armed before.
    /// </summary>
    public void Arm(IReadOnlyList<IntroCard> cards, string? words, DateTime? nowUtc = null)
    {
        lock (_gate)
        {
            _armed = cards is { Count: > 0 }
                ? new Armed(cards.ToList(), Squashed(words), nowUtc ?? DateTime.UtcNow)
                : null;
        }
    }

    /// <summary>How many letters and digits from the start of the armed words a heard line has to share to be them.</summary>
    internal const int SameStart = 60;

    /// <summary>
    /// Whether a heard line is the words that were armed: the same letters
    /// and digits from the start, whatever happened to the spaces, the
    /// punctuation and any [audio tags] on the way to the voice. Either way
    /// round, so a line cut short is still them.
    /// </summary>
    internal static bool SameWords(string? armed, string? heard)
    {
        var a = Squashed(armed);
        var h = Squashed(heard);

        if (a.Length == 0 || h.Length == 0)
        {
            return false;
        }

        var n = Math.Min(SameStart, Math.Min(a.Length, h.Length));

        return string.CompareOrdinal(a, 0, h, 0, n) == 0;
    }

    /// <summary>Lower-case letters and digits only, with any [audio tags] taken out first.</summary>
    private static string Squashed(string? words)
    {
        var text = words ?? string.Empty;
        var kept = new System.Text.StringBuilder(text.Length);
        var inTag = false;

        foreach (var c in text)
        {
            if (c == '[')
            {
                inTag = true;
            }
            else if (c == ']')
            {
                inTag = false;
            }
            else if (!inTag && char.IsLetterOrDigit(c))
            {
                kept.Append(char.ToLowerInvariant(c));
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// A line is about to be heard. When it is the armed introductions, its
    /// cards are planned and wait for the line to play; any other line is let
    /// by, and the cards stay armed for the introductions behind it.
    /// </summary>
    public void Heard(string? words, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        lock (_gate)
        {
            // Whatever was planned and never played is not this line's.
            _heard = null;

            if (_armed is not { } armed || string.IsNullOrWhiteSpace(words))
            {
                return;
            }

            if (now - armed.AtUtc > ArmedFor)
            {
                _armed = null;
                return;
            }

            var spoken = armed.Cards.Select(c => c.SpokenName).ToList();
            var screen = armed.Cards.Select(c => c.ScreenName).ToList();

            // The introductions are the words armed. The opening said just
            // before them can name drivers too ("Sam and Bill, head to
            // head"), so naming them is not enough - but it is the fallback
            // when nothing came with the cards: at least two drivers, and at
            // least half of a bigger field.
            if (armed.Words.Length > 0)
            {
                if (!SameWords(armed.Words, words))
                {
                    return;
                }
            }
            else if (IntroCards.NamedCount(words, spoken, screen) < Math.Max(2, (armed.Cards.Count + 1) / 2))
            {
                return;
            }

            var plan = IntroCards.Plan(words, spoken, screen);

            _armed = null;

            if (plan.Count > 0)
            {
                _heard = new Planned(armed.Cards, plan, words.Length);
            }
        }
    }

    /// <summary>
    /// A line starts playing, <paramref name="lead"/> of silence then
    /// <paramref name="spokenFor"/> of voice. Returns a token for
    /// <see cref="Stopped"/>, or 0 when it is not the introductions.
    /// </summary>
    public int Playing(TimeSpan lead, TimeSpan spokenFor, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        lock (_gate)
        {
            if (_heard is not { } heard || spokenFor <= TimeSpan.Zero)
            {
                return 0;
            }

            _heard = null;

            var starts = now + (lead > TimeSpan.Zero ? lead : TimeSpan.Zero);
            var length = Math.Max(1, heard.Length);

            var cards = heard.Plan
                .Select(p => (heard.Cards[p.Driver], starts + spokenFor * (Math.Clamp(p.At, 0, length) / (double)length) - Early))
                .ToList();

            var token = ++_nextToken;

            _show = new Show(token, cards, starts + spokenFor + Hold);
            return token;
        }
    }

    /// <summary>
    /// The line with <paramref name="token"/> has stopped: played to its end,
    /// the last card stays up for <see cref="Hold"/>; cut off, the cards come
    /// down now. A token of 0, or of a show already over, changes nothing.
    /// </summary>
    public void Stopped(int token, bool heardToTheEnd, DateTime? nowUtc = null)
    {
        if (token == 0)
        {
            return;
        }

        var now = nowUtc ?? DateTime.UtcNow;

        lock (_gate)
        {
            if (_show is not { } show || show.Token != token)
            {
                return;
            }

            if (!heardToTheEnd)
            {
                _show = null;
                return;
            }

            // The voice can finish ahead of the words' share of the length:
            // every card left goes by, and the last stays up for the hold.
            var until = now + Hold;
            _show = show with { UntilUtc = until < show.UntilUtc ? until : show.UntilUtc };
        }
    }

    /// <summary>The card to show now, or null when no driver is being introduced.</summary>
    public IntroCard? Current(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        lock (_gate)
        {
            if (_show is not { } show)
            {
                return null;
            }

            if (now >= show.UntilUtc)
            {
                _show = null;
                return null;
            }

            IntroCard? current = null;

            foreach (var (card, from) in show.Cards)
            {
                if (from <= now)
                {
                    current = card;
                }
            }

            return current;
        }
    }

    /// <summary>Takes everything down and forgets what was armed - the race reset, or a test starting clean.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _armed = null;
            _heard = null;
            _show = null;
        }
    }
}
