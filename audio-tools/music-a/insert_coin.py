"""Insert Coin — chiptune / arcade theme (C major -> D major, 148 BPM).

Run:  python3 insert_coin.py [out.mp3]      (MIX_ONLY=1 re-mixes cached stems)
2A03-inspired voices (two pulse channels with duty changes, triangle bass, LFSR noise drums)
produced with a modern chain: sub kick, echo, room reverb, bus compression, true-peak mastering.
"""
import sys, os, time
import numpy as np
from scipy import signal
from scipy.ndimage import uniform_filter1d
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dsp
from dsp import SR, mtof, db, pulse, triangle, sine, adsr, tv_filter, hp, lp, eq
from song import Arr, auto_balance

T0 = time.time()
rng = np.random.default_rng(77)
BPM = 148
NB = 92
A = Arr(BPM, NB, tail_s=3.0)
SPB = A.spb
FRAME = 1 / 60.0  # NES frame rate for stepped envelopes

# ------------------------------------------------------------------ harmony
# chord -> (arp tones, bass root)
CH = {
    'C': ((60, 64, 67), 36), 'Am': ((57, 60, 64), 45), 'F': ((57, 60, 65), 41), 'G': ((59, 62, 67), 43),
    'Dm': ((57, 62, 65), 38), 'Em': ((59, 64, 67), 40), 'E': ((56, 59, 64), 40), 'A': ((57, 61, 64), 45),
    'A7': ((55, 61, 64), 45), 'D': ((57, 62, 66), 38), 'F#m': ((57, 61, 66), 42), 'Bm': ((59, 62, 66), 47),
}
VERSE = ['C', 'Am', 'F', 'G', 'C', 'Am', 'Dm', 'G']
VERSE2 = ['C', 'Am', 'Dm', 'G', 'C', 'Am', 'F', 'G']
CHORUS = ['F', 'G', 'Em', 'Am', 'F', 'G', 'C', 'C', 'F', 'G', 'Em', 'Am', 'Dm', 'G', 'C', 'C']
CHORUS_D = ['G', 'A', 'F#m', 'Bm', 'G', 'A', 'D', 'D', 'G', 'A', 'F#m', 'Bm', 'Em', 'A', 'D', 'D']
BRIDGE = ['Am', 'F', 'C', 'G', 'Am', 'F', 'Dm', 'E']
prog = (['C', 'Am', 'F', 'G'] + VERSE * 2 + CHORUS + VERSE + VERSE2 + CHORUS[:8] + BRIDGE +
        ['F', 'G', 'A', 'A7'] + CHORUS_D + ['G', 'A', 'D', 'D'])
assert len(prog) == NB, len(prog)
SECTIONS = [('Intro', 0, 4), ('Verse', 4, 16), ('Chorus', 20, 16), ("Verse 2", 36, 16), ('Chorus 2', 52, 8),
            ('Bridge', 60, 8), ('Build', 68, 4), ('Final chorus', 72, 16), ('Ending', 88, 4)]


def section_of(bar):
    for name, b0, n in SECTIONS:
        if b0 <= bar < b0 + n:
            return name


# ------------------------------------------------------------------ melody data (beat, dur, midi[, flags])
C5, D5, E5, F5, G5, A5, B5, C6, D6, E6 = 72, 74, 76, 77, 79, 81, 83, 84, 86, 88
G4, A4, B4, GS5, F4, E4 = 67, 69, 71, 80, 65, 64
CALL1 = [[(0, .5, E5), (.5, .5, G5), (1, .75, C6), (1.75, .25, B5), (2, .5, A5), (2.5, .5, G5), (3, .5, E5), (3.5, .5, G5)],
         [(0, 1, A5, 's'), (1, .5, E5), (1.5, .5, C5)]]
RESP1 = [[(0, .5, F5), (.5, .5, A5), (1, .5, C6), (1.5, .5, A5), (2, .5, G5), (2.5, .5, F5), (3, .5, E5), (3.5, .5, D5)],
         [(0, .75, D5), (.75, .25, E5), (1, .5, D5), (1.5, .5, B4), (2, 1, G4)]]
CALL2 = [CALL1[0], [(0, .5, A5), (.5, .5, C6), (1, 1, E6, 's')]]
RESP2 = [[(0, .5, F5), (.5, .5, E5), (1, .5, D5), (1.5, .5, F5), (2, 1, A5), (3, .5, G5), (3.5, .5, F5)],
         [(0, .5, D5), (.5, .5, B4), (1, .5, G4), (1.5, .5, B4), (2, 1.5, D5, 's')]]
