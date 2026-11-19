namespace HeatCaller;

/// <summary>
/// A driver's report card on screen (22 Oct 2026), opened from a right-click
/// on a driver in Driver Hub or a result in Race History - with Print and
/// Save Image, so it can go to the driver on paper or be sent on.
///
/// It used to open in the app's message box: fine for reading once, but a
/// card is something a driver wants to keep, and a message box can't be
/// printed or saved. The lines are CoachReport's; the page is
/// ReportCardSheet's.
/// </summary>
internal sealed class ReportCardForm : Form
{
    private readonly string _driver;
    private readonly string _when;
    private readonly DateTime _night;
    private readonly IReadOnlyList<string> _lines;

    /// <summary>A card for tonight's racing (Driver Hub).</summary>
    // WHY (22 Oct 2026): kept so existing callers still build. The night is
    // worked out the same way the track does (TonightAtTheTrack.NightOf), so a
    // card saved after midnight is still named for the evening it covers.
    public ReportCardForm(string driver, string when, IReadOnlyList<string> lines)
        : this(driver, TonightAtTheTrack.NightOf(DateTime.Now), when, lines)
    {
    }

    /// <param name="driver">Who the card is for, as the page names them.</param>
    /// <param name="night">The night the card covers - used for the saved file's name.</param>
    /// <param name="when">The night, already worded - "tonight" or "Wed 22 Oct".</param>
    /// <param name="lines">CoachReport's lines; empty when there is nothing to report.</param>
    public ReportCardForm(string driver, DateTime night, string when, IReadOnlyList<string> lines)
    {
        _driver = string.IsNullOrWhiteSpace(driver) ? "Driver" : driver.Trim();
        _night = night.Date;
        _when = when;
        _lines = lines;

        Text = $"Report card - {_driver} - {_when}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        // Who and which night at a glance across the top (18 Nov 2026,
        // dialog jazz-up), then the lines in a card of their own with the
        // note on how they are worked out as its hint. The printed and saved
        // card is ReportCardSheet's, and none of this changes it.
        var tiles = new[] { new StatTile(), new StatTile() };

        tiles[0].Set(_driver, "Driver");
        tiles[1].Set(_when, "Night");

        var note = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Text = "Worked out from the lap times HeatCaller read that night. A line is left out rather than guessed "
                   + "when there weren't enough clean laps (or sector times) to say it honestly.",
        };

        var body = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Text = lines.Count == 0
                ? "Nothing to report for that night - no laps were filed for this driver. The card fills in from the laps of a night they raced."
                : string.Join(Environment.NewLine + Environment.NewLine, lines.Select((line, i) => $"{i + 1}.  {line}")),
        };

        var card = new Section("What the laps said");
        card.Add(note, gapBelow: UiLayout.RowGap);
        card.Add(body, gapBelow: 0);

        var btnPrint = new Button { Text = "Print..." };
        btnPrint.Click += (_, _) => PrintCard();

        var btnSave = new Button { Text = "Save Image..." };
        btnSave.Click += (_, _) => SaveCard();

        var btnCopy = new Button { Text = "Copy" };
        btnCopy.Click += (_, _) => CopyCard();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        btnPrint.Enabled = btnSave.Enabled = btnCopy.Enabled = lines.Count > 0;

        var footer = new SplitBar(
            new ButtonBar().With(btnPrint, btnSave, btnCopy),
            new ButtonBar(alignRight: true).With(btnClose));

        var page = new FillPage();
        page.Add(new TileStrip(tiles), gapBelow: UiLayout.RowGap);
        page.Add(card, gapBelow: UiLayout.SectionGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // The tiles' faces, the hint's small face and the empty line's muted
        // ink, over what the theme pass gives every label.
        foreach (var tile in tiles)
        {
            tile.Value.Font = AppTheme.TitleFont;
            tile.Caption.Font = AppTheme.MicroFont;
            tile.Caption.ForeColor = AppTheme.TextMuted;
        }

        note.Font = AppTheme.SmallFont;
        note.ForeColor = AppTheme.TextMuted;
        body.ForeColor = lines.Count == 0 ? AppTheme.TextMuted : AppTheme.TextDark;

        DialogFit.Keep(this, page, width: 620);
    }

    /// <summary>The card as text: who, when, and the numbered lines - for pasting into a message.</summary>
    internal static string AsText(string driver, string when, IReadOnlyList<string> lines) =>
        $"Report card - {driver} - {when}" + Environment.NewLine + Environment.NewLine
        + string.Join(Environment.NewLine, lines.Select((line, i) => $"{i + 1}. {line}"));

    /// <summary>The suggested file name for a saved card: driver and the night it covers.</summary>
    // WHY (22 Oct 2026): the name used to carry DateTime.Now, so a card for a
    // past night opened from Race History was named for the day it was saved
    // (and two past nights saved the same day got the same name). It now
    // carries the night the card is about.
    internal static string SuggestedFileName(string driver, DateTime night) =>
        $"report-card-{PlainFileName.SafePart(driver, "driver").Replace(' ', '-')}-{night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}.png";

    private void PrintCard()
    {
        Bitmap? page = null;

        try
        {
            page = ReportCardSheet.Render(_driver, _when, null, _lines);
            PagePrinting.PrintWithPreview(this, $"Report card - {_driver}", new[] { page });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't print the report card: {ex.Message}", "Print",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            page?.Dispose();
        }
    }

    private void SaveCard()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save report card",
            Filter = "PNG image (*.png)|*.png",
            FileName = SuggestedFileName(_driver, _night),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        Bitmap? page = null;

        try
        {
            page = ReportCardSheet.Render(_driver, _when, null, _lines);

            if (PageFiles.SavePages(this, new[] { page }, dialog.FileName) is { } written)
            {
                AppDialog.Show(this, PageFiles.Describe(written), "Report card saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the report card: {ex.Message}", "Save Image",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            page?.Dispose();
        }
    }

    private void CopyCard()
    {
        try
        {
            Clipboard.SetText(AsText(_driver, _when, _lines));
        }
        catch (Exception ex)
        {
            // The clipboard is shared with every other program, and one
            // holding it open makes this throw - say so rather than crash.
            AppDialog.Show(this, $"Couldn't copy the report card: {ex.Message}", "Copy",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
