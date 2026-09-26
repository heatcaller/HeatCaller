"""Chord symbols, voice leading, patterns."""
import itertools
import numpy as np

PC = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
QUAL = {'': [0, 4, 7], 'm': [0, 3, 7], '7': [0, 4, 10, 7], 'm7': [0, 3, 10, 7], 'maj7': [0, 4, 11, 7],
        'sus4': [0, 5, 7], 'sus2': [0, 2, 7], '7sus4': [0, 5, 10, 7], 'add9': [0, 4, 2, 7], '5': [0, 7],
        'dim': [0, 3, 6], 'm9': [0, 3, 10, 2, 7]}


def _pc(s):
    v = PC[s[0]]
    for ch in s[1:]:
        v += 1 if ch == '#' else -1
    return v % 12


def chord(sym, transpose=0):
    """'F#m/A' -> dict(root, pcs(ordered: root, 3rd/sus, 7th.., 5th), bass pc)."""
    body, _, bass = sym.partition('/')
    i = 1
    while i < len(body) and body[i] in '#b':
        i += 1
    root = (_pc(body[:i]) + transpose) % 12
    q = body[i:]
    ivs = QUAL[q]
    pcs = [(root + x) % 12 for x in ivs]
    b = (_pc(bass) + transpose) % 12 if bass else root
    return {'root': root, 'pcs': pcs, 'bass': b, 'sym': sym}


def expand(bars, transpose=0):
    """list of bar strings ('D', 'Dsus4|D') -> list of (bar_index, beat_offset, beats, chord)."""
    out = []
    for bi, s in enumerate(bars):
        parts = s.split('|')
        L = 4 / len(parts)
        for k, p in enumerate(parts):
            out.append((bi, k * L, L, chord(p, transpose)))
    return out


def nearest(pc, lo, hi, target):
    cands = [m for m in range(lo, hi + 1) if m % 12 == pc]
    return min(cands, key=lambda m: abs(m - target))


def voice_lead(chords, ranges, start=None, max_gap=12):
    """chords: list of chord dicts; ranges: [(lo,hi)] low->high. Returns list of tuples of midi."""
    nv = len(ranges)
    prev = start or [int((lo + hi) / 2) for lo, hi in ranges]
    res = []
    for c in chords:
        pcs = c['pcs']
        cand = [[m for m in range(lo, hi + 1) if m % 12 in pcs] for lo, hi in ranges]
        best, bc = None, 1e9
        for combo in itertools.product(*cand):
            if any(combo[i] >= combo[i + 1] for i in range(nv - 1)):
                continue
            if any(combo[i + 1] - combo[i] > max_gap for i in range(nv - 1)):
                continue
            have = [m % 12 for m in combo]
            essential = pcs[:3] if len(pcs) >= 4 else pcs[:2]
            if any(p not in have for p in essential):
                continue
            cost = sum(abs(a - b) for a, b in zip(combo, prev))
            if len(pcs) >= 3 and pcs[-1] not in have:
                cost += 1.0
            if have.count(pcs[1]) > 1:
                cost += 3.0
            # parallel fifths / octaves
            for i in range(nv):
                for j in range(i + 1, nv):
                    a0 = (prev[j] - prev[i]) % 12; a1 = (combo[j] - combo[i]) % 12
                    if a0 == a1 and a1 in (0, 7) and combo[i] != prev[i]:
                        cost += 4.0
            if cost < bc:
                bc, best = cost, combo
        if best is None:
            best = tuple(nearest(pcs[i % len(pcs)], lo, hi, p) for i, ((lo, hi), p) in enumerate(zip(ranges, prev)))
        res.append(best)
        prev = best
    return res


def steps(pat):
    """'x..x' -> indices of hits; 'X' accent (vel 1), 'x' (0.8), 'o' ghost (0.45)."""
    out = []
    for i, ch in enumerate(pat):
        if ch in 'Xxo':
            out.append((i, {'X': 1.0, 'x': 0.8, 'o': 0.45}[ch]))
    return out