VERSE2_MEL = [
    [(0, .25, G5), (.25, .25, A5), (.5, .5, G5), (1, .5, E5), (1.5, .5, C5), (2, .5, E5), (2.5, .5, G5), (3, 1, C6)],
    [(0, .5, B5), (.5, .5, A5), (1, 1, E5), (3, .5, A5), (3.5, .5, B5)],
    [(0, .5, C6), (.5, .5, B5), (1, .5, A5), (1.5, .5, F5), (2, 1, D5), (3, 1, F5)],
    [(0, .75, G5), (.75, .25, F5), (1, .5, E5), (1.5, .5, D5), (2, 2, B4, 's')],
    [(0, .25, G5), (.25, .25, A5), (.5, .5, G5), (1, .5, E5), (1.5, .5, C5), (2, .5, E5), (2.5, .5, G5), (3, 1, C6)],
    [(0, .5, C6), (.5, .5, B5), (1, .5, A5), (1.5, .5, E5), (2, 1, A5), (3, 1, C6)],
    [(0, .5, C6), (.5, .5, A5), (1, .5, F5), (1.5, .5, A5), (2, 1, C6), (3, 1, A5)],
    [(0, .5, B5), (.5, .5, D6), (1, .5, B5), (1.5, .5, G5), (2, 2, D6, 's')],
]
CHO = [
    [(0, 1, A5), (1, 1, C6), (2, .5, A5), (2.5, .5, G5), (3, 1, F5)],
    [(0, 1.5, G5), (1.5, .5, D5), (2, 1, G5), (3, 1, B5)],
    [(0, 1, B5), (1, .5, G5), (1.5, .5, E5), (2, 1, B5), (3, .5, C6), (3.5, .5, B5)],
    [(0, 2, A5, 's'), (2, .5, E5), (2.5, .5, A5), (3, .5, B5), (3.5, .5, C6)],
    [(0, 1, C6), (1, 1, A5), (2, .5, F5), (2.5, .5, A5), (3, 1, C6)],
    [(0, 1.5, D6), (1.5, .5, C6), (2, 1, B5), (3, 1, G5)],
    [(0, .5, C6), (.5, .5, G5), (1, .5, E5), (1.5, .5, G5), (2, 2, C6)],
    [],
]
CHO_B = CHO[:4] + [
    [(0, 1, D6), (1, .5, C6), (1.5, .5, A5), (2, 1, F5), (3, 1, A5)],
    [(0, 1, B5), (1, .5, C6), (1.5, .5, D6), (2, 1, B5), (3, 1, G5)],
    [(0, 3.5, C6, 's')],
    [],
]
# the "response" jingle answering the chorus (second pulse channel), bar 8 of each half
JINGLE = [(0, .25, E6), (.25, .25, D6), (.5, .25, C6), (.75, .25, G5), (1, .25, E5), (1.25, .25, G5), (1.5, .5, C6)]
BRIDGE_MEL = [
    [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, 1, B4)], [(0, 2, A4), (2, 1, C5), (3, 1, F5)],
    [(0, 1.5, E5), (1.5, .5, D5), (2, 1, E5), (3, 1, G5)], [(0, 4, D5)],
    [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, 1, E5)], [(0, 2, A5), (2, 1, G5), (3, 1, F5)],
    [(0, 1.5, F5), (1.5, .5, E5), (2, 1, D5), (3, 1, F5)], [(0, 2, E5), (2, 2, GS5)],
]
INTRO_MEL = [
    [],
    [(0, .5, A5), (.5, .25, A5), (1.5, .5, E5), (2, 1, A5), (3, 1, C6)],
    [(0, .5, C6), (.5, .25, C6), (1.5, .5, A5), (2, 1, C6), (3, 1, D6)],
    [(0, 1.5, D6, 's'), (1.5, .5, B5), (2, .5, G5), (2.5, .5, A5), (3, .5, B5), (3.5, .5, D6)],
]
BUILD_MEL = [  # rising sequence over F G A A7
    [(0, .5, A5), (.5, .5, C6), (1, .5, F5), (1.5, .5, A5), (2, .5, C6), (2.5, .5, F5), (3, .5, A5), (3.5, .5, C6)],
    [(0, .5, B5), (.5, .5, D6), (1, .5, G5), (1.5, .5, B5), (2, .5, D6), (2.5, .5, G5), (3, .5, B5), (3.5, .5, D6)],
    [(0, .5, C6 + 1), (.5, .5, E6), (1, .5, A5), (1.5, .5, C6 + 1), (2, .5, E6), (2.5, .5, A5), (3, .5, C6 + 1), (3.5, .5, E6)],
    [(0, 2, E6, 's'), (2, .25, C6 + 1), (2.25, .25, A5), (2.5, .25, G5), (2.75, .25, E5), (3, 1, C6 + 1)],
]
ENDING_MEL = [
    [(0, .5, B5 + 0), (.5, .5, A5), (1, .5, G5), (1.5, .5, A5), (2, 1, B5), (3, 1, D6)],
    [(0, .5, C6 + 1), (.5, .5, B5), (1, .5, A5), (1.5, .5, B5), (2, 1, C6 + 1), (3, 1, E6)],
    [(0, .5, D6), (1.5, .25, A5), (1.75, .25, D6), (2, .25, 90), (2.25, .25, 93), (3, 1.5, 98 - 12)],
    [],
]

