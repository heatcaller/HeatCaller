#!/usr/bin/env python3
"""QC for rendered crowd loops: loudness, true peak, DC, clipping, seam,
stereo/mono checks, spectrograms and long-term spectra.  Usage:
    python3 analyze.py [Pack ...] [--png DIR]"""
import os
import sys
import json
import numpy as np
from scipy import signal
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dsp import FS, read_wav, lufs, true_peak_db, short_term_lufs  # noqa: E402
from packs import PACKS  # noqa: E402

AUDIO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(AUDIO, 'soundpacks')


def third_oct(f, P, centers):
    out = []
    for c in centers:
        m = (f >= c / 2 ** (1 / 6)) & (f < c * 2 ** (1 / 6))
        out.append(np.mean(P[..., m], axis=-1) if m.any() else np.full(P.shape[:-1], np.nan))
    return np.array(out).T


CENTERS = 1000 * 2 ** (np.arange(-15, 14) / 3)   # ~31 Hz .. 16 kHz


def stats(x, path):
    n = x.shape[1]
    st = {}
    st['dur'] = n / FS
    st['size_MB'] = os.path.getsize(path) / 1e6
    st['lufs'] = lufs(x)
    st['tp'] = true_peak_db(x)
    st['dc'] = [float(v) for v in x.mean(1)]
    st['clip'] = int(np.sum(np.abs(x) >= 32766 / 32768))
    L, R = x[0].astype(np.float64), x[1].astype(np.float64)
    st['corr'] = float(np.corrcoef(L, R)[0, 1])
    mid, side = (L + R) / 2, (L - R) / 2
    st['side_mid_db'] = float(10 * np.log10(np.mean(side ** 2) / np.mean(mid ** 2)))
    st['mono_lufs_drop'] = float(lufs(np.stack([mid, mid])) - st['lufs'])
    f, PL = signal.welch(L, FS, nperseg=4096)
    _, PR = signal.welch(R, FS, nperseg=4096)
    _, PM = signal.welch(mid, FS, nperseg=4096)
    band = third_oct(f, np.stack([PM, (PL + PR) / 2]), CENTERS[3:])   # 63 Hz+
    ratio = 10 * np.log10(band[0] / band[1])
    st['mono_band_min_db'] = float(np.nanmin(ratio))   # 0 = fully correlated, -3 = uncorrelated
    w = int(0.1 * FS)
    r_end = np.sqrt(np.mean(x[:, -w:].astype(np.float64) ** 2))
    r_beg = np.sqrt(np.mean(x[:, :w].astype(np.float64) ** 2))
    st['seam_rms_diff_db'] = float(20 * np.log10(r_end / r_beg))
    d_in = np.median(np.abs(np.diff(x, axis=1)))
    st['seam_jump_ratio'] = float(np.max(np.abs(x[:, 0] - x[:, -1])) / (d_in + 1e-9))
    ad = np.abs(np.diff(x, axis=1))
    sj = np.abs(x[:, 0] - x[:, -1])
    st['seam_jump_pctile'] = float(max(np.mean(ad[c] < sj[c]) for c in range(2)) * 100)
    # distribution of 100 ms RMS steps anywhere, for comparison with the seam
    blocks = np.sqrt(np.mean(x[:, : n // w * w].reshape(2, -1, w).astype(np.float64) ** 2, axis=(0, 2)))
    steps = np.abs(20 * np.log10(blocks[1:] / blocks[:-1]))
    st['typ_100ms_step_db_p50_p95'] = [float(np.percentile(steps, 50)), float(np.percentile(steps, 95))]
    return st, f, (PL + PR) / 2


def spec_img(ax, x, title, vmin=-95, vmax=-35):
    m = x.mean(0)
    f, t, S = signal.spectrogram(m, FS, nperseg=1024, noverlap=768, window='hann')
    ax.pcolormesh(t, f, 10 * np.log10(S + 1e-14), vmin=vmin, vmax=vmax, shading='auto', cmap='magma')
    ax.set_yscale('log')
    ax.set_ylim(50, 16000)
    ax.set_title(title, fontsize=9)
    ax.set_ylabel('Hz')


def analyze(pack, pngdir):
    res = {}
    fig = plt.figure(figsize=(16, 13))
    gs = fig.add_gridspec(4, 3)
    ltas = {}
    ax2 = fig.add_subplot(gs[3, 2])
    for i, (layer, fn) in enumerate([('bed', 'Ambience.wav'), ('roar', 'CrowdRoar.wav')]):
        p = os.path.join(OUT, pack, fn)
        _, x = read_wav(p)
        st, f, P = stats(x, p)
        res[layer] = st
        ltas[layer] = (f, P)
        spec_img(fig.add_subplot(gs[i, :2]), x, f'{pack} {fn}  {st["lufs"]:.1f} LUFS  TP {st["tp"]:.1f}  '
                 f'corr {st["corr"]:.2f}')
        seam = np.concatenate([x[:, -2 * FS:], x[:, :2 * FS]], axis=1)
        ax = fig.add_subplot(gs[i, 2])
        spec_img(ax, seam, f'seam (last 2 s | first 2 s)')
        ax.axvline(2.0, color='c', lw=0.8)
        t, s = short_term_lufs(x)
        res[layer]['st_lufs_range'] = [float(np.min(s)), float(np.max(s))]
        ax2.plot(t, s, label=layer)
        ax2.set_title('short-term loudness (3 s)')
        ax2.legend()
        ax2.grid(alpha=0.3)
    ax = fig.add_subplot(gs[2:, :2])
    for layer, (f, P) in ltas.items():
        b = third_oct(f, P, CENTERS)
        ax.semilogx(CENTERS, 10 * np.log10(b), marker='o', label=layer)
    ax.set_xlim(25, 16000)
    ax.grid(alpha=0.3, which='both')
    ax.legend()
    ax.set_title(f'{pack}: long-term average spectrum (1/3 oct, dB)')
    fig.tight_layout()
    os.makedirs(pngdir, exist_ok=True)
    fig.savefig(os.path.join(pngdir, f'{pack}.png'), dpi=60)
    plt.close(fig)
    return res


if __name__ == '__main__':
    args = sys.argv[1:]
    pngdir = os.path.join(AUDIO, 'tools', 'crowd', 'qc')
    if '--png' in args:
        i = args.index('--png')
        pngdir = args[i + 1]
        del args[i:i + 2]
    allres = {}
    for p in args or list(PACKS):
        r = analyze(p, pngdir)
        allres[p] = r
        for layer, st in r.items():
            print(p, layer, json.dumps({k: (round(v, 3) if isinstance(v, float) else v) for k, v in st.items()}))
