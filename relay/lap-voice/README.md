# The lap voice's clips

Spoken laps on the relay's Live page and on a driver's own night play short
clips from this folder, in ElevenLabs' Daniel voice ("Steady Broadcaster"),
for every club. See src/lapvoice.ts.

The clips are not in git, all but lap6.mp3 (a recut take, force-added so a
fresh copy keeps it). Make them on your own PC, on your own ElevenLabs key,
with lap-voice-render.cmd in the relay folder, then run fly deploy from the
relay folder: the deploy carries this folder up with it. The full set is
2,744 clips, 45,755 characters (about $1.83 on eleven_v4_turbo; the v3 set is
kept in lap-voice-v3, the Flash set in lap-voice-flash). lap-voice-render.cmd
count says what is missing and what it would cost, and spends nothing.

The MODEL file here says which ElevenLabs model made the set. A set made on
another model - or before MODEL existed - is made again in full on the lap
voice's model (LAP_VOICE.modelId in src/lapvoice.ts), and the old set is
moved beside this folder as lap-voice-<its model> (lap-voice-older for one
that never said), not deleted.

A relay with no clips here still reads out laps, in the phone's own voice.

If you ever deploy from another PC or a fresh copy, run lap-voice-render.cmd
there first (it costs nothing for clips already made there), or copy this
folder across, or the relay goes back to the phone's voice.
