"""Per-bar chroma of a rendered track vs the intended chord (sanity check for harmony/timing).
python3 chroma_check.py file.mp3 bpm module   (module exposes prog + CH)"""
import sys, importlib
import numpy as np
from scipy import signal
from analyze import decode, SR

NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
x = decode(sys.argv[1]).mean(0)
bpm = float(sys.argv[2])
chords = [l.split() for l in open(sys.argv[3]).read().strip().split('\n')]  # per bar: chord tone pitch classes
bar = 4 * 60 / bpm
f, t, Z = signal.stft(x, SR, nperseg=8192, noverlap=6144)
P = np.abs(Z) ** 2
sel = (f > 100) & (f < 2000)
midi = 69 + 12 * np.log2(f[sel] / 440)
pc = np.round(midi).astype(int) % 12
C = np.zeros((12, len(t)))
for k in range(12):
    C[k] = P[sel][pc == k].sum(0)
ok = 0
for i, tones in enumerate(chords):
    m = (t >= i * bar) & (t < (i + 1) * bar)
    if not m.any():
        continue
    v = C[:, m].sum(1); v /= v.sum() + 1e-20
    top = [NAMES[j] for j in np.argsort(v)[::-1][:4]]
    tp = set(int(q) for q in tones)
    frac = sum(v[j] for j in tp)
    ok += frac > 0.5
    print(f"bar {i:3d} want {' '.join(NAMES[int(q)] for q in tones):10s} top {' '.join(top):14s} chord-tone energy {frac:.2f}")
print(f"{ok}/{len(chords)} bars with >50% energy on chord tones")
