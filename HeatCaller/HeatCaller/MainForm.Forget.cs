namespace HeatCaller;

/// <summary>
/// Forget this driver and Export this driver's data, wired to the stores
/// this form holds (15 Nov 2026, children's-privacy plan 2.5). The work is
/// in DriverForget and DriverDataExport; the page's menu items and their
/// questions are in ProfileManagerControl.Forget.
///
/// THE RELAY. A forgotten driver's badge codes and names go into
/// AppSettings.RelayForgetsOwed at once, and are sent to the relay's
/// driver/forget straight away and then on the roster tick until it says
/// done - so a laptop offline at the time, or closed straight after, still
/// gets them off the relay the next time it can.
/// </summary>
public partial class MainForm
{
    /// <summary>When the owed relay forgets may next be tried - not more than once a minute while it keeps failing.</summary>
    private long _nextRelayForgetTick;

    /// <summary>True while a send of the owed forgets is under way.</summary>
    private bool _relayForgetSending;

    /// <summary>Every store that holds something about a driver, as this form holds them.</summary>
    internal DriverDataSources DriverDataSourcesNow() => new()
    {
        Profiles = _profiles,
        History = _sessionHistory,
        CheckIns = _checkInStore,
        Events = _raceEvents,
        Rentals = _rentals,
        Handicaps = _handicaps,
        TechInspections = _techInspections,
        SetupLog = _setupLog,
        Requests = _driverRequests,
        Payments = _payments,
        PreEntries = _preEntries,
        LapBeeps = _lapBeeps,
        Achievements = _raceControlControl?.Achievements,
        PersonalBestResets = _pbResets,
        Rivalries = _rivalryStore,
        Seasons = _seasonStore,
        Rounds = _rounds,
        Cars = _cars,
        Batteries = _batteryPacks,
        Signups = _signupDesk?.Store,
        Diary = PrincipalDiary.Load,
        Settings = _settings,
    };

    /// <summary>
    /// Forgets the driver here, owes the relay the same, and starts that
    /// going. Returns what went, for the page to say.
    /// </summary>
    internal DriverForget.Outcome ForgetDriverEverywhere(DriverProfile profile)
    {
        // Their name as the club's pages show it - initials, or shortened -
        // read before the profile goes, since that is what a follower's
        // phone followed them by.
        var shown = PublicPrivacy.Current.PublicName(profile.DriverName, profile.Transponder);

        var outcome = DriverForget.Forget(profile, DriverDataSourcesNow());

        // Nothing can bring them back now - and an older Undo (an edit of
        // their profile, or a Delete before this) would put their details
        // straight back.
        _undo.Clear();

        // Their published results are sent again, now under the
        // placeholder - the same batch a privacy change uses.
        _settings.PrivacyRepublishOwed = MergePrivacyOwed(_settings.PrivacyRepublishOwed, profile.AllTransponderIds.ToList());
        _privacyBatch = null;

        if (_relayPublisher.Status.Configured)
        {
            var names = outcome.Names.Append(shown).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _settings.RelayForgetsOwed.Add(new RelayForgetOwed { Badges = outcome.Badges.ToList(), Names = names });
        }

        _settings.Save();
        _nextRelayForgetTick = 0;
        SendOwedRelayForgets();

        OnClientRawMessage($"[privacy] A driver was forgotten; their results now read \"{outcome.Placeholder}\".");
        return outcome;
    }

    /// <summary>The export page for a driver.</summary>
    internal string ExportDriverData(DriverProfile profile) =>
        DriverDataExport.Html(profile, DriverDataSourcesNow(), string.Empty, DateTime.Now);

    /// <summary>On the roster tick: any forgets the relay is still owed.</summary>
    partial void RosterTickForget() => SendOwedRelayForgets();

    private async void SendOwedRelayForgets()
    {
        if (_relayForgetSending || _closingDown || _settings.RelayForgetsOwed.Count == 0
            || Environment.TickCount64 < _nextRelayForgetTick || !_relayPublisher.Status.Configured)
        {
            return;
        }

        _relayForgetSending = true;

        try
        {
            foreach (var owed in _settings.RelayForgetsOwed.ToList())
            {
                var answer = await _relayPublisher.ForgetDriverAsync(owed.Badges, owed.Names);

                if (!answer.Ok)
                {
                    _nextRelayForgetTick = Environment.TickCount64 + 60_000;
                    OnClientRawMessage($"[privacy] {answer.Message}");
                    return;
                }

                _settings.RelayForgetsOwed.Remove(owed);
                _settings.Save();
                OnClientRawMessage("[privacy] A forgotten driver was removed from the club website.");
            }
        }
        catch (Exception ex)
        {
            _nextRelayForgetTick = Environment.TickCount64 + 60_000;
            OnClientRawMessage($"[privacy] Couldn't remove a forgotten driver from the club website yet: {ex.Message}");
        }
        finally
        {
            _relayForgetSending = false;
        }
    }
}
