namespace HeatCaller;

/// <summary>
/// Every name the app used before it was called HeatCaller, in one place.
///
/// Nothing here is shown to anyone. These are only the names older copies
/// wrote to disk or keyed data by, so this version can find that data and
/// bring it across (see AppDataMigration). Do not use them for anything
/// new, and do not change them: each one has to match what is already on
/// somebody's laptop.
/// </summary>
internal static class LegacyNames
{
    /// <summary>The data folder under %AppData% before the move to %AppData%\HeatCaller.</summary>
    public const string DataFolder = "MiniZRaceDirector";

    /// <summary>The saved-results folder under Documents.</summary>
    public const string ResultsFolder = "MiniZRaceDirector Results";

    /// <summary>The default backup folder under Documents.</summary>
    public const string BackupsFolder = "MiniZRaceDirector Backups";

    /// <summary>What backup zip names started with, paired with what they start with now.</summary>
    public static readonly IReadOnlyList<(string Old, string New)> BackupPrefixes = new[]
    {
        ("MiniZRaceDirector-AutoBackup-", "HeatCaller-AutoBackup-"),
        ("MiniZRaceDirector-Backup-", "HeatCaller-Backup-"),
    };

    /// <summary>
    /// The single-instance mutex older copies held. Checked at start-up so
    /// the data folder is never moved out from under an older copy that is
    /// still running.
    /// </summary>
    public const string InstanceMutex = @"Local\HorizonGrid.MiniZRaceDirector";

    /// <summary>The DPAPI entropy values sealed as "dpapi1:" were sealed with. See SecretBox.</summary>
    public const string SecretEntropy = "HorizonGrid.MiniZRaceDirector.v1";

    /// <summary>Hosted relay addresses from earlier names. AppSettings moves a saved address off these.</summary>
    public static readonly IReadOnlyList<string> RelayHosts = new[]
    {
        "horizon-grid-relay.fly.dev",
        "gridcaller-relay.fly.dev",
    };

    /// <summary>
    /// Saved voice-settings fields that were renamed, old to new. Their
    /// values are carried across once by AppDataMigration.
    /// </summary>
    public static readonly IReadOnlyList<(string Old, string New)> VoiceProfileFields = new[]
    {
        ("GridCallerVoiceName", "HeatCallerVoiceName"),
        ("GridCallerVoice", "HeatCallerVoice"),
        ("GridCallerRev", "HeatCallerRev"),
    };

    /// <summary>
    /// Part of every saved voice line's key (see VoiceLibrary). Frozen: a
    /// different value would stop every line a club has already paid to
    /// render from matching, and they would all be rendered - and paid
    /// for - again.
    /// </summary>
    public const string VoiceKeyVersion = "gridcaller-voice-1";

    /// <summary>The engine name inside a relay voice line's key. Frozen for the same reason as VoiceKeyVersion.</summary>
    public const string VoiceKeyEngine = "gridcaller";
}
