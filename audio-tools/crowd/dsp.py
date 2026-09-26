"""Core DSP helpers for the procedural crowd engine.

Everything here is *circular*: signals are treated as periodic with the loop
length, so filtering, STFT processing and reverb tails wrap around the seam and
the rendered loops are seamless by construction.
"""
import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import minimum_filter1d, uniform_filter1d

FS = 32000
NFFT = 512
HOP = 128
FR = FS / HOP                      # frame rate (250 frames/s)
_n = np.arange(NFFT)
WIN = np.sqrt(0.5 - 0.5 * np.cos(2 * np.pi * _n / NFFT)).astype(np.float32)
FREQS = np.fft.rfftfreq(NFFT, 1 / FS).astype(np.float32)
NB = len(FREQS)
OLA_NORM = float(np.sum(WIN.astype(np.float64) ** 2) / HOP)
NOISE_SCALE = float(np.sqrt(np.sum(WIN.astype(np.float64) ** 2) / 2))


# ----------------------------------------------------------------- STFT ----
def frames_view(x):
    M = len(x) // HOP
    xp = np.concatenate([x[-NFFT // 2:], x, x[:NFFT // 2]])
    return np.lib.stride_tricks.sliding_window_view(xp, NFFT)[::HOP][:M]


def stft_c(x, idx=None):
    """Circular, centred STFT (frame m centred on sample m*HOP)."""
    fr = frames_view(x)
    if idx is not None:
        fr = fr[idx]
    return np.fft.rfft(fr * WIN, axis=1).astype(np.complex64)


def istft_c(S, chunk=4000):
    """Circular overlap-add inverse of stft_c for a full (M, NB) spectrogram."""
    M = S.shape[0]
    L = M * HOP
    out = np.zeros(L, np.float32)
    K = NFFT // HOP
    for c0 in range(0, M, chunk):
        c1 = min(M, c0 + chunk)
        fr = np.fft.irfft(S[c0:c1], n=NFFT, axis=1).astype(np.float32) * WIN
        for k in range(K):
            seg = fr[:, k * HOP:(k + 1) * HOP].reshape(-1)
            start = (c0 + k - K // 2) * HOP
            s0 = start % L
            n1 = min(len(seg), L - s0)
            out[s0:s0 + n1] += seg[:n1]
            if n1 < len(seg):
                out[:len(seg) - n1] += seg[n1:]
    return out / OLA_NORM


# --------------------------------------------------- frequency-domain FX ----
def ffilt(x, fn):
    """Zero-phase circular filter: x (..., L) multiplied by |H(f)| = fn(f)."""
    L = x.shape[-1]
    X = np.fft.rfft(x, axis=-1)
    f = np.fft.rfftfreq(L, 1 / FS)
    X *= fn(f)
    return np.fft.irfft(X, n=L, axis=-1).astype(np.float32)


def lp(f, fc, order=2):
    return 1.0 / np.sqrt(1.0 + (np.asarray(f) / fc) ** (2 * order))


def hp(f, fc, order=2):
    f = np.maximum(np.asarray(f, dtype=np.float64), 1e-3)
    return 1.0 / np.sqrt(1.0 + (fc / f) ** (2 * order))


def bump(f, fc, gain_db, width_oct):
    lf = np.log2(np.maximum(np.asarray(f, dtype=np.float64), 1.0) / fc)
    return 10 ** (gain_db / 20 * np.exp(-0.5 * (lf / width_oct) ** 2))


def air_abs(f, d, k=0.004):
    """Approximate air absorption (ISO 9613-ish at 20C/50%RH) for distance d [m]."""
    f = np.asarray(f, dtype=np.float64)
    return 10 ** (-(k * (f / 1000.0) ** 1.7 * d) / 20)


def noise_shaped(rng, L, fn, ch=2):
    """Periodic coloured noise generated in the frequency domain, unit std."""
    f = np.fft.rfftfreq(L, 1 / FS)
    out = np.empty((ch, L), np.float32)
    mag = fn(f)
    mag[0] = 0
    for c in range(ch):
        X = (rng.standard_normal(len(f)) + 1j * rng.standard_normal(len(f))) * mag
        x = np.fft.irfft(X, n=L)
        out[c] = x / (x.std() + 1e-12)
    return out


def slow_curve(rng, n, rate, fmax, fmin=None, power=0.0):
    """Periodic smooth random curve (zero mean, unit std) of n points sampled at
    `rate` Hz with spectral content up to fmax Hz (1/f^power weighting)."""
    f = np.fft.rfftfreq(n, 1 / rate)
    fmin = f[1] if fmin is None else fmin
    w = ((f >= fmin) & (f <= fmax)).astype(float)
    w[1:] /= np.maximum(f[1:], fmin) ** power
    X = (rng.standard_normal(len(f)) + 1j * rng.standard_normal(len(f))) * w
    c = np.fft.irfft(X, n=n)
    return ((c - c.mean()) / (c.std() + 1e-12)).astype(np.float32)


def pan_gains(p):
    """Equal-power pan, p in [-1, 1]."""
    a = (np.clip(p, -1, 1) + 1) * np.pi / 4
    return np.cos(a), np.sin(a)


# ---------------------------------------------------------------- reverb ----
BAND_CENTERS = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]


def make_ir(rng, dur, rt, predelay=0.01, onset=0.02, er=(), slaps=(),
            er_lp=6000, tail_gain=1.0, density_hz=None):
    """Synthetic impulse response (mono) with frequency-dependent decay.

    rt: dict band-centre -> RT60 seconds (interpolated in log-f).
    er: list of (delay_s, gain) early reflections (diffused a little, low-passed).
    slaps: list of (delay_s, gain, smear_s, lp_hz) discrete echo clusters.
    """
    n = int(dur * FS)
    t = np.arange(n) / FS
    noise = rng.standard_normal(n)
    N = np.fft.rfft(noise)
    f = np.fft.rfftfreq(n, 1 / FS)
    logf = np.log2(np.maximum(f, 1.0))
    keys = sorted(rt)
    rtv = lambda c: float(np.interp(np.log2(c), np.log2(keys), [rt[k] for k in keys]))
    tail = np.zeros(n)
    for i, c in enumerate(BAND_CENTERS):
        d = logf - np.log2(c)
        w = np.where(np.abs(d) < 1, np.cos(np.pi / 2 * d) ** 2, 0.0)
        if i == 0:
            w[d < 0] = 1
        if i == len(BAND_CENTERS) - 1:
            w[d > 0] = 1
        band = np.fft.irfft(N * w, n=n)
        tail += band * np.exp(-6.9078 * t / rtv(c))
    tt = np.maximum(t - predelay, 0)
    env = np.where(t >= predelay, 1 - np.exp(-tt / max(onset, 1e-3)), 0.0)
    tail *= env
    if density_hz:  # sparse early tail for open spaces (grainier)
        pass
    tail /= np.sqrt(np.sum(tail ** 2)) + 1e-12
    tail *= tail_gain
    ir = tail
    if er:
        e = np.zeros(n)
        for d, g in er:
            i = int(d * FS)
            if i < n:
                e[i] += g
        k = np.exp(-np.arange(48) / 10.0) * rng.standard_normal(48)
        k[0] = 1.5
        k /= np.sqrt(np.sum(k ** 2))
        e = np.convolve(e, k)[:n]
        e = np.fft.irfft(np.fft.rfft(e) * lp(f, er_lp, 1), n=n)
        ir = ir + e
    for d, g, smear, lph in slaps:
        i = int(d * FS)
        m = int(smear * 4 * FS)
        if i + m >= n:
            m = n - i - 1
        if m <= 8:
            continue
        tk = np.arange(m) / FS
        burst = rng.standard_normal(m) * np.exp(-tk / smear) * np.minimum(tk / 0.002, 1)
        burst /= np.sqrt(np.sum(burst ** 2))
        s = np.zeros(n)
        s[i:i + m] = burst * g
        s = np.fft.irfft(np.fft.rfft(s) * lp(f, lph, 2), n=n)
        ir = ir + s
    return ir.astype(np.float32)


class StereoReverb:
    """True-stereo convolution reverb from four decorrelated IRs; circular."""

    def __init__(self, rng, cross=0.7, **kw):
        self.irs = {}
        for k in ('LL', 'LR', 'RL', 'RR'):
            ir = make_ir(rng, **kw)
            if k in ('LR', 'RL'):
                ir *= cross
            self.irs[k] = ir
        self._cache = {}

    def _H(self, L):
        if L not in self._cache:
            self._cache[L] = {k: np.fft.rfft(v, n=L) if len(v) <= L else None
                              for k, v in self.irs.items()}
        return self._cache[L]

    def __call__(self, sL, sR):
        L = len(sL)
        H = self._H(L)
        SL = np.fft.rfft(sL)
        SR = np.fft.rfft(sR)
        wl = np.fft.irfft(SL * H['LL'] + SR * H['RL'], n=L)
        wr = np.fft.irfft(SL * H['LR'] + SR * H['RR'], n=L)
        return np.stack([wl, wr]).astype(np.float32)


def cconv(x, ir):
    L = x.shape[-1]
    return np.fft.irfft(np.fft.rfft(x, axis=-1) * np.fft.rfft(ir, n=L), n=L, axis=-1).astype(np.float32)


# -------------------------------------------------------------- loudness ----
def _kcoef(fs=FS):
    G = 3.999843853973347
    f0 = 1681.974450955533
    Q = 0.7071752369554196
    K = np.tan(np.pi * f0 / fs)
    Vh = 10 ** (G / 20)
    Vb = Vh ** 0.4996667741545416
    a0 = 1 + K / Q + K * K
    b1 = np.array([(Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0])
    a1 = np.array([1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0])
    f0 = 38.13547087602444
    Q = 0.5003270373238773
    K = np.tan(np.pi * f0 / fs)
    d = 1 + K / Q + K * K
    a2 = np.array([1, 2 * (K * K - 1) / d, (1 - K / Q + K * K) / d])
    b2 = np.array([1.0, -2.0, 1.0])
    return b1, a1, b2, a2


_KC = _kcoef()


def lufs(x, circular=True):
    """ITU-R BS.1770-4 integrated loudness; x shape (ch, n)."""
    x = np.atleast_2d(x).astype(np.float64)
    b1, a1, b2, a2 = _KC
    w = FS if circular else 0
    xe = np.concatenate([x[:, -w:], x], axis=1) if w else x
    y = signal.lfilter(b1, a1, xe, axis=1)
    y = signal.lfilter(b2, a2, y, axis=1)[:, w:]
    n = y.shape[1]
    blk = int(0.4 * FS)
    step = int(0.1 * FS)
    cs = np.concatenate([np.zeros((y.shape[0], 1)), np.cumsum(y ** 2, axis=1)], axis=1)
    st = np.arange(0, n - blk + 1, step)
    z = (cs[:, st + blk] - cs[:, st]) / blk
    lk = -0.691 + 10 * np.log10(np.sum(z, 0) + 1e-20)
    g = lk > -70
    if not g.any():
        return -120.0
    lr = -0.691 + 10 * np.log10(np.sum(np.mean(z[:, g], 1))) - 10
    g2 = g & (lk > lr)
    return float(-0.691 + 10 * np.log10(np.sum(np.mean(z[:, g2], 1))))


def short_term_lufs(x, win=3.0, step=0.5):
    x = np.atleast_2d(x).astype(np.float64)
    b1, a1, b2, a2 = _KC
    xe = np.concatenate([x[:, -FS:], x], axis=1)
    y = signal.lfilter(b1, a1, xe, axis=1)
    y = signal.lfilter(b2, a2, y, axis=1)[:, FS:]
    n = y.shape[1]
    blk = int(win * FS)
    y2 = np.concatenate([y, y[:, :blk]], axis=1)   # circular
    cs = np.concatenate([np.zeros((y.shape[0], 1)), np.cumsum(y2 ** 2, axis=1)], axis=1)
    st = np.arange(0, n, int(step * FS))
    z = (cs[:, st + blk] - cs[:, st]) / blk
    return st / FS, -0.691 + 10 * np.log10(np.sum(z, 0) + 1e-20)


def true_peak_db(x):
    x = np.atleast_2d(x)
    pk = 0.0
    for c in range(x.shape[0]):
        xe = np.concatenate([x[c, -64:], x[c], x[c, :64]]).astype(np.float64)
        up = signal.resample_poly(xe, 4, 1)
        pk = max(pk, float(np.max(np.abs(up))))
    return 20 * np.log10(pk + 1e-12)


def limit_circular(x, ceil_db, win_ms=8.0):
    """Circular look-ahead peak limiter on the 4x-oversampled peak envelope."""
    c = 10 ** (ceil_db / 20)
    pk = np.zeros(x.shape[1])
    for ch in range(x.shape[0]):
        xe = np.concatenate([x[ch, -64:], x[ch], x[ch, :64]]).astype(np.float64)
        up = np.abs(signal.resample_poly(xe, 4, 1)).reshape(-1, 4).max(1)[64:-64]
        pk = np.maximum(pk, up)
    g = np.minimum(1.0, c / np.maximum(pk, 1e-9))
    if g.min() >= 1.0:
        return x, 0.0
    w = max(3, int(win_ms / 1000 * FS))
    g = minimum_filter1d(g, 2 * w + 1, mode='wrap')
    g = uniform_filter1d(g, w, mode='wrap')
    g = uniform_filter1d(g, w // 2 + 1, mode='wrap')
    g = np.minimum(g, 1.0)
    return (x * g).astype(np.float32), float(20 * np.log10(g.min()))


def write_wav16(path, x, rng):
    """x (2, n) float -> 16-bit PCM with TPDF dither."""
    y = x.T * 32767.0 + (rng.random(x.T.shape) - rng.random(x.T.shape))
    y = np.clip(np.round(y), -32768, 32767).astype(np.int16)
    wavfile.write(path, FS, y)


def read_wav(path):
    sr, y = wavfile.read(path)
    return sr, (y.astype(np.float32) / 32768.0).T
