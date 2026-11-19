using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Where a restored backup sent this laptop's secrets, held back until
/// somebody at the laptop has looked (25 Sep security audit).
///
/// THE HOLE. A restore copies settings.json in verbatim, and the addresses
/// in it are plain text: the relay's, the voice relay's, the mail server's.
/// The secrets that go to them are not in the file - they are sealed to
/// this laptop - so a backup edited to change only an address, and restored
/// here, sent this laptop's own secrets there from the first tick: the
/// club's publish token (and with it results, the roster, the check-in
/// document, and a manager inbox the laptop then runs requests out of), the
/// voice relay's token, and the mailbox password followed by one email per
/// racer with their check-in PIN in it. Nothing on screen said the address
/// had changed.
///
/// THE RULE. RestoreBackup compares each address in the backup with the one
/// this laptop was using before it, and any that is new is recorded here, in
/// a file of its own - never in settings.json, which is the file being
/// distrusted, and never taken from the backup (a backup that carries this
/// file has it left out). Until the club confirms it on the page where the
/// address is shown, nothing is sent there:
/// <list type="bullet">
/// <item>the relay: nothing published or collected until Save or Test
/// Connection on the Club Website page (RelayPublisher.ReadTarget);</item>
/// <item>the voice relay: no line asked for until Enter is pressed in its
/// address box on Voice Settings (HeatCallerVoiceClient.Target);</item>
/// <item>the mail server: nothing emailed until Send Test Email works, or
/// the server is chosen or typed again (PinMailer.ConfigurationProblem).</item>
/// </list>
/// An address this laptop was already using is not new, so restoring the
/// club's own backup on the club's own laptop asks nothing; nor is a mail
/// server from the page's own list (Gmail, iCloud, Outlook.com), which
/// nobody but the provider can answer as.
///
/// Fails closed: a record that cannot be read holds everything.
/// </summary>
internal static class RestoredSettings
{
    /// <summary>The record's file, beside settings.json.</summary>
    public const string FileName = "restored-settings.json";

    [Flags]
    public enum Part
    {
        None = 0,

        /// <summary>The Club Website page's relay: RelayBaseUrl.</summary>
        PublishRelay = 1,

        /// <summary>Voice Settings' voice relay of its own: VoiceRelayBaseUrl.</summary>
        VoiceRelay = 2,

        /// <summary>The mail server PINs and invites go out through: SmtpHost.</summary>
        Mail = 4,

        All = PublishRelay | VoiceRelay | Mail,
    }

    internal sealed record Record(DateTime RestoredUtc, List<string> Held);

    private static readonly object Gate = new();

    /// <summary>The last answer read or written, and the file it was about - AppPaths can point elsewhere under test.</summary>
    private static (string Path, Part Held)? _cached;

    private static string FilePath => Path.Combine(AppPaths.Root, FileName);

