using System.Collections.Concurrent;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The consent behind a cloned voice (bug hunt 7, 13 Oct 2026).
///
/// Taking a consent back stopped new lines in the person's voice but every
/// saved and prepared take went on playing; recording one left the voice
/// resting for half an hour; and a consent for another voice read as if it
/// covered the one now chosen.
/// </summary>
public sealed class BugHunt7ConsentTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Clone = "acme-dave";

    private const double TakeStability = 0.35;
    private const double TakeStyle = 0.6;

    private static readonly DateTime Agreed = new(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>The relay's catalogue has said this voice is a clone; the consent is as given.</summary>
    private static AppSettings ClonedVoiceSettings(DateTime? revoked, bool recorded = true)
    {
        var settings = new AppSettings();
        settings.HeatCallerClonedVoices.Add(Clone);

        if (recorded)
        {
            settings.HeatCallerConsentVoiceId = Clone;
            settings.HeatCallerConsentWho = "Dave Bellamy";
            settings.HeatCallerConsentAtUtc = Agreed;
            settings.HeatCallerConsentRevokedUtc = revoked;
        }

        return settings;
    }

    /// <summary>
    /// An announcer on the HeatCaller voice, set to the cloned one, with a
    /// take of <paramref name="text"/> already in the saved-voice library.
    /// No relay is set up, so anything that is not the saved take comes
    /// back as no take at all.
    /// </summary>
    private static async Task<(AnnouncementService Announcer, byte[] SavedTake)> AnnouncerWithSavedTakeAsync(
        string text, AppSettings settings)
    {
        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.HeatCaller;
        voices.Current.HeatCallerVoice = Clone;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load())
        {
            HeatCallerVoice = new HeatCallerVoiceClient(settings),
        };

        // Looks like an MP3 to the library, which keeps nothing else.
        var take = new byte[512];
        take[0] = (byte)'I';
        take[1] = (byte)'D';
        take[2] = (byte)'3';
        take[100] = 42;

        var profile = voices.Current.CloneForDelivery(TakeStability, TakeStyle);
        VoiceLibrary.Shared.Save(profile, text, take);

        // Saved in the background.
        for (var i = 0; i < 100 && !VoiceLibrary.Shared.Contains(profile, text); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(VoiceLibrary.Shared.Contains(profile, text));
        return (announcer, take);
    }

    // --- Saved takes and the consent ---------------------------------------

    [Fact]
    public async Task A_saved_take_in_a_cloned_voice_is_not_played_once_the_consent_is_taken_back()
    {
        const string text = "Drivers, this is the consent taken back check, bring your cars to the grid please";
        var (announcer, saved) = await AnnouncerWithSavedTakeAsync(text, ClonedVoiceSettings(revoked: Agreed.AddDays(30)));

        var take = await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle);

        Assert.NotEqual(saved, take);
    }

    [Fact]
    public async Task A_saved_take_in_a_cloned_voice_is_not_played_with_no_consent_recorded()
    {
        const string text = "Drivers, this is the consent never recorded check, bring your cars to the grid please";
        var (announcer, saved) = await AnnouncerWithSavedTakeAsync(text, ClonedVoiceSettings(revoked: null, recorded: false));

        var take = await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle);

        Assert.NotEqual(saved, take);
    }

    [Fact]
    public async Task A_saved_take_in_a_cloned_voice_still_plays_while_the_consent_stands()
    {
        const string text = "Drivers, this is the consent standing check, bring your cars to the grid please";
        var (announcer, saved) = await AnnouncerWithSavedTakeAsync(text, ClonedVoiceSettings(revoked: null));

        var take = await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle);

        Assert.Equal(saved, take);
    }

    [Fact]
    public void The_client_answers_the_consent_the_way_its_own_gate_does()
    {
        Assert.True(new HeatCallerVoiceClient(ClonedVoiceSettings(revoked: null)).ConsentAllows(Clone));
        Assert.False(new HeatCallerVoiceClient(ClonedVoiceSettings(revoked: Agreed.AddDays(1))).ConsentAllows(Clone));
        Assert.False(new HeatCallerVoiceClient(ClonedVoiceSettings(revoked: null, recorded: false)).ConsentAllows(Clone));

        // A stock voice needs nothing.
        Assert.True(new HeatCallerVoiceClient(ClonedVoiceSettings(revoked: null, recorded: false)).ConsentAllows("clubhouse"));
    }

    // --- Recording a consent ends the half-hour rest ------------------------

    [Fact]
    public async Task Recording_a_consent_tries_the_voice_again_from_the_next_line()
    {
        const string text = "Drivers, this is the consent rest check, bring your cars to the grid please";

        // A relay set up, so the line gets as far as the consent gate and is
        // refused there - no request goes anywhere.
        var settings = ClonedVoiceSettings(revoked: null, recorded: false);
        settings.VoiceRelayBaseUrl = "http://localhost:8787";
        settings.VoiceRelayClub = "testclub";
        settings.VoiceRelayToken = "club-token";

        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.HeatCaller;
        voices.Current.HeatCallerVoice = Clone;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load())
        {
            HeatCallerVoice = new HeatCallerVoiceClient(settings),
        };

        await announcer.RecordVoiceLineAsync(text, TakeStability, TakeStyle);

        var retry = typeof(AnnouncementService).GetField("_heatCallerRetryUtc", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Resting on the refusal, for the half hour a settled one gets.
        Assert.True((DateTime)retry.GetValue(announcer)! > DateTime.UtcNow.AddMinutes(10));

        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        announcer.RetryHeatCallerVoiceSoon("a voice consent was recorded on the Voice tab");

        Assert.Equal(DateTime.MinValue, (DateTime)retry.GetValue(announcer)!);
        Assert.Contains(log, line => line.Contains("tried again", StringComparison.Ordinal));
    }

    [Fact]
    public void Recording_a_consent_says_nothing_when_the_voice_was_not_resting()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        announcer.RetryHeatCallerVoiceSoon("a voice consent was recorded on the Voice tab");

        Assert.Empty(log);
    }

    // --- The consent line against the voice now chosen ----------------------

    [Fact]
    public void A_consent_for_another_cloned_voice_says_so()
    {
        var consent = new VoiceConsent.Record(Clone, "Dave Bellamy", Agreed);

        var line = VoiceConsent.Describe(consent, "acme-sue", nowCloned: true);

        Assert.StartsWith(VoiceConsent.Describe(consent), line, StringComparison.Ordinal);
        Assert.Contains(Clone, line, StringComparison.Ordinal);
        Assert.Contains("free voice reads every line", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_consent_for_another_voice_does_not_claim_a_stock_voice_is_quiet()
    {
        var consent = new VoiceConsent.Record(Clone, "Dave Bellamy", Agreed);

        var line = VoiceConsent.Describe(consent, "clubhouse", nowCloned: false);

        Assert.Contains(Clone, line, StringComparison.Ordinal);
        Assert.DoesNotContain("free voice", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_consent_for_the_voice_chosen_reads_as_it_always_did()
    {
        var consent = new VoiceConsent.Record(Clone, "Dave Bellamy", Agreed);
        var revoked = consent with { RevokedAtUtc = Agreed.AddDays(3) };

        Assert.Equal(VoiceConsent.Describe(consent), VoiceConsent.Describe(consent, " ACME-DAVE ", nowCloned: true));
        Assert.Equal(VoiceConsent.Describe(revoked), VoiceConsent.Describe(revoked, "acme-sue", nowCloned: true));
        Assert.Equal(VoiceConsent.Describe(null), VoiceConsent.Describe(null, Clone, nowCloned: true));
        Assert.Equal(VoiceConsent.Describe(consent), VoiceConsent.Describe(consent, "", nowCloned: false));
    }
}
