"""Shared DSP toolkit for the music-a generators (numpy/scipy only).

Band-limited oscillators (PolyBLEP), envelopes, block-time-varying resonant filters,
RBJ EQ, chorus, FDN-generated reverb impulse responses, tempo delays, compressors,
true-peak limiter, BS.1770 loudness normalisation, MP3 export.
"""
import subprocess
import numpy as np
from scipy import signal, ndimage

SR = 44100
rng = np.random.default_rng(1234)


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=np.float64) - 69.0) / 12.0)


def db(x):
    return 10 ** (x / 20.0)


# ---------------------------------------------------------------- oscillators
def _phase(freq, n, phase0=0.0):
    f = np.broadcast_to(np.asarray(freq, dtype=np.float64), (n,))
    dt = f / SR
    ph = (phase0 + np.cumsum(dt) - dt[0]) % 1.0
    return ph, dt


def _blep(t, dt):
    """PolyBLEP residual for a unit step at phase 0 (vectorised)."""
    r = np.zeros_like(t)
    m = t < dt
    x = t[m] / dt[m]
    r[m] = x + x - x * x - 1.0
    m2 = t > 1.0 - dt
    x = (t[m2] - 1.0) / dt[m2]
    r[m2] = x * x + x + x + 1.0
    return r


def saw(freq, n, phase0=None):
    if phase0 is None:
        phase0 = rng.random()
    ph, dt = _phase(freq, n, phase0)
    return 2.0 * ph - 1.0 - _blep(ph, dt)


def pulse(freq, n, duty=0.5, phase0=None):
    if phase0 is None:
        phase0 = rng.random()
    ph, dt = _phase(freq, n, phase0)
    duty = np.broadcast_to(np.asarray(duty, dtype=np.float64), (n,))
    y = np.where(ph < duty, 1.0, -1.0)
    y += _blep(ph, dt)
    ph2 = (ph - duty) % 1.0
    y -= _blep(ph2, dt)
    return y - (2 * duty - 1)  # remove DC of the pulse


def triangle(freq, n, phase0=None):
    """Band-limited triangle by leaky integration of a PolyBLEP square."""
    f = np.broadcast_to(np.asarray(freq, dtype=np.float64), (n,))
    sq = pulse(f, n, 0.5, phase0)
    tri = signal.lfilter([1.0], [1.0, -0.9995], sq * 4.0 * f / SR)
    tri = signal.lfilter([1, -1], [1, -0.9995], tri)  # remove residual DC drift
    return tri / (np.max(np.abs(tri)) + 1e-9)


def sine(freq, n, phase0=0.0):
    ph, _ = _phase(freq, n, phase0)
    return np.sin(2 * np.pi * ph)


def supersaw(freq, n, voices=7, detune=0.18, mix=0.75, drift=0.0015):
    """JP-8000-ish supersaw: centre saw + detuned side saws, each with slow random drift.
    Returns (left, right)."""
    offs = np.array([0, -0.11002313, 0.11002313, -0.06288439, 0.06288439, -0.01952356, 0.01952356])[:voices]
    offs = offs * detune
    t = np.arange(n) / SR
    L = np.zeros(n); R = np.zeros(n)
    for i, o in enumerate(offs):
        dr = 1 + drift * np.sin(2 * np.pi * (0.07 + 0.05 * rng.random()) * t + rng.random() * 6.28)
        s = saw(freq * (1 + o) * dr, n)
        g = (1 - mix) if i == 0 else mix / (voices - 1) * 1.6
        if i == 0:
            L += g * s; R += g * s
        else:
            p = 0.5 + (0.45 if i % 2 else -0.45) * (1 if i < 5 else 0.6)
            L += g * s * np.cos(p * np.pi / 2); R += g * s * np.sin(p * np.pi / 2)
    return L, R


