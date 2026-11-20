import { SHARED_ASSETS } from "../src/pages.js";

/**
 * THE SCRIPT A PAGE RUNS, AS ONE (19 Nov 2026, driver phone plan item 1
 * step 6). A club page no longer carries the shared script inline: it names
 * /c/<club>/app.js?v=... and keeps only its own lines inline. So a test that
 * reads or runs a page's script gets the shared app.js (when the page names
 * it) followed by every inline script, in the order the browser runs them -
 * the same text the one inline script used to hold.
 *
 * In the shape RegExp.prototype.exec gives - [whole, script] - so it stands
 * in for the old /<script[^>]*>([\s\S]*)<\/script>/.exec(html); null when
 * the page has no script at all.
 */
export function pageScriptMatch(html: string): [string, string] | null {
  const tags = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)];
  if (tags.length === 0) return null;

  const parts: string[] = [];
  for (const tag of tags) {
    const src = /\bsrc="([^"]*)"/.exec(tag[1] ?? "")?.[1];
    if (src === undefined) {
      parts.push(tag[2] ?? "");
    } else if (/^\/c\/[^/]+\/app\.js\?v=/.test(src)) {
      parts.push(SHARED_ASSETS["app.js"]?.body ?? "");
    }
  }

  return [html, parts.join("\n")];
}

/**
 * The page as it would read with the shared files written into it, the way
 * every page was before 19 Nov 2026: app.css's <link> becomes a <style>
 * holding it, and app.js's tag joins the page's own script, so the page has
 * one inline script again - the shared lines first, then the page's own.
 * For tests that look for a shared rule or helper in a page's text.
 */
export function withSharedInline(html: string): string {
  return html
    .replace(/<link rel="stylesheet" href="\/c\/[^/"]+\/app\.css\?v=[^"]*">/g, () => `<style>${SHARED_ASSETS["app.css"]?.body ?? ""}</style>`)
    .replace(/<script src="\/c\/[^/"]+\/app\.js\?v=[^"]*" nonce="([^"]*)"><\/script>\n<script nonce="([^"]*)">/g,
      (_all, _a: string, nonce: string) => `<script nonce="${nonce}">${SHARED_ASSETS["app.js"]?.body ?? ""}`);
}

/** The page's markup with every <script> tag taken out - what the old exec's match[0] left behind when replaced. */
export function pageWithoutScripts(html: string): string {
  return html.replace(/<script\b[^>]*>[\s\S]*?<\/script>\n?/g, "");
}

/** The page's script as one string, or "" with none (see pageScriptMatch). */
export function pageScriptOf(html: string): string {
  return pageScriptMatch(html)?.[1] ?? "";
}

/** The page's style as one: the shared app.css (when the page names it), then every inline <style>. */
export function pageStyleOf(html: string): string {
  const parts: string[] = [];
  for (const tag of html.matchAll(/<link rel="stylesheet" href="([^"]*)">|<style\b[^>]*>([\s\S]*?)<\/style>/g)) {
    if (tag[1] !== undefined) {
      if (/^\/c\/[^/]+\/app\.css\?v=/.test(tag[1])) parts.push(SHARED_ASSETS["app.css"]?.body ?? "");
    } else {
      parts.push(tag[2] ?? "");
    }
  }
  return parts.join("\n");
}
