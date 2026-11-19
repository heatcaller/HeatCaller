namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Three small desk windows the catalog had no builder for (18 Nov 2026,
    /// dialog pass): the Desk Inbox's quick replies (QuickRepliesForm), with
    /// the club's own and with none; a driver's phone request declined
    /// (DriverRequestReasonForm), for the longest name; and the next night
    /// called off (CallOffForm), with email and a push able to go, and with
    /// email that can't and no club website. None is on
    /// LayoutAudit.ConvertedWindows, so their findings are in the report
    /// without failing the sweep.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> DeskPromptWindows() => new (string, Func<Form>)[]
    {
        ("Quick replies", () => new QuickRepliesForm(new[]
        {
            "Not tonight, sorry - the class is full.",
            "Ask at the desk when you get here.",
            "We can't change your transponder from a phone. Bring it to the desk.",
        })),
        ("Quick replies (none yet)", () => new QuickRepliesForm(Array.Empty<string>())),
        ("Decline Request", () => new DriverRequestReasonForm(Drivers(12)[11].DriverName)),
        ("Call It Off", () => new CallOffForm(
            new AnnouncedNight { Day = DateTime.Today.AddDays(3), Title = "Club Night" },
            regulars: 23,
            mailProblem: null,
            canPush: true)),
        ("Call It Off (no email, no website)", () => new CallOffForm(
            new AnnouncedNight { Day = DateTime.Today.AddDays(3), Title = "Club Night", OffText = "Track's flooded - see you next week." },
            regulars: 23,
            mailProblem: "the club's email isn't set up yet - add it on the Club page under Email.",
            canPush: false)),
    };
}
