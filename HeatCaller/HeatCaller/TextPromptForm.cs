namespace HeatCaller;

/// <summary>
/// A line of text asked for over another window - the check the Tech
/// Checklist's Add... and Rename... ask for, a chassis sheet's field, a
/// setup sheet's name, a season's name (New Season, Rename Season), and a
/// new soundscape's name and description. One window for all of them, a
/// class of its own and internal so the layout test can build it
/// (WindowCatalog). The answer is as typed; the caller trims it if it
/// wants it trimmed.
///
/// On the layout pieces (DialogFit): the question, the box under it
/// across the window, and OK and Cancel at the foot; as tall as that.
/// It used to be built in pixels in a 400x130 window, the box a fixed
/// 372px and the buttons at fixed places and widths under it.
/// </summary>
internal sealed class TextPromptForm : Form
{
    private readonly TextBox _txt = new();

    public TextPromptForm(string title, string prompt, string defaultValue)
    {
        Text = title;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lbl = new Label { Text = prompt, AutoSize = true };
        _txt.Text = defaultValue;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(lbl, gapBelow: UiLayout.HintGap);
        page.Add(_txt, gapBelow: UiLayout.SectionGap);

        // A pixel either side of the box for the ring the theme draws round
        // it, outside it, on what holds it (Pieces.RingRoom): stretched
        // across the page, the box's sides would be the page's edges, and
        // the ring's sides would be drawn past them, where nothing shows.
        // The gap under the box stays as it was.
        _txt.Margin = new Padding(1, 0, 1, UiLayout.SectionGap);

        // Its height is the box's own, and changes when the theme takes its
        // border off - see Pieces.KeepHeightTold.
        Pieces.KeepHeightTold(_txt);

        // Against the right-hand edge, Cancel furthest right, as they
        // were - and Tab still reaches OK first: the bar takes them right
        // to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: 400);
    }

    /// <summary>What was typed, as typed. Only meaningful after DialogResult.OK.</summary>
    public string Answer => _txt.Text;
}
