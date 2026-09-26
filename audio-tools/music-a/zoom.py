"""Zoomed spectrogram + waveform of a time window: python3 zoom.py file.mp3 t0 t1 out.png"""
import sys
import numpy as np
from scipy import signal
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from analyze import decode, SR

x = decode(sys.argv[1]); t0, t1 = float(sys.argv[2]), float(sys.argv[3])
seg = x[:, int(t0 * SR):int(t1 * SR)]
m = seg.mean(0)
fig, axs = plt.subplots(2, 1, figsize=(16, 10), gridspec_kw=dict(height_ratios=[3, 1]))
f, t, S = signal.spectrogram(m, SR, nperseg=2048, noverlap=1792)
SdB = 10 * np.log10(S + 1e-14)
axs[0].pcolormesh(t + t0, f, SdB, vmin=SdB.max() - 80, vmax=SdB.max(), shading='auto', cmap='magma')
axs[0].set_yscale('symlog', linthresh=200); axs[0].set_ylim(30, 20000)
axs[0].set_title(f"{sys.argv[1].split('/')[-1]} {t0}-{t1}s")
tt = t0 + np.arange(seg.shape[1]) / SR
axs[1].plot(tt, seg[0], lw=.3); axs[1].plot(tt, seg[1], lw=.3, alpha=.6); axs[1].set_xlim(t0, t1)
plt.tight_layout(); plt.savefig(sys.argv[4], dpi=60)
