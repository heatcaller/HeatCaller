namespace HeatCaller;

/// <summary>
/// A window around <see cref="PaceAnalysisControl"/>, for opening the pace
/// table on one particular race straight after it finishes.
///
/// The table itself moved into a control when Race Analytics gained a Pace
/// tab. The two views differ by a combo box - which is not a reason to keep
/// two copies of a table with twelve columns and a paragraph of tooltips in
/// it - so this is now a frame and nothing else.
/// </summary>
public sealed class PaceAnalysisForm : Form
{
    public PaceAnalysisForm(SessionRecord session)
    {
        Text = $"Pace - {(string.IsNullOrWhiteSpace(session.Label) ? "Race" : session.Label)}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(880, 460);
        ClientSize = new Size(1120, 620);

        Controls.Add(new PaceAnalysisControl(session) { Dock = DockStyle.Fill });

        AppTheme.ApplyRecursively(this);
    }
}
