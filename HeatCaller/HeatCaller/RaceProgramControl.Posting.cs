using System.Drawing;

namespace HeatCaller;

/// <summary>
/// Posting tonight's heats (12 Nov 2026, setup plan D2 and D3): the moment
/// they go on the board. Next Steps asks for it once the heats are built,
/// before the first race. It prints the heat sheets and the marshal list,
/// tells the room, and from then on something that moves drivers between
/// heats asks first. It also says which cars on the heats the loop hasn't
/// heard tonight, so a dead transponder is found in practice and not in Q1.
/// </summary>
public sealed partial class RaceProgramControl
{
    /// <summary>
    /// Whether a transponder has crossed the loop tonight, practice
    /// included. Set by MainForm, which hears the decoder. Null leaves the
    /// not-heard list out.
    /// </summary>
    internal Func<string, bool>? HeardTonight { get; set; }

    /// <summary>Asks the announcer to say something to the room, such as "Tonight's heats are posted".</summary>
    public event Action<string>? NightNotice;

    /// <summary>The heats were posted (or the posting was undone), so Next Steps should say it again.</summary>
    public event Action? HeatsPostedChanged;

    /// <summary>The names of the drivers whose cars on tonight's heats the loop hasn't heard tonight, in program order.</summary>
    internal IReadOnlyList<string> NotHeardTonight(IReadOnlyList<RoundDefinition> day)
    {
        if (HeardTonight is not { } heard)
        {
            return Array.Empty<string>();
        }

        return HeatPosting.NotHeard(day, heard)
            .Select(t => _profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t)
            .ToList();
    }

