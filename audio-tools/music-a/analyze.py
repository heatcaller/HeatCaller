"""Analysis helpers: BS.1770-4 loudness, true peak, spectrogram / LTAS / per-bar RMS plots.

Usage:  python3 analyze.py <audio file> [bpm] [out_prefix]
"""
import sys, os, subprocess
import numpy as np
from scipy import signal

SR = 44100


def decode(path, sr=SR):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(sr), "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).T.astype(np.float64)


def k_weight_coeffs(sr):
    # ITU-R BS.1770 pre-filter (high shelf) + RLB high-pass, re-derived for arbitrary sr
    f0, G, Q = 1681.974450955533, 3.999843853973347, 0.7071752369554196
    K = np.tan(np.pi * f0 / sr)
    Vh = 10 ** (G / 20); Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = np.array([(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0])
    a1 = np.array([1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0])
    f0, Q = 38.13547087602444, 0.5003270373238773
    K = np.tan(np.pi * f0 / sr)
    a0 = 1 + K / Q + K * K
    b2 = np.array([1, -2, 1.0])
    a2 = np.array([1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0])
    return (b1, a1), (b2, a2)


def lufs(x, sr=SR, return_short=False):
    """Integrated loudness (gated), x shape (ch, n)."""
    (b1, a1), (b2, a2) = k_weight_coeffs(sr)
    y = signal.lfilter(b2, a2, signal.lfilter(b1, a1, x, axis=-1), axis=-1)
    blk = int(0.4 * sr); hop = int(0.1 * sr)
    n = (y.shape[1] - blk) // hop + 1
    if n < 1:
        return -np.inf
    sq = np.cumsum(np.concatenate([np.zeros((y.shape[0], 1)), y ** 2], axis=1), axis=1)
    idx = np.arange(n) * hop
    ms = (sq[:, idx + blk] - sq[:, idx]) / blk  # (ch, n)
    z = ms.sum(0)
    l = -0.691 + 10 * np.log10(z + 1e-20)
    g = z[l > -70]
    if len(g) == 0:
        return -np.inf
    lr = -0.691 + 10 * np.log10(g.mean()) - 10
    g2 = z[(l > -70) & (l > lr)]
    I = -0.691 + 10 * np.log10(g2.mean())
    if return_short:
        # 3 s short-term loudness, 1 s hop
        blk3 = 3 * sr; hop3 = sr
        n3 = max(1, (y.shape[1] - blk3) // hop3 + 1)
        i3 = np.arange(n3) * hop3
        z3 = ((sq[:, np.minimum(i3 + blk3, sq.shape[1] - 1)] - sq[:, i3]) / blk3).sum(0)
        return I, -0.691 + 10 * np.log10(z3 + 1e-20)
    return I


def true_peak_db(x, os_=4):
    m = 0.0
    step = SR * 10
    for s in range(0, x.shape[1], step):
        seg = x[:, max(0, s - 64): s + step + 64]
        u = signal.resample_poly(seg, os_, 1, axis=-1)
        m = max(m, np.abs(u).max())
    return 20 * np.log10(m + 1e-20)


def stats(x, sr=SR, bpm=None):
    L, st = lufs(x, sr, return_short=True)
    tp = true_peak_db(x)
    sp = 20 * np.log10(np.abs(x).max() + 1e-20)
    m = 0.5 * (x[0] + x[1]); s = 0.5 * (x[0] - x[1])
    corr = np.sum(x[0] * x[1]) / np.sqrt(np.sum(x[0] ** 2) * np.sum(x[1] ** 2) + 1e-20)
    sos = signal.butter(4, 120, 'low', fs=sr, output='sos')
    ml = signal.sosfilt(sos, m); sl = signal.sosfilt(sos, s)
    low_side = 20 * np.log10(np.sqrt(np.mean(sl ** 2)) / (np.sqrt(np.mean(ml ** 2)) + 1e-20) + 1e-20)
    sos2 = signal.butter(4, [1000, 4000], 'band', fs=sr, output='sos')
    pres = signal.sosfilt(sos2, m)
    pres_ratio = 10 * np.log10(np.mean(pres ** 2) / np.mean(m ** 2))
    # PLR / crest
    rms = 10 * np.log10(np.mean(x ** 2) + 1e-20)
    lra = np.percentile(st[st > -70], 95) - np.percentile(st[st > -70], 10) if np.any(st > -70) else 0
    return dict(dur=x.shape[1] / sr, lufs=L, true_peak=tp, sample_peak=sp, dc=[float(np.mean(x[0])), float(np.mean(x[1]))],
                corr=corr, low_side_vs_mid_db=low_side, presence_1_4k_db=pres_ratio, rms_db=rms,
                plr=tp - L, lra=lra, clipped=int(np.sum(np.abs(x) >= 0.9999)))


def plots(x, out_prefix, sr=SR, bpm=None, title=""):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    m = 0.5 * (x[0] + x[1])
    fig, axs = plt.subplots(3, 1, figsize=(16, 13))
    # spectrogram (log freq)
    f, t, S = signal.spectrogram(m, sr, nperseg=4096, noverlap=2048, window='hann')
    SdB = 10 * np.log10(S + 1e-14)
    ax = axs[0]
    ax.pcolormesh(t, f, SdB, vmin=SdB.max() - 90, vmax=SdB.max(), shading='auto', cmap='magma')
    ax.set_yscale('symlog', linthresh=100); ax.set_ylim(20, sr / 2)
    ax.set_title(f"{title} spectrogram (mid)"); ax.set_xlabel("s")
    # per bar RMS
    ax = axs[1]
    if bpm:
        bar = 4 * 60 / bpm
    else:
        bar = 2.0
    nb = int(np.ceil(x.shape[1] / (bar * sr)))
    r = []; rl = []; rh = []
    sosl = signal.butter(4, 150, 'low', fs=sr, output='sos'); sosh = signal.butter(4, 4000, 'high', fs=sr, output='sos')
    ml = signal.sosfilt(sosl, m); mh = signal.sosfilt(sosh, m)
    for i in range(nb):
        a = int(i * bar * sr); b = int((i + 1) * bar * sr)
        r.append(10 * np.log10(np.mean(m[a:b] ** 2) + 1e-12))
        rl.append(10 * np.log10(np.mean(ml[a:b] ** 2) + 1e-12))
        rh.append(10 * np.log10(np.mean(mh[a:b] ** 2) + 1e-12))
    ax.bar(np.arange(nb), np.array(r) + 60, color='C0', label='full', bottom=-60, alpha=.6)
    ax.plot(np.arange(nb), rl, 'C1.-', label='<150 Hz')
    ax.plot(np.arange(nb), rh, 'C2.-', label='>4 kHz')
    ax.set_ylim(-60, 0); ax.legend(); ax.grid(alpha=.3)
    ax.set_title(f"RMS per bar ({bar:.3f}s bars)"); ax.set_xlabel("bar")
    # LTAS
    ax = axs[2]
    f, P = signal.welch(m, sr, nperseg=8192)
    f2, Ps = signal.welch(0.5 * (x[0] - x[1]), sr, nperseg=8192)
    # 1/6-octave smoothing
    fc = 20 * 2 ** (np.arange(0, 60) / 6); fc = fc[fc < sr / 2]
    sm = []; ss = []
    for c in fc:
        sel = (f >= c / 2 ** (1 / 12)) & (f < c * 2 ** (1 / 12))
        sm.append(10 * np.log10(P[sel].mean() + 1e-20) if sel.any() else np.nan)
        ss.append(10 * np.log10(Ps[sel].mean() + 1e-20) if sel.any() else np.nan)
    ax.semilogx(fc, sm, label='mid'); ax.semilogx(fc, ss, label='side')
    ax.axvspan(1000, 4000, color='y', alpha=.15, label='announcer region')
    ax.grid(alpha=.3, which='both'); ax.legend(); ax.set_title("LTAS (1/6 oct)"); ax.set_xlim(20, 20000)
    plt.tight_layout(); plt.savefig(out_prefix + ".png", dpi=60); plt.close(fig)
    return fc, np.array(sm)


if __name__ == "__main__":
    p = sys.argv[1]
    bpm = float(sys.argv[2]) if len(sys.argv) > 2 else None
    out = sys.argv[3] if len(sys.argv) > 3 else "/tmp/an"
    x = decode(p)
    s = stats(x, bpm=bpm)
    for k, v in s.items():
        print(f"{k:22s} {v}")
    plots(x, out, bpm=bpm, title=os.path.basename(p))
    print("plot", out + ".png")
