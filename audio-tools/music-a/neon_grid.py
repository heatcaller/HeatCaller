"""Neon Grid — synthwave / outrun theme (A minor, 108 BPM).

Run:  python3 neon_grid.py [out.mp3]
Everything is synthesised here from oscillators and noise; no samples.
"""
import sys, os, time
import numpy as np
from scipy import signal
from scipy.ndimage import uniform_filter1d as ndimage_uniform
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dsp
from dsp import SR, mtof, db, saw, pulse, sine, supersaw, adsr, tv_filter, hp, lp, eq, pan
from song import Arr, auto_balance

T0 = time.time()
rng = np.random.default_rng(2024)
BPM = 108
A = Arr(BPM, 70, tail_s=3.5)
SPB = A.spb
S16 = SPB / 4

# ------------------------------------------------------------------ harmony
# pad voicings (rootless / voice-led), bass root
CH = {
    'Am9': ((64, 67, 71, 72), 45), 'Fmaj7': ((64, 65, 69, 72), 41), 'Cadd9': ((64, 67, 72, 74), 48),
    'G': ((62, 67, 71, 74), 43), 'Am': ((64, 69, 72, 76), 45), 'F': ((65, 69, 72, 77), 41),
    'C': ((64, 67, 72, 76), 48), 'Esus': ((64, 69, 71, 76), 40), 'E': ((64, 68, 71, 76), 40),
    'Dm7': ((62, 65, 69, 72), 38), 'Fmaj7b': ((64, 65, 69, 72), 41), 'Am7': ((64, 67, 69, 72), 45),
    'Dm': ((62, 65, 69, 74), 38), 'Fc': ((60, 65, 69, 72), 41), 'E7sus': ((62, 64, 69, 71), 40),
    'E7': ((62, 64, 68, 71), 40), 'Am9end': ((60, 64, 67, 71), 45),
}
P1 = ['Am9', 'Fmaj7', 'Cadd9', 'G']
P2 = ['Am', 'F', 'C', 'G', 'Am', 'F', 'G', ('Esus', 'E')]
P3 = ['Dm7', 'Fmaj7b', 'Am7', 'G', 'Dm7', 'Fc', 'E7sus', 'E7']
BUILD = ['Dm', 'Fc', 'G', ('Esus', 'E')]

SECTIONS = [  # (name, first bar, n bars)
    ('Intro', 0, 8), ('Verse', 8, 8), ('Build', 16, 4), ('Hook 1', 20, 16), ('Breakdown', 36, 8),
    ('Build 2', 44, 4), ('Final hook', 48, 16), ('Outro', 64, 4), ('Ending', 68, 2)]
prog = []  # per bar: list of (chord, beat_start, beats)
def put(chs):
    for c in chs:
        if isinstance(c, tuple):
            prog.append([(c[0], 0, 2), (c[1], 2, 2)])
        else:
            prog.append([(c, 0, 4)])
put(P1 * 4)            # 0-15 intro + verse
put(BUILD)             # 16-19
put(P2 * 2)            # 20-35
put(P3)                # 36-43
put(BUILD)             # 44-47
put(P2 * 2)            # 48-63
put(['Am', 'F', 'G', ('Esus', 'E')])  # 64-67 outro
put(['Am9end', 'Am9end'])  # 68-69 ending
assert len(prog) == 70


def section_of(bar):
    for name, b0, n in SECTIONS:
        if b0 <= bar < b0 + n:
            return name


CACHE = os.environ.get("MUSIC_A_CACHE", "/tmp/music-a-cache") + "/ng"  # rendered stems, reused by MIX_ONLY=1
MIX_ONLY = os.environ.get("MIX_ONLY") == "1"
if MIX_ONLY:
    for f in os.listdir(CACHE):
        if f.endswith('.npy') and f != 'kick_times.npy':
            A.buses[f[:-4]] = np.load(os.path.join(CACHE, f))
    kick_times = list(np.load(os.path.join(CACHE, 'kick_times.npy')))
    print("loaded cached stems")
