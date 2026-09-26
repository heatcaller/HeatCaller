"""Race Day Live - Broadcast soundscape theme (TV sports rock/electronic hybrid).

126 BPM, E major/mixolydian -> F# for the final hook, 88 bars + ring (~2:52).
Run: nice python3 race_day_live.py [groups|--mix|--remaster]
"""
import sys, os, time, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mblib as M
from mblib import SR, RNG, Track, parse_line, hz, midi
import instruments as I
from theory import chord, expand, voice_lead, steps
from stems import StemStore, mixdown

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'music', 'Race Day Live.mp3')
SCRATCH = os.environ.get('MB_SCRATCH', '/tmp')
T0 = time.time()


def log(*a):
    print(f'[{time.time() - T0:6.1f}s]', *a, flush=True)


# ============================================================================ form
BPM = 126
INTRO, GROOVE, HOOKA, STING, VERSE, BUILD, HOOKB, BRIDGE, FINAL, ENDING, END = 0, 4, 12, 28, 29, 45, 49, 65, 73, 85, 88
tm = M.TempoMap(BPM, END * 4 + 16, changes=[(ENDING * 4 + 4, ENDING * 4 + 8, BPM, 112)])
RING = 6.0
N = int((float(tm.t(B_END := END * 4 - 4)) + RING) * SR)


def B(bar, beat=0.0):
    return bar * 4 + beat


def S(beat):
    return tm.s(beat)


def secs(beat, nb):
    return tm.dur(beat, nb)


def jit(ms=4.0):
    return int(RNG.normal(0, ms / 1000) * SR)


# ---------------------------------------------------------------- harmony (E terms; final + ending +2)
hook_ch = ['E', 'D', 'A/C#', 'C|D', 'E', 'D', 'C', 'Bsus4|B', 'E', 'D', 'A/C#', 'C|D', 'E', 'D', 'C|D', 'E']
SECTIONS = [
    ('intro', ['E', 'D', 'E5', 'E5'], INTRO, 0),
    ('groove', ['E5', 'E5', 'G5|A5', 'E5', 'E5', 'E5', 'C5|D5', 'E5'], GROOVE, 0),
    ('hookA', hook_ch, HOOKA, 0),
    ('sting', ['E'], STING, 0),
    ('verse', ['C#m', 'A', 'E', 'B', 'C#m', 'A', 'F#m', 'B', 'C#m', 'A', 'E', 'B/D#', 'C#m', 'A', 'F#m7', 'Bsus4|B'], VERSE, 0),
    ('build', ['A', 'B', 'C#m', 'Bsus4|B'], BUILD, 0),
    ('hookB', hook_ch, HOOKB, 0),
    ('bridge', ['C#m', 'A', 'E', 'B', 'C#m', 'A', 'D', 'D|E'], BRIDGE, 0),
    ('final', ['E', 'D', 'A/C#', 'C|D', 'E', 'D', 'C', 'Bsus4|B', 'C', 'D', 'C|D', 'E'], FINAL, 2),
    ('ending', ['E', 'C|D', 'E'], ENDING, 2),
]
CH = []
for sec, bars, bar0, tr in SECTIONS:
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
    return 'ending'


def mel(bars_txt, bar0, tr=0):
    out = []
    for i, txt in enumerate(bars_txt):
        if txt:
            out += parse_line(txt, B(bar0 + i), tr)
    return out


# ---------------------------------------------------------------- melodies
hook = ['E5:0.75 B4:0.75 E5:0.5 F#5:0.5 G#5:1.5', 'A5:0.5 G#5:0.5 F#5:0.5 D5:1 r:1.5',
        'E5:0.75 C#5:0.75 E5:0.5 A5:2', 'G5:1 E5:0.5 C5:0.5 D5:0.5 F#5:0.5 A5:1',
        'E5:0.75 B4:0.75 E5:0.5 F#5:0.5 G#5:1.5', 'A5:0.5 B5:0.5 A5:0.5 F#5:1 D5:0.5 E5:1',
        'E5:0.75 G5:0.75 C6:0.5 B5:1 G5:1', 'F#5:2 D#5:1 B4:1',
        'E5:0.75 B4:0.75 E5:0.5 F#5:0.5 G#5:1.5', 'A5:0.5 G#5:0.5 F#5:0.5 D5:1 r:1.5',
        'E5:0.75 C#5:0.75 E5:0.5 A5:2', 'G5:1 E5:0.5 C5:0.5 D5:0.5 F#5:0.5 A5:1',
        'B5:0.75 G#5:0.75 E5:0.5 F#5:0.5 G#5:1.5', 'A5:0.5 B5:0.5 A5:0.5 F#5:1 D5:0.5 E5:1',
        'G5:0.75 E5:0.75 G5:0.5 A5:0.75 F#5:0.75 A5:0.5', 'B5:0.75 G#5:0.75 E5:2.5']
