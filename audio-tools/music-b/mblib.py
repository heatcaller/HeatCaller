"""Shared DSP / mixing / mastering library for the music-b theme tracks.

Everything is numpy/scipy; recursive processes are done block-wise (filters with carried state,
FDN reverb processed in blocks shorter than its shortest delay line, compressor envelopes on
32-sample decimated control signals) so a 3-minute track renders in a couple of minutes on 1 core.
"""
import subprocess
import numpy as np
from scipy import signal
from scipy.ndimage import minimum_filter1d, uniform_filter1d

SR = 44100
RNG = np.random.default_rng(20260922)

NOTE_IDX = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def midi(name):
    """'F#4' / 'Bb3' / 'C5' -> midi number (C4 = 60)."""
    if isinstance(name, (int, np.integer)):
        return int(name)
    n = NOTE_IDX[name[0].upper()]
    i = 1
    while i < len(name) and name[i] in '#b':
        n += 1 if name[i] == '#' else -1
        i += 1
    return n + 12 * (int(name[i:]) + 1)


def hz(m):
    return 440.0 * 2 ** ((np.asarray(m, dtype=float) - 69) / 12)


def parse_line(s, start=0.0, transpose=0):
    """'D4:1.5 A4:0.5 r:1 A4:2' -> list of (beat, dur, midi). Ties with '~' (e.g. 'A4:2~' joins next same pitch)."""
    out = []
    b = start
    for tok in s.split():
        p, d = tok.split(':')
        tie = d.endswith('~')
        d = float(eval(d.rstrip('~')))
        if p != 'r':
            m = midi(p) + transpose
            if out and out[-1][3] and out[-1][2] == m:
                ob, od, om, _ = out[-1]
                out[-1] = (ob, od + d, om, tie)
            else:
                out.append((b, d, m, tie))
        b += d
    return [(a, d, m) for a, d, m, _ in out]


# ----------------------------------------------------------------------------- oscillators
def _blep(t, dt):
    y = np.zeros_like(t)
    m = t < dt
    x = t[m] / dt[m]
    y[m] = x + x - x * x - 1
    m = t > 1 - dt
    x = (t[m] - 1) / dt[m]
    y[m] = x * x + x + x + 1
    return y


def saw(freq, n=None, phase0=None):
    """PolyBLEP band-limited sawtooth; freq scalar or per-sample array."""
    if np.isscalar(freq):
        freq = np.full(n, float(freq))
    if phase0 is None:
        phase0 = RNG.random()
    dt = freq / SR
    ph = (phase0 + np.cumsum(dt)) % 1.0
    return 2 * ph - 1 - _blep(ph, dt)


def pulse(freq, width=0.5, n=None, phase0=None):
    if np.isscalar(freq):
        freq = np.full(n, float(freq))
    if phase0 is None:
        phase0 = RNG.random()
    dt = freq / SR
    ph = (phase0 + np.cumsum(dt)) % 1.0
    ph2 = (ph + width) % 1.0
    s1 = 2 * ph - 1 - _blep(ph, dt)
    s2 = 2 * ph2 - 1 - _blep(ph2, dt)
    return 0.5 * (s1 - s2)


def sine(freq, n=None, phase0=0.0):
    if np.isscalar(freq):
        return np.sin(2 * np.pi * (freq * np.arange(n) / SR + phase0))
    return np.sin(2 * np.pi * (phase0 + np.cumsum(freq) / SR))


def drift(n, cents=4.0, rate=0.7):
    """Slow random pitch drift as a frequency multiplier (smoothed noise)."""
    k = max(4, int(n / SR * rate * 4) + 4)
    pts = RNG.standard_normal(k)
    x = np.interp(np.linspace(0, k - 1, n), np.arange(k), pts)
    return 2 ** (cents * x / 1200)


def noise(n):
    return RNG.standard_normal(n)


# ----------------------------------------------------------------------------- envelopes
def adsr(n, a, d, s, r, dur_s=None, curve=3.0):
    """Sample envelope of length n. note-off at dur_s (seconds), release r after that."""
    t = np.arange(n) / SR
    if dur_s is None:
        dur_s = n / SR - r
    e = np.empty(n)
    a = max(a, 1e-4)
    att = t < a
    e[att] = (t[att] / a) ** 0.8
    dec = (~att)
    e[dec] = s + (1 - s) * np.exp(-(t[dec] - a) * curve / max(d, 1e-4))
    # release
    off = t >= dur_s
    if off.any():
        i0 = np.argmax(off)
        lvl = e[i0 - 1] if i0 > 0 else 0.0
        e[off] = lvl * np.exp(-(t[off] - dur_s) * 6.9 / max(r, 1e-3))
    return e


