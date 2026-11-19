namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The small windows that set up a night (18 Nov 2026, dialog pass 4):
    /// a template's nights (TemplateDaysForm), the Breaks window
    /// (BreaksForm) and the class running order (ClassOrderForm) - each
    /// filled, and each again with nothing in it.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> NightSetupWindows()
    {
        static List<string> Classes(int count) =>
            Enumerable.Range(1, count)
                .Select(i => i % 3 == 0 ? $"Kyosho Mini-Z MR-03 Stock 70T Touring Cup {i}" : $"Class {i}")
                .ToList();

        return new (string, Func<Form>)[]
        {
            ("Template Nights", () => new TemplateDaysForm(new NightTemplate
            {
                Name = "Monthly points round for the Kyosho Mini-Z championship",
                Weekdays = new List<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Thursday },
                Dates = Enumerable.Range(1, 9).Select(i => DateTime.Today.AddDays(7 * i)).ToList(),
            })),
            ("Template Nights (none yet)", () => new TemplateDaysForm(new NightTemplate { Name = "Tuesday club night" })),
            ("Breaks", () => new BreaksForm(
                new List<PlannedBreak>
                {
                    new() { AfterRound = 2, Minutes = 15, What = "Watering the track" },
                    new() { AfterRound = PlannedBreak.BeforeFinals, Minutes = 45, What = "Dinner, the raffle and the juniors' trophies before the finals" },
                },
                rounds: 4,
                clubNight: false)),
            ("Breaks (club night, none yet)", () => new BreaksForm(null, rounds: 3, clubNight: true)),
            ("Class Running Order", () => new ClassOrderForm(Classes(9), rotate: true)),
            ("Class Running Order (two classes)", () => new ClassOrderForm(Classes(2), rotate: false)),
        };
    }
}