tag = ['E5:0.75 C5:0.75 E5:0.5 G5:2', 'F#5:0.75 D5:0.75 F#5:0.5 A5:2',
       'G5:0.75 E5:0.75 G5:0.5 A5:0.75 F#5:0.75 A5:0.5', 'B5:0.75 G#5:0.75 E5:2.5']
answer = {1: 'r:2.5 D4:0.5 E4:0.5 F#4:0.5', 9: 'r:2.5 D4:0.5 E4:0.5 F#4:0.5'}   # trombone answers in hook gaps
sting_mel = ['E5:0.75 D5:0.75 E5:2.5', 'D5:0.75 E5:0.75 F#5:2.5']
sting28 = ['E5:0.75 D5:0.75 E5:1.5']
verse_ctr = ['E4:4', 'E4:2 C#4:2', 'B3:4', 'D#4:4', 'E4:4', 'C#4:4', 'A4:2 F#4:2', 'F#4:2 D#4:2']

MEL_HOOKA = mel(hook, HOOKA)
MEL_HOOKB = mel(hook, HOOKB)
MEL_FINAL = mel(hook[:8] + tag, FINAL, 2)
MEL_STING = mel(sting_mel, INTRO) + mel(sting28, STING)
MEL_ANS = [n for k, v in answer.items() for n in parse_line(v, B(HOOKA + k))] + \
          [n for k, v in answer.items() for n in parse_line(v, B(HOOKB + k))] + \
          [(b, d, m + 2) for k, v in answer.items() if k < 8 for (b, d, m) in parse_line(v, B(FINAL + k))]
MEL_VCTR = mel(verse_ctr, VERSE + 8)
# ending hits: (bar, beat, melody note in E terms, chord)
END_HITS = [(ENDING, 0, 'E5'), (ENDING, 0.75, 'B4'), (ENDING, 1.5, 'E5'), (ENDING, 2.5, 'G#5'), (ENDING, 3, 'B5'),
            (ENDING + 1, 0, 'G5'), (ENDING + 1, 0.75, 'G5'), (ENDING + 1, 1.5, 'A5')]
FINAL_HIT = B(END - 1)


def below_tone(m, c, min_gap=3):
    """nearest chord tone at least min_gap semitones below m (for close brass harmony)."""
    for k in range(min_gap, 10):
        if (m - k) % 12 in c['pcs']:
            return m - k
    return m - 5


# ============================================================================ stems
store = StemStore(os.path.join(SCRATCH, 'rdl_stems'), log)


