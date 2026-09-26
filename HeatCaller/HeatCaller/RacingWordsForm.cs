namespace HeatCaller;

/// <summary>
/// What the words on the race programme mean, in plain English.
///
/// The buttons in this app used to be labelled in the language of the sport
/// - "Seed the Mains", "Apply Bump-Ups", "Qualifying rounds" - and the
/// language of the sport is exactly what a first-time race director does not
/// have. Those BUTTONS now say what they do ("Build the finals from
/// qualifying"), because a button is read once, under pressure, by somebody
/// who has to decide whether to press it.
///
/// The NOUNS are a different problem and deliberately have not been
/// renamed. A-Main, heat, qualifier, bump-up and TQ are what the printed
/// heat sheet says, what the drivers in the pits say, and what the announcer
/// says over the PA. Renaming them inside the app would leave the app
/// speaking a private language that matched nothing else at the track, which
/// helps the newcomer for one evening and then strands them. So the nouns
/// stay, and this window is where they get explained - once, in one place,
/// reachable from the page that uses them.
///
/// It is a reference sheet and nothing else: it reads no stores, changes
/// nothing, and can be opened and shut at any point in a race day.
/// </summary>
public sealed class RacingWordsForm : Form
{
    /// <summary>
    /// One entry. <paramref name="WasCalled"/> is filled in for the four
    /// captions that were renamed, so somebody who learned the old wording
    /// - from an older build, a forum post, or another club's software -
    /// can still find their way to the new one.
    /// </summary>
    private sealed record Word(string Term, string Meaning, string? WasCalled = null);

    private sealed record Group(string Heading, string Blurb, Word[] Words);

    private const int Pad = 18;

    private readonly Panel _body = new();
    private readonly Panel _header = new();
    private readonly Label _lblIntro = new();

    /// <summary>Every label placed by Relayout, in the order they appear.</summary>
    private readonly List<Control> _flow = new();

    public RacingWordsForm()
    {
        Text = "What these words mean";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);
        ClientSize = new Size(720, 620);
        Font = AppTheme.BodyFont;

        _header.Dock = DockStyle.Top;
        _header.Height = 1;

        _lblIntro.AutoSize = true;
        _lblIntro.Font = AppTheme.BodyFont;
        _lblIntro.ForeColor = AppTheme.TextMuted;
        _lblIntro.Text =
            "The buttons in HeatCaller say what they do. The words below are the sport's own, "
            + "and they are the ones you will hear in the pits and see on a printed heat sheet, "
            + "so they are left as they are and explained here instead.";

        _header.Controls.Add(_lblIntro);

        _body.Dock = DockStyle.Fill;
        _body.AutoScroll = true;

        // Both bands measure and place their own children on every resize, so
        // the repair pass is told to keep out of them - otherwise the two
        // take turns moving the same labels.
        LayoutDoctor.LeaveAlone(_body);
        LayoutDoctor.LeaveAlone(_header);

        BuildWords();

        var footer = new Panel { Dock = DockStyle.Bottom, Height = UiLayout.ButtonHeight + 24 };