else:
    # ------------------------------------------------------------------ drum one-shots
    def mk_kick(v=1.0, n_s=0.55):
        n = int(n_s * SR); t = np.arange(n) / SR
        f = 50 + 150 * np.exp(-t / 0.028) + 60 * np.exp(-t / 0.005)
        body = sine(f, n) * (np.exp(-t / 0.16) * 0.8 + 0.2 * np.exp(-t / 0.04))
        knock = sine(190 * (1 + 0.3 * np.exp(-t / 0.01)), n) * np.exp(-t / 0.022) * 0.25
        click = hp(rng.standard_normal(n) * np.exp(-t / 0.002), 2200) * 0.5
        body = body + knock
        k = np.tanh(1.6 * (body + click)) / np.tanh(1.6)
        return dsp.fade(k * v, 0.0005, 0.03)


    def mk_snare(v=1.0):
        n = int(0.45 * SR); t = np.arange(n) / SR
        # body tuned to the key (A3 + E4) so rolls and the gated tail don't clash with the harmony
        tone = (sine(220 * (1 + 0.12 * np.exp(-t / 0.010)), n) * np.exp(-t / 0.065) * 0.8 +
                sine(330 * (1 + 0.08 * np.exp(-t / 0.008)), n) * np.exp(-t / 0.040) * 0.45)
        nz = rng.standard_normal(n)
        nz = dsp.filt(nz, 'bp', 2400, 0.6) * 1.2 + hp(nz, 5000) * 0.35
        nz *= np.exp(-t / 0.13) * (1 - np.exp(-t / 0.0008))
        s = np.tanh(1.4 * (tone + nz * 0.9))
        return dsp.fade(s * v, 0.0003, 0.05)


    def mk_clap(v=1.0):
        n = int(0.35 * SR); t = np.arange(n) / SR
        env = np.zeros(n)
        for i, off in enumerate((0.0, 0.009, 0.019, 0.031)):
            s = int(off * SR)
            tau = 0.006 if i < 3 else 0.09
            env[s:] += np.exp(-(t[s:] - off) / tau) * (0.8 if i < 3 else 1.0)
        nz = dsp.filt(rng.standard_normal(n), 'bp', 1250, 1.1) + 0.3 * dsp.filt(rng.standard_normal(n), 'bp', 2600, 1.5)
        return dsp.fade(nz * env * v, 0.0002, 0.03)


    METAL = np.array([205.3, 304.4, 369.6, 522.7, 540.0, 800.0]) * 1.55


    def mk_hat(open_=False, v=1.0):
        n = int((0.5 if open_ else 0.12) * SR); t = np.arange(n) / SR
        m = sum(pulse(f * (1 + 0.004 * rng.standard_normal()), n, 0.5) for f in METAL)
        m = dsp.filt(m, 'bp', 9500, 0.9)
        m = hp(m, 7000, 4) + 0.25 * hp(rng.standard_normal(n), 8000, 2)
        tau = 0.16 if open_ else 0.028
        env = np.exp(-t / tau) * (1 - np.exp(-t / 0.0004))
        return dsp.fade(m * env * v * 0.5, 0.0002, 0.02)


    def mk_tom(f0, v=1.0):
        n = int(0.6 * SR); t = np.arange(n) / SR
        f = f0 * (1 + 0.7 * np.exp(-t / 0.05))
        s = sine(f, n) * np.exp(-t / 0.25) + 0.25 * dsp.filt(rng.standard_normal(n), 'bp', f0 * 4, 1.0) * np.exp(-t / 0.05)
        return dsp.fade(np.tanh(1.5 * s) * v, 0.0003, 0.05)


    def mk_crash(n_s=3.0, v=1.0):
        n = int(n_s * SR); t = np.arange(n) / SR
        fr = np.exp(rng.uniform(np.log(420), np.log(9000), 28))
        m = sum(pulse(f, n, 0.5) * rng.uniform(0.4, 1.0) for f in fr) / 10
        nz = rng.standard_normal(n)
        s = hp(m * 0.6 + nz * 0.8, 3500, 2)
        s = dsp.filt(s, 'highshelf', 9000, 0.7, 3.0)
        env = np.exp(-t / 1.1) * 0.8 + 0.4 * np.exp(-t / 0.08)
        return dsp.fade(s * env * v * 0.35, 0.0005, 0.3)


    def mk_riser(dur, v=1.0):
        n = int(dur * SR); x = np.linspace(0, 1, n)
        fc = 250 * (9000 / 250) ** (x ** 1.4)
        L = tv_filter(rng.standard_normal(n), fc, 2.2, 'bp'); R = tv_filter(rng.standard_normal(n), fc, 2.2, 'bp')
        env = x ** 2.2 * v * 0.5
        return np.vstack([L * env, R * env])


    def mk_subdrop(v=1.0):
        n = int(1.6 * SR); t = np.arange(n) / SR
        f = 34 + 70 * np.exp(-t / 0.35)
        return dsp.fade(sine(f, n) * np.exp(-t / 0.55) * v, 0.001, 0.2)


    KICKS = [mk_kick(1.0) for _ in range(3)]
    SNARES = [mk_snare() for _ in range(4)]
    CLAPS = [mk_clap() for _ in range(3)]
    CHAT = [mk_hat(False) for _ in range(6)]
    OHAT = [mk_hat(True) for _ in range(3)]
    TOMS = {'hi': mk_tom(150), 'mid': mk_tom(112), 'lo': mk_tom(82)}
    CRASH = mk_crash()
    kick_times = []


    def kick(bar, beat, v=1.0):
        t = A.t(bar, beat)
        A.add('kick', KICKS[rng.integers(3)] * v, t)
        kick_times.append(t)


    def snare(bar, beat, v=1.0, clap=False, p=0.0):
        t = A.t(bar, beat) + rng.normal(0, 0.0015)
        v *= 1 + rng.normal(0, 0.04)
        A.add('snare', SNARES[rng.integers(4)] * v, t, p)
        if clap:
            A.add('clap', CLAPS[rng.integers(3)] * v, t + 0.004, 0.0)


    def hat(bar, beat, v=1.0, open_=False, p=0.25):
        t = A.t(bar, beat) + rng.normal(0, 0.002)
        v *= 1 + rng.normal(0, 0.08)
        src = OHAT[rng.integers(3)] if open_ else CHAT[rng.integers(6)]
        A.add('hats', src * v, t, p)


    def tom(bar, beat, which, v=1.0):
        pans = {'hi': 0.35, 'mid': 0.0, 'lo': -0.35}
        A.add('toms', TOMS[which] * v * (1 + rng.normal(0, 0.05)), A.t(bar, beat), pans[which])


    def crash(bar, beat=0, v=1.0, p=-0.3):
        A.add('cym', CRASH * v, A.t(bar, beat), p)
        if v > 0.5:
            A.add('fx', mk_subdrop(0.8 * v), A.t(bar, beat))


    def rev_cymbal(bar_end, bars=1.0):
        n = int(bars * A.bar_s * SR)
        c = mk_crash(bars * A.bar_s + 0.2)[:n][::-1].copy()
        c *= np.linspace(0, 1, n) ** 1.5
        A.add('cym', c * 0.8, A.t(bar_end) - n / SR, 0.3)


    def riser(bar0, bars, v=1.0):
        A.add('fx', mk_riser(bars * A.bar_s, v), A.t(bar0))


    # ------------------------------------------------------------------ drum programming
    def hats16(bar, level=1.0, open_off=False):
        acc = [1.0, 0.45, 0.7, 0.45]
        for s in range(16):
            b = s / 4
            if open_off and s % 4 == 2:
                hat(bar, b, 0.55 * level, True)
            else:
                hat(bar, b, acc[s % 4] * 0.55 * level)


    def fill_snare16(bar, from_beat=3.0, v0=0.5, v1=1.0):
        steps = int((4 - from_beat) * 4)
        for i in range(steps):
            snare(bar, from_beat + i / 4, v0 + (v1 - v0) * i / max(1, steps - 1), p=0.1 * ((i % 2) * 2 - 1))


    def fill_toms(bar, from_beat=2.0):
        seq = ['hi', 'hi', 'mid', 'mid', 'lo', 'lo', 'lo', 'lo'][-int((4 - from_beat) * 4):]
        seq = (['hi'] * 16 + seq)[-int((4 - from_beat) * 4):]
        for i, w in enumerate(seq):
            b = from_beat + i / 4
            tom(bar, b, w, 0.75 + 0.25 * i / len(seq))


    for bar in range(70):
        sec = section_of(bar)
        if sec == 'Intro':
            if bar >= 4:
                for b in range(4):
                    kick(bar, b, 0.8)
                for b in range(4):
                    hat(bar, b + 0.5, 0.35)
            if bar == 7:
                fill_snare16(bar, 2.0, 0.25, 0.9)
                rev_cymbal(8)
        elif sec == 'Verse':
            if bar == 8:
                crash(8)
            for b in range(4):
                kick(bar, b)
            snare(bar, 1); snare(bar, 3)
            hats16(bar, 0.85)
            if bar == 11:
                snare(bar, 3.75, 0.4)
            if bar == 15:
                fill_toms(bar, 2.0)
        elif sec in ('Build', 'Build 2'):
            k = bar - (16 if sec == 'Build' else 44)
            if k < 3:
                for b in range(4):
                    kick(bar, b)
                hats16(bar, 0.6 + 0.15 * k)
            div = [1, 2, 4, 8][k]
            last = 4.0 if not (sec == 'Build 2' and k == 3) else 3.5
            steps = int(last * div)
            for i in range(steps):
                prog_ = (k * 4 + i / div) / 16
                snare(bar, i / div, 0.35 + 0.6 * prog_, p=0.15 * np.sin(i))
            if k == 0:
                riser(bar, 4, 0.9)
            if k == 3:
                rev_cymbal(bar + 1)
        elif sec in ('Hook 1', 'Final hook'):
            k = bar - (20 if sec == 'Hook 1' else 48)
            if k in (0, 8):
                crash(bar, 0, 1.0 if k == 0 else 0.7)
            for b in range(4):
                kick(bar, b)
            snare(bar, 1, clap=True); snare(bar, 3, clap=True)
            hats16(bar, 1.0, open_off=True)
            if sec == 'Final hook':
                for s in range(16):  # shaker layer
                    A.add('hats', hp(rng.standard_normal(int(0.06 * SR)), 6000) * dsp.expdecay(int(0.06 * SR), 0.018) *
                          (0.14 if s % 2 else 0.22), A.t(bar, s / 4) + 0.006, -0.45)
            if k == 7:
                fill_snare16(bar, 3.0, 0.4, 0.9)
            if k == 15:
                fill_toms(bar, 2.0 if sec == 'Hook 1' else 1.0)
                if sec == 'Hook 1':
                    rev_cymbal(bar + 1, 0.5)
        elif sec == 'Breakdown':
            k = bar - 36
            if k == 0:
                crash(bar, 0, 0.9)
            if k >= 4:
                kick(bar, 0, 0.75); kick(bar, 2.5, 0.55)
                for b in range(4):
                    hat(bar, b + 0.5, 0.3)
                if k == 7:
                    snare(bar, 3.5, 0.5); snare(bar, 3.75, 0.7)
        elif sec == 'Outro':
            k = bar - 64
            if k == 0:
                crash(bar, 0, 0.9)
            for b in range(4):
                kick(bar, b, 0.95)
            snare(bar, 1, clap=True); snare(bar, 3, clap=True)
            hats16(bar, 0.8)
            if k == 3:
                fill_toms(bar, 1.0)
        elif sec == 'Ending' and bar == 68:
            kick(bar, 0, 1.0); snare(bar, 0, 1.0, clap=True); crash(bar, 0, 1.0); crash(bar, 0.02, 0.6, 0.3)
            tom(bar, 0, 'lo', 0.8)
    print(f"drums done {time.time()-T0:.1f}s")


    # ------------------------------------------------------------------ pads
    def pad_cutoff(bar):
        sec = section_of(bar)
        if sec == 'Intro':
            return 350 * (2600 / 350) ** (bar / 8)
        return {'Verse': 2000, 'Build': 2200 + 500 * (bar - 16), 'Hook 1': 3200, 'Breakdown': 1500,
                'Build 2': 1800 + 700 * (bar - 44), 'Final hook': 3800, 'Outro': 3000, 'Ending': 2600}[sec]


    def render_pad(notes, dur, cutoff, att=0.35, rel=1.1, level=1.0, detune=0.16):
        n = int((dur + rel * 1.5) * SR)
        L = np.zeros(n); R = np.zeros(n)
        for m in notes:
            l, r = supersaw(mtof(m) * (1 + 0.0008 * rng.standard_normal()), n, voices=7, detune=detune, mix=0.7)
            off = int(rng.uniform(0, 0.012) * SR)
            L[off:] += l[:n - off]; R[off:] += r[:n - off]
        t = np.arange(n) / SR
        fenv = cutoff * (0.55 + 0.45 * (1 - np.exp(-t / 0.6))) * (1 + 0.06 * np.sin(2 * np.pi * 0.2 * t))
        L = tv_filter(L, fenv, 1.6, stages=2); R = tv_filter(R, fenv, 1.6, stages=2)
        env = adsr(n, att, 1.2, 0.85, rel, dur)
        return np.vstack([L, R]) * env * level / len(notes)


    for bar, chords in enumerate(prog):
        for (c, b0, nb) in chords:
            notes, _ = CH[c]
            dur = nb * SPB
            if bar >= 68:  # final chord: one long render
                if bar == 68 and b0 == 0:
                    A.add('pad', render_pad(notes + (57,), 2 * A.bar_s, 2600, att=0.01, rel=2.5, level=1.1), A.t(bar))
                continue
            sec = section_of(bar)
            att = 0.6 if sec in ('Intro', 'Breakdown') else 0.25
            lvl = 1.7 if bar < 4 else 1.0
            A.add('pad', render_pad(notes, dur, pad_cutoff(bar), att=att, level=lvl), A.t(bar, b0))
            if sec == 'Final hook' or (sec == 'Breakdown'):
                # high string layer: top two voices an octave up, slow & soft
                top = tuple(m + 12 for m in notes[2:])
                A.add('strings', render_pad(top, dur, 2600, att=0.5, rel=1.4, level=0.9, detune=0.1), A.t(bar, b0))
    print(f"pads done {time.time()-T0:.1f}s")


    # ------------------------------------------------------------------ bass
    def bass_note(m, dur, v=1.0, bright=1.0, sus=0.7, dec=0.25):
        n = int((dur + 0.08) * SR); t = np.arange(n) / SR
        f = mtof(m)
        x = saw(f, n) * 0.6 + pulse(f * 1.003, n, 0.5) * 0.4 + sine(f / 2, n) * 0.0
        fc = (180 + 1300 * bright * v * np.exp(-t / 0.09)) + 2 * f
        y = tv_filter(x, fc, 2.2, stages=2, block=32)
        y += sine(f, n) * 0.55  # clean fundamental for weight
        env = adsr(n, 0.002, dec, sus, 0.05, dur)
        return np.tanh(1.3 * y * env) * v


    for bar, chords in enumerate(prog):
        sec = section_of(bar)
        for (c, b0, nb) in chords:
            root = CH[c][1]
            if root > 44:
                root -= 12  # keep roots in A1..G2 range (33..44)
            steps = int(nb * 4)
            for s in range(steps):
                beat = b0 + s / 4
                pos = s % 4
                if sec == 'Intro':
                    if bar < 4 or pos % 2:
                        continue
                    m, v, d, br = root, 0.8, S16 * 1.8, 0.35 + 0.1 * (bar - 4)
                elif sec in ('Verse', 'Outro'):
                    if pos % 2:
                        continue
                    m = root + (12 if (s // 2) % 2 else 0); v = 0.95; d = S16 * 1.7; br = 0.7
                elif sec in ('Build', 'Build 2'):
                    m, v, d, br = root, 0.85 + 0.1 * (pos == 0), S16 * 0.85, 0.5 + 0.1 * (bar % 4)
                elif sec in ('Hook 1', 'Final hook'):
                    m = root + (12 if pos == 2 else 0); v = 1.0 if pos == 0 else 0.85; d = S16 * 0.85; br = 0.9
                elif sec == 'Breakdown':
                    if bar < 40:
                        if s == 0:
                            m, v, d, br = root, 0.8, nb * SPB * 0.97, 0.15
                        else:
                            continue
                    else:
                        if pos % 2:
                            continue
                        m, v, d, br = root, 0.6, S16 * 1.7, 0.3
                elif sec == 'Ending':
                    if bar == 68 and s == 0:
                        m, v, d, br = root, 1.0, 2 * A.bar_s, 0.5
                    else:
                        continue
                else:
                    continue
                if sec == 'Ending':
                    A.add('bass', bass_note(m, d * 1.6, v, br, sus=0.0, dec=4.0), A.t(bar, beat))
                    continue
                A.add('bass', bass_note(m, d, v, br), A.t(bar, beat))
    print(f"bass done {time.time()-T0:.1f}s")


    # ------------------------------------------------------------------ arp (pluck)
    def pluck(m, v=1.0, cutoff=5000):
        n = int(0.45 * SR); t = np.arange(n) / SR
        f = mtof(m)
        x = saw(f, n) * 0.7 + pulse(f * 1.004, n, 0.3) * 0.4
        fc = 250 + cutoff * v * np.exp(-t / 0.07)
        y = tv_filter(x, fc, 3.0, stages=2, block=32)
        return dsp.fade(y * np.exp(-t / 0.16) * v, 0.001, 0.05)


    arp_cut = {'Intro': None, 'Verse': 3500, 'Build': 4000, 'Hook 1': 4500, 'Breakdown': 2200, 'Build 2': 3800,
               'Final hook': 5000, 'Outro': 4000}
    ARP_PAT = [0, 1, 2, 3, 1, 2, 3, 4]
    for bar, chords in enumerate(prog):
        sec = section_of(bar)
        if sec == 'Ending' or (sec == 'Intro' and bar < 2):
            continue
        for (c, b0, nb) in chords:
            notes = list(CH[c][0])
            tones = notes + [notes[0] + 12]
            for s in range(int(nb * 4)):
                idx = ARP_PAT[(int(b0 * 4) + s) % 8]
                m = tones[idx]
                cut = arp_cut[sec] if sec != 'Intro' else 500 * (3000 / 500) ** ((bar - 2) / 6)
                v = (1.0 if s % 2 == 0 else 0.72) * (1 + rng.normal(0, 0.06))
                t = A.t(bar, b0 + s / 4) + rng.normal(0, 0.002)
                A.add('arp', pluck(m, v, cut), t, 0.25 if s % 2 else -0.25)
        if bar == 67:  # final upward run into the last chord
            for s in range(16):
                m = [57, 60, 64, 69, 64, 69, 72, 76, 69, 72, 76, 81, 76, 81, 84, 88][s]
                A.add('arp', pluck(m, 0.6 + 0.4 * s / 15, 5000),
                      A.t(bar, s / 4), 0.3 * np.sin(s))
    print(f"arp done {time.time()-T0:.1f}s")


    # ------------------------------------------------------------------ lead
    def render_lead(notes, kind='lead', throw_last=True):
        """notes: list of (t_start, dur, midi, vel). Legato phrase with glide + delayed vibrato."""
        t0 = notes[0][0]
        t_end = max(s + d for s, d, _, _ in notes) + 0.45
        n = int((t_end - t0) * SR)
        tt = np.arange(n) / SR
        pitch = np.full(n, float(notes[0][2]))
        amp = np.zeros(n)
        vib = np.zeros(n)
        bright = np.zeros(n)
        for i, (s, d, m, v) in enumerate(notes):
            a = int((s - t0) * SR); b = min(n, int((s - t0 + d) * SR))
            pitch[a:] = m
            legato = i > 0 and s - (notes[i - 1][0] + notes[i - 1][1]) < 0.02
            e = adsr(n - a, 0.012 if not legato else 0.004, 0.35, 0.78, 0.16, d * 0.96)
            amp[a:] = np.maximum(amp[a:], e * v)
            # delayed vibrato on notes longer than a beat
            tn = tt[a:] - tt[a]
            vib[a:b] = np.clip((tn[:b - a] - 0.22) / 0.3, 0, 1) * (0.22 if d > SPB * 0.9 else 0.08)
            bright[a:] = np.maximum(bright[a:] * 0.0 + (bright[a:] if not legato else 0), v * np.exp(-tn / 0.25))
        # glide: smooth the pitch staircase (portamento ~22 ms)
        pg = np.exp(-1 / (0.022 * SR))
        pitch = signal.lfilter([1 - pg], [1, -pg], pitch - pitch[0]) + pitch[0]
        vib_sig = vib * np.sin(2 * np.pi * 5.6 * tt + 0.3 * np.sin(2 * np.pi * 0.7 * tt))
        f = mtof(pitch + vib_sig + 0.03 * np.sin(2 * np.pi * 0.31 * tt))
        if kind == 'lead':
            x = saw(f * db(0) * 2 ** (6 / 1200), n) * 0.5 + saw(f * 2 ** (-6 / 1200), n) * 0.5 + pulse(f * 0.5, n, 0.45) * 0.3
            fc = 2200 + 3200 * bright + 0.8 * f
            y = tv_filter(x, fc, 1.1, stages=2, block=64)
        elif kind == 'double':  # square an octave down, softer
            x = pulse(f * 0.5, n, 0.25 + 0.1 * np.sin(2 * np.pi * 0.25 * tt))
            y = tv_filter(x, 1800 + 1200 * bright, 0.9, stages=2)
        else:  # soft: breakdown flute-ish lead
            x = pulse(f, n, 0.5) * 0.6 + sine(f, n) * 0.6
            y = tv_filter(x, 900 + 900 * bright + 0.5 * f, 0.8, stages=2)
        return np.tanh(1.5 * y * amp) * 0.8, t0


    def mel(bar0, spec):
        """spec: list of bars, each list of (beat, dur, midi). returns absolute note list"""
        out = []
        for i, bar_notes in enumerate(spec):
            for (b, d, m) in bar_notes:
                out.append((A.t(bar0 + i, b), d * SPB, m, 0.9 + 0.1 * (b == 0)))
        return out


    E5, D5, C5, B4, A4, G4, F5, G5, A5, B5, C6, D6, GS4, GS5, F4, E4 = 76, 74, 72, 71, 69, 67, 77, 79, 81, 83, 84, 86, 68, 80, 65, 64
    HOOK_A = [
        [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, .5, D5), (3.5, .5, E5)],
        [(0, 1.5, F5), (1.5, .5, E5), (2, 2, C5)],
        [(0, 1.5, G5), (1.5, .5, E5), (2, 1, C5), (3, 1, E5)],
        [(0, 3, D5), (3, .5, B4), (3.5, .5, D5)],
        [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, 1, A5)],
        [(0, 1.5, A5), (1.5, .5, G5), (2, 1, F5), (3, 1, E5)],
        [(0, 1, D5), (1, .5, E5), (1.5, .5, D5), (2, 1, B4), (3, 1, G4)],
        [(0, 1, A4), (1, 1, GS4), (2, 2, B4)],
    ]
    HOOK_B = [
        [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, .5, D5), (3.5, .5, E5)],
        [(0, 1.5, F5), (1.5, .5, E5), (2, 2, C5)],
        [(0, 1.5, G5), (1.5, .5, A5), (2, 1, G5), (3, 1, E5)],
        [(0, 2, D5), (2, .5, E5), (2.5, .5, D5), (3, 1, B4)],
        [(0, 1.5, E5), (1.5, .5, D5), (2, 1, C5), (3, 1, A5)],
        [(0, 1.5, C6), (1.5, .5, B5), (2, 1, A5), (3, .5, G5), (3.5, .5, F5)],
        [(0, 1, G5), (1, .5, F5), (1.5, .5, E5), (2, 1, D5), (3, 1, B4)],
        [(0, 3.5, E5)],
    ]
    HOOK_B_FINAL = HOOK_B[:4] + [
        [(0, 1.5, A5), (1.5, .5, G5), (2, 1, E5), (3, 1, A5)],
        [(0, 1.5, C6), (1.5, .5, B5), (2, 1, A5), (3, .5, G5), (3.5, .5, F5)],
        [(0, 1, G5), (1, .5, A5), (1.5, .5, B5), (2, 1, D6), (3, 1, B5)],
        [(0, 2, B5), (2, 1.75, GS5)],
    ]
    BREAK_MEL = [
        [(0, 2, A4), (2, 1, C5), (3, 1, D5)], [(0, 3, E5), (3, .5, D5), (3.5, .5, C5)],
        [(0, 2, C5), (2, 1, E5), (3, 1, G5)], [(0, 3.5, D5)],
        [(0, 2, A4), (2, 1, C5), (3, 1, D5)], [(0, 2, F5), (2, 1, E5), (3, 1, C5)],
        [(0, 2, B4), (2, 1, D5), (3, 1, E5)], [(0, 2, GS4), (2, 1.5, B4)],
    ]
    OUTRO_MEL = [[(0, 3.5, A5)], [], [(0, 1.5, D5), (1.5, .5, E5), (2, 1, D5), (3, 1, B4)], [(0, 2, GS4), (2, 2, B4)]]
    BELL_MEL = [
        [(0, .75, E5), (.75, .75, D5), (1.5, 1, C5), (3, 1, B4)], [(0, 1.5, C5), (1.5, 2.5, A4)],
        [(0, .75, G5), (.75, .75, E5), (1.5, 1, D5), (3, 1, C5)], [(0, 3, D5)],
        [(0, .75, E5), (.75, .75, D5), (1.5, 1, C5), (3, 1, B4)], [(0, 1.5, C5), (1.5, 2.5, A4)],
        [(0, .75, G5), (.75, .75, A5), (1.5, 1, G5), (3, 1, E5)], [(0, 1.5, D5), (1.5, .5, B4), (2, 2, D5)],
    ]


    def phrase_split(notes, gap=0.3):
        out = [[notes[0]]]
        for nt in notes[1:]:
            p = out[-1][-1]
            if nt[0] - (p[0] + p[1]) > gap:
                out.append([nt])
            else:
                out[-1].append(nt)
        return out


    def play_lead(bar0, spec, bus='lead', kind='lead', throws=True):
        notes = mel(bar0, spec)
        for ph in phrase_split(notes):
            y, t0 = render_lead(ph, kind)
            A.add(bus, y, t0, 0.0)
            if throws:  # delay throw on the last (long) note of each phrase
                s, d, m, v = ph[-1]
                if d >= SPB * 0.9:
                    a = int((s - t0) * SR)
                    A.add(bus + '_throw', y[a:], s, 0.0)


    play_lead(20, HOOK_A)
    play_lead(28, HOOK_B)
    play_lead(36, BREAK_MEL, bus='softlead', kind='soft')
    play_lead(48, HOOK_A)
    play_lead(56, HOOK_B_FINAL)
    play_lead(48, HOOK_A, bus='double', kind='double', throws=False)
    play_lead(56, HOOK_B_FINAL, bus='double', kind='double', throws=False)
    play_lead(64, OUTRO_MEL)
    # final note of the whole piece
    A.add('lead', render_lead([(A.t(68), A.bar_s * 1.6, 69, 0.9)])[0], A.t(68))
    print(f"lead done {time.time()-T0:.1f}s")


    # ------------------------------------------------------------------ FM bell / e-piano (verse + breakdown colour)
    def bell(m, dur=1.5, v=1.0):
        n = int((dur + 1.2) * SR); t = np.arange(n) / SR
        f = mtof(m)
        I1 = 2.2 * np.exp(-t / 0.25) + 0.3
        I2 = 1.6 * np.exp(-t / 0.08)
        ph = 2 * np.pi * f * t
        y = np.sin(ph + I1 * np.sin(ph * 1.0 + 0.3)) * np.exp(-t / 0.9) * 0.7
        y += np.sin(ph * 1.0 + I2 * np.sin(ph * 3.5)) * np.exp(-t / 0.35) * 0.4
        y *= np.minimum(1, t / 0.002)
        return dsp.fade(y * v, 0.0005, 0.2)


    for bar0 in (8,):
        for nt in mel(bar0, BELL_MEL):
            A.add('bell', bell(nt[2], nt[1], 0.8), nt[0], 0.15)
    for nt in mel(40, [[(0, .75, E5), (.75, .75, D5), (1.5, 1, C5), (3, 1, B4)], [(0, 1.5, C5), (1.5, 2.5, A4)]]):
        A.add('bell', bell(nt[2] + 12, nt[1], 0.45), nt[0], -0.2)
    A.add('bell', bell(81, 3.0, 0.7), A.t(68), 0.1)
    print(f"bell done {time.time()-T0:.1f}s")

    os.makedirs(CACHE, exist_ok=True)
    for k, v in A.buses.items():
        np.save(os.path.join(CACHE, k + '.npy'), v)
    np.save(os.path.join(CACHE, 'kick_times.npy'), np.array(kick_times))