# ============================================================================ DRUMS
def render_drums():
    kicks = []
    kt = Track(N, 'kick'); sn = Track(N, 'snare'); hh = Track(N, 'hats'); tomt = Track(N, 'toms')
    global RIDE
    RIDE = Track(N, 'ride')
    kick_s = I.kick(1.0, f_end=52, f_start=230, decay=0.3, click=0.75, drive=2.2)
    kick_s2 = I.kick(0.9, f_end=53, f_start=220, decay=0.26, click=0.7, drive=2.2)

    def _sn(k):
        a = I.snare(1.0, tone=200 + 6 * k, decay=0.24, snappy=1.0, body=1.0); c = I.clap(1.0)
        a[:len(c)] += 0.55 * c
        return I.gated_snare_verb(a, 0.26)
    snares = [_sn(k) for k in range(3)]
    ghost = I.snare(0.35, tone=205, decay=0.12, snappy=0.6)
    hats = [I.hat(0.6) for _ in range(4)]
    ohat = I.hat(0.7, open_=True)
    toms = {'hi': I.tom(190, 0.9, decay=0.45), 'mid': I.tom(145, 0.9, decay=0.5), 'lo': I.tom(105, 0.95, decay=0.6),
            'floor': I.tom(82, 1.0, decay=0.7)}

    def put(tr, sig, beat, vel, j=1.5, p=0.0):
        tr.add(sig * vel, S(beat) + jit(j), 1.0, p)

    def kick_at(beat, v):
        put(kt, kick_s if v > 0.9 else kick_s2, beat, v, 1.0)
        kicks.append(float(tm.t(beat)))

    P = {
        'groove': ('X.....x.X.x.....', '....X.......X...', 'x.x.x.x.x.x.x...', '..............x.', ''),
        'hook':   ('X.....x.X.x...x.', '....X.......X...', 'xoxoxoxoxoxoxo..', '..............x.', ''),
        'verse':  ('X.......X.x.....', '....X.......X...', '', '', 'x.x.x.x.x.x.x.x.'),
        'final':  ('X.....x.X.x...x.', '....X.......X..o', 'xoxoxoxoxoxoxo..', '..............x.', ''),
    }
    for bar in range(0, END - 1):
        s = sec_at(B(bar))
        phrase_end = (bar + 1) in (GROOVE, HOOKA, HOOKA + 8, STING, VERSE + 8, BUILD, HOOKB, HOOKB + 8, BRIDGE, FINAL,
                                   FINAL + 8, ENDING) or (s in ('verse', 'hookA', 'hookB', 'final') and bar % 4 == 3)
        big_fill = (bar + 1) in (HOOKA, STING, BUILD, HOOKB, FINAL, ENDING, BRIDGE)
        if s == 'intro':
            if bar < 2:
                for bt in (0, 0.75, 1.5):
                    kick_at(B(bar, bt), 1.0); put(sn, snares[0], B(bar, bt), 0.9 if bt else 1.0)
                if bar == 1:
                    for k in range(8):   # 16th tom run on beats 3-4
                        put(tomt, toms[['hi', 'hi', 'mid', 'mid', 'lo', 'lo', 'floor', 'floor'][k]], B(bar, 2 + k / 4),
                            0.7 + 0.04 * k, 1.5, 0.5 - 0.14 * k)
                continue
            K, Sn, H, OH, R = 'X.......X.......', '', 'x.x.x.x.x.x.x.x.', '', ''
            if bar == 3:
                K, Sn = 'X.......X...X...', '........x.x.xxxx'
        elif s == 'sting':
            for bt in (0, 0.75, 1.5):
                kick_at(B(bar, bt), 1.0); put(sn, snares[1], B(bar, bt), 1.0)
            for k in range(8):
                put(tomt, toms[['hi', 'hi', 'mid', 'mid', 'lo', 'lo', 'floor', 'floor'][k]], B(bar, 2 + k / 4),
                    0.75 + 0.03 * k, 1.5, 0.5 - 0.14 * k)
            continue
        elif s == 'groove':
            K, Sn, H, OH, R = P['groove']
        elif s in ('hookA', 'hookB'):
            K, Sn, H, OH, R = P['hook']
        elif s == 'final':
            K, Sn, H, OH, R = P['final']
        elif s == 'verse':
            K, Sn, H, OH, R = P['verse']
        elif s == 'build':
            k = bar - BUILD
            K = 'X...X...X...X...'
            Sn = ['x...x...x...x...', 'x.x.x.x.x.x.x.x.', 'x.x.x.x.xxxxxxxx', 'xxxxxxxxxxxx....'][k]
            H, OH, R = '', '', ''
            if k == 3:
                K = 'X...X...X.......'
        elif s == 'bridge':
            k = bar - BRIDGE
            K, Sn, H, OH, R = 'X.......X.......', '', '', '', ''
            for i, v in steps('X..x..x.X..x..x.' if k < 6 else 'X.x.x.x.X.x.x.x.'):
                put(tomt, toms['floor'] if i % 8 == 0 else toms['lo'], B(bar, i / 4), v * 0.8, 2, 0.2 if i % 8 else -0.1)
            if k >= 6:
                Sn = 'x...x...x.x.x.x.' if k == 6 else 'xxxxxxxxxxxxxxxx'
        elif s == 'ending':
            continue
        vel_sec = {'verse': 0.9, 'groove': 0.95}.get(s, 1.0)
        for i, v in steps(K):
            if big_fill and i >= 12:
                continue
            kick_at(B(bar, i / 4), v * vel_sec)
        for i, v in steps(Sn):
            if big_fill and i >= 12 and s not in ('build', 'bridge'):
                continue
            vv = v
            if s in ('build', 'bridge'):
                vv = 0.35 + 0.6 * ((bar - (BUILD if s == 'build' else BRIDGE + 6)) * 16 + i) / 64
                vv = min(vv, 1.0)
            put(sn, snares[RNG.integers(0, 3)] if vv > 0.5 else ghost * 2, B(bar, i / 4), vv * vel_sec, 1.5, 0.03)
        for i, v in steps(H):
            if phrase_end and i >= 12:
                continue
            put(hh, hats[RNG.integers(0, 4)], B(bar, i / 4), v * RNG.uniform(0.8, 1.0), 2.5, 0.3)
        for i, v in steps(OH):
            if phrase_end:
                continue
            put(hh, ohat, B(bar, i / 4), 0.8, 2.0, 0.3)
        for i, v in steps(R):
            put(RIDE, I.ride(0.55 if i % 4 else 0.75, variant=int(RNG.integers(0, 3))), B(bar, i / 4), 1.0, 2.5, -0.35)
        if s in ('verse',) and phrase_end is False:
            for i in (2, 6, 10, 14):   # ghost notes
                if RNG.random() < 0.4:
                    put(sn, ghost, B(bar, i / 4), 0.8, 3, 0.03)
        if phrase_end and s not in ('build', 'bridge', 'intro'):
            seq = (['hi', 'hi', 'mid', 'mid'] if not big_fill else ['snare', 'hi', 'mid', 'lo'])
            for k in range(4):
                b = B(bar, 3 + k / 4)
                if seq[k] == 'snare':
                    put(sn, snares[0], b, 0.85)
                else:
                    put(tomt, toms[seq[k]], b, 0.8 + 0.05 * k, 1.5, 0.45 - 0.3 * k)
            if big_fill:
                for k in range(4):
                    put(tomt, toms[['hi', 'mid', 'lo', 'floor'][k]], B(bar, 2 + k / 4), 0.7, 1.5, 0.45 - 0.3 * k)
    # ending: unison hits, fill, final hit
    for bar, bt, _ in END_HITS:
        kick_at(B(bar, bt), 1.0); put(sn, snares[bt == 0 and 1 or 2], B(bar, bt), 1.0)
    for k in range(8):
        put(tomt, toms[['hi', 'hi', 'mid', 'mid', 'lo', 'lo', 'floor', 'floor'][k]], B(ENDING + 1, 2 + k / 4),
            0.8 + 0.025 * k, 1.5, 0.5 - 0.14 * k)
    kick_at(FINAL_HIT, 1.0); put(sn, snares[0], FINAL_HIT, 1.0)
    put(tomt, toms['floor'], FINAL_HIT, 1.0)
    return kt, sn, hh, tomt, kicks