def lfsr_noise(n, clock_hz, short=False, seed=1):
    """NES-style 15-bit LFSR noise, sample-and-hold at clock_hz. short=True -> 93-step metallic mode."""
    period = 93 if short else 32767
    reg = seed & 0x7FFF or 1
    seq = np.empty(period, dtype=np.float64)
    tap = 6 if short else 1
    for i in range(period):
        bit = (reg & 1) ^ ((reg >> tap) & 1)
        reg = (reg >> 1) | (bit << 14)
        seq[i] = 1.0 if (reg & 1) else -1.0
    idx = (np.arange(n) * clock_hz / SR).astype(np.int64) % period
    return seq[idx]


# ---------------------------------------------------------------- envelopes
def adsr(n, a, d, s, r, gate, curve=4.0):
    """n samples total, gate = seconds the key is held. Exponential-ish segments."""
    t = np.arange(n) / SR
    env = np.zeros(n)
    a = max(a, 1e-4); d = max(d, 1e-4); r = max(r, 1e-4)
    m = t < a
    env[m] = (t[m] / a) ** 0.8
    m = (t >= a) & (t < gate)
    env[m] = s + (1 - s) * np.exp(-curve * (t[m] - a) / d)
    # level at release
    if gate < a:
        lg = (gate / a) ** 0.8
    else:
        lg = s + (1 - s) * np.exp(-curve * (gate - a) / d)
    m = t >= gate
    env[m] = lg * np.exp(-curve * (t[m] - gate) / r)
    return env


def expdecay(n, tau):
    return np.exp(-np.arange(n) / (tau * SR))


def fade(x, fin=0.002, fout=0.01):
    n = len(x)
    a = min(int(fin * SR), n // 2); b = min(int(fout * SR), n // 2)
    if a > 0:
        x[:a] *= np.linspace(0, 1, a)
    if b > 0:
        x[-b:] *= np.linspace(1, 0, b)
    return x


# ---------------------------------------------------------------- filters
def rbj(kind, f0, q=0.7071, gain_db=0.0):
    f0 = np.clip(f0, 10, SR * 0.49)
    w = 2 * np.pi * f0 / SR
    cw, sw = np.cos(w), np.sin(w)
    al = sw / (2 * q)
    A = 10 ** (gain_db / 40)
    if kind == 'lp':
        b = [(1 - cw) / 2, 1 - cw, (1 - cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'hp':
        b = [(1 + cw) / 2, -(1 + cw), (1 + cw) / 2]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'bp':
        b = [al, 0, -al]; a = [1 + al, -2 * cw, 1 - al]
    elif kind == 'peak':
        b = [1 + al * A, -2 * cw, 1 - al * A]; a = [1 + al / A, -2 * cw, 1 - al / A]
    elif kind == 'lowshelf':
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * cw + sq), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - sq)]
        a = [(A + 1) + (A - 1) * cw + sq, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - sq]
    elif kind == 'highshelf':
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * cw + sq), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - sq)]
        a = [(A + 1) - (A - 1) * cw + sq, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - sq]
    else:
        raise ValueError(kind)
    b = np.array(b) / a[0]; a = np.array(a) / a[0]
    return b, a


def filt(x, kind, f0, q=0.7071, gain_db=0.0, axis=-1):
    b, a = rbj(kind, f0, q, gain_db)
    return signal.lfilter(b, a, x, axis=axis)


def hp(x, f0, order=2):
    sos = signal.butter(order, f0, 'high', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=-1)


def lp(x, f0, order=2):
    sos = signal.butter(order, min(f0, SR * 0.45), 'low', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=-1)


def eq(x, bands):
    """bands: list of (kind, f0, q, gain_db)."""
    for k, f0, q, g in bands:
        x = filt(x, k, f0, q, g)
    return x


