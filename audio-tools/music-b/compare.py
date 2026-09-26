"""Old vs new comparison plot: LTAS (mid) and short-term loudness."""
import sys
import numpy as np
from scipy import signal
import matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
import analysis as A
files = sys.argv[2:]
fig, ax = plt.subplots(2, 1, figsize=(14, 9))
for f in files:
    x, sr = A.decode(f)
    m = x.mean(axis=1)
    fr, P = signal.welch(m, sr, nperseg=8192)
    Pd = 10 * np.log10(P + 1e-20); Pd -= Pd[(fr > 100) & (fr < 1000)].mean()
    ax[0].semilogx(fr[1:], Pd[1:], label=f.split('/')[-2] + '/' + f.split('/')[-1], lw=1)
    t, st = A.short_term(x, sr)
    ax[1].plot(t, st, label=f.split('/')[-1])
ax[0].set_xlim(30, 20000); ax[0].set_ylim(-70, 15); ax[0].grid(True, which='both', alpha=0.3); ax[0].legend()
ax[0].axvspan(1000, 4000, color='orange', alpha=0.1); ax[0].set_title('LTAS (normalised to 100-1k mean)')
ax[1].set_ylim(-40, -8); ax[1].grid(alpha=0.3); ax[1].legend(); ax[1].set_title('short-term loudness')
plt.tight_layout(); plt.savefig(sys.argv[1], dpi=60)
