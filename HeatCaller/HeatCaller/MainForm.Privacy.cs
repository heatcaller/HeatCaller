using System.Text;

namespace HeatCaller;

/// <summary>
/// Keeping the club's online pages in step with what each driver asked for
/// online - no photo, no podium videos, initials only (22 Oct 2026, driver
/// experience, item 12; design 1.4 rules 3, 4 and 6).
/// </summary>
/// <remarks>
/// THE REGISTER. PublicPrivacy.Current is what every public document is
/// built against (RelayPublisher.DisplayNames, the check-in photos, the
/// podium videos). It is rebuilt here, on the roster tick, whenever a
/// profile's names or flags change - a desk edit in ProfileForm, or a
/// driver's own request approved at the desk - and swapped whole.
///
/// MAKING A CHANGE STICK. The live snapshots (roster, standings, schedule,
/// check-in list, achievements, tracks) put themselves right: their content
/// changes with the register, so SendToRelayIfChanged sends them. Saved
/// results do not - each went once, when it was raced - so for the drivers
/// whose flags changed, every saved session of theirs the relay holds is
/// sent again: 25 every tick, and only while the publisher's queue is under
/// 300 (it holds 500 and drops the oldest when full, and a driver with a
/// season of results must not push tonight's racing out of it). Then each
/// affected year's index, then tonight's nights, then - for a driver now
/// keeping their photo, videos or name off - their own podium videos are
/// taken down (driver/forget-media).
///
/// SURVIVES A RESTART. The drivers still owed that are kept in
/// AppSettings.PrivacyRepublishOwed, and only taken off once all of it has
/// gone. A laptop shut halfway starts their batch again on the next run -
/// re-sending a session the relay already has changes nothing.
///
/// UI thread only, like every other relay document builder.
/// </remarks>
public partial class MainForm
{
    /// <summary>Sessions sent again per roster tick while a privacy change is being made to stick.</summary>
    private const int PrivacySessionsPerTick = 25;

    /// <summary>Only top the publisher's queue up while it holds fewer than this (its ceiling is 500).</summary>
    private const int PrivacyQueueCeiling = 300;

    // No initializers in a part file (the build rule) - each starts at its
    // default, which is "nothing seen yet" / "no batch running".

    /// <summary>What the register was last built from (PrivacyKeyOf), so an unchanged roster costs one string compare.</summary>
    private string? _privacyKey;

    /// <summary>The drivers (by transponder) the batch in hand is for. Null when no batch is running.</summary>
    private HashSet<string>? _privacyBatch;

    /// <summary>Moves on with every new batch, so a forget-media answer for an abandoned one is not taken as this one's.</summary>
    private int _privacyBatchNumber;

    /// <summary>The batch's saved sessions still to send again, by id - looked up afresh when sent, so an edit made meanwhile goes as it now stands.</summary>
    private Queue<string>? _privacySessionsToSend;

    /// <summary>The years whose results index the batch sends once its sessions have gone.</summary>
    private IReadOnlyCollection<string>? _privacyYears;

    private bool _privacyIndexSent;
    private bool _privacyForgetDone;
    private bool _privacyForgetRunning;
    private DateTime _privacyForgetRetryUtc;

    /// <summary>The last error said in the console, so a fault that repeats every tick is said once.</summary>
    private string? _privacyLastError;

