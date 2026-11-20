namespace HeatCaller;

/// <summary>
/// Opens the driver's profile for editing from a page other than Drivers -
/// the leaderboard's Edit Driver and a double-click on a name on Who's
/// Racing (13 Nov 2026) - saving it with an undo that puts every field back.
/// </summary>
internal static class DriverEditDialog
{
    /// <summary>
    /// Edits the driver on <paramref name="transponder"/> (adds one when there
    /// is none on file). True when the form was saved.
    /// </summary>
    /// <param name="crewVoices">
    /// The voices a driver may pick for their crew (MainForm.CrewVoicesForDesk),
    /// for the profile's voice drop-downs (19 Nov 2026); null leaves them off.
    /// </param>
    public static bool Edit(IWin32Window owner, string transponder, RaceClassStore raceClasses, UndoService undo, ProfileStore profiles, IReadOnlyList<CrewVoiceOption>? crewVoices = null)
    {
        var existing = profiles.Get(transponder);

        // The driver as on file before the form opens - the form may edit
        // the record in place - so Undo can put every field back.
        var before = existing is null ? null : System.Text.Json.JsonSerializer.Serialize(existing);
        var openedOn = existing is null ? null : ProfileStore.Snapshot(existing);

        using var form = FormFor(existing, transponder, raceClasses, undo, profiles, crewVoices);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        var saved = ToSave(openedOn, form.Result, profiles.Get(transponder));
        var after = System.Text.Json.JsonSerializer.Serialize(saved);
        var key = saved.Transponder;

        if (before != after)
        {
            undo.Capture(before is null ? $"Add driver {saved.DriverName}" : $"Edit driver {saved.DriverName}", () =>
            {
                // Only while the driver is still as this edit left them -
                // otherwise a later edit would be thrown away with it.
                var current = profiles.Get(key);

                if (current is null || System.Text.Json.JsonSerializer.Serialize(current) != after)
                {
                    throw new InvalidOperationException(
                        $"{saved.DriverName} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
                }

                if (before is null)
                {
                    profiles.Delete(key);
                }
                else if (System.Text.Json.JsonSerializer.Deserialize<DriverProfile>(before) is { } restored)
                {
                    // The alt transponder the old details had may be
                    // somebody else's by now - one car, two owners.
                    if (profiles.DescribeClash(restored, editing: restored.Transponder) is { } clash)
                    {
                        throw new InvalidOperationException($"the old details can't come back - {clash} Nothing was changed.");
                    }

                    profiles.Upsert(restored);

                    // The form retires a replaced photo rather than
                    // deleting it when it has an undo, so it can come back.
                    if (!string.Equals(current.PhotoFileName, restored.PhotoFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        DriverPhotos.Unretire(restored.PhotoFileName);
                        DriverPhotos.Retire(current.PhotoFileName);
                    }
                }
            });
        }

        profiles.Upsert(saved);
        return true;
    }

    /// <summary>
    /// What the save stores: the form's profile, except that a field the
    /// desk left alone keeps what is on file now.
    ///
    /// WHY (14 Nov 2026, bug hunt 18): a phone's "racing tonight" pick is
    /// applied on the roster tick with no desk step, so it can land while
    /// this dialog is open - and saving it put the old car straight back.
    /// Driver Hub's edit has had the same guard since bug hunt 15.
    /// </summary>
    /// <summary>The profile dialog Edit shows, built but not shown - for the tests too.</summary>
    internal static ProfileForm FormFor(
        DriverProfile? existing, string transponder, RaceClassStore raceClasses, UndoService undo, ProfileStore profiles, IReadOnlyList<CrewVoiceOption>? crewVoices) =>
        new(existing, raceClasses, fixedTransponder: transponder, undo: undo, profiles: profiles, crewVoices: crewVoices);

    internal static DriverProfile ToSave(DriverProfile? openedOn, DriverProfile edited, DriverProfile? onFile) =>
        openedOn is null ? edited : ProfileStore.KeepChangesMadeSince(openedOn, edited, onFile);
}
