using System.IO.Compression;

namespace HeatCaller.Tests;

/// <summary>
/// RestoredSettings - a restored backup's addresses held back until checked
/// (25 Sep security audit). A backup edited to change only the relay or mail
/// server address, restored here, used to send this laptop's own sealed
/// secrets there from the first tick: the publish token, the mailbox
/// password, and every racer's PIN.
/// </summary>
public sealed class RestoredSettingsTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    private const string ClubRelay = "https://relay.club.invalid";
    private const string EvilRelay = "https://relay.evil.invalid";
    private const string ClubMail = "mail.club.invalid";
    private const string EvilMail = "mx.evil.invalid";

    public void Dispose() => _data.Dispose();

    // --- What a backup's settings need confirming ------------------------------------

    [Fact]
    public void A_relay_address_this_laptop_wasn_t_using_is_held()
    {
        var parts = RestoredSettings.WhatNeedsConfirming(
            Settings(relay: ClubRelay), Settings(relay: EvilRelay));

        Assert.Equal(RestoredSettings.Part.PublishRelay, parts);
    }

    [Fact]
    public void The_same_relay_written_differently_is_not_new()
    {
        var parts = RestoredSettings.WhatNeedsConfirming(
            Settings(relay: "https://Relay.Club.invalid/"), Settings(relay: "relay.club.invalid"));

        Assert.Equal(RestoredSettings.Part.None, parts);
    }

    [Fact]
    public void A_mail_server_this_laptop_wasn_t_using_is_held_and_its_own_is_not()
    {
        Assert.Equal(RestoredSettings.Part.Mail,
            RestoredSettings.WhatNeedsConfirming(Settings(mail: ClubMail), Settings(mail: EvilMail)));

        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Settings(mail: ClubMail), Settings(mail: "MAIL.club.invalid ")));
    }

    [Fact]
    public void A_mail_server_from_the_page_s_own_list_is_not_held_even_on_a_new_laptop()
    {
        // Nobody but Google can answer as smtp.gmail.com.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(null, Settings(mail: "smtp.mail.me.com")));
    }

    [Fact]
    public void On_a_laptop_with_no_settings_yet_every_address_in_the_backup_is_new()
    {
        var parts = RestoredSettings.WhatNeedsConfirming(null,
            Settings(relay: ClubRelay, voiceRelay: "https://voice.club.invalid", mail: ClubMail));

        Assert.Equal(RestoredSettings.Part.All, parts);
    }

    [Fact]
    public void A_blank_address_sends_nothing_anywhere_so_holds_nothing()
    {
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Settings(relay: ClubRelay), Settings(relay: string.Empty)));
    }

    // --- The record --------------------------------------------------------------------

    [Fact]
    public void A_second_restore_adds_to_what_is_held_and_never_clears_it()
    {
        RestoredSettings.Hold(RestoredSettings.Part.Mail);
        RestoredSettings.Hold(RestoredSettings.Part.PublishRelay);

        Assert.Equal(RestoredSettings.Part.PublishRelay | RestoredSettings.Part.Mail, RestoredSettings.Held);
    }

    [Fact]
    public void Confirming_the_last_one_removes_the_record()
    {
        RestoredSettings.Hold(RestoredSettings.Part.Mail | RestoredSettings.Part.PublishRelay);

        RestoredSettings.Confirm(RestoredSettings.Part.Mail);
        Assert.Equal(RestoredSettings.Part.PublishRelay, RestoredSettings.Held);

        RestoredSettings.Confirm(RestoredSettings.Part.PublishRelay);
        Assert.Equal(RestoredSettings.Part.None, RestoredSettings.Held);
        Assert.False(File.Exists(_data.PathOf(RestoredSettings.FileName)));
    }

    [Fact]
    public void A_damaged_record_holds_everything()
    {
        File.WriteAllText(_data.PathOf(RestoredSettings.FileName), "{ not json");

        Assert.Equal(RestoredSettings.Part.All, RestoredSettings.Held);
    }

    // --- Through a real restore -----------------------------------------------------------

    [Fact]
    public void Restoring_a_backup_with_new_addresses_holds_them()
    {
        File.WriteAllText(_data.PathOf("settings.json"), Settings(relay: ClubRelay, mail: ClubMail));

        Restore(Settings(relay: EvilRelay, mail: EvilMail));

        Assert.Equal(RestoredSettings.Part.PublishRelay | RestoredSettings.Part.Mail, RestoredSettings.Held);
        Assert.NotNull(RestoredSettings.StartupNote());
    }

    [Fact]
    public void Restoring_the_club_s_own_backup_on_its_own_laptop_asks_nothing()
    {
        File.WriteAllText(_data.PathOf("settings.json"), Settings(relay: ClubRelay, mail: ClubMail));

        Restore(Settings(relay: ClubRelay, mail: ClubMail));

        Assert.Equal(RestoredSettings.Part.None, RestoredSettings.Held);
        Assert.Null(RestoredSettings.StartupNote());
    }

    [Fact]
    public void A_backup_cannot_bring_its_own_record_to_clear_the_holds_in_advance()
    {
        File.WriteAllText(_data.PathOf("settings.json"), Settings(relay: ClubRelay, mail: ClubMail));

        Restore(Settings(relay: EvilRelay, mail: EvilMail),
            (RestoredSettings.FileName, "{\"RestoredUtc\":\"2026-01-01T00:00:00Z\",\"Held\":[]}"));

        Assert.Equal(RestoredSettings.Part.PublishRelay | RestoredSettings.Part.Mail, RestoredSettings.Held);
    }

    // --- What is held back ------------------------------------------------------------------

    [Fact]
    public void The_relay_publisher_sends_nothing_to_a_held_address_until_it_is_confirmed()
    {
        RestoredSettings.Hold(RestoredSettings.Part.PublishRelay);

        // Switched off, so the publisher never tries the network either way.
        var settings = new AppSettings { RelayEnabled = false, RelayBaseUrl = EvilRelay, RelayClub = "acme-rc", RelayToken = "hg_test" };

        using var publisher = new RelayPublisher(settings);
        Assert.False(publisher.Status.Configured);

        RestoredSettings.Confirm(RestoredSettings.Part.PublishRelay);
        publisher.SettingsChanged();

        Assert.True(publisher.Status.Configured);
    }

    [Fact]
    public void Nothing_is_emailed_through_a_held_mail_server_but_the_test_email_may_go()
    {
        RestoredSettings.Hold(RestoredSettings.Part.Mail);

        var settings = new AppSettings { SmtpHost = EvilMail, SmtpPort = 587, SmtpUser = "club@club.invalid", SmtpPassword = "app-password" };

        Assert.Contains(EvilMail, PinMailer.ConfigurationProblem(settings));
        Assert.False(PinMailer.IsConfigured(settings)); // the automatic pass, invites, Email PINs
        Assert.Null(PinMailer.SetupProblem(settings));  // Send Test Email

        RestoredSettings.Confirm(RestoredSettings.Part.Mail);

        Assert.Null(PinMailer.ConfigurationProblem(settings));
    }

    [Fact]
    public void The_voice_does_not_speak_through_a_held_club_relay()
    {
        RestoredSettings.Hold(RestoredSettings.Part.PublishRelay);

        var voice = new HeatCallerVoiceClient(new AppSettings { RelayBaseUrl = EvilRelay, RelayClub = "acme-rc", RelayToken = "hg_test" });
        Assert.False(voice.RelayConfigured);

        RestoredSettings.Confirm(RestoredSettings.Part.PublishRelay);
        Assert.True(voice.RelayConfigured);
    }

    [Fact]
    public void A_held_voice_relay_of_its_own_is_not_asked_and_the_club_relay_still_is()
    {
        RestoredSettings.Hold(RestoredSettings.Part.VoiceRelay);

        var ownRelay = new HeatCallerVoiceClient(new AppSettings
        {
            RelayBaseUrl = ClubRelay, RelayClub = "acme-rc", RelayToken = "hg_test",
            VoiceRelayBaseUrl = EvilRelay, VoiceRelayToken = "hg_voice",
        });

        var clubRelay = new HeatCallerVoiceClient(new AppSettings { RelayBaseUrl = ClubRelay, RelayClub = "acme-rc", RelayToken = "hg_test" });

        Assert.False(ownRelay.RelayConfigured);
        Assert.True(clubRelay.RelayConfigured);
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static string Settings(string? relay = null, string? voiceRelay = null, string? mail = null)
    {
        var fields = new Dictionary<string, object>();

        if (relay is not null)
        {
            fields[nameof(AppSettings.RelayBaseUrl)] = relay;
        }

        if (voiceRelay is not null)
        {
            fields[nameof(AppSettings.VoiceRelayBaseUrl)] = voiceRelay;
        }

        if (mail is not null)
        {
            fields[nameof(AppSettings.SmtpHost)] = mail;
        }

        return System.Text.Json.JsonSerializer.Serialize(fields);
    }

    /// <summary>Restores a zip holding these settings (and any other files), as the Backup page does.</summary>
    private void Restore(string settingsJson, params (string Name, string Text)[] others)
    {
        var zip = Path.Combine(_data.Folder, "backup.zip");

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in others.Prepend(("settings.json", settingsJson)))
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        // The safety copy of what was there goes in the test folder, never Documents.
        var result = BackupService.RestoreBackup(zip, Path.Combine(_data.Folder, "backups"));

        Assert.Empty(result.Failed);
    }
}
