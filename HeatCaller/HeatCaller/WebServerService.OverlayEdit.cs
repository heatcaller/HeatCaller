namespace HeatCaller;

/// <summary>
/// THE OVERLAY PAGE AS THE EDITOR'S PICTURE (17 Nov 2026,
/// plans/overlay-editor-mockups.html, option A "Studio").
///
/// The Stream overlays editor shows the real overlay page in a WebView2
/// (OverlayPreview), so what is placed there is what OBS draws - one page,
/// never a drawing of it that could drift. Two additions to the page make
/// that work, both read from its address and both doing nothing otherwise:
///
///   &amp;edit=1  a layer over the picture: a box round every graphic the
///            layout has on, its name, a dot to resize it from, a dashed
///            "ghost" for one that does not show in this part of the night,
///            and a red outline where two graphics cover each other. A drag,
///            a resize or an arrow key works out where the graphic now sits
///            (one of the nine places, and how far in from its edges, in vw
///            as OverlayLayouts.SlotCss writes it) and posts that to the
///            window. The page itself never saves anything: there is no
///            write over the LAN, only a message to the window that holds it.
///   &amp;hold=   the demo race keeps to one part of the night (see the page).
///
/// Inside the window, the page also says which part of the night it is
/// showing, so "Play the night" can light the tab it has reached.
///
/// /overlay-gallery (GalleryPage): every layout as a live thumbnail, for
/// picking a look - the editor shows it when the club has none of its own.
/// </summary>
public sealed partial class WebServerService
{
    internal const string OverlayGalleryPath = "/overlay-gallery";

