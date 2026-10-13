using System.Net;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The room voting on who wins the next race, from a phone on the club's
/// own Wi-Fi.
///
/// WHY THIS IS A SAFER ROUTE THAN THE SHOUT-OUTS BESIDE IT
///
/// A club note is a stranger's WORDS, which is why that route needs an
/// approval gate before anything can reach the PA. A vote is a tap on one
/// of the names the club itself put on the ballot: nothing a phone sends
/// here can become a word the announcer says. The worst a stranger on the
/// club Wi-Fi can achieve is being wrong about who wins.
///
/// So there is no approval list, and there does not need to be. What there
/// is instead is <see cref="RoomVote"/>'s own refusal to speak at all
/// unless enough people voted to stand for the room, and its refusal to
/// take a vote for a driver who is not on the grid.
///
/// AND IT IS OFF UNTIL A CLUB TURNS IT ON
///
/// For the one reason that applies to every public route here: it writes
/// to the race PC's disk on a stranger's request. See
/// AppSettings.RoomVoteFromTheRoom.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>
    /// Tonight's vote, as MainForm owns it. Null at a club on an older
    /// wiring, and then the route answers as though it were switched off.
    /// </summary>
    internal Func<RoomVote?>? TonightsVote { get; set; }

    /// <summary>Whether the club wants the room to be able to vote - AppSettings.RoomVoteFromTheRoom.</summary>
    internal Func<bool>? VotingFromTheRoomOpen { get; set; }

    /// <summary>
    /// Six from one phone in five minutes.
    ///
    /// Looser than the shout-out limit because changing your mind is a
    /// legitimate thing to do here and sending a second dedication is not
    /// - a vote REPLACES the one before it, so a handful of taps is
    /// somebody deciding rather than somebody flooding.
    /// </summary>
    private readonly AttemptLimiter _votesFromOnePhone = new(6, TimeSpan.FromMinutes(5), 1000);

    /// <summary>
    /// Three hundred from every phone together in five minutes. A full
    /// hall voting and half of them changing their mind is nowhere near
    /// it; RoomVote.MostVoters bites first, and this is only here so the
    /// laptop's disk is not what notices.
    /// </summary>
    private readonly AttemptLimiter _votesFromTheHall = new(300, TimeSpan.FromMinutes(5), 1);

    /// <summary>The address a club gives out, for the Commentary page and the docs to say.</summary>
    internal const string VotePath = "/vote";

    /// <summary>Whether the route is open: the club switched it on and there is a store behind it.</summary>
    private bool VotingOpen => VotingFromTheRoomOpen?.Invoke() == true && TonightsVote?.Invoke() is not null;

    /// <summary>
    /// The drivers on the ballot right now, as JSON, so the page can draw
    /// itself and keep itself honest while it is open.
    ///
    /// ASKED FOR RATHER THAN BAKED INTO THE PAGE because the vote is
    /// opened and closed by the race director while phones are already
    /// looking at it. A page built once at the moment somebody opened it
    /// would offer last race's grid for the rest of the night.
    /// </summary>
    private void HandleVoteBallot(HttpListenerContext context)
    {
        if (!HostAccepting(context))
        {
            return;
        }

        if (!VotingOpen || TonightsVote?.Invoke() is not { } vote)
        {
            // 404 and not 403, for the reason the notes route gives: a
            // club with this switched off has no such page rather than a
            // page it is refusing.
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not found\"}");
            return;
        }

        WriteJson(context, BuildVoteBallotJson(vote, ClientKey(context)));
    }

    /// <summary>
    /// The ballot as one phone sees it.
    ///
    /// NO TALLY GOES OUT WHILE IT IS OPEN, and that is deliberate.
    /// Showing the room what it is voting for turns a prediction into a
    /// bandwagon, and the announcer's line is only worth anything if
    /// the vote was each person's own guess.
    ///
    /// EACH DRIVER UNDER THE ROSTER'S OPAQUE ID, not their transponder.
    /// This route needs no PIN, and a transponder beside a full name is the
    /// mapping /api/state stopped handing out on 9 Oct - see RosterView and
    /// AddFollowing. HandleRoomVote maps the id back.
    ///
    /// AND THIS PHONE'S OWN PICK, from the store. A new vote starts with
    /// nobody's pick in it, and a page left open from the race before
    /// would otherwise go on showing its old pick as though it counted.
    /// </summary>
    /// <param name="vote">Tonight's vote.</param>
    /// <param name="voter">This phone, as ClientKey names it.</param>
    internal string BuildVoteBallotJson(RoomVote vote, string voter)
    {
        var key = _rosterKey;
        var picked = vote.PickOf(voter);

        return JsonSerializer.Serialize(new
        {
            ok = true,
            open = vote.Taking,
            race = vote.OnRace ?? string.Empty,
            drivers = vote.Options.Select(one => new { id = RosterId(key, one.Id), name = one.Name }).ToArray(),
            pick = picked is { Length: > 0 } ? RosterId(key, picked) : null,
        });
    }

    /// <summary>
    /// The driver a phone tapped, back from the opaque id the ballot gave
    /// it to the option's own id - or null when it matches nobody on the
    /// ballot, which RoomVote.Cast then refuses as not on the grid.
    /// </summary>
    internal string? VoteOptionFor(RoomVote vote, string? servedId)
    {
        if (string.IsNullOrWhiteSpace(servedId))
        {
            return null;
        }

        var key = _rosterKey;
        var asked = servedId.Trim();

        return vote.Options
            .FirstOrDefault(one => string.Equals(RosterId(key, one.Id), asked, StringComparison.Ordinal))
            ?.Id;
    }

    /// <summary>
    /// Takes a vote in from a phone in the room.
    ///
    /// Answers JSON either way, and the "why not" is written for somebody
    /// who is not a club official and has never seen this app.
    /// </summary>
    private void HandleRoomVote(HttpListenerContext context)
    {
        if (!IsAppWrite(context) || !HostAccepting(context))
        {
            return;
        }

        if (!VotingOpen)
        {
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not found\"}");
            return;
        }

        var device = ClientKey(context);

        if (!_votesFromOnePhone.TryReserve(device, out _))
        {
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"You have changed your mind a few times already - that one stands.\"}");
            return;
        }

        if (!_votesFromTheHall.TryReserve("all", out _))
        {
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"Lots of votes coming in right now. Try again in a moment.\"}");
            return;
        }

        if (TonightsVote?.Invoke() is not { } vote)
        {
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not found\"}");
            return;
        }

        // THE PHONE IS THE VOTER, which is what keeps one vote to one
        // person without asking anybody who they are. The same key the
        // rate limit uses - no name, no sign-in, nothing stored about
        // whoever it is beyond the fact that they have voted once.
        //
        // The page sends the ballot's opaque id; Cast wants the option's
        // own. One that matches nobody goes in as it came, so the phone is
        // told the driver is not on the grid (or that voting has closed)
        // rather than that its vote did not come through.
        var served = context.Request.QueryString["d"];

        if (vote.Cast(device, VoteOptionFor(vote, served) ?? served) is { } why)
        {
            context.Response.StatusCode = 400;
            WriteJson(context, JsonSerializer.Serialize(new { ok = false, error = why }));
            return;
        }

        WriteJson(context, "{\"ok\":true}");
    }

    /// <summary>
    /// The page a phone on the club Wi-Fi opens to vote.
    ///
    /// Deliberately plain: the race, a list of names to tap, and a line
    /// saying what happens next. It is used once, by somebody standing on
    /// the bank, on a phone in one hand.
    ///
    /// It draws its own grid from <see cref="HandleVoteBallot"/> and
    /// re-asks while it is open, so a phone left on the page through two
    /// races is never offering the wrong grid.
    /// </summary>
    internal string BuildVotePage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Who wins?</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px; max-width:560px; margin:0 auto; }
        .race { text-align:center; font-weight:700; font-size:18px; margin:4px 0 14px; }
        .pick { width:100%; padding:16px; margin-bottom:10px; border-radius:10px; font-size:18px;
                font-family:inherit; text-align:left; border:1px solid var(--raised);
                background:var(--surface); color:var(--text); }
        .pick.mine { background:var(--primary); color:var(--on-primary); border-color:var(--primary); font-weight:700; }
        .pick:disabled { opacity:.5; }
        .msg { text-align:center; padding:10px; font-weight:700; min-height:22px; font-size:15px; }
        .small { color:var(--muted); font-size:13px; line-height:1.5; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}WHO WINS?</h1></div>
        <div class="wrap">
          <div class="race" id="race"></div>
          <div id="list"></div>
          <div class="msg" id="msg">Loading...</div>
          <p class="small">Tap a name. You can change your mind while voting is open, and the announcer says what
          the room reckoned after the race - it never says who you picked. Nothing you tap here reaches the timing
          or the results.</p>
        </div>
        <script>
        var list = document.getElementById('list');
        var race = document.getElementById('race');
        var msg = document.getElementById('msg');
        var mine = null;
        var open = true;
        var shown = null;

        function draw(d) {
          race.textContent = d.race || 'Tonight';
          open = !!d.open;
          // This phone's pick as the laptop holds it, not as this page
          // remembers it: a new vote starts with nobody's pick in it, and a
          // page left open from the race before must not go on showing one.
          mine = d.pick || null;
          var mineName = null;
          list.innerHTML = '';
          (d.drivers || []).forEach(function(driver){
            var b = document.createElement('button');
            b.className = 'pick' + (driver.id === mine ? ' mine' : '');
            b.textContent = driver.name;
            b.disabled = !open;
            b.addEventListener('click', function(){ cast(driver.id, driver.name); });
            list.appendChild(b);
            if (driver.id === mine) { mineName = driver.name; }
          });
          // Only rewritten when what it says has changed, so an error from
          // the last tap is not wiped by the next redraw.
          var now = (open ? 'open' : 'closed') + '|' + (mine || '') + '|' + (d.race || '');
          if (now === shown) { return; }
          shown = now;
          if (!open) { msg.textContent = mine ? 'Voting has closed. Your pick is in.' : 'Voting has closed.'; }
          else if (mine) { msg.textContent = 'You have ' + (mineName || 'your pick') + '. Good luck.'; }
          else { msg.textContent = 'Tap who you reckon wins.'; }
        }

        function ballot() {
          fetch('/api/vote/ballot')
            .then(function(r){ return r.ok ? r.json() : null; })
            .then(function(d){
              if (d && d.ok) { draw(d); return; }
              shown = null;
              race.textContent = '';
              list.innerHTML = '';
              msg.textContent = 'No vote is running at the moment.';
            })
            .catch(function(){ shown = null; msg.textContent = 'Could not reach the laptop.'; });
        }

        function cast(id, name) {
          msg.textContent = 'Sending...';
          // The same header and method every write to this server uses, so
          // a page left open from an older version is turned away rather
          // than half-working.
          fetch('/api/vote?d=' + encodeURIComponent(id), { method:'POST', headers:{ 'X-HeatCaller':'1' } })
            .then(function(r){ return r.json().catch(function(){ return { ok:false, error:'No answer from the laptop.' }; }); })
            .then(function(d){
              if (d && d.ok) {
                mine = id;
                msg.textContent = 'You have ' + name + '. Good luck.';
                ballot();
                return;
              }
              msg.textContent = (d && d.error) || 'Could not send that.';
            })
            .catch(function(){ msg.textContent = 'Could not reach the laptop.'; });
        }

        ballot();

        // Re-asked while the page is open, so a phone left on it through
        // two races is never offering the wrong grid - and so a vote that
        // the race director has closed stops taking taps.
        setInterval(ballot, 5000);
        </script>
        <script>{{LookWatchScript}}</script>
        </body></html>
        """;
    }
}
