using System.Net;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Shout-outs and dedications sent in from a phone on the club's own
/// Wi-Fi - the room's way into the broadcast.
///
/// WHY THIS IS A FILE OF ITS OWN
///
/// The same reason WebServerService.Camera.cs is: the main file is a
/// quarter of a megabyte and this is a self-contained route with a page, a
/// rate limit and a store behind it. Nothing here is reached from anywhere
/// else in the server except the two cases added to the switch.
///
/// WHAT THE ROOM CAN AND CANNOT DO
///
/// It can put a sentence in a list. That is the whole of it. Every note
/// that arrives here is UNAPPROVED - see <see cref="ClubNotes.AddFromTheRoom"/>
/// - and nothing unapproved is ever offered to the announcer, so the worst
/// a stranger on the club Wi-Fi can achieve is a line on a screen that the
/// race director reads and deletes.
///
/// That gate is why this route can exist at all. The alternative - anonymous
/// text from a crowd going straight to a text-to-speech engine wired to a
/// PA - is not a feature with a risk to be managed; it is a prank waiting
/// for the first teenager who finds the address.
///
/// AND IT IS OFF UNTIL A CLUB TURNS IT ON
///
/// Not because approving notes is dangerous, but because this writes to the
/// race PC's disk on a stranger's request, and a club that has not decided
/// to want that should not have it. See AppSettings.ClubNotesFromTheRoom.
/// </summary>
public sealed partial class WebServerService
{
    /// <summary>
    /// Tonight's notes, as MainForm owns them. Null at a club on an older
    /// wiring, and then the route answers as though it were switched off.
    /// </summary>
    internal Func<ClubNotes?>? TonightsNotes { get; set; }

    /// <summary>Whether the club wants the room to be able to send one - AppSettings.ClubNotesFromTheRoom.</summary>
    internal Func<bool>? NotesFromTheRoomOpen { get; set; }

    /// <summary>
    /// Four notes in five minutes from one phone.
    ///
    /// A shout-out is not something anybody sends twice, let alone four
    /// times - the limit is here for the phone that is not a person. It is
    /// far tighter than the check-in limiter beside it because a check-in
    /// is somebody correcting themselves and this is somebody writing a
    /// sentence; there is no honest reason to send a fifth.
    /// </summary>
    private readonly AttemptLimiter _notesFromOnePhone = new(4, TimeSpan.FromMinutes(5), 1000);

    /// <summary>
    /// Thirty from every phone together in five minutes. A full hall
    /// sending a birthday message each is nowhere near it; the store's own
    /// ClubNotes.MostFromTheRoom ceiling bites long before this does, and
    /// this is only here so the laptop's disk is not the thing that
    /// notices first.
    /// </summary>
    private readonly AttemptLimiter _notesFromTheHall = new(30, TimeSpan.FromMinutes(5), 1);

    /// <summary>The address a club gives out, for the Commentary page and the docs to say.</summary>
    internal const string NotesPath = "/shoutout";

    /// <summary>
    /// Whether the route is open: the club has switched it on and there is
    /// a store behind it.
    /// </summary>
    private bool NotesOpen => NotesFromTheRoomOpen?.Invoke() == true && TonightsNotes?.Invoke() is not null;

    /// <summary>
    /// Takes a note in from a phone in the room.
    ///
    /// Answers JSON either way, and the "why not" is written for somebody
    /// who is not a club official and has no idea what any of this is.
    /// </summary>
    private void HandleClubNote(HttpListenerContext context)
    {
        if (!IsAppWrite(context) || !HostAccepting(context))
        {
            return;
        }

        if (!NotesOpen)
        {
            // 404 and not 403: a club that has this switched off is not
            // refusing a request, it has no such page. Saying "switched
            // off" would tell a stranger there is a thing to switch on.
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not found\"}");
            return;
        }

        var device = ClientKey(context);