        var btnClose = new Button
        {
            Text = "Close",
            Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight),
            Location = new Point(12, 12),
        };

        btnClose.Click += (_, _) => Close();
        footer.Controls.Add(btnClose);

        // Fill is added first and docks last: WinForms resolves docking from
        // the end of the collection backwards.
        Controls.Add(_body);
        Controls.Add(footer);
        Controls.Add(_header);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Both hooked and both run once, AFTER theming: a label that is not
        // yet parented and styled reports the WinForms default font's
        // metrics, and every position below is measured from the real ones.
        _header.Resize += (_, _) => LayoutHeader();
        _body.Resize += (_, _) => Relayout();

        LayoutHeader();
        Relayout();
    }

    /// <summary>
    /// Wraps the opening paragraph to the window's width and sizes the band
    /// to it. Re-run on resize, so the intro is not stuck at the width the
    /// window happened to open at.
    /// </summary>
    private void LayoutHeader()
    {
        _lblIntro.MaximumSize = new Size(Math.Max(200, _header.Width - Pad * 2), 0);
        _lblIntro.Location = new Point(Pad, 14);

        var wanted = UiLayout.Below(_lblIntro, 12);

        // Only when it changed: assigning Height re-runs this through Resize,
        // and an unconditional assignment would never settle.
        if (_header.Height != wanted)
        {
            _header.Height = wanted;
        }
    }

    /// <summary>Opens it, owned, so it centres on the page that asked.</summary>
    public static void ShowFor(IWin32Window? owner)
    {
        using var form = new RacingWordsForm();
        form.ShowDialog(owner);
    }

    /// <summary>
    /// The sheet itself.
    ///
    /// Ordered the way a race day happens rather than alphabetically: what
    /// you build first, what runs, what decides the result, and then the
    /// kit. An A-to-Z is easier to write and useless to somebody who does
    /// not yet know which word they are looking for.
    /// </summary>
    private static Group[] Words() => new[]
    {
        new Group(
            "Setting the night up",
            "The words on the buttons that build a race day.",
            new[]
            {
                new Word(
                    "Build tonight's heats",
                    "Takes everyone who is checked in, splits each class into heats of the size you set, and "
                    + "writes the running order. Press it once the classes are ticked and the drivers are in.",
                    "Build the Qualifying Program"),
                new Word(
                    "Heats each driver gets",
                    "How many times every driver in a class races before the finals. Three is the usual club "
                    + "number. Their best result out of those counts.",
                    "Qualifying rounds"),
                new Word(
                    "Build the finals from qualifying",
                    "Sorts each class by how they did in the heats and puts them into the finals - the fastest "
                    + "into the A-Main, the next lot into the B-Main, and so on.",
                    "Seed the Mains"),
                new Word(
                    "Move the top finishers up",
                    "Takes the drivers who won through in a lower final and puts them into the final above it. "
                    + "Has to happen before that final goes out, which is why the app asks for it when it does.",
                    "Apply Bump-Ups"),
            }),

        new Group(
            "What actually runs",
            "One line on the program is one of these.",
            new[]
            {
                new Word(
                    "Heat",
                    "A short race that does not decide the result. It decides where you start in the final. "
                    + "Also called a qualifier."),
                new Word(
                    "Qualifier",
                    "The same thing as a heat. \"Q2\" means the second round of qualifying, so \"Q2 Heat 3\" is "
                    + "the third heat of that round."),
                new Word(
                    "Round",
                    "One pass through a class: every driver in it races once. Three rounds of qualifying means "
                    + "every driver races three times."),
                new Word(
                    "Main",
                    "The race that counts. Also called the final. A class's result comes from its mains, not "
                    + "from the heats."),
                new Word(
                    "A-Main, B-Main, C-Main",
                    "Finals in order of speed. The A-Main has the fastest qualifiers in it and decides the win; "
                    + "B is the next group down, C the one below that. The app runs them from the bottom up, so "
                    + "the drivers who move up get to race in the tier above."),
                new Word(
                    "Tier",
                    "Which letter a final is. The A-Main is the top tier."),
            }),

        new Group(
            "How places are worked out",
            "Where the numbers on the program come from.",
            new[]
            {
                new Word(
                    "Seeding",
                    "Sorting drivers by how well they qualified, then dealing them into the finals in that "
                    + "order. \"Build the finals from qualifying\" is the button that does it."),
                new Word(
                    "Bump-up",
                    "A driver moving from a lower final into the one above it, earned by finishing near the "
                    + "front. You set how many bump up per class on the Classes page, and it can be switched "
                    + "off entirely."),
                new Word(
                    "TQ",
                    "Top qualifier - whoever was quickest across the heats. They start the A-Main from pole."),
                new Word(
                    "Grid",
                    "The starting order of a race. Position one is the front."),
                new Word(
                    "Best run counts",
                    "Only a driver's single best heat is used for seeding, so one bad heat - a flat battery, "
                    + "somebody else's mistake - does not decide their night."),
            }),

        new Group(
            "People and kit",
            "The rest of the vocabulary you will meet on these pages.",
            new[]
            {
                new Word(
                    "Transponder",
                    "The small tag fitted to a car. The timing loop in the track hears it go past, and that is "
                    + "how laps get counted. Every driver's transponder number is their identity in the app."),
                new Word(
                    "Check-in",
                    "Saying who actually turned up tonight. Only drivers who are checked in go into the heats, "
                    + "which is what stops last week's entry list racing this week."),
                new Word(
                    "Marshal",
                    "Someone standing at a corner to turn cars back over. The app works out who marshals which "
                    + "race, normally the drivers from the race before."),
                new Word(
                    "Scrutineering",
                    "The check on a car before it races - battery, tyres, body, whatever your club inspects. "
                    + "Also called tech."),
                new Word(
                    "Decoder",
                    "The box that listens to the timing loop and tells the app about every pass. If it is not "
                    + "connected, no laps are counted."),
            }),
    };

    private void BuildWords()
    {
        var first = true;

        foreach (var group in Words())
        {
            var heading = new Label
            {
                Text = group.Heading.ToUpperInvariant(),
                AutoSize = true,
                Font = AppTheme.MicroFont,
                ForeColor = AppTheme.Primary,
                // Tagged so Relayout knows this row opens a section and
                // wants the bigger gap above it - the alternative is a
                // parallel list of indices that has to stay in step.
                Tag = first ? "group-first" : "group",
            };

            first = false;

            var blurb = new Label
            {
                Text = group.Blurb,
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
                Tag = "blurb",
            };

            _body.Controls.Add(heading);
            _body.Controls.Add(blurb);
            _flow.Add(heading);
            _flow.Add(blurb);

            foreach (var word in group.Words)
            {
                var term = new Label
                {
                    Text = word.WasCalled is null
                        ? word.Term
                        : $"{word.Term}   (used to be called “{word.WasCalled}”)",
                    AutoSize = true,
                    Font = AppTheme.BoldFont,
                    ForeColor = AppTheme.TextDark,
                    Tag = "term",
                };

                var meaning = new Label
                {
                    Text = word.Meaning,
                    AutoSize = true,
                    Font = AppTheme.BodyFont,
                    ForeColor = AppTheme.TextDark,
                    Tag = "meaning",
                };

                _body.Controls.Add(term);
                _body.Controls.Add(meaning);
                _flow.Add(term);
                _flow.Add(meaning);
            }
        }
    }

    /// <summary>
    /// Places every line down the page at whatever width the window
    /// currently is.
    ///
    /// The content width is taken from the panel's OUTER width less a
    /// permanent allowance for the scrollbar, rather than from ClientSize.
    /// ClientSize shrinks the moment the scrollbar appears, which would
    /// re-fire Resize, re-wrap every paragraph, and - at the one window
    /// height where the content is just longer than the panel - flip between
    /// the two states forever.
    /// </summary>
    private void Relayout()
    {
        var width = Math.Max(
            220,
            _body.Width - Pad * 2 - SystemInformation.VerticalScrollBarWidth);

        var y = Pad;

        foreach (var control in _flow)
        {
            var kind = control.Tag as string;

            y += kind switch
            {
                "group-first" => 0,
                "group" => UiLayout.SectionGap,
                "blurb" => 2,
                "term" => UiLayout.RowGap + 2,
                _ => UiLayout.HintGap,
            };

            if (control is Label label)
            {
                label.MaximumSize = new Size(width, 0);
            }

            control.Location = new Point(Pad, y);
            y = control.Top + UiLayout.HeightOf(control);
        }

        // A spacer rather than Padding: AutoScroll measures the scroll range
        // from where the children end and ignores the container's bottom
        // padding, so without this the last line sits hard against the
        // footer when the sheet is scrolled to the end.
        //
        // Assigned only when it changed, for the same reason the two bands
        // guard their heights: it can bring a scrollbar in or out, which
        // re-fires Resize.
        var wanted = new Size(0, y + Pad);

        if (_body.AutoScrollMinSize != wanted)
        {
            _body.AutoScrollMinSize = wanted;
        }
    }
}