    /// <summary>What is being held back now.</summary>
    public static Part Held
    {
        get
        {
            var path = FilePath;

            lock (Gate)
            {
                if (_cached is { } cached && cached.Path == path)
                {
                    return cached.Held;
                }

                if (!File.Exists(path))
                {
                    _cached = (path, Part.None);
                    return Part.None;
                }

                try
                {
                    var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path));
                    var held = Part.None;

                    foreach (var name in record?.Held ?? new List<string>())
                    {
                        if (Enum.TryParse<Part>(name, out var part))
                        {
                            held |= part & Part.All;
                        }
                    }

                    // A file that says nothing sensible is not "nothing held".
                    if (record?.Held is null)
                    {
                        held = Part.All;
                    }

                    _cached = (path, held);
                    return held;
                }
                catch (JsonException)
                {
                    // Damaged: everything held until each is confirmed, which
                    // writes the file afresh.
                    _cached = (path, Part.All);
                    return Part.All;
                }
                catch (Exception)
                {
                    // There but unreadable just now - a backup tool holding
                    // it. Held for this ask; asked again next time.
                    return Part.All;
                }
            }
        }
    }

    public static bool IsHeld(Part part) => (Held & part) != Part.None;

    /// <summary>
    /// Which of a backup's addresses this laptop was not already using.
    /// Compared as the app uses them - the relay addresses tidied the way
    /// RelayPublisher tidies them, the mail server without case - and a
    /// setting missing from a file counts as its default, as AppSettings
    /// would read it.
    /// </summary>
    /// <param name="liveJson">settings.json as it is on this laptop before the restore, or null when there is none (or it cannot be read).</param>
    /// <param name="restoredJson">settings.json as the backup has it.</param>
    public static Part WhatNeedsConfirming(string? liveJson, string restoredJson)
    {
        var live = Read(liveJson);
        var restored = Read(restoredJson) ?? throw new InvalidDataException(
            "The settings in that backup can't be read, so it can't be told where they would send anything. Nothing has been restored.");

        var parts = Part.None;

        if (NewAddress(RelayAddress(restored, nameof(AppSettings.RelayBaseUrl)), RelayAddress(live, nameof(AppSettings.RelayBaseUrl))))
        {
            parts |= Part.PublishRelay;
        }

        // The address "that last tested clean" is one too (Bug hunt 17):
        // Restore Last Working on the Club Website page copies it into the
        // live settings along with this laptop's own sealed token. One this
        // laptop was neither using nor remembering holds the relay, and the
        // page does not offer it while held.
        var restoredLastGood = RelayAddress(restored, nameof(AppSettings.RelayLastGoodBaseUrl));

        if (NewAddress(restoredLastGood, RelayAddress(live, nameof(AppSettings.RelayBaseUrl)))
            && NewAddress(restoredLastGood, RelayAddress(live, nameof(AppSettings.RelayLastGoodBaseUrl))))
        {
            parts |= Part.PublishRelay;
        }

        if (NewAddress(RelayAddress(restored, nameof(AppSettings.VoiceRelayBaseUrl)), RelayAddress(live, nameof(AppSettings.VoiceRelayBaseUrl))))
        {
            parts |= Part.VoiceRelay;
        }

        var restoredMail = MailHost(restored);

        if (NewAddress(restoredMail, MailHost(live))
            && !PinMailer.Presets.Any(p => string.Equals(p.Host, restoredMail, StringComparison.OrdinalIgnoreCase)))
        {
            parts |= Part.Mail;
        }

        return parts;
    }

    /// <summary>
    /// Adds to what is held - never takes away: a restore on top of one not
    /// yet confirmed keeps the first one's holds. Throws when the record
    /// cannot be written, so RestoreBackup can refuse the restore rather
    /// than put the addresses in place unguarded.
    /// </summary>
    public static void Hold(Part parts)
    {
        parts &= Part.All;

        if (parts == Part.None)
        {
            return;
        }

        lock (Gate)
        {
            var held = Held | parts;
            File.WriteAllText(FilePath, Serialise(held));
            _cached = (FilePath, held);
        }
    }

    /// <summary>
    /// The club has looked at this address and it is theirs. Kept for this
    /// run even if the record cannot be rewritten (then it asks again next
    /// time, which is the safe way to be wrong).
    /// </summary>
    public static void Confirm(Part part)
    {
        lock (Gate)
        {
            var before = Held;
            var held = before & ~part;

            if (held == before)
            {
                return;
            }

            var path = FilePath;
            _cached = (path, held);

            try
            {
                if (held == Part.None)
                {
                    File.Delete(path);
                }
                else
                {
                    SafeFile.WriteAllText(path, Serialise(held));
                }
            }
            catch
            {
                // See the summary: held again after a restart, no worse.
            }
        }
    }

    /// <summary>Why nothing is being sent to the relay, for the Club Website page.</summary>
    public const string PublishRelayMessage =
        "These relay settings came from a restored backup, so nothing is published or collected until you check them. "
        + "If the address above is your club's relay, press Save or Test Connection.";


    /// <summary>Why Restore Last Working is not offered while the relay is held, for the Club Website page.</summary>
    public const string LastGoodHeldMessage =
        "The settings that last tested clean came with a restored backup too, so they are not offered until these are checked.";

    /// <summary>Why the voice is not asking its relay, for Voice Settings.</summary>
    public const string VoiceRelayMessage =
        "The voice relay address came from a restored backup and is not used until you check it: "
        + "if it's yours, click in the address box and press Enter.";

    /// <summary>Why nothing is being emailed - the server named, so the check is against what will actually be used.</summary>
    public static string MailMessage(string host) =>
        $"\"{host.Trim()}\" came from a restored backup, so nothing is emailed until you check it: "
        + "if it's your club's mail server, press Send Test Email on the Club Website page.";

    /// <summary>One line for the log at startup, or null when nothing is held.</summary>
    public static string? StartupNote()
    {
        var held = Held;

        if (held == Part.None)
        {
            return null;
        }

        var what = new List<string>();

        if ((held & Part.PublishRelay) != 0)
        {
            what.Add("relay publishing (check it on the Club Website page)");
        }

        if ((held & Part.VoiceRelay) != 0)
        {
            what.Add("the voice relay (the Voice tab)");
        }

        if ((held & Part.Mail) != 0)
        {
            what.Add("PIN and invite emails (the mail settings on the Club Website page)");
        }

        return $"[restore] These settings came from a restored backup with addresses this laptop hadn't used. Paused until "
               + $"somebody checks them: {string.Join("; ", what)}.";
    }

    // ----------------------------------------------------------------- bits

    private static string Serialise(Part held) => JsonSerializer.Serialize(
        new Record(
            DateTime.UtcNow,
            Enum.GetValues<Part>()
                .Where(p => (p is Part.PublishRelay or Part.VoiceRelay or Part.Mail) && (held & p) != 0)
                .Select(p => p.ToString())
                .ToList()),
        new JsonSerializerOptions { WriteIndented = true });

    private static JsonElement? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement? settings, string name, string fallback) =>
        settings is { } s && s.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    // 17 Oct 2026, Bug Hunt 8, A23-4: an old relay host is moved onto the
    // current one first, as AppSettings.Load will move it once restored -
    // otherwise the club's own pre-rename backup reads as a new address.
    private static string RelayAddress(JsonElement? settings, string name) =>
        RelayPublisher.NormaliseBaseUrl(AppSettings.MovedOffLegacyRelay(Text(settings, name, string.Empty)));

    /// <summary>A missing SmtpHost is AppSettings' default, as a fresh laptop reads it.</summary>
    private static string MailHost(JsonElement? settings) =>
        Text(settings, nameof(AppSettings.SmtpHost), new AppSettings().SmtpHost).Trim();

    /// <summary>A non-blank address the laptop was not already using. Blank sends nothing anywhere.</summary>
    private static bool NewAddress(string restored, string live) =>
        restored.Length > 0 && !string.Equals(restored, live, StringComparison.OrdinalIgnoreCase);
}
