"""Non-vocal crowd and venue sound events: claps, whistles, air horns, stomps,
RC-car motor passes, lap-decoder chirps, and continuous venue textures.
All placement is circular (events wrap across the loop seam)."""
import numpy as np
from scipy import signal
from dsp import FS, ffilt, lp, hp, bump, air_abs, noise_shaped, slow_curve, pan_gains


class EventBus:
    """Time-domain stereo dry + reverb-send accumulators with wrap-around."""

    def __init__(self, L, pad_s=8.0):
        self.L = L
        self.pad = int(pad_s * FS)
        self.d = np.zeros((2, L + self.pad), np.float32)
        self.s = np.zeros((2, L + self.pad), np.float32)

    def add(self, x, t0, pan=0.0, gdry=1.0, gsend=0.0):
        n = min(len(x), self.pad)
        i0 = int(t0) % self.L
        gl, gr = pan_gains(pan)
        seg = x[:n]
        if gdry:
            self.d[0, i0:i0 + n] += seg * np.float32(gdry * gl)
            self.d[1, i0:i0 + n] += seg * np.float32(gdry * gr)
        if gsend:
            self.s[0, i0:i0 + n] += seg * np.float32(gsend * gl)
            self.s[1, i0:i0 + n] += seg * np.float32(gsend * gr)

    def finish(self, post=None):
        L, p = self.L, self.pad
        d = self.d[:, :L].copy()
        d[:, :p] += self.d[:, L:L + p]
        s = self.s[:, :L].copy()
        s[:, :p] += self.s[:, L:L + p]
        if post is not None:
            d = ffilt(d, post)
            s = ffilt(s, post)
        return d, s


# ------------------------------------------------------------------ claps
CLAP_TYPES = ['P1', 'P2', 'A1', 'A2', 'A3', 'P3']


def make_clap(rng, ctype):
    """One hand clap: 5-20 ms noise burst + hand-cavity resonance (Repp 1987 style
    hand configurations: P = palm-to-palm (low narrow peak, ~2.5 kHz notch),
    A = fingers-to-palm (broad ~2 kHz peak))."""
    n = int(0.07 * FS)
    t = np.arange(n) / FS
    att = rng.uniform(0.0002, 0.0008)
    tau = rng.uniform(0.0025, 0.007)
    env = np.minimum(t / att, 1.0) * np.exp(-np.maximum(t - att, 0) / tau)
    x = rng.standard_normal(n) * env
    y = 0.35 * x
    if ctype == 'P1':       # flat palms
        fc, Q, g = rng.uniform(750, 1050), rng.uniform(2.5, 4.5), 1.0
    elif ctype == 'P2':     # cupped palms: low, hollow, ringy
        fc, Q, g = rng.uniform(420, 650), rng.uniform(6, 10), 1.3
    elif ctype == 'P3':     # palm-palm, slightly offset
        fc, Q, g = rng.uniform(900, 1300), rng.uniform(2, 3.5), 1.0
    elif ctype == 'A1':     # fingers to palm
        fc, Q, g = rng.uniform(1600, 2300), rng.uniform(1.0, 1.8), 1.0
    elif ctype == 'A2':     # fingers to fingers, bright
        fc, Q, g = rng.uniform(2100, 3000), rng.uniform(1.0, 1.6), 0.9
    else:                   # A3 mixed, two peaks
        fc, Q, g = rng.uniform(1000, 1500), rng.uniform(1.5, 2.5), 0.8
    b, a = signal.iirpeak(fc, Q, fs=FS)
    y = y + g * signal.lfilter(b, a, x) * (Q ** 0.5)
    if ctype == 'A3':
        b, a = signal.iirpeak(rng.uniform(2000, 2600), 1.6, fs=FS)
        y += 0.7 * signal.lfilter(b, a, x)
    if ctype.startswith('P'):
        b, a = signal.iirnotch(rng.uniform(2300, 2800), 1.5, fs=FS)
        y = signal.lfilter(b, a, y)
    # remove sub-bass thump + keep a little of the air "whoosh" of hands
    b, a = signal.butter(2, 150, 'hp', fs=FS)
    y = signal.lfilter(b, a, y)
    y /= np.sqrt(np.sum(y ** 2)) + 1e-9
    return y.astype(np.float32)


