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
///
/// On the layout pieces (DialogFit): the introduction wrapping across the
/// top, the words under it taking the height the window has and scrolling
/// on their own - each group's heading and line about it, then each word in
/// bold and what it means, all wrapping to the window's width - and Close at
/// the foot. It used to be placed in pixels: the introduction in a docked
/// strip sized to it by hand, the words placed down a scrolling panel one
/// under another on every resize, and Close at the left of a footer of fixed
/// height.
/// </summary>
public sealed class RacingWordsForm : Form
{
    /// <summary>
    /// One entry: the words as the app shows them, and what they mean.
    /// </summary>
    // WHY (23 Nov 2026, bug hunt 24): internal, not private, so a test can
    // read the words the sheet shows without opening the window.
    internal sealed record Word(string Term, string Meaning);

    internal sealed record Group(string Heading, string Blurb, Word[] Words);

    /// <summary>The words show at least this many lines of text before the window scrolls instead.</summary>
    private const int LeastLines = 6;

    /// <summary>
    /// How narrow the window can be made inside its frame, at the normal text
    /// size: the 560 across its frame it always could be.
    /// </summary>
    private const int LeastWidth = 544;

    public RacingWordsForm()
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, most parts had their windows before the
        // theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        Text = "What these words mean";
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
            Text =
                "The buttons in HeatCaller say what they do. The words below are the sport's own - "
                + "the ones you will hear in the pits and see on a printed heat sheet - so they are explained here.",
        };

        // The words scroll down on their own, as wide as the introduction
        // over them: no margins of their own - the page's are round them -
        // and no room under the last line, which the theme pass gives a
        // panel that scrolls (see ScrollTheming.NoRoomBelow).
        var words = new PageStack { Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(words);
        BuildWords(words);

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        page.Add(lblIntro);

        // The words take the height; they keep a few lines before the window
        // scrolls instead.
        page.AddFill(words, _ => LeastLines * Pieces.LineHeight(Font), gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, as a dialog's buttons go.
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        DialogFit.Keep(this, page, width: 720, height: 620);

        // And it can be made narrower than it opened, down to the 560 it
        // always could, at the text size in use: nothing in it is measured
        // for its width - the introduction and every word wrap to what they
        // have. After DialogFit's own Load handler, added above, which sets
        // the least to the width it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, Width - ClientSize.Width + UiLayout.AtTextSize(LeastWidth)),
            MinimumSize.Height);
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
    internal static Group[] Words() => new[]
    {
        new Group(
            "Setting the night up",
            "The words on the buttons that build a race day.",
            new[]
            {
                new Word(
                    "Build tonight's heats",
                    "Takes everyone who is checked in, splits each class into heats of the size you set, and "
                    + "writes the running order. Press it once the classes are ticked and the drivers are in."),
                new Word(
                    "Heats each driver gets",
                    "How many times every driver in a class races before the finals. Three is the usual club "
                    // WHY (23 Nov 2026, bug hunt 24): "Usually" - the old words said the best
                    // result always counts, which is wrong for a class set to add heats together.
                    + "number. Usually their best result out of those counts."),
                new Word(
                    "Build the finals from qualifying",
                    "Sorts each class by how they did in the heats and puts them into the finals - the fastest "
                    + "into the A-Main, the next lot into the B-Main, and so on."),
                new Word(
                    "Move the top finishers up",
                    "Takes the drivers who won through in a lower final and puts them into the final above it. "
                    + "Has to happen before that final goes out, which is why the app asks for it when it does."),
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
                // WHY (23 Nov 2026, bug hunt 24): the old entry, "Best run counts", said only a
                // driver's single best heat is ever used for seeding. That is true only when a
                // class's "Heats that count" is 1: set to 2 or more the best heats are added
                // together, and a class can qualify on points instead. The term is now the
                // setting's own name, which "Best run counts" matched nowhere in the app.
                new Word(
                    "Heats that count",
                    "Usually only a driver's single best heat is used for seeding, so one bad heat - a flat "
                    + "battery, somebody else's mistake - does not decide their night. A class can be set to "
                    + "add its best two or more heats together instead, or to qualify on points."),
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
                    "The check on a car before it races - battery, tires, body, whatever your club inspects. "
                    + "Also called tech."),
                new Word(
                    "Decoder",
                    "The box that listens to the timing loop and tells the app about every pass. If it is not "
                    + "connected, no laps are counted."),
            }),
    };

    /// <summary>
    /// The sheet, down <paramref name="words"/>: each group a card of its
    /// own (18 Nov 2026, the dialog look) - its heading the card's caption
    /// and the line about it the card's hint - then each word in bold with
    /// what it means under it, every line as wide as the card, wrapping to
    /// it. A row's gap and a hair before each word, a hint's gap between a
    /// word and its meaning, and a section's gap between one card and the
    /// next. Nothing under the last card - the last part of a column that
    /// scrolls, whose gap would be scrolled to as blank room.
    /// </summary>
    private static void BuildWords(PageStack words)
    {
        var groups = Words();

        for (var g = 0; g < groups.Length; g++)
        {
            var group = groups[g];
            var card = new Section(group.Heading);

            card.Add(new Label
            {
                Text = group.Blurb,
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: UiLayout.RowGap + 2);

            for (var w = 0; w < group.Words.Length; w++)
            {
                var word = group.Words[w];

                card.Add(new Label
                {
                    Text = word.Term,
                    AutoSize = true,
                    Font = AppTheme.BoldFont,
                    ForeColor = AppTheme.TextDark,
                }, gapBelow: UiLayout.HintGap);

                var lastInGroup = w == group.Words.Length - 1;

                card.Add(new Label
                {
                    Text = word.Meaning,
                    AutoSize = true,
                    Font = AppTheme.BodyFont,
                    ForeColor = AppTheme.TextDark,
                }, gapBelow: lastInGroup ? 0 : UiLayout.RowGap + 2);
            }

            words.Add(card, gapBelow: g < groups.Length - 1 ? UiLayout.SectionGap : 0);
        }
    }
}