    /// <summary>
    /// The first register, from the profiles as loaded - at start-up, before
    /// anything is published. Not counted as a change: nothing has been
    /// published under any other, and work left over from the last run is
    /// already in PrivacyRepublishOwed - unless the last run never saw the
    /// change at all (see below).
    /// </summary>
    partial void WirePrivacy()
    {
        try
        {
            var profiles = _profiles.All;
            _privacyKey = PrivacyKeyOf(profiles);
            var register = PublicPrivacy.From(profiles, _settings.PrivacyRetained);
            PublicPrivacy.Current = register;

            // WHY (23 Oct 2026, bug hunt 10): a change is only noticed on the
            // two-second roster tick, so a privacy tick saved just before
            // HeatCaller closed, restarted or crashed was never owed - and
            // this start took the changed profiles as already published. The
            // register last made to stick is kept, and when the profiles say
            // otherwise every flagged driver is republished (who exactly
            // changed cannot be told from a fingerprint; a session sent again
            // changes nothing). A file from before this has none: recorded only.
            var applied = _settings.PrivacyAppliedFingerprint;

            if (!string.Equals(applied, register.FlaggedFingerprint, StringComparison.Ordinal))
            {
                // WHY (23 Oct 2026, bug hunt 10 review): the drivers flagged
                // last time too, so one whose flag went OFF just before
                // closing is republished as well - the profiles alone no
                // longer say they were flagged. And the night-before preview,
                // which RefreshPrivacyRegister would have redone. Only a
                // driver who still has a profile: a removed one's results are
                // not to be sent again under their full name.
                var owedNow = register.FlaggedTransponders
                    .Concat((_settings.PrivacyAppliedFlagged ?? new List<string>()).Where(t => _profiles.Get(t) is not null))
                    .ToList();

                if (!string.IsNullOrEmpty(applied) && owedNow.Count > 0)
                {
                    _settings.PrivacyRepublishOwed = MergePrivacyOwed(_settings.PrivacyRepublishOwed, owedNow);
                    OnClientRawMessage("[privacy] A driver's online privacy changed just before HeatCaller last closed - the club's page is being brought in line.");
                    RedactTheNightBeforeAgain();
                }

                _settings.PrivacyAppliedFingerprint = register.FlaggedFingerprint;
                _settings.PrivacyAppliedFlagged = register.FlaggedTransponders.ToList();
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            // The empty register stays, and the first tick tries again.
            _privacyKey = null;
            OnClientRawMessage($"[privacy] Could not read the drivers' online privacy choices: {ex.Message}");
        }
    }

    /// <summary>Every two seconds: the register brought up to date, then the next step of any republish owed.</summary>
    partial void RosterTickPrivacy()
    {
        if (_closingDown)
        {
            return;
        }

        try
        {
            RefreshPrivacyRegister();
            RepublishForPrivacy();
            _privacyLastError = null;
        }
        catch (Exception ex)
        {
            if (!string.Equals(_privacyLastError, ex.Message, StringComparison.Ordinal))
            {
                _privacyLastError = ex.Message;
                OnClientRawMessage($"[privacy] Could not bring the club's page in line with the drivers' privacy choices: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Rebuilds PublicPrivacy.Current when any driver's names or flags have
    /// changed, and notes who now needs their saved results sent again.
    /// </summary>
    private void RefreshPrivacyRegister()
    {
        var profiles = _profiles.All;
        var key = PrivacyKeyOf(profiles);

        if (string.Equals(key, _privacyKey, StringComparison.Ordinal))
        {
            return;
        }

        var before = PublicPrivacy.Current;

        // WHY (23 Oct 2026, bug hunt 10): a flagged driver whose profile has
        // just gone keeps their protection (PublicPrivacy.Retain). It was
        // dropped with the profile, and the batch below then republished
        // every saved result of theirs with the full name.
        var retained = PublicPrivacy.Retain(before, profiles, _settings.PrivacyRetained, out var retainedChanged);
        var after = PublicPrivacy.From(profiles, retained);

        // Swapped first, so even the next document built - before any
        // republishing - already honours it.
        PublicPrivacy.Current = after;
        _privacyKey = key;

        var settingsChanged = false;

        if (retainedChanged)
        {
            _settings.PrivacyRetained = retained;
            settingsChanged = true;
        }

        // Saved with the owed list below, so the two always agree - see WirePrivacy.
        if (!string.Equals(_settings.PrivacyAppliedFingerprint, after.FlaggedFingerprint, StringComparison.Ordinal))
        {
            _settings.PrivacyAppliedFingerprint = after.FlaggedFingerprint;
            _settings.PrivacyAppliedFlagged = after.FlaggedTransponders.ToList();
            settingsChanged = true;
        }

        var changed = PublicPrivacy.Changed(before, after);

        if (changed.Count == 0)
        {
            if (settingsChanged)
            {
                _settings.Save();
            }

            return;
        }

        // The night-before preview names drivers in prose and is sent once;
        // done again against the new register (see there).
        RedactTheNightBeforeAgain();

        _settings.PrivacyRepublishOwed = MergePrivacyOwed(_settings.PrivacyRepublishOwed, changed);

        if (!_settings.Save())
        {
            OnClientRawMessage("[privacy] Could not save which drivers' results still need updating online; if HeatCaller closes first, edit their profile again.");
        }

        // Whatever batch was running was built for the old register. Start
        // over: everybody it had not finished is still owed, and a session
        // sent twice changes nothing.
        _privacyBatch = null;

        OnClientRawMessage("[privacy] A driver's online privacy changed - the club's page is being brought in line.");
    }

    /// <summary>One step of the republish owed, if any is - see the class note for the order.</summary>
    private void RepublishForPrivacy()
    {
        var owed = _settings.PrivacyRepublishOwed;

        if (owed is null || owed.Count == 0)
        {
            _privacyBatch = null;
            return;
        }

        // Kept owed, not dropped, while the relay is off or not set up: a
        // send would go nowhere (the publisher drops it silently), and the
        // work is still wanted the day it comes back.
        if (!_settings.RelayEnabled || !_relayPublisher.Status.Configured)
        {
            return;
        }

        if (_privacyBatch is null)
        {
            StartPrivacyBatch(owed);
        }

        // 1. The saved sessions, a few at a time.
        if (_privacySessionsToSend is { Count: > 0 } queue)
        {
            if (_relayPublisher.Status.Queued >= PrivacyQueueCeiling)
            {
                return;
            }

            var saved = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);

            foreach (var session in _sessionHistory.Saved)
            {
                saved.TryAdd(session.Id, session);
            }

            for (var sent = 0; sent < PrivacySessionsPerTick && queue.Count > 0; sent++)
            {
                // A session deleted since the batch began is simply skipped.
                if (saved.TryGetValue(queue.Dequeue(), out var again))
                {
                    PublishSessionToRelay(again);
                }
            }

            return;
        }

        // 2. Each year's index, once the sessions in it have gone, and
        // tonight's nights (a night names other drivers in its coach card).
        if (!_privacyIndexSent)
        {
            if (_relayPublisher.Status.Queued >= PrivacyQueueCeiling)
            {
                return;
            }

            foreach (var year in _privacyYears ?? Array.Empty<string>())
            {
                PublishResultsIndexForYear(year);
            }

            ResendDriverNights();
            _privacyIndexSent = true;
        }

        // 3. Their own podium videos taken down.
        if (!_privacyForgetDone)
        {
            if (_privacyForgetRunning || DateTime.UtcNow < _privacyForgetRetryUtc)
            {
                return;
            }

            var badges = PrivacyBadgesToForget(_privacyBatch!, PublicPrivacy.Current, _settings.RelayCheckInSecret);

            if (badges.Count == 0)
            {
                _privacyForgetDone = true;
            }
            else
            {
                _privacyForgetRunning = true;
                _ = ForgetDriverMediaInBackgroundAsync(badges, _privacyBatchNumber);
                return;
            }
        }

        // 4. Done: these drivers are owed nothing more.
        var finished = _privacyBatch!;
        _settings.PrivacyRepublishOwed = owed.Where(t => !finished.Contains(t)).ToList();
        _settings.Save();
        _privacyBatch = null;

        OnClientRawMessage("[privacy] The club's page now shows the drivers' latest privacy choices.");
    }

    /// <summary>Takes everybody owed now as one batch: their saved sessions on the relay, oldest first, and those sessions' years.</summary>
    private void StartPrivacyBatch(IEnumerable<string> owed)
    {
        _privacyBatch = new HashSet<string>(owed, StringComparer.OrdinalIgnoreCase);
        _privacyBatchNumber++;

        var sessions = PrivacySessionsToRepublish(
            _sessionHistory.Saved,
            _privacyBatch,
            s => _relayPublisher.ResultIsOnRelay(s.Id, RelayResultsIndex.EffectiveStart(s)));

        _privacySessionsToSend = new Queue<string>(sessions.Select(s => s.Id));
        _privacyYears = RelayResultsIndex.YearsOf(sessions);
        _privacyIndexSent = false;
        _privacyForgetDone = false;
        _privacyForgetRetryUtc = DateTime.MinValue;
    }

    /// <summary>
    /// The forget-media request, off the UI thread; its answer comes back to
    /// it. Taken as done when the relay took it or is too old to (said once
    /// in the log - the videos then expire within seven days); asked again a
    /// minute later otherwise.
    /// </summary>
    private async Task ForgetDriverMediaInBackgroundAsync(IReadOnlyList<string> badges, int batchNumber)
    {
        var result = new ForgetMediaResult(ForgetMediaOutcome.Failed, 0);

        try
        {
            result = await _relayPublisher.ForgetDriverMediaAsync(badges).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fire and forget, so nothing else would ever see this.
            OnClientRawMessage($"[privacy] Could not ask the relay to take podium videos down: {ex.Message}");
        }

        var answer = result;

        TryPost(() =>
        {
            _privacyForgetRunning = false;

            if (batchNumber != _privacyBatchNumber)
            {
                // A newer batch has started; it asks again for itself.
                return;
            }

            if (answer.Outcome is ForgetMediaOutcome.Done or ForgetMediaOutcome.RelayTooOld)
            {
                _privacyForgetDone = true;

                if (answer.Forgotten > 0)
                {
                    OnClientRawMessage($"[privacy] {Plural.Of(answer.Forgotten, "podium video")} taken off the club's page.");
                }
            }
            else
            {
                _privacyForgetRetryUtc = DateTime.UtcNow + TimeSpan.FromMinutes(1);
            }
        });
    }

    // ----------------------------------------------------------- pure parts

    /// <summary>
    /// Everything PublicPrivacy.From reads from the profiles, as one string:
    /// each driver's transponders, names and three flags. Equal strings
    /// mean an equal register, so the register (and its patterns) is only
    /// rebuilt when something in it could differ.
    /// </summary>
    internal static string PrivacyKeyOf(IEnumerable<DriverProfile> profiles)
    {
        var key = new StringBuilder();

        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                continue;
            }

            key.Append(string.Join(",", profile.AllTransponderIds)).Append('|')
                .Append(profile.DriverName).Append('|')
                .Append(profile.AnnouncerName).Append('|')
                .Append(profile.NoPhotoOnline ? '1' : '0')
                .Append(profile.NoVideoOnline ? '1' : '0')
                .Append(profile.InitialsOnline ? '1' : '0')
                .Append('\n');
        }

        return key.ToString();
    }

    /// <summary>The owed list with these transponders added, each once (any case), in the order first owed.</summary>
    internal static List<string> MergePrivacyOwed(IEnumerable<string>? owed, IEnumerable<string> changed)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();

        foreach (var transponder in (owed ?? Enumerable.Empty<string>()).Concat(changed))
        {
            var id = (transponder ?? string.Empty).Trim();

            if (id.Length > 0 && seen.Add(id))
            {
                merged.Add(id);
            }
        }

        return merged;
    }

    /// <summary>
    /// The saved sessions to send again for these drivers: not demos, with
    /// one of them in the results, and already on the relay - one it was
    /// never given (raced while publishing was off, or before the club's
    /// "from" date) is not this change's to publish. Oldest first, so they
    /// land in the order they were raced.
    /// </summary>
    internal static IReadOnlyList<SessionRecord> PrivacySessionsToRepublish(
        IEnumerable<SessionRecord> saved,
        IReadOnlySet<string> transponders,
        Func<SessionRecord, bool> onRelay)
    {
        return saved
            .Where(s => s is not null && !s.IsDemoSession())
            .Where(s => s.Results.Any(r => transponders.Contains((r.Transponder ?? string.Empty).Trim())))
            .Where(onRelay)
            .OrderBy(RelayResultsIndex.EffectiveStart)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The badge ids whose podium videos should come down: drivers in the
    /// batch who now ask for no photo or no videos online - and, a step
    /// past the design's rule 4/5, initials only too, since their name is
    /// drawn into their own videos' pixels and on the videos page. A driver
    /// whose flags went OFF has nothing to take down. Empty without a badge
    /// key: no video was ever filed under a badge then.
    /// </summary>
    internal static IReadOnlyList<string> PrivacyBadgesToForget(IEnumerable<string> transponders, PublicPrivacy register, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Array.Empty<string>();
        }

        return transponders
            .Where(t => !string.IsNullOrWhiteSpace(t)
                        && (register.NoPhoto(t) || register.NoVideo(t) || register.InitialsForTransponder(t)))
            .Select(t => RelayCheckIn.IdFor(t, secret))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
