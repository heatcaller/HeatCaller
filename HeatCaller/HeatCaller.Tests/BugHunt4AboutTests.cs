namespace HeatCaller.Tests;

/// <summary>
/// The About box and the support address, from the 5 Oct 2026 pass before
/// the installer: the third-party licence list held to every package that
/// ships and to no component without a licence, and the support address
/// held to the two places the app has to say it - the Getting Help card on
/// Backup &amp; Restore and the crash dialog.
///
/// The list is data (ThirdPartyCatalog) rather than wording inside the
/// About box, which is what lets this test read it at all.
/// </summary>
public sealed class BugHunt4AboutTests
{
    /// <summary>
    /// Every NuGet package whose bits ship with HeatCaller, as the licence
    /// audit on 5 Oct 2026 found them: HeatCaller.csproj's own
    /// PackageReferences and every transitive package in
    /// HeatCaller\obj\project.assets.json that contributes a file to the
    /// build output.
    ///
    /// The sixteen runtime.&lt;platform&gt;.runtime.native.System.IO.Ports
    /// packages are deliberately not here: they only help NuGet pick a
    /// native file per platform and contribute nothing on Windows (the
    /// Windows copy comes from System.IO.Ports itself). The
    /// runtime.native.System.IO.Ports meta-package is, because it is the one
    /// System.IO.Ports depends on by name.
    ///
    /// Typed out rather than read from project.assets.json: a test that
    /// parses the build's own files would pass whatever the build happened
    /// to contain, and the point of this one is to notice when what ships
    /// stops matching what was audited. When a PackageReference changes,
    /// both this list and ThirdPartyCatalog change with it.
    /// </summary>
    private static readonly string[] PackagesThatShip = new[]
    {
        "HidSharp",
        "KokoroSharp",
        "KokoroSharp.CPU",
        "Microsoft.ML.OnnxRuntime",
        "Microsoft.ML.OnnxRuntime.Managed",
        "MisakiSharp",
        "NAudio",
        "NAudio.Asio",
        "NAudio.Core",
        "NAudio.Midi",
        "NAudio.Wasapi",
        "NAudio.WinForms",
        "NAudio.WinMM",
        "NumSharp",
        "OpenTK.Audio.OpenAL",
        "OpenTK.Core",
        "OpenTK.Mathematics",
        "QRCoder",
        "System.IO.Ports",
        "System.Numerics.Tensors",
        "System.Speech",
        "runtime.native.System.IO.Ports",
    };

    // --- The licence list ------------------------------------------------------------------