# ------------------------------------------------------------------ mix
B = A.buses
N = A.N
duck_fast = dsp.duck_env(N, kick_times, -9.0, 0.003, 0.02, 0.10, 2.2)
duck_pad = dsp.duck_env(N, kick_times, -6.0, 0.005, 0.03, 0.16, 2.0)
duck_light = dsp.duck_env(N, kick_times, -3.0, 0.005, 0.02, 0.14, 2.0)


def proc(name, f):
    if name in B:
        B[name] = f(B[name].astype(np.float64)).astype(np.float32)


# per-track EQ / filtering
proc('kick', lambda x: eq(x, [('peak', 60, 1.0, 2.0), ('peak', 320, 1.2, -3.0), ('peak', 3500, 1.0, 2.0)]))
proc('snare', lambda x: eq(hp(x, 120), [('peak', 220, 1.0, 2.0), ('peak', 900, 1.0, -2.0), ('highshelf', 7000, .7, 2.0)]))
proc('clap', lambda x: hp(x, 400))
proc('hats', lambda x: hp(x, 3000, 2))
proc('toms', lambda x: hp(x, 60))
proc('cym', lambda x: hp(x, 800))
proc('fx', lambda x: x)
proc('bass', lambda x: dsp.compressor(eq(hp(x, 30, 4), [('peak', 90, 1.0, 1.5), ('peak', 250, 1.0, -2.0)]),
                                        -20, 3, 0.003, 0.08)[0] * duck_fast)