# ============================================================================ CYMBALS / FX
def render_fx():
    cy = Track(N, 'cymbals'); fx = Track(N, 'fx')
    crash_bars = [0, 1, GROOVE, HOOKA, HOOKA + 4, HOOKA + 8, HOOKA + 12, STING, VERSE, VERSE + 8, HOOKB, HOOKB + 4,
                  HOOKB + 8, HOOKB + 12, BRIDGE, FINAL, FINAL + 4, FINAL + 8]
    for bar in crash_bars:
        v = 1.0 if bar in (0, HOOKA, HOOKB, FINAL, STING) else 0.75
        cy.add(I.crash(3.5, int(RNG.integers(0, 4))) * v, S(B(bar)) + jit(2), 1.0)
    for bar, bt, _ in END_HITS:
        cy.add(I.crash(2.5, int(RNG.integers(0, 4))) * 0.8, S(B(bar, bt)), 1.0)
    cy.add(I.crash(6.0, 7) * 1.0, S(FINAL_HIT), 1.0)
    cy.add(I.crash(6.0, 8) * 0.8, S(FINAL_HIT) + 200, 1.0)
    for bar, nb in [(HOOKA, 2), (HOOKB, 4), (FINAL, 4), (GROOVE, 2), (BRIDGE, 1)]:
        r = I.reverse_cymbal(secs(B(bar) - nb, nb), 3 + bar % 3)
        cy.add(r * 0.7, S(B(bar)) - len(r), 1.0)
    # risers
    for bar, nb, m0 in [(GROOVE, 8, 52), (HOOKA, 8, 52), (HOOKB, 16, 50), (FINAL, 8, 54)]:
        r = I.riser(secs(B(bar) - nb, nb), 300, 7500, pitch=True, m0=m0)
        fx.add(r * 0.6, S(B(bar)) - len(r), 1.0)
    for bar in (0, HOOKA, STING, HOOKB, FINAL):
        fx.add(I.impact(0.9, 3.0), S(B(bar)), 1.0)
    fx.add(I.impact(1.0, 4.0), S(FINAL_HIT), 1.0)
    for bar in (HOOKA, HOOKB, FINAL, BRIDGE):
        d = I.downlifter(secs(B(bar), 4))
        fx.add(d * 0.45, S(B(bar)) + int(0.1 * SR), 1.0)
    return cy, fx


# ============================================================================ GUITARS
PAT = {
    'groove':  'MmmMmmMm',
    'groove2': 'O--mMmmm',
    'hook':    'O-------',
    'hook2':   'O---O---',
    'hookp':   'O-----mm',
    'verse':   'Mmmmmmmm',
    'verse1':  'O-mmMmmm',
    'build':   'mmmmmmmmmmmmmmmm',
    'bridge':  'O-------',
}


def guitar_pattern(bar):
    s = sec_at(B(bar))
    two = len([1 for b, L, c, ss in CH if B(bar) <= b < B(bar + 1)]) > 1
    if s == 'intro':
        return None if bar < 2 else ('mmmmmmmm' if bar == 2 else 'mmmmmmmmmmmmmmmm')
    if s == 'groove':
        return PAT['groove2'] if two else PAT['groove']
    if s in ('hookA', 'hookB', 'final'):
        if two:
            return PAT['hook2']
        return PAT['hookp'] if bar % 2 else PAT['hook']
    if s == 'verse':
        return PAT['verse1'] if bar % 2 == 0 else PAT['verse']
    if s == 'build':
        return PAT['build']
    if s == 'bridge':
        if two:
            return PAT['hook2']
        return PAT['bridge'] if bar % 2 == 0 else None
    return None


def power(c, open_=True):
    r = min([m for m in range(40, 52) if m % 12 == c['root']])
    return [r, r + 7, r + 12] if open_ else [r, r + 7]


