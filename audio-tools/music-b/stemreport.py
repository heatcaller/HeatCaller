"""Per-stem band-energy report (with mixdown gains) to see who owns which band."""
import sys, os, importlib
import numpy as np
from scipy import signal
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
mod = importlib.import_module(sys.argv[1])
t0, t1 = float(sys.argv[2]), float(sys.argv[3])
SR = 44100
bands = [(20, 120), (120, 300), (300, 1000), (1000, 4000), (4000, 16000)]
print(f"{'stem':14s}" + ''.join(f'{a}-{b}'.rjust(11) for a, b in bands))
tot = np.zeros(len(bands))
rows = []
for name, c in mod.MIXCFG.items():
    x = np.array(mod.store.load(name)[int(t0 * SR):int(t1 * SR)], dtype=np.float64) * 10 ** (c.get('gain', 0) / 20)
    m = x.mean(axis=1)
    f, P = signal.welch(m, SR, nperseg=8192)
    e = np.array([P[(f >= a) & (f < b)].sum() for a, b in bands]) + 1e-20
    tot += e
    rows.append((name, e))
for name, e in rows:
    print(f'{name:14s}' + ''.join(f'{10*np.log10(v):11.1f}' for v in e))
print(f"{'TOTAL':14s}" + ''.join(f'{10*np.log10(v):11.1f}' for v in tot))
