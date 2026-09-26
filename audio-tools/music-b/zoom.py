"""Zoomed spectrogram of a file or a stem: python3 zoom.py <file.mp3|stem.npy> t0 t1 fmax out.png"""
import sys
import numpy as np
from scipy import signal
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
src, t0, t1, fmax, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
SR = 44100
if src.endswith('.npy'):
    x = np.load(src, mmap_mode='r')[int(t0 * SR):int(t1 * SR)].astype(np.float64)
else:
    import analysis as A
    x, _ = A.decode(src); x = x[int(t0 * SR):int(t1 * SR)]
m = x.mean(axis=1)
f, t, S = signal.spectrogram(m, SR, nperseg=2048, noverlap=1792)
Sd = 10 * np.log10(S + 1e-14)
fig, ax = plt.subplots(2, 1, figsize=(16, 10), gridspec_kw={'height_ratios': [3, 1]})
ax[0].pcolormesh(t + t0, f, Sd, vmin=Sd.max() - 80, vmax=Sd.max(), shading='auto', cmap='magma')
ax[0].set_ylim(0, fmax)
ax[1].plot(np.arange(len(m)) / SR + t0, x[:, 0], lw=0.3); ax[1].plot(np.arange(len(m)) / SR + t0, x[:, 1], lw=0.3, alpha=0.6)
ax[1].set_xlim(t0, t1)
plt.tight_layout(); plt.savefig(out, dpi=55)
