# Troubleshooting

Find the symptom, then work through the likely causes in order. Where HeatCaller shows a message, it's quoted exactly so you can search this page for it.

Many problems leave a line in the app's console, which is the first place to look. See the [tour](../getting-started/tour.md) for where it is. If HeatCaller crashes, the full details go to `crash.log` in the data folder; see [Data files](data-files.md).

## Starting HeatCaller

| Symptom | Likely cause | Fix |
|---|---|---|
| `HeatCaller is already running.` | Another copy is open, maybe minimized or on another screen. Only one copy can run at a time, because two would overwrite each other's files and fight over the decoder. | Look for it on the taskbar. If you've just restarted it (after a restore or a change of the app's look), wait a few seconds; the new copy waits for the old one to close. |
| An error box titled `HeatCaller couldn't start` | Something failed during start-up. | Note the message. The full details are in `crash.log`; send that file with any bug report. |
| Console shows `[storage] Couldn't read "..." (...). Started with defaults instead; the original was kept as "....unreadable-...".` | A data file was locked by another program (antivirus, a sync tool) or damaged. HeatCaller started that part with defaults and kept the original. | Nothing is lost. See [Files kept as `.unreadable-` copies](backups.md#files-kept-as-unreadable-copies). |
| Console shows `[storage] Couldn't write "...": ...` | A save failed: the disk is full, the folder is locked down, or antivirus is holding the file. | Free up space or check permissions on `%AppData%\HeatCaller`. The app keeps running. |
| A `Backup password` dialog appears at start-up: `Unlock the keys from this backup` | You restored a backup from another PC, and it carries keys locked with the club's backup password. | Type the backup password and click **Unlock**. See [Restoring on a new PC](backups.md#restoring-on-a-new-pc). |
| A `Keys need entering again` message | The settings came from another PC or Windows account, and no password-locked copy of the keys came with them (or the password was skipped or wrong three times). | Re-enter the keys it lists and reprint QR check-ins. To avoid this next time, set a backup password. See [Backups](backups.md#if-the-backup-has-no-password-or-you-skipped). |
| API keys or PINs are blank after moving to a new laptop | Keys are locked to the Windows account that saved them. Copying the data folder by hand doesn't bring them. | Restore a backup made with a backup password, or re-enter them. |

## Decoders and timing

The [Decoders](../timing/decoders.md) page covers each connection type in detail.

| Symptom | Likely cause | Fix |
|---|---|---|
| `No CP2110 device found (VID 0x10C4, PID 0x86B9). Is the EasyLap decoder plugged in?` | The EasyLap decoder isn't connected, or its USB cable or port is faulty. | Plug it in, try another USB port, and connect again. |
| `Found the decoder but could not open it. If Next Level Timing (or another instance of this app) already has it open, ...` | Another program has the decoder open. Only one program can use it at a time. | Close Next Level Timing (or the other program), then connect again. |
| `Pick the decoder's COM port first.` | I-Lap selected with no COM port chosen. | Choose the decoder's COM port on the **Connection** page. |
| `Couldn't open COM...: ... If another timing program has the port, close it first.` | The COM port is in use or doesn't exist. | Close other timing software; check the port in Windows Device Manager. |
| Console shows `[ilap] Discarded ... characters with no line ending - check the baud rate.` | The baud rate doesn't match the decoder. | Pick the decoder's rate in **Baud:** on the **Connection** page (9600 is the usual default). |
| No **Next Level Timing (Neon API)** choice in **Connect via:** | That connection was retired in 0.50.0. | Choose **Direct EasyLap Decoder (USB)** or **I-Lap Decoder (Serial)**. See [Decoders](../timing/decoders.md). |

## Announcer and voice

See [Voice announcements](../announcer/voice.md) for setup.

| Symptom | Likely cause | Fix |
|---|---|---|
| No voice at all; console shows `[voice] NO VOICE - ...` and `Open the Voice tab and press Download under the Kokoro voice (about 310 MB, once) - until then the announcer can't say anything at all.` | Kokoro's voice file isn't on this PC, or it's there and won't load. Kokoro is the bottom of the ladder and nothing stands in underneath it, so nothing can speak. Said once for that reason, not for every line. | Open **Voice → Voice Settings** and press **Download (about 310 MB)** under the Kokoro choice in **Speak with**. The **Mixer** shows a red `NO VOICE:` notice meanwhile, and **Voice Settings** a red `NOTHING CAN SPEAK -` line. Announcement lines are still chosen and still appear on the Announcer screen and the Big Screen; sound effects, lap beeps, ambience, music and a soundscape's recorded voice lines are unaffected. See [When nothing can speak](../announcer/voice.md#when-nothing-can-speak). |
| No voice; console shows `[voice] Kokoro couldn't speak a line (...) - and nothing else on this PC can, so the line was lost. Said once for this reason, not for every line.` | Kokoro was asked for a line and failed on it. Nothing covers for Kokoro, so that line is gone. The reason is in the brackets. | Read the reason. If it mentions the model, press **Download Again (about 310 MB)** under the Kokoro choice on **Voice Settings** to fetch a fresh copy. See [Use the Kokoro voice](../announcer/voice.md#use-the-kokoro-voice). |
| Console shows `[voice] The Kokoro model wouldn't load (...) - downloading it again on Voice Settings usually fixes this.` | The voice file is there but won't load on this PC - usually a damaged or part-finished download. **Voice Settings** shows the same reason in red and its button reads **Download Again (about 310 MB)**. | Press **Download Again (about 310 MB)** under the Kokoro choice on **Voice Settings**. A line is tried again by itself a minute after each failure, so the Mixer may flicker between states until the file is replaced. |
| Console shows `[voice] The Kokoro voice "..." isn't on this PC - Michael (American, male) is speaking instead.` | The Kokoro voice saved on this laptop isn't among the voices beside HeatCaller.exe, usually because the settings came from another PC. Kokoro's own default speaks instead, so nothing goes quiet. | Pick a voice this PC has in **Kokoro voice:**, at the top of **Voice → Cast** (it shows there while Kokoro is chosen or standing in). If the message says Kokoro's voices folder is missing from beside HeatCaller.exe, reinstall HeatCaller (see [Installing](../getting-started/install.md)). |
| No voice; console shows `[voice] Skipped announcement - ElevenLabs API key or voice not configured.` | ElevenLabs is chosen under **Speak with** with no API key or no voice chosen, and the Kokoro voice isn't standing in (its box is unticked, or the PC hasn't got Kokoro's voice file). Common after moving PCs without a backup password. | Enter the key in **Voice → Voice Settings** and pick a voice in **ElevenLabs voice:** at the top of **Voice → Cast**, tick the stand-in box, or choose the Kokoro voice. |
| No voice and no sound effects | The announcer is muted. Muting silences both. | Unmute it. |
| `[voice] The HeatCaller voice has used 90,000 of 100,000 characters this month. When it's gone, the free Kokoro voice speaks every line until it starts again on 1 Oct. Lines already saved are kept either way.` | Nine tenths of the club's monthly characters are spent. Said once a month, the next time the relay speaks a line after the count crosses that mark. | Not a fault — a warning, so a spent allowance isn't first noticed mid-race. Turn a chatty announcement off (**Every lap completed**, **Lap time only**, **Personal best lap**), or use your own ElevenLabs key for the rest of the month. See [Your club's monthly allowance](../announcer/voice.md#your-clubs-monthly-allowance). |
| `[voice] This month's HeatCaller voice allowance is used up - the free Kokoro voice speaks every line until it starts again. Nothing is lost: every line already saved is kept and plays again then.` | The club's monthly characters on the HeatCaller voice are spent. Every line goes to the Kokoro voice, saved ones included, so the night is one voice rather than two. It holds until the relay speaks a line again, so nothing slips through mid-night. | Nothing is lost and nothing needs doing. The allowance starts again next calendar month; the line under the HeatCaller choice on **Voice Settings** says when. To keep the natural voice tonight, choose your own ElevenLabs key under **Speak with**. Club admins can raise the club's `voice.monthlyChars`; the relay is asked again about every half hour, so a club topped up mid-month is speaking again within half an hour. See [When the HeatCaller voice can't speak a line](../announcer/voice.md#when-the-heatcaller-voice-cant-speak-a-line). |
| `[voice] The HeatCaller relay can't be reached (...) - the free Kokoro voice is standing in. It is tried again in the background about once a minute.` | No internet at the track, the relay is asleep or down, or the request timed out. | Check the laptop's internet. Lines already saved still play in the HeatCaller voice, and the relay is tried again about once a minute. When it answers, the console says `[voice] The HeatCaller voice is back - it speaks from the next line, and the Kokoro voice stands down.` |
| `[voice] The HeatCaller voice isn't included for this club (...) - the free Kokoro voice is standing in. It is tried again about every half hour.` (or `... isn't switched on for this relay`, `the relay didn't accept this club's token`, `that HeatCaller voice isn't available any more`) | The relay refused for a reason a minute won't mend. | Ask whoever runs the relay: the club needs `"voice": { "enabled": true }`, the relay needs the `RELAY_VOICE_KEY` secret, and the club's token must be current. For a voice that's gone, pick another in **HeatCaller voice:** at the top of **Voice → Cast**. See [The HeatCaller voice](relay-hosting.md#the-heatcaller-voice). |
| `[voice] No HeatCaller voice is chosen yet - the free Kokoro voice is speaking. Pick one at the top of the Cast tab.` | The HeatCaller voice is chosen under **Speak with**, but no announcer voice has been picked from the relay's list. | Open **Voice → Cast** and pick one in **HeatCaller voice:**, at the top of the **Announcer** card. Press **Refresh** beside it if the list is empty. |
| `[voice] The HeatCaller voice couldn't speak a line (...) - the free Kokoro voice said it.` | That one line: empty, or longer than the 600 characters the HeatCaller voice speaks at once. The count is of the spoken words, so audio tags don't add to it. The next line is asked for as normal. | Shorten the line. Announcement lines are far shorter than this; a very long AI recap or Timetable reminder is the usual cause. |
| `[voice] A saved release call for ... wouldn't play (...) - rendering it again.` | A saved audio file was damaged or cut short, so it couldn't be decoded. | Nothing to do. The line is forgotten and rendered again by itself, on either paid voice. If it keeps happening, check the laptop's disk. |
| `[voice] The voice queue was full, so the free Kokoro voice said a line. Said once, not for every line.` | Several lines wanted rendering at once and one waited 30 seconds for its turn. This is the laptop's own queue, not the relay, so nothing is rested for it. | Usually a slow connection. See the ElevenLabs rows below for the same situation with your own key. |
| `[voice] No ElevenLabs API key is set - the free Kokoro voice is speaking instead.` (or `The ElevenLabs key is locked to the PC it was saved on - ...`, or `No ElevenLabs voice is selected - ...`) | ElevenLabs is chosen but can't be asked, so the Kokoro voice is standing in. A locked key means the settings came from another PC. | Enter the key on **Voice Settings** and pick a voice in **ElevenLabs voice:** at the top of **Voice → Cast**; after a move, restore a backup made with a backup password. Or choose the Kokoro voice if that's what you want. |
| `[voice] Can't reach ElevenLabs (...) - the free Kokoro voice is standing in. ...` | No internet, or requests to ElevenLabs are timing out. | Check the laptop's internet connection. Lines already saved still play in your ElevenLabs voice, and ElevenLabs is tried again about once a minute. When it answers, the console says `[voice] ElevenLabs is reachable again - it speaks from the next line, and the Kokoro voice stands down.` |
| `[voice] ElevenLabs turned the API key down (...) - the free Kokoro voice is standing in. ...` | ElevenLabs refused the key: it's wrong, or it has been revoked. Lines go straight to the Kokoro voice meanwhile, and lines already saved still play in your ElevenLabs voice. | Check the key and paste it again on **Voice Settings**; a changed key is tried straight away. Otherwise ElevenLabs is tried again in the background about once a minute. |
| `[voice] ElevenLabs quota exceeded - the free Kokoro voice is standing in until it's back. ...` | Your ElevenLabs account has used its allowance, and the Kokoro voice is saying the new lines. | Top up the account or wait for the monthly reset. ElevenLabs is still asked first for each line, so it takes over by itself the moment a request succeeds, and the console says `[voice] ElevenLabs quota available again - voice announcements have resumed.` |
| `[voice] ElevenLabs quota exceeded - voice announcements are paused for now. ...` | Your ElevenLabs account has used its allowance, and the Kokoro voice isn't standing in. | Top up the account or wait for the monthly reset. Voice resumes by itself the next time a request succeeds, and the console says `[voice] ElevenLabs quota available again - voice announcements have resumed.` Sound effects keep working meanwhile. To keep talking in the meantime, tick the stand-in box under **Speak with**. |
| `[voice] ElevenLabs couldn't speak a line (...) - the free Kokoro voice is saying the lines it can't. Said once for this reason, not for every line.` | ElevenLabs refused a request for another reason, such as too many requests or a server error, or a line waited 30 seconds for its turn behind HeatCaller's other ElevenLabs requests. Its reason is in the brackets. ElevenLabs is still asked first for the next line. | Read the reason. For a key problem, paste the key again in **Voice Settings**. |
| `[voice] Playback failed: ElevenLabs didn't respond within 30 s.` | ElevenLabs was slow or the internet connection dropped, and the Kokoro voice isn't standing in. (With it standing in, the Kokoro voice says the line instead.) | Check the laptop's internet connection. Occasional misses on poor venue Wi-Fi are expected; the announcer carries on with the next line. |
| `[voice] Playback failed: No synthesis slot came free within 30 s.` | Several lines were waiting for ElevenLabs at once and the queue backed up, and the Kokoro voice isn't standing in. (With it standing in, the Kokoro voice says the line instead. This is the laptop's own queue, so ElevenLabs isn't rested for it.) | Usually a slow connection; see above. |
| `[voice] Playback failed: ElevenLabs speech request failed (429): ...` | ElevenLabs refused because too many requests were in flight for your account. HeatCaller keeps at most two going at once to leave room, but another app or device using the same account can push it over. | Don't run voice tests or other ElevenLabs tools on the same account during a race. |
| `[voice] Playback failed: ElevenLabs speech request failed (401): ...`, not about the quota | ElevenLabs turned the API key down, and the Kokoro voice isn't standing in. A wrong or revoked key is the usual cause. | Check the key and paste it again in **Voice Settings**. |
| `[voice] Playback failed: ElevenLabs speech request failed (...)` with another code | ElevenLabs refused the request; its own reason follows the code. | Read the reason. For a key problem, paste the key again in **Voice Settings**. |

For the local commentary model, see [Commentary](../announcer/commentary.md).

## Phones and the Wi-Fi server

These are about the **Local Screens** page. Setup is in [Local Screens](../screens/phone-and-web.md).

| Symptom | Likely cause | Fix |
|---|---|---|
| **Start Server** shows `Windows blocked the app from accepting connections from other devices.` | Windows needs a one-time URL reservation for the port before an app can accept connections from other devices. | Open an administrator Command Prompt and run the command the message shows, for example `netsh http add urlacl url=http://+:8080/ sddl=D:(A;;GX;;;WD)` (with your port). Or run HeatCaller as administrator. |
| `Couldn't start the web server on port ...: ...` | The port is already used by another program. | Pick another **Port:** and start again. |
| The server is running but phones time out | The Windows firewall is blocking the port, or the Wi-Fi is set as a Public network. | In an administrator Command Prompt: `netsh advfirewall firewall add rule name="HeatCaller web 8080" dir=in action=allow protocol=TCP localport=8080 profile=private` (with your port), and set the venue Wi-Fi as a Private network in Windows. |
| Phones still can't connect, firewall is fine | The phone is on a different network (mobile data, or a "guest" Wi-Fi that keeps devices apart). | Put the phone on the same Wi-Fi as the laptop, not a guest network. Turn off mobile data while testing. |
| The addresses on the page say `localhost` | The laptop isn't connected to a network. | Connect the laptop to the venue Wi-Fi, then stop and start the server. |
| `Server started - check the PINs` | The **Remote PIN** or **Manager PIN** is missing or too short, or the check-in PIN equals one of them. | Stop the server, fix the PINs as the message says, and start it again. |
| The phone remote says `The remote needs a PIN. Set one on the laptop's Local Screens page first.` | No **Remote PIN** is set. | Set a PIN of four or more digits. |
| `Wrong PIN` | The PIN typed on the phone doesn't match. | Check the PIN on the **Local Screens** page (**Show PINs**). |
| `Too many wrong PINs. Wait a few minutes.` (HTTP 429) | Ten wrong PINs from that phone in five minutes. | Wait five minutes, or stop and start the server, which clears the count. |
| `Too many changes from this phone. Wait a minute.` (HTTP 429) | More than 30 check-in changes a minute from one phone. | Wait a minute. |
| `Laptop still starting - try again in a moment.` (HTTP 503) | HeatCaller's main window hasn't finished opening. | Wait a few seconds and try again. |
| `This address is not served here.` (HTTP 421) | The page was opened through a public domain name pointing at the laptop, which the server refuses to protect against DNS rebinding. | Use the address shown on the **Local Screens** page (the laptop's IP address). |
| `This page is out of date - reload it and try again.` | The phone has an old copy of the page. | Reload the page. |
| The remote asks to confirm STAGE or COUNTDOWN | A race is running - it may have gone green while the command was on its way - and staging would wipe its standings. Over a demo, it would end the demo. | Confirm only if you really mean to restart. The laptop won't ask again. During practice it doesn't ask at all: it refuses the command, so stop practice on the **Practice** page first. |
| `The laptop could not take that command just now - it was NOT sent. Try again.` | Race Control couldn't take the command at that moment. Nothing happened on the laptop. | Press the button again. |
| `The laptop is closing - that command may not have gone. Check the laptop.` (or `The laptop is closing - it was NOT sent.`) | HeatCaller was closing on the laptop when the command arrived. | Check the laptop, and press the button again once HeatCaller is running. |
| The remote shows `GREEN not done:` (or another button's name) and a reason | Race Control refused the command, and nothing changed. For example `Practice is running - stop it on the Practice page first.`, or for **CHECKERED** before the green `The race hasn't gone green yet. To call off this start, press Reset.` | Deal with the reason. See [Running a race](../race-control/running-a-race.md#flags). |
| `GREEN sent: Check the laptop - it may be asking a question.` | The laptop had the command but hadn't finished with it within about 3 seconds, usually because a question is open on its screen. | Answer the question on the laptop. |
| `Connection lost. Check the laptop to see whether GREEN went.` | The phone lost contact while it waited for the answer, or had no answer within 12 seconds. The command may or may not have reached the laptop. | Check the laptop or the live page before pressing again, then check the Wi-Fi. |
| The race-day manager says `Wrong PIN. Enter the manager PIN again.` | A wrong Manager PIN. The phone emptied the box and forgot it. | Type the PIN again. Check it on the **Local Screens** page (**Show PINs**). |
| `Too many wrong PINs on this Wi-Fi. The PIN is not being accepted for five minutes.` (HTTP 429) | 50 wrong tries at that PIN in five minutes, from any devices. Phones that already entered the PIN keep working. | Wait five minutes, or stop and start the server. Consider a longer PIN. |

## Online relay

Setup is in [Setting up online publishing](../online/setup.md); hosting is in [Hosting the relay](relay-hosting.md).

### Test Connection results

| Message | Cause | Fix |
|---|---|---|
| `Connected as ....` | Everything works. The settings are saved. | Tick **Publish to the relay** if you haven't. |
| `Enter the relay's address first, for example https://your-relay.fly.dev` | **Address:** is empty. | Enter the relay's address. |
| `A club id and a publish token are both needed.` | **Club id:** or **Publish token:** is empty. | Fill both in. |
| `That club id is not one the relay can hold. ...` | The club id has characters or a length the relay doesn't allow. | Use 3 to 32 lower-case letters, digits and single hyphens, for example `acme-rc`. |
| `The relay address must start with https:// - the token is never sent unencrypted over the internet. ...` | The address is `http://` and not on this laptop or your own network. | Use the `https://` address. |
| `Could not reach https://.... It did not answer in time.` (or another reason) | No internet, a typo in the address, or the relay is down. | Check the laptop's internet, the spelling, and that the relay is running (`fly status`). A relay that has been asleep answers within a few seconds; test again. |
| `Reached ..., but it does not look like a relay (HTTP ...).` | The address answers but isn't a relay: a typo, or a page address pasted with extra parts. | Use the relay's base address, such as `https://heatcaller-relay.fly.dev`. |
| `The relay does not recognise that token.` (401) | The token is mistyped, was replaced by a re-mint, or you pasted the 64-character hash instead of the token. | Paste the current `hg_` token. If it's lost, re-mint it ([how](relay-hosting.md#replacing-a-leaked-or-lost-token)). |
| `That token belongs to a different club. Check the club id.` (403) | The token is valid but for another club id. | Correct **Club id:** to match the token. |
| `The relay has no club called "...".` (404, `No such club`) | The club id isn't in the relay's config, or has a typo. | Check the id. If you host the relay, mint the club and run `fly-set-config.cmd`. |
| `This relay is running an older build that has no /whoami. ...` | The relay is out of date. | Update and redeploy the relay (`fly deploy --ha=false`). |
| `The relay answered HTTP 308.` | The address is an old relay that now forwards to a new one. HeatCaller never follows redirects, so its token can't be carried elsewhere. | Change **Address:** to the new relay's address. |
| `... Not saved - the boxes were changed during the test. Test again.` | You edited a box while the test ran. | Click **Test Connection** again. |

**Restore Last Working** puts back the last address, club and token that tested clean, if an edit went wrong.

### While publishing

The **Status** section of the **Club Website** page shows `Publishing`, `Offline`, `Waiting for the first publish`, `Not configured` or `Not publishing`, with a line of detail.

| Symptom or message | Cause | Fix |
|---|---|---|
| `Offline` with `The relay does not recognise this token.` or `This token belongs to a different club.` | The token was replaced or mistyped. Nothing is thrown away; the queue waits. | Paste the correct token and **Test Connection**. The queue then catches up. |
| `The relay has no club called "...". Check the club id.` | Club id typo, or the club was removed from the relay. | Fix the id, or ask whoever hosts the relay. |
| `The relay is an older build that cannot take this. Redeploy it.` or `The relay has no "..." channel - it may need updating.` | The relay is older than the app. | Redeploy the relay with the current code. |
| `A ... payload was too large to publish.` / `The relay refused the payload as too large.` | A document (often badges or track pictures) was over the limit. | Use smaller pictures. If you host the relay, don't set `maxBodyBytes` below the default. |
| `... could not be published.` count keeps rising | The relay refused some documents outright. | Read the error after it; the cause is usually one of the rows above. |
| Pages show results from another laptop, or flip between two | Two laptops are publishing with the same club and token. The relay keeps whichever copy has the higher number, and each laptop re-sends once to get above the other. | Publish from one laptop per club. Turn off **Publish to the relay** on the other. |
| A result doesn't replace the one online after the Windows clock was changed | HeatCaller numbers what it publishes by the time. If the clock went backwards, the relay already holds a "newer" copy. HeatCaller notices this (`applied:false`) and re-sends once with a number above the relay's, so it normally fixes itself. | Set Windows to set the time automatically. If a page is still stale, restart HeatCaller, which re-sends the last 36 hours. |
| A page or the check-in list answers `The relay could not read that just now. Try again in a moment.` | The relay had a brief problem reading its storage. | Try again; it normally clears by itself. If it keeps happening, check the relay's logs and its volume. |
| The first publish after a quiet spell is slow | The relay machine was asleep and is waking. | Nothing to do; it retries. |

### Online check-in

| Symptom or message | Cause | Fix |
|---|---|---|
| The check-in page says `Check-in is closed` | Online check-in isn't switched on, or publishing is off. | On the **Club Website** page tick **Let drivers check in from their own phones** and **Publish to the relay**. |
| `The club has not set a check-in PIN, so the list is closed. QR check-ins still work.` | No staff PIN and no personal PINs are set. | Set **Staff PIN:**, or give racers their own PINs. |
| `That PIN is not right.` | Wrong PIN. It counts toward the limits. | Check the PIN. |
| `That PIN has changed - check your latest email.` | The racer's personal PIN was replaced by a new set. | Use the PIN from the latest email, or look it up with **Show PINs** on the **Club Website** page. |
| `Too many wrong PINs from this connection. Try again in a few minutes.` (or `Too many attempts from this connection...`) | Too many wrong PINs from that connection (see [the limits](security-and-privacy.md#pins-and-rate-limits)). | Wait up to ten minutes. QR check-ins keep working, with the driver's own PIN when they have one. |
| `Too many wrong PINs have been tried for this club, so PIN sign-in is closed for about ...` | 50 wrong PINs in 10 minutes across the club. Each lockout in a row lasts longer, up to 12 hours. | Change the staff PIN on the **Club Website** page: that reopens sign-in at once. Phones already signed in keep working, and so do QR check-ins, with the driver's own PIN when they have one. |
| `Too many wrong PINs for this driver. Wait ten minutes, or check in at the desk.` | Five wrong PINs were typed into one driver's QR check-in card in ten minutes. This limit is per driver, apart from the others. | Wait ten minutes, or check the driver in at the desk. |
| The **Club Website** page says `PIN sign-in is locked after many wrong guesses until ...` | The relay has closed PIN sign-in for the club. | Change the staff PIN (or set one) to reopen it. |
| `QR check-in not recognised` with `That driver is not on today's check-in list.` | The QR check-in is for someone not entered today, or was printed with a different QR check-in key: before **Reissue All QR Check-ins** was used, or on another PC. | Enter the driver for today's event. If the QR check-in was printed before a reissue, or on another PC without a backup password, reprint it. |
| At the desk: `That QR check-in is out of date - print a new one.` | An online QR check-in whose code no driver on this laptop has any more: printed before **Reissue All QR Check-ins**, on another laptop's QR check-in key, or for a driver since deleted. | Print that driver a new QR check-in. See [Reissue all QR check-ins](../online/check-in.md#reissue-all-qr-check-ins). |
| The race-day manager over the relay says `The race-day manager is busy - the laptop has not caught up with recent requests yet. Try again shortly.` | Requests are waiting on the relay because the laptop isn't collecting them. The manager has an allowance of its own on the relay, and it's full. | Make sure HeatCaller is running with **Publish to the relay** ticked and a working connection, then try again. |
| `Check-in is busy - the timing desk has not collected recent taps yet. Try again in a minute, or check in at the desk.` | 500 taps are waiting on the relay because the laptop isn't collecting them. | Make sure HeatCaller is running with **Publish to the relay** ticked and a working connection. |
| Status shows `The relay answered "not found" for check-ins. ...` | Club id mismatch, or a relay older than online check-in. | Check the club id; redeploy the relay if needed. |
| Console shows `[relay] Ignored an old check-in for ...` | The tap was from before a change made at the desk, or too old. | Nothing to do; the desk's change wins. |

See [Online check-in](../online/check-in.md) for how it works end to end.

### Hosting problems

If you run the relay yourself and it won't start, `fly logs` shows why. The start-up messages and fixes are listed in [Hosting the relay](relay-hosting.md#the-relay-refuses-to-start-when).

## Email (racer PINs)

Full setup is in [Emailing racer PINs](../online/email.md).

| Message | Cause | Fix |
|---|---|---|
| `Gmail refused the sign-in: use an app password, not the normal one.` | You entered the normal Gmail password, or the app password is wrong. | Create an app password (Google Account → Security → App passwords) and paste it into **App password:**. |
| `... refused the sign-in: check the club email address, and use an app password, not the normal one.` | Same, for another provider. | Check **Club email address:** and use an app password. |
| `Port 465 isn't supported - use 587, which every mainstream provider also offers.` | Port 465 needs a connection type HeatCaller doesn't use. | Set **Port:** to 587. |
| `The mail server's port isn't valid - it is usually 587.` | The port is out of range. | Use 587. |
| `No mail server is set.` / `Enter the club's email address.` / `Enter the mailbox's app password.` | A field is empty. | Fill it in. Choosing a **Provider:** fills in the server and port. |
| `Couldn't send through ...: ...` | The mail server couldn't be reached. | Check the internet connection and the **Server:** name. |
| `Stopped: the first three emails all failed (...)` | Something is wrong for every message, usually sign-in. | Fix the cause shown, then **Send Test Email**. |
| `Online check-in isn't set up and switched on yet, so there is nowhere to use a PIN.` | Online check-in is off. | Switch it on first. |
| `Nobody with a PIN has an email address.` | No driver profiles have an email address. | Add email addresses to driver profiles. |

## Backups and restore

| Message | Cause | Fix |
|---|---|---|
| `No saved data found to back up yet - nothing has been configured or raced yet.` | Fresh install with nothing saved. | Nothing to back up yet. |
| `Backup failed` | The backup folder is unavailable, or the disk is full. | Check **Folder:** on the **Backup & Restore** page, or choose another with **Change**. |
| Console shows `[backup] Automatic backup failed: ...` | Same as above, for an automatic backup. | Same fix. It tries again every hour. |
| `That zip isn't a backup made by this app - ...` | The zip isn't a HeatCaller backup. | Pick a `HeatCaller-...zip` file. |
| `That backup tries to write outside the app's data folder (...)` | The zip wasn't made by HeatCaller and could be harmful. | Don't use it. |
| `That backup unpacks to far more than any real backup could. ...` | The zip would unpack to over 2 GB. | Use another backup. |
| `That password doesn't open this backup. ... left.` | Wrong backup password. | Try again; three tries in all. |
| `The restore only partly worked. ...` | Another program had some data files open, so they weren't replaced. | Close anything that might have them open and restore the same backup again, or restore the safety backup the message names. |
| `A race or practice is running. Restore a backup once it has finished - ...` | A restore restarts the app, which would end the session. | Finish or stop the race or practice, then restore. |
| `A backup is being written - try again in a moment.` | A backup (often the automatic one) is still being written. | Wait a moment and try the restore again. |
| Automatic backup didn't happen during a race | Automatic backups wait until no race or practice is running. | Nothing to do: it's made once the session finishes. |
| Keys missing after restoring on a new PC | The backup was made without a backup password, or before one was set. | Re-enter keys and reprint QR check-ins; set a backup password for next time. |

More detail in [Backups](backups.md).

## Big Screen

See [Big Screen](../screens/big-screen.md).

| Symptom | Likely cause | Fix |
|---|---|---|
| The Big Screen opens on the laptop, not the projector or TV | It opens as a window in the middle of the main screen. | Drag it onto the other display, then press <kbd>F11</kbd> for full screen. <kbd>Esc</kbd> closes it. |
| The Big Screen turned light after updating to 0.48 | The crowd screens match the app, and the app uses a light look such as **Daylight**. | On the **Themes** page, choose **Race Wall** (or another dark look) for the crowd screens. See [Looks and the club logo](../screens/looks-and-logo.md). |
| Column changes don't show on the Big Screen | The Big Screen picks up its columns when it opens. (A new look or logo shows straight away.) | Close it and open it again (**Big Screen** on Race Control, **Open Big Screen** on Practice or Local Screens). |
| Sponsor slides don't appear | **Show sponsor slides on the Big Screen** is off, there are no slides, or the Big Screen was already open when you turned them on. | Tick the box on the **Sponsors** page, add slides, then reopen the Big Screen. |
| Sponsor slides never show during a race | **Never interrupt the leaderboard while a race is actually running (recommended)** is ticked. | That's intended. Untick it if you want slides during races. |
| The leaderboard stays up when a sponsor slide is due | That slide's image file is missing, can't be read, or is over the size limits (50 MB, 20000 pixels across or down, 40 million pixels in all), so the Big Screen stays on the leaderboard. | Remove the slide on the **Sponsors** page and add the image again, or a smaller copy of it. |

## Still stuck?

Before asking for help:

- Save the support file: **Save Support Info** on the **Backup & Restore** page. It holds the version, the console from the last few runs and `crash.log`, with driver names and email addresses taken out. See [Save Support Info](backups.md#save-support-info).
- Note what you were doing when it happened, and copy the exact message from the console or take a screenshot.
- Note the version you're on: **About HeatCaller...** on the **Getting help** card shows it, along with the licences of the components HeatCaller ships.

Then email it all to **support@heatcaller.com**. The address is on the **Getting help** card too, with **Copy Address** beside it, and the crash dialog names it as well.
