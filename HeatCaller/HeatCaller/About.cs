namespace HeatCaller;

/// <summary>
/// What ships inside HeatCaller that somebody else wrote, and under what
/// licence.
///
/// Data, not wording inside the About box, so a test can hold the list to
/// every package that ships and to none of them having a blank licence
/// (BugHunt4AboutTests). The box only reads it.
///
/// Worked out on 5 Oct 2026 from the files themselves rather than from
/// memory: the csproj's PackageReferences, every transitive package in
/// obj\project.assets.json, and each package's own .nuspec and licence file
/// in the NuGet cache. The Assets folder was read the same way - the two
/// typefaces carry their OFL text beside them, and the music and the sound
/// packs are the club's own, synthesised by the scripts in audio-tools, so
/// nothing in them is anyone else's to license.
///
/// NOTHING IN HERE IS COPYLEFT. That matters for the installer: the Kokoro
/// voice is MIT (the code) over Apache-2.0 (the model and its voices), and
/// Kokoro's text processing is MisakiSharp, a C# port that is explicit about
/// having no espeak-ng in it - espeak-ng is GPL-3.0, and a copy of it inside
/// the installer would put the whole download under the GPL. So the 310 MB
/// model can be bundled if the club would rather not download it. The two
/// conditions that do travel with HeatCaller are attribution ones: the OFL
/// licence files must stay beside the fonts (Assets\Fonts\OFL-*.txt, and the
/// csproj copies them), and the Apache-2.0 components want their licence
/// text passed on - which is why Kokoro's own LICENSE lands in voices\
/// beside the exe.
///
/// A new PackageReference goes on this list in the same change that adds it.
/// </summary>
internal static class ThirdPartyCatalog
{
    /// <summary>
    /// One component, as the About box lists it.
    /// </summary>
    /// <param name="Name">What it is called.</param>
    /// <param name="Version">The version that ships, or "" for something that has none (a typeface).</param>
    /// <param name="License">Its licence, as its own files state it. Never blank.</param>
    /// <param name="What">What HeatCaller uses it for, in a line.</param>
    /// <param name="Packages">
    /// The NuGet package ids this entry covers - a family published as
    /// several packages is one component here. Empty for anything that is
    /// not a package.
    /// </param>
    /// <param name="Ships">
    /// Whether its bits are inside the app a club installs. False for
    /// something the club downloads later.
    /// </param>
    /// <param name="Note">Anything that constrains redistribution, or where the licence came from when the package does not carry it. Null when there is nothing to say.</param>
    internal sealed record Component(
        string Name,
        string Version,
        string License,
        string What,
        IReadOnlyList<string> Packages,
        bool Ships = true,
        string? Note = null);