def fade_edges(x, fi=0.002, fo=0.01):
    n = len(x)
    a = min(n, int(fi * SR)); b = min(n, int(fo * SR))
    if a > 0:
        x[:a] *= np.linspace(0, 1, a) if x.ndim == 1 else np.linspace(0, 1, a)[:, None]
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b) if x.ndim == 1 else np.linspace(1, 0, b)[:, None]
    return x


# ----------------------------------------------------------------------------- filters
def rbj(kind, f, q=0.707, gain_db=0.0):
    f = min(max(f, 10.0), SR * 0.49)
    A = 10 ** (gain_db / 40)
    w = 2 * np.pi * f / SR
    cw, sw = np.cos(w), np.sin(w)
    al = sw / (2 * q)
    if kind == 'lp':
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'hp':
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'bp':
        b = [al, 0, -al]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'peak':
        b = [1 + al * A, -2 * cw, 1 - al * A]; a = [1 + al / A, -2 * cw, 1 - al / A]
    elif kind == 'ls':
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * cw + sq), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sq)]
        a = [(A + 1) + (A - 1) * cw + sq, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sq]
    elif kind == 'hs':
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * cw + sq), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sq)]
        a = [(A + 1) - (A - 1) * cw + sq, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sq]
    else:
        raise ValueError(kind)
    b = np.array(b) / a[0]; a = np.array(a) / a[0]
    return np.concatenate([b, a])


def eq(x, bands):
    """bands: list of (kind, f, q, gain_db). Works on mono (n,) or stereo (n,2)."""
    if not bands:
        return x
    sos = np.array([rbj(k, f, q, g) for k, f, q, g in bands])
    return signal.sosfilt(sos, x, axis=0).astype(x.dtype, copy=False)


def hpf(x, f, order=2):
    sos = signal.butter(order, f, 'high', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0).astype(x.dtype, copy=False)


def lpf(x, f, order=2):
    sos = signal.butter(order, min(f, SR * 0.45), 'low', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0).astype(x.dtype, copy=False)


def bpf(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, min(hi, SR * 0.45)], 'band', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0).astype(x.dtype, copy=False)


def tv_filter(x, fc, q=0.707, kind='lp', block=64, stages=2):
    """Time-varying biquad (cascade) with per-block coefficients. x: (n,) or (c,n); fc: (n,) array."""
    x2 = np.atleast_2d(x)
    c, n = x2.shape
    y = np.empty_like(x2)
    zis = [np.zeros((c, 2)) for _ in range(stages)]
    fcs = fc[np.minimum(np.arange(0, n, block) + block // 2, n - 1)]
    for i, s in enumerate(range(0, n, block)):
        co = rbj(kind, fcs[i], q)
        b, a = co[:3], co[3:]
        seg = x2[:, s:s + block]
        for k in range(stages):
            seg, zis[k] = signal.lfilter(b, a, seg, axis=1, zi=zis[k])
        y[:, s:s + block] = seg
    return y[0] if np.ndim(x) == 1 else y


def allpass(x, m, g):
    """Schroeder allpass, delay m samples."""
    b = np.zeros(m + 1); a = np.zeros(m + 1)
    b[0] = -g; b[m] = 1
    a[0] = 1; a[m] = -g
    return signal.lfilter(b, a, x)


def karplus(f, dur, bright=0.5, decay=0.996, excite=None):
    """Karplus-Strong via a single IIR (lfilter). Loop: z^-N * avg * allpass-fracdelay * g."""
    n = int(dur * SR)
    P = SR / f
    N = int(np.floor(P - 0.5))
    frac = P - 0.5 - N  # remaining fractional delay for allpass
    if frac < 0.1:
        N -= 1; frac += 1
    c = (1 - frac) / (1 + frac)
    g = decay
    # H = (1 + c z^-1) / [(1 + c z^-1) - g/2 z^-N (1+z^-1)(c + z^-1)]
    den = np.zeros(N + 3)
    den[0] = 1; den[1] += c
    # (1+z^-1)(c+z^-1) = c + (1+c) z^-1 + z^-2
    den[N] -= g / 2 * c; den[N + 1] -= g / 2 * (1 + c); den[N + 2] -= g / 2 * 1
    num = np.array([1, c])
    if excite is None:
        L = N
        ex = noise(L)
        # brightness: lowpass the burst
        ex = lpf(ex, 500 + bright * 9000)
        excite = np.zeros(n); excite[:L] = ex * np.hanning(2 * L)[L:] ** 0.3
    return signal.lfilter(num, den, excite[:n])


# ----------------------------------------------------------------------------- stereo / placement
def pan(x, p):
    """constant-power pan, p in [-1,1]."""
    th = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(th), x * np.sin(th)], axis=-1) * np.sqrt(2)