pad = B['pad'].astype(np.float64)
pad_m = 0.5 * (pad[0] + pad[1])
pad_s = 0.5 * (pad[0] - pad[1])
ch = dsp.chorus(pad_m, voices=3, base_ms=9, depth_ms=2.5, rate=0.4, mix=0.55)
pad = np.vstack([ch[0] + pad_s * 1.2, ch[1] - pad_s * 1.2])
pad = eq(hp(pad, 160), [('peak', 400, 0.8, -2.0), ('peak', 2800, 0.9, -3.5), ('highshelf', 9000, .7, 1.5)])
B['pad'] = (pad * duck_pad).astype(np.float32)
proc('strings', lambda x: eq(hp(x, 400), [('peak', 2800, 0.9, -3.0)]) * duck_light)
proc('arp', lambda x: eq(hp(x, 280), [('peak', 2500, 1.0, -3.0), ('peak', 5000, 1, 1.0)]) * duck_light)
proc('lead', lambda x: dsp.compressor(eq(hp(x, 220), [('peak', 1200, 0.8, 1.0), ('peak', 3000, 1.2, -2.5), ('highshelf', 8000, .7, -1.0)]),
                                      -18, 2.5, 0.004, 0.12)[0])
proc('double', lambda x: eq(hp(x, 200), [('peak', 2500, 1.0, -3.0)]))
proc('softlead', lambda x: hp(x, 200))
proc('bell', lambda x: eq(hp(x, 300), [('peak', 3000, 1.0, -2.0)]))

