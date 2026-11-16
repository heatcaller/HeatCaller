/**
 * THE COOKIE POLICY, ON THE CLUB'S OWN PAGES (15 Nov 2026, Asher: "Yes, add
 * the link", then "fix and address all points the attorney brought up").
 *
 * THIS IS THE POLICY. It is written for whoever opens a club's pages - a
 * driver, a parent, a visitor - and served at /c/<club>/cookies, linked
 * from every page's footer. Part 3 of the terms a club accepts in
 * HeatCaller (HeatCaller/HeatCaller/Assets/Legal/HeatCaller-Terms.md) only
 * points here and sets out the club's side, so changing these words does
 * not make every club accept the terms again.
 *
 * KEPT TRUE BY TESTS. test/cookies-page.test.ts reads the page code and
 * fails when it keeps something in the browser under a name this page does
 * not list, so a new item cannot ship without a line here. When this text
 * changes, bump COOKIE_POLICY_VERSION and add a line under "What changed".
 *
 * Open legal questions are in website/legal-drafts/cookie-policy-questions.md.
 */

/** The policy's version and date, shown at its top and named by Part 3 of the terms. */
export const COOKIE_POLICY_VERSION = "Version 3, 16 November 2026";

/** The policy as the page shows it - static, no driver or club data in it. */
export const COOKIE_POLICY_HTML = `<div class="policy">
<p class="when">${COOKIE_POLICY_VERSION}</p>

<p><b>Who this is from.</b> These pages are made and run by HeatCaller LLC, a New York company (in this policy, "we" and "us"), for the club whose name is at the top of the page. We decide what the pages keep in your browser, so this policy is ours. The club decides what it publishes about its drivers: ask the club about that, or email us.</p>

<p><b>The short version.</b> The pages use one small cookie, for the colour theme you pick, and your browser's own storage to remember things you asked for and to keep the pages working when the signal drops. They send us a few anonymous counts of how the pages are used, with nothing about who you are. There is no advertising, no tracking across other websites, no profiling and no third-party cookies set by us. Nothing is sold or shared for advertising. The pages do not ask you to accept anything: we have kept only what they need for the things they do for you, and stopped keeping what they did not.</p>

<h2>Children</h2>
<p>Many drivers are children. The pages keep nothing about a child that the pages do not need to work: no visit counts, no advertising ids, and nothing used to contact them unless they or a parent turn on alerts. Signing out on the phone removes what was kept about the driver. A parent or guardian can ask us about anything the pages keep, or ask the club about their child's information.</p>

<h2>The cookie</h2>
<ul>
<li><b>hg-theme</b>: the colour theme picked on the <b>Look</b> button (for example "pitlane"), so the next page opens in that look instead of flashing the club's colours first. Only the theme's name, never anything about a driver. Set by us, sent only to the club pages on this relay, and kept for one year or until the theme is set back.</li>
</ul>

<h2>What the pages keep in your browser</h2>
<p>Browsers let a page keep small notes on the device (local and session storage). The page reads them on the device; they are not sent to us the way a cookie is. "This tab" means the note goes when the tab is closed. "This phone" means it stays until you sign out, clear the site's data, or the browser removes it. In each name, &lt;club&gt; is the club's short name.</p>

<p><b>Signing in</b> (needed to show a driver their own night, or a manager the club's tools):</p>
<ul>
<li><b>hg-me-&lt;club&gt;</b>, <b>hg-me-list-&lt;club&gt;</b> and <b>hg-me-was-&lt;club&gt;</b> (this phone): the code that opens a driver's night, the drivers signed in on this phone (up to five, with the name each night gave) and a marker that a driver was signed in. Until <b>Sign out</b>.</li>
<li><b>hg-me-at-&lt;club&gt;</b> (this phone): when each driver on this phone was signed in, and whether they are a junior, so a junior is signed out by themselves 30 days after signing in. Removed with the driver at <b>Sign out</b>.</li>
<li><b>hg-night-pass-&lt;club&gt;</b> (this tab): the pass that shows <b>Your details</b> and the <b>Garage</b>.</li>
<li><b>hg-me-out-&lt;club&gt;</b> (this tab): a note that this tab signed out, so Back goes to the PIN.</li>
<li><b>hg-pin-&lt;club&gt;</b> (this tab): on the check-in page only, the PIN typed (a driver's or the desk's), with the pass it earned and whether it was a driver's. Gone when the tab closes; a driver's goes at <b>Sign out</b>.</li>
<li><b>hg-signup-&lt;club&gt;</b> (this phone): the pass for one sign-up in progress, so its answer shows when you come back. It opens that sign-up and nothing else.</li>
<li><b>hg-manage-&lt;club&gt;</b> and <b>hg-manage-pinkey-&lt;club&gt;</b> (this tab): a manager's pass, and a key worked out from the manager PIN that the page signs its requests with. Anyone holding that key in the open tab could act as the manager, so close the tab or sign out on a shared phone. The PIN itself is not kept.</li>
<li><b>hg-manage-key-&lt;club&gt;</b> and <b>hg-manage-id-&lt;club&gt;</b> (this phone): a random id the phone puts on its requests to the club's computer, so its log can tell phones apart, and, if the club enrols the phone for managing, the key that lets the club's computer recognise it. Signing out keeps these; clearing the site's data removes them.</li>
</ul>

<p><b>Things you chose:</b></p>
<ul>
<li><b>hg-theme</b>, <b>hg-textsize</b> and <b>hg-contrast</b> (this phone): theme, text size and high contrast.</li>
<li><b>hg-follow-&lt;club&gt;</b> and <b>hg-follow-alerts-&lt;club&gt;</b> (this phone): the name of the driver you follow, and whether you get alerts for them.</li>
<li><b>hc-alerts:&lt;club&gt;</b>, <b>hc-alert-kinds:&lt;club&gt;</b> and <b>hc-alert-kinds-seen:&lt;club&gt;</b> (this phone): whether alerts are on, which kinds you want, and which kinds you have been offered.</li>
<li><b>hg-laptalk-&lt;club&gt;</b>, <b>hg-laptalk-short-&lt;club&gt;</b>, <b>hg-laptalk-say-&lt;club&gt;</b>, <b>hg-nighttalk-&lt;club&gt;</b>, <b>hg-nighttalk-short-&lt;club&gt;</b> and <b>hg-nighttalk-say-&lt;club&gt;</b> (this phone): whether lap times are read in your ear, and how.</li>
<li><b>hg-me-space</b>, <b>hg-night-order</b>, <b>hg-night-just-me</b>, <b>hg-night-hide-runs</b> and <b>hg-me-seen-&lt;club&gt;</b> (this phone): which part of Me was open, the order of its cards, what it shows, and what you have already seen there. Until <b>Sign out</b>.</li>
<li><b>hc-clubs</b> (this phone): the clubs where a driver is signed in on this phone (up to eight, club names only), so Me can link to them. A club comes off it when its last driver signs out.</li>
<li><b>hc-video-dock:&lt;club&gt;</b> (this phone): whether the live video player was open, small or closed.</li>
<li><b>hg-picks-&lt;club&gt;</b> (this phone): your picks in the <b>Who wins?</b> game and how they turned out.</li>
<li><b>hg-install-&lt;club&gt;</b> (this phone): that you said "Not now" (for 14 days) or "Don't ask again" to adding the club to your home screen.</li>
</ul>

<p><b>Keeping the pages working:</b></p>
<ul>
<li><b>hc-last-&lt;club&gt;:</b> (this phone): a copy of what the relay last sent each page, so it can show something with no signal. All of these copies go at <b>Sign out</b>.</li>
<li><b>hc-seen-&lt;club&gt;:</b> and <b>hc-reads-&lt;club&gt;:</b> (this tab): copies of recent answers, and which reads each page makes, so a page draws at once rather than blank.</li>
<li><b>hc-owe-&lt;club&gt;</b> (this tab): the entry fee banner for a signed-in driver who owes tonight's fee.</li>
<li><b>hg-me-replies-&lt;club&gt;</b> (this tab): the desk's answers you have already put away.</li>
<li><b>hg-lastrace-&lt;club&gt;</b> (this phone, 12 hours at most) and <b>hg-livepos-&lt;club&gt;</b> (this phone, until the next race): the race just finished, and the positions this page saw each lap for its chart.</li>
<li><b>hg-spseen-&lt;club&gt;</b>, <b>hc-guide-done:&lt;club&gt;</b> and <b>hc-updates-done:&lt;club&gt;</b> (this phone): which sponsor slides, guides and "what's new" notes have been seen, so they are not shown again and again.</li>
<li><b>hg-voter-&lt;club&gt;</b> (this phone): a random id made by the phone and sent with a vote, so two phones on the venue's one Wi-Fi count as two voters. It says nothing about the phone or its user, and the relay keeps it only scrambled with that race's own key.</li>
</ul>

<p><b>Removed by the pages.</b> Older versions kept <b>hg-visits-&lt;club&gt;</b> (a count of visits, for the home-screen offer) and <b>hg-charge-&lt;club&gt;</b> (a battery-charge timer). The pages now delete both whenever they are opened.</p>

<h2>Offline copies and the home-screen app</h2>
<p>The pages ask the browser to install a small helper (a service worker) for the club's pages. It keeps a copy of the club's public pages (Home, Live, Schedule, Results and its tabs, Tracks, Me, check-in), the club's icon and logo, in a store named <b>hc-pages-</b> followed by the version, so they open with no signal. It never keeps a driver's own night or garage, a sign-up, the manager's page or any answer from the relay's API. Each new version of the pages throws the old copies away.</p>

<h2>Alerts</h2>
<p>Alerts are off until you turn them on. When you do, your browser gives the relay a push address, made by your browser's push service (for example Google for Chrome, Apple for Safari, Mozilla for Firefox), and each alert passes through that service on its way to you. The relay keeps the address, and which alerts you asked for, until you turn alerts off or sign out, or the browser withdraws it, and forgets it after 180 days in which no alert reached it and it was not renewed. A junior can only be followed through a family link from their own phone, never by name. Clearing the site's data does not tell the relay, so turn alerts off first if you want the address gone at once; otherwise the relay forgets it when the push service says it no longer works.</p>

<h2>What reaches the relay</h2>
<ul>
<li><b>Your device's internet address</b>, with every request, as with any website. The relay holds it in memory, for ten minutes at most, only to limit how fast one address can try PINs or send requests, and does not write it down. Fly.io, which hosts the relay in the United States, also handles it to run its network, under its own privacy policy.</li>
<li><b>Anonymous counts</b>: a few named events (for example "lost signal and got it back", "the home-screen card was shown") counted per club per day, with no driver, device id, page address or internet address kept. Kept 90 days.</li>
<li>What you send yourself, such as a check-in, a sign-up, a vote or a request to the desk, as the club's privacy information describes.</li>
</ul>

<h2>Race video</h2>
<p>Race video is shown from YouTube in its privacy-enhanced mode (youtube-nocookie.com). Nothing loads from YouTube until you tap to watch, or until a later visit to Live if you left the player open there. Once the player loads, your browser contacts Google, which receives your internet address and may keep its own information in your browser under Google's privacy policy, which we do not control. YouTube's privacy-enhanced mode reduces this but does not stop it.</p>

<h2>Your choices</h2>
<ul>
<li><b>Sign out</b> on Me removes what the phone kept about that driver.</li>
<li>Clearing the site's data in your browser's settings removes every item above, apart from the push address the relay holds (see Alerts).</li>
<li>If you block cookies or storage, the pages still show results and races, but the theme flashes the club's colours first, you will need the PIN or code each time, sign-ups and votes may not carry over, and the pages cannot work with no signal.</li>
<li>Alerts can be turned off on the page or in your phone's settings.</li>
<li>The pages do not track you across websites or sell or share information for advertising, so there is nothing for Global Privacy Control or Do Not Track to switch off. If your browser sends either, we treat it as a request not to sell or share, which we never do.</li>
</ul>

<h2>Why we keep these, and your rights</h2>
<p>Each item is kept because it is needed for something the pages do for you; the anonymous counts and the offline copies are kept because we need to keep the pages working well. Where the law gives you rights over information about you (for example in the UK, the EU or California), you can ask us what we hold, to correct it or to delete it, by emailing us. Everything kept in your browser you can also remove yourself, as above. The relay is in the United States, so if you open the pages from elsewhere, what your browser sends goes there. If you are in the UK or the EU you can also complain to your data protection authority.</p>

<h2>What changed</h2>
<ul>
<li><b>Version 3, 16 November 2026</b>: a junior signed in on a phone is signed out after 30 days (hg-me-at-&lt;club&gt;); push addresses unused for 180 days are forgotten; juniors can't be followed by name.</li>
<li><b>Version 2, 15 November 2026</b>: every item named; offline copies, anonymous counts, internet addresses, push services and children added; the visit count for the home-screen offer stopped, and the list of clubs now kept only where a driver is signed in.</li>
<li><b>Version 1, 15 November 2026</b>: first version.</li>
</ul>
<p>When the pages start keeping anything new, this page changes first, with a new version and date here.</p>

<h2>Contact</h2>
<p>HeatCaller LLC, New York, USA. <a href="mailto:support@heatcaller.com">support@heatcaller.com</a></p>
<p class="when">This policy &copy; 2026 HeatCaller LLC.</p>
</div>`;