    /// <summary>The editor's layer and the part-of-the-night report. Plain script, no braces of C#'s.</summary>
    internal const string OverlayEditScript = """
        (function(){
          const HOST = window.chrome && window.chrome.webview;
          function post(m){ try { if (HOST) HOST.postMessage(m); } catch (e) { /* the window has gone */ } }
          // Which part of the night the demo is in, for the window's tabs.
          if (HOST && DEMO && !PHONE){
            let told = '';
            setInterval(() => { if (PART !== told){ told = PART; post({ t: 'part', p: PART }); } }, 300);
          }
          if (params.get('edit') !== '1' || PHONE) return;
          // Something like a camera shot behind the graphics, as on the stream.
          document.documentElement.style.background = 'radial-gradient(120% 80% at 30% 110%, #3b4a2e 0%, transparent 60%), repeating-linear-gradient(-18deg, transparent 0 46px, rgba(255,255,255,.04) 46px 50px), linear-gradient(180deg, #26303a 0%, #3a4430 55%, #2d3624 100%)';

          const NAMES = { tower: 'Positions', battle: 'Battle box', fastest: 'Fastest lap', moment: 'Lead changes', grid: 'The grid',
            podium: 'The podium', champ: 'Championship', captions: 'Captions', status: 'Race and clock', ticker: 'Positions along the bottom',
            bug: 'Corner bug', sponsor: 'Sponsor logo', clublogo: 'Club logo', schedule: 'Up next', basics: 'New to RC racing?', mark: "HeatCaller's mark" };
          const ORDER = Object.keys(NAMES);
          let sel = params.get('sel') || '';
          const ghosts = params.get('ghosts') !== '0';

          const style = document.createElement('style');
          style.textContent =
            '#hc-edit{position:fixed;inset:0;z-index:2147483000;pointer-events:none;font:600 clamp(9px,1.1vw,13px) Segoe UI,system-ui,sans-serif}' +
            '#hc-edit .safe{position:absolute;left:2%;right:2%;top:3%;bottom:3%;border:1px dashed rgba(47,211,224,.45);border-radius:4px}' +
            '#hc-edit .bx{position:absolute;pointer-events:auto;cursor:grab;border-radius:4px;outline:1px solid rgba(255,255,255,.35);outline-offset:2px}' +
            '#hc-edit .bx:hover{outline-color:rgba(255,255,255,.8)}' +
            '#hc-edit .bx.gh{outline:1px dashed rgba(255,255,255,.35);background:rgba(255,255,255,.04)}' +
            '#hc-edit .bx.cl{outline:2px dashed #ff5252}' +
            '#hc-edit .bx.on{outline:2px solid #ff2e88;outline-offset:3px}' +
            '#hc-edit .nm{position:absolute;left:-3px;bottom:calc(100% + 3px);padding:.2em .5em;border-radius:4px;background:#000c;color:#fff;white-space:nowrap;display:none}' +
            '#hc-edit .bx:hover .nm,#hc-edit .bx.on .nm{display:block}' +
            '#hc-edit .bx.lo .nm{bottom:auto;top:calc(100% + 3px)}' +
            '#hc-edit .bx.on .nm{background:#ff2e88;color:#1a0614}' +
            '#hc-edit .bx.gh .nm{background:#0009;color:#ddd;font-weight:500}' +
            '#hc-edit .rz{position:absolute;right:-7px;bottom:-7px;width:12px;height:12px;border-radius:50%;background:#ff2e88;border:2px solid #fff;cursor:nwse-resize;display:none}' +
            '#hc-edit .bx.on .rz{display:block}' +
            '#hc-edit.dragging .bx{cursor:grabbing}';
          document.head.appendChild(style);

          // Outside the body, so the layout's text size (zoom on the body) does not size the boxes.
          const layer = document.createElement('div');
          layer.id = 'hc-edit';
          layer.innerHTML = '<div class="safe"></div>';
          document.documentElement.appendChild(layer);
          const probe = document.createElement('div');
          probe.style.cssText = 'position:absolute;left:0;top:0;width:10vw;height:1px;visibility:hidden;pointer-events:none';
          document.body.appendChild(probe);

          const boxes = {};
          let drag = null, clashSaid = '';

          function nodeOf(k){ return document.getElementById('slot-' + k) || document.getElementById(k); }
          function zoom(){ return parseFloat(getComputedStyle(document.body).zoom) || 1; }
          // How many of the picture's pixels one vw of the layout is (SlotCss writes vw).
          function pxPerVw(){ return (probe.getBoundingClientRect().width / 10) || (innerWidth / 100); }
          function shown(k){
            const el = document.getElementById(k);
            for (let e = el; e && e !== document.documentElement; e = e.parentElement){
              const cs = getComputedStyle(e);
              if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) < 0.05) return false;
            }
            const r = nodeOf(k).getBoundingClientRect();
            const w = document.documentElement.clientWidth, h = document.documentElement.clientHeight;
            const inside = Math.max(0, Math.min(r.right, w) - Math.max(r.left, 0)) * Math.max(0, Math.min(r.bottom, h) - Math.max(r.top, 0));
            return r.width > 2 && r.height > 2 && inside > r.width * r.height * 0.5;
          }
          function scaleOf(k){ const e = PARTS[k]; return e && e.placed && e.scale ? e.scale : 1; }

          // Where a graphic now sits, as the layout writes it: one of nine
          // places, and how far in from the nearest edges.
          function placement(r){
            const v = pxPerVw(), w = document.documentElement.clientWidth, h = document.documentElement.clientHeight;
            const cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;
            const near = 3 * v;
            const across = Math.abs(cx - w / 2) < near ? '' : cx < w / 2 ? 'Left' : 'Right';
            const down = Math.abs(cy - h / 2) < near ? '' : cy < h / 2 ? 'Top' : 'Bottom';
            const round = n => Math.max(-50, Math.min(50, Math.round(n / v * 2) / 2));
            const x = across === 'Left' ? round(r.left) : across === 'Right' ? round(w - r.right) : 0;
            const y = down === 'Top' ? round(r.top) : down === 'Bottom' ? round(h - r.bottom) : 0;
            return { anchor: (down + across) || 'Middle', x, y };
          }

          function pick(k){ sel = k; post({ t: 'pick', k }); paint(); }

          function paint(){
            const want = ORDER.filter(k => PARTS[k] && PARTS[k].on && document.getElementById(k));
            const seen = [];
            want.forEach(k => {
              let b = boxes[k];
              if (!b){
                b = document.createElement('div');
                b.className = 'bx';
                b.dataset.k = k;
                b.innerHTML = '<span class="nm"></span><span class="rz"></span>';
                b.querySelector('.nm').textContent = NAMES[k];
                layer.appendChild(b);
                boxes[k] = b;
              }
              const vis = shown(k);
              // The mark sits in the pylon's foot while the pylon shows: nothing to place then.
              const skip = !vis && (!ghosts || (k === 'mark' && !PARTS.mark.placed));
              b.style.display = skip ? 'none' : '';
              if (skip) return;
              let r = nodeOf(k).getBoundingClientRect();
              const v = pxPerVw();
              let left = r.left, top = r.top, wd = r.width, ht = r.height;
              if (wd < 2 * v) { wd = Math.max(wd, 9 * v); }
              if (ht < v) { ht = Math.max(ht, 2.4 * v); }
              left = Math.max(0, Math.min(left, innerWidth - wd));
              top = Math.max(0, Math.min(top, innerHeight - ht));
              b.style.left = left + 'px'; b.style.top = top + 'px'; b.style.width = wd + 'px'; b.style.height = ht + 'px';
              b.classList.toggle('gh', !vis);
              b.classList.toggle('lo', top < 26);
              b.classList.toggle('on', k === sel);
              b.querySelector('.nm').textContent = vis ? NAMES[k] : NAMES[k] + ' - not showing now';
              if (vis && k !== 'mark') seen.push([k, left, top, left + wd, top + ht]);
            });
            Object.keys(boxes).forEach(k => { if (!want.includes(k)) boxes[k].style.display = 'none'; });

            // What covers what, measured on the picture as it is.
            const hit = new Set(), pairs = [];
            for (let i = 0; i < seen.length; i++) for (let j = i + 1; j < seen.length; j++){
              const a = seen[i], c = seen[j];
              if (Math.min(a[3], c[3]) - Math.max(a[1], c[1]) > 4 && Math.min(a[4], c[4]) - Math.max(a[2], c[2]) > 4){
                hit.add(a[0]); hit.add(c[0]); pairs.push([a[0], c[0]]);
              }
            }
            Object.keys(boxes).forEach(k => boxes[k].classList.toggle('cl', hit.has(k) && k !== sel));
            const said = JSON.stringify(pairs);
            if (said !== clashSaid){ clashSaid = said; post({ t: 'clash', pairs }); }
          }

          function finish(k, node, scale){
            const r = node.getBoundingClientRect();
            const p = placement(r);
            post({ t: 'place', k, anchor: p.anchor, x: p.x, y: p.y, scale: Math.max(0.5, Math.min(2, Math.round(scale * 20) / 20)) });
          }

          layer.addEventListener('pointerdown', ev => {
            const b = ev.target.closest('.bx');
            if (!b) return;
            ev.preventDefault();
            const k = b.dataset.k;
            if (k !== sel) pick(k);
            const node = nodeOf(k);
            drag = { k, node, b, sx: ev.clientX, sy: ev.clientY, rz: ev.target.classList.contains('rz'), started: false,
                     w0: node.getBoundingClientRect().width || 1, s0: scaleOf(k) };
            b.setPointerCapture(ev.pointerId);
          });
          layer.addEventListener('pointermove', ev => {
            if (!drag) return;
            const dx = ev.clientX - drag.sx, dy = ev.clientY - drag.sy;
            if (!drag.started){ if (Math.abs(dx) + Math.abs(dy) < 4) return; drag.started = true; layer.classList.add('dragging'); }
            if (drag.rz){
              drag.f = Math.max(0.5 / drag.s0, Math.min(2 / drag.s0, (drag.w0 + dx) / drag.w0));
              drag.node.style.scale = String(drag.f);
            } else {
              const z = zoom();
              drag.node.style.translate = (dx / z) + 'px ' + (dy / z) + 'px';
            }
            paint();
          });
          function up(){
            const d = drag; drag = null; layer.classList.remove('dragging');
            if (d && d.started) finish(d.k, d.node, d.rz ? d.s0 * (d.f || 1) : d.s0);
          }
          layer.addEventListener('pointerup', up);
          layer.addEventListener('pointercancel', up);
          // A click on the picture between graphics lets the picked one go.
          document.addEventListener('pointerdown', ev => { if (!ev.target.closest || !ev.target.closest('#hc-edit .bx')) { if (sel) pick(''); } });

          // Arrow keys nudge the picked graphic half a vw; Shift for five.
          document.addEventListener('keydown', ev => {
            const m = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[ev.key];
            if (!m || !sel || !nodeOf(sel)) return;
            ev.preventDefault();
            const node = nodeOf(sel), step = (ev.shiftKey ? 5 : 0.5) * pxPerVw() / zoom();
            const was = (node.style.translate || '0px 0px').split(' ').map(parseFloat);
            node.style.translate = ((was[0] || 0) + m[0] * step) + 'px ' + ((was[1] || 0) + m[1] * step) + 'px';
            paint();
            clearTimeout(node._hcNudge);
            node._hcNudge = setTimeout(() => finish(sel, node, scaleOf(sel)), 350);
          });

          // The window says which graphic its list picked.
          if (HOST) HOST.addEventListener('message', ev => { const m = ev.data; if (m && m.t === 'select' && typeof m.k === 'string'){ sel = m.k; paint(); } });

          setInterval(() => { if (!drag) paint(); }, 150);
          addEventListener('resize', paint);
          paint();
        })();
        """;