def render_guitars():
    outs = []
    for side, pn in (('gtrL', -0.85), ('gtrR', 0.85)):
        di = Track(N, side + '_di')
        for bar in range(0, END - 1):
            pat = guitar_pattern(bar)
            if pat is None:
                continue
            step = 4 / len(pat)
            s = sec_at(B(bar))
            base_v = {'verse': 0.7, 'bridge': 0.55, 'intro': 0.6, 'build': 0.75}.get(s, 0.9)
            for i, ch in enumerate(pat):
                if ch in '.-':
                    continue
                b = B(bar, i * step)
                c = chord_at(b)
                if ch in 'mM':
                    v = base_v * (1.0 if ch == 'M' else 0.8) * RNG.uniform(0.9, 1.0)
                    if s == 'build':
                        v *= 0.6 + 0.4 * (bar - BUILD + i / 16) / 4
                    y = I.guitar_di(power(c, False), secs(b, step) * 0.9, v, mute=True, strum_ms=3)
                else:
                    L = 1
                    while i + L < len(pat) and pat[i + L] == '-':
                        L += 1
                    y = I.guitar_di(power(c, True), secs(b, L * step) * 0.97, base_v, mute=False, strum_ms=8)
                    if s == 'bridge':   # volume swell
                        y = y * np.minimum(np.arange(len(y)) / (0.5 * SR), 1.0) ** 2
                di.add(y, S(b) + jit(5), 1.0, 0)
        # stinger + ending hits
        for bar, bts in [(0, [0, 0.75, 1.5]), (1, [0, 0.75, 1.5]), (STING, [0, 0.75, 1.5])]:
            for k, bt in enumerate(bts):
                b = B(bar, bt)
                L = 0.6 if k < 2 else 2.3
                di.add(I.guitar_di(power(chord_at(b)), secs(b, L), 1.0, strum_ms=6), S(b) + jit(3), 1.0)
        for bar, bt, _ in END_HITS:
            b = B(bar, bt)
            di.add(I.guitar_di(power(chord_at(b)), secs(b, 0.5), 1.0, strum_ms=5), S(b) + jit(3), 1.0)
        di.add(I.guitar_di(power(chord('E', 2)), 4.5, 1.0, strum_ms=12), S(FINAL_HIT), 1.0)
        log('  guitar DI', side)
        wet = I.amp_sim(di.buf[:, 0].astype(np.float64), drive=16.0 if side == 'gtrL' else 19.0,
                        cab_lp=4600 if side == 'gtrL' else 5200)
        # natural decay of the last chord (distortion would otherwise hold it at full level)
        t_rel = (np.arange(len(wet)) - (S(FINAL_HIT) + int(0.25 * SR))) / SR
        wet = wet * np.where(t_rel > 0, np.exp(-np.clip(t_rel, 0, None) / 1.1), 1.0)
        tr = Track(N, side)
        tr.add(wet, 0, 1.0, pn)
        outs.append(tr)
        del di
    return outs


# ============================================================================ BASS
def render_bass():
    sb = Track(N, 'bass')
    for bar in range(0, END - 1):
        s = sec_at(B(bar))
        if s in ('intro',) and bar < 2 or s == 'sting' or s == 'ending':
            continue
        if s == 'bridge':
            for b, L, c, ss in CH:
                if B(bar) <= b < B(bar + 1):
                    m = min([x for x in range(28, 40) if x % 12 == c['bass']])
                    sb.add(I.synthbass(m, secs(b, L) * 0.95, 0.7, cut=300, res=0.8, drive=1.8), S(b), 1.0)
            continue
        grid = 16 if s == 'build' else 8
        for k in range(grid):
            b = B(bar, k * 4 / grid)
            c = chord_at(b)
            m = min([x for x in range(28, 40) if x % 12 == c['bass']])
            if s in ('hookA', 'hookB', 'final') and k in (3, 7):
                m += 12
            if s == 'groove' and k == 7:
                m += 12
            v = (0.9 if k % 2 == 0 else 0.75) * (0.85 if s in ('verse', 'intro') else 1.0)
            sb.add(I.synthbass(m, secs(b, 4 / grid) * 0.85, v, cut=1400, res=0.9, drive=2.6, sub=0.6), S(b) + jit(1.5), 1.0)
    for bar, bts in [(0, [0, 0.75, 1.5]), (1, [0, 0.75, 1.5]), (STING, [0, 0.75, 1.5])]:
        for k, bt in enumerate(bts):
            b = B(bar, bt)
            m = min([x for x in range(28, 40) if x % 12 == chord_at(b)['bass']])
            sb.add(I.synthbass(m, secs(b, 0.6 if k < 2 else 2.3), 1.0, cut=1200, drive=2.6), S(b), 1.0)
    for bar, bt, _ in END_HITS:
        b = B(bar, bt)
        m = min([x for x in range(28, 40) if x % 12 == chord_at(b)['bass']])
        sb.add(I.synthbass(m, secs(b, 0.45), 1.0, cut=1200, drive=2.6), S(b), 1.0)
    fb = I.synthbass(30, 4.5, 1.0, cut=800, drive=2.2)
    fb *= np.exp(-np.arange(len(fb)) / SR / 1.2)
    sb.add(fb, S(FINAL_HIT), 1.0)
    return sb