MAJOR_C = [0, 2, 4, 5, 7, 9, 11]


def transpose(spec, k):
    return [[(n[0], n[1], n[2] + k) + tuple(n[3:]) for n in bar] for bar in spec]


def harmony_below(spec, tonic):
    """Diatonic third below (in the major key of `tonic` pitch class)."""
    scale = [(tonic + s) % 12 for s in MAJOR_C]
    out = []
    for bar in spec:
        nb = []
        for n in bar:
            m = n[2]
            # walk down to the scale tone two steps lower
            cands = [m - d for d in range(1, 5) if (m - d) % 12 in scale]
            h = cands[1] if len(cands) > 1 else m - 3
            nb.append((n[0], n[1], h) + tuple(n[3:]))
        out.append(nb)
    return out


CACHE = os.environ.get("MUSIC_A_CACHE", "/tmp/music-a-cache") + "/ic"  # rendered stems, reused by MIX_ONLY=1
MIX_ONLY = os.environ.get("MIX_ONLY") == "1"
kick_times = []

if MIX_ONLY:
    for f in os.listdir(CACHE):
        if f.endswith('.npy') and f != 'kick_times.npy':
            A.buses[f[:-4]] = np.load(os.path.join(CACHE, f))
    kick_times = list(np.load(os.path.join(CACHE, 'kick_times.npy')))
    print("loaded cached stems")
