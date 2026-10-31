using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// THE PHONE COACH PAGE (22 Oct 2026): /coach on the club's own Wi-Fi.
///
/// One driver, picked once and remembered on the phone - the same pick the
/// Live Results page's Follow uses, so a phone already following somebody
/// opens straight on them. It shows their laps this session as they come
/// in, the last against their best, the trend of their last ten, the gap
/// to their target lap when race control has set one (DriverTargets), and a flash and a
/// buzz on a new best. Under it, their report card for the night
/// (CoachReport).
///
/// NOTHING NEW ON THE WIRE ABOUT ANYBODY. The live part reads /api/state,
/// the spectator board every phone already polls: each row's lap list,
/// best lap and opaque id are on it already (see AddFollowing for why the
/// ids are the roster's, never a transponder). The only new route is
/// /api/coach, the report card - and that is worked out on the UI thread
/// and handed over here (PublishCoachCards), with full names as the board
/// shows them and WITHOUT the setup line: anybody on the Wi-Fi can open any
/// driver's card here, and what somebody changed on their car is theirs to
/// tell. Their own night, keyed by their badge, carries it.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>The report cards, by transponder - replaced whole by PublishCoachCards, never changed in place.</summary>
    private volatile IReadOnlyDictionary<string, IReadOnlyList<string>> _coachCards =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tonight's report cards, by transponder, for /api/coach to serve.
    /// Called on the UI thread when a race or practice is filed - see
    /// MainForm.PublishCoachCards. The dictionary is copied, so the caller
    /// is free to reuse its own.
    /// </summary>
    public void PublishCoachCards(IReadOnlyDictionary<string, IReadOnlyList<string>> byTransponder)
    {
        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (who, lines) in byTransponder)
        {
            if (!string.IsNullOrWhiteSpace(who) && lines.Count > 0)
            {
                copy[who.Trim()] = lines.ToArray();
            }
        }

        _coachCards = copy;
    }

    /// <summary>
    /// The target lap race control set for a driver tonight, in ms, or null -
    /// see DriverTargets (the pacer). It goes onto each card in AddFollowing
    /// as "target", so the coach page can show the gap to it. DriverTargets
    /// is a concurrent map, so the UI thread building the state may read it.
    /// </summary>
    private static long? TargetLapMsFor(string transponder) => DriverTargets.TargetMs(transponder);

    /// <summary>
    /// /api/coach?id=: one driver's report card, by the roster's opaque id
    /// - the id the board and the Follow list already give them. An id that
    /// matches nobody gets an empty card rather than an error, so a phone
    /// following a driver who has not run yet just shows nothing yet.
    /// </summary>
    private void HandleCoachCard(System.Net.HttpListenerContext context)
    {
        var id = (context.Request.QueryString["id"] ?? string.Empty).Trim().ToLowerInvariant();
        IReadOnlyList<string> lines = Array.Empty<string>();

        if (id.Length == 16 && id.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
        {
            var key = _rosterKey;

            foreach (var (who, card) in _coachCards)
            {
                if (RosterId(key, who) == id)
                {
                    lines = card;
                    break;
                }
            }
        }

        WriteJson(context, JsonSerializer.Serialize(new { ok = true, lines }));
    }

    /// <summary>The page itself - everything live is drawn by its script from /api/state and /api/coach.</summary>
    internal string BuildCoachPage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Coach</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px; max-width:560px; margin:0 auto; }
        .pick { width:100%; padding:12px; font-size:17px; border-radius:8px; font-family:inherit;
                border:1px solid var(--raised); background:var(--surface); color:var(--text); margin-bottom:12px; }
        .big { display:flex; gap:10px; margin-bottom:12px; }
        .tile { flex:1; background:var(--surface); border-radius:10px; padding:12px; text-align:center; }
        .tile .k { color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:.05em; }
        .tile .v { font-size:28px; font-weight:800; font-variant-numeric:tabular-nums; margin-top:2px; }
        .tile .s { font-size:14px; font-weight:700; margin-top:2px; min-height:18px; }
        .up { color:var(--good, #2e9d4f); } .down { color:var(--danger, #d23c3c); }
        .trend { background:var(--surface); border-radius:10px; padding:10px; margin-bottom:12px; }
        .trend svg { width:100%; height:90px; display:block; }
        .laps { list-style:none; padding:0; margin:0 0 12px; }
        .laps li { display:flex; justify-content:space-between; padding:7px 10px; border-bottom:1px solid var(--raised);
                   font-variant-numeric:tabular-nums; }
        .laps li.best { font-weight:800; }
        .card h2 { font-size:16px; margin:16px 0 6px; }
        .card ul { margin:0; padding-left:18px; } .card li { margin:0 0 8px; line-height:1.4; }
        .small { color:var(--muted); font-size:13px; line-height:1.5; }
        .say { display:flex; align-items:center; gap:10px; background:var(--surface); border-radius:10px;
               padding:12px; margin-bottom:12px; font-size:16px; cursor:pointer; }
        .say input { width:22px; height:22px; margin:0; }
        /* WHY (22 Oct 2026): .big and .say set display:flex, which beat the
           browser's own [hidden] rule, so the Target row and the Say row
           showed even when hidden. This puts hidden back in charge. */
        .big[hidden], .say[hidden] { display:none; }
        .flash { animation: flash 1.2s ease-out; }
        @keyframes flash { 0% { background:var(--primary); color:var(--on-primary); } 100% { } }
        /* THE PIT BOARD (22 Oct 2026, driver experience, item 10): the
           numbers a driver glances at from the rostrum, as big as the
           screen allows - a phone taped up sideways at the driver stand. */
        .pbtn { width:100%; padding:12px; font:inherit; font-size:17px; font-weight:700; border-radius:8px; cursor:pointer;
                border:1px solid var(--raised); background:var(--surface); color:var(--text); margin-bottom:12px; }
        .pbtn[hidden] { display:none; }
        #board, #boardTap { position:fixed; inset:0; z-index:50; background:var(--bg); color:var(--text); }
        #board[hidden], #boardTap[hidden] { display:none; }
        #boardTap { display:flex; align-items:center; justify-content:center; text-align:center; padding:24px;
                    font-size:24px; font-weight:800; cursor:pointer; }
        #board { padding:1.5vw; display:flex; flex-direction:column; }
        #board .bhead { display:flex; justify-content:space-between; align-items:center; gap:12px; font-size:max(14px, 2.4vw);
                        font-weight:700; color:var(--muted); min-height:36px; }
        #board .bx { font:inherit; font-size:22px; line-height:1; padding:6px 14px; border-radius:8px; cursor:pointer;
                     border:1px solid var(--raised); background:var(--surface); color:var(--text); }
        .bgrid { flex:1; display:grid; gap:1.2vw; min-height:0;
                 grid-template-columns:3fr 2fr; grid-template-rows:1fr 1fr 1fr;
                 grid-template-areas:"last pos" "last laps" "ahead target"; }
        .bc { background:var(--surface); border-radius:1.2vw; padding:.6vw 1.2vw; display:flex; flex-direction:column;
              justify-content:center; min-width:0; overflow:hidden; }
        .bc .bk { color:var(--muted); font-size:max(12px, 1.6vw); text-transform:uppercase; letter-spacing:.08em; font-weight:700; }
        .bc .bv { font-weight:800; font-variant-numeric:tabular-nums; line-height:1; white-space:nowrap; font-size:7vw; }
        .bc.blast { grid-area:last; } .bc.blast .bv { font-size:min(20vw, 40vh); }
        .bc.bpos { grid-area:pos; } .bc.bpos .bv { font-size:min(14vw, 22vh); }
        .bc.blaps { grid-area:laps; } .bc.bahead { grid-area:ahead; } .bc.btarget { grid-area:target; }
        /* Upright, it stacks: the last lap across the top, the rest below. */
        @media (orientation:portrait) {
          .bgrid { grid-template-columns:1fr 1fr; grid-template-rows:2fr 1fr 1fr;
                   grid-template-areas:"last last" "pos laps" "ahead target"; }
          .bc .bv { font-size:11vw; } .bc.bpos .bv { font-size:14vw; }
        }
        /* Slower than the target: the last lap goes red. A new best: the
           whole board flashes green. */
        #board.slow .blast { background:var(--bad); color:#fff; }
        #board.slow .blast .bk { color:#fff; }
        #board.best .bc { animation: bestflash 1.4s ease-out; }
        @keyframes bestflash { 0% { background:var(--good); color:#fff; } 100% { } }
        #board .bnote { color:var(--muted); font-size:max(13px, 1.6vw); padding-top:.6vw; min-height:1em; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}COACH</h1></div>
        <div class="wrap">
          <select class="pick" id="pick" aria-label="Driver"><option value="">Pick your driver...</option></select>
          <button type="button" class="pbtn" id="boardBtn" hidden>Pit board</button>
          <label class="say" id="sayRow"><input type="checkbox" id="say"> <span id="sayText">Say my lap time</span></label>
          <div id="live" hidden>
            <div class="big">
              <div class="tile" id="lastTile"><div class="k">Last lap</div><div class="v" id="last">-</div><div class="s" id="lastVs"></div></div>
              <div class="tile"><div class="k">Best</div><div class="v" id="best">-</div><div class="s" id="lapCount"></div></div>
            </div>
            <div class="big" id="targetRow" hidden>
              <div class="tile"><div class="k">Target</div><div class="v" id="target">-</div><div class="s" id="targetVs"></div></div>
            </div>
            <div class="trend"><div class="small">Last 10 laps</div><svg id="trend" viewBox="0 0 300 90" preserveAspectRatio="none" aria-label="Trend of the last ten laps"></svg></div>
            <ul class="laps" id="laps"></ul>
          </div>
          <p class="small" id="none">Not on track right now. Your laps show here as soon as you are.</p>
          <div class="card" id="card"></div>
          <p class="small">Laps from the race control laptop, as the live board shows them. Your report card is
          worked out each time a race or practice is filed.</p>
        </div>
        <div id="board" hidden>
          <div class="bhead"><span id="bName"></span><button type="button" class="bx" id="boardExit" aria-label="Close the pit board">&times;</button></div>
          <div class="bgrid">
            <div class="bc blast"><div class="bk">Last lap</div><div class="bv" id="bLast">-</div></div>
            <div class="bc bpos"><div class="bk">Position</div><div class="bv" id="bPos">-</div></div>
            <div class="bc blaps"><div class="bk">Laps</div><div class="bv" id="bLaps">-</div></div>
            <div class="bc bahead"><div class="bk">Gap ahead</div><div class="bv" id="bAhead">-</div></div>
            <div class="bc btarget"><div class="bk">To target</div><div class="bv" id="bTarget">-</div></div>
          </div>
          <div class="bnote" id="bNote" aria-live="polite"></div>
        </div>
        <div id="boardTap" hidden>Tap anywhere to start the pit board</div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        // The same remembered pick as Live Results' Follow - see FOLLOW_KEY there.
        const FOLLOW_KEY = 'hcFollow';
        let following = '';
        try { following = localStorage.getItem(FOLLOW_KEY) || ''; } catch(e) {}

        const pick = document.getElementById('pick');
        const esc = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        const secs = ms => (ms / 1000).toFixed(3);
        const gap = ms => (ms < 0 ? '-' : '+') + (Math.abs(ms) / 1000).toFixed(2) + 's';

        let shownFor = '', lapsSeen = 0, bestSeen = 0, firstSeen = 0;

        // SAY MY LAP TIME (22 Oct 2026): this driver's own lap time read out
        // by the phone after every lap - "nine point two one", and "best
        // lap" on a new best - and nobody else's. The phone's own voice, so
        // it never waits for the PA. Off until ticked, and remembered on the
        // phone. Ticking it is the tap a phone needs before it will make a
        // sound, so it also wakes the beep the new best plays on a phone
        // that cannot buzz (an iPhone).
        const SAY_KEY = 'hcCoachSay';
        const sayBox = document.getElementById('say');
        const canSpeak = 'speechSynthesis' in window;
        let audio = null;
        try { sayBox.checked = canSpeak && localStorage.getItem(SAY_KEY) === '1'; } catch(e) {}
        if (!canSpeak) document.getElementById('sayRow').hidden = true;

        // WHY (23 Oct 2026, bug hunt 10): the box came back ticked after a
        // reload (a change of look reloads every page) but the phone said
        // nothing all night - a phone only lets a page speak once it has
        // been tapped since it loaded, and nothing here spoke inside a tap.
        // Now, as on Live Results, the box says "tap to resume" and the
        // first tap anywhere starts the voice again.
        let sayUnlocked = false;
        function showSay(){
          document.getElementById('sayText').textContent = 'Say my lap time' + (sayBox.checked && !sayUnlocked ? ': tap to resume' : '');
        }
        showSay();

        function wakeAudio(){
          try { audio = audio || new (window.AudioContext || window.webkitAudioContext)(); if (audio.state === 'suspended') audio.resume(); } catch(e) {}
        }

        function sayAloud(text){
          if (!canSpeak || !sayBox.checked) return;
          try {
            window.speechSynthesis.cancel();
            const u = new SpeechSynthesisUtterance(text);
            u.rate = 1.05;
            window.speechSynthesis.speak(u);
          } catch(e) {}
        }

        sayBox.addEventListener('change', () => {
          try { localStorage.setItem(SAY_KEY, sayBox.checked ? '1' : '0'); } catch(e) {}
          sayUnlocked = true; showSay();
          wakeAudio();
          if (sayBox.checked) sayAloud('Lap times on'); else { try { window.speechSynthesis.cancel(); } catch(e) {} }
        });

        // Any tap wakes the beep too, for a phone that never ticks the box -
        // and the first one since the page loaded lets it speak again (see
        // showSay). A tap on the box's own row while it says "tap to resume"
        // resumes it, as Live Results' button does, rather than letting the
        // tap untick it - which is what that row invites a driver to tap.
        document.addEventListener('click', e => {
          wakeAudio();
          if (sayUnlocked) return;
          sayUnlocked = true;
          const onRow = document.getElementById('sayRow').contains(e.target);
          if (sayBox.checked) {
            if (onRow) e.preventDefault();
            sayAloud(onRow ? 'Lap times on' : ' ');
          }
          showSay();
        }, true);

        function beep(){
          try {
            if (!audio || audio.state !== 'running') return;
            const o = audio.createOscillator(), g = audio.createGain();
            o.type = 'sine'; o.frequency.value = 880;
            g.gain.setValueAtTime(0.0001, audio.currentTime);
            g.gain.exponentialRampToValueAtTime(0.3, audio.currentTime + 0.02);
            g.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + 0.35);
            o.connect(g); g.connect(audio.destination);
            o.start(); o.stop(audio.currentTime + 0.4);
          } catch(e) {}
        }

        pick.addEventListener('change', () => {
          following = pick.value;
          try { localStorage.setItem(FOLLOW_KEY, following); } catch(e) {}
          shownFor = ''; lapsSeen = 0; bestSeen = 0;
          tick(); card();
        });

        function people(d){
          const list = d.people || [];
          const now = Array.from(pick.options).slice(1).map(o => o.value).join(',');
          if (now !== list.map(p => p.id).join(',')) {
            pick.innerHTML = '<option value="">Pick your driver...</option>'
              + list.map(p => '<option value="' + esc(p.id) + '">' + esc(p.name) + '</option>').join('');
          }
          pick.value = list.some(p => p.id === following) ? following : '';
        }

        // A new best, said with a flash and - where the phone can - a buzz.
        function celebrate(){
          const tile = document.getElementById('lastTile');
          tile.classList.remove('flash'); void tile.offsetWidth; tile.classList.add('flash');
          // And the pit board, when it is up (22 Oct 2026).
          if (boardOpen) { boardEl.classList.remove('best'); void boardEl.offsetWidth; boardEl.classList.add('best'); }
          let buzzed = false;
          try { if (navigator.vibrate) buzzed = navigator.vibrate([120,80,240]); } catch(e) {}
          // An iPhone has no vibration for a web page, so it beeps instead.
          if (!buzzed) beep();
        }

        function trend(laps, best){
          const svg = document.getElementById('trend');
          const last = laps.slice(-10);
          if (last.length < 2) { svg.innerHTML = ''; return; }
          const lo = Math.min(...last), hi = Math.max(...last), span = Math.max(hi - lo, 1);
          const x = i => 10 + i * (280 / (last.length - 1));
          const y = v => 10 + (v - lo) / span * 70;
          const pts = last.map((v, i) => x(i).toFixed(1) + ',' + y(v).toFixed(1)).join(' ');
          const bestY = best > 0 && best >= lo && best <= hi ? y(best).toFixed(1) : null;
          svg.innerHTML = (bestY ? '<line x1="0" x2="300" y1="' + bestY + '" y2="' + bestY + '" stroke="currentColor" stroke-opacity=".25" stroke-dasharray="4 4"/>' : '')
            + '<polyline points="' + pts + '" fill="none" stroke="currentColor" stroke-width="3" stroke-linejoin="round"/>'
            + last.map((v, i) => '<circle cx="' + x(i).toFixed(1) + '" cy="' + y(v).toFixed(1) + '" r="3.5" fill="currentColor"/>').join('');
        }

        function draw(d){
          const row = (d.standings || []).find(s => s.id && s.id === following);
          const live = document.getElementById('live'), none = document.getElementById('none');

          if (!following || !row || !(row.laps || []).length) {
            live.hidden = true; none.hidden = !following;
            // WHY (23 Oct 2026, bug hunt 10): a restart of the web server (the
            // only way to change a PIN) gives everybody new ids, and a phone
            // still holding the old one was told "Not on track right now"
            // while its driver raced. A pick nobody on the list matches now
            // says to pick again. It is not wiped: a driver done for the
            // night is off the list too, and still wants their report card.
            const listed = d.people || [];
            const lost = following && listed.length > 0 && !listed.some(p => p.id === following);
            none.textContent = lost
              ? 'Not on the board or in a race still to come. If you are racing tonight, pick your driver again above.'
              : 'Not on track right now. Your laps show here as soon as you are.';
            // WHY (22 Oct 2026): the counts kept the last race's lap total,
            // so the first lap of the next race (1 is not more than 15) was
            // never said. Off track or between races, the next lap that comes
            // in is lap 1 of a new session, seen live by this page.
            if (following) { shownFor = following; lapsSeen = 0; bestSeen = 0; }
            return;
          }

          live.hidden = false; none.hidden = true;
          const laps = row.laps, best = row.best > 0 ? row.best : Math.min(...laps), last = laps[laps.length - 1];

          document.getElementById('last').textContent = secs(last);
          document.getElementById('best').textContent = secs(best);
          // WHY (23 Oct 2026, bug hunt 10): row.laps leaves out the start lap
          // (RaceGaps.FlyingLaps), so the count here was one short of the
          // board, the PA and this page's own pit board, and every lap was
          // listed one number low. The count is now the board's own (the
          // card's), and the first lap in the list is lap 2.
          const lapTotal = ((d.cards || {})[following] || {}).laps || (laps.length + 1);
          document.getElementById('lapCount').textContent = lapTotal + (lapTotal === 1 ? ' lap' : ' laps');

          const vs = document.getElementById('lastVs');
          vs.textContent = last <= best ? 'Best lap' : gap(last - best) + ' off best';
          vs.className = 's ' + (last <= best ? 'up' : 'down');

          const target = ((d.cards || {})[following] || {}).target;
          document.getElementById('targetRow').hidden = !(target > 0);
          if (target > 0) {
            document.getElementById('target').textContent = secs(target);
            const tv = document.getElementById('targetVs');
            tv.textContent = last <= target ? 'On target' : gap(last - target) + ' off';
            tv.className = 's ' + (last <= target ? 'up' : 'down');
          }

          trend(laps, best);
          document.getElementById('laps').innerHTML = laps.map((v, i) => ({ v, n: i + 2 })).slice(-10).reverse()
            .map(l => '<li class="' + (l.v === best ? 'best' : '') + '"><span>Lap ' + l.n + '</span><span>' + secs(l.v) + '</span></li>').join('');

          // WHY (22 Oct 2026): fewer laps than last time, with a different
          // first lap, means a new race started without a poll seeing the
          // empty board in between - start counting again so its laps are
          // still said. The first-lap check is there because "Remove a lap"
          // also drops the count by one (it folds the last lap into the one
          // before), and that must not read the merged lap out as a new one.
          if (shownFor === following && laps.length < lapsSeen && laps[0] !== firstSeen) { lapsSeen = 0; bestSeen = 0; }

          // A new best since the last look - not the first look at a driver
          // already on track, which would buzz for a best set minutes ago.
          const newBest = shownFor === following && laps.length > lapsSeen && bestSeen > 0 && best < bestSeen && last === best;

          if (newBest) {
            celebrate();
          }

          // Said for a lap that has just come in - not for the laps already
          // there when the driver was picked or the page opened.
          if (shownFor === following && laps.length > lapsSeen) {
            sayAloud((last / 1000).toFixed(2) + (newBest ? ', best lap' : ''));
          }

          shownFor = following; lapsSeen = laps.length; bestSeen = best; firstSeen = laps[0];
        }

        // WHY (23 Oct 2026, bug hunt 10): one poll at a time, and none left
        // hanging - as on Live Results. On poor Wi-Fi an older answer landing
        // after a newer one moved the lap count back, so the next poll said
        // the lap again and buzzed a new best twice, and hung requests used
        // up the phone's connections.
        let ticking = false;
        async function tick(){
          if (ticking) return;
          ticking = true;
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
          try {
            const d = await (await fetch('/api/state', { signal: stop.signal })).json();
            people(d); draw(d); boardDraw(d);
          } catch(e) {}
          finally { clearTimeout(timer); ticking = false; }
        }

        // --- The pit board (22 Oct 2026, driver experience, item 10) -------
        // The same /api/state as everything above - nothing new on the wire
        // - drawn as big as the screen allows: last lap, position, laps, the
        // gap to the car ahead and the gap to the target. Opened with the
        // Pit board button, or straight away with ?board=1 (a tap is still
        // needed first: a phone only goes full screen, and only stays
        // awake, for a page somebody has touched).
        const boardEl = document.getElementById('board'), boardBtn = document.getElementById('boardBtn');
        const boardTap = document.getElementById('boardTap');
        let boardOpen = false;

        function boardSet(id, text){ document.getElementById(id).textContent = text; }

        function boardDraw(d){
          boardBtn.hidden = !following;
          if (!boardOpen) return;
          const row = (d.standings || []).find(s => s.id && s.id === following);
          const card = row ? ((d.cards || {})[following] || {}) : {};
          const who = (d.people || []).find(p => p.id === following);
          boardSet('bName', who ? who.name : 'Pick your driver first');
          const laps = row && row.laps ? row.laps : [];
          const last = laps.length ? laps[laps.length - 1] : 0;
          boardSet('bLast', last > 0 ? (last / 1000).toFixed(2) : '-');
          boardSet('bPos', card.pos ? 'P' + card.pos : '-');
          boardSet('bLaps', row ? String(card.laps || 0) : '-');
          boardSet('bAhead', row && card.ahead ? card.ahead : '-');
          const target = card.target > 0 ? card.target : 0;
          boardSet('bTarget', target && last > 0 ? gap(last - target) : '-');
          boardEl.classList.toggle('slow', target > 0 && last > target);
        }

        // --- Keeping the screen on ------------------------------------------
        // 1. The Screen Wake Lock, which a browser only offers a secure page.
        // 2. Otherwise - and the club Wi-Fi pages are plain http - a tiny
        //    silent video played on a loop, which no phone sleeps through
        //    (/keepawake.mp4, NoSleep.js's own, see KeepAwake.cs).
        // 3. Otherwise a note to set Auto-Lock to Never.
        // Asked again whenever the page comes back into view: a wake lock is
        // let go of, and a video paused, when the phone hides the page.
        const AUTOLOCK = 'If the screen still goes dark, set Auto-Lock to Never (iPhone: Settings, Display & Brightness) or the screen timeout to its longest (Android: Settings, Display) while the pit board is up.';
        let wakeLock = null, wakeVideo = null, wantAwake = false;

        function awakeNote(text){ boardSet('bNote', text); }

        function keepAwake(){
          wantAwake = true;
          if (window.isSecureContext && navigator.wakeLock && navigator.wakeLock.request) {
            navigator.wakeLock.request('screen').then(l => { wakeLock = l; awakeNote(''); }).catch(() => playAwake());
            return;
          }
          playAwake();
        }

        function playAwake(){
          try {
            if (!wakeVideo) {
              wakeVideo = document.createElement('video');
              wakeVideo.setAttribute('playsinline', '');
              wakeVideo.setAttribute('muted', '');
              wakeVideo.setAttribute('title', 'Keep awake');
              wakeVideo.muted = true;
              wakeVideo.loop = true;
              wakeVideo.src = '/keepawake.mp4';
              // NoSleep's own trick for this video, which is just over a
              // second long: jump back before it ends, so it never stops.
              wakeVideo.addEventListener('timeupdate', () => { if (wakeVideo.currentTime > 0.5) wakeVideo.currentTime = Math.random(); });
            }
            const playing = wakeVideo.play();
            if (playing && playing.then) playing.then(() => awakeNote('')).catch(() => awakeNote(AUTOLOCK));
          } catch(e) { awakeNote(AUTOLOCK); }
        }

        function letSleep(){
          wantAwake = false;
          try { if (wakeLock) wakeLock.release(); } catch(e) {}
          wakeLock = null;
          try { if (wakeVideo) wakeVideo.pause(); } catch(e) {}
        }

        document.addEventListener('visibilitychange', () => {
          if (wantAwake && document.visibilityState === 'visible') keepAwake();
        });

        function openBoard(){
          // The video and the wake lock first, while this is still the tap
          // itself: an iPhone forgets a tap once anything has been waited for.
          keepAwake(); wakeAudio();
          boardOpen = true; boardEl.hidden = false; boardTap.hidden = true;
          try {
            const root = document.documentElement;
            const full = root.requestFullscreen || root.webkitRequestFullscreen;
            // An iPhone has no full screen for a page; the board fills the window anyway.
            if (full) {
              const going = full.call(root);
              const sideways = () => { try { if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => {}); } catch(e) {} };
              if (going && going.then) going.then(sideways).catch(() => {}); else sideways();
            }
          } catch(e) {}
          tick();
        }

        function closeBoard(){
          boardOpen = false; boardEl.hidden = true;
          boardEl.classList.remove('slow', 'best');
          letSleep();
          try {
            if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});
            else if (document.webkitFullscreenElement && document.webkitExitFullscreen) document.webkitExitFullscreen();
          } catch(e) {}
        }

        boardBtn.addEventListener('click', openBoard);
        boardTap.addEventListener('click', openBoard);
        document.getElementById('boardExit').addEventListener('click', closeBoard);
        if (new URLSearchParams(location.search).get('board') === '1') boardTap.hidden = false;

        // Not reloaded for a change of look while the board is up: it would
        // drop out of full screen and let the phone sleep. Nor while it is
        // saying lap times (23 Oct 2026, bug hunt 10): a reload silences the
        // voice until the next tap.
        function lookBusy(){ return boardOpen || sayBox.checked; }

        async function card(){
          const box = document.getElementById('card');
          if (!following) { box.innerHTML = ''; return; }
          // Given up on after 12 s, so a hung one does not hold a connection
          // the 1-second poll needs, and dropped if the driver was changed
          // while it was out (23 Oct 2026, bug hunt 10).
          const asked = following;
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 12000);
          try {
            const d = await (await fetch('/api/coach?id=' + encodeURIComponent(asked), { signal: stop.signal })).json();
            if (asked !== following) return;
            const lines = (d && Array.isArray(d.lines)) ? d.lines.filter(l => typeof l === 'string' && l.trim() !== '') : [];
            box.innerHTML = lines.length === 0 ? ''
              : '<h2>Your report card</h2><ul>' + lines.map(l => '<li>' + esc(l) + '</li>').join('') + '</ul>';
          } catch(e) {}
          finally { clearTimeout(timer); }
        }

        tick(); card();
        setInterval(tick, 1000);
        setInterval(card, 20000);
        </script>
        <script>{{LookWatchScript}}</script>
        </body></html>
        """;
    }
}
