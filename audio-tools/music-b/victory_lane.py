"""Victory Lane - Podium soundscape theme (cinematic-hybrid anthem).

100 BPM, D major -> E major lift for the final anthem, 68 bars + ring-out (~2:48).
Run: nice python3 victory_lane.py [--quick]   (writes ../../music/Victory Lane.mp3)
"""
import sys, os, time, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mblib as M
from mblib import SR, RNG, Track, parse_line, hz, midi
import instruments as I
from theory import chord, expand, voice_lead, steps

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'music', 'Victory Lane.mp3')
SCRATCH = os.environ.get('MB_SCRATCH', '/tmp')
T0 = time.time()


def log(*a):
    print(f'[{time.time() - T0:6.1f}s]', *a, flush=True)


# ============================================================================ form
BPM = 100
INTRO, VERSE, ANTHEM, BREAK, FINAL, OUTRO, END = 0, 8, 24, 40, 48, 64, 68
NBARS = END
tm = M.TempoMap(BPM, NBARS * 4 + 16, changes=[(OUTRO * 4 + 8, OUTRO * 4 + 13, BPM, 74)])
RING = 7.0
N = int((float(tm.t(NBARS * 4 - 3)) + RING) * SR)   # final hit is bar 67 beat 0, hold then ring out


def B(bar, beat=0.0):
    return bar * 4 + beat


def S(beat):
    return tm.s(beat)


def secs(beat, nb):
    return tm.dur(beat, nb)


# ---------------------------------------------------------------- harmony (written in D; final & outro +2)
intro_ch = ['D', 'D', 'C/D', 'D', 'Bb', 'C', 'Dsus4|D', 'Asus4|A']
verse_ch = ['Bm', 'G', 'D', 'A', 'Bm', 'G', 'Em', 'Asus4|A',
            'Bm', 'G', 'D', 'A/C#', 'Bm', 'G', 'Em7', 'A7']
anthem_ch = ['D', 'A/C#', 'Bm', 'F#m/A', 'G', 'D/F#', 'Em7', 'Asus4|A',
             'D', 'A/C#', 'Bm', 'D/A', 'G', 'Gm/Bb', 'D/A|A7', 'D']
break_ch = ['G', 'D/F#', 'Em7', 'Bm7', 'Gmaj7', 'D/F#', 'Em7|C', 'B7sus4|B7']
final_ch = anthem_ch[:14] + ['Bb', 'C']
outro_ch = ['D', 'Bb', 'C', 'D']

CH = []   # (abs_beat, beats, chord dict, section)
for sec, bars, bar0, tr in [('intro', intro_ch, INTRO, 0), ('verse', verse_ch, VERSE, 0),
                            ('anthem', anthem_ch, ANTHEM, 0), ('break', break_ch, BREAK, 0),
                            ('final', final_ch, FINAL, 2), ('outro', outro_ch, OUTRO, 2)]:
    for bi, off, L, c in expand(bars, tr):
        CH.append((B(bar0 + bi, off), L, c, sec))


def chord_at(beat):
    for b, L, c, s in CH:
        if b <= beat < b + L:
            return c
    return CH[-1][2]


def sec_at(beat):
    for b, L, c, s in CH:
        if b <= beat < b + L:
            return s
    return 'outro'


def mel(bars_txt, bar0, tr=0):
    out = []
    for i, txt in enumerate(bars_txt):
        if txt:
            out += parse_line(txt, B(bar0 + i), tr)
    return out


# ---------------------------------------------------------------- melodies (D terms)
fanfare = ['', 'r:2 A4:1/3 A4:1/3 A4:1/3 D5:1', 'E5:1.5 D5:0.5 C5:1 G4:1', 'A4:3', 'D5:1.5 F5:0.5 F5:2',
           'E5:1 G5:1 E5:0.5 D5:0.5 C5:1', 'D5:4', '']
verse_mel = ['B3:1.5 F#4:0.5 F#4:2', 'G4:0.5 F#4:0.5 E4:0.5 D4:0.5 B3:2', 'A3:1.5 D4:0.5 F#4:1 A4:1',
             'G4:1 E4:1 C#4:2', 'B3:1.5 F#4:0.5 B4:2', 'A4:0.5 G4:0.5 F#4:0.5 E4:0.5 D4:2',
             'E4:1 G4:1 B4:1 D5:1', 'C#5:3']
anthem_mel = ['D4:1.5 A4:0.5 A4:2', 'C#5:1 B4:0.5 A4:0.5 E4:2', 'B4:1.5 A4:0.5 F#4:1 D4:1',
              'E4:1 F#4:0.5 A4:0.5 C#5:2', 'D5:1.5 B4:0.5 B4:2', 'A4:0.5 G4:0.5 F#4:0.5 G4:0.5 A4:2',
              'G4:1 B4:1 E5:1 D5:0.5 B4:0.5', 'D5:2 C#5:2',
              'D4:1.5 A4:0.5 A4:1.5 D5:0.5', 'C#5:1.5 B4:0.5 A4:1 E4:1', 'B4:1.5 C#5:0.5 D5:1 E5:1',
              'F#5:3 E5:0.5 D5:0.5', 'G5:1.5 F#5:0.5 E5:1 D5:1', 'D5:1.5 C5:0.5 Bb4:1 G4:1',
              'F#5:1 D5:1 E5:1.5 C#5:0.5', 'D5:4']