def width(st, w):
    m = (st[:, 0] + st[:, 1]) / 2; s = (st[:, 0] - st[:, 1]) / 2 * w
    return np.stack([m + s, m - s], axis=-1)


def mono_below(st, f=120):
    """Make everything below f mono (M/S: high-pass the side channel)."""
    m = (st[:, 0] + st[:, 1]) / 2; s = (st[:, 0] - st[:, 1]) / 2
    s = hpf(s, f, 4)
    return np.stack([m + s, m - s], axis=-1).astype(st.dtype)


# ----------------------------------------------------------------------------- tempo map / tracks
class TempoMap:
    def __init__(self, bpm, nbeats, changes=None):
        """changes: list of (beat_from, beat_to, bpm_from, bpm_to) linear ramps."""
        spb = np.full(int(nbeats) + 64, 60.0 / bpm)
        for b0, b1, t0, t1 in (changes or []):
            for b in range(int(b0), int(b1)):
                fr = (b - b0 + 0.5) / (b1 - b0)
                spb[b] = 60.0 / (t0 + (t1 - t0) * fr)
            spb[int(b1):] = 60.0 / t1
        self.spb = spb
        self.cum = np.concatenate([[0], np.cumsum(spb)])

    def t(self, beat):
        beat = np.asarray(beat, dtype=float)
        i = np.floor(beat).astype(int)
        return self.cum[i] + (beat - i) * self.spb[i]

    def s(self, beat):
        return int(round(float(self.t(beat)) * SR))

    def dur(self, beat, nbeats):
        return float(self.t(beat + nbeats) - self.t(beat))


class Track:
    def __init__(self, n, name=''):
        self.buf = np.zeros((n, 2), dtype=np.float32)
        self.name = name

    def add(self, x, start, gain=1.0, p=0.0):
        if x.ndim == 1:
            x = pan(x, p)
        start = int(start)
        if start < 0:
            x = x[-start:]; start = 0
        n = min(len(x), len(self.buf) - start)
        if n > 0:
            self.buf[start:start + n] += (x[:n] * gain).astype(np.float32)


# ----------------------------------------------------------------------------- dynamics
def _env_blocks(x, blk):
    a = np.abs(x) if x.ndim == 1 else np.abs(x).max(axis=1)
    nb = int(np.ceil(len(a) / blk))
    pad = np.zeros(nb * blk); pad[:len(a)] = a
    return pad.reshape(nb, blk).max(axis=1)


def compress(x, thr_db=-18, ratio=3.0, att_ms=10, rel_ms=120, knee_db=6, makeup_db=0.0,
             sc=None, blk=32, return_gr=False):
    det = _env_blocks(sc if sc is not None else x, blk)
    lv = 20 * np.log10(det + 1e-9)
    # static curve with soft knee -> desired gain reduction (dB, <=0)
    over = lv - thr_db
    gr = np.where(over <= -knee_db / 2, 0.0,
                  np.where(over >= knee_db / 2, -(1 - 1 / ratio) * over,
                           -(1 - 1 / ratio) * (over + knee_db / 2) ** 2 / (2 * knee_db)))
    ca = np.exp(-blk / (SR * att_ms / 1000)); cr = np.exp(-blk / (SR * rel_ms / 1000))
    sm = np.empty_like(gr)
    g = 0.0
    for i in range(len(gr)):
        v = gr[i]
        c = ca if v < g else cr
        g = c * g + (1 - c) * v
        sm[i] = g
    t_blk = (np.arange(len(sm)) + 0.5) * blk
    gs = np.interp(np.arange(len(x)), t_blk, sm)
    gain = 10 ** ((gs + makeup_db) / 20)
    y = x * (gain[:, None] if x.ndim == 2 else gain)
    y = y.astype(x.dtype, copy=False)
    return (y, gs) if return_gr else y


