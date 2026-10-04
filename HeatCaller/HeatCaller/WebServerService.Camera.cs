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
        <script>
        const FPS = 15, KEEP_MS = 4000, BEFORE = 1000, AFTER = 500;
        const video = document.getElementById('video');
        const grab = document.createElement('canvas');
        const gx = grab.getContext('2d');
        const cams = document.getElementById('cams');
        const every = document.getElementById('every');
        const CAM_KEY = 'hcCamera', EVERY_KEY = 'hcCameraEvery';
        let frames = [], stream = null, last = -1, run = '', pending = [], keptCount = 0, cameraOk = false, appOk = false, polling = false;
        const queued = new Set();

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
          const d = new Date(t), stamp = d.toLocaleTimeString([], { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
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
            if (last < 0 || d.run !== run) { last = d.latest; run = d.run; queued.clear(); pending = []; }
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
          try { await send(); } finally { polling = false; }
        }
        setInterval(poll, 250);

        async function send(){
          const now = Date.now();
          const ready = pending.filter(s => now >= s.at + AFTER + 150);
          pending = pending.filter(s => now < s.at + AFTER + 150);

          // Every ready shot's frames picked now, before any is sent: a pack
          // crossing together is many uploads, and the frames before the
          // later shots would be gone from the buffer by their turn.
          const jobs = ready.map(s => ({ s, picked: frames.filter(f => f.t >= s.at - BEFORE && f.t <= s.at + AFTER) }));
          for (const { s, picked } of jobs){
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
    /// 1920x1080 over the camera): a timing tower down the left, the
    /// closest battle along the bottom, a pop-up for each new fastest lap,
    /// the grid before the start, the podium at the end, and the
    /// championship as it stands if it ended now. Transparent everywhere
    /// else. Parts can be left out with ?hide=tower,battle,fastest,grid,podium,champ.
    /// </summary>
    private string BuildBroadcastPage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Broadcast Graphics</title><style>{{look.Root}}
        * { box-sizing:border-box; }
        html, body { margin:0; background:transparent; overflow:hidden; width:100vw; height:100vh;
                     font-family:'Bahnschrift','Segoe UI',system-ui,sans-serif; color:#fff; }
        /* Everything is sized in vw so the graphics fit any canvas size. */
        .glass { background:rgba(10,12,18,.82); border-radius:.5vw; box-shadow:0 .3vw 1.2vw rgba(0,0,0,.35); }
        .acc { color:var(--accent); }
        #tower { position:absolute; left:2vw; top:3vw; width:21vw; overflow:hidden; transition:opacity .4s; }
        #tower .head { padding:.7vw 1vw; font-size:1vw; letter-spacing:.12em; font-weight:700; display:flex; justify-content:space-between;
                       border-bottom:.15vw solid var(--accent); text-transform:uppercase; }
        #rows { position:relative; }
        .row { position:absolute; left:0; right:0; height:2.3vw; display:flex; align-items:center; font-size:1.15vw;
               transition:top .6s ease, background .6s; padding:0 1vw; gap:.8vw; }
        .row .p { width:1.8vw; font-weight:800; text-align:right; }
        .row .n { flex:1; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; font-weight:600; }
        .row .g { color:#c9cdd6; font-variant-numeric:tabular-nums; }
        .row.up { background:rgba(74,222,128,.25); }
        .row.fl .n::after { content:' \25C6'; color:#B06CFF; font-size:.8vw; }
        #champ { position:absolute; left:2vw; width:21vw; padding:.6vw 1vw; font-size:.9vw; line-height:1.4; display:none; }
        #battle { position:absolute; left:50%; bottom:3vw; transform:translateX(-50%) translateY(8vw); padding:.8vw 1.6vw;
                  font-size:1.4vw; font-weight:700; white-space:nowrap; transition:transform .5s ease; }
        #battle.show { transform:translateX(-50%) translateY(0); }
        #battle .gap { color:var(--accent); margin-left:1vw; font-variant-numeric:tabular-nums; }
        #fastest { position:absolute; right:2vw; bottom:3vw; padding:.9vw 1.4vw; font-size:1.3vw; font-weight:700;
                   transform:translateX(30vw); transition:transform .5s ease; border-left:.4vw solid #B06CFF; }
        #fastest.show { transform:translateX(0); }
        #fastest small { display:block; font-size:.8vw; letter-spacing:.14em; color:#B06CFF; }
        .centre { position:absolute; left:50%; top:50%; transform:translate(-50%,-50%) scale(.9); opacity:0;
                  transition:opacity .5s, transform .5s; padding:1.6vw 2.4vw; min-width:30vw; }
        .centre.show { opacity:1; transform:translate(-50%,-50%) scale(1); }
        .centre h2 { margin:0 0 1vw; font-size:1.6vw; letter-spacing:.12em; text-transform:uppercase; border-bottom:.15vw solid var(--accent); padding-bottom:.6vw; }
        .centre .line { font-size:1.4vw; padding:.35vw 0; display:flex; gap:1vw; }
        .centre .line b { width:2.4vw; text-align:right; color:var(--accent); }
        #podium .line { font-size:1.8vw; }
        #podium .line span.t { margin-left:auto; color:#c9cdd6; font-size:1.2vw; align-self:center; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div id="tower" class="glass" style="opacity:0"><div class="head"><span id="title">-</span><span id="clock" class="acc"></span></div><div id="rows"></div></div>
        <div id="champ" class="glass"></div>
        <div id="battle" class="glass"></div>
        <div id="fastest" class="glass"><small>FASTEST LAP</small><span id="fname"></span> <span id="ftime" class="acc"></span></div>
        <div id="grid" class="glass centre"></div>
        <div id="podium" class="glass centre"></div>
        <script>
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        const hide = (new URLSearchParams(location.search).get('hide') || '').toLowerCase().split(',');
        const want = part => !hide.includes(part);
        const SHOWN = 8;
        const rowEls = {};
        let lastPos = {}, best = { ms: 0, id: '' }, fastTimer = 0, first = true;

        function fmt(ms){ return ms > 0 ? (ms/1000).toFixed(3) : '-'; }
        function gapSecs(g){ const m = /^\+(\d+([.,]\d+)?)$/.exec(g || ''); return m ? parseFloat(m[1].replace(',', '.')) : null; }
        function nameOf(d, id){ const p = (d.people || []).find(x => x.id === id); return p ? p.name : id; }

        // One poll at a time, none left hanging - an older answer arriving
        // after a newer one would bring a fastest lap back round.
        let ticking = false;
        async function tick(){
          if (ticking) return;
          ticking = true;
          try { await draw(); } finally { ticking = false; }
        }

        async function draw(){
          let d;
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
          try { d = await (await fetch('/api/state', { cache: 'no-store', signal: stop.signal })).json(); }
          catch(e) { return; }
          finally { clearTimeout(timer); }
          const racing = d.state === 'Racing', finished = d.state === 'Finished';
          const rows = (d.standings || []).filter(s => s.id);
          const cards = d.cards || {};

          // The timing tower - each row slides to its new place.
          const tower = document.getElementById('tower');
          const showTower = want('tower') && (racing || finished) && rows.length > 0;
          tower.style.opacity = showTower ? 1 : 0;
          document.getElementById('title').textContent = d.race || 'Live';
          document.getElementById('clock').textContent = d.countdown || '';
          const box = document.getElementById('rows'), rowH = window.innerWidth * 0.023;
          const seen = {};
          rows.slice(0, SHOWN).forEach((s, i) => {
            seen[s.id] = true;
            let el = rowEls[s.id];
            if (!el){
              el = document.createElement('div'); el.className = 'row';
              el.innerHTML = '<span class="p"></span><span class="n"></span><span class="g"></span>';
              el.style.top = (SHOWN * rowH) + 'px';
              box.appendChild(el); rowEls[s.id] = el;
            }
            const c = cards[s.id] || {};
            el.children[0].textContent = i + 1;
            el.children[1].textContent = s.name;
            el.children[2].textContent = i === 0 ? ('Lap ' + (c.laps || 0)) : (c.ahead || '');
            el.classList.toggle('up', lastPos[s.id] !== undefined && i < lastPos[s.id]);
            el.classList.toggle('fl', best.id === s.id && best.ms > 0);
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

          // The closest battle in the top places, under a second.
          const battle = document.getElementById('battle');
          let close = null;
          if (racing){
            rows.slice(1, SHOWN).forEach((s, i) => {
              const g = gapSecs((cards[s.id] || {}).ahead);
              if (g !== null && g < 1.0 && (!close || g < close.g)) close = { g, a: rows[i], b: s, pos: i + 1 };
            });
          }
          if (want('battle') && close){
            battle.innerHTML = 'BATTLE FOR P' + close.pos + ' &nbsp; ' + esc(close.a.name) + ' <span class="acc">vs</span> ' + esc(close.b.name) +
              '<span class="gap">' + close.g.toFixed(2) + 's</span>';
            battle.classList.add('show');
          } else battle.classList.remove('show');

          // A new fastest lap of the race - not the ones already set when the page opened.
          let top = { ms: 0, id: '' };
          // Ghosts set the pace; they don't hold the fastest lap.
          rows.forEach(s => { if (s.best > 0 && !/^GHOST-/i.test(s.id) && (!top.ms || s.best < top.ms)) top = { ms: s.best, id: s.id }; });
          if (!racing && !finished) top = { ms: 0, id: '' };
          if (top.ms > 0 && (top.ms < best.ms || !best.ms) && !first && racing && want('fastest')){
            const row = rows.find(s => s.id === top.id);
            document.getElementById('fname').textContent = row ? row.name : '';
            document.getElementById('ftime').textContent = fmt(top.ms);
            const f = document.getElementById('fastest'); f.classList.add('show');
            clearTimeout(fastTimer); fastTimer = setTimeout(() => f.classList.remove('show'), 6000);
          }
          best = top;

          // The grid, before the start.
          const grid = document.getElementById('grid');
          const onGrid = Object.entries(d.callups || {}).filter(([, c]) => c.now).sort((x, y) => x[1].slot - y[1].slot);
          if (want('grid') && (d.state === 'Staging' || d.state === 'Countdown') && onGrid.length){
            grid.innerHTML = '<h2>' + esc(d.race || 'The grid') + '</h2>' +
              onGrid.map(([id, c]) => '<div class="line"><b>' + c.slot + '</b>' + esc(nameOf(d, id)) + '</div>').join('');
            grid.classList.add('show');
          } else grid.classList.remove('show');

          // The podium, at the end.
          const podium = document.getElementById('podium');
          if (want('podium') && finished && rows.length){
            podium.innerHTML = '<h2>' + esc(d.race || 'Result') + '</h2>' + rows.slice(0, 3).map((s, i) =>
              '<div class="line"><b>' + (i + 1) + '</b>' + esc(s.name) + '<span class="t">' + ((cards[s.id] || {}).laps || 0) + ' laps &nbsp; best ' + fmt(s.best) + '</span></div>').join('');
            podium.classList.add('show');
          } else podium.classList.remove('show');

          first = false;
        }

        setInterval(tick, 1000);
        tick();
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }
}