    [Fact]
    public void Every_package_that_ships_with_the_app_is_in_the_licence_list()
    {
        var listed = ThirdPartyCatalog.PackageIds.ToHashSet(StringComparer.Ordinal);

        var missing = PackagesThatShip.Where(id => !listed.Contains(id)).ToList();

        Assert.True(missing.Count == 0,
            "Ships with the app but has no licence in ThirdPartyCatalog: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_licence_list_claims_no_package_that_does_not_ship()
    {
        var ships = PackagesThatShip.ToHashSet(StringComparer.Ordinal);

        var extra = ThirdPartyCatalog.PackageIds.Where(id => !ships.Contains(id)).ToList();

        Assert.True(extra.Count == 0,
            "In ThirdPartyCatalog but not among the packages that ship: " + string.Join(", ", extra));
    }

    [Fact]
    public void No_package_is_listed_twice()
    {
        var twice = ThirdPartyCatalog.PackageIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(twice.Count == 0, "Listed under more than one component: " + string.Join(", ", twice));
    }

    [Fact]
    public void No_component_has_a_blank_licence()
    {
        var blank = ThirdPartyCatalog.Components
            .Where(component => string.IsNullOrWhiteSpace(component.License))
            .Select(component => component.Name)
            .ToList();

        Assert.True(blank.Count == 0, "No licence given: " + string.Join(", ", blank));
    }

    [Fact]
    public void Every_component_says_what_it_is_and_what_it_is_for()
    {
        foreach (var component in ThirdPartyCatalog.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(component.Name));
            Assert.False(string.IsNullOrWhiteSpace(component.What));
        }
    }

    [Fact]
    public void Every_component_that_is_a_package_says_which_version_ships()
    {
        var noVersion = ThirdPartyCatalog.Components
            .Where(component => component.Packages.Count > 0 && string.IsNullOrWhiteSpace(component.Version))
            .Select(component => component.Name)
            .ToList();

        Assert.True(noVersion.Count == 0, "A package with no version: " + string.Join(", ", noVersion));
    }

    [Fact]
    public void The_fonts_are_listed_under_the_open_font_license()
    {
        // Both OFL files have to travel with the fonts when the app is
        // redistributed (Assets\Fonts\README.txt says so, and the csproj
        // copies them), so the About box has to name the licence they are
        // under.
        foreach (var family in new[] { "Oxanium", "Barlow" })
        {
            var font = ThirdPartyCatalog.Components.SingleOrDefault(component => component.Name == family);

            Assert.NotNull(font);
            Assert.Contains("Open Font License", font!.License, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_Kokoro_voice_model_is_marked_as_not_shipping_with_the_app()
    {
        // The 310 MB model is downloaded from Voice Settings by a club that
        // chooses the Kokoro voice (KokoroSpeech), so the installer does not
        // carry it. Everything else on the list does.
        var kokoro = ThirdPartyCatalog.Components.Single(component => component.Name == "Kokoro 82M");

        Assert.Equal("Apache-2.0", kokoro.License);

        var notShipping = ThirdPartyCatalog.Components.Where(component => !component.Ships).ToList();

        Assert.DoesNotContain(notShipping, component => component.Name != "Kokoro 82M");
    }

    [Fact]
    public void Nothing_on_the_licence_list_is_copyleft()
    {
        // The reason the whole audit was done: the owner is deciding whether
        // to bundle the Kokoro model in the installer, and one GPL or LGPL
        // component in that stack would decide it for them. Nothing in it is
        // - MisakiSharp is a port with no espeak-ng (GPL-3.0) in it - and
        // this fails the day something changes that.
        foreach (var component in ThirdPartyCatalog.Components)
        {
            Assert.DoesNotContain("GPL", component.License, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mozilla Public", component.License, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Eclipse Public", component.License, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_components_headline_reads_as_its_name_version_and_licence()
    {
        var naudio = ThirdPartyCatalog.Components.Single(component => component.Name == "NAudio");
        var barlow = ThirdPartyCatalog.Components.Single(component => component.Name == "Barlow");

        Assert.Equal("NAudio 2.3.0 - MIT", ThirdPartyCatalog.Headline(naudio));

        // No version, so no empty gap where one would have been.
        Assert.Equal("Barlow - SIL Open Font License 1.1", ThirdPartyCatalog.Headline(barlow));
    }

    // --- The support address ---------------------------------------------------------------

    [Fact]
    public void The_support_address_is_the_one_that_is_live()
    {
        Assert.Equal("support@heatcaller.com", AppSupport.Email);
        Assert.Equal("mailto:support@heatcaller.com", AppSupport.MailTo);
    }

    [Fact]
    public void The_Getting_Help_wording_says_the_support_address()
    {
        // The sentence under Save Support Info on Backup & Restore's Getting
        // help card. It used to stop at making the file and never say where
        // to send it.
        Assert.Contains(AppSupport.Email, AppSupport.EmailSentence, StringComparison.Ordinal);
        Assert.Contains("support file", AppSupport.EmailSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_crash_dialog_says_the_support_address_and_where_the_log_went()
    {
        var crash = new InvalidOperationException("The timing loop went away.");

        var message = Program.CrashMessage(crash, @"C:\Users\club\AppData\Roaming\HeatCaller\crash.log");

        Assert.Contains(AppSupport.Email, message, StringComparison.Ordinal);
        Assert.Contains("Save Support Info", message, StringComparison.Ordinal);
        Assert.Contains(@"C:\Users\club\AppData\Roaming\HeatCaller\crash.log", message, StringComparison.Ordinal);
        Assert.Contains("The timing loop went away.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_About_box_has_a_version_to_show_and_somewhere_to_send_people()
    {
        // The box reads these rather than holding its own copies.
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Short));
        Assert.Contains(AppVersion.Number, AppVersion.Short, StringComparison.Ordinal);
        Assert.StartsWith("https://", AppSupport.DocsUrl, StringComparison.Ordinal);
        Assert.Contains(AppSupport.DocsText, AppSupport.DocsUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_About_box_is_on_the_list_of_windows_the_layout_test_holds_to_its_size()
    {
        Assert.Contains("About HeatCaller", LayoutAudit.ConvertedWindows);
        Assert.True(LayoutAudit.IsConvertedWindow("About HeatCaller"));
    }
}