    /// <summary>
    /// Every layout as a live thumbnail - the demo race in each, held at
    /// racing (between races for the Between races one) - for picking a
    /// look in the editor. A click tells the window which, and in a
    /// browser opens that layout.
    /// </summary>
    internal string BuildOverlayGalleryPage(IEnumerable<(string Id, string Name, string About, bool Preset)> layouts)
    {
        var look = CurrentPageLook();
        var cards = new System.Text.StringBuilder();

        foreach (var (id, name, about, preset) in layouts)
        {
            var hold = id == "between" ? "between&at=2" : "racing&at=60";
            var safeId = System.Net.WebUtility.HtmlEncode(id);
            cards.Append("<a class=\"card\" data-id=\"").Append(safeId).Append("\" href=\"").Append(OverlayPath).Append('/')
                 .Append(Uri.EscapeDataString(id)).Append("?demo=1&hold=").Append(hold).Append("\">")
                 .Append("<div class=\"pic\"><iframe tabindex=\"-1\" loading=\"lazy\" src=\"").Append(OverlayPath).Append('/')
                 .Append(Uri.EscapeDataString(id)).Append("?demo=1&hold=").Append(hold).Append("\"></iframe></div>")
                 .Append("<b>").Append(System.Net.WebUtility.HtmlEncode(name)).Append("</b>")
                 .Append(preset ? string.Empty : "<i>Your own</i>")
                 .Append("<span>").Append(System.Net.WebUtility.HtmlEncode(about)).Append("</span></a>");
        }

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Pick a look</title><style>{{look.Root}}
        * { box-sizing:border-box; }
        body { margin:0; padding:12px; background:#150c29; color:#f4effe; font:15px 'Segoe UI',system-ui,sans-serif; }
        h1 { margin:0 0 2px; font-size:16px; }
        p { margin:0 0 10px; color:#a99cc8; font-size:12px; }
        .grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(150px,1fr)); gap:10px; }
        .card { display:block; text-decoration:none; color:inherit; background:#1f1439; border:1px solid #3a2a62; border-radius:12px; padding:6px; }
        .card:hover, .card:focus { border-color:var(--accent); outline:none; }
        .pic { position:relative; aspect-ratio:16/9; overflow:hidden; border-radius:8px; pointer-events:none;
               background:linear-gradient(180deg,#26303a,#3a4430 55%,#2d3624); }
        .pic iframe { position:absolute; left:0; top:0; width:1920px; height:1080px; border:0; transform-origin:0 0; background:transparent; }
        .card b { display:block; margin:6px 2px 0; font-size:13px; }
        .card i { font-style:normal; font-size:11px; letter-spacing:.1em; text-transform:uppercase; color:var(--accent); margin:0 4px; }
        .card span { display:block; margin:2px 2px 2px; font-size:11px; line-height:1.3; color:#a99cc8; }
        </style></head><body>
        <h1>Pick a look</h1>
        <p>Each one is running a made-up race. Pick one to start from - a change you make goes into a copy of your own, so the look itself stays as it is.</p>
        <div class="grid">{{cards}}</div>
        <script>
        function fit(){ document.querySelectorAll('.pic').forEach(p => { const f = p.querySelector('iframe'); f.style.transform = 'scale(' + (p.clientWidth / 1920) + ')'; }); }
        addEventListener('resize', fit); fit();
        const HOST = window.chrome && window.chrome.webview;
        document.querySelectorAll('.card').forEach(a => a.addEventListener('click', ev => {
          if (!HOST) return;
          ev.preventDefault();
          HOST.postMessage({ t: 'pickLayout', id: a.dataset.id });
        }));
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }
}