# ============================================================================ BRASS / LEAD / SYNTHS
def brass_notes(tr, notes, kind, vel, nv=3, oct=0, spread=0.4, stacc=0.85, bright=1.0, fall_last=False):
    for idx, (b, d, m) in enumerate(notes):
        dur = secs(b, d) * (stacc if d < 1.5 else 0.93)
        fall = 0.0
        y = I.brass(m + oct, dur, vel * RNG.uniform(0.95, 1.0), kind, n_voices=nv, spread=spread, bright=bright,
                    fall=fall, vib=d >= 1.5)
        tr.add(y, S(b) + jit(5), 1.0)


def render_brass():
    tp = Track(N, 'trumpets'); tb = Track(N, 'trombones'); hn = Track(N, 'horns'); ld = Track(N, 'lead')
    hooks = MEL_HOOKA + MEL_HOOKB + MEL_FINAL
    brass_notes(tp, hooks, 'trumpet', 0.92, nv=3, bright=1.1)
    brass_notes(tb, hooks, 'trombone', 0.85, nv=3, oct=-12, spread=0.5)
    # hook B + final: horns harmony a chord-tone below
    harm = [(b, d, below_tone(m, chord_at(b))) for b, d, m in MEL_HOOKB + MEL_FINAL]
    brass_notes(hn, harm, 'horn', 0.85, nv=3, spread=0.6)
    brass_notes(tb, MEL_ANS, 'trombone', 0.9, nv=3, spread=0.5)
    log('  hooks done')
    # stingers
    brass_notes(tp, MEL_STING, 'trumpet', 1.0, nv=3, bright=1.15, stacc=0.7)
    brass_notes(tb, MEL_STING, 'trombone', 0.95, nv=3, oct=-12, stacc=0.7)
    for b, d, m in MEL_STING:
        c = chord_at(b)
        for mm in (below_tone(m, c), below_tone(below_tone(m, c), c)):
            hn.add(I.brass(mm, secs(b, d) * 0.7, 0.9, 'horn', n_voices=2, spread=0.6), S(b) + jit(4), 1.0)
    # groove + verse stabs: short bright chords
    for bar in list(range(GROOVE, HOOKA)) + list(range(VERSE, BUILD)):
        s = sec_at(B(bar))
        if s == 'groove':
            hits = [2.5, 3.0] if bar % 2 == 1 else []
        else:
            hits = [0.0] if bar % 2 == 0 else [1.5, 2.5]
        for bt in hits:
            b = B(bar, bt)
            c = chord_at(b)
            top = min([x for x in range(67, 79) if x % 12 in c['pcs']], key=lambda x: abs(x - 72))
            vo = [top, below_tone(top, c), below_tone(below_tone(top, c), c)]
            for m in vo:
                tp.add(I.brass(m, secs(b, 0.3), 0.85, 'trumpet', n_voices=2, spread=0.5, vib=False, release=0.08),
                       S(b) + jit(4), 1.0)
            tb.add(I.brass(vo[-1] - 12, secs(b, 0.3), 0.85, 'trombone', n_voices=2, vib=False, release=0.08),
                   S(b) + jit(4), 1.0)
    # verse horn counter-line
    brass_notes(hn, MEL_VCTR, 'horn', 0.6, nv=3, spread=0.6, stacc=0.97)
    # ending hits + final chord
    for bar, bt, nm in END_HITS:
        b = B(bar, bt)
        m = midi(nm) + 2
        c = chord_at(b)
        tp.add(I.brass(m, secs(b, 0.4), 1.0, 'trumpet', n_voices=3, vib=False, release=0.1), S(b) + jit(3), 1.0)
        tp.add(I.brass(below_tone(m, c), secs(b, 0.4), 0.9, 'trumpet', n_voices=2, vib=False, release=0.1), S(b) + jit(3), 1.0)
        tb.add(I.brass(m - 12, secs(b, 0.4), 0.95, 'trombone', n_voices=3, vib=False, release=0.1), S(b) + jit(3), 1.0)
        hn.add(I.brass(below_tone(below_tone(m, c), c), secs(b, 0.4), 0.9, 'horn', n_voices=2, vib=False,
                       release=0.1), S(b) + jit(3), 1.0)
    # rip up into the final hit (trumpets gliss on beat 4 of the fill bar)
    for m in [66, 70, 73, 78]:
        tp.add(I.brass(m, 2.6, 1.0, 'trumpet', n_voices=3, spread=0.5, release=0.6), S(FINAL_HIT), 1.0)
    for m in [54, 61, 66]:
        tb.add(I.brass(m, 2.6, 1.0, 'trombone', n_voices=3, release=0.6), S(FINAL_HIT), 1.0)
    for m in [58, 61, 66, 70]:
        hn.add(I.brass(m, 2.6, 1.0, 'horn', n_voices=2, spread=0.6, release=0.6), S(FINAL_HIT), 1.0)
    log('  stabs done')
    # synth lead doubles the hook (with glide between close notes)
    prev = None
    for b, d, m in hooks:
        g = prev if prev is not None and abs(prev - m) <= 5 else None
        ld.add(I.supersaw(m, secs(b, d) * 0.9, 0.55, voices=5, det_c=12, cut=3500, attack=0.005, release=0.12,
                          glide_from=g), S(b), 1.0)
        prev = m
    # bridge: filtered hook fragment on the lead (quiet, dark)
    for b, d, m in mel(hook[:4], BRIDGE):
        ld.add(I.supersaw(m - 12, secs(b, d) * 0.9, 0.35, voices=5, det_c=10, cut=1200, attack=0.02, release=0.3),
               S(b), 1.0)
    return tp, tb, hn, ld