    /// <summary>
    /// Asks before a change that moves drivers in heats that have been
    /// posted. True to go ahead: the heats aren't posted, or the director
    /// said yes. Also used by Race Control's Redo Remaining Heats.
    /// </summary>
    /// <param name="what">What the change does, as a sentence: "This moves drivers in 2 heats of Stock."</param>
    internal bool ChangePostedHeatsOk(IWin32Window? owner, string what)
    {
        if (!HeatPosting.Posted(ActiveDay) || ActiveDay is not { } day)
        {
            return true;
        }

        return AppDialog.Show(owner ?? this, HeatPosting.ChangeQuestion(day, what), "The heats are posted",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    /// <summary>Next Steps' "Post the heats...": asks what to print, then posts.</summary>
    private void PostTheHeats()
    {
        if (ActiveDay is not { IsLegacy: false } day || _events is null)
        {
            return;
        }

        var dayRounds = DayRounds();
        var clubNight = DayIsClubNight;
        var notHeard = NotHeardTonight(dayRounds);

        using var form = new PostHeatsForm(clubNight, HeatPosting.NotHeardWords(notHeard, listed: 12));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        day.HeatsPostedUtc = DateTime.UtcNow;
        _events.Upsert(day);

        var dayId = day.Id;
        _undo.Capture(clubNight ? "Post the races" : "Post the heats", () =>
        {
            if (_events.Get(dayId) is { } current)
            {
                current.HeatsPostedUtc = null;
                _events.Upsert(current);
            }

            HeatsPostedChanged?.Invoke();
        });

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {(clubNight ? "Tonight's races" : "Tonight's heats")} posted."
                              + (notHeard.Count > 0 ? $" Not heard by the loop tonight: {string.Join(", ", notHeard)}." : string.Empty));

        if (form.Announce)
        {
            NightNotice?.Invoke(HeatPosting.Announcement(clubNight));
        }

        HeatsPostedChanged?.Invoke();

        if (form.PrintHeats || form.PrintMarshals)
        {
            PrintForTheBoard(form.PrintHeats, form.PrintMarshals);
        }
    }

    /// <summary>
    /// Tonight's heat sheets and marshal lists in one print preview, every
    /// class that has races tonight - what goes on the board.
    /// </summary>
    private void PrintForTheBoard(bool heats, bool marshals)
    {
        var pages = new List<Bitmap>();

        try
        {
            var classes = RacingClassNames()
                .Select(name => (Name: name, Rounds: DayRoundsForClass(name)))
                .Where(c => c.Rounds.Count > 0)
                .ToList();

            if (heats)
            {
                var starts = EstimatedStarts();

                foreach (var (name, rounds) in classes)
                {
                    pages.AddRange(HeatSheetRenderer.RenderPages(
                        name,
                        rounds,
                        _profiles,
                        ClassByName(name)?.Colour ?? RaceClass.DefaultColourFor(name),
                        starts));
                }
            }

            if (marshals)
            {
                var lists = classes
                    .Select(c => (c.Name, MarshalAssigner.ToText(c.Name, MarshalAssigner.Assign(c.Rounds, LayoutOf(c.Rounds), _profiles))))
                    .ToList();

                if (lists.Count > 0)
                {
                    pages.AddRange(MarshalSheetRenderer.RenderPages(lists));
                }
            }

            if (pages.Count > 0)
            {
                PagePrinting.PrintWithPreview(this, DayIsClubNight ? "Tonight's races" : "Tonight's heats", pages);
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"The heats are posted, but the sheets couldn't be printed: {ex.Message}\r\n\r\n"
                                 + "Print Heat Sheet... on the program's More menu prints a class at a time.",
                "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    /// <summary>The track layout some rounds run on, for their marshal posts: the first one named, as the Marshals window reads it.</summary>
    private TrackLayout? LayoutOf(IReadOnlyList<RoundDefinition> rounds)
    {
        var layoutName = rounds
            .Select(r => r.TrackLayoutName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        return layoutName is null
            ? null
            : _trackLayouts.All.FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The Post the heats window: what posting does, the cars the loop hasn't
/// heard tonight, and what to print. Everything starts ticked.
/// </summary>
internal sealed class PostHeatsForm : Form
{
    private readonly CheckBox _chkHeats = new ThemedCheckBox();
    private readonly CheckBox _chkMarshals = new ThemedCheckBox();
    private readonly CheckBox _chkAnnounce = new ThemedCheckBox();

    public bool PrintHeats => _chkHeats.Checked;

    public bool PrintMarshals => _chkMarshals.Checked;

    public bool Announce => _chkAnnounce.Checked;

    /// <param name="clubNight">Races rather than heats.</param>
    /// <param name="notHeard">HeatPosting.NotHeardWords, or empty when every car has been heard.</param>
    public PostHeatsForm(bool clubNight, string notHeard)
    {
        var heats = clubNight ? "races" : "heats";

        Text = clubNight ? "Post the Races" : "Post the Heats";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblWhat = new Label
        {
            Text = $"Posting puts tonight's {heats} on the board. Drivers look up their {(clubNight ? "races" : "heat")} once and trust it, "
                   + $"so from now on, Move Drivers Between Heats and Redo Remaining Heats ask before changing a posted {(clubNight ? "race" : "heat")}. "
                   + "A driver who arrives late still goes in with Add a Late Entry, which moves nobody else.",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
        };

        var lblNotHeard = new Label
        {
            Text = notHeard.Length > 0 ? notHeard : "Every car on tonight's " + heats + " has crossed the loop tonight.",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            ForeColor = notHeard.Length > 0 ? AppTheme.Warning : AppTheme.TextMuted,
        };

        _chkHeats.Text = clubNight ? "Print the race sheets" : "Print the heat sheets";
        _chkHeats.AutoSize = true;
        _chkHeats.Checked = true;

        _chkMarshals.Text = "Print the marshal list";
        _chkMarshals.AutoSize = true;
        _chkMarshals.Checked = true;

        _chkAnnounce.Text = "Tell the room (the announcer)";
        _chkAnnounce.AutoSize = true;
        _chkAnnounce.Checked = true;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnPost = new Button { Text = clubNight ? "Post the Races" : "Post the Heats", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblWhat, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(lblNotHeard, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(_chkHeats, stretch: false, gapBelow: 0);
        page.Add(_chkMarshals, stretch: false, gapBelow: 0);
        page.Add(_chkAnnounce, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnPost), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnPost;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 560, height: 380);
    }
}