    /// <summary>
    /// The list itself, in the order the About box shows it: the parts of
    /// the app a club meets first, then the voice stack, then the
    /// typefaces.
    /// </summary>
    internal static readonly IReadOnlyList<Component> Components = new[]
    {
        new Component(
            "NAudio",
            "2.3.0",
            "MIT",
            "Plays the announcer, the sound effects, the ambience and the music.",
            new[] { "NAudio", "NAudio.Core", "NAudio.Asio", "NAudio.Midi", "NAudio.Wasapi", "NAudio.WinForms", "NAudio.WinMM" }),

        new Component(
            "HidSharp",
            "2.1.0",
            "Apache-2.0",
            "Talks to an EasyLap decoder over USB.",
            new[] { "HidSharp" },
            Note: "The package itself carries no licence file, only a link to the project's own LICENSE.txt. Confirm the text before the installer ships."),

        new Component(
            "System.IO.Ports",
            "10.0.12",
            "MIT",
            "Talks to an I-Lap decoder over a serial port.",
            new[] { "System.IO.Ports", "runtime.native.System.IO.Ports" },
            Note: "Microsoft. The sixteen runtime.<platform>... packages beside it only help NuGet pick a file; none of their bits ship on Windows."),

        new Component(
            "System.Speech",
            "10.0.12",
            "MIT",
            "The free Windows voice, when a club has no ElevenLabs account.",
            new[] { "System.Speech" },
            Note: "Microsoft."),

        new Component(
            "QRCoder",
            "1.6.0",
            "MIT",
            "Draws the QR codes on check-in badges and the scan-to-join poster.",
            new[] { "QRCoder" }),

        new Component(
            "KokoroSharp",
            "0.8.4",
            "MIT",
            "Runs the Kokoro voice on this PC.",
            new[] { "KokoroSharp", "KokoroSharp.CPU" }),

        new Component(
            "Kokoro 82M",
            "v1.0",
            "Apache-2.0",
            "The Kokoro voice itself - the model that speaks the lines.",
            Array.Empty<string>(),
            Note: "By hexgrad. The voices (voices\\*.npy) ship beside the app with Kokoro's own LICENSE; the 310 MB model is downloaded from Voice Settings, only by a club that chooses this voice."),

        new Component(
            "MisakiSharp",
            "2.1.1",
            "Apache-2.0",
            "Works out how the Kokoro voice should pronounce a line.",
            new[] { "MisakiSharp" },
            Note: "A C# port of hexgrad's misaki, with no espeak-ng in it - so no GPL code comes in with it. Its NOTICE file, which Apache-2.0 asks to be passed on, is not in the package; take it from the project."),

        new Component(
            "ONNX Runtime",
            "1.22.0",
            "MIT",
            "Does the work of running the Kokoro voice model.",
            new[] { "Microsoft.ML.OnnxRuntime", "Microsoft.ML.OnnxRuntime.Managed" },
            Note: "Microsoft. Its own ThirdPartyNotices.txt lists no copyleft component."),

        new Component(
            "NumSharp",
            "0.30.0",
            "Apache-2.0",
            "Reads the Kokoro voice files.",
            new[] { "NumSharp" },
            Note: "SciSharp STACK."),

        new Component(
            "OpenTK",
            "5.0.0-pre.13",
            "MIT",
            "Audio playback KokoroSharp brings with it.",
            new[] { "OpenTK.Audio.OpenAL", "OpenTK.Core", "OpenTK.Mathematics" },
            Note: "The packages state no licence at all - this is the project's own. Confirm the text before the installer ships."),

        new Component(
            "System.Numerics.Tensors",
            "9.0.5",
            "MIT",
            "Number crunching for the Kokoro voice.",
            new[] { "System.Numerics.Tensors" },
            Note: "Microsoft."),

        new Component(
            "Oxanium",
            string.Empty,
            "SIL Open Font License 1.1",
            "The typeface HeatCaller's headings and numbers are set in.",
            Array.Empty<string>(),
            Note: "Its licence file travels with it, which the OFL requires: Assets\\Fonts\\OFL-Oxanium.txt."),

        new Component(
            "Barlow",
            string.Empty,
            "SIL Open Font License 1.1",
            "The typeface everything else is set in.",
            Array.Empty<string>(),
            Note: "Its licence file travels with it, which the OFL requires: Assets\\Fonts\\OFL-Barlow.txt."),
    };

    /// <summary>Every NuGet package id the list covers, for the test that holds it to what ships.</summary>
    internal static IEnumerable<string> PackageIds => Components.SelectMany(component => component.Packages);

    /// <summary>"NAudio 2.3.0 - MIT", or "Barlow - SIL Open Font License 1.1" for one with no version.</summary>
    internal static string Headline(Component component) => component.Version.Length == 0
        ? $"{component.Name} - {component.License}"
        : $"{component.Name} {component.Version} - {component.License}";
}

/// <summary>
/// About HeatCaller: what this is, which build it is, where to write when it
/// goes wrong, where the guides are, and what ships inside it that somebody
/// else wrote.
///
/// Opened from the Getting Help card on Backup &amp; Restore. There is no
/// menu bar to hang it off - every page in this app is in the sidebar - and
/// Getting Help is already the one place the app answers "where do I go when
/// something is wrong": the crash dialog and the console's right-click menu
/// both point at it, and the sidebar search finds that page for "support"
/// and "help". So the About box lives there too, beside Save Support Info,
/// rather than on a page of its own that a club would meet once.
///
/// On the layout pieces (DialogFit), the way the glossary is
/// (RacingWordsForm): the name, version and a line about the app across the
/// top with the support address and the guides under them, then the
/// components taking the height the window has and scrolling on their own,
/// and Close at the foot where it stays in view.
/// </summary>
public sealed class AboutForm : Form
{
    /// <summary>The components show at least this many lines before the window scrolls instead.</summary>
    private const int LeastLines = 8;

    /// <summary>
    /// How narrow the window can be made inside its frame, at the normal
    /// text size. Nothing in it is measured for its width - every line
    /// wraps to the room it has.
    /// </summary>
    private const int LeastWidth = 480;