final_mel = anthem_mel[:14] + ['D5:1.5 C5:0.5 Bb4:1 D5:1', 'E5:1.5 D5:0.5 C5:1 G4:0.5 A4:0.5']
outro_mel = ['D5:3 A4:1/3 A4:1/3 A4:1/3', 'D5:1.5 F5:0.5 F5:2', 'E5:1 G5:1 E5:0.5 D5:0.5 E5:1', 'D5:3']
break_mel = ['r:1 B3:1 D4:1 G4:1', 'A4:3 F#4:1', 'G4:1.5 F#4:0.5 E4:1 D4:1', 'F#4:4',
             'D5:1.5 C#5:0.5 B4:1 F#4:1', 'A4:4', 'G4:2 E4:2', 'E4:2 D#4:2']
# violin counter-line for the second half of the anthems (D terms)
counter = ['F#5:4', 'E5:4', 'D5:2 F#5:2', 'A5:4', 'B5:2 G5:2', 'Bb5:2 G5:2', 'A5:2 G5:2', 'F#5:4']
counter_final = counter[:6] + ['F5:4', 'G5:4']

MEL_FANFARE = mel(fanfare, INTRO)
MEL_VERSE = mel(verse_mel, VERSE + 8)
MEL_ANTHEM = mel(anthem_mel, ANTHEM)
MEL_BREAK = mel(break_mel, BREAK)
MEL_FINAL = mel(final_mel, FINAL, 2)
MEL_OUTRO = mel(outro_mel, OUTRO, 2)
MEL_COUNTER = mel(counter, ANTHEM + 8) + mel(counter_final, FINAL + 8, 2)


# ============================================================================ stems
from stems import StemStore, mixdown
store = StemStore(os.path.join(SCRATCH, 'vl_stems'), log)


def jit(ms=6.0):
    return int(RNG.normal(0, ms / 1000) * SR)


# ============================================================================ DRUMS (hybrid, tight)
def render_drums():
    kicks = []
    kt = Track(N, 'kick'); sn = Track(N, 'snare'); hh = Track(N, 'hats'); tomt = Track(N, 'toms')
    K_ver = 'x.......x.......'
    S_ver = '............x...'
    H_ver = '..x...x...x...x.'
    K_ant = 'X.....x.x.......'
    S_ant = '....X.......X...'
    H_ant = 'x.oxx.oxx.oxx.ox'
    K_fin = 'X.....x.x.....o.'
    S_fin = '....X.......X..o'
    H_fin = 'xoxxxoxxxoxxxoxo'
    kick_s = I.kick(1.0, f_end=46, f_start=170, decay=0.42, click=0.3)
    kick_s2 = I.kick(0.9, f_end=47, f_start=175, decay=0.38, click=0.3)
    def _sn():
        a = I.snare(1.0, tone=185, decay=0.22, snappy=0.8); c = I.clap(1.0)
        a[:len(c)] += 0.6 * c
        return a
    snares = [_sn() for _ in range(3)]
    hats = [I.hat(0.6) for _ in range(4)]
    shk = I.shaker(0.8)

    def put(tr, sig, beat, vel, j=2.0, p=0.0):
        tr.add(sig * vel, S(beat) + jit(j), 1.0, p)

    for bar in range(VERSE + 8, END):
        s = sec_at(B(bar))
        if s == 'break' and bar < BREAK + 6:
            continue
        if s in ('verse',):
            K, Sn, H = K_ver, S_ver, H_ver
        elif s == 'anthem':
            K, Sn, H = K_ant, S_ant, H_ant
        elif s in ('final',):
            K, Sn, H = K_fin, S_fin, H_fin
        elif s == 'break':   # bars 46-47: build
            K, Sn, H = 'x...x...x...x...', '', '' if bar == BREAK + 6 else 'x.x.x.x.x.x.x.x.'
        else:  # outro
            if bar == OUTRO:
                K, Sn, H = 'X.......x.......', '....x.......x...', 'x.x.x.x.x.x.x.x.'
            elif bar < END - 1:
                K, Sn, H = 'X.......X.......', '........x.......', 'x...x...x...x...'
            else:
                K, Sn, H = 'X...............', '', ''
        # phrase-end fills replace the last beat pattern
        fill = (bar + 1) in (ANTHEM, ANTHEM + 8, BREAK, FINAL, FINAL + 8, OUTRO) or bar == VERSE + 11
        for i, v in steps(K):
            if fill and i >= 12:
                continue
            b = B(bar, i / 4)
            put(kt, kick_s if i == 0 else kick_s2, b, v, 1.5)
            kicks.append(float(tm.t(b)))
        for i, v in steps(Sn):
            if fill and i >= 12:
                continue
            put(sn, snares[RNG.integers(0, 3)], B(bar, i / 4), v * (0.9 if s == 'verse' else 1.0), 2.0, 0.05)
        for i, v in steps(H):
            if fill and i >= 12:
                continue
            put(hh, hats[RNG.integers(0, 4)], B(bar, i / 4), v * RNG.uniform(0.75, 1.0), 3.0, 0.3)
        if s in ('anthem', 'final'):
            for i in range(16):
                put(hh, shk, B(bar, i / 4), (0.9 if i % 4 == 2 else 0.5) * RNG.uniform(0.8, 1.0), 4.0, -0.45)
        if fill:
            # descending tom fill across beat 3.. (16ths) with a snare flam at the end
            fr = [210, 180, 150, 130, 110, 95]
            for k in range(4):
                b = B(bar, 3 + k / 4)
                f = fr[min(5, k + (2 if bar % 2 else 0))]
                put(tomt, I.tom(f, 0.7 + 0.1 * k, decay=0.5), b, 1.0, 3.0, 0.5 - 0.3 * k)
    # snare-roll builds (16th -> 32nd with crescendo)
    for bar in (ANTHEM - 1, FINAL - 1):
        for k in range(32):
            b = B(bar, 2 + k / 16) if k >= 16 else B(bar, k / 8)
            if k < 16 and k % 2:
                continue
            v = 0.2 + 0.75 * (k / 31) ** 1.5
            put(sn, snares[k % 3] * 0.8, b, v, 1.0, 0.05)
    # outro final hit
    put(sn, snares[0], B(END - 1), 1.0)
    return kt, sn, hh, tomt, kicks


