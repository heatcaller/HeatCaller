namespace HeatCaller;

/// <summary>
/// Where a club writes when something goes wrong, and where the guides are.
///
/// One place, because the address has to read the same wherever the app says
/// it: the Getting Help card on Backup &amp; Restore, the crash dialog, and
/// the About box. An address typed into three screens is an address that
/// ends up spelled three ways the day it changes.
/// </summary>
internal static class AppSupport
{
    /// <summary>The club's support address. Live since 30 Sep 2026.</summary>
    internal const string Email = "support@heatcaller.com";

    /// <summary>
    /// The guides. The documentation site has no published address of its
    /// own yet - mkdocs.yml still has site_url commented out - so this is
    /// the domain the site will go up on; point it at the site itself once
    /// it is there, and every screen follows.
    /// </summary>
    internal const string DocsUrl = "https://heatcaller.com";

    /// <summary>The docs address as it is shown, without the scheme in front of it.</summary>
    internal const string DocsText = "heatcaller.com";

    /// <summary>
    /// The user guide itself.
    ///
    /// WHY (25 Nov 2026, Asher: "put the guide online and link the app to
    /// it"): the guide is up at heatcaller.com/docs/, built from website/docs
    /// by website/build-site.ps1. Every way into it from the app opens this
    /// address, or a page under it (GuidePages). DocsUrl and DocsText stay as
    /// they were: DocsText is also the mark on emails and results
    /// (HeatCallerMark), where the plain web address is right.
    /// </summary>
    internal const string GuideUrl = "https://heatcaller.com/docs/";

    /// <summary>Opens a guide address - swapped by the tests, so pressing a button in one opens no browser.</summary>
    internal static Action<string> OpenGuide { get; set; } = Open;

    /// <summary>
    /// What the Getting Help card says under the Save Support Info button,
    /// and what the About box says: one sentence, the address in it.
    /// </summary>
    internal const string EmailSentence =
        "When something goes wrong, email " + Email + " and attach the support file.";

    /// <summary>A mailto: for the address, for a link that opens the club's email program.</summary>
    internal const string MailTo = "mailto:" + Email;

    /// <summary>
    /// Opens <paramref name="target"/> in whatever Windows uses for it - the
    /// club's email program for the address, a browser for the guides.
    /// Nothing happens when there is none: a track PC often has no email
    /// program set up at all, which is no reason to break the screen the
    /// address is written on. The address is on it to be read either way,
    /// and Copy Address is beside it.
    /// </summary>
    internal static void Open(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Puts the support address on the clipboard. Another program holding
    /// the clipboard open is enough to make this throw, and there is nothing
    /// to say that the address on screen does not already say.
    /// </summary>
    internal static void CopyEmail()
    {
        try
        {
            Clipboard.SetText(Email);
        }
        catch (Exception)
        {
        }
    }
}
