namespace HeatCaller;

/// <summary>
/// THE ROOT ADDRESS (13 Nov 2026): a short list of the pages this laptop
/// serves on the club's Wi-Fi.
///
/// The root used to be Live Results. That page and the phone coach page
/// were taken off the LAN (Asher, 13 Nov 2026), the results table living
/// on only as the OBS overlay at /overlay. Anybody who still types the bare
/// address gets a page of links rather than a 404 - the address is the one
/// the app has always printed, and it is on posters.
///
/// Only addresses, nothing behind them: Remote and Manage still ask for
/// their own PINs when opened, so listing them gives nothing away. The
/// photo-finish camera is left off - it answers on this PC only.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>The OBS overlay's address - the results table, see-through, with no chrome.</summary>
    internal const string OverlayPath = "/overlay";

    /// <summary>The page list, in the order a club meets them on a race night.</summary>
    internal static readonly IReadOnlyList<(string Path, string Name, string What)> IndexPages =
    [
        ("/checkin", "Driver check-in", "Drivers check themselves in for tonight."),
        ("/shoutout", "Message the announcer", "A birthday, a dedication, a first race."),
        ("/vote", "Vote", "The room's vote, when one is running."),
        ("/remote", "Race control remote", "Start, stop and call races from a phone. Asks for the remote PIN."),
        ("/manage", "Manage", "Manager tools. Asks for the manager PIN."),
        ("/broadcast", "Broadcast graphics", "For OBS."),
        (OverlayPath, "OBS overlay", "The results table, see-through, for OBS."),
    ];

    /// <summary>The page at the root address: a link to each LAN page.</summary>
    internal string BuildIndexPage()
    {
        var look = CurrentPageLook();

        var links = string.Concat(IndexPages.Select(p =>
            $"""<li><a href="{p.Path}"><span class="n">{p.Name}</span><span class="w">{p.What}</span></a></li>"""));

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>HeatCaller</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px; max-width:560px; margin:0 auto; }
        ul.pages { list-style:none; padding:0; margin:0; }
        ul.pages li { margin-bottom:10px; }
        ul.pages a { display:block; padding:14px 16px; border-radius:10px; text-decoration:none;
                     background:var(--surface); border:1px solid var(--border); color:var(--text); }
        ul.pages .n { display:block; font-size:17px; font-weight:700; color:var(--accent); }
        ul.pages .w { display:block; font-size:13px; color:var(--muted); margin-top:3px; line-height:1.4; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}PAGES ON THIS WI-FI</h1></div>
        <div class="wrap">
          <ul class="pages">{{links}}</ul>
        </div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>{{LookWatchScript}}</script>
        </body></html>
        """;
    }
}