# ============================================================================ ORCHESTRAL PERCUSSION
def render_perc():
    tp = Track(N, 'timpani'); tk = Track(N, 'taiko'); cy = Track(N, 'cymbals'); fx = Track(N, 'fx')
    gl = Track(N, 'glock')
    timp_hits = {}   # midi -> list of (time_s, vel)

    def timp_note(c):
        return min([m for m in range(38, 51) if m % 12 == c['root']] or [38])

    def th(m, beat, v):
        timp_hits.setdefault(m, []).append((float(tm.t(beat)) + RNG.normal(0, 0.004), float(np.clip(v, 0.05, 1))))

    def roll(m, b0, nb, v0, v1, rate=13.0):
        t0 = float(tm.t(b0)); t1 = float(tm.t(b0 + nb))
        k = int((t1 - t0) * rate)
        for i in range(k):
            fr = i / max(1, k - 1)
            timp_hits.setdefault(m, []).append((t0 + i / rate + RNG.normal(0, 0.006),
                                                (v0 + (v1 - v0) * fr ** 1.6) * RNG.uniform(0.8, 1.0)))

    D2 = 38
    # intro: roll swell, then hits on the fanfare
    roll(D2, B(0), 4, 0.05, 0.75)
    for bar, hits in [(1, [(0, 1.0), (3.5, 0.5)]), (2, [(0, 0.8)]), (3, [(0, 0.9), (2, 0.5), (2.5, 0.6), (3, 0.7)]),
                      (4, [(0, 1.0)]), (5, [(0, 0.9), (2.5, 0.6), (3, 0.8)]), (6, [(0, 1.0)])]:
        for bt, v in hits:
            th(timp_note(chord_at(B(bar, bt))), B(bar, bt), v)
    th(45, B(7), 0.35); th(45, B(7, 2), 0.25)
    # verse B: soft downbeats, then anthem/final pattern
    for bar in range(VERSE + 8, END - 1):
        s = sec_at(B(bar))
        c = chord_at(B(bar))
        m = timp_note(c)
        if s == 'verse':
            if bar % 2 == 0:
                th(m, B(bar), 0.45)
        elif s in ('anthem', 'final'):
            k = 1.0 if s == 'final' else 0.85
            th(m, B(bar), k)
            th(m, B(bar, 2.5), 0.55 * k)
            th(m, B(bar, 3), 0.7 * k)
            if bar % 4 == 3:
                th(m, B(bar, 3.5), 0.6 * k); th(m, B(bar, 3.75), 0.7 * k)
        elif s == 'outro' and bar < END - 1:
            th(m, B(bar), 1.0); th(m, B(bar, 2), 0.7)
    roll(D2 + 9, B(BREAK + 6), 8, 0.05, 0.9)       # B2 roll into final (E)
    roll(40, B(END - 2, 2), 2, 0.3, 1.0, rate=15)  # E2 roll into final hit
    th(40, B(END - 1), 1.0)
    th(47, B(END - 1), 0.7)
    log('  timpani hits', sum(len(v) for v in timp_hits.values()))
    for m, hits in timp_hits.items():
        hits.sort()
        t0 = max(0, hits[0][0] - 0.05)
        L = hits[-1][0] - t0 + 4.0
        y = I.timpani(m, [(h - t0, v) for h, v in hits], L)
        tp.add(y, int(t0 * SR), 1.0, -0.15)

    # taiko ensemble: two drums (L/R), hits
    taikos = [I.taiko(56, 1.0), I.taiko(61, 1.0), I.taiko(52, 1.0)]

    def tkh(beat, v, p=None):
        tk.add(taikos[RNG.integers(0, 3)] * v, S(beat) + jit(4), 1.0, RNG.uniform(-0.5, 0.5) if p is None else p)

    for bar in (1, 4, 6):
        tkh(B(bar), 1.0, -0.3); tkh(B(bar), 0.8, 0.35)
    tkh(B(5, 2.5), 0.7); tkh(B(5, 3), 0.8)
    for bar in range(VERSE + 8, VERSE + 16, 2):
        tkh(B(bar), 0.6)
    for bar in list(range(ANTHEM, BREAK)) + list(range(FINAL, OUTRO)):
        if bar % 2 == 0:
            tkh(B(bar), 0.9, -0.3); tkh(B(bar, 0.02), 0.7, 0.35)
        tkh(B(bar, 2), 0.6)
        if bar % 8 == 7:
            for k in range(6):  # triplet fill on beats 3-4
                tkh(B(bar, 2 + k / 3), 0.5 + 0.08 * k)
    for bar in range(OUTRO, END - 1):
        tkh(B(bar), 1.0, -0.3); tkh(B(bar), 0.8, 0.3)
    for k, bt in enumerate([1, 2, 2.5, 3, 3.5]):
        tkh(B(BREAK + 7, bt), 0.5 + 0.1 * k)
    tkh(B(END - 1), 1.0, -0.3); tkh(B(END - 1), 1.0, 0.3); tkh(B(END - 1, 0.01), 0.9, 0.0)

    # cymbals: crashes, reverse swells
    for bar in (1, 6, ANTHEM, ANTHEM + 8, FINAL, FINAL + 8, OUTRO, VERSE + 8):
        cy.add(I.crash(4.5, RNG.integers(0, 3)) * (0.9 if bar != VERSE + 8 else 0.55), S(B(bar)) + jit(3), 1.0)
    for bar in (ANTHEM + 4, ANTHEM + 12, FINAL + 4, FINAL + 12, VERSE + 12):
        cy.add(I.crash(3.5, 3 + bar % 2) * 0.55, S(B(bar)) + jit(3), 1.0, 0)
    cy.add(I.crash(7.0, 5) * 1.0, S(B(END - 1)), 1.0)
    for bar, nb in [(1, 2), (VERSE, 2), (ANTHEM, 4), (FINAL, 4), (END - 1, 4)]:
        L = secs(B(bar) - nb, nb)
        r = I.reverse_cymbal(L, 3 + bar % 3)
        cy.add(r * 0.7, S(B(bar)) - len(r), 1.0)
    # risers + impacts (subtle hybrid layer)
    for bar, nb in [(ANTHEM, 8), (FINAL, 8)]:
        L = secs(B(bar) - nb, nb)
        r = I.riser(L, 300, 6000, pitch=True, m0=50 if bar == ANTHEM else 52)
        fx.add(r * 0.5, S(B(bar)) - len(r), 1.0)
    for bar in (1, ANTHEM, FINAL, END - 1):
        fx.add(I.impact(1.0 if bar == END - 1 else 0.8, 3.5), S(B(bar)), 1.0)

    # glockenspiel sparkle: doubles long melody notes an octave up in chosen bars; breakdown sparkle
    def glock_line(notes, bars_ok, oct=12, vel=0.5):
        for b, d, m in notes:
            bar = int(b // 4)
            if bar in bars_ok and d >= 1:
                gl.add(I.glock(m + oct, vel * RNG.uniform(0.85, 1.0)), S(b) + jit(3), 1.0, 0.35)
    glock_line(MEL_FANFARE, set(range(0, 8)))
    glock_line(MEL_ANTHEM, set(range(ANTHEM, ANTHEM + 4)) | set(range(ANTHEM + 8, ANTHEM + 16)))
    glock_line(MEL_FINAL, set(range(FINAL, OUTRO)))
    glock_line(MEL_OUTRO, set(range(OUTRO, END)))
    gl.add(I.glock(midi('E6'), 0.7, 4.0), S(B(END - 1)), 1.0, 0.3)
    gl.add(I.glock(midi('B6'), 0.5, 4.0), S(B(END - 1)) + 300, 1.0, -0.2)
    return tp, tk, cy, fx, gl


# ============================================================================ STRINGS
def strings_line(tr, notes, vel_fn, attack=0.2, pan_c=0.0, nv=5, spread=0.5, bright=1.0, rel=0.5, vib=11):
    """Render a monophonic line with legato: connected notes get a short attack."""
    prev_end = -1
    for b, d, m in notes:
        dur = secs(b, d)
        legato = abs(b - prev_end) < 1e-6
        a = 0.08 if legato else attack
        y = I.strings([m], dur + (0.06 if legato else 0), vel_fn(b), attack=a, release=rel, n_voices=nv,
                      spread=spread, bright=bright, vib_depth=vib, pan_center=pan_c)
        tr.add(y, S(b) + jit(8) - (int(0.03 * SR) if legato else 0), 1.0)
        prev_end = b + d


def merge_lines(voicings, spans):
    """voicings: list of tuples per span; spans: list of (beat, beats). -> per-voice list of (beat,dur,midi) with ties."""
    nv = len(voicings[0])
    lines = [[] for _ in range(nv)]
    for (b, L), vc in zip(spans, voicings):
        for v in range(nv):
            if lines[v] and lines[v][-1][2] == vc[v] and abs(lines[v][-1][0] + lines[v][-1][1] - b) < 1e-6:
                ob, od, om = lines[v][-1]
                lines[v][-1] = (ob, od + L, om)
            else:
                lines[v].append((b, L, vc[v]))
    return lines


def clip_end(notes, end):
    return [(b, min(d, end - b), m) for b, d, m in notes if b < end]


SEC_VEL = {'intro': 0.5, 'verse': 0.4, 'anthem': 0.68, 'break': 0.45, 'final': 0.78, 'outro': 0.82}


def render_strings():
    hi = Track(N, 'strings_hi'); lo = Track(N, 'strings_lo'); ctr = Track(N, 'violins_ctr')
    # pads: voice-led 4-part, bars 0-67 except the fanfare bars get a lighter pad
    spans = [(b, L) for b, L, c, s in CH]
    chords = [c for b, L, c, s in CH]
    vcs = voice_lead(chords, [(55, 66), (60, 71), (64, 76), (67, 79)], start=[57, 62, 66, 69])
    lines = merge_lines(vcs, spans)

    def vf(b):
        s = sec_at(b)
        v = SEC_VEL[s]
        if s == 'verse' and b < B(VERSE + 8):
            v = 0.3 + 0.1 * (b - B(VERSE)) / 32
        if s == 'intro' and b < B(1):
            v = 0.3
        return v
    pans = [0.35, 0.1, -0.2, -0.45]
    for v, line in enumerate(lines):
        line = clip_end(line, B(END - 1))
        strings_line(hi, line, vf, attack=0.35, pan_c=pans[v], nv=4, spread=0.35, bright=0.9)
        hi.add(I.strings([lines[v][-1][2]], 3.8, 0.8, attack=0.08, release=2.6, n_voices=4, spread=0.35,
                         bright=0.9, pan_center=pans[v]), S(B(END - 1)) + jit(5), 1.0)
    log('  pads done')
    # low strings: celli + basses on the bass note, sustained, merged
    bl = []
    for b, L, c, s in CH:
        m = min([x for x in range(38, 51) if x % 12 == c['bass']])
        if bl and bl[-1][2] == m and abs(bl[-1][0] + bl[-1][1] - b) < 1e-6:
            bl[-1] = (bl[-1][0], bl[-1][1] + L, m)
        else:
            bl.append((b, L, m))
    bl = clip_end(bl, B(END - 1))
    lo.add(I.strings([40, 28], 3.8, 0.8, attack=0.05, release=2.6, n_voices=4, spread=0.3, bright=0.8,
                     pan_center=0.2), S(B(END - 1)), 1.0)
    for b, d, m in bl:
        dur = secs(b, d)
        vel = SEC_VEL[sec_at(b)] * (0.9 if sec_at(b) != 'break' else 0.8)
        y = I.strings([m, m - 12], dur, vel, attack=0.12, release=0.5, n_voices=4, spread=0.3, bright=0.8,
                      vib_depth=8, pan_center=0.2)
        lo.add(y, S(b) + jit(6), 1.0)
    # celli double the verse melody an octave below (bars 16-23), violins counter-line in anthems
    strings_line(lo, [(b, d, m - 12) for b, d, m in MEL_VERSE], lambda b: 0.5, attack=0.1, pan_c=0.3, nv=4)
    strings_line(ctr, MEL_COUNTER, lambda b: 0.55 if sec_at(b) == 'anthem' else 0.65, attack=0.25,
                 pan_c=-0.4, nv=6, spread=0.4, bright=0.85, vib=13)
    log('  low + counter done')
    return hi, lo, ctr


def render_ostinato():
    tr = Track(N, 'ostinato')
    PA = ['r', '-', '5', '-', '8', '-', '5', '-'] * 2
    PB = ['r', 'r', '8', 'r', 'r', '8', 'r', '5', 'r', 'r', '8', 'r', 'r', '8', '5', '3']
    PC = ['8', 'r', '5', '8', 'r', '5', '8', 'r', '3', 'r', '5', '8', 'r', '5', '8', '5']
    acc = {0, 3, 6, 8, 11, 14}

    def pitch(sym, c, center):
        r = min([m for m in range(center - 6, center + 7) if m % 12 == c['root']])
        third = c['pcs'][1]
        fifth = c['pcs'][-1] if len(c['pcs']) >= 3 else (c['root'] + 7) % 12
        if sym == 'r':
            return r
        if sym == '8':
            return r + 12
        if sym == '5':
            return r + ((fifth - c['root']) % 12)
        if sym == '3':
            return r + ((third - c['root']) % 12)

    for bar in list(range(VERSE, BREAK)) + [BREAK + 6, BREAK + 7] + list(range(FINAL, OUTRO)) + [OUTRO, OUTRO + 1, OUTRO + 2]:
        s = sec_at(B(bar))
        if s == 'verse' and bar < VERSE + 8:
            pat, center, vel = PA, 50, 0.45 + 0.2 * (bar - VERSE) / 8
        elif s == 'verse':
            pat, center, vel = PB, 55, 0.62
        elif s == 'break':
            pat, center, vel = PB, 57, 0.4 + 0.15 * (bar - BREAK - 6)
        elif s == 'anthem':
            pat, center, vel = (PB if (bar - ANTHEM) % 8 < 4 else PC), 57, 0.72
        else:
            pat, center, vel = (PB if (bar - FINAL) % 8 < 4 else PC), 59, 0.8
        for i, sym in enumerate(pat):
            if sym == '-':
                continue
            b = B(bar, i / 4)
            c = chord_at(b)
            m = pitch(sym, c, center)
            v = vel * (1.0 if i in acc else 0.72) * RNG.uniform(0.92, 1.05)
            y = I.spiccato(m, min(v, 1.0), length=0.13 if pat is not PA else 0.2)
            tr.add(y, S(b) + jit(3), 1.0, 0)
    return tr


# ============================================================================ BRASS
def brass_line(tr, notes, kind, vel_fn, nv=3, oct=0, pan_c=0.0, spread=0.5, bright=1.0, stacc=0.92):
    prev_end = -1
    for b, d, m in notes:
        legato = abs(b - prev_end) < 1e-6 and d < 3
        dur = secs(b, d) * (0.98 if legato else stacc)
        y = I.brass(m + oct, dur, vel_fn(b), kind, n_voices=nv, spread=spread, bright=bright, legato=legato)
        tr.add(y, S(b) + jit(7), 1.0, 0)
        prev_end = b + d
    if pan_c:
        pass


def render_brass():
    hn = Track(N, 'horns'); tp = Track(N, 'trumpets'); tb = Track(N, 'low_brass'); solo = Track(N, 'solo_horn')

    def v_sec(b):
        s = sec_at(b)
        return {'intro': 0.85, 'verse': 0.62 + 0.25 * max(0, b - B(VERSE + 8)) / 32, 'anthem': 0.82,
                'break': 0.5, 'final': 0.9, 'outro': 0.95}[s]

    # fanfare: trumpets lead, horns an octave lower
    brass_line(tp, MEL_FANFARE, 'trumpet', lambda b: 0.9, nv=3, spread=0.4)
    brass_line(hn, MEL_FANFARE, 'horn', lambda b: 0.8, nv=4, oct=-12, spread=0.6)
    # verse melody (horns) + anthem/final/outro melody
    brass_line(hn, MEL_VERSE, 'horn', v_sec, nv=4, spread=0.6)
    for notes in (MEL_ANTHEM, MEL_FINAL, MEL_OUTRO):
        brass_line(hn, notes, 'horn', v_sec, nv=4, spread=0.6)
        brass_line(hn, notes, 'horn', lambda b: v_sec(b) * 0.85, nv=3, oct=-12, spread=0.5)
    # trumpets double 2nd half of the anthem, whole final + outro
    brass_line(tp, [n for n in MEL_ANTHEM if n[0] >= B(ANTHEM + 8)], 'trumpet', lambda b: 0.72, nv=3, spread=0.4)
    brass_line(tp, MEL_FINAL + MEL_OUTRO, 'trumpet', lambda b: 0.8, nv=3, spread=0.4)
    log('  melodies done')
    # breakdown solo horn
    brass_line(solo, MEL_BREAK, 'horn', lambda b: 0.5, nv=1, spread=0)
    # low brass (trombones + tuba) sustained chords in intro/anthem/final/outro; horn pads in verse A
    spans = [(b, L) for b, L, c, s in CH]
    chords = [c for b, L, c, s in CH]
    vl_tb = voice_lead(chords, [(45, 55), (50, 60), (55, 65)], start=[50, 54, 57])
    for (b, L), vc, (_, _, c, s) in zip(spans, vl_tb, CH):
        if s in ('anthem', 'final', 'outro') or (s == 'intro' and 1 <= b // 4 <= 6):
            if b >= B(END - 1):
                continue
            vel = 0.7 if s != 'intro' else 0.8
            for m in vc:
                tb.add(I.brass(m, secs(b, L) * 0.97, vel, 'trombone', n_voices=2, spread=0.3, bright=0.8),
                       S(b) + jit(8), 1.0)
            bassm = min([x for x in range(33, 45) if x % 12 == c['bass']])
            tb.add(I.brass(bassm, secs(b, L) * 0.97, vel, 'tuba', n_voices=1), S(b) + jit(8), 1.0)
    vl_hn = voice_lead(chords, [(50, 60), (54, 64), (57, 68)], start=[50, 54, 57])
    for (b, L), vc, (_, _, c, s) in zip(spans, vl_hn, CH):
        if s == 'verse' and b < B(VERSE + 8):
            for m in vc:
                hn.add(I.brass(m, secs(b, L), 0.35 + 0.15 * (b - B(VERSE)) / 32, 'horn', n_voices=2, spread=0.5,
                               bright=0.8, legato=True), S(b) + jit(10), 1.0)
    log('  chords done')
    # final hit chord: whole brass section
    fin = B(END - 1)
    hold = 3.2
    for m in [52, 56, 59, 64, 68]:
        hn.add(I.brass(m, hold, 1.0, 'horn', n_voices=3, spread=0.6, release=0.5), S(fin), 1.0)
    for m in [64, 68, 71, 76]:
        tp.add(I.brass(m, hold, 0.95, 'trumpet', n_voices=2, spread=0.4, release=0.45), S(fin), 1.0)
    for m in [40, 47, 52, 56]:
        tb.add(I.brass(m, hold, 1.0, 'trombone', n_voices=2, release=0.5), S(fin), 1.0)
    tb.add(I.brass(28, hold, 1.0, 'tuba', n_voices=1, release=0.5), S(fin), 1.0)
    return hn, tp, tb, solo


# ============================================================================ KEYS / BASS
def render_keys():
    bp = Track(N, 'bellpiano')
    # breakdown arpeggios (8ths) + gentle verse-A sparkle every other bar
    for bar in range(BREAK, BREAK + 8):
        for half in (0, 2):
            c = chord_at(B(bar, half))
            base = min([m for m in range(60, 72) if m % 12 == c['root']])
            tones = sorted(set([base + ((p - c['root']) % 12) for p in c['pcs']]))
            seq = tones + [tones[0] + 12, tones[1] + 12]
            order = [0, 1, 2, 3, 4, 3] if len(seq) >= 5 else [0, 1, 2, 3, 2, 1]
            for k in range(4):
                m = seq[order[(k + half * 2) % len(order)] % len(seq)]
                b = B(bar, half + k * 0.5)
                bp.add(I.bellpiano(m, RNG.uniform(0.5, 0.65), secs(b, 0.6)), S(b) + jit(5), 1.0, 0.25 - 0.1 * k)
    for m, v in [(40, 0.8), (47, 0.6), (52, 0.6), (64, 0.45)]:   # low 'bell' ring-out under the final hit
        bp.add(I.bellpiano(m, v, 6.5, length=7.5), S(B(END - 1)), 1.0, 0.0)
    for bar in range(VERSE + 4, VERSE + 8):
        c = chord_at(B(bar))
        base = min([m for m in range(67, 79) if m % 12 == c['root']])
        for k, bt in enumerate([0, 1.5, 3]):
            m = base + [0, 7, 12][k]
            bp.add(I.bellpiano(m, 0.4, secs(B(bar, bt), 1.5)), S(B(bar, bt)) + jit(5), 1.0, -0.3 + 0.3 * k)
    return bp


def render_bass(kicks):
    sb = Track(N, 'synthbass')
    for bar in list(range(VERSE, BREAK)) + [BREAK + 6, BREAK + 7] + list(range(FINAL, END - 1)):
        s = sec_at(B(bar))
        for k in range(8):
            b = B(bar, k * 0.5)
            c = chord_at(b)
            m = min([x for x in range(33, 45) if x % 12 == c['bass']])
            if s in ('anthem', 'final', 'outro') and k in (3, 7):
                m += 12
            if s == 'verse' and bar < VERSE + 8:
                cut = 150 + 500 * (bar - VERSE) / 8; vel = 0.6
            elif s == 'break':
                cut = 500 + 400 * (bar - BREAK - 6); vel = 0.7
            else:
                cut = 900; vel = 0.8
            sb.add(I.synthbass(m, secs(b, 0.42), vel * (1 if k % 2 == 0 else 0.85), cut=cut, res=1.1),
                   S(b) + jit(1.5), 1.0, 0)
    sb.add(I.synthbass(28, 3.5, 1.0, cut=600, res=0.9), S(B(END - 1)), 1.0)
    return sb


# ============================================================================ main
STEMS = ['kick', 'snare', 'hats', 'toms', 'timpani', 'taiko', 'cymbals', 'fx', 'glock', 'strings_hi',
         'strings_lo', 'violins_ctr', 'ostinato', 'horns', 'trumpets', 'low_brass', 'solo_horn', 'bellpiano',
         'synthbass']


ALL_GROUPS = {'drums', 'perc', 'strings', 'ost', 'brass', 'keys', 'bass'}


def render_all(groups=ALL_GROUPS):
    log('N =', N / SR, 's', 'groups', groups)
    kt, sn, hh, tomt, kicks = render_drums()
    json.dump(kicks, open(os.path.join(store.path, 'kicks.json'), 'w'))
    if 'drums' in groups:
        store.save(kt, bands=[('peak', 60, 1.0, 2.0), ('peak', 350, 1.2, -4)])
        store.save(sn, hp=120, bands=[('peak', 200, 1.0, 2.0), ('peak', 2500, 1.0, -2.0)])
        store.save(hh, hp=5000)
        store.save(tomt, hp=70)
    del kt, sn, hh, tomt
    if 'perc' in groups:
        render_perc_group()
    if 'strings' in groups:
        render_strings_group()
    if 'ost' in groups:
        os_ = render_ostinato()
        store.save(os_, hp=120, bands=[('peak', 2500, 1.0, -2.5)], width=1.3)
        del os_
    if 'brass' in groups:
        render_brass_group()
    if 'keys' in groups:
        bp = render_keys()
        store.save(bp, hp=150, bands=[('peak', 3000, 1.0, -2)])
        del bp
    if 'bass' in groups:
        sb = render_bass(kicks)
        store.save(sb, hp=30, lp=2500, comp=dict(thr_db=-14, ratio=3, att_ms=5, rel_ms=80))
        del sb


def render_perc_group():
    tp, tk, cy, fx, gl = render_perc()
    store.save(tp, hp=40, bands=[('peak', 90, 1.0, 2.0)])
    store.save(tk, hp=35, bands=[('peak', 400, 1.0, -3)])
    store.save(cy, hp=500)
    store.save(fx, hp=30)
    store.save(gl, hp=700)
    del tp, tk, cy, fx, gl


def render_strings_group():
    hi, lo, ctr = render_strings()
    store.save(hi, hp=180, bands=[('peak', 300, 1.0, 1.5), ('peak', 2600, 0.8, -3.0)], width=1.3)
    store.save(lo, hp=45, bands=[('peak', 250, 1.0, -1.5), ('lp', 3000, 0.7, 0)])
    store.save(ctr, hp=300, bands=[('peak', 2600, 0.8, -3.0)], width=1.2)
    del hi, lo, ctr


def render_brass_group():
    hn, trp, tb, solo = render_brass()
    store.save(hn, hp=110, bands=[('peak', 450, 0.9, 2.0), ('peak', 2200, 1.0, -2.0)],
               comp=dict(thr_db=-16, ratio=2.5, att_ms=15, rel_ms=150))
    store.save(trp, hp=200, bands=[('peak', 1400, 1.0, 1.0), ('peak', 2800, 1.0, -2.5)])
    store.save(tb, hp=45, bands=[('peak', 180, 1.0, 1.0), ('lp', 2500, 0.7, 0)])
    store.save(solo, hp=120)
    del hn, trp, tb, solo


def A(*pts):
    """automation helper: (bar, dB) pairs -> (beat, dB)."""
    return [(B(b), d) for b, d in pts]


MIXCFG = {
    'kick':        dict(bus='drums', gain=-7),
    'snare':       dict(bus='drums', gain=-7, hall=0.35),
    'hats':        dict(bus='drums', gain=-3, hall=0.1, eq=[('hs', 10000, 0.7, -3)]),
    'toms':        dict(bus='drums', gain=-10, hall=0.35),
    'taiko':       dict(bus='drums', gain=-7.5, hall=0.5),
    'timpani':     dict(bus='orch', gain=-5, hall=0.45, eq=[('ls', 90, 0.7, -2)]),
    'cymbals':     dict(bus='orch', gain=-6, hall=0.3),
    'fx':          dict(bus='orch', gain=-12, hall=0.25),
    'glock':       dict(bus='orch', gain=-14, hall=0.5, dly=0.25),
    'strings_hi':  dict(bus='orch', gain=-18.5, hall=0.45, duck=0.12, eq=[('hs', 7000, 0.7, 4)],
                        auto=A((0, 0), (8, -3), (15, -3), (16, -1), (24, 0), (40, 2), (47, 2), (48, 1))),
    'strings_lo':  dict(bus='orch', gain=-11, hall=0.35, duck=0.2),
    'violins_ctr': dict(bus='orch', gain=-11.5, hall=0.5, eq=[('hs', 7000, 0.7, 4)]),
    'ostinato':    dict(bus='orch', gain=-4.5, hall=0.3, duck=0.18, auto=A((8, -3), (16, 0))),
    'horns':       dict(bus='orch', gain=0, hall=0.4, eq=[('hs', 5000, 0.7, 3)]),
    'trumpets':    dict(bus='orch', gain=-10, hall=0.45, eq=[('hs', 6000, 0.7, 2)]),
    'low_brass':   dict(bus='orch', gain=-16, hall=0.4),
    'solo_horn':   dict(bus='orch', gain=-5, hall=0.55, dly=0.2),
    'bellpiano':   dict(bus='orch', gain=-6, hall=0.45, dly=0.3),
    'synthbass':   dict(bus='low', gain=-8.5, duck=0.6),
}


def main(groups=ALL_GROUPS):
    if groups or not store.has(STEMS):
        render_all(groups or ALL_GROUPS)
    kicks = json.load(open(os.path.join(store.path, 'kicks.json')))
    duck_k = M.duck_env(N, kicks, depth=1.0, att=0.003, rel=0.16)
    ir = M.fdn_ir(rt60=2.9, rt60_hf=1.5, length_s=5.0, predelay_ms=24, size=1.25, seed=7, hf_cut=8500, lf_cut=120)
    total, rep = mixdown(store, MIXCFG, N, tm, duck_k,
                         bus_proc={'orch': dict(thr_db=-20, ratio=2.0, att_ms=30, rel_ms=250),
                                   'drums': dict(thr_db=-16, ratio=3.0, att_ms=8, rel_ms=120)},
                         hall_ir=ir, returns=dict(hall_hp=150, hall_eq=[('peak', 2500, 0.8, -3.0), ('ls', 250, 0.7, -2.0)],
                                                  hall_db=-7, dly_db=-10), delay_s=secs(0, 0.75), log=log,
                         master_auto=A((0, 0), (7, 0), (8, -4.5), (15.5, -3.5), (16, -2), (23, -1), (24, 0), (39.9, 0),
                                       (40, -5), (45.5, -5), (47.9, -1.5), (48, 0.5), (68, 0.5)))
    np.save(os.path.join(SCRATCH, 'vl_premaster.npy'), total.astype(np.float32))
    log('premaster peak', 20 * np.log10(np.abs(total).max()))
    finish(total)


def finish(total):
    y = M.master(total, target_lufs=-14.0, ceiling_db=-1.6,
                 glue=dict(thr_db=-14, ratio=1.8, att_ms=30, rel_ms=200, knee_db=8),
                 eq_bands=[('peak', 300, 0.8, -1.5), ('peak', 2800, 0.7, -1.0), ('hs', 9000, 0.7, 2.0)],
                 clip_drive=1.3)
    # fade only the very end of the reverb tail (last 0.8 s) to exact digital silence
    k = int(0.8 * SR)
    y[-k:] *= np.linspace(1, 0, k)[:, None] ** 2
    log('master LUFS', M.lufs(y), 'TP', M.true_peak_db(y))
    M.deliver_mp3(y, OUT, 'Victory Lane', log=log)
    log('wrote', OUT)


if __name__ == '__main__':
    arg = sys.argv[1] if len(sys.argv) > 1 else ''
    if arg == '--remaster':
        finish(np.load(os.path.join(SCRATCH, 'vl_premaster.npy')))
    else:
        main(set() if arg == '--mix' else (set(arg.split(',')) if arg else ALL_GROUPS))
