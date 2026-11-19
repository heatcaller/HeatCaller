namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two windows the catalog had no builder for (18 Nov 2026, dialog
    /// pass): a driver's own changes from their phone approved at the desk
    /// (ApproveDriverRequestForm, titled "Approve - name's Changes"), every
    /// field a phone can ask for, sent by a parent, with a voice to hear the
    /// new announcer name in - and without one; and a transponder given to a
    /// driver on file (BelongsToForm), as the unknown-transponder list opens
    /// it and as a transfer from one driver to another, over the catalog's
    /// forty drivers.
    ///
    /// The approve window is named under "Approve", which is on
    /// LayoutAudit.ConvertedWindows, so it is held to it as the sign-up's
    /// Approve is. "Who is on transponder" is not on it, so its findings are
    /// reported but do not fail the test.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ApprovalAndTransponderWindows() => new (string, Func<Form>)[]
    {
        ("Approve (a driver's own changes, sent by a parent)", () => DriverChangesWindow(withVoice: true)),
        ("Approve (a driver's own changes, no voice to hear them in)", () => DriverChangesWindow(withVoice: false)),
        ("Who is on transponder", () => new BelongsToForm(
            "B70E21",
            "14 laps in Qualifier 2 Heat 3 · best 11.482",
            GarageDriversOnFile(),
            CarStore.Load())),
        ("Who is on transponder (a transfer)", () => new BelongsToForm(
            "8467BE",
            "Give transponder 8467BE to another driver for good - sold, given or swapped. It's Driver 5's now. "
            + "Laps timed on it from then on count for the new owner. Races already run where it was timed as a car of its own "
            + "are saved under 8467BE itself, so qualifying and the mains will count those for the new owner too.",
            GarageDriversOnFile(),
            CarStore.Load(),
            fromDriver: Drivers(40)[4].Transponder)),
    };

    /// <summary>A details request asking for every change a phone can send, from the driver with the longest name.</summary>
    private static ApproveDriverRequestForm DriverChangesWindow(bool withVoice)
    {
        var profiles = GarageDriversOnFile();
        var profile = profiles.Get(Drivers(40)[11].Transponder)!;

        var request = new DriverRequest
        {
            Id = "0a0b0c" + new string('0', 18),
            Badge = new string('b', 20),
            Kind = DriverAsk.Details,
            Proof = DriverAsk.ByPin,
            AskedUtc = new DateTime(2026, 9, 26, 18, 5, 0, DateTimeKind.Utc),
        };

        request.Texts["announcer"] = "Alexandria the Great";
        request.Texts["pronouns"] = "she/her";
        request.Texts["about"] = "Racing since I was eight. Ask me about my Mini-Z.";
        request.Texts["email"] = "alexandria.montgomery-fitzwilliam@example.com";
        request.Flags["initials"] = true;
        request.Flags["noPhoto"] = true;
        request.Flags["guardian"] = true;

        var desk = new DriverRequestDesk(
            DriverRequestStore.Load(),
            new AppSettings(),
            profiles,
            null,
            SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
            new UndoService(),
            _ => { },
            _ => null);

        return new ApproveDriverRequestForm(desk, request, profile, profiles, withVoice ? _ => { } : null, null);
    }
}