def render_synths():
    pl = Track(N, 'pluck'); pad = Track(N, 'pad')
    # 16th pluck arpeggio in verse, build and bridge (broadcast "ticker" energy)
    for bar in list(range(VERSE, BUILD + 4)) + list(range(BRIDGE, FINAL)) + list(range(GROOVE + 4, HOOKA)):
        s = sec_at(B(bar))
        for k in range(16):
            b = B(bar, k / 4)
            c = chord_at(b)
            base = min([x for x in range(61, 73) if x % 12 == c['root']])
            tones = sorted(set(base + ((p - c['root']) % 12) for p in c['pcs']))
            seq = tones + [tones[0] + 12]
            order = [0, 2, 1, 3, 2, 1, 3, 0]
            m = seq[order[k % 8] % len(seq)]
            v = (0.75 if k % 4 == 0 else 0.5) * (0.8 if s == 'groove' else 1.0)
            pl.add(I.pluck(m, secs(b, 0.2), v, cut=2200 if s != 'bridge' else 1500, decay=0.12), S(b), 1.0,
                   0.35 if k % 2 else -0.35)
    # pad in verse + bridge
    for b, L, c, s in CH:
        if s in ('verse', 'bridge', 'build'):
            base = min([x for x in range(52, 64) if x % 12 == c['root']])
            ms = sorted(set(base + ((p - c['root']) % 12) for p in c['pcs']))
            pad.add(I.strings(ms, secs(b, L), 0.45, attack=0.4, release=0.8, n_voices=4, spread=0.9, bright=0.7),
                    S(b), 1.0)
    return pl, pad


# ============================================================================ main
ALL_GROUPS = {'drums', 'fx', 'gtr', 'bass', 'brass', 'synth'}
STEMS = ['kick', 'snare', 'hats', 'ride', 'toms', 'cymbals', 'fx', 'gtrL', 'gtrR', 'bass', 'trumpets', 'trombones', 'horns',
         'lead', 'pluck', 'pad']


def render_all(groups):
    log('N =', N / SR, 's', 'groups', groups)
    kt, sn, hh, tomt, kicks = render_drums()
    json.dump(kicks, open(os.path.join(store.path, 'kicks.json'), 'w'))
    if 'drums' in groups:
        store.save(kt, hp=30, bands=[('peak', 58, 1.0, 3.0), ('peak', 320, 1.2, -5), ('peak', 4000, 1.0, 2.5)],
                   comp=dict(thr_db=-12, ratio=4, att_ms=3, rel_ms=60))
        store.save(sn, hp=100, bands=[('peak', 210, 1.0, 3.0), ('peak', 800, 1.0, -2.0), ('peak', 2500, 1.0, -1.5),
                                      ('hs', 7000, 0.7, 2.0)], comp=dict(thr_db=-18, ratio=4, att_ms=5, rel_ms=90))
        store.save(hh, hp=4000)
        store.save(RIDE, hp=2500, bands=[('peak', 3000, 1.0, -3)])
        store.save(tomt, hp=60, bands=[('peak', 400, 1.0, -3)])
    del kt, sn, hh, tomt
    if 'fx' in groups:
        cy, fx = render_fx()
        store.save(cy, hp=400)
        store.save(fx, hp=28)
        del cy, fx
    if 'gtr' in groups:
        for tr in render_guitars():
            store.save(tr, hp=90, bands=[('peak', 2500, 1.0, -2.0)])
    if 'bass' in groups:
        sb = render_bass()
        store.save(sb, hp=30, lp=3000, bands=[('peak', 700, 1.0, 2.0)], comp=dict(thr_db=-14, ratio=4, att_ms=4, rel_ms=60))
        del sb
    if 'brass' in groups:
        tp, tb, hn, ld = render_brass()
        store.save(tp, hp=250, bands=[('peak', 1200, 1.0, 1.5), ('peak', 2800, 1.2, -2.5), ('hs', 6000, 0.7, 2.0)],
                   comp=dict(thr_db=-16, ratio=3, att_ms=8, rel_ms=100))
        store.save(tb, hp=90, bands=[('peak', 250, 1.0, 1.0), ('lp', 3500, 0.7, 0)],
                   comp=dict(thr_db=-16, ratio=3, att_ms=8, rel_ms=100))
        store.save(hn, hp=150, bands=[('peak', 2500, 1.0, -2)])
        store.save(ld, hp=300, bands=[('peak', 2500, 1.0, -3.0)])
        del tp, tb, hn, ld
    if 'synth' in groups:
        pl, pad = render_synths()
        store.save(pl, hp=250, bands=[('peak', 2200, 1.0, -3)])
        store.save(pad, hp=200, bands=[('peak', 2500, 0.8, -3)], width=1.4)
        del pl, pad