def tv_filter(x, fc, q=0.707, kind='lp', block=64, stages=1):
    """Time-varying resonant biquad (per-block coefficients, carried state).
    stages=2 gives a 24 dB/oct ladder-ish slope (resonance only on the last stage)."""
    n = len(x)
    fc = np.broadcast_to(np.asarray(fc, dtype=np.float64), (n,))
    y = x.astype(np.float64).copy()
    for st in range(stages):
        qq = q if st == stages - 1 else 0.6
        out = np.empty(n)
        zi = np.zeros(2)
        for s in range(0, n, block):
            e = min(n, s + block)
            b, a = rbj(kind, fc[s], qq)
            out[s:e], zi = signal.lfilter(b, a, y[s:e], zi=zi)
        y = out
    return y


# ---------------------------------------------------------------- stereo / modulation
def pan(x, p):
    """p in [-1, 1], constant power. returns (2, n)."""
    th = (p + 1) * np.pi / 4
    return np.vstack([x * np.cos(th), x * np.sin(th)])


def frac_delay(x, d):
    """Read x at n - d(n) with linear interpolation (d in samples, array)."""
    n = len(x)
    idx = np.arange(n) - d
    i0 = np.floor(idx).astype(np.int64)
    fr = idx - i0
    i0c = np.clip(i0, 0, n - 1); i1c = np.clip(i0 + 1, 0, n - 1)
    y = x[i0c] * (1 - fr) + x[i1c] * fr
    y[i0 < 0] = 0
    return y


def chorus(x, voices=3, base_ms=11.0, depth_ms=2.2, rate=0.45, mix=0.5, t0=0.0):
    """Ensemble chorus (mono in -> stereo out). Juno/Solina-flavoured."""
    n = len(x)
    t = t0 + np.arange(n) / SR
    L = x * (1 - mix); R = x * (1 - mix)
    for v in range(voices):
        ph = 2 * np.pi * v / voices
        d = (base_ms + depth_ms * np.sin(2 * np.pi * rate * t + ph) +
             0.25 * depth_ms * np.sin(2 * np.pi * rate * 6.3 * t + 2 * ph)) * SR / 1000
        y = frac_delay(x, d) * mix / voices * 2
        a = (v + 0.5) / voices
        L += y * np.cos(a * np.pi / 2); R += y * np.sin(a * np.pi / 2)
    return np.vstack([L, R])


def haas(x, ms=12.0, side_gain=0.6):
    """Widen: add delayed copy to side channel only (mono compatible)."""
    d = int(ms * SR / 1000)
    dl = np.zeros_like(x); dl[d:] = x[:-d]
    s = hp(dl, 250) * side_gain
    return np.vstack([x + s, x - s])


def ms_mono_low(st, fc=120.0):
    """High-pass the side channel so the low end is mono (zero-phase)."""
    m = 0.5 * (st[0] + st[1]); s = 0.5 * (st[0] - st[1])
    sos = signal.butter(2, fc, 'high', fs=SR, output='sos')
    s = signal.sosfiltfilt(sos, s)
    return np.vstack([m + s, m - s])


def width(st, w):
    m = 0.5 * (st[0] + st[1]); s = 0.5 * (st[0] - st[1]) * w
    return np.vstack([m + s, m - s])


