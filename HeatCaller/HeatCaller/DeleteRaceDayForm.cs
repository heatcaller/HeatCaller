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
/// </summary>
public sealed class DeleteRaceDayForm : Form
{
    private readonly CheckBox _chkHistory = new();
    private readonly CheckBox _chkRecords = new();
    private readonly CheckBox _chkSeasons = new();
    private readonly CheckBox _chkBadges = new();

    /// <summary>What a day's delete would touch, counted before anything is asked.</summary>
    /// <param name="Rounds">Heats and finals on the day. Always removed - see the dialog's own wording.</param>
    /// <param name="Sessions">Saved races in Race History.</param>
    /// <param name="Records">Track records whose lap was set in one of those races.</param>
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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        const int width = 520;
        var y = UiLayout.PageTop;

        var lblWhat = new Label
        {
            Text = $"Delete \"{dayName}\"?",
            AutoSize = true,
            Font = AppTheme.HeadingFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblWhat);

        y = UiLayout.Below(lblWhat, UiLayout.LabelGap);

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
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblAlways);

        y = UiLayout.Below(lblAlways, UiLayout.SectionGap);

        // --- The choices ---

        var lblAlso = new Label
        {
            Text = counts.Sessions == 0
                ? "No races were saved on this day, so there is nothing else to remove."
                : "What about the races that were run?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblAlso);

        y = UiLayout.Below(lblAlso, UiLayout.LabelGap);

        int Tick(CheckBox box, string text, string hint, int indent, bool enabled)
        {
            box.Text = text;
            box.AutoSize = true;
            box.Enabled = enabled;
            box.Font = AppTheme.BodyFont;
            box.Location = new Point(UiLayout.PageLeft + indent, y);

            Controls.Add(box);

            var lbl = new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(width - indent - 24, 0),
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
                Location = new Point(UiLayout.PageLeft + indent + 20, UiLayout.Below(box, 2)),
            };

            Controls.Add(lbl);

            return UiLayout.Below(lbl, UiLayout.RowGap);
        }

        var anyRaces = counts.Sessions > 0;

        y = Tick(_chkHistory,
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
        y = Tick(_chkRecords,
            counts.Records == 1
                ? "Also clear 1 track record set in them"
                : $"Also clear {counts.Records} track records set in them",
            "A lap record lives on the track layout, not in history, so it survives unless it is cleared "
            + "here. Cleared means back to no record - it is not recalculated from the races that are left.",
            22,
            anyRaces && counts.Records > 0);

        y = Tick(_chkSeasons,
            counts.Seasons == 1
                ? "Also take them out of 1 championship that picked them by hand"
                : $"Also take them out of {counts.Seasons} championships that picked them by hand",
            "Deleting the day already stops these races counting towards a championship that scores by race "
            + "day. This is for a season someone named the races in individually.",
            22,
            anyRaces && counts.Seasons > 0);

        y = Tick(_chkBadges,
            counts.Badges == 1
                ? "Also take back 1 badge earned in them"
                : $"Also take back {counts.Badges} badges earned in them",
            "A badge names the race that produced it. Race totals on a driver's profile are left as they "
            + "are - winding those back cannot be done reliably from here - so a milestone badge (10 races, "
            + "first win) is earned again next time they race.",
            22,
            anyRaces && counts.Badges > 0);

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

        y += UiLayout.RowGap;

        var lblUndo = new Label
        {
            Text = "Undo puts all of it back - but only until a few more changes are made, or the app is closed. "
                   + "Anything set since (a track record taken in a later race, a race added to a season) is left as it is.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblUndo);

        y = UiLayout.Below(lblUndo, UiLayout.SectionGap);

        // Cancel is the default button, not Delete. Enter should not be able
        // to destroy a race day.
        var btnDelete = new Button { Text = "Delete", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnDelete.Size = new Size(UiLayout.WidthFor(btnDelete), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + width;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnDelete.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnDelete.Width, y);

        Controls.Add(btnDelete);
        Controls.Add(btnCancel);

        AcceptButton = btnCancel;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Danger colour applied after the theme pass, or it would be painted
        // back over.
        btnDelete.ForeColor = AppTheme.Warning;

        ClientSize = new Size(
            right + UiLayout.PageLeft,
            UiLayout.Below(btnDelete, UiLayout.PageTop));
    }
}
