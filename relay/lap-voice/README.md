# The lap voice's clips

Spoken laps on the relay's Live page and on a driver's own night play short
clips from this folder, in ElevenLabs' Finley voice ("Articulate Anchor"),
for every club. See src/lapvoice.ts. Until 24 Nov 2026 the voice was Daniel
("Steady Broadcaster"), one of ElevenLabs' Default voices, which stop working
on 31 Dec 2026.

The clips are not in git, all but lap6.mp3 (force-added when
Daniel's take was recut; Finley's replaced it on 24 Nov 2026) and the MODEL and
VOICE files, which must travel with the clips. Make them on your own PC, on your own ElevenLabs key,
with lap-voice-render.cmd in the relay folder, then run fly deploy from the
relay folder: the deploy carries this folder up with it. The full set is
2,745 clips, 45,768 characters (about $1.83 on eleven_v4_turbo; the v3 set is
kept in lap-voice-v3, the Flash set in lap-voice-flash). lap-voice-render.cmd
count says what is missing and what it would cost, and spends nothing.

The MODEL file here says which ElevenLabs model made the set, and the VOICE
file which voice (its ElevenLabs voice id). A set made on another model or in
another voice is made again in full, on the lap voice's model and in its
voice (LAP_VOICE in src/lapvoice.ts), and the old set is moved beside this
folder, not deleted: as lap-voice-daniel for Daniel's set (or the old voice's
id for any other voice), or lap-voice-<its model> for another model's
(lap-voice-older for one that never said). A folder with a MODEL file and no
VOICE file is the set made before VOICE existed, which was Daniel's. This
README stays in the folder.

The renderer also deletes each clip from the key's ElevenLabs history as it
is made, the same as the relay does for its lines. That needs the key's
Speech history permission; without it the run says so once and carries on.

A relay with no clips here still reads out laps, in the phone's own voice.

If you ever deploy from another PC or a fresh copy, run lap-voice-render.cmd
there first (it costs nothing for clips already made there), or copy this
folder across, or the relay goes back to the phone's voice.
