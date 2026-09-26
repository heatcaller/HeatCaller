"""Analysis helpers: BS.1770-4 loudness, true peak, spectrogram / LTAS / per-bar RMS plots.

Usage: python3 analysis.py <audio file> <out prefix> [bpm] [beats_per_bar]
"""
import sys, subprocess, json
import numpy as np
from scipy import signal


def decode(path, sr=44100):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-f', 'f32le', '-ac', '2',
                          '-ar', str(sr), '-'], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).astype(np.float64), sr


def k_weight(x, sr):
    """ITU-R BS.1770 K-weighting (pre-filter shelf + RLB high-pass), coefficients derived for any sr."""
    # Stage 1: high shelf
    f0 = 1681.974450955533; G = 3.999843853973347; Q = 0.7071752369554196
    K = np.tan(np.pi * f0 / sr); Vh = 10 ** (G / 20); Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = [(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0]
    a1 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    # Stage 2: RLB high-pass
    f0 = 38.13547087602444; Q = 0.5003270373238773
    K = np.tan(np.pi * f0 / sr)
    a0 = 1 + K / Q + K * K
    b2 = [1, -2, 1]
    a2 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    y = signal.lfilter(b1, a1, x, axis=0)
    return signal.lfilter(b2, a2, y, axis=0)


def lufs_integrated(x, sr):
    y = k_weight(x, sr)
    blk = int(0.4 * sr); hop = int(0.1 * sr)
    n = 1 + (len(y) - blk) // hop
    if n < 1:
        return -np.inf
    p = y ** 2
    cs = np.concatenate([np.zeros((1, p.shape[1])), np.cumsum(p, axis=0)])
    idx = np.arange(n) * hop
    ms = (cs[idx + blk] - cs[idx]) / blk  # (n, ch)
    z = ms.sum(axis=1)
    l = -0.691 + 10 * np.log10(z + 1e-20)
    g1 = z[l > -70]
    if len(g1) == 0:
        return -np.inf
    rel = -0.691 + 10 * np.log10(g1.mean()) - 10
    g2 = z[(l > -70) & (l > rel)]
    return -0.691 + 10 * np.log10(g2.mean())


def short_term(x, sr, win=3.0, hop=0.5):
    y = k_weight(x, sr); p = (y ** 2).sum(axis=1)
    blk = int(win * sr); h = int(hop * sr)
    cs = np.concatenate([[0], np.cumsum(p)])
    idx = np.arange(0, len(p) - blk, h)
    return idx / sr, -0.691 + 10 * np.log10((cs[idx + blk] - cs[idx]) / blk + 1e-20)


def true_peak(x, os=4):
    tp = 0
    for ch in range(x.shape[1]):
        # chunked polyphase resample to limit memory
        n = len(x); step = sr_chunk = 441000
        for s in range(0, n, step):
            a = max(0, s - 256); b = min(n, s + step + 256)
            y = signal.resample_poly(x[a:b, ch], os, 1)
            y = y[(s - a) * os:(s - a) * os + min(step, n - s) * os]
            tp = max(tp, np.abs(y).max())
    return 20 * np.log10(tp + 1e-20)


def report(x, sr, bpm=None, bpb=4):
    r = {}
    r['length_s'] = len(x) / sr
    r['lufs_i'] = lufs_integrated(x, sr)
    r['sample_peak_db'] = 20 * np.log10(np.abs(x).max() + 1e-20)
    r['true_peak_db'] = true_peak(x)
    r['dc'] = [float(x[:, 0].mean()), float(x[:, 1].mean())]
    L, R = x[:, 0], x[:, 1]
    r['corr'] = float(np.sum(L * R) / np.sqrt(np.sum(L * L) * np.sum(R * R) + 1e-20))
    sos = signal.butter(4, 120, 'low', fs=sr, output='sos')
    lo = signal.sosfilt(sos, x, axis=0)
    m = (lo[:, 0] + lo[:, 1]) / 2; s = (lo[:, 0] - lo[:, 1]) / 2
    r['low_side_to_mid_db'] = float(10 * np.log10((s ** 2).sum() / ((m ** 2).sum() + 1e-20) + 1e-20))
    M = (L + R) / 2; S = (L - R) / 2
    r['side_to_mid_db'] = float(10 * np.log10((S ** 2).sum() / (M ** 2).sum()))
    t, st = short_term(x, sr)
    r['st_max'] = float(st.max()); r['st_min_after_5s'] = float(st[t > 5].min()) if (t > 5).any() else None
    # crude LRA: 10th-95th pct of short-term gated
    g = st[st > r['lufs_i'] - 20]
    r['lra'] = float(np.percentile(g, 95) - np.percentile(g, 10))
    # crest (peak to rms)
    r['crest_db'] = float(r['sample_peak_db'] - 10 * np.log10(np.mean(x ** 2)))
    # band energies
    f, P = signal.welch(M, sr, nperseg=8192)
    tot = P.sum()
    bands = [(20, 60), (60, 120), (120, 250), (250, 500), (500, 1000), (1000, 4000), (4000, 8000), (8000, 16000), (16000, 22050)]
    r['bands_pct'] = {f'{a}-{b}': round(100 * P[(f >= a) & (f < b)].sum() / tot, 1) for a, b in bands}
    return r


def plots(x, sr, prefix, bpm=None, bpb=4, title=''):
    import matplotlib; matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    M = x.mean(axis=1)
    fig, ax = plt.subplots(4, 1, figsize=(16, 16))
    # spectrogram (downsample the time axis)
    f, t, S = signal.spectrogram(M, sr, nperseg=4096, noverlap=2048)
    Sd = 10 * np.log10(S + 1e-14)
    ax[0].pcolormesh(t, f, Sd, vmin=Sd.max() - 90, vmax=Sd.max(), shading='auto', cmap='magma')
    ax[0].set_yscale('symlog', linthresh=200); ax[0].set_ylim(30, 20000)
    ax[0].set_title(f'{title} spectrogram (mid)')
    # LTAS
    f2, P = signal.welch(M, sr, nperseg=16384)
    ax[1].semilogx(f2[1:], 10 * np.log10(P[1:] + 1e-20))
    Sx = (x[:, 0] - x[:, 1]) / 2
    f3, P3 = signal.welch(Sx, sr, nperseg=16384)
    ax[1].semilogx(f3[1:], 10 * np.log10(P3[1:] + 1e-20), alpha=0.6)
    ax[1].legend(['mid', 'side']); ax[1].set_xlim(20, 20000); ax[1].grid(True, which='both', alpha=0.3)
    ax[1].axvspan(1000, 4000, color='orange', alpha=0.1)
    ax[1].set_title('LTAS (mid vs side); shaded = announcer 1-4k')
    # per bar RMS
    if bpm:
        bl = int(round(60 / bpm * bpb * sr))
    else:
        bl = sr * 2
    nb = len(x) // bl
    rms = [20 * np.log10(np.sqrt(np.mean(x[i * bl:(i + 1) * bl] ** 2)) + 1e-12) for i in range(nb)]
    ax[2].bar(np.arange(nb), np.array(rms)+60, bottom=-60)
    ax[2].set_ylim(-45, 0); ax[2].set_title('RMS per bar (dBFS)' if bpm else 'RMS per 2 s')
    ax[2].grid(alpha=0.3)
    ts, st = short_term(x, sr)
    ax[3].plot(ts, st); ax[3].set_ylim(-40, -5); ax[3].grid(alpha=0.3)
    ax[3].set_title('short-term loudness (LUFS, 3 s)')
    plt.tight_layout(); plt.savefig(prefix + '_analysis.png', dpi=60); plt.close()


if __name__ == '__main__':
    path, prefix = sys.argv[1], sys.argv[2]
    bpm = float(sys.argv[3]) if len(sys.argv) > 3 else None
    x, sr = decode(path)
    r = report(x, sr, bpm)
    print(json.dumps(r, indent=1))
    plots(x, sr, prefix, bpm, title=path.split('/')[-1])