# ---------------------------------------------------------------- reverb / delay
def fdn_ir(rt60=2.5, seconds=None, size=1.0, damp_hz=6000.0, predelay_ms=12.0, n_lines=16,
           er=True, seed=7, bright_lf_rt=1.2):
    """Generate a stereo impulse response from a 16-line feedback delay network
    (Hadamard feedback matrix, per-line absorption + one-pole damping in the loop),
    processed block-wise so the recursion is vectorised."""
    r = np.random.default_rng(seed)
    if seconds is None:
        seconds = rt60 * 1.3
    n = int(seconds * SR)
    base = np.array([613, 709, 797, 887, 953, 1049, 1117, 1223, 1307, 1409, 1511, 1601, 1709, 1823, 1931, 2053])[:n_lines]
    d = np.round(base * size * (1 + 0.03 * r.standard_normal(n_lines))).astype(int)
    B = int(d.min())
    H = np.array([[1.0]])
    while H.shape[0] < n_lines:
        H = np.block([[H, H], [H, -H]])
    H /= np.sqrt(n_lines)
    g = 10 ** (-3 * d / (rt60 * SR))
    # damping one-pole: y = (1-p) x + p y
    p = np.exp(-2 * np.pi * damp_hz / SR)
    buf = np.zeros((n_lines, n + B + d.max() + 10))
    bin_ = r.choice([-1.0, 1.0], n_lines)
    cL = r.choice([-1.0, 1.0], n_lines); cR = r.choice([-1.0, 1.0], n_lines)
    # input: short diffused burst rather than a single impulse (smoother onset)
    burst_len = int(0.004 * SR)
    burst = r.standard_normal(burst_len) * np.hanning(burst_len)
    inp = np.zeros(n + B); inp[:burst_len] = burst
    outL = np.zeros(n + B); outR = np.zeros(n + B)
    zi = np.zeros((n_lines, 1))
    for s in range(0, n, B):
        # outputs of the delay lines for this block
        o = np.empty((n_lines, B))
        for i in range(n_lines):
            st = s - d[i]
            if st + B <= 0:
                o[i] = 0
            else:
                seg = buf[i, max(st, 0): st + B]
                o[i] = 0; o[i, B - len(seg):] = seg
        o, zi = signal.lfilter([1 - p], [1, -p], o, axis=1, zi=zi)
        o *= g[:, None]
        outL[s:s + B] = cL @ o; outR[s:s + B] = cR @ o
        fb = H @ o + bin_[:, None] * inp[None, s:s + B]
        buf[:, s:s + B] = fb
    L = outL[:n]; R = outR[:n]
    if er:
        # sparse early reflections, decorrelated L/R
        for ch in (L, R):
            for k in range(14):
                t = int((0.004 + 0.07 * r.random() ** 1.5) * SR)
                ch[t] += r.choice([-1, 1]) * (0.9 - 0.05 * k) * 2.0
    # low frequencies slightly longer decay than mids: gentle LF emphasis in tail
    tt = np.arange(n) / SR
    L *= 1.0; R *= 1.0
    pd = int(predelay_ms * SR / 1000)
    ir = np.zeros((2, n + pd)); ir[0, pd:] = L; ir[1, pd:] = R
    ir[:, -int(0.05 * SR):] *= np.linspace(1, 0, int(0.05 * SR))
    ir /= np.sqrt(np.sum(ir ** 2) / 2)
    return ir


def convolve_st(x, ir):
    """x: (2,n) or (n,) send; ir (2,m). Returns (2, n) (tail truncated to n)."""
    if x.ndim == 1:
        x = np.vstack([x, x])
    n = x.shape[1]
    out = np.empty((2, n))
    for c in range(2):
        out[c] = signal.oaconvolve(x[c], ir[c])[:n]
    return out


def tempo_delay(x, delay_s, fb=0.4, repeats=8, lp_hz=4500.0, hp_hz=300.0, pingpong=True, width_=1.0):
    """Feedback delay built from explicit repeats (each repeat filtered again). mono in -> stereo out."""
    n = len(x)
    d = int(round(delay_s * SR))
    out = np.zeros((2, n))
    tap = hp(x, hp_hz)
    sos = signal.butter(1, lp_hz, 'low', fs=SR, output='sos')
    for k in range(1, repeats + 1):
        tap = signal.sosfilt(sos, tap) * (fb if k > 1 else 1.0)
        sh = k * d
        if sh >= n:
            break
        if pingpong:
            c = (k + 1) % 2
            out[c, sh:] += tap[:n - sh] * (0.5 + 0.5 * width_)
            out[1 - c, sh:] += tap[:n - sh] * (0.5 - 0.5 * width_)
        else:
            out[:, sh:] += tap[:n - sh]
    return out


def gate_env(n, hold_s, fall_s=0.02):
    e = np.ones(n)
    h = int(hold_s * SR); f = int(fall_s * SR)
    if h < n:
        e[h:min(n, h + f)] = np.linspace(1, 0, min(n, h + f) - h)
        e[h + f:] = 0
    return e