else:
    # ------------------------------------------------------------------ chip voices
    def frame_env(env):
        """Quantise an envelope to 60 Hz frames and 16 volume steps (2A03 style), then de-click."""
        n = len(env)
        fl = int(SR * FRAME)
        idx = (np.arange(n) // fl) * fl
        q = np.round(env[np.minimum(idx, n - 1)] * 15) / 15
        return uniform_filter1d(q, 48)

    def chip_phrase(notes, kind='pulse', duty=0.25, vib_depth=0.28, rel=0.03, decay=0.12, sus=0.7, chan_detune=0.0):
        """notes: list of (t, dur, midi, vel, flags). Returns mono signal and start time."""
        t0 = notes[0][0]
        t_end = max(s + d for s, d, *_ in notes) + rel + 0.05
        n = int((t_end - t0) * SR)
        tt = np.arange(n) / SR
        pitch = np.zeros(n); amp = np.zeros(n); vib = np.zeros(n)
        dut = np.full(n, duty) if np.isscalar(duty) else None
        for i, (s, d, m, v, fl) in enumerate(notes):
            a = int((s - t0) * SR); b = min(n, int((s - t0 + d) * SR))
            pitch[a:] = m
            if 's' in fl:  # scoop up into the note (pitch slide)
                k = min(b - a, int(0.06 * SR))
                pitch[a:a + k] = m - 2 * (1 - np.linspace(0, 1, k)) ** 2
            g = d * 0.92 if d < SPB * 0.4 else d - 0.03
            e = adsr(n - a, 0.003, decay, sus, rel, g)
            amp[a:] = np.where(np.arange(a, n) < b, e[:n - a] * v, np.maximum(amp[a:], e[:n - a] * v))
            tn = tt[a:b] - tt[a]
            if d >= SPB * 0.7:
                vib[a:b] = np.clip((tn - 0.16) / 0.12, 0, 1) * vib_depth
            if dut is None:  # duty sequence per note: 12.5% blip then 25% (classic pluck trick)
                pass
        if dut is None:
            dut = np.full(n, 0.25)
            for (s, d, m, v, fl) in notes:
                a = int((s - t0) * SR)
                dut[a:a + int(0.035 * SR)] = 0.125
        f = mtof(pitch + vib * np.sin(2 * np.pi * 6.0 * tt) + chan_detune)
        amp = frame_env(amp)
        if kind == 'pulse':
            x = pulse(f, n, dut)
        else:
            x = triangle(f, n)
        return x * amp, t0

    def mel(bar0, spec, vel=0.9):
        out = []
        for i, bar in enumerate(spec):
            for nt in bar:
                b, d, m = nt[:3]
                fl = nt[3] if len(nt) > 3 else ''
                out.append((A.t(bar0 + i, b), d * SPB, m, vel * (1.0 if b % 1 == 0 else 0.88), fl))
        return out

    def phrases(notes, gap=0.35):
        if not notes:
            return []
        out = [[notes[0]]]
        for nt in notes[1:]:
            p = out[-1][-1]
            if nt[0] - (p[0] + p[1]) > gap:
                out.append([nt])
            else:
                out[-1].append(nt)
        return out

    def play(bus, bar0, spec, p=0.0, vel=0.9, **kw):
        for ph in phrases(mel(bar0, spec, vel)):
            y, t0 = chip_phrase(ph, **kw)
            A.add(bus, y, t0, p)

    # ---- lead / response channels
    play('coin', 0, [[(0, .09, B5)]], duty=0.5, sus=1.0, decay=1, rel=0.005)
    coin_hi = chip_phrase([(A.t(0, 0) + 0.085, 0.55, E6, 1.0, '')], duty=0.5, decay=0.3, sus=0.0, rel=0.3)[0]
    A.add('coin', coin_hi, A.t(0, 0) + 0.085)
    # rising run after the coin
    run = [[(2 + i * 0.25, 0.25, m) for i, m in enumerate([60, 64, 67, 72, 76, 79, 84, 88])]]
    play('arpl', 0, run, p=0.2, vel=0.7, duty=0.125, sus=0.5, decay=0.05)
    play('lead', 0, INTRO_MEL, p=-0.1, duty=None)

    def verse_block(bar0, second=False):
        d1 = 0.25 if not second else 0.125
        play('lead', bar0, CALL1, p=-0.28, duty=d1)
        play('resp', bar0 + 2, RESP1, p=0.4, duty=0.5, vel=0.8)
        play('lead', bar0 + 4, CALL2, p=-0.28, duty=d1)
        play('resp', bar0 + 6, RESP2, p=0.4, duty=0.5, vel=0.8)
        if second:  # harmony a third below on the calls
            play('harm', bar0, harmony_below(CALL1, 0), p=0.4, duty=0.5, vel=0.55)
            play('harm', bar0 + 4, harmony_below(CALL2, 0), p=0.4, duty=0.5, vel=0.55)

    verse_block(4)
    verse_block(12, second=True)

    def chorus_block(bar0, spec16, key=0, final=False):
        a = transpose(spec16[:8], key); b = transpose(spec16[8:], key) if len(spec16) > 8 else None
        play('lead', bar0, a, p=-0.1, duty=None, vib_depth=0.32)
        play('resp', bar0 + 7, [transpose([JINGLE], key)[0]], p=0.3, duty=0.125, vel=0.75)
        if b is not None:
            play('lead', bar0 + 8, b, p=-0.1, duty=None, vib_depth=0.32)
            play('resp', bar0 + 15, [transpose([JINGLE], key)[0]], p=0.3, duty=0.125, vel=0.75)
        if final:
            play('harm', bar0, harmony_below(a, 2), p=0.4, duty=0.5, vel=0.6)
            play('harm', bar0 + 8, harmony_below(b, 2), p=0.4, duty=0.5, vel=0.6)
            # octave-up sparkle doubling (quiet)
            play('sparkle', bar0, transpose(a, 12), p=0.0, duty=0.125, vel=0.35, vib_depth=0.2)
            play('sparkle', bar0 + 8, transpose(b, 12), p=0.0, duty=0.125, vel=0.35, vib_depth=0.2)

    chorus_block(20, CHO + CHO_B)
    # verse 2: calls on 12.5% duty, response doubled, then new melody
    play('lead', 36, CALL1, p=-0.28, duty=0.125)
    play('resp', 38, RESP1, p=0.32, duty=0.25, vel=0.8)
    play('lead', 40, CALL2, p=-0.28, duty=0.125)
    play('resp', 42, RESP2, p=0.32, duty=0.25, vel=0.8)
    play('lead', 44, VERSE2_MEL, p=-0.1, duty=None)
    play('harm', 44, harmony_below(VERSE2_MEL, 0), p=0.4, duty=0.5, vel=0.5)
    chorus_block(52, CHO)
    play('tri_lead', 60, BRIDGE_MEL, p=0.0, kind='tri', vel=1.0, vib_depth=0.3, sus=1.0, decay=1.0, rel=0.02)
    play('lead', 68, BUILD_MEL, p=-0.1, duty=None, vel=0.85)
    chorus_block(72, transpose(CHO + CHO_B, 0), key=2, final=True)
    play('lead', 88, ENDING_MEL, p=-0.05, duty=None)
    play('harm', 88, harmony_below(ENDING_MEL[:2], 2) + [[(0, .5, 81)], [], []], p=0.4, duty=0.5, vel=0.55)
    print(f"leads done {time.time()-T0:.1f}s")

    # ---- fast chord arpeggios (32nd-note cycling, pulse 12.5%/25%)
    def arp_bar(bar, chord, level=0.5, rate=8, duty=0.125, octave=12, beats=4.0, start=0.0):
        tones = [m + octave for m in CH[chord][0]]
        dur = beats * SPB
        n = int((dur + 0.02) * SR)
        step = SPB / rate
        idx = (np.arange(n) / SR / step).astype(int)
        pat = np.array([0, 1, 2, 1] if rate > 4 else [0, 1, 2])
        pitch = np.array(tones)[pat[idx % len(pat)]]
        f = mtof(pitch)
        x = pulse(f, n, duty)
        ph = (np.arange(n) / SR) % step
        env = 0.6 + 0.4 * np.exp(-ph / 0.02)
        env *= np.clip((dur - np.arange(n) / SR) / 0.01, 0, 1)
        A.add('arp', dsp.fade(x * env * level, 0.002, 0.01), A.t(bar, start), 0.0)

    for bar, c in enumerate(prog):
        sec = section_of(bar)
        if sec == 'Intro' and bar >= 1:
            arp_bar(bar, c, 0.35)
        elif sec in ('Verse', 'Verse 2'):
            arp_bar(bar, c, 0.3 if sec == 'Verse' else 0.34, duty=0.125 if bar % 2 == 0 else 0.25)
        elif sec in ('Chorus', 'Chorus 2', 'Final chorus'):
            arp_bar(bar, c, 0.42, rate=8, duty=0.25)
        elif sec == 'Bridge':
            arp_bar(bar, c, 0.45, rate=4, duty=0.125, octave=12)
        elif sec == 'Build':
            arp_bar(bar, c, 0.35 + 0.1 * (bar - 68), rate=8, duty=0.25, octave=12 + 12 * (bar >= 70))
        elif sec == 'Ending' and bar < 90:
            arp_bar(bar, c, 0.4, rate=8, duty=0.25)
    print(f"arps done {time.time()-T0:.1f}s")

    # ---- triangle bass
    def tri_note(m, dur, v=1.0, tau=None):
        n = int((dur + 0.01) * SR)
        f = mtof(m) * np.ones(n)
        x = triangle(f, n)
        env = np.ones(n) if tau is None else np.exp(-np.arange(n) / (tau * SR))
        env[-int(0.006 * SR):] *= np.linspace(1, 0, int(0.006 * SR))
        env[:int(0.001 * SR)] = np.linspace(0, 1, int(0.001 * SR))
        return x * env * v

    BASS_PATS = {
        'verse': [(0, .5, 0), (.5, .5, 12), (1, .5, 0), (1.5, .5, 12), (2, .5, 0), (2.5, .5, 12), (3, .5, 7), (3.5, .5, 12)],
        'chorus': [(0, .25, 0), (.5, .25, 0), (.75, .25, 12), (1, .5, 0), (1.5, .25, 12), (2, .25, 0), (2.5, .25, 0),
                   (2.75, .25, 12), (3, .5, 7), (3.5, .5, 10)],
        'bridge': [(0, 1.5, 0), (1.5, .5, 7), (2, 2, 12)],
        'build': [(i * .25, .2, 0) for i in range(16)],
        'intro': [(0, .5, 0), (1, .5, 0), (2, .5, 0), (2.5, .5, 12), (3, .5, 0), (3.5, .5, 12)],
    }
    for bar, c in enumerate(prog):
        sec = section_of(bar)
        root = CH[c][1]
        if root > 44:
            root -= 12
        pat = {'Intro': 'intro', 'Verse': 'verse', 'Verse 2': 'verse', 'Chorus': 'chorus', 'Chorus 2': 'chorus',
               'Final chorus': 'chorus', 'Bridge': 'bridge', 'Build': 'build', 'Ending': 'chorus'}[sec]
        if sec == 'Intro' and bar == 0:
            continue
        if sec == 'Ending' and bar >= 90:
            if bar == 90:
                A.add('tri', tri_note(root, 0.9 * SPB, 1.0), A.t(bar, 0))
                A.add('tri', tri_note(root, 3.0, 1.0, tau=0.45), A.t(bar, 3))
            continue
        for (b, d, iv) in BASS_PATS[pat]:
            if iv == 10 and c not in ('Am', 'Dm', 'Em', 'F#m', 'Bm', 'A7'):
                iv = 12  # b7 passing tone only over minor / dominant-7 chords
            A.add('tri', tri_note(root + iv, d * SPB * 0.92), A.t(bar, b))
    print(f"bass done {time.time()-T0:.1f}s")

    # ---- mid-register rhythm stabs (pulse 50%): fills the 250-500 Hz gap between triangle bass and leads
    STAB_PATS = {
        'verse': [0.5, 1.5, 2.5, 3.5],
        'chorus': [0.5, 1.5, 2.0, 2.75, 3.5],
        'bridge': [0.0, 2.0],
    }

    def stab(chord, t, dur, v):
        n = int((dur + 0.06) * SR)
        x = sum(pulse(mtof(m), n, 0.5) for m in CH[chord][0]) / 3
        e = frame_env(adsr(n, 0.002, 0.09, 0.35, 0.04, dur))
        return x * e * v

    for bar, c in enumerate(prog):
        sec = section_of(bar)
        key = {'Verse': 'verse', 'Verse 2': 'verse', 'Chorus': 'chorus', 'Chorus 2': 'chorus', 'Final chorus': 'chorus',
               'Bridge': 'bridge', 'Build': 'verse'}.get(sec)
        if key is None or (sec == 'Verse' and bar < 12):
            continue
        for b in STAB_PATS[key]:
            d = SPB * (0.3 if key != 'bridge' else 1.6)
            A.add('stab', stab(c, A.t(bar, b), d, 0.9 if b % 1 == 0.5 else 0.75), A.t(bar, b), 0.0)

    # ---- modern layer: soft PWM pad under the choruses (two detuned pulses, low-passed)
    def pwm_pad(chord, dur):
        n = int((dur + 0.5) * SR); t = np.arange(n) / SR
        L = np.zeros(n); R = np.zeros(n)
        for m in CH[chord][0]:
            f = mtof(m)
            L += pulse(f * 1.002, n, 0.5 + 0.35 * np.sin(2 * np.pi * 0.6 * t))
            R += pulse(f * 0.998, n, 0.5 + 0.35 * np.sin(2 * np.pi * 0.6 * t + 1.7))
        L = lp(L, 1800, 4); R = lp(R, 1800, 4)
        e = adsr(n, 0.12, 0.5, 0.8, 0.35, dur)
        return np.vstack([L, R]) * e / 3

    for bar, c in enumerate(prog):
        sec = section_of(bar)
        if sec in ('Chorus', 'Chorus 2', 'Final chorus', 'Bridge') or (sec == 'Ending' and bar < 90):
            A.add('pad', pwm_pad(c, A.bar_s), A.t(bar))
        if sec == 'Ending' and bar == 90:
            A.add('pad', pwm_pad('D', A.bar_s * 1.2) * 1.2, A.t(bar, 3))

    # ---- drums: noise channel + triangle-kick + subtle modern sub kick
    NZ_LONG = dsp.lfsr_noise(int(0.5 * SR), 55930, False, 1)   # bright long-mode noise
    NZ_MID = dsp.lfsr_noise(int(0.5 * SR), 13983, False, 7)
    NZ_MET = dsp.lfsr_noise(int(0.3 * SR), 27965, True, 3)     # short (metallic) mode

    def nes_env(n, decay, steps=True):
        e = np.exp(-np.arange(n) / (decay * SR))
        return frame_env(e) if steps else e

    def chip_kick():
        n = int(0.16 * SR); t = np.arange(n) / SR
        f = 55 + 260 * np.exp(-t / 0.018)
        k = triangle(f, n) * nes_env(n, 0.06, False)
        k += NZ_MID[:n] * np.exp(-t / 0.008) * 0.35
        return dsp.fade(k, 0.0005, 0.01)

    def sub_kick():
        n = int(0.4 * SR); t = np.arange(n) / SR
        f = 48 + 110 * np.exp(-t / 0.03)
        k = sine(f, n) * np.exp(-t / 0.14)
        k += hp(rng.standard_normal(n) * np.exp(-t / 0.002), 3000) * 0.2
        return dsp.fade(np.tanh(1.5 * k), 0.0005, 0.02)

    def chip_snare():
        n = int(0.3 * SR); t = np.arange(n) / SR
        s = NZ_MID[:n] * nes_env(n, 0.09) * 0.8 + NZ_LONG[:n] * nes_env(n, 0.05) * 0.5
        s += pulse(220 * (1 + 0.5 * np.exp(-t / 0.01)), n, 0.5) * np.exp(-t / 0.03) * 0.35
        return dsp.fade(s, 0.0005, 0.02)

    def chip_hat(open_=False):
        n = int((0.22 if open_ else 0.05) * SR)
        src = NZ_MET if not open_ else NZ_LONG
        return dsp.fade(src[:n] * nes_env(n, 0.07 if open_ else 0.012, False), 0.0003, 0.005)

    KICK, SUB, SNR, HAT, OHAT = chip_kick(), sub_kick(), chip_snare(), chip_hat(), chip_hat(True)
    CRASH_N = int(1.4 * SR)
    CRASH = dsp.lfsr_noise(CRASH_N, 44000, False, 11) * nes_env(CRASH_N, 0.45)

    def k(bar, b, v=1.0, sub=True):
        t = A.t(bar, b)
        A.add('kick', KICK * v, t)
        if sub:
            A.add('sub', SUB * v, t)
            kick_times.append(t)

    def s(bar, b, v=1.0):
        A.add('snare', SNR * v * (1 + rng.normal(0, 0.03)), A.t(bar, b), 0.05)

    def h(bar, b, v=1.0, open_=False):
        A.add('hats', (OHAT if open_ else HAT) * v * (1 + rng.normal(0, 0.05)), A.t(bar, b), 0.3)

    def crash(bar, b=0, v=1.0):
        A.add('crash', CRASH * v, A.t(bar, b), -0.3)

    def fill(bar, from_beat=2.0):
        steps = int((4 - from_beat) * 4)
        for i in range(steps):
            s(bar, from_beat + i / 4, 0.5 + 0.5 * i / steps)

    for bar in range(NB):
        sec = section_of(bar)
        if sec == 'Intro':
            if bar >= 1:
                k(bar, 0); k(bar, 2)
                for b8 in range(8):
                    h(bar, b8 / 2, 0.5 if b8 % 2 else 0.7)
            if bar == 3:
                fill(bar, 3.0)
        elif sec in ('Verse', 'Verse 2'):
            if bar in (4, 36):
                crash(bar)
            k(bar, 0); k(bar, 1.5, 0.8); k(bar, 2.5 if bar % 2 else 2, 0.9)
            s(bar, 1); s(bar, 3)
            for b8 in range(8):
                h(bar, b8 / 2, 0.7 if b8 % 2 == 0 else 0.45)
            if sec == 'Verse 2':
                h(bar, 3.75, 0.4)
            if (bar - 4) % 8 == 7:
                fill(bar, 3.0)
            if bar in (19, 51):
                fill(bar, 2.0)
        elif sec in ('Chorus', 'Chorus 2', 'Final chorus'):
            b0 = {'Chorus': 20, 'Chorus 2': 52, 'Final chorus': 72}[sec]
            if (bar - b0) % 8 == 0:
                crash(bar)
            for b in range(4):
                k(bar, b)
            s(bar, 1); s(bar, 3)
            for b16 in range(16):
                if b16 % 4 == 2:
                    h(bar, b16 / 4, 0.55, True)
                else:
                    h(bar, b16 / 4, 0.6 if b16 % 2 == 0 else 0.35)
            if (bar - b0) % 8 == 7:
                fill(bar, 3.0 if (bar - b0) % 16 != 15 else 2.0)
        elif sec == 'Bridge':
            k(bar, 0); k(bar, 2.5, 0.7)
            s(bar, 2, 0.8)
            for b8 in range(8):
                h(bar, b8 / 2, 0.4)
            if bar == 60:
                crash(bar, 0, 0.8)
        elif sec == 'Build':
            j = bar - 68
            for b in range(4):
                k(bar, b, 0.9)
            div = [2, 4, 4, 8][j]
            for i in range(4 * div):
                s(bar, i / div, 0.35 + 0.6 * (j * 4 + i / div) / 16)
        elif sec == 'Ending':
            if bar == 88:
                crash(bar)
            if bar < 90:
                for b in range(4):
                    k(bar, b)
                s(bar, 1); s(bar, 3)
                for b8 in range(8):
                    h(bar, b8 / 2, 0.6)
                if bar == 89:
                    fill(bar, 2.0)
            elif bar == 90:
                k(bar, 0); s(bar, 0); crash(bar, 0)
                k(bar, 3); s(bar, 3); crash(bar, 3, 1.0)
    print(f"drums done {time.time()-T0:.1f}s")
    os.makedirs(CACHE, exist_ok=True)
    for kk, v in A.buses.items():
        np.save(os.path.join(CACHE, kk + '.npy'), v)
    np.save(os.path.join(CACHE, 'kick_times.npy'), np.array(kick_times))

# ------------------------------------------------------------------ mix
B = A.buses
N = A.N
duck = dsp.duck_env(N, kick_times, -5.0, 0.003, 0.02, 0.09, 2.0)
duck_l = dsp.duck_env(N, kick_times, -2.0, 0.003, 0.02, 0.09, 2.0)


def proc(name, f):
    if name in B:
        B[name] = f(B[name].astype(np.float64)).astype(np.float32)


# chip channels: gentle console-like low-pass, high-pass everything above the bass, keep 2-4 kHz in check
chip_eq = lambda x, hpf: eq(lp(hp(x, hpf), 12000), [('peak', 1900, 1.0, -1.5), ('peak', 3100, 1.0, -3.5), ('highshelf', 6000, 0.7, -1.5)])
proc('lead', lambda x: chip_eq(x, 250))
proc('resp', lambda x: chip_eq(x, 300))
proc('harm', lambda x: chip_eq(x, 300))
proc('sparkle', lambda x: chip_eq(x, 800))
proc('coin', lambda x: chip_eq(x, 300))
proc('arpl', lambda x: chip_eq(x, 300))
proc('arp', lambda x: eq(chip_eq(x, 350), [('peak', 2200, 1.0, -2.0)]) * duck_l)
proc('tri_lead', lambda x: hp(x, 150))
proc('stab', lambda x: eq(chip_eq(x, 180), [('peak', 1200, 1.0, -2.0)]) * duck_l)
proc('pad', lambda x: eq(hp(x, 200), [('peak', 2500, 1.0, -3.0)]) * dsp.duck_env(N, kick_times, -6.0, 0.004, 0.02, 0.12, 2.0))
proc('tri', lambda x: eq(hp(x, 35, 2), [('lowshelf', 90, 0.7, 2.0)]) * duck)
proc('kick', lambda x: hp(x, 40))
proc('sub', lambda x: eq(x, [('peak', 55, 1.0, 1.5), ('peak', 300, 1.0, -3)]))
proc('snare', lambda x: eq(hp(x, 150), [('peak', 200, 1.0, 2.0), ('peak', 2500, 1.0, -2.0)]))
proc('hats', lambda x: hp(x, 4000))
proc('crash', lambda x: hp(x, 2500))

ref0, ref1 = A.t(20), A.t(36)
targets = {'lead': -8.0, 'resp': -13.0, 'stab': -15.0, 'pad': -18.0, 'arp': -15.0, 'tri': -9.0, 'kick': -13.0, 'sub': -9.0, 'snare': -11.0,
           'hats': -17.0, 'crash': -22.0}
g = auto_balance(A, targets, ref0, ref1)
g.update(auto_balance(A, {'harm': -14.0, 'sparkle': -19.0}, A.t(72), A.t(88)))
g.update(auto_balance(A, {'tri_lead': -8.0}, A.t(60), A.t(68)))
g.update(auto_balance(A, {'coin': -11.0}, 0, 0.8))
g.update(auto_balance(A, {'arpl': -14.0}, A.t(0, 2), A.t(1)))
for kk, v in g.items():
    print(f"  gain {kk:9s} {v:+.1f} dB")

# stereo image: leads slightly apart, arps wide (Haas), bass & kicks mono
def mono(x):
    return 0.5 * (x[0] + x[1]).astype(np.float64)


arp_w = dsp.haas(mono(B['arp']), 14, 0.7)
B['arp'] = arp_w.astype(np.float32)
B['stab'] = dsp.haas(mono(B['stab']), 9, 0.55).astype(np.float32)

# sends: short bright room + dotted-8th echo on leads (the "echo" of the brief)
room_ir = dsp.fdn_ir(rt60=1.1, size=0.7, damp_hz=7000, predelay_ms=8, seed=21)
room_send = hp(B['lead'] * 0.25 + B['resp'] * 0.3 + B['harm'] * 0.3 + B['arp'] * 0.3 + B['tri_lead'] * 0.4 +
               B['snare'] * 0.25 + B['sparkle'] * 0.4 + B['coin'] * 0.3, 300).astype(np.float64)
room = dsp.convolve_st(room_send, room_ir) * duck_l[None, :]
del room_send
dot8 = SPB * 0.75
echo = dsp.tempo_delay(mono(B['lead']) + mono(B['coin']) * 0.8 + mono(B['tri_lead']) * 0.6, dot8, 0.38, 6, 5000, 500) * 0.3
echo += dsp.tempo_delay(mono(B['resp']) + mono(B['arpl']), SPB * 0.5, 0.3, 4, 4500, 500) * 0.22
echo = eq(echo, [('peak', 3000, 1.0, -3)]) * duck_l[None, :]

drums = sum(B[kk].astype(np.float64) for kk in ('kick', 'sub', 'snare', 'hats', 'crash'))
drums, grd = dsp.compressor(drums, -18, 2.5, 0.006, 0.1, knee=6)
print(f"  drum bus GR max {grd.max():.1f} dB")
music = sum(B[kk].astype(np.float64) for kk in ('lead', 'resp', 'harm', 'sparkle', 'coin', 'arpl', 'arp', 'tri_lead', 'tri',
                                                 'stab', 'pad'))
mix = drums + music + room * db(-8) + echo * db(-4)
del drums, music, room, echo
SEC_GAIN = {'Intro': -1.0, 'Verse': -1.0, 'Chorus': 0.0, 'Verse 2': -0.5, 'Chorus 2': 0.0, 'Bridge': -3.5,
            'Build': (-3.0, 0.0), 'Final chorus': 1.0, 'Ending': 1.0}
gcurve = np.full(mix.shape[1], 1.0)
for name, b0, nb in SECTIONS:
    a = int(A.t(b0) * SR); e = min(mix.shape[1], int(A.t(b0 + nb) * SR))
    v = SEC_GAIN[name]
    gcurve[a:e] = np.linspace(v[0], v[1], e - a) if isinstance(v, tuple) else v
gcurve = uniform_filter1d(gcurve, int(0.2 * SR))
mix *= db(gcurve)[None, :]
mix, grm = dsp.compressor(mix, -14, 1.8, 0.012, 0.2, knee=8)
print(f"  mix bus GR max {grm.max():.1f} dB")
env = np.abs(mix).max(0)
last = np.nonzero(env > db(-70) * env.max())[0][-1]
end = min(N, last + int(0.2 * SR))
mix = mix[:, :end]
fl = int(1.0 * SR)
mix[:, -fl:] *= np.linspace(1, 0, fl) ** 2
print(f"mastering... {time.time()-T0:.1f}s  length {end/SR:.1f}s")
y = dsp.master(mix, -14.0, -1.2, clip_drive_db=1.5)
out = sys.argv[1] if len(sys.argv) > 1 else "/home/claude/work/audio/music/Insert Coin.mp3"
os.makedirs(os.path.dirname(out), exist_ok=True)
dsp.finalize_mp3(y, out, -14.0)
print("sections:")
for name, b0, nb in SECTIONS:
    print(f"  {name:13s} {int(A.t(b0)//60)}:{A.t(b0)%60:05.2f}")
print(f"wrote {out} in {time.time()-T0:.1f}s")
