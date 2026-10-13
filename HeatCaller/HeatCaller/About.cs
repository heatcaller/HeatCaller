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
/// The voice stack is permissive throughout: the Kokoro voice is MIT (the
/// code) over Apache-2.0 (the model and its voices), and there is no copy
/// of espeak-ng - GPL-3.0 - inside the installer, which is the thing that
/// would have put the whole download under the GPL. So the 310 MB model can
/// be bundled.
///
/// ONE COPYLEFT COMPONENT DOES SHIP, from 0.60.0: LibVLC and LibVLCSharp,
/// both LGPL-2.1-or-later, which play sponsor VIDEO slides on the Big
/// Screen (see SponsorVideo.cs). The line that used to stand here said
/// every licence was permissive and no copyleft binary shipped; that was
/// true until videos were added and is not true now.
///
/// The LGPL is a weak copyleft and HeatCaller meets it the way it is
/// designed to be met, but the conditions are real and worth stating
/// rather than filing away:
///
///   - HeatCaller is NOT a derived work of LibVLC. It links to it
///     dynamically - LibVLCSharp is P/Invoke over libvlc.dll - so section
///     6 applies and HeatCaller's own source stays HeatCaller's.
///   - The licence text must travel with the binary. Neither package
///     carries one, so it was fetched from the FSF on 8 Oct 2026 and now
///     ships as Assets\Licences\LibVLC-LGPL-2.1.txt.
///   - A club must be able to REPLACE the library. It can: libvlc.dll,
///     libvlccore.dll and plugins\ sit in libvlc\win-x64 beside the exe as
///     ordinary files, not packed into it, so dropping in another build of
///     the same version works. Nothing here must ever start linking LibVLC
///     statically or packing it into a single-file publish without taking
///     that obligation seriously first - which is one more reason
///     PublishLayoutTests pins the folder it lands in.
///   - VLC's plugin set is trimmed in HeatCaller.csproj. Removing files
///     from an unmodified library is a choice about what to distribute,
///     not a modification of it, and every plugin that ships is byte for
///     byte VideoLAN's.
///
/// This is a straightforward LGPL dynamic-linking case rather than an open
/// question like the MisakiSharp one below, but it is still a question for
/// somebody qualified to sign off before the public beta, and it is now on
/// the list of what to show them.
///
/// ONE THING IS NOT SETTLED, found on 6 Oct 2026 when MisakiSharp's NOTICE
/// was finally fetched (Assets\Licences\MisakiSharp-NOTICE.txt, and read it
/// rather than this summary). MisakiSharp is Apache-2.0 and ships no
/// copyleft code, but its own NOTICE says two of its parts were derived from
/// copyleft sources rather than merely behaving like them:
///
///   - "num2words (LGPL-2.1) - the English number verbalization logic was
///     ported from its sources". English number verbalization is on
///     HeatCaller's hot path: it is what turns 3 into "third" and 1:04.2
///     into a lap time read aloud.
///   - "phonemizer (GPL-3.0) - EspeakG2P/EspeakFallback reproduce its espeak
///     backend behavior ... via misaki". Those two classes are the espeak
///     path; whether anything in HeatCaller can reach them has not been
///     traced.
///
/// So "no espeak-ng in it" is true and still not the whole question, and the
/// line that used to stand here - nothing in here is copyleft - was resting
/// on it. This is upstream's characterisation of upstream's own code, and
/// what it means for a redistributed binary is a question for somebody
/// qualified to answer it, before the public beta. It is not a reason to
/// stop: MisakiSharp's own licence grant is Apache-2.0.
///
/// The conditions that do travel with HeatCaller are attribution ones, plus
/// the LGPL ones above: the OFL licence files must stay beside the fonts
/// (Assets\Fonts\OFL-*.txt, and the csproj copies them), the Apache-2.0
/// components want their licence text passed on - which is why Kokoro's own
/// LICENSE lands in voices\ beside the exe - and Apache-2.0 4(d) wants
/// MisakiSharp's NOTICE passed on, which is why Assets\Licences\ now ships
/// beside the exe too.
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
            Note: "By James F. Bellinger, copyright 2007-2025. The package carries no licence file, only a dead link; confirmed 6 Oct 2026 from the author's own page (software.seekye.com/hidsharp, where zer7.com/software/hidsharp now redirects), which states the Apache licence without naming a version. The package's old licenseUrl pointed at Apache-2.0."),

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
            "Still in the install, but nothing uses it now: the free Windows voice it ran was retired on 10 Oct 2026, and Kokoro speaks in its place.",
            new[] { "System.Speech" },
            Note: "Microsoft."),

        new Component(
            "QRCoder",
            "1.6.0",
            "MIT",
            "Draws the QR codes on check-in badges and the scan-to-join poster.",
            new[] { "QRCoder" }),

        new Component(
            "Velopack",
            "1.2.161",
            "MIT",
            "Installs HeatCaller and brings it its updates.",
            new[] { "Velopack" },
            Note: "Velopack Ltd, Caelan Sayler and Kevin Bost. Missing from this list until 8 Oct 2026 - it shipped from the day the installer was built and was in neither this catalogue nor the audited list in BugHunt4AboutTests, so the test that holds the two together passed by agreeing about nothing. It has no dependencies of its own on this target framework; the Newtonsoft.Json and Microsoft.Win32.Registry its package names are for .NET Framework and .NET Standard, not net10.0."),

        new Component(
            "LibVLCSharp",
            "3.10.1",
            "LGPL-2.1-or-later",
            "Plays sponsor video slides on the Big Screen.",
            new[] { "LibVLCSharp", "LibVLCSharp.WinForms" },
            Note: "VideoLAN. The packages state the licence as an expression and carry no licence file; the text ships as Assets\\Licences\\LibVLC-LGPL-2.1.txt, shared with LibVLC below. Linked dynamically - see the LGPL note at the top of this file."),

        new Component(
            "LibVLC",
            "3.0.24",
            "LGPL-2.1-or-later",
            "VLC itself - the decoders a sponsor's video is actually played by.",
            new[] { "VideoLAN.LibVLC.Windows" },
            Note: "VideoLAN. Ships unmodified in libvlc\\win-x64 beside the exe as replaceable DLLs, which is what section 6 of the LGPL asks for; only the win-x64 copy and a trimmed plugin set are distributed (see HeatCaller.csproj). Its licence text is Assets\\Licences\\LibVLC-LGPL-2.1.txt, fetched from the FSF on 8 Oct 2026 because neither package carries one."),

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
            Note: "By hexgrad. The voices (voices\\*.npy) ship beside the app with Kokoro's own LICENSE; the 310 MB model (kokoro.onnx) ships beside HeatCaller.exe too, in the installer. A club can also download it again from Voice Settings, into LocalAppData, if the bundled copy will not load."),

        new Component(
            "MisakiSharp",
            "2.1.1",
            "Apache-2.0",
            "Works out how the Kokoro voice should pronounce a line.",
            new[] { "MisakiSharp" },
            Note: "A C# port of hexgrad's misaki, with no espeak-ng binary in it. Its NOTICE, which Apache-2.0 4(d) asks to be passed on, is not in the package - fetched from the project on 6 Oct 2026 and now shipped as Assets\\Licences\\MisakiSharp-NOTICE.txt. Read it: it names num2words (LGPL-2.1) and phonemizer (GPL-3.0) as sources two of its parts were ported from. See the note at the top of this file."),

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
            Note: "The packages state no licence at all. Confirmed 6 Oct 2026 from the project's own LICENSE.md: MIT, \"Copyright (c) 2006-2019 Stefanos Apostolopoulos for the Open Toolkit project\". MIT wants that notice passed on, so it ships as Assets\\Licences\\OpenTK-LICENSE.txt. Still a pre-release (5.0.0-pre.13), pulled in by KokoroSharp."),

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
