using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's side of the transponders that crossed the line with no
/// driver (UnknownTransponders): the sidebar count, and the three answers
/// the banner and the board's right-click menu offer - give it to a driver
/// already on file, make a new driver, or register it as a club rental.
/// Each takes one Undo step.
/// </summary>
public partial class MainForm
{
    /// <summary>The list behind the banners on Race Control and Driver Hub.</summary>
    private readonly UnknownTransponders _unknownTransponders = new();

    /// <summary>The sidebar entry the count goes on.</summary>
    private const string DriversNavItem = "Driver Hub";

    /// <summary>Connects the list to the sidebar count and to the banners' buttons. Once, after the pages are built.</summary>
    private void WireUnknownTransponders()
    {
        _unknownTransponders.Changed += () =>
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            void Show() => lstNav.SetBadge(DriversNavItem, _unknownTransponders.Pending.Count);

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(Show);
                }
                catch (InvalidOperationException)
                {
                    // Closing.
                }
            }
            else
            {
                Show();
            }
        };

        _unknownTransponders.Requested += (transponder, choice) =>
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnUnknownTransponderChoice(transponder, choice));
                return;
            }

            OnUnknownTransponderChoice(transponder, choice);
        };
    }

    /// <summary>
    /// Takes off the list any transponder that has a driver now, however it
    /// got one - by hand on Driver Hub, a phone sign-up, an import.
    /// From the two-second roster tick.
    /// </summary>
    private void DropNamedUnknownTransponders() =>
        _unknownTransponders.DropKnown(transponder => _profiles.Get(transponder) is not null);

    private void OnUnknownTransponderChoice(string transponder, UnknownTransponders.Choice choice)
    {
        // Named some other way since the banner was drawn.
        if (_profiles.Get(transponder) is { } already)
        {
            _unknownTransponders.Resolve(transponder);
            AppDialog.Show(this,
                $"Transponder {transponder} already belongs to {NameOf(already)}.",
                "Already has a driver", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Bug hunt 11 (2026-11-05): a club rental unit is never one
        // driver's - see UnknownTransponders.RentalRefusal.
        if (UnknownTransponders.RentalRefusal(_rentals.Get(transponder), choice) is { } refusal)
        {
            AppDialog.Show(this, refusal, "Club rental unit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        switch (choice)
        {
            case UnknownTransponders.Choice.BelongsTo:
                GiveTransponderToDriver(transponder);
                break;
            case UnknownTransponders.Choice.NewDriver:
                AddDriverOnTransponder(transponder);
                break;
            case UnknownTransponders.Choice.ClubRental:
                AddClubRental(transponder);
                break;
        }
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

    /// <summary>What the banner says it did, for the dialogs - or empty when it is not on the list.</summary>
    private string WhatItDid(string transponder) =>
        _unknownTransponders.Pending
            .Where(s => string.Equals(s.Id, transponder, StringComparison.OrdinalIgnoreCase))
            .Select(UnknownTransponders.Describe)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>Puts a transponder back on the list - the undo of naming it.</summary>
    private void PutBackOnUnknownList(string transponder, string where) =>
        _unknownTransponders.Note(transponder, where);

    /// <summary>Where the list says a transponder was seen, for putting it back.</summary>
    private string WhereSeen(string transponder) =>
        _unknownTransponders.Pending
            .FirstOrDefault(s => string.Equals(s.Id, transponder, StringComparison.OrdinalIgnoreCase))?.Where ?? string.Empty;

    /// <summary>Everything that shows a driver's name, once one has changed.</summary>
    private void AfterTransponderNamed()
    {
        _raceControlControl?.RefreshNames();

        if (_navRefreshHooks.TryGetValue(DriversNavItem, out var drivers))
        {
            drivers();
        }
    }

    /// <summary>
    /// Belongs To: the transponder joins a driver already on file - fitted
    /// to one of their cars or moving between them, and replacing (retiring)
    /// one of theirs if it is a replacement. Their laps on it are theirs from
    /// then on, the session just run included.
    /// </summary>
    private void GiveTransponderToDriver(string transponder)
    {
        using var form = new BelongsToForm(transponder, WhatItDid(transponder), _profiles, _cars);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_profiles.Get(form.DriverKey) is not { } current)
        {
            AppDialog.Show(this, "That driver has been deleted since the list was shown. Nothing was changed.",
                "Driver not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var before = ProfileStore.Snapshot(current);
        var updated = ProfileStore.Snapshot(current);
        var now = DateTime.UtcNow;

        updated.Transponders.Add(new OwnedTransponder { Id = transponder, CarId = form.CarId, AddedUtc = now });

        if (form.Replaces.Length > 0)
        {
            var replaced = updated.EntryToChange(form.Replaces);
            replaced.Retired = true;
            replaced.RetiredUtc = now;
        }

        // Somebody else may have it by now (a phone sign-up meanwhile).
        if (_profiles.DescribeClash(updated, editing: updated.Transponder) is { } clash)
        {
            AppDialog.Show(this, clash, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var key = updated.Transponder;
        var name = NameOf(updated);
        var after = JsonSerializer.Serialize(updated);
        var where = WhereSeen(transponder);
        var profiles = _profiles;

        _undo.Capture($"Give transponder {transponder} to {name}", () =>
        {
            // Only while the driver is still as this left them - otherwise a
            // later edit would be thrown away with it.
            var nowOnFile = profiles.Get(key);

            if (nowOnFile is null || JsonSerializer.Serialize(nowOnFile) != after)
            {
                throw new InvalidOperationException(
                    $"{name} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
            }

            profiles.PutBack(new[] { before }, Array.Empty<string>());
            PutBackOnUnknownList(transponder, where);
            AfterTransponderNamed();
        });

        _profiles.Upsert(updated);
        _unknownTransponders.Resolve(transponder);

        var retiredNote = form.Replaces.Length > 0 ? $" {form.Replaces} is retired." : string.Empty;
        OnClientRawMessage($"{AppTime.LogStampNow()}  [drivers] Transponder {transponder} is {name}'s now - their laps on it count as theirs.{retiredNote}");
        AfterTransponderNamed();
    }

    /// <summary>New Driver: the profile form, on this transponder.</summary>
    private void AddDriverOnTransponder(string transponder)
    {
        // speakName: so Hear It is there when a driver is named from Race
        // Control too. This is where the names that catch a club out get
        // typed - a transponder nobody recognised, at seven o'clock, by
        // whoever is on the laptop - so it is the one place a respelling is
        // most likely to be guessed at and most worth hearing first.
        using var form = new ProfileForm(
            null,
            _raceClasses,
            fixedTransponder: transponder,
            undo: _undo,
            profiles: _profiles,
            speakName: text => _announcer.SpeakTest(_announcer.NameAsTheRoomHearsIt(text), _voiceStore.Current.Speed),
            crewVoices: CrewVoicesForDesk());

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var added = form.Result;

        // A NEW driver, so TryAdd rather than Upsert: Upsert would replace
        // anybody stored on this transponder meanwhile.
        if (!_profiles.TryAdd(added, out var refusal))
        {
            AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var where = WhereSeen(transponder);
        var profiles = _profiles;
        var name = NameOf(added);

        _undo.Capture($"Add driver {name}", () =>
        {
            if (profiles.PutBack(Array.Empty<DriverProfile>(), new[] { added }) == 0)
            {
                throw new InvalidOperationException($"{name} has been deleted since. Nothing was changed.");
            }

            PutBackOnUnknownList(transponder, where);
            AfterTransponderNamed();
        });

        _unknownTransponders.Resolve(transponder);
        AfterTransponderNamed();
    }

    /// <summary>
    /// Club Rental: registers the transponder as one of the club's loan
    /// units (asked for its asset tag) and gives it a rental profile, so its
    /// laps have a name and stay out of the club's records - as a rental
    /// added by hand on the Rentals page and given a profile does.
    /// </summary>
    private void AddClubRental(string transponder)
    {
        using var ask = new RentalPromptForm(
            "Club Rental",
            $"Transponder {transponder} is one of the club's loan units.\n\nWhat is it called at the counter? An asset tag such as R4 - or leave it blank.",
            string.Empty);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var tag = ask.Answer;
        var unit = new RentalTransponder { Transponder = transponder, AssetTag = tag };
        var registered = _rentals.Get(transponder) is null;

        var profile = new DriverProfile
        {
            Transponder = transponder,
            DriverName = $"Rental {(tag.Length > 0 ? tag : transponder)}",
            IsRental = true,
        };

        if (!_profiles.TryAdd(profile, out var refusal))
        {
            AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (registered)
        {
            _rentals.Upsert(unit);
        }

        var where = WhereSeen(transponder);
        var profiles = _profiles;
        var rentals = _rentals;

        _undo.Capture($"Make {transponder} a club rental", () =>
        {
            if (profiles.PutBack(Array.Empty<DriverProfile>(), new[] { profile }) == 0)
            {
                throw new InvalidOperationException($"The rental profile for {transponder} has been deleted since. Nothing was changed.");
            }

            // A unit this made, and only while nobody has it out.
            if (registered && rentals.Get(transponder) is { IsOut: false })
            {
                rentals.Delete(transponder);
            }

            PutBackOnUnknownList(transponder, where);
            AfterTransponderNamed();

            if (_navRefreshHooks.TryGetValue("Rentals", out var refreshRentals))
            {
                refreshRentals();
            }
        });

        _unknownTransponders.Resolve(transponder);
        OnClientRawMessage($"{AppTime.LogStampNow()}  [drivers] Transponder {transponder} is a club rental now, named \"{profile.DriverName}\".");
        AfterTransponderNamed();

        if (_navRefreshHooks.TryGetValue("Rentals", out var refresh))
        {
            refresh();
        }
    }
}