        if (!_notesFromOnePhone.TryReserve(device, out var phoneTicket))
        {
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"You have sent a few already - give the announcer a chance.\"}");
            return;
        }

        if (!_notesFromTheHall.TryReserve("all", out _))
        {
            // Not this phone's doing (17 Oct 2026, Bug Hunt 8, A18-2), so
            // its own allowance is given back - as ReserveCheckInWrite does.
            _notesFromOnePhone.Release(device, phoneTicket);
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"Lots of messages coming in right now. Try again in a few minutes.\"}");
            return;
        }

        var query = context.Request.QueryString;

        var message = query["m"] ?? string.Empty;
        var from = query["n"] ?? string.Empty;

        if (TonightsNotes?.Invoke() is not { } notes)
        {
            // Not this phone's doing either (A18-2).
            _notesFromOnePhone.Release(device, phoneTicket);
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not found\"}");
            return;
        }

        if (notes.AddFromTheRoom(message, from) is { } why)
        {
            context.Response.StatusCode = 400;

            // Serialised rather than concatenated: the reason is a
            // sentence with an apostrophe in it, and one hand-built quote
            // away from being broken JSON on somebody's phone. The same
            // way every other dynamic answer in this server is written.
            WriteJson(context, JsonSerializer.Serialize(new { ok = false, error = why }));
            return;
        }

        // SAYS IT IS NOT GUARANTEED, and this is the honest part of the
        // whole feature. Somebody who is told "sent!" and never hears it
        // has been let down by the club; somebody who is told the race
        // director has to okay it, and then hears it, has been given
        // something. The second is both true and better.
        WriteJson(context, "{\"ok\":true}");
    }

    /// <summary>
    /// The page a phone on the club Wi-Fi opens to send one.
    ///
    /// Deliberately plain: a box, a name, a button. It is used once, by
    /// somebody standing up, on a phone they are holding in one hand.
    /// </summary>
    internal string BuildNotePage()
    {
        var look = CurrentPageLook();

        // WHY (20 Oct 2026, Bug Hunt 8): the route comment promised that a
        // club with this switched off still serves the page "with the page
        // saying so", but the page never looked - it always showed the box
        // and the button, and somebody who typed a birthday message lost it
        // to a bare "Not found". It now asks NotesOpen when it is built (the
        // same listener thread HandleClubNote asks it on) and, when the
        // route is closed, shows one neutral sentence in place of the form.
        // The wording never says "switched off", for the same reason
        // HandleClubNote answers 404 rather than 403.
        var content = NotesOpen ? NoteForm() : """
          <p class="small">Messages to the announcer aren't being taken right now.</p>
        </div>
        """;

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Message the announcer</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px; max-width:560px; margin:0 auto; }
        .note, .who { width:100%; padding:13px; font-size:17px; border-radius:8px; font-family:inherit;
                      border:1px solid var(--raised); background:var(--surface); color:var(--text); margin-bottom:10px; }
        .note { min-height:110px; resize:vertical; }
        .send { width:100%; padding:15px; border:none; border-radius:10px; background:var(--primary);
                color:var(--on-primary); font-size:17px; font-weight:700; font-family:inherit;
                display:flex; align-items:center; justify-content:center; line-height:1.25; }
        .send:disabled { opacity:.5; }
        .msg { text-align:center; padding:10px; font-weight:700; min-height:22px; font-size:15px; }
        .small { color:var(--muted); font-size:13px; line-height:1.5; }
        .left { text-align:right; color:var(--muted); font-size:12px; margin:-6px 0 10px; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}MESSAGE THE ANNOUNCER</h1></div>
        <div class="wrap">
        {{content}}
        {{HeatCallerMark.PageFooterHtml}}
        <script>{{LookWatchScript}}</script>
        </body></html>
        """;
    }

    /// <summary>
    /// The box, the name, the button and the script that sends them -
    /// BuildNotePage's body when the route is open. Ends by closing the
    /// .wrap div, as the closed sentence does.
    /// </summary>
    private static string NoteForm() => $$"""
          <p class="small">A birthday, a dedication, somebody's first race, the raffle closing - whatever the
          announcer would not otherwise know. Keep it short and it may get read out between races.</p>
          <textarea class="note" id="note" maxlength="{{ClubNotes.MostChars}}"
                    placeholder="It's Jack's first race since his operation - good to have him back"></textarea>
          <div class="left" id="left"></div>
          <input class="who" id="who" maxlength="40" placeholder="Your name (optional)" autocomplete="name">
          <button class="send" id="send">SEND IT</button>
          <div class="msg" id="msg"></div>
          <p class="small">The race director reads every message before the announcer says anything, so this is not
          a promise that it goes out - and nothing you send here reaches the timing or the results.</p>
        </div>
        <script>
        var note = document.getElementById('note');
        var who = document.getElementById('who');
        var send = document.getElementById('send');
        var msg = document.getElementById('msg');
        var left = document.getElementById('left');
        var most = {{ClubNotes.MostChars}};
        function count(){ left.textContent = (most - note.value.length) + ' left'; }
        note.addEventListener('input', count);
        count();
        send.addEventListener('click', function(){
          var text = note.value.trim();
          if (!text) { msg.textContent = 'Type something first.'; return; }
          send.disabled = true;
          msg.textContent = 'Sending...';
          // The same header and method every write to this server uses, so
          // a page left open from an older version is turned away rather
          // than half-working.
          fetch('/api/note?m=' + encodeURIComponent(text) + '&n=' + encodeURIComponent(who.value.trim()),
                { method:'POST', headers:{ 'X-HeatCaller':'1' } })
            .then(function(r){
              if (r.status === 404) { return { ok:false, closed:true }; }
              return r.json().catch(function(){ return { ok:false, error:'No answer from the laptop.' }; });
            })
            .then(function(d){
              if (d && d.ok) {
                msg.textContent = 'Sent to the race director. Thanks!';
                note.value = '';
                count();
                // Left disabled on success: one message is the point, and
                // a button that goes live again invites a second.
                return;
              }
              // A 404 is a page left open from before the club stopped
              // taking messages (20 Oct 2026, Bug Hunt 8) - said in the
              // same words the page itself now uses, not as "Not found".
              msg.textContent = (d && d.closed) ? 'Messages to the announcer aren\'t being taken right now.'
                              : (d && d.error) || 'Could not send that.';
              send.disabled = false;
            })
            .catch(function(){ msg.textContent = 'Could not reach the laptop.'; send.disabled = false; });
        });
        </script>
        """;
}