class ClapperPool:
    def __init__(self, rng, types_p=None, variants=6):
        self.rng = rng
        types_p = types_p or [0.25, 0.1, 0.25, 0.15, 0.15, 0.1]
        self.types_p = np.array(types_p) / np.sum(types_p)
        self.variants = variants

    def clapper_bank(self):
        t = CLAP_TYPES[self.rng.choice(len(CLAP_TYPES), p=self.types_p)]
        return [make_clap(self.rng, t) for _ in range(self.variants)]


def clap_train(rng, t0, t1, rate, jitter=0.05, drift=0.08):
    """Clap onset times for one person clapping from t0..t1 seconds."""
    times = []
    t = t0 + rng.uniform(0, 1 / rate)
    r = rate
    while t < t1:
        times.append(t)
        r = np.clip(r * (1 + drift * 0.15 * rng.standard_normal()), rate * (1 - drift), rate * (1 + drift))
        t += (1 / r) * (1 + jitter * rng.standard_normal())
    return np.array(times)


def applause_rain(rng, L, n_people, rate, activity, bank, pan_spread=1.0, amp_sd=0.3):
    """Diffuse far applause: thousands of clappers approximated as Poisson rain of
    clap variants, density following `activity` (per-sample 0..1 curve). Circular."""
    out = np.zeros((2, L), np.float32)
    dur = L / FS
    lam = n_people * rate * dur * float(np.mean(activity))
    nb = len(bank)
    for ch in range(2):
        imp_sum = [np.zeros(L, np.float32) for _ in range(nb)]
        n = rng.poisson(lam / 2)
        # draw times by rejection according to activity
        tt = rng.integers(0, L, size=int(n * 1.8) + 10)
        keep = rng.random(len(tt)) < activity[tt] / (activity.max() + 1e-9)
        tt = tt[keep][:n]
        amps = rng.lognormal(0, amp_sd, size=len(tt)).astype(np.float32)
        which = rng.integers(0, nb, size=len(tt))
        for k in range(nb):
            sel = which == k
            np.add.at(imp_sum[k], tt[sel], amps[sel])
        acc = np.zeros(L // 2 + 1, np.complex128)
        for k in range(nb):
            acc += np.fft.rfft(imp_sum[k]) * np.fft.rfft(bank[k], n=L)
        out[ch] = np.fft.irfft(acc, n=L)
    return out


# ----------------------------------------------------------------- whistles
def _whistle_seg(rng, d, pts, trill=False):
    n = int(d * FS)
    t = np.arange(n) / FS
    f = np.interp(np.linspace(0, 1, n), pts[0], pts[1])
    vr = rng.uniform(9, 13) if trill else rng.uniform(5, 7)
    vd = rng.uniform(0.04, 0.07) if trill else rng.uniform(0.004, 0.015)
    f = f * (1 + vd * np.sin(2 * np.pi * vr * t + rng.uniform(0, 6)))
    ph = 2 * np.pi * np.cumsum(f) / FS
    tone = np.sin(ph) + 0.06 * np.sin(2 * ph) + 0.02 * np.sin(3 * ph)
    # breath noise band around the pitch (narrowband via modulated low-passed noise)
    b, a = signal.butter(2, 180, fs=FS)
    nb = signal.lfilter(b, a, rng.standard_normal(n)) * 18
    breath = nb * np.sin(ph + 0.3)
    hiss = ffilt(rng.standard_normal(n)[None], lambda ff: hp(ff, 3000, 2))[0] * 0.05
    env = np.minimum(t / rng.uniform(0.02, 0.05), 1) * np.clip((d - t) / 0.04, 0, 1)
    env *= 1 + 0.08 * np.sin(2 * np.pi * rng.uniform(3, 6) * t)
    return ((tone * (0.85 + 0.15 * rng.random()) + 0.25 * breath + hiss) * env).astype(np.float32)


def whistle(rng, style=None):
    """Finger/lip whistle, 1.8-3.5 kHz with glides and vibrato + breath noise."""
    style = style or rng.choice(['long', 'wolf', 'tweets', 'trill', 'rise'], p=[0.35, 0.12, 0.23, 0.15, 0.15])
    f0 = rng.uniform(1900, 3300)
    if style == 'long':
        y = _whistle_seg(rng, rng.uniform(0.6, 1.5), ([0, 0.08, 0.85, 1], np.array([0.8, 1.0, 1.0, 0.85]) * f0))
    elif style == 'wolf':   # "wheet ... wheee-ooo"
        a = _whistle_seg(rng, rng.uniform(0.25, 0.35), ([0, 0.8, 1], np.array([0.62, 1.1, 1.05]) * f0))
        b = _whistle_seg(rng, rng.uniform(0.6, 0.8), ([0, 0.3, 1], np.array([0.75, 1.1, 0.55]) * f0))
        y = np.concatenate([a, np.zeros(int(FS * rng.uniform(0.1, 0.18)), np.float32), b])
    elif style == 'rise':
        y = _whistle_seg(rng, rng.uniform(0.5, 0.9), ([0, 0.8, 1], np.array([0.65, 1.1, 1.05]) * f0))
    elif style == 'trill':
        y = _whistle_seg(rng, rng.uniform(0.6, 1.2), ([0, 0.1, 1], np.array([0.85, 1.0, 0.95]) * f0), trill=True)
    else:  # tweets: 2-3 short blasts
        parts = []
        for _ in range(int(rng.integers(2, 4))):
            parts.append(_whistle_seg(rng, rng.uniform(0.15, 0.3), ([0, 0.3, 1], np.array([0.8, 1.0, 0.97]) * f0)))
            parts.append(np.zeros(int(FS * rng.uniform(0.08, 0.16)), np.float32))
        y = np.concatenate(parts)
    y /= np.sqrt(np.mean(y ** 2)) + 1e-9
    return y.astype(np.float32)


# ---------------------------------------------------------------- air horns
def air_horn(rng, dur, f0=None, reeds=None):
    """Canned reed air horn: bright pulse wave ~350-450 Hz, onset blip & pitch
    scoop, slow droop, 1-2 slightly detuned reeds, bell resonances, rasp."""
    f0 = f0 or rng.uniform(340, 460)
    reeds = reeds or (1 if rng.random() < 0.6 else 2)
    n = int(dur * FS)
    t = np.arange(n) / FS
    y = np.zeros(n)
    for r in range(reeds):
        fr = f0 * (1 if r == 0 else rng.uniform(1.008, 1.03))
        scoop = rng.uniform(0.75, 0.9)
        st = rng.uniform(0.03, 0.08)
        f = fr * (scoop + (1 - scoop) * (1 - np.exp(-t / st)))
        f *= 1 - rng.uniform(0.01, 0.04) * (t / max(dur, 0.1))
        f *= 1 + 0.003 * np.convolve(rng.standard_normal(n), np.hanning(801) / 400, mode='same')
        ph = 2 * np.pi * np.cumsum(f) / FS
        duty = rng.uniform(0.25, 0.4)
        H = int(15000 / (fr * 1.02))
        w = np.zeros(n)
        for h in range(1, H + 1):
            w += (np.sin(np.pi * h * duty) / h) * np.sin(h * ph)
        y += w
    y /= np.max(np.abs(y)) + 1e-9
    y = np.tanh(1.8 * y)   # rasp
    y = ffilt(y[None], lambda ff: bump(ff, 1300, 5, 0.6) * bump(ff, 2800, 4, 0.5) * hp(ff, 250, 2) * lp(ff, 7000, 2))[0]
    env = np.minimum(t / 0.012, 1) * np.minimum((dur - t) / 0.04, 1)
    env *= 1 + 0.4 * np.exp(-t / 0.05)       # onset blip
    y = y * env
    blip = rng.standard_normal(int(0.01 * FS)) * np.exp(-np.arange(int(0.01 * FS)) / 80.0) * 0.5
    y[:len(blip)] += blip
    y /= np.sqrt(np.mean(y ** 2)) + 1e-9
    return y.astype(np.float32)


def horn_pattern(rng):
    """A person's horn burst: e.g. baap-baap-baaaaap."""
    k = int(rng.choice([1, 2, 3, 3, 4]))
    parts = []
    f0 = rng.uniform(340, 460)
    reeds = 1 if rng.random() < 0.6 else 2
    for i in range(k):
        d = rng.uniform(0.25, 0.5) if i < k - 1 else rng.uniform(0.6, 2.2)
        parts.append(air_horn(rng, d, f0 * rng.uniform(0.99, 1.01), reeds))
        parts.append(np.zeros(int(FS * rng.uniform(0.08, 0.2)), np.float32))
    return np.concatenate(parts)


# -------------------------------------------------------------------- stomps
def stomp(rng):
    """Foot stomp on bleachers: low body thud + knock + short click."""
    n = int(0.35 * FS)
    t = np.arange(n) / FS
    f = rng.uniform(62, 85) + rng.uniform(40, 70) * np.exp(-t / 0.03)
    body = np.sin(2 * np.pi * np.cumsum(f) / FS) * np.exp(-t / rng.uniform(0.05, 0.1))
    kf = rng.uniform(160, 320)
    knock = np.sin(2 * np.pi * kf * t) * np.exp(-t / rng.uniform(0.02, 0.05)) * rng.uniform(0.5, 0.9)
    click = rng.standard_normal(n) * np.exp(-t / 0.006)
    click = ffilt(click[None], lambda ff: lp(ff, 1200, 2))[0] * 0.8
    y = body + knock + click
    y *= np.minimum(t / 0.002, 1)
    y /= np.sqrt(np.mean(y[:int(0.1 * FS)] ** 2)) + 1e-9
    return y.astype(np.float32)


# --------------------------------------------------------- RC car / timing
def rc_motor_run(rng, dur):
    """A Mini-Z running laps somewhere across the room: whiny electric motor
    with gear-mesh overtone and tyre hiss, speed changing through a lap, level
    changing with distance to the listener."""
    n = int(dur * FS)
    t = np.arange(n) / FS
    lap = rng.uniform(4.5, 7.0)
    # speed profile: sum of harmonics of lap rate (straights & corners)
    u = 0.55 + 0.25 * np.sin(2 * np.pi * t / lap * 3 + rng.uniform(0, 6)) + 0.15 * np.sin(2 * np.pi * t / lap * 5 + rng.uniform(0, 6))
    u = np.clip(u + 0.05 * np.convolve(rng.standard_normal(n), np.hanning(3201) / 1600, mode='same'), 0.15, 1.0)
    fm = rng.uniform(550, 750) + rng.uniform(900, 1300) * u
    ph = 2 * np.pi * np.cumsum(fm) / FS
    w = np.zeros(n)
    for h, a in [(1, 1.0), (2, 0.5), (3, 0.3), (4, 0.15), (6, 0.08)]:
        w += a * np.sin(h * ph + rng.uniform(0, 6))
    mesh = 0.35 * np.sin(ph * rng.uniform(3.3, 3.8))
    hiss = ffilt(rng.standard_normal(n)[None], lambda ff: bump(ff, 4500, 0, 1) * hp(ff, 2000, 2) * lp(ff, 9000, 2))[0]
    y = (w + mesh) * (0.3 + 0.7 * u) + 0.35 * hiss * u
    # distance to listener over the lap (1/r with near/far)
    dist = 2.0 + 1.6 * np.sin(2 * np.pi * t / lap + rng.uniform(0, 6))
    y = y / dist
    env = np.minimum(t / 0.8, 1) * np.minimum((dur - t) / 0.8, 1)
    y *= env
    y /= np.sqrt(np.mean(y ** 2)) + 1e-9
    return y.astype(np.float32)


def decoder_chirp(rng, f=None):
    """Lap-timing decoder beep (short square-ish tone)."""
    f = f or 2750.0
    d = rng.uniform(0.06, 0.09)
    n = int(d * FS)
    t = np.arange(n) / FS
    ph = 2 * np.pi * f * t
    y = np.sin(ph) + 0.3 * np.sin(3 * ph) + 0.12 * np.sin(5 * ph)
    y *= np.minimum(t / 0.003, 1) * np.minimum((d - t) / 0.005, 1)
    return (y / np.sqrt(np.mean(y ** 2))).astype(np.float32)


# ------------------------------------------------------------------ textures
def wind(rng, L, strength=1.0):
    gust = slow_curve(rng, L, FS, 0.25, power=0.7)
    g = np.clip(0.55 + 0.3 * gust, 0.1, None)
    low = noise_shaped(rng, L, lambda f: lp(f, 220, 2) * hp(f, 55, 2) / np.sqrt(np.maximum(f, 20)))
    whoosh = noise_shaped(rng, L, lambda f: bump(f, 700, 0, 1.0) * lp(f, 2500, 2) * hp(f, 200, 2))
    y = low * g + 0.5 * whoosh * g ** 2.5
    return (y * strength).astype(np.float32)


def flag_flutter(rng, L, n_flags=4):
    out = np.zeros((2, L), np.float32)
    t = np.arange(L) / FS
    for i in range(n_flags):
        env = np.clip(slow_curve(rng, L, FS, 0.15), 0, None) ** 2
        fl = rng.uniform(7, 14)
        am = 0.5 + 0.5 * np.sin(2 * np.pi * fl * t + 0.8 * np.sin(2 * np.pi * 0.7 * t)) ** 2
        nz = noise_shaped(rng, L, lambda f: bump(f, rng.uniform(600, 1400), 0, 1.0) * lp(f, 4000, 2) * hp(f, 250, 2), ch=1)[0]
        y = nz * am * env
        gl, gr = pan_gains(rng.uniform(-0.8, 0.8))
        out[0] += y * gl
        out[1] += y * gr
    return out


def room_tone(rng, L, lp_hz=5000, hp_hz=40, tilt=0.5):
    return noise_shaped(rng, L, lambda f: lp(f, lp_hz, 2) * hp(f, hp_hz, 2) / np.maximum(f, 20) ** (tilt / 2))


def rumble(rng, L, fc=70):
    return noise_shaped(rng, L, lambda f: lp(f, fc, 3) * hp(f, 18, 3) / np.maximum(f, 15) ** 0.5)


def mains_hum(rng, L, base=60.0, amps=(1, 0.5, 0.35, 0.15, 0.1, 0.05)):
    t = np.arange(L) / FS
    y = np.zeros(L)
    for k, a in enumerate(amps, 1):
        y += a * np.sin(2 * np.pi * base * k * t + rng.uniform(0, 6))
    y /= np.sqrt(np.mean(y ** 2))
    return np.stack([y, np.roll(y, 37)]).astype(np.float32)


def fan_whir(rng, L, n=3):
    t = np.arange(L) / FS
    out = np.zeros((2, L), np.float32)
    for i in range(n):
        # fan blade tone (periodic in loop: frequency on 1/loop grid)
        dur = L / FS
        f = round(rng.uniform(95, 180) * dur) / dur
        tone = np.sin(2 * np.pi * f * t) + 0.3 * np.sin(4 * np.pi * f * t + 1)
        nz = noise_shaped(rng, L, lambda ff: bump(ff, rng.uniform(250, 600), 0, 0.8) * lp(ff, 3000, 2), ch=1)[0]
        y = 0.25 * tone + nz
        gl, gr = pan_gains(rng.uniform(-0.7, 0.7))
        out[0] += y * gl
        out[1] += y * gr
    return out