def A(*pts):
    return [(B(b), d) for b, d in pts]


MIXCFG = {
    'kick':      dict(bus='drums', gain=-5),
    'snare':     dict(bus='drums', gain=1, hall=0.25),
    'hats':      dict(bus='drums', gain=-2, hall=0.05),
    'ride':      dict(bus='drums', gain=-14, hall=0.1),
    'toms':      dict(bus='drums', gain=-8, hall=0.25),
    'cymbals':   dict(bus='drums', gain=-6, hall=0.15),
    'fx':        dict(bus='music', gain=-12, hall=0.2),
    'gtrL':      dict(bus='music', gain=-13, hall=0.08, duck=0.12, eq=[('hp', 110, 0.7, 0), ('peak', 240, 1.0, -3)]),
    'gtrR':      dict(bus='music', gain=-13, hall=0.08, duck=0.12, eq=[('hp', 110, 0.7, 0), ('peak', 240, 1.0, -3)]),
    'bass':      dict(bus='low', gain=-8, duck=0.5),
    'trumpets':  dict(bus='music', gain=-1, hall=0.25),
    'trombones': dict(bus='music', gain=-7, hall=0.25),
    'horns':     dict(bus='music', gain=-10, hall=0.3),
    'lead':      dict(bus='music', gain=-11, hall=0.2, dly=0.25),
    'pluck':     dict(bus='music', gain=-14, hall=0.15, dly=0.35, duck=0.2),
    'pad':       dict(bus='music', gain=-16, hall=0.35, duck=0.3),
}


def main(groups):
    if groups or not store.has(STEMS):
        render_all(groups or ALL_GROUPS)
    kicks = json.load(open(os.path.join(store.path, 'kicks.json')))
    duck_k = M.duck_env(N, kicks, depth=1.0, att=0.002, rel=0.12)
    ir = M.fdn_ir(rt60=1.7, rt60_hf=0.9, length_s=3.0, predelay_ms=18, size=0.9, seed=11, hf_cut=9000, lf_cut=150)
    total, rep = mixdown(store, MIXCFG, N, tm, duck_k,
                         bus_proc={'music': dict(thr_db=-18, ratio=2.5, att_ms=20, rel_ms=150),
                                   'drums': dict(thr_db=-14, ratio=3.5, att_ms=6, rel_ms=90)},
                         hall_ir=ir, returns=dict(hall_hp=200, hall_eq=[('peak', 2500, 0.8, -3.0)], hall_db=-8,
                                                  dly_db=-11, dly_fb=0.38),
                         delay_s=secs(0, 0.75), log=log,
                         master_auto=A((0, 0), (3.9, 0), (4, -1.5), (11.9, -1), (12, 0), (28.9, 0), (29, -3),
                                       (44.9, -2), (45, -2.5), (48.9, 0), (49, 0), (64.9, 0), (65, -5), (71, -4),
                                       (72.9, -1), (73, 0.5), (88, 0.5)))
    np.save(os.path.join(SCRATCH, 'rdl_premaster.npy'), total.astype(np.float32))
    log('premaster peak', 20 * np.log10(np.abs(total).max()))
    finish(total)


def finish(total):
    y = M.master(total, target_lufs=-14.0, ceiling_db=-1.6,
                 glue=dict(thr_db=-14, ratio=2.0, att_ms=20, rel_ms=150, knee_db=8),
                 eq_bands=[('peak', 2800, 0.7, -1.0), ('hs', 10000, 0.7, 1.0)], clip_drive=1.4)
    k = int(0.6 * SR)
    y[-k:] *= np.linspace(1, 0, k)[:, None] ** 2
    y[:32] *= np.linspace(0, 1, 32)[:, None]
    y = np.concatenate([np.zeros((int(0.08 * SR), 2), np.float32), y])   # tiny pre-roll so the opening hit is never clipped by decoders
    log('master LUFS', M.lufs(y), 'TP', M.true_peak_db(y))
    M.deliver_mp3(y, OUT, 'Race Day Live', log=log)
    log('wrote', OUT)


if __name__ == '__main__':
    arg = sys.argv[1] if len(sys.argv) > 1 else ''
    if arg == '--remaster':
        finish(np.load(os.path.join(SCRATCH, 'rdl_premaster.npy')))
    else:
        main(set() if arg == '--mix' else (set(arg.split(',')) if arg else ALL_GROUPS))
