using System.Net;

namespace HeatCaller;

/// <summary>
/// Two pages for the race night's pictures: the photo-finish camera
/// (/camera, with /api/camera/shots and /api/camera/frame - see PhotoFinish)
/// and the broadcast graphics for a stream (/broadcast).
///
/// The camera is this PC's only. A browser opens a webcam only for a secure
/// page, which http://localhost is and the club Wi-Fi address is not - and a
/// page anybody on the Wi-Fi could post pictures into, or read who has just
/// finished from, is not one to leave open. So the camera's three addresses
/// answer requests from this PC and nothing else.
///
/// The broadcast graphics are worked out in the page from the live state
/// (/api/state) the other pages read, so they cost the laptop nothing more.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>The photo finish the camera page works for - null until the host sets it, and the camera then says it isn't set up.</summary>
    public PhotoFinish? PhotoFinish { get; set; }

    /// <summary>Whether a request came from this PC itself.</summary>
    private static bool FromThisPc(HttpListenerContext context)
    {
        try
        {
            return context.Request.RemoteEndPoint is { } remote && IPAddress.IsLoopback(remote.Address);
        }
        catch
        {
            return false;
        }
    }

    private void HandleCamera(HttpListenerContext context, string path)
    {
        if (!FromThisPc(context))
        {
            context.Response.StatusCode = 403;
            WriteText(context, $"The finish camera only opens on the timing laptop itself, at http://localhost:{Port}/camera");
            return;
        }

        if (PhotoFinish is not { } photoFinish)
        {
            context.Response.StatusCode = 503;
            WriteText(context, "The photo finish isn't set up.");
            return;
        }

        switch (path)
        {
            case "/camera":
                WriteHtml(context, BuildCameraPage());
                break;

            case "/api/camera/shots":
                _ = int.TryParse(context.Request.QueryString["after"], out var after);
                WriteJson(context, photoFinish.AfterJson(after));
                break;

            case "/api/camera/frame":
                if (!IsAppWrite(context))
                {
                    return;
                }

                if (!int.TryParse(context.Request.QueryString["shot"], out var shot)
                    || !int.TryParse(context.Request.QueryString["t"], out var offset))
                {
                    context.Response.StatusCode = 400;
                    WriteJson(context, "{\"ok\":false}");
                    return;
                }

                var bytes = ReadCappedBytes(context.Request, PhotoFinish.LargestFrame);
                var saved = false;

                try
                {
                    saved = bytes is not null && photoFinish.SaveFrame(shot, offset, bytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    StatusMessage?.Invoke($"[camera] Couldn't save a photo-finish frame: {ex.Message}");
                }

                context.Response.StatusCode = saved ? 200 : 400;
                WriteJson(context, saved ? "{\"ok\":true}" : "{\"ok\":false}");
                break;
        }
    }

    /// <summary>A request's body as bytes, or null when it is bigger than <paramref name="maxBytes"/>.</summary>
    private static byte[]? ReadCappedBytes(HttpListenerRequest request, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];

        while (true)
        {
            var read = request.InputStream.Read(chunk, 0, chunk.Length);

            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);

            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The photo-finish camera: the webcam's picture, a choice of camera,
    /// "Keep every crossing", and what has been kept. Keeps the last four
    /// seconds of the camera in memory, fifteen frames a second, each
    /// stamped with the time it was taken; asks four times a second which
    /// crossings to keep, and sends back the frames from half a second
    /// before each to half a second after.
    /// </summary>
    private string BuildCameraPage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}">
        <title>Finish Camera</title><style>{{look.Root}}{{SharedStyle}}
        .bar { display:flex; justify-content:space-between; align-items:center; padding:12px 18px;
               background:var(--surface); border-bottom:1px solid var(--border); flex-wrap:wrap; gap:10px; }
        .tools { display:flex; gap:14px; align-items:center; flex-wrap:wrap; font-size:15px; }
        .tools select { font:inherit; padding:5px 8px; border-radius:8px; background:var(--bg); color:var(--text); border:1px solid var(--border); }
        .wrap { display:flex; gap:18px; padding:18px; flex-wrap:wrap; align-items:flex-start; }
        video { width:min(100%, 960px); background:#000; border-radius:10px; }
        .side { flex:1; min-width:260px; }
        .state { font-weight:700; font-size:18px; margin-bottom:8px; }
        .state.ok { color:var(--good); } .state.bad { color:var(--bad); }
        .hint { color:var(--soft); font-size:14px; line-height:1.45; }
        ul { list-style:none; padding:0; margin:12px 0 0; font-size:15px; }
        li { padding:6px 0; border-bottom:1px solid var(--border); }
        li b { color:var(--accent); }
        canvas#last { width:100%; margin-top:10px; border-radius:8px; display:none; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}FINISH CAMERA</h1>
          <div class="tools">
            <select id="cams"><option value="">Camera...</option></select>
            <label><input type="checkbox" id="every"> Keep every crossing</label>
          </div>
        </div>
        <div class="wrap">
          <video id="video" autoplay muted playsinline></video>
          <div class="side">
            <div class="state" id="state">Starting the camera...</div>
            <div class="hint">Point the camera along the finish line. HeatCaller keeps the frames from a second
              before to half a second after each car takes the checkered flag - tick "Keep every crossing" to keep
              every lap. Leave this page open and on screen (a small window is fine; minimized is not). Look at the
              pictures in HeatCaller under Connection, Photo Finish.</div>
            <canvas id="last"></canvas>
            <ul id="kept"></ul>
          </div>
        </div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        const FPS = 15, KEEP_MS = 4000, BEFORE = 1000, AFTER = 500;
        const video = document.getElementById('video');
        const grab = document.createElement('canvas');
        const gx = grab.getContext('2d');
        const cams = document.getElementById('cams');
        const every = document.getElementById('every');
        const CAM_KEY = 'hcCamera', EVERY_KEY = 'hcCameraEvery';
        let frames = [], stream = null, last = -1, run = '', pending = [], keptCount = 0, cameraOk = false, appOk = false, polling = false, uploading = false;
        const queued = new Set(), jobs = [];

        try { every.checked = localStorage.getItem(EVERY_KEY) === '1'; } catch(e) {}
        every.addEventListener('change', () => { try { localStorage.setItem(EVERY_KEY, every.checked ? '1' : '0'); } catch(e) {} });

        function state(text, good){
          const el = document.getElementById('state');
          el.textContent = text; el.className = 'state ' + (good ? 'ok' : 'bad');
        }

        async function start(deviceId){
          try {
            if (stream) stream.getTracks().forEach(t => t.stop());
            const video_ = { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } };
            if (deviceId) video_.deviceId = { exact: deviceId };
            stream = await navigator.mediaDevices.getUserMedia({ video: video_, audio: false });
            video.srcObject = stream;
            cameraOk = true;
            // Unplugged, or asleep: say so and try again, rather than going on
            // stamping the last picture with the time as if it were live.
            const mine = stream;
            mine.getVideoTracks()[0].addEventListener('ended', () => {
              if (stream !== mine) return;
              cameraOk = false; frames = [];
              state('The camera stopped - trying it again...', false);
              setTimeout(() => start(cams.value), 2000);
            });
            const devices = (await navigator.mediaDevices.enumerateDevices()).filter(d => d.kind === 'videoinput');
            const using = stream.getVideoTracks()[0].getSettings().deviceId || '';
            cams.innerHTML = devices.map((d, i) =>
              `<option value="${d.deviceId}">${(d.label || ('Camera ' + (i + 1))).replace(/[<>&"]/g, '')}</option>`).join('');
            cams.value = using;
          } catch(e) {
            cameraOk = false;
            // A camera remembered from before that isn't plugged in now: any camera instead.
            if (deviceId && e && (e.name === 'OverconstrainedError' || e.name === 'NotFoundError')) { start(''); return; }
            state('No camera: ' + (e && e.name === 'NotAllowedError' ? 'the browser was not allowed to use it.' : (e && e.message || e)), false);
            // Not plugged in yet, or being replugged: keep trying. Refused
            // by the browser is for the person at the laptop to fix.
            if (!e || e.name !== 'NotAllowedError') setTimeout(() => { if (!cameraOk) start(''); }, 5000);
          }
        }

        cams.addEventListener('change', () => {
          try { localStorage.setItem(CAM_KEY, cams.value); } catch(e) {}
          start(cams.value);
        });

        // The last few seconds, stamped with the time each frame was taken,
        // and that time written on the picture too.
        setInterval(() => {
          if (!cameraOk || video.readyState < 2 || !video.videoWidth) return;
          if (grab.width !== video.videoWidth || grab.height !== video.videoHeight){ grab.width = video.videoWidth; grab.height = video.videoHeight; }
          const t = Date.now();
          gx.drawImage(video, 0, 0);
          const d = new Date(t), stamp = d.toLocaleTimeString([], { hour12: !window.HC24 }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
          const size = Math.max(14, Math.round(grab.height / 28));
          gx.font = 'bold ' + size + 'px system-ui'; gx.fillStyle = 'rgba(0,0,0,.6)';
          gx.fillRect(0, grab.height - size * 1.6, gx.measureText(stamp).width + size, size * 1.6);
          gx.fillStyle = '#fff'; gx.fillText(stamp, size / 2, grab.height - size * 0.5);
          grab.toBlob(b => {
            if (!b) return;
            frames.push({ t, b });
            const cut = Date.now() - KEEP_MS;
            while (frames.length && frames[0].t < cut) frames.shift();
          }, 'image/jpeg', 0.85);
        }, 1000 / FPS);

        // Which crossings to keep - asked four times a second.
        // One at a time: a slow answer must not let two polls queue the same shot.
        async function poll(){
          if (polling) return;
          polling = true;
          try {
            const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
            let r;
            try { r = await fetch('/api/camera/shots?after=' + Math.max(0, last), { cache: 'no-store', signal: stop.signal }); }
            finally { clearTimeout(timer); }
            const d = await r.json();
            appOk = true;
            // First answer, or HeatCaller restarted - its shots are numbered
            // from 1 again, so what was queued under the old numbers goes.
            if (last < 0 || d.run !== run) { last = d.latest; run = d.run; queued.clear(); pending = []; jobs.length = 0; }
            else {
              (d.shots || []).forEach(s => {
                last = Math.max(last, s.id);
                if ((s.finish || every.checked) && !queued.has(s.id)) { queued.add(s.id); pending.push(s); }
              });
            }
          } catch(e) { appOk = false; }
          if (cameraOk){
            if (document.hidden) state('This window is hidden or minimized - the browser slows it down and frames will be missed. Keep it on screen.', false);
            else state(appOk ? 'Ready - ' + keptCount + ' kept' : 'Camera on, but HeatCaller is not answering', appOk);
          }
          try { pick(); } finally { polling = false; }
        }
        setInterval(poll, 250);

        // Every ready shot's frames picked the moment it is ready, and the
        // sending left to upload(): a pack crossing together is many
        // uploads, and the frames before the later shots would be gone from
        // the buffer by their turn. Not waited for either - the poll used to
        // wait for the uploads, so a car crossing during a long batch was
        // only heard of afterwards, with its frames already gone.
        function pick(){
          const now = Date.now();
          const ready = pending.filter(s => now >= s.at + AFTER + 150);
          pending = pending.filter(s => now < s.at + AFTER + 150);
          ready.forEach(s => jobs.push({ s, picked: frames.filter(f => f.t >= s.at - BEFORE && f.t <= s.at + AFTER) }));
          upload();
        }

        // One at a time, in the order they were picked.
        async function upload(){
          if (uploading) return;
          uploading = true;
          try {
            while (jobs.length){
              const { s, picked } = jobs.shift();
              if (!picked.length) continue;
              let sent = 0;
              for (const f of picked){
                try {
                  const r = await fetch('/api/camera/frame?shot=' + s.id + '&t=' + (f.t - s.at),
                    { method: 'POST', headers: { 'X-HeatCaller': '1', 'Content-Type': 'image/jpeg' }, body: f.b });
                  if (r.ok) sent++;
                } catch(e) {}
              }
              if (!sent) continue;
              keptCount++;
              const li = document.createElement('li');
              const b = document.createElement('b'); b.textContent = s.finish ? (s.pos ? 'P' + s.pos + ' ' : '') + 'finish' : 'lap ' + s.lap;
              li.appendChild(b); li.appendChild(document.createTextNode(' ' + s.name + ' - ' + sent + ' frames'));
              const list = document.getElementById('kept'); list.insertBefore(li, list.firstChild);
              while (list.children.length > 30) list.removeChild(list.lastChild);
              showLast(picked.reduce((a, f) => Math.abs(f.t - s.at) < Math.abs(a.t - s.at) ? f : a, picked[0]));
            }
          } finally { uploading = false; }
        }

        async function showLast(f){
          try {
            const img = await createImageBitmap(f.b);
            const c = document.getElementById('last');
            c.width = img.width; c.height = img.height;
            c.getContext('2d').drawImage(img, 0, 0);
            c.style.display = 'block';
          } catch(e) {}
        }

        // Kept open all night: never reloaded for a change of look.
        function lookBusy(){ return true; }

        let saved = '';
        try { saved = localStorage.getItem(CAM_KEY) || ''; } catch(e) {}
        start(saved);
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }

    /// <summary>
    /// The broadcast graphics, for a stream (an OBS browser source at
    /// 1920x1080 over the camera): a NASCAR-style pylon down the left, the
    /// closest battle along the bottom, a pop-up for each new fastest lap,
    /// a banner for each lead change and milestone, the grid before the
    /// start, the podium with driver ratings at the end, and the
    /// championship as it stands if it ended now. Transparent everywhere
    /// else. Parts can be left out with
    /// ?hide=tower,battle,fastest,grid,podium,champ,moment. A captions strip
    /// of what the announcer says is added with ?captions=1 (22 Oct 2026).
    ///
    /// THE PYLON (18 Oct 2026). Each row carries the car's racing number
    /// when the driver has one, and its last column cycles the way a
    /// broadcast tower does: the interval to the car ahead, then the gap to
    /// the leader, then laps led once anybody but the polesitter has led -
    /// eight seconds each. ?gap=interval or ?gap=leader pins it to one.
    /// Under the title, the lead change count - the announcer's own
    /// (AnnouncementService.Leads), so the tower and the voice agree.
    ///
    /// THE BATTLE BOX remembers the fight it is showing between polls, so
    /// it can say how many laps the two have been inside a second and
    /// whether the gap is coming down or going out - worked out here from
    /// the gaps the page already reads, at no cost to the laptop.
    ///
    /// A sponsor who presents a moment (SponsoredMoments) is named on its
    /// graphic every time - "FASTEST LAP · PRESENTED BY ..." - which costs
    /// the room nothing, unlike the voice, which rations it.
    /// </summary>
    /// <summary>
    /// One graphic's markup, as the layout wants it: where it has always
    /// been, or - moved by the layout - inside a slot of its own (see
    /// OverlayLayouts.SlotCss), marked inslot so it fades there instead of
    /// sliding. Always written, so the page's script finds every element;
    /// one the layout leaves out simply never shows (want() in the script).
    /// </summary>
    private static string PlaceInLayout(OverlayLayout layout, OverlayElementKind kind, string html)
    {
        if (layout.Element(kind) is not { On: true, Anchor: not OverlayAnchor.Default })
        {
            return html;
        }

        var at = html.IndexOf("class=\"", StringComparison.Ordinal);
        var inner = at < 0 ? html : html.Insert(at + "class=\"".Length, "inslot ");

        return "<div id=\"slot-" + OverlayLayouts.ElementName(kind) + "\" class=\"slot\">" + inner + "</div>";
    }

    internal string BuildBroadcastPage(OverlayLayout? layout = null)
    {
        var look = CurrentPageLook();

        // THE LAYOUT (14 Nov 2026, plans/obs-overlay-plan.md section 2):
        // which graphics show, where and when. The Broadcast preset places
        // nothing, so /broadcast draws as it always has.
        layout ??= OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!;
        var markPlaced = layout.Element(OverlayElementKind.Mark) is { Anchor: not OverlayAnchor.Default };
        string Part(OverlayElementKind kind, string html) => PlaceInLayout(layout, kind, html);
        var slotCss = OverlayLayouts.SlotCss(layout);
        var layoutJson = OverlayLayouts.ScriptJson(layout).Replace("</", "<\\/", StringComparison.Ordinal);
        var basicsJson = StreamBasics.ScriptJson(_settings?.StreamBasics);
        var glass = Math.Abs(layout.Glass - 0.82) < 0.001
            ? ".82"
            : Math.Clamp(layout.Glass, 0.4, 1).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Broadcast Graphics</title><style>{{look.Root}}
        * { box-sizing:border-box; }
        html, body { margin:0; background:transparent; overflow:hidden; width:100vw; height:100vh;
                     font-family:'Bahnschrift','Segoe UI',system-ui,sans-serif; color:#fff; }
        /* Everything is sized in vw so the graphics fit any canvas size.
           Nothing under .95vw (14 Nov 2026, stream overlay plan 6.8): most
           viewers watch on a phone, where .72vw was about 3px tall. */
        .glass { background:rgba(10,12,18,{{glass}}); border-radius:.5vw; box-shadow:0 .3vw 1.2vw rgba(0,0,0,.35); }
        .acc { color:var(--accent); }
        .by { display:block; font-size:.95vw; letter-spacing:.14em; color:#c9cdd6; text-transform:uppercase; margin-top:.3vw; }
        #tower { position:absolute; left:2vw; top:3vw; width:21vw; overflow:hidden; transition:opacity .4s; }
        #tower .head { padding:.7vw 1vw; font-size:1vw; letter-spacing:.12em; font-weight:700; display:flex; justify-content:space-between;
                       border-bottom:.15vw solid var(--accent); text-transform:uppercase; }
        #tower .sub { padding:.35vw 1vw; font-size:.95vw; letter-spacing:.14em; color:#c9cdd6; display:flex; justify-content:space-between;
                      text-transform:uppercase; background:rgba(255,255,255,.04); }
        #tower .sub .mode { color:var(--accent); font-weight:700; }
        #rows { position:relative; }
        /* HeatCaller's credit at the foot of the tower (31 Oct 2026, the mark
           always on everywhere), the way a broadcast credits its timing. In
           the look's accent, like the rest of the tower. */
        #tower .hcfoot { display:flex; align-items:center; gap:.4vw; padding:.4vw 1vw; font-size:.95vw; color:#c9cdd6;
                         border-top:1px solid rgba(255,255,255,.08); }
        #tower .hcsun { width:1vw; height:1vw; flex:none; fill:currentColor; color:var(--accent); }
        #tower .hcword { font-weight:700; letter-spacing:.12em; text-transform:uppercase; }
        #tower .hcword .hcc { color:var(--accent); }
        .row { position:absolute; left:0; right:0; height:2.3vw; display:flex; align-items:center; font-size:1.15vw;
               transition:top .6s ease, background .6s; padding:0 1vw; gap:.6vw; }
        .row .p { width:1.8vw; font-weight:800; text-align:right; }
        .row .num { min-width:1.9vw; padding:0 .25vw; font-size:.95vw; font-weight:800; text-align:center; border-radius:.2vw;
                    background:var(--accent); color:#0a0c12; font-variant-numeric:tabular-nums; }
        .row .num:empty { visibility:hidden; }
        #tower:not(.nums) .row .num { display:none; }
        /* The car's colour (6.5): a small block, outlined so a black or a
           white car still shows on the glass. */
        .sw { display:inline-block; width:.9vw; height:.9vw; border-radius:.2vw; flex:none; box-shadow:0 0 0 .1vw rgba(255,255,255,.55); vertical-align:middle; }
        .row .n { flex:1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; font-weight:600; }
        .row .g { color:#c9cdd6; font-variant-numeric:tabular-nums; }
        .row .g.purple { color:#B06CFF; font-weight:700; }
        .row .g.green { color:#4ade80; font-weight:700; }
        .row.up { background:rgba(74,222,128,.25); }
        .row.fl .n::after { content:' \25C6'; color:#B06CFF; font-size:.8vw; }
        .row.led .p { color:var(--accent); }
        /* Home: a car that has taken the checkered (6.3). */
        .row.done .g { color:#fff; font-weight:700; }
        .row.done .n::before { content:'\2691 '; color:#fff; font-size:.95vw; }
        #champ { position:absolute; left:2vw; width:21vw; padding:.6vw 1vw; font-size:.9vw; line-height:1.4; display:none; }
        #battle { position:absolute; left:50%; bottom:3vw; transform:translateX(-50%) translateY(10vw); padding:.8vw 1.6vw;
                  font-size:1.4vw; font-weight:700; white-space:nowrap; transition:transform .5s ease; text-align:center; }
        #battle.show { transform:translateX(-50%) translateY(0); }
        #battle .gap { color:var(--accent); margin-left:1vw; font-variant-numeric:tabular-nums; }
        #battle .meta { display:block; font-size:.95vw; letter-spacing:.14em; margin-top:.3vw; color:#c9cdd6; }
        #battle .trend { margin-right:1vw; }
        #battle .trend.closing { color:#4ade80; }
        #battle .trend.pulling { color:#f87171; }
        #fastest { position:absolute; right:2vw; bottom:3vw; padding:.9vw 1.4vw; font-size:1.3vw; font-weight:700;
                   transform:translateX(30vw); transition:transform .5s ease; border-left:.4vw solid #B06CFF; }
        #fastest.show { transform:translateX(0); }
        #fastest small { display:block; font-size:.95vw; letter-spacing:.14em; color:#B06CFF; }
        #moment { position:absolute; left:50%; top:3vw; transform:translateX(-50%) translateY(-14vw); padding:1vw 2.2vw; min-width:28vw; max-width:52vw;
                  text-align:center; transition:transform .5s ease; border-top:.35vw solid var(--accent); }
        #moment.show { transform:translateX(-50%) translateY(0); }
        #moment .t { font-size:1.6vw; font-weight:800; letter-spacing:.14em; text-transform:uppercase; color:var(--accent); }
        #moment .l { font-size:1.3vw; font-weight:600; margin-top:.3vw; }
        #moment.milestone { border-top-color:#facc15; }
        #moment.milestone .t { color:#facc15; }
        .centre { position:absolute; left:50%; top:50%; transform:translate(-50%,-50%) scale(.9); opacity:0;
                  transition:opacity .5s, transform .5s; padding:1.6vw 2.4vw; min-width:30vw; }
        .centre.show { opacity:1; transform:translate(-50%,-50%) scale(1); }
        .centre h2 { margin:0 0 1vw; font-size:1.6vw; letter-spacing:.12em; text-transform:uppercase; border-bottom:.15vw solid var(--accent); padding-bottom:.6vw; }
        .centre h2 .by { font-size:.95vw; font-weight:400; }
        .centre .line { font-size:1.4vw; padding:.35vw 0; display:flex; gap:1vw; }
        .centre .line b { width:2.4vw; text-align:right; color:var(--accent); }
        #podium .line { font-size:1.8vw; }
        /* "1st", not "1" (overlay plan 7.2): plain words first, the RC numbers small beside. */
        #podium .line b { width:3.6vw; }
        /* PROVISIONAL / CORRECTED beside the race's name (6.9). */
        .prov { margin-left:1vw; padding:.15vw .6vw; border-radius:.3vw; font-size:1.15vw; letter-spacing:.12em; vertical-align:middle;
                background:rgba(250,204,21,.18); color:#facc15; }
        .prov.fix { background:rgba(74,222,128,.18); color:#4ade80; }
        #podium .line span.t { margin-left:auto; color:#c9cdd6; font-size:1.2vw; align-self:center; }
        #podium .line span.r { color:var(--accent); font-size:1.1vw; align-self:center; min-width:6vw; text-align:right; }
        /* CAPTIONS (?captions=1, 22 Oct 2026, driver experience, item 9):
           the line the announcer has just said, along the very bottom, for
           a viewer watching with the sound off. With them on, the battle
           box and the fastest lap sit higher, clear of the strip. */
        #captions { position:absolute; left:50%; bottom:.8vw; transform:translateX(-50%); width:max-content; max-width:70vw;
                    padding:.5vw 1.4vw; font-size:1.3vw; font-weight:600; line-height:1.3; text-align:center;
                    opacity:0; transition:opacity .6s; }
        #captions.show { opacity:1; }
        body.cap #battle, body.cap #fastest { bottom:5.4vw; }
        /* THE LAYOUT'S OWN GRAPHICS (14 Nov 2026, stream overlay plan
           sections 2 and 3). Nothing smaller than 1.15vw - 22px on a 1920
           canvas - because most viewers watch on a phone. Each fades in and
           out; hidden until the layout asks for it. */
        .fade { opacity:0; transition:opacity .45s; }
        .fade.show { opacity:1; }
        #status { position:absolute; right:2vw; top:2vw; padding:.6vw 1.1vw; font-size:1.3vw; font-weight:700; display:flex; gap:1vw;
                  align-items:baseline; white-space:nowrap; text-transform:uppercase; letter-spacing:.08em; }
        #status .acc { font-variant-numeric:tabular-nums; }
        #ticker { position:absolute; left:4vw; bottom:2vw; width:92vw; display:flex; align-items:center; gap:1.2vw; padding:.6vw 1.1vw;
                  font-size:1.3vw; white-space:nowrap; overflow:hidden; }
        #ticker .tk-race { flex:none; color:var(--accent); font-weight:800; text-transform:uppercase; letter-spacing:.1em; }
        #ticker .tk-track { flex:1; overflow:hidden; }
        #ticker .tk-items { display:inline-flex; gap:2.2vw; }
        #ticker .tk-items.scroll { animation:tk var(--tk-s, 12s) linear infinite alternate; }
        @keyframes tk { from { transform:translateX(0); } to { transform:translateX(var(--tk-d, 0)); } }
        #ticker .tk-item b { color:var(--accent); margin-right:.4vw; font-variant-numeric:tabular-nums; }
        #ticker .tk-item .g { color:#c9cdd6; margin-left:.5vw; font-variant-numeric:tabular-nums; }
        #bug { position:absolute; right:2vw; top:2vw; min-width:17vw; max-width:30vw; padding:.7vw 1.1vw; font-size:1.2vw; line-height:1.35; }
        #bug .bg-race { font-weight:800; text-transform:uppercase; letter-spacing:.1em; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
        #bug .bg-clock { font-size:1.7vw; font-weight:800; color:var(--accent); font-variant-numeric:tabular-nums; }
        #bug .bg-lead { color:#c9cdd6; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
        #clublogo { position:absolute; left:2vw; top:2vw; }
        #clublogo .logo { display:block; height:6vw; width:auto; max-width:18vw; object-fit:contain; }
        #clublogo .logo.plate { background:var(--plate); padding:.4vw; border-radius:.5vw; }
        #mark { position:absolute; right:2vw; bottom:2vw; display:flex; align-items:center; gap:.5vw; padding:.45vw .9vw; font-size:1.15vw; color:#c9cdd6; }
        #mark .hcsun { width:1.3vw; height:1.3vw; flex:none; fill:currentColor; color:var(--accent); }
        #mark .hcword { font-weight:700; letter-spacing:.12em; text-transform:uppercase; }
        #mark .hcword .hcc { color:var(--accent); }
        #tower.nofoot .hcfoot { display:none; }
        /* NEW TO RC RACING? (overlay plan 7.6): the basics, a card at a time, between races. */
        #basics { position:absolute; left:50%; bottom:3vw; transform:translateX(-50%); width:56vw; padding:1vw 1.6vw; }
        #basics .bs-k { font-size:1.15vw; font-weight:800; letter-spacing:.14em; text-transform:uppercase; color:var(--accent); margin-bottom:.4vw; }
        #basics .bs-t { font-size:1.5vw; line-height:1.4; }
        /* The sponsor graphic (section 5): their logo, or their name when the logo will not load. */
        #sponsor { position:absolute; right:2vw; top:2vw; padding:.6vw .9vw; display:flex; align-items:center; gap:.7vw; font-size:1.2vw; font-weight:700; }
        #sponsor img { display:block; height:4.2vw; max-width:16vw; width:auto; object-fit:contain; }
        #sponsor img:not([src]) { display:none; }
        .bylogo { height:1.6vw; width:auto; max-width:7vw; object-fit:contain; vertical-align:middle; margin-left:.5vw; }
        #schedule h2 .by { font-size:1.15vw; }
        #schedule .line { font-size:1.5vw; }
        #schedule .who { font-size:1.2vw; color:#c9cdd6; margin:0 0 .6vw 3.4vw; line-height:1.35; }
        #schedule .who.mar { color:var(--accent); }
        #schedule .line .when { margin-left:auto; color:#c9cdd6; font-size:1.2vw; align-self:center; }
        /* A graphic the layout has moved sits in a slot of its own, and
           fades rather than sliding in from the edge it used to live by. */
        .slot > .inslot { position:relative !important; left:auto !important; right:auto !important; top:auto !important; bottom:auto !important;
                          transform:none !important; }
        .slot > .inslot.anim { opacity:0; transition:opacity .45s; }
        .slot > .inslot.anim.show { opacity:1; }
        {{slotCss}}
        {{(Math.Abs(layout.TextScale - 1) < 0.001 ? string.Empty : "body{zoom:" + Math.Clamp(layout.TextScale, 0.8, 1.6).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "}")}}
        </style></head><body data-look="{{look.Fingerprint}}">
        {{Part(OverlayElementKind.Tower, "<div id=\"tower\" class=\"glass" + (markPlaced ? " nofoot" : string.Empty) + "\" style=\"opacity:0\"><div class=\"head\"><span id=\"title\">-</span><span id=\"clock\" class=\"acc\"></span></div><div class=\"sub\"><span id=\"mode\" class=\"mode\"></span><span id=\"changes\"></span></div><div id=\"rows\"></div><div class=\"hcfoot\">" + HeatCallerMark.SunSvg + HeatCallerMark.WordHtml + "</div></div>")}}
        {{Part(OverlayElementKind.Champ, "<div id=\"champ\" class=\"glass\"></div>")}}
        {{Part(OverlayElementKind.Battle, "<div id=\"battle\" class=\"glass anim\"></div>")}}
        {{Part(OverlayElementKind.Fastest, "<div id=\"fastest\" class=\"glass anim\"><small id=\"fhead\">FASTEST LAP</small><span id=\"fname\"></span> <span id=\"ftime\" class=\"acc\"></span></div>")}}
        {{Part(OverlayElementKind.Moment, "<div id=\"moment\" class=\"glass anim\"><div class=\"t\" id=\"mtitle\"></div><div class=\"l\" id=\"mline\"></div><span class=\"by\" id=\"mby\"></span></div>")}}
        {{Part(OverlayElementKind.Grid, "<div id=\"grid\" class=\"glass centre anim\"></div>")}}
        {{Part(OverlayElementKind.Podium, "<div id=\"podium\" class=\"glass centre anim\"></div>")}}
        {{Part(OverlayElementKind.Captions, "<div id=\"captions\" class=\"glass anim\" aria-live=\"polite\"></div>")}}
        {{Part(OverlayElementKind.Status, "<div id=\"status\" class=\"glass fade\"><span id=\"st-race\"></span><span id=\"st-clock\" class=\"acc\"></span></div>")}}
        {{Part(OverlayElementKind.Ticker, "<div id=\"ticker\" class=\"glass fade\"><span class=\"tk-race\" id=\"tk-race\"></span><div class=\"tk-track\" id=\"tk-track\"><div class=\"tk-items\" id=\"tk-items\"></div></div></div>")}}
        {{Part(OverlayElementKind.Bug, "<div id=\"bug\" class=\"glass fade\"><div class=\"bg-race\" id=\"bg-race\"></div><div class=\"bg-clock\" id=\"bg-clock\"></div><div class=\"bg-lead\" id=\"bg-lead\"></div></div>")}}
        {{Part(OverlayElementKind.ClubLogo, "<div id=\"clublogo\" class=\"fade\">" + look.Logo + "</div>")}}
        {{Part(OverlayElementKind.Schedule, "<div id=\"schedule\" class=\"glass centre anim\"></div>")}}
        {{Part(OverlayElementKind.Basics, "<div id=\"basics\" class=\"glass fade\"><div class=\"bs-k\" id=\"bs-k\"></div><div class=\"bs-t\" id=\"bs-t\"></div></div>")}}
        {{Part(OverlayElementKind.Sponsor, "<div id=\"sponsor\" class=\"glass fade\"><img id=\"sp-logo\" alt=\"\"><span id=\"sp-name\"></span></div>")}}
        {{Part(OverlayElementKind.Mark, "<div id=\"mark\" class=\"glass fade\">" + HeatCallerMark.SunSvg + HeatCallerMark.WordHtml + "</div>")}}
        <script>
        // The layout this page draws (stream overlay plan section 2).
        const LAYOUT = {{layoutJson}};
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        const params = new URLSearchParams(location.search);
        const hide = (params.get('hide') || '').toLowerCase().split(',');
        // What the layout shows, and in which part of the night: "racing",
        // "staging", "finished" or "between". ?hide= still leaves a part
        // out on top of that, as it always did.
        const PARTS = (LAYOUT && LAYOUT.elements) || {};
        let PART = 'between';
        function partOf(state){
          if (state === 'Racing') return 'racing';
          if (state === 'Finished') return 'finished';
          if (state === 'Staging' || state === 'Countdown') return 'staging';
          return 'between';
        }
        function want(part){
          if (hide.includes(part)) return false;
          const e = PARTS[part];
          if (!e || !e.on) return false;
          return !Array.isArray(e.during) || e.during.length === 0 || e.during.includes(PART);
        }
        // Moved by the layout: fades in its slot rather than sliding.
        function placed(part){ const e = PARTS[part]; return !!(e && e.placed); }
        const gapPin = (params.get('gap') || '').toLowerCase();
        const SHOWN = (PARTS.tower && PARTS.tower.rows) || 8, TOP = 8, CYCLE_MS = 8000, MOMENT_MS = 7000;
        const rowEls = {};
        let lastPos = {}, best = { ms: 0, id: '' }, fastTimer = 0, first = true;
        let fight = null, momentSeq = -1, momentTimer = 0, podiumSeen = null, momentQueue = [], momentBusy = false;
        function showNextMoment(){
          if (momentBusy || momentQueue.length === 0) return;
          const m = momentQueue.shift();
          momentBusy = true;
          const el = document.getElementById('moment');
          // The kind is a class; anim and the slot's class are kept.
          el.className = 'glass anim ' + esc(m.kind || '') + (placed('moment') ? ' inslot' : '');
          document.getElementById('mtitle').textContent = m.title || '';
          document.getElementById('mline').textContent = snText(m.line || '');
          document.getElementById('mby').textContent = m.sponsor ? 'Presented by ' + m.sponsor : '';
          requestAnimationFrame(() => el.classList.add('show'));
          clearTimeout(momentTimer);
          momentTimer = setTimeout(() => {
            el.classList.remove('show');
            // A beat between banners, then the next.
            setTimeout(() => { momentBusy = false; showNextMoment(); }, 700);
          }, MOMENT_MS);
        }

        // Captions, only when asked for: the line the announcer has just
        // said, fading after eight seconds. Only a line said while the page
        // is open - the first look takes what is there as already said, so
        // OBS reloading the source does not put an old line on the stream.
        // A different number counts as new rather than only a bigger one,
        // because the numbers start again when HeatCaller does.
        const captionsOn = params.get('captions') === '1' || !!(PARTS.captions && PARTS.captions.on);
        if (captionsOn) document.body.classList.add('cap');
        const CAPTION_MS = 8000;
        let captionSeen = -1, captionTimer = 0;
        function showCaptions(list){
          if (!captionsOn) return;
          const lines = Array.isArray(list) ? list : [];
          const newest = lines.length ? lines[lines.length - 1] : null;
          if (captionSeen < 0){ captionSeen = newest ? newest.n : 0; return; }
          if (!newest || newest.n === captionSeen) return;
          captionSeen = newest.n;
          const el = document.getElementById('captions');
          el.textContent = snText(newest.t);
          el.classList.add('show');
          clearTimeout(captionTimer);
          captionTimer = setTimeout(() => el.classList.remove('show'), CAPTION_MS);
        }

        // "1st", "2nd", "3rd", "11th", "22nd" (overlay plan 7.2).
        function ordinal(n){
          const t = n % 100, e = n % 10;
          return n + (t >= 11 && t <= 13 ? 'th' : e === 1 ? 'st' : e === 2 ? 'nd' : e === 3 ? 'rd' : 'th');
        }
        function fmt(ms){ return ms > 0 ? (ms/1000).toFixed(3) : '-'; }
        function gapSecs(g){ const m = /^\+(\d+([.,]\d+)?)$/.exec(g || ''); return m ? parseFloat(m[1].replace(',', '.')) : null; }
        // The car's colour as a swatch (6.5), or nothing for a car without one.
        function swatch(d, id){
          const c = ((d.pylon && d.pylon.cars) || {})[id] || {};
          return /^#[0-9A-F]{6}$/.test(c.col || '') ? '<span class="sw" style="background:' + c.col + ';margin-right:.5vw"></span>' : '';
        }
        function nameOf(d, id){ const p = (d.people || []).find(x => x.id === id); return p ? sn(p.name) : id; }
        // A junior's name as the stream shows it (overlay plan 7.5): "Mia K",
        // and the same inside a line of words - a moment, a caption.
        let SN = {};
        function sn(name){ const n = String(name == null ? '' : name); return Object.prototype.hasOwnProperty.call(SN, n) ? String(SN[n]) : n; }
        function snText(text){
          let t = String(text == null ? '' : text);
          Object.keys(SN).sort((a, b) => b.length - a.length).forEach(full => { t = t.split(full).join(String(SN[full])); });
          return t;
        }
        // Only an address the laptop makes is ever put in an img (section 5).
        const LOGO_URL = /^\/sponsor-logo\/sponsor-logo-[0-9a-f]{12}\.png$/;
        // The presenting sponsor's logo beside their name, or nothing.
        function byLogo(d, moment){
          const url = (d.presentedLogos || {})[moment];
          return typeof url === 'string' && LOGO_URL.test(url) ? '<img class="bylogo" alt="" src="' + url + '">' : '';
        }
        function presentedBy(d, moment){ const p = d.presented || {}; return p[moment] ? 'Presented by ' + p[moment] : ''; }

        // Which gap the pylon's last column shows right now. Laps led joins
        // the cycle only once somebody other than the first leader has led -
        // until then it is one name with every lap, and says nothing the
        // order does not.
        function gapMode(racing, pylon){
          if (gapPin === 'interval' || gapPin === 'leader' || gapPin === 'last' || gapPin === 'overall') return gapPin;
          // Last lap (overlay plan 6.6): purple for the fastest of the race,
          // green for the driver's own best, plain otherwise - how a racer
          // watches somebody finding pace or fading.
          const modes = ['interval', 'leader', 'last'];
          // In a qualifier (6.4): where this run puts each driver overall,
          // in place of laps led - in qualifying nobody minds who leads on track.
          const qualifying = Object.values((pylon && pylon.cars) || {}).some(c => c && c.qual);
          if (qualifying) modes.push('overall');
          else if (racing && pylon && pylon.changes > 0) modes.push('led');
          return modes[Math.floor(Date.now() / CYCLE_MS) % modes.length];
        }

        // One poll at a time, none left hanging - an older answer arriving
        // after a newer one would bring a fastest lap back round.
        let ticking = false;
        async function tick(){
          if (ticking) return;
          ticking = true;
          try { await draw(); } finally { ticking = false; }
        }

        // ?demo=1 (stream overlay plan section 4, step 4): a made-up race on
        // a loop - between races, the grid, two minutes of racing with a
        // battle, fastest laps and a lead change, then the result - so a
        // layout can be set up on a weeknight with nothing on track. Made
        // here in the page: it never comes from, or goes into, the night's
        // real data.
        // ?phone=1 (overlay plan 6.8): the overlay as most viewers see it -
        // the whole 1920 x 1080 picture shrunk to a phone held sideways.
        const PHONE = params.get('phone') === '1' && window.top === window;
        if (PHONE){
          const inner = new URLSearchParams(location.search); inner.delete('phone');
          document.body.innerHTML = '<div style="position:fixed;inset:0;background:#2b3a2b;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;font:14px system-ui;color:#ddd">' +
            '<iframe src="?' + esc(inner.toString()) + '" style="width:390px;height:219px;border:2px solid #000;border-radius:10px;background:#556b4a"></iframe>' +
            '<div>About what a viewer sees on a phone held sideways (390 px wide).</div></div>';
        }
        const DEMO = params.get('demo') === '1';
        // Whether the sponsor graphic is up: said on the next poll, so the
        // laptop counts the time its logo is really on screen (section 5).
        let sponsorShowing = false;
        // "Presented by" graphics put up since the last poll, by moment name.
        const graphicsShown = new Set();
        function shownBy(d, moment){ if (!DEMO && presentedBy(d, moment)) graphicsShown.add(moment); }
        const DEMO_NAMES = ['Mia Kent', 'Sam Tr', 'Terry Byte', 'Al Gorithim', 'Ella Vator', 'Annie Wayout', 'Rob Otics', 'Cy Clone'];
        // &at=60 starts the loop that many seconds in: 0 between races, 20
        // the grid, 35 the start, 95 a lead change, 140 the result.
        // A page opened from a file has no laptop to ask.
        const DEMO_ONLY_STATIC = location.protocol === 'file:';
        const DEMO_START = Date.now() - Math.max(0, Number(params.get('at')) || 0) * 1000;
        // "about 19:40", that many minutes from now, as the laptop writes it.
        function demoAbout(minutes){ const t = new Date(Date.now() + minutes * 60000); return 'about ' + String(t.getHours()).padStart(2, '0') + ':' + String(t.getMinutes()).padStart(2, '0'); }
        function demoState(){
          const loop = 160, t = ((Date.now() - DEMO_START) / 1000) % loop;
          const people = DEMO_NAMES.map((name, i) => ({ id: 'd' + i, name }));
          const base = { sponsorUp: { name: 'Hobby Hut', logo: '' }, race: 'Junior - Heat 2', people, presented: {}, presentedLogos: {}, champ: null, ratings: {}, captions: [], pylon: { changes: 0, leaders: 1, cars: {} } };
          if (t < 20) return Object.assign(base, { state: 'Idle', flag: 'None', countdown: '', standings: [], cards: {}, onDeck: '19:40  •  Junior  •  Heat 2',
            upNext: [
              { title: 'Heat 2 (Junior)', eta: demoAbout(8), drivers: DEMO_NAMES.slice(0, 6), marshals: [{ name: 'Rob Otics', post: 'Turn 1' }, { name: 'Cy Clone', post: 'Turn 3' }] },
              { title: 'Heat 2 (Stock)', eta: demoAbout(16), drivers: [], marshals: [] },
              { title: 'Heat 2 (Mod Buggy)', eta: demoAbout(24), drivers: [], marshals: [] },
            ] });
          if (t < 35) {
            const callups = {}; people.forEach((p, i) => { callups[p.id] = { now: true, slot: i + 1 }; });
            return Object.assign(base, { state: 'Staging', flag: 'None', countdown: '', standings: [], cards: {}, callups });
          }
          const racing = t < 140, el = Math.min(t, 140) - 35, finishing = racing && el >= 105;
          const order = people.map((p, i) => ({ p, i, pace: 9.6 + i * 0.07 + Math.sin(el / 9 + i) * 0.05 }));
          // Sam takes the lead a minute in.
          if (el > 60) { const a = order[0]; order[0] = order[1]; order[1] = a; }
          const standings = [], cards = {}, cars = {};
          order.forEach((o, pos) => {
            const laps = Math.floor(el / o.pace);
            standings.push({ id: o.p.id, name: o.p.name, best: Math.round((o.pace - 0.2) * 1000) });
            const ms = Math.round(laps * o.pace * 1000), m = Math.floor(ms / 60000);
            const result = laps > 0 ? laps + '/' + m + ':' + String(Math.floor(ms / 1000) % 60).padStart(2, '0') + '.' + String(ms % 1000).padStart(3, '0') : '';
            cards[o.p.id] = { laps, result, done: finishing && pos <= (el - 105) / 3 || !racing,
              ahead: pos === 0 ? '' : pos === 7 ? '+1L' : '+' + (pos === 1 ? (0.4 + Math.abs(Math.sin(el / 6)) * 0.4) : 0.9 + pos * 0.6).toFixed(3) };
            // A last lap that is now and then the driver's best, and once in a while the race's.
            const best = Math.round((o.pace - 0.2) * 1000), beat = Math.floor(el / 4) % 5;
            const last = beat === o.i % 5 ? best : best + 120 + Math.round(Math.abs(Math.sin(el + o.i)) * 300);
            cars[o.p.id] = { last, col: ['#E53935', '#1E88E5', '#FDD835', '#43A047', '#FB8C00', '#8E24AA', '#FFFFFF', '#212121'][o.i], num: String(pos + 3), lead: pos === 0 ? '' : '+' + (pos * 1.1).toFixed(3), led: pos < 2 && el > 60 ? 4 : pos === 0 ? Math.floor(el / 10) : 0 };
          });
          const left = Math.max(0, 105 - el);
          const clock = racing ? Math.floor(left / 60) + ':' + String(Math.floor(left % 60)).padStart(2, '0') : '';
          const moment = el > 61 ? { seq: 1, kind: 'lead', title: 'Lead change', line: 'Sam Tr takes the lead from Mia Kent', sponsor: '' } : null;
          return Object.assign(base, { state: racing ? 'Racing' : 'Finished', flag: racing ? 'Green' : 'Checkered', countdown: clock, standings, cards, moment, finishing,
            pylon: { changes: el > 60 ? 1 : 0, leaders: el > 60 ? 2 : 1, cars } });
        }

        async function draw(){
          let d;
          if (DEMO) d = demoState();
          else {
            const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
            // What it showed since the last poll goes with this one (section 5).
            const said = [];
            if (sponsorShowing) said.push('sponsor=1');
            if (graphicsShown.size) said.push('shown=' + encodeURIComponent([...graphicsShown].join(',')));
            graphicsShown.clear();
            try { d = await (await fetch('/api/state' + (said.length ? '?' + said.join('&') : ''), { cache: 'no-store', signal: stop.signal })).json(); }
            catch(e) { return; }
            finally { clearTimeout(timer); }
          }
          SN = d.streamNames && typeof d.streamNames === 'object' ? d.streamNames : {};
          showCaptions(d.captions);
          PART = partOf(d.state);
          const racing = d.state === 'Racing', finished = d.state === 'Finished';
          const rows = (d.standings || []).filter(s => s.id);
          const cards = d.cards || {};
          const pylon = d.pylon || { changes: 0, leaders: 0, cars: {} };
          const pcars = pylon.cars || {};

          // The pylon - each row slides to its new place.
          const tower = document.getElementById('tower');
          const showTower = want('tower') && (racing || finished) && rows.length > 0;
          tower.style.opacity = showTower ? 1 : 0;
          // The number column only when somebody has a number - an empty
          // column down every row would be a gap with nothing in it.
          tower.classList.toggle('nums', rows.slice(0, SHOWN).some(s => (pcars[s.id] || {}).num));
          document.getElementById('title').textContent = d.race || 'Live';
          document.getElementById('clock').textContent = d.finishing ? 'FINAL LAP' : (d.countdown || '');
          const mode = gapMode(racing, pylon);
          document.getElementById('mode').textContent = mode === 'leader' ? 'To leader' : mode === 'led' ? 'Laps led' : mode === 'last' ? 'Last lap' : mode === 'overall' ? 'Overall' : 'Interval';
          document.getElementById('changes').textContent = pylon.changes > 0
            ? pylon.changes + (pylon.changes === 1 ? ' lead change' : ' lead changes') + (pylon.leaders > 1 ? ' · ' + pylon.leaders + ' leaders' : '')
            : '';
          const box = document.getElementById('rows'), rowH = window.innerWidth * 0.023;
          const seen = {};
          rows.slice(0, SHOWN).forEach((s, i) => {
            seen[s.id] = true;
            let el = rowEls[s.id];
            if (!el){
              el = document.createElement('div'); el.className = 'row';
              el.innerHTML = '<span class="p"></span><span class="num"></span><span class="sw"></span><span class="n"></span><span class="g"></span>';
              el.style.top = (SHOWN * rowH) + 'px';
              box.appendChild(el); rowEls[s.id] = el;
            }
            const c = cards[s.id] || {}, pc = pcars[s.id] || {};
            el.children[0].textContent = i + 1;
            el.children[1].textContent = pc.num || '';
            // The car's colour (6.5), only for a car that has one.
            const col = /^#[0-9A-F]{6}$/.test(pc.col || '') ? pc.col : '';
            el.children[2].style.background = col;
            el.children[2].style.display = col ? '' : 'none';
            el.children[3].textContent = sn(s.name);
            let g;
            let pace = '';
            if (mode === 'last'){
              g = pc.last > 0 ? fmt(pc.last) : '';
              const quickest = rows.reduce((m, r) => r.best > 0 && !r.ghost && (!m || r.best < m) ? r.best : m, 0);
              pace = pc.last > 0 && pc.last === quickest ? 'purple' : pc.last > 0 && pc.last === s.best ? 'green' : '';
            }
            else if (mode === 'overall') g = pc.qual || '';
            else if (mode === 'led') g = pc.led > 0 ? pc.led + ' led' : '';
            else if (i === 0) g = 'Lap ' + (c.laps || 0);
            else g = mode === 'leader' ? (pc.lead || c.ahead || '') : (c.ahead || '');
            // Home (stream overlay plan 6.2, 6.3): a car that has taken the
            // checkered shows its result the RC way, 23/5:02.341, and keeps it.
            if ((c.done || finished) && c.result) g = c.result;
            el.children[4].textContent = g;
            el.children[4].className = 'g' + (pace ? ' ' + pace : '');
            el.classList.toggle('done', !!c.done || finished);
            el.classList.toggle('up', lastPos[s.id] !== undefined && i < lastPos[s.id]);
            el.classList.toggle('fl', best.id === s.id && best.ms > 0);
            el.classList.toggle('led', (pc.led || 0) > 0);
            el.style.top = (i * rowH) + 'px';
            lastPos[s.id] = i;
          });
          Object.keys(rowEls).forEach(id => { if (!seen[id]) { rowEls[id].remove(); delete rowEls[id]; delete lastPos[id]; } });
          box.style.height = (Math.min(rows.length, SHOWN) * rowH) + 'px';

          // If it ended now, under the tower.
          const ch = document.getElementById('champ');
          if (want('champ') && racing && d.champ && d.champ.rows && d.champ.rows.length){
            ch.style.top = (showTower ? tower.offsetTop + tower.offsetHeight + window.innerWidth * 0.006 : window.innerWidth * 0.03) + 'px';
            ch.innerHTML = '<b class="acc">IF IT ENDED NOW</b><br>' + esc(d.champ.headline);
            ch.style.display = 'block';
          } else ch.style.display = 'none';

          // The closest battle in the top places, under a second - and,
          // while it is the same two cars, how long they have been at it
          // and which way the gap is going.
          const battle = document.getElementById('battle');
          let close = null;
          if (racing){
            rows.slice(1, TOP).forEach((s, i) => {
              const g = gapSecs((cards[s.id] || {}).ahead);
              if (g !== null && g < 1.0 && (!close || g < close.g)) close = { g, a: rows[i], b: s, pos: i + 1 };
            });
          }
          if (close){
            const key = close.a.id + '|' + close.b.id;
            const laps = (cards[close.b.id] || {}).laps || 0;
            if (!fight || fight.key !== key) fight = { key, fromLap: laps, gaps: [] };
            fight.gaps.push(close.g);
            if (fight.gaps.length > 6) fight.gaps.shift();
            fight.laps = Math.max(0, laps - fight.fromLap);
          } else fight = null;
          if (want('battle') && close){
            let trend = '';
            if (fight && fight.gaps.length >= 4){
              const moved = fight.gaps[fight.gaps.length - 1] - fight.gaps[0];
              if (moved <= -0.05) trend = '<span class="trend closing">▼ CLOSING</span>';
              else if (moved >= 0.05) trend = '<span class="trend pulling">▲ PULLING AWAY</span>';
            }
            const together = fight && fight.laps >= 2 ? '<span class="trend">' + fight.laps + ' LAPS TOGETHER</span>' : '';
            const by = presentedBy(d, 'Battle');
            // The trend, the laps and the sponsor on a line of their own
            // under the names, so the box stays as wide as the fight and
            // never runs into the fastest-lap box beside it.
            const meta = trend + together + (by ? '<span class="trend">' + esc(by.toUpperCase()) + byLogo(d, 'Battle') + '</span>' : '');
            battle.innerHTML = (close.pos === 1 ? 'BATTLE FOR THE LEAD' : 'BATTLE FOR P' + close.pos) + ' &nbsp; ' + esc(sn(close.a.name)) + ' <span class="acc">vs</span> ' + esc(sn(close.b.name)) +
              '<span class="gap">' + close.g.toFixed(3) + 's</span>' + (meta ? '<span class="meta">' + meta + '</span>' : '');
            if (!battle.classList.contains('show')) shownBy(d, 'Battle');
            battle.classList.add('show');
          } else battle.classList.remove('show');

          // A new fastest lap of the race - not the ones already set when the page opened.
          let top = { ms: 0, id: '' };
          // Ghosts set the pace; they don't hold the fastest lap. Told by
          // the row's own flag rather than by the GHOST- on its id: the ids
          // in /api/state are opaque now (see AddIdsAndGrid), and reading a
          // prefix that is no longer there put a ghost's lap on the stream.
          rows.forEach(s => { if (s.best > 0 && !s.ghost && (!top.ms || s.best < top.ms)) top = { ms: s.best, id: s.id }; });
          if (!racing && !finished) top = { ms: 0, id: '' };
          if (top.ms > 0 && (top.ms < best.ms || !best.ms) && !first && racing && want('fastest')){
            const row = rows.find(s => s.id === top.id);
            const by = presentedBy(d, 'FastestLap');
            document.getElementById('fhead').innerHTML = 'FASTEST LAP' + (by ? ' · ' + esc(by.toUpperCase()) + byLogo(d, 'FastestLap') : '');
            document.getElementById('fname').textContent = row ? sn(row.name) : '';
            document.getElementById('ftime').textContent = fmt(top.ms);
            const f = document.getElementById('fastest'); f.classList.add('show');
            shownBy(d, 'FastestLap');
            clearTimeout(fastTimer); fastTimer = setTimeout(() => f.classList.remove('show'), 6000);
          }
          best = top;

          // A banner for each new moment - a lead change, a milestone.
          // Only for one that arrives while the page is open: a page loaded
          // (or reloaded in OBS) takes the current number as already seen.
          // Every new moment is queued and shown in turn (overlay plan 7.3):
          // several badges land at once after a race. A number lower than
          // the last seen is HeatCaller started again - counted afresh.
          const list = Array.isArray(d.moments) && d.moments.length ? d.moments : (d.moment ? [d.moment] : []);
          const highest = list.reduce((n, m) => Math.max(n, Number(m.seq) || 0), 0);
          if (highest < momentSeq) momentSeq = 0;
          for (const m of list){
            if (!m || !(Number(m.seq) > momentSeq)) continue;
            momentSeq = Number(m.seq);
            if (!first && want('moment')) momentQueue.push(m);
          }
          if (momentQueue.length > 6) momentQueue.splice(0, momentQueue.length - 6);
          showNextMoment();

          // The grid, before the start.
          const grid = document.getElementById('grid');
          const onGrid = Object.entries(d.callups || {}).filter(([, c]) => c.now).sort((x, y) => x[1].slot - y[1].slot);
          if (want('grid') && (d.state === 'Staging' || d.state === 'Countdown') && onGrid.length){
            const by = presentedBy(d, 'DriverIntros');
            grid.innerHTML = '<h2>' + esc(d.race || 'The grid') + (by ? '<span class="by">' + esc(by) + byLogo(d, 'DriverIntros') + '</span>' : '') + '</h2>' +
              onGrid.map(([id, c]) => {
                const num = (pcars[id] || {}).num;
                return '<div class="line"><b>' + c.slot + '</b>' + (num ? '<span class="acc">#' + esc(num) + '</span> ' : '') + swatch(d, id) + esc(nameOf(d, id)) + '</div>';
              }).join('');
            if (!grid.classList.contains('show')) shownBy(d, 'DriverIntros');
            grid.classList.add('show');
          } else grid.classList.remove('show');

          // The podium, at the end - with each driver's rating for the race.
          const podium = document.getElementById('podium');
          // WHY (20 Oct 2026): the podium was the top three of the whole
          // board, ghosts included, so a ghost that beat the field went out
          // on the stream as the winner and the real winner showed as P2
          // with P1's rating. Only real cars now, numbered by their place
          // among real cars - the same places the ratings are worked out
          // for. A field of nothing but ghosts shows no podium.
          const podiumRows = rows.filter(s => !s.ghost);
          if (want('podium') && finished && podiumRows.length){
            const ratings = d.ratings || {};
            const by = presentedBy(d, 'RaceResult');
            // PROVISIONAL until the protest window closes, CORRECTED when
            // the order changes while it is up (overlay plan 6.9).
            const podiumKey = (d.race || '') + '|' + podiumRows.slice(0, 3).map(s => s.id).join(',');
            if (!podiumSeen || podiumSeen.race !== (d.race || '')) podiumSeen = { race: d.race || '', key: podiumKey, corrected: false };
            else if (podiumSeen.key !== podiumKey) { podiumSeen.key = podiumKey; podiumSeen.corrected = true; }
            const until = Date.parse(d.provisionalUntil || '');
            const badge = podiumSeen.corrected ? '<span class="prov fix">Corrected</span>' : Number.isFinite(until) && Date.now() < until ? '<span class="prov">Provisional</span>' : '';
            podium.innerHTML = '<h2>' + esc(d.race || 'Result') + badge + (by ? '<span class="by">' + esc(by) + byLogo(d, 'RaceResult') + '</span>' : '') + '</h2>' + podiumRows.slice(0, 3).map((s, i) =>
              '<div class="line"><b>' + ordinal(i + 1) + '</b>' + swatch(d, s.id) + esc(sn(s.name)) + '<span class="t">' + esc((cards[s.id] || {}).result || (((cards[s.id] || {}).laps || 0) + ' laps')) + ' &nbsp; best ' + fmt(s.best) + '</span>' +
              (ratings[s.id] !== undefined ? '<span class="r">RTG ' + Number(ratings[s.id]).toFixed(1) + '</span>' : '') + '</div>').join('');
            if (!podium.classList.contains('show')) shownBy(d, 'RaceResult');
            podium.classList.add('show');
          } else podium.classList.remove('show');

          drawLayoutParts(d, rows, cards, racing, finished);
          first = false;
        }

        // NEW TO RC RACING? (overlay plan 7.6): the basics and a few words,
        // one card every twelve seconds between races.
        // The club's own cards, or HeatCaller's (StreamBasics).
        const BASICS = {{basicsJson}};

        // --- The layout's own graphics (stream overlay plan section 2) ---
        // The last minute of a timed race, or the last lap of a lap race,
        // read from the countdown the laptop writes ("0:45", "1 LAP TO GO").
        function lastMinute(text){
          const t = String(text || '').toUpperCase();
          if (/FINAL LAP|1 LAP TO GO|LAST LAP/.test(t)) return true;
          const m = /(\d+):(\d{2})/.exec(t);
          return !!m && Number(m[1]) * 60 + Number(m[2]) < 60;
        }
        function show(id, on){ const el = document.getElementById(id); if (el) el.classList.toggle('show', !!on); return el; }
        function setText(id, text){ const el = document.getElementById(id); if (el && el.textContent !== text) el.textContent = text; }
        let tickerHtml = '';

        function drawLayoutParts(d, rows, cards, racing, finished){
          const onTrack = racing || finished;
          const pcars = (d.pylon && d.pylon.cars) || {};
          const leader = rows.length ? rows[0] : null;

          // The race and its clock, small, in a corner.
          const status = want('status') && (onTrack || PART === 'staging');
          if (status){ setText('st-race', d.race || ''); setText('st-clock', d.finishing ? 'Final lap' : (d.countdown || (d.flag && d.flag !== 'None' ? d.flag + ' flag' : ''))); }
          show('status', status);

          // One bar of positions, for a camera that needs the picture.
          const ticker = want('ticker') && onTrack && rows.length > 0;
          if (ticker){
            setText('tk-race', d.race || 'Live');
            const n = (PARTS.ticker && PARTS.ticker.rows) || 10;
            const html = rows.slice(0, n).map((s, i) => {
              const c = cards[s.id] || {};
              const g = (c.done || finished) && c.result ? c.result : i === 0 ? 'Lap ' + (c.laps || 0) : (c.ahead || '');
              const col = /^#[0-9A-F]{6}$/.test((pcars[s.id] || {}).col || '') ? pcars[s.id].col : '';
              return '<span class="tk-item"><b>' + (i + 1) + '</b>' + (col ? '<span class="sw" style="background:' + col + ';margin-right:.4vw"></span>' : '') + esc(sn(s.name)) + (g ? '<span class="g">' + esc(g) + '</span>' : '') + '</span>';
            }).join('');
            if (html !== tickerHtml){
              tickerHtml = html;
              const items = document.getElementById('tk-items'), track = document.getElementById('tk-track');
              items.innerHTML = html;
              // Too many to fit: drift along and back, slowly.
              const over = items.scrollWidth - track.clientWidth;
              items.classList.toggle('scroll', over > 4);
              items.style.setProperty('--tk-d', over > 4 ? (-over) + 'px' : '0px');
              items.style.setProperty('--tk-s', Math.max(8, Math.round(over / 40)) + 's');
            }
          }
          show('ticker', ticker);

          // The corner bug: the race, the clock and the leader - or, with
          // nothing on track, what is up next.
          const bug = want('bug') && (onTrack || PART === 'staging' || d.onDeck);
          if (bug){
            if (onTrack || PART === 'staging'){
              setText('bg-race', d.race || 'Live');
              setText('bg-clock', d.finishing ? 'Final lap' : (d.countdown || ''));
              setText('bg-lead', leader ? (finished ? 'Won by ' : 'Leader ') + sn(leader.name) : '');
            } else {
              setText('bg-race', 'Up next');
              setText('bg-clock', '');
              setText('bg-lead', d.onDeck || '');
            }
          }
          show('bug', bug);

          // The club's own logo, when it has one.
          const logo = document.querySelector('#clublogo img');
          show('clublogo', want('clublogo') && !!logo);

          // What's next, between races.
          const sched = document.getElementById('schedule');
          // The next three races (6.7): when, who drives, who marshals.
          const up = Array.isArray(d.upNext) ? d.upNext.filter(r => r && r.title) : [];
          const over = want('schedule') && PART === 'between' && up.length === 0 && !d.onDeck && (Number(d.racedTonight) || 0) > 0;
          if (over){
            const html = '<h2>That\'s it for tonight</h2><div class="line">Thanks for watching. The results are on the club\'s page.</div>';
            if (sched.innerHTML !== html) sched.innerHTML = html;
          }
          const next = !over && want('schedule') && PART === 'between' && (up.length > 0 || d.onDeck);
          if (next){
            const raced = Number(d.racedTonight) || 0;
            // Never looks broken (7.6): before the first race it is "starting
            // soon", between races "back shortly", and after the last "that's it".
            const head = raced === 0 ? 'Starting soon' : 'Back shortly · up next';
            const html = '<h2>' + head + '</h2>' + (up.length ? up.map((r, i) => {
              const drivers = Array.isArray(r.drivers) ? r.drivers : [];
              const marshals = Array.isArray(r.marshals) ? r.marshals.filter(m => m && m.name) : [];
              return '<div class="line"><b>' + (i + 1) + '</b>' + esc(r.title) + (r.eta ? '<span class="when">' + esc(r.eta) + '</span>' : '') + '</div>' +
                (i === 0 && drivers.length ? '<div class="who">' + drivers.map(n => esc(sn(n))).join(', ') + '</div>' : '') +
                (i === 0 && marshals.length ? '<div class="who mar">Marshals: ' + marshals.map(m => esc(sn(m.name)) + (m.post ? ' (' + esc(m.post) + ')' : '')).join(', ') + '</div>' : '');
            }).join('') : '<div class="line">' + esc(d.onDeck) + '</div>');
            if (sched.innerHTML !== html) sched.innerHTML = html;
          }
          if (sched) sched.classList.toggle('show', !!next || over);

          // The basics, a card at a time (7.6), only with time to read them.
          const firstEta = up.length && /about (\d{1,2}):(\d{2})/.exec(up[0].eta || '');
          let minutesToGo = 99;
          if (firstEta){ const at = new Date(); at.setHours(Number(firstEta[1]), Number(firstEta[2]), 0, 0); minutesToGo = (at - Date.now()) / 60000; }
          const basics = want('basics') && PART === 'between' && !over && minutesToGo >= 2;
          if (basics){
            // Tonight's bests (7.3) join the turns once there are some:
            // "Mia K: First podium".
            const bests = Array.isArray(d.bests) ? d.bests.filter(b => b && b.title).slice(-4) : [];
            const cards = bests.length
              ? BASICS.concat([['Tonight\u2019s bests', bests.map(b => snText(String(b.line || '').split(' \u00b7 ')[0]) + ': ' + b.title).join(' \u00b7 ')]])
              : BASICS;
            const card = cards[Math.floor(Date.now() / 12000) % cards.length];
            setText('bs-k', card[0]);
            setText('bs-t', card[1]);
          }
          show('basics', basics);

          // The sponsor up now (section 5) - held back in the last minute,
          // on the final lap and while the battle box is up, so a logo is
          // never what hid the finish.
          const battleUp = document.getElementById('battle').classList.contains('show');
          const busy = racing && (d.finishing || lastMinute(d.countdown) || battleUp);
          const sp = d.sponsorUp && typeof d.sponsorUp.name === 'string' ? d.sponsorUp : null;
          const showSponsor = want('sponsor') && !!sp && !busy;
          if (showSponsor){
            const img = document.getElementById('sp-logo');
            const url = typeof sp.logo === 'string' && LOGO_URL.test(sp.logo) ? sp.logo : '';
            if (url && img.getAttribute('src') !== url) img.setAttribute('src', url);
            if (!url) img.removeAttribute('src');
            img.setAttribute('alt', sp.name);
            setText('sp-name', url ? '' : sp.name);
          }
          show('sponsor', showSponsor);
          sponsorShowing = showSponsor && !DEMO;

          // HeatCaller's mark, always somewhere on the stream: in the
          // tower's foot while the tower shows, or here, where the layout put it.
          const towerShowing = document.getElementById('tower').style.opacity === '1';
          show('mark', want('mark') && (placed('mark') || !towerShowing && !PARTS.tower));
        }

        // A CLUB'S OWN LAYOUT REDRAWS AS IT IS EDITED (section 4): every three
        // seconds it asks whether it has changed, and reloads when it has -
        // or when it has been deleted, to say so in words.
        if (!PHONE && LAYOUT && LAYOUT.custom && !DEMO_ONLY_STATIC){
          let layoutSeen = null;
          setInterval(async () => {
            try {
              const a = await (await fetch('/api/overlay-version?id=' + encodeURIComponent(LAYOUT.id), { cache: 'no-store' })).json();
              const now = a.v + '|' + a.here;
              if (layoutSeen === null) layoutSeen = now;
              else if (now !== layoutSeen) location.reload();
            } catch (e) { /* the laptop is away; try again */ }
          }, 3000);
        }

        // The phone view only frames the overlay; the frame does the drawing.
        if (!PHONE) { setInterval(tick, 1000); tick(); }
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }
}
