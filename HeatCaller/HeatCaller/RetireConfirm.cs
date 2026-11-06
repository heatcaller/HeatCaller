namespace HeatCaller;

/// <summary>
/// "Are you sure?" before a Retire button retires anything (5 Nov 2026,
/// Asher: "make retire button pop up are you sure?").
///
/// One question for every Retire in the app - battery packs, transponders,
/// cars and chassis - so they all ask it the same way. Only on the way out:
/// Bring Back and Put Back lose nothing and stay one click.
///
/// No is the default button, so a stray Enter or a double click lands on the
/// answer that changes nothing.
/// </summary>
internal static class RetireConfirm
{
    /// <param name="what">What is about to be retired, as the user knows it - "pack Blue 2", "transponder 1234567".</param>
    /// <returns>True when the user said Yes.</returns>
    public static bool Ask(IWin32Window? owner, string what) =>
        AppDialog.Show(
            owner,
            Question(what),
            "Retire",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>The question's words - apart from the dialog, so a test can read them.</summary>
    internal static string Question(string what) =>
        $"Are you sure you want to retire {what}?\n\n"
        + "It keeps its history and can be brought back later.";
}
