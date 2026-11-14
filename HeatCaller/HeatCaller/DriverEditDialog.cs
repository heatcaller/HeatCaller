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
    public static bool Edit(IWin32Window owner, string transponder, RaceClassStore raceClasses, UndoService undo, ProfileStore profiles)
    {
        var existing = profiles.Get(transponder);

        // The driver as on file before the form opens - the form may edit
        // the record in place - so Undo can put every field back.
        var before = existing is null ? null : System.Text.Json.JsonSerializer.Serialize(existing);

        using var form = new ProfileForm(existing, raceClasses, fixedTransponder: transponder, undo: undo, profiles: profiles);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return false;
        }

        var saved = form.Result;
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
}