# gain staging: loudness of each stem relative to the target mix loudness, measured in Hook 1
hook_t0, hook_t1 = A.t(20), A.t(36)
targets = {'kick': -5.0, 'snare': -10.0, 'clap': -18.0, 'hats': -17.0, 'bass': -8.5, 'pad': -11.0, 'arp': -15.0,
           'lead': -8.5, 'cym': -20.0}
g = auto_balance(A, targets, hook_t0, hook_t1)
# stems not present in Hook 1: set relative to other sections
g2 = auto_balance(A, {'double': -15.0, 'strings': -17.0}, A.t(48), A.t(64))
g3 = auto_balance(A, {'softlead': -11.0, 'bell': -13.5}, A.t(36), A.t(44))
g4 = auto_balance(A, {'toms': -9.0}, A.t(35, 2), A.t(36))
g5 = auto_balance(A, {'fx': -16.0}, A.t(16), A.t(20))
# bell level in verse (same gain applies)
for k, v in {**g, **g2, **g3, **g4, **g5}.items():
    print(f"  gain {k:9s} {v:+.1f} dB")
hs, he = int(hook_t0 * SR), int(hook_t1 * SR)
for k in sorted(B):
    seg = B[k][:, hs:he]
    print(f"  stem {k:15s} hook RMS {10*np.log10(np.mean(seg.astype(np.float64)**2)+1e-20):6.1f}  peak {20*np.log10(np.abs(seg).max()+1e-9):6.1f} dBFS")