def duck_env(n, times_s, depth=0.5, att=0.004, rel=0.18, shape=2.0):
    """Sidechain-style ducking gain curve from trigger times (seconds)."""
    L = int((att + rel * 1.6) * SR)
    t = np.arange(L) / SR
    k = np.where(t < att, t / att, np.exp(-((t - att) / rel) ** shape * 3.0))
    imp = np.zeros(n)
    for ts in times_s:
        i = int(ts * SR)
        if 0 <= i < n:
            imp[i] = 1.0
    d = signal.oaconvolve(imp, k)[:n]
    return 1 - depth * np.clip(d, 0, 1)


def softclip(x, drive=1.0):
    return np.tanh(x * drive) / np.tanh(drive)


def oversampled(fn, x, os=4, chunk=SR * 10):
    """Apply a static nonlinearity fn at os x rate, chunked (x: (n,) or (n,2))."""
    y = np.empty_like(x)
    n = len(x); pad = 64
    for s in range(0, n, chunk):
        a = max(0, s - pad); b = min(n, s + chunk + pad)
        u = signal.resample_poly(x[a:b], os, 1, axis=0)
        u = fn(u)
        d = signal.resample_poly(u, 1, os, axis=0)
        y[s:min(n, s + chunk)] = d[s - a:s - a + min(chunk, n - s)]
    return y


# ----------------------------------------------------------------------------- reverb / delay
def fdn_ir(rt60=2.8, rt60_hf=1.4, length_s=4.5, predelay_ms=20, n_lines=16, size=1.0, damp_hz=5000,
           er_gain=0.6, seed=1, hf_cut=9000, lf_cut=90):
    """Stereo impulse response of a 16-line Hadamard FDN with frequency-dependent decay,
    input allpass diffusion and a sparse early-reflection pattern. Returns (n,2) array."""
    rng = np.random.default_rng(seed)
    n = int(length_s * SR)
    base = np.array([1553, 1709, 1847, 1993, 2137, 2281, 2459, 2609, 2767, 2917, 3079, 3221,
                     3371, 3533, 3697, 3851])[:n_lines]
    dl = (base * size).astype(int)
    B = int(dl.min()) - 1
    # Hadamard
    H = np.array([[1.0]])
    while H.shape[0] < n_lines:
        H = np.block([[H, H], [H, -H]])
    H /= np.sqrt(n_lines)
    # per-line gains for broadband RT and a one-pole lowpass for HF RT
    g_dc = 10 ** (-3 * dl / (rt60 * SR))
    g_hf = 10 ** (-3 * dl / (rt60_hf * SR))
    # one-pole: y = (1-p) x + p y[-1]; gain at nyquist-ish ~ (1-p)/(1+p) -> choose p so HF gain ratio matches
    ratio = g_hf / g_dc
    p = (1 - ratio) / (1 + ratio)
    # input: impulse through diffusion allpasses (different per channel)
    imp = np.zeros(n); imp[0] = 1
    ins = []
    for ch in range(2):
        v = imp.copy()
        for m, gg in zip(rng.integers(113, 677, 4), [0.7, 0.65, 0.6, 0.55]):
            v = allpass(v, int(m), gg)
        ins.append(v)
    bin_ = rng.choice([-1, 1], (n_lines, 2)) * 0.5
    cL = rng.choice([-1, 1], n_lines) / np.sqrt(n_lines)
    cR = rng.choice([-1, 1], n_lines) / np.sqrt(n_lines)
    w = np.zeros((n_lines, n))
    y = np.zeros((n_lines, n))
    zi = np.zeros((n_lines, 1))
    inp = bin_[:, 0:1] * ins[0][None, :] + bin_[:, 1:2] * ins[1][None, :]
    for s in range(0, n, B):
        e = min(n, s + B)
        o = np.zeros((n_lines, e - s))
        for i in range(n_lines):
            a0 = s - dl[i]; a1 = e - dl[i]
            if a1 > 0:
                src = w[i, max(a0, 0):a1]
                o[i, (e - s) - len(src):] = src
        # absorption: gain * one-pole lowpass (vector over lines using lfilter per line)
        of = np.empty_like(o)
        for i in range(n_lines):
            of[i], zi[i] = signal.lfilter([g_dc[i] * (1 - p[i])], [1, -p[i]], o[i], zi=zi[i])
        y[:, s:e] = of
        w[:, s:e] = H @ of + inp[:, s:e]
    L = cL @ y; R = cR @ y
    ir = np.stack([L, R], axis=-1)
    # early reflections
    er = np.zeros((n, 2))
    for k in range(18):
        tms = rng.uniform(5, 80) * size
        i = int(tms / 1000 * SR)
        gg = er_gain * rng.uniform(0.3, 1.0) * np.exp(-tms / 60)
        er[i, rng.integers(0, 2)] += gg * rng.choice([-1, 1])
    er = lpf(er, 6000)
    ir = ir / (np.sqrt(np.mean(ir ** 2)) * np.sqrt(n) + 1e-12) + er * 0.5
    # smooth onset & tail fade, band-limit
    pre = int(predelay_ms / 1000 * SR)
    ir = np.concatenate([np.zeros((pre, 2)), ir])[:n]
    ir = hpf(lpf(ir, hf_cut), lf_cut)
    ir[-int(0.3 * SR):] *= np.linspace(1, 0, int(0.3 * SR))[:, None]
    ir /= np.sqrt(np.sum(ir ** 2) / 2)
    return ir