# ---------------------------------------------------------------- dynamics
def _smooth_gr(gr_db, attack_s, release_s):
    """gr_db >= 0 (amount of reduction). Peak-hold attack (look-ahead) + one-pole release."""
    a = max(1, int(attack_s * SR))
    hold = ndimage.maximum_filter1d(gr_db, size=2 * a + 1, mode='nearest')
    box = ndimage.uniform_filter1d(hold, size=2 * a + 1, mode='nearest')
    pr = np.exp(-1.0 / (release_s * SR))
    rel = signal.lfilter([1 - pr], [1, -pr], box)
    return np.maximum(box, rel)


def compressor(x, thresh_db=-18, ratio=3.0, attack=0.005, release=0.12, knee=6.0, makeup_db=0.0,
               detector=None, rms_ms=5.0):
    """Stereo-linked feed-forward compressor. x (2,n) or (n,). detector: optional sidechain signal."""
    xs = x if x.ndim == 2 else x[None, :]
    det = xs if detector is None else (detector if detector.ndim == 2 else detector[None, :])
    p = np.exp(-1.0 / (rms_ms / 1000 * SR))
    pw = signal.lfilter([1 - p], [1, -p], np.max(det ** 2, axis=0))
    lvl = 10 * np.log10(pw + 1e-12)
    over = lvl - thresh_db
    gr = np.where(over <= -knee / 2, 0.0,
                  np.where(over >= knee / 2, over * (1 - 1 / ratio),
                           (1 - 1 / ratio) * (over + knee / 2) ** 2 / (2 * knee)))
    gr = _smooth_gr(gr, attack, release)
    g = db(-gr + makeup_db)
    y = xs * g[None, :]
    return (y if x.ndim == 2 else y[0]), gr


def duck_env(n, hits, depth_db=-8.0, attack=0.004, hold=0.03, release=0.22, shape=2.0):
    """Sidechain ducking gain curve from known kick times (seconds)."""
    g = np.zeros(n)  # attenuation in 0..1
    L = int((attack + hold + release * 3) * SR)
    t = np.arange(L) / SR
    curve = np.where(t < attack, t / attack,
                     np.where(t < attack + hold, 1.0, np.exp(-((t - attack - hold) / release) ** 1.0 * shape)))
    for h in hits:
        s = int(h * SR - attack * SR)
        if s >= n:
            continue
        s0 = max(0, s); e = min(n, s + L)
        g[s0:e] = np.maximum(g[s0:e], curve[s0 - s:e - s])
    return db(depth_db * g)


def softclip(x, drive_db=0.0, os_=2):
    """Oversampled soft clipper (tanh-like with gentle knee)."""
    g = db(drive_db)
    u = signal.resample_poly(x, os_, 1, axis=-1) * g
    u = np.tanh(u)
    return signal.resample_poly(u, 1, os_, axis=-1) / g


def tp_limiter(x, ceiling_db=-1.2, lookahead_ms=1.5, release_ms=80.0, os_=4):
    """True-peak look-ahead limiter: gain computed from 4x oversampled peaks, zero-latency (offline)."""
    n = x.shape[1]
    peaks = np.zeros(n)
    step = SR * 5
    for s in range(0, n, step):
        e = min(n, s + step)
        seg = x[:, max(0, s - 32): min(n, e + 32)]
        u = np.abs(signal.resample_poly(seg, os_, 1, axis=-1)).max(0)
        off = (s - max(0, s - 32)) * os_
        u = u[off: off + (e - s) * os_]
        peaks[s:e] = u.reshape(-1, os_).max(1)
    peaks = ndimage.maximum_filter1d(peaks, 3)
    c = db(ceiling_db)
    need = np.maximum(0.0, 20 * np.log10(np.maximum(peaks, 1e-9) / c))
    la = int(lookahead_ms / 1000 * SR)
    hold = ndimage.maximum_filter1d(need, size=2 * la + 1, mode='nearest')
    box = ndimage.uniform_filter1d(hold, size=2 * la + 1, mode='nearest')
    pr = np.exp(-1.0 / (release_ms / 1000 * SR))
    rel = signal.lfilter([1 - pr], [1, -pr], box)
    gr = np.maximum(box, rel)
    return x * db(-gr)[None, :], gr


