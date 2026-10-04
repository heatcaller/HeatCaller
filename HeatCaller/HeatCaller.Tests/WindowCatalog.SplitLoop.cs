namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Line Up (SplitLineUpForm), from the Connection page's split loop row:
    /// already lined up, which shows the offset in force, and never lined up.
    /// Both open empty - the crossings come from the decoders - which is the
    /// state its Save button was found below the fold in (4 Oct).
    ///
    /// On LayoutAudit.ConvertedWindows since that day, but with no builder
    /// here it was never measured; Every_guarded_window_is_built_by_the_sweep
    /// now fails for a window like that.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> SplitLoopWindows()
    {
        static Form LineUp(int offsetMs) => new SplitLineUpForm(
            new SplitLoop(_ => null),
            ProfileStore.Load(),
            offsetMs,
            _ => { },
            _ => { });

        return new (string, Func<Form>)[]
        {
            ("Line Up the Split Loop", () => LineUp(225)),
            ("Line Up the Split Loop (never lined up)", () => LineUp(0)),
        };
    }
}