# throws follow the lead gain
for nm, src in (('lead_throw', 'lead'), ('softlead_throw', 'softlead')):
    if nm in B:
        gg = {**g, **g3}.get(src, 0)
        B[nm] = hp(B[nm].astype(np.float64) * db(gg), 220).astype(np.float32)

# --- sends
print(f"reverbs... {time.time()-T0:.1f}s")
dot8 = SPB * 0.75
hall_ir = dsp.fdn_ir(rt60=2.8, size=1.25, damp_hz=5200, predelay_ms=25, seed=11)
gated_ir = dsp.fdn_ir(rt60=1.6, size=1.1, damp_hz=7000, predelay_ms=4, seed=5, seconds=0.5)
gated_ir *= dsp.gate_env(gated_ir.shape[1], 0.27, 0.025)[None, :]
gated_ir[:, :int(0.25 * SR)] *= np.linspace(0.6, 1.0, int(0.25 * SR))[None, :]  # slightly swelling body


def mono(x):
    return 0.5 * (x[0] + x[1]).astype(np.float64)


hall_send = (B['pad'] * 0.35 + B['strings'] * 0.6 + B['arp'] * 0.35 + B['lead'] * 0.28 + B['softlead'] * 0.7 +
             B['bell'] * 0.55 + B['double'] * 0.3 + B['snare'] * 0.12 + B['cym'] * 0.2).astype(np.float64)