def convolve_stereo(x, ir):
    """x (n,2) float32 -> wet (n,2); L->L/R and R->R/L with slight cross-feed."""
    n = len(x)
    out = np.zeros((n, 2), dtype=np.float32)
    for ch in range(2):
        for oc in range(2):
            g = 1.0 if ch == oc else 0.35
            out[:, oc] += (g * signal.oaconvolve(x[:, ch].astype(np.float64), ir[:, oc if ch == 0 else 1 - oc])[:n]).astype(np.float32)
    return out


def pingpong(x, delay_s, fb=0.45, taps=6, lp_hz=3500, hp_hz=300):
    """Tempo-synced ping-pong delay (wet only). x (n,2)."""
    n = len(x)
    m = hpf(x.mean(axis=1), hp_hz)
    d = int(delay_s * SR)
    out = np.zeros((n, 2), dtype=np.float32)
    sig = m
    for k in range(1, taps + 1):
        sig = lpf(sig, lp_hz, 1)
        if k * d >= n:
            break
        out[k * d:, (k + 1) % 2] += (sig[:n - k * d] * fb ** (k - 1)).astype(np.float32)
    return out


# ----------------------------------------------------------------------------- loudness / mastering
def k_weight(x):
    f0 = 1681.974450955533; G = 3.999843853973347; Q = 0.7071752369554196
    K = np.tan(np.pi * f0 / SR); Vh = 10 ** (G / 20); Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = [(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0]
    a1 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    f0 = 38.13547087602444; Q = 0.5003270373238773
    K = np.tan(np.pi * f0 / SR); a0 = 1 + K / Q + K * K
    a2 = [1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0]
    y = signal.lfilter(b1, a1, x, axis=0)
    return signal.lfilter([1, -2, 1], a2, y, axis=0)


def lufs(x):
    y = k_weight(x.astype(np.float64))
    blk = int(0.4 * SR); hop = int(0.1 * SR)
    nb = 1 + (len(y) - blk) // hop
    p = (y ** 2).sum(axis=1)
    cs = np.concatenate([[0], np.cumsum(p)])
    idx = np.arange(nb) * hop
    z = (cs[idx + blk] - cs[idx]) / blk
    l = -0.691 + 10 * np.log10(z + 1e-20)
    g1 = z[l > -70]
    rel = -0.691 + 10 * np.log10(g1.mean()) - 10
    g2 = z[(l > -70) & (l > rel)]
    return -0.691 + 10 * np.log10(g2.mean())


def tp_env(x, os=4, chunk=SR * 10):
    """Per-sample true-peak envelope (max |x| of the os-times oversampled signal around each sample)."""
    n = len(x)
    out = np.empty(n)
    pad = 64
    for s in range(0, n, chunk):
        a = max(0, s - pad); b = min(n, s + chunk + pad)
        u = np.abs(signal.resample_poly(x[a:b].astype(np.float64), os, 1, axis=0)).max(axis=1)
        u = u.reshape(-1, os).max(axis=1)
        out[s:min(n, s + chunk)] = u[s - a:s - a + min(chunk, n - s)]
    return out


def true_peak_db(x):
    return 20 * np.log10(tp_env(x).max() + 1e-12)


def limiter(x, ceiling_db=-1.5, look_ms=1.5, rel_ms=120):
    """Look-ahead true-peak-aware brickwall limiter."""
    c = 10 ** (ceiling_db / 20)
    pk = tp_env(x)
    req = np.minimum(1.0, c / (pk + 1e-12))
    L = max(3, int(look_ms / 1000 * SR))
    gmin = minimum_filter1d(req, 2 * L + 1)
    # release smoothing on 16-sample blocks
    blk = 16
    nb = int(np.ceil(len(gmin) / blk))
    pad = np.ones(nb * blk); pad[:len(gmin)] = gmin
    gb = pad.reshape(nb, blk).min(axis=1)
    cr = np.exp(-blk / (SR * rel_ms / 1000))
    sm = np.empty(nb); g = 1.0
    for i in range(nb):
        v = gb[i]
        g = v if v < g else cr * g + (1 - cr) * v
        sm[i] = g
    gs = np.repeat(sm, blk)[:len(gmin)]
    gs = np.minimum(gs, gmin)
    gs = uniform_filter1d(gs, L + 1)
    gs = np.minimum(gs, minimum_filter1d(req, 2 * L + 1))  # safety
    return (x * gs[:, None]).astype(np.float32), 20 * np.log10(gs.min())


def master(mix, target_lufs=-14.0, ceiling_db=-1.5, glue=None, clip_drive=1.4, eq_bands=None, verbose=True):
    x = mix.astype(np.float32)
    x = x - x.mean(axis=0)
    x = hpf(x, 25, 2)
    if eq_bands:
        x = eq(x, eq_bands)
    x = mono_below(x, 130)
    if glue:
        x = compress(x, **glue)
    gain_db = target_lufs - lufs(x) + 2.0
    for it in range(4):
        y = x * 10 ** (gain_db / 20)
        # gentle oversampled soft clip for transients above ~ -3 dBFS
        th = 10 ** (-3 / 20)

        def sc(u):
            a = np.abs(u)
            over = a > th
            v = u.copy()
            v[over] = np.sign(u[over]) * (th + (1 - th) * np.tanh((a[over] - th) / (1 - th) * clip_drive) / clip_drive)
            return v
        y = oversampled(sc, y, os=4)
        y, gr = limiter(y, ceiling_db)
        l = lufs(y)
        if verbose:
            print(f'  master iter {it}: gain {gain_db:+.2f} dB -> {l:.2f} LUFS, max limiter GR {gr:.1f} dB')
        if abs(l - target_lufs) < 0.05:
            break
        gain_db += target_lufs - l
    return y


def write_mp3(x, path, title='', artist='HeatCaller'):
    x = np.clip(x, -1, 1).astype('<f4')
    subprocess.run(['ffmpeg', '-y', '-v', 'error', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-',
                    '-c:a', 'libmp3lame', '-b:a', '192k', '-ar', str(SR), '-id3v2_version', '3',
                    '-metadata', f'title={title}', '-metadata', f'artist={artist}', path],
                   input=x.tobytes(), check=True)


def write_wav16(x, path):
    from scipy.io import wavfile
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))


def decode_mp3(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2)


def deliver_mp3(y, path, title, target=-14.0, tp_max=-1.2, log=print):
    """Encode, decode, verify loudness / true peak of the actual MP3; correct the gain and re-encode.
    (LAME round-trips measured ~0.26 dB low on this material, so the correction matters.)"""
    y = y.astype(np.float32)
    ceiling = tp_max - 0.6
    for it in range(4):
        write_mp3(y, path, title=title)
        d = decode_mp3(path)
        l = lufs(d); tp = true_peak_db(d)
        log(f'  mp3 check {it}: {l:.2f} LUFS, TP {tp:.2f} dBTP')
        if abs(l - target) <= 0.05 and tp <= tp_max:
            return l, tp
        g = target - l
        y = y * np.float32(10 ** (g / 20))
        if tp + g > tp_max - 0.1:
            ceiling -= max(0.1, tp + g - (tp_max - 0.1))
            y, _ = limiter(y, ceiling)
    return l, tp