    public AboutForm()
    {
        Text = "About HeatCaller";
        Font = AppTheme.BodyFont;

        var lblName = new Label
        {
            Text = "HeatCaller",
            AutoSize = true,
            Font = AppTheme.TitleFont,
            ForeColor = AppTheme.TextDark,
        };

        var lblVersion = new Label
        {
            Text = AppVersion.Short,
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        var lblWhat = new Label
        {
            Text = "Runs an RC race night from one laptop: the timing, the race itself, the announcer, "
                   + "the results and the championship.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextDark,
        };

        // The address and the guides as links: one opens the club's email
        // program, the other a browser. Copy Address is beside them because
        // a track PC often has no email program set up at all, and the
        // address is then wanted on a phone instead - the same pairing the
        // club page gets on Club Website.
        var lnkEmail = Link(AppSupport.EmailSentence, AppSupport.Email, AppSupport.MailTo);
        var lnkDocs = Link($"Guides and help: {AppSupport.DocsText}", AppSupport.DocsText, AppSupport.DocsUrl);

        var btnCopyEmail = new Button { Text = "Copy Address" };
        btnCopyEmail.Click += (_, _) => AppSupport.CopyEmail();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        // The components scroll down on their own, as wide as the lines over
        // them: no margins of their own - the page's are round them - and no
        // room under the last line, which the theme pass gives a panel that
        // scrolls (ScrollTheming.NoRoomBelow).
        var components = new PageStack { Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(components);
        BuildComponents(components);

        var page = new FillPage();
        page.Add(lblName, gapBelow: 2);
        page.Add(lblVersion);
        page.Add(lblWhat);
        page.Add(FieldGrid.Inline(lnkEmail, btnCopyEmail));
        page.Add(lnkDocs);

        page.Add(new Label
        {
            Text = "WHAT SHIPS INSIDE HEATCALLER",
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        }, gapBelow: 2);

        // The components take the height; they keep a few lines before the
        // window scrolls instead.
        page.AddFill(components, _ => LeastLines * Pieces.LineHeight(Font), gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, as a dialog's buttons go.
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, which gives every label the page's colour.
        lblVersion.ForeColor = AppTheme.Primary;

        DialogFit.Keep(this, page, width: 620, height: 600);

        // And it can be made narrower than it opened, down to LeastWidth at
        // the text size in use, as the glossary can: every line in it wraps
        // to what it has. After DialogFit's own Load handler, which sets the
        // least to the width it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, Width - ClientSize.Width + UiLayout.AtTextSize(LeastWidth)),
            MinimumSize.Height);
    }

    /// <summary>Opens it, owned, so it centres on the page that asked.</summary>
    public static void ShowFor(IWin32Window? owner)
    {
        using var form = new AboutForm();
        form.ShowDialog(owner);
    }

    /// <summary>
    /// A sentence with one part of it as a link - the address in "email
    /// support@... and attach", so the sentence reads as a sentence and the
    /// address is still clickable. The whole sentence when the part is not
    /// in it, which cannot happen but would otherwise leave a dead link
    /// over the first few letters.
    /// </summary>
    private static LinkLabel Link(string sentence, string part, string target)
    {
        var at = sentence.IndexOf(part, StringComparison.Ordinal);

        var link = new LinkLabel
        {
            Text = sentence,
            AutoSize = true,
            Font = AppTheme.BodyFont,
            LinkArea = at >= 0 ? new LinkArea(at, part.Length) : new LinkArea(0, sentence.Length),
            LinkColor = AppTheme.Accent,
            ActiveLinkColor = AppTheme.Accent,
            VisitedLinkColor = AppTheme.Accent,
        };

        link.LinkClicked += (_, _) => AppSupport.Open(target);

        return link;
    }

    /// <summary>
    /// The list, down <paramref name="components"/>: each component's name,
    /// version and licence in bold, what it is for under that, and the note
    /// under that when it has one. Wrapping to the window's width, with a
    /// row's gap between one component and the next and nothing under the
    /// last - the last part of a column that scrolls, whose gap would be
    /// scrolled to as blank room.
    /// </summary>
    private static void BuildComponents(PageStack components)
    {
        var all = ThirdPartyCatalog.Components;

        for (var i = 0; i < all.Count; i++)
        {
            var component = all[i];
            var last = i == all.Count - 1;

            components.Add(new Label
            {
                Text = ThirdPartyCatalog.Headline(component),
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = AppTheme.TextDark,
            }, gapBelow: UiLayout.HintGap);

            // The line under the last part of a component carries the gap to
            // the next one; the very last part of the column carries none.
            var between = UiLayout.RowGap + 2;

            components.Add(new Label
            {
                Text = component.What,
                AutoSize = true,
                Font = AppTheme.BodyFont,
                ForeColor = AppTheme.TextDark,
            }, gapBelow: component.Note is not null
                ? UiLayout.HintGap
                : last ? 0 : between);

            if (component.Note is null)
            {
                continue;
            }

            components.Add(new Label
            {
                Text = component.Note,
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: last ? 0 : between);
        }
    }
}