hall_send = hp(lp(hall_send, 9000), 250)
hall = dsp.convolve_st(hall_send, hall_ir)
hall *= duck_light[None, :]
gated_send = hp(mono(B['snare']) * 1.0 + mono(B['clap']) * 0.6 + mono(B['toms']) * 0.8, 200)
gated = dsp.convolve_st(gated_send, gated_ir)
gated = eq(gated, [('peak', 2500, 1.0, -2.0), ('lowshelf', 300, .7, 1.5)])
del hall_send, gated_send
# tempo delays
d_lead = dsp.tempo_delay(mono(B['lead']), dot8, 0.35, 6, 4000, 400) * 0.25
d_throw = dsp.tempo_delay(mono(B['lead_throw']) + mono(B.get('softlead_throw', np.zeros((2, N)))), dot8, 0.55, 9, 3500, 400)
d_arp = dsp.tempo_delay(mono(B['arp']), dot8, 0.3, 4, 3500, 500) * 0.35
d_bell = dsp.tempo_delay(mono(B['bell']), SPB * 0.5, 0.4, 6, 3000, 400) * 0.4
delays = (d_lead + d_throw * 0.45 + d_arp + d_bell) * duck_light[None, :]
delays = eq(delays, [('peak', 2500, 1.0, -3.0)])
print(f"sends done {time.time()-T0:.1f}s")