def multiband_glue(x, xo=(120.0, 5000.0), settings=((-20, 2.0, 0.02, 0.2), (-18, 1.6, 0.01, 0.15), (-24, 1.8, 0.004, 0.08))):
    """3-band zero-phase split (perfect reconstruction) with gentle per-band compression."""
    sos1 = signal.butter(2, xo[0], 'low', fs=SR, output='sos')
    sos2 = signal.butter(2, xo[1], 'low', fs=SR, output='sos')
    low = signal.sosfiltfilt(sos1, x, axis=-1)
    rest = x - low
    mid = signal.sosfiltfilt(sos2, rest, axis=-1)
    high = rest - mid
    out = np.zeros_like(x)
    grs = []
    for band, (t, r, a, rl) in zip((low, mid, high), settings):
        y, gr = compressor(band, t, r, a, rl, knee=8)
        out += y; grs.append(gr.max())
    return out, grs


# ---------------------------------------------------------------- mastering / export
def master(mix, target_lufs=-14.0, ceiling_db=-1.2, clip_drive_db=1.5, glue=True):
    from analyze import lufs, true_peak_db
    x = mix - mix.mean(axis=1, keepdims=True)
    x = hp(x, 22, order=2)  # remove DC / subsonics
    x = ms_mono_low(x, 110)
    if glue:
        x, grs = multiband_glue(x)
        print("  glue GR max per band (dB):", [round(g, 2) for g in grs])
    gain = 0.0
    for it in range(6):
        y = x * db(gain)
        y = softclip(y, clip_drive_db - 1.0) if clip_drive_db > 0 else y
        y, gr = tp_limiter(y, ceiling_db)
        L = lufs(y)
        print(f"  master pass {it}: gain {gain:+.2f} dB -> {L:.2f} LUFS, limiter GR max {gr.max():.2f} dB")
        if abs(L - target_lufs) < 0.05:
            break
        gain += target_lufs - L
    tp = true_peak_db(y)
    k = 0
    while tp > ceiling_db + 0.15 and k < 4:  # safety
        y, _ = tp_limiter(y, ceiling_db - 0.3 * (k + 1))
        tp = true_peak_db(y); k += 1
    return y


def write_mp3(x, path, bitrate="192k"):
    y = np.clip(x, -1, 1).astype(np.float32).T.copy()
    p = subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "f32le", "-ar", str(SR), "-ac", "2", "-i", "-",
                        "-c:a", "libmp3lame", "-b:a", bitrate, "-ar", str(SR), path], input=y.tobytes())
    assert p.returncode == 0


def finalize_mp3(y, path, target_lufs=-14.0, tp_max=-1.05, ceiling_db=-1.2):
    """Encode, decode and re-measure the MP3 itself; correct gain / ceiling until the decoded
    file meets the loudness target (+-0.1 LU) and true-peak limit."""
    from analyze import decode, lufs, true_peak_db
    y = lp(y, 18500, 4)
    for it in range(5):
        write_mp3(y, path)
        d = decode(path)
        L = lufs(d); tp = true_peak_db(d)
        print(f"  mp3 check {it}: {L:.2f} LUFS, {tp:.2f} dBTP")
        if abs(L - target_lufs) <= 0.1 and tp <= tp_max:
            break
        y = y * db(target_lufs - L)
        if tp + (target_lufs - L) > tp_max:
            ceiling_db -= 0.3
        y, _ = tp_limiter(y, ceiling_db)
    return L, tp


def write_wav(x, path):
    from scipy.io import wavfile
    wavfile.write(path, SR, np.clip(x, -1, 1).T.astype(np.float32))
