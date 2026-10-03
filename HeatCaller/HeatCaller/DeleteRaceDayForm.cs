namespace HeatCaller;

/// <summary>
/// What to take away with a race day.
///
/// Deleting the day itself is never in question - that is what was asked
/// for. What IS a question is how far it reaches, because a race day leaves
/// marks in five other places and a club deleting a mistake wants a
/// different answer from a club clearing out a test evening:
///
///   - the saved races in Race History, and everything drawn from them
///   - a track record one of those laps set
///   - a season that picked those races by hand
///   - badges the drivers earned in them
///
/// Tickboxes rather than a Yes/No, because those four are genuinely
/// independent and a single "delete everything?" makes the careful answer
/// unavailable. Every line carries its own count, so nobody is agreeing to
/// a number they cannot see.
///
/// On the layout pieces (DialogFit): the question, what goes regardless,
/// the four ticks each with its hint under its words - the three that
/// depend on the first a step in from it - and the note on Undo, all of it
/// the window's body, wrapping to the width; Delete and Cancel at the foot.
/// The window is as tall as all of that, and on a screen too short for it
/// the body scrolls and the buttons stay in view. It used to be placed in
/// pixels: the text wrapped at a fixed 520px whatever the text size, a long
/// day's name ran on one line past the right-hand edge, and the window was
/// as tall as all of it with nothing to scroll - at 200% taller than a
/// 1920x1080 screen, with Delete and Cancel under its edge.
/// </summary>
public sealed class DeleteRaceDayForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size - wider when a tick's words need more.</summary>
    private const int DialogWidth = 552;

    /// <summary>
    /// How far the three ticks that depend on the first are in from it, at
    /// the normal text size - under its words. Further in once the app's
    /// tick box has grown past it with the text (TickBox.UnderCaption).
    /// </summary>
    private const int TickIndent = 22;

    /// <summary>
    /// How far a tick's hint is in from the tick, at the normal text size,
    /// so it starts under the tick's words rather than its box - and as far
    /// as the words start once the box grows past it with the text: at 200%
    /// the hints and the three ticks under the first started left of the
    /// words above them (layout pictures, 30 Sep).
    /// </summary>
    private const int HintIndent = 20;

    private readonly CheckBox _chkHistory = new ThemedCheckBox();
    private readonly CheckBox _chkRecords = new ThemedCheckBox();
    private readonly CheckBox _chkSeasons = new ThemedCheckBox();
    private readonly CheckBox _chkBadges = new ThemedCheckBox();

    /// <summary>What a day's delete would touch, counted before anything is asked.</summary>
    /// <param name="Rounds">Heats and finals on the day. Always removed - see the dialog's own wording.</param>
    /// <param name="Sessions">Saved races in Race History.</param>
    /// <param name="Records">Tracks with a record - the lap record or a sector record - set in one of those races.</param>
    /// <param name="Seasons">Championships that named one of those races by hand.</param>
    /// <param name="Badges">Badges earned in those races.</param>
    public readonly record struct Counts(int Rounds, int Sessions, int Records, int Seasons, int Badges);

    /// <summary>Remove the saved races from Race History too. Only meaningful after DialogResult.OK.</summary>
    public bool DeleteHistory => _chkHistory.Checked;

    /// <summary>Clear track records set in those races.</summary>
    public bool ClearRecords => _chkHistory.Checked && _chkRecords.Checked;

    /// <summary>Take those races out of any championship that picked them by hand.</summary>
    public bool ClearSeasons => _chkHistory.Checked && _chkSeasons.Checked;

    /// <summary>Take back badges earned in those races.</summary>
    public bool ClearBadges => _chkHistory.Checked && _chkBadges.Checked;

    public DeleteRaceDayForm(string dayName, Counts counts)
    {
        Text = "Delete race day";
        Font = AppTheme.BodyFont;

        var page = new FillPage();

        // Everything the window says: on a screen too short for all of it,
        // it scrolls, and Delete and Cancel under it stay in view.
        var body = DialogFit.Body(page);

        var lblWhat = new Label
        {
            Text = $"Delete \"{dayName}\"?",
            AutoSize = true,
            Font = AppTheme.HeadingFont,
        };

        body.Add(lblWhat, gapBelow: UiLayout.LabelGap);

        // What goes regardless, said plainly and first. The rounds have to
        // go with the day: a round belonging to no event is the exact state
        // race days were introduced to remove, and the next launch would
        // sweep them into the legacy holder where they would fold into
        // somebody else's qualifying.
        var lblAlways = new Label
        {
            Text = counts.Rounds == 1
                ? "The day goes, and so does the one heat or final on it, with its check-in sheet, "
                  + "its scrutineering and its timetable."
                : $"The day goes, and so do the {counts.Rounds} heats and finals on it, with its check-in sheet, "
                  + "its scrutineering and its timetable.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        body.Add(lblAlways);

        // --- The choices ---

        var lblAlso = new Label
        {
            Text = counts.Sessions == 0
                ? "No races were saved on this day, so there is nothing else to remove."
                : "What about the races that were run?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        body.Add(lblAlso, gapBelow: UiLayout.LabelGap);

        // A tick, as wide as its words, in from the left by its indent, and
        // its hint under its words, wrapping to the width the body has. The
        // body measures its parts inside their margins, so a part moved in
        // is narrower by as much and wraps to that.
        void Tick(CheckBox box, string text, string hint, int indent, bool enabled, int gapBelow = UiLayout.RowGap)
        {
            box.Text = text;
            box.AutoSize = true;
            box.Enabled = enabled;
            box.Font = AppTheme.BodyFont;

            var lbl = new Label
            {
                Text = hint,
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            body.Add(box, stretch: false, gapBelow: UiLayout.HintGap);
            body.Add(lbl, gapBelow: gapBelow);

            box.Margin = new Padding(indent, box.Margin.Top, box.Margin.Right, box.Margin.Bottom);
            lbl.Margin = new Padding(indent + TickBox.UnderCaption(HintIndent), lbl.Margin.Top, lbl.Margin.Right, lbl.Margin.Bottom);
        }

        var anyRaces = counts.Sessions > 0;

        Tick(_chkHistory,
            counts.Sessions == 1
                ? "Delete the 1 saved race from Race History"
                : $"Delete the {counts.Sessions} saved races from Race History",
            "Their finishing order, lap times and notes. Personal bests, pace charts and head-to-head all "
            + "read from these, so those go with them. Leave this unticked and the races stay - they simply "
            + "stop belonging to a race day.",
            0,
            anyRaces);

        // The three below only mean anything once the races themselves are
        // going: they are the traces those races left elsewhere. Indented
        // and gated so the dialog cannot be left in a state that reads as a
        // choice but is not one.
        Tick(_chkRecords,
            counts.Records == 1
                ? "Also clear 1 track record set in them"
                : $"Also clear {counts.Records} track records set in them",
            "A lap record lives on the track layout, not in history, so it survives unless it is cleared "
            + "here. Cleared means back to no record - it is not recalculated from the races that are left.",
            TickBox.UnderCaption(TickIndent),
            anyRaces && counts.Records > 0);

        Tick(_chkSeasons,
            counts.Seasons == 1
                ? "Also take them out of 1 championship that picked them by hand"
                : $"Also take them out of {counts.Seasons} championships that picked them by hand",
            "Deleting the day already stops these races counting towards a championship that scores by race "
            + "day. This is for a season someone named the races in individually.",
            TickBox.UnderCaption(TickIndent),
            anyRaces && counts.Seasons > 0);

        // A section's gap under the last, before the note on Undo, which is
        // about all four rather than about this one.
        Tick(_chkBadges,
            counts.Badges == 1
                ? "Also take back 1 badge earned in them"
                : $"Also take back {counts.Badges} badges earned in them",
            "A badge names the race that produced it. Race totals on a driver's profile are left as they "
            + "are - winding those back cannot be done reliably from here - so a milestone badge (10 races, "
            + "first win) is earned again next time they race.",
            TickBox.UnderCaption(TickIndent),
            anyRaces && counts.Badges > 0,
            gapBelow: UiLayout.SectionGap);

        // Ticking the top one is what makes the other three available, and
        // unticking it puts them away again rather than leaving three
        // ticked boxes that do nothing.
        void SyncChildren()
        {
            foreach (var (box, count) in new[]
                     {
                         (_chkRecords, counts.Records),
                         (_chkSeasons, counts.Seasons),
                         (_chkBadges, counts.Badges),
                     })
            {
                box.Enabled = _chkHistory.Checked && count > 0;

                if (!box.Enabled)
                {
                    box.Checked = false;
                }
            }
        }

        _chkHistory.CheckedChanged += (_, _) => SyncChildren();
        SyncChildren();

        var lblUndo = new Label
        {
            Text = "Undo puts all of it back - but only until a few more changes are made, or the app is closed. "
                   + "Anything set since (a track record taken in a later race, a race added to a season) is left as it is.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // The last part of the body: nothing under it inside the part that
        // scrolls - see DialogFit.Body.
        body.Add(lblUndo, gapBelow: 0);

        // Cancel is the default button, not Delete. Enter should not be able
        // to destroy a race day.
        var btnDelete = new Button { Text = "Delete", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Delete first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnDelete), gapBelow: 0);
        btnDelete.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnCancel;
        CancelButton = btnCancel;

        // The theme pass colours Delete as a button that destroys something
        // (red, its words in white), from its caption. It used to have its
        // words turned amber after it, from before the theme coloured such
        // buttons red - amber on red, hard to read (layout pictures, 30 Sep).
        AppTheme.ApplyRecursively(this);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when a tick's words need more on one line: a tick does not
        // wrap, and a count running to three figures lengthens it. Measured
        // in the faces the theme pass left them in, each tick with its
        // indent, and with room for the body's scrollbar, which takes its
        // width from the ticks on a screen too short for all of it.
        var widestTick = new[] { _chkHistory, _chkRecords, _chkSeasons, _chkBadges }
            .Max(box => box.Margin.Horizontal + box.GetPreferredSize(Size.Empty).Width);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + widestTick + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }
}