# --- drum bus (kick, snare, clap, hats, toms) with glue compression
drums = sum(B[k].astype(np.float64) for k in ('kick', 'snare', 'clap', 'hats', 'toms'))
drums += gated * db(-7.0)
drums, grd = dsp.compressor(drums, -16, 2.5, 0.01, 0.12, knee=6)
print(f"  drum bus GR max {grd.max():.1f} dB")
music = sum(B[k].astype(np.float64) for k in ('bass', 'pad', 'strings', 'arp', 'lead', 'double', 'softlead', 'bell', 'cym', 'fx'))
music += hall * db(-6.0) + delays * db(-3.0)
mix = drums + music
# section fader automation (arrangement dynamics)
SEC_GAIN = {'Intro': 0.0, 'Verse': -1.5, 'Build': (-1.5, 0.0), 'Hook 1': 0.0, 'Breakdown': -3.0, 'Build 2': (-3.0, 0.0),
            'Final hook': 0.8, 'Outro': 0.3, 'Ending': 0.3}
gcurve = np.zeros(mix.shape[1])
for name, b0, nb in SECTIONS:
    a = int(A.t(b0) * SR); e = min(mix.shape[1], int(A.t(b0 + nb) * SR))
    v = SEC_GAIN[name]
    gcurve[a:e] = np.linspace(v[0], v[1], e - a) if isinstance(v, tuple) else v
gcurve[int(A.t(70) * SR):] = SEC_GAIN['Ending']
gcurve = ndimage_uniform(gcurve, int(0.25 * SR))
mix *= db(gcurve)[None, :]
del drums, music, hall, gated, delays
mix, grm = dsp.compressor(mix, -14, 1.8, 0.015, 0.25, knee=8)
print(f"  mix bus GR max {grm.max():.1f} dB")

# end: trim to last audible sample + small margin, fade the reverb tail smoothly to silence
env = np.abs(mix).max(0)
last = np.nonzero(env > db(-70) * env.max())[0][-1]
end = min(N, last + int(0.2 * SR))
mix = mix[:, :end]
fl = int(1.5 * SR)
mix[:, -fl:] *= np.linspace(1, 0, fl) ** 2
print(f"mastering... {time.time()-T0:.1f}s  length {end/SR:.1f}s")
mix = dsp.filt(mix, 'highshelf', 10000, 0.7, 1.5)  # air
mix = dsp.filt(mix, 'highshelf', 10000, 0.7, 1.5)  # air
y = dsp.master(mix, -14.0, -1.2, clip_drive_db=1.5)
out = sys.argv[1] if len(sys.argv) > 1 else "/home/claude/work/audio/music/Neon Grid.mp3"
os.makedirs(os.path.dirname(out), exist_ok=True)
dsp.finalize_mp3(y, out, -14.0)
print("sections:")
for name, b0, nb in SECTIONS:
    print(f"  {name:11s} {int(A.t(b0)//60)}:{A.t(b0)%60:05.2f}")
print(f"wrote {out} in {time.time()-T0:.1f}s")
