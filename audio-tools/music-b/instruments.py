"""Synthesised instruments for the music-b theme tracks. All return float arrays at SR:
mono (n,) or stereo (n,2)."""
import numpy as np
from scipy import signal
from mblib import (SR, RNG, hz, saw, pulse, sine, noise, drift, adsr, tv_filter, lpf, hpf, bpf, eq,
                   karplus, pan, fade_edges)


def _vib(n, rate, depth_c, delay_s, ramp_s=0.4):
    t = np.arange(n) / SR
    amt = np.clip((t - delay_s) / ramp_s, 0, 1)
    # slightly irregular rate
    r = rate * (1 + 0.04 * np.sin(2 * np.pi * 0.37 * t + RNG.random() * 6))
    ph = np.cumsum(r) / SR * 2 * np.pi + RNG.random() * 6.28
    return 2 ** (depth_c * amt * np.sin(ph) / 1200)


# ============================================================================ BRASS
BRASS = {
    #          attack  hlo  hhi  cap    rel   scoop breath_f  breath_lvl
    'horn':    (0.045, 1.3, 6.5, 2600, 0.22, 45, 900, 0.018),
    'trumpet': (0.022, 1.8, 11., 6500, 0.14, 35, 1800, 0.022),
    'trombone': (0.035, 1.4, 8.0, 3400, 0.18, 40, 700, 0.02),
    'tuba':    (0.05, 1.2, 5.0, 1200, 0.22, 30, 400, 0.01),
}


def brass(m, dur, vel=0.8, kind='horn', n_voices=3, spread=0.5, bright=1.0, legato=False,
          fall=0.0, vib=True, release=None):
    """One note of a brass section (n_voices players, detuned, staggered). Returns stereo."""
    att, hlo, hhi, cap, rel, scoop, bf, bl = BRASS[kind]
    if release is not None:
        rel = release
    att *= (1.25 - 0.5 * vel)
    f0 = float(hz(m))
    n = int((dur + rel + 0.05) * SR)
    t = np.arange(n) / SR
    out = np.zeros((2, n))
    for v in range(n_voices):
        det = 2 ** (RNG.normal(0, 5.5) / 1200)
        dly = int(RNG.uniform(0, 0.014 if not legato else 0.006) * SR)
        sc = scoop * RNG.uniform(0.5, 1.2) * (0.35 if legato else 1.0)
        pitch = det * 2 ** (-sc * np.exp(-t / 0.035) / 1200) * drift(n, 3.0, 0.8)
        if vib and dur > 0.7:
            pitch = pitch * _vib(n, RNG.uniform(4.8, 5.6), RNG.uniform(5, 9), 0.35)
        if fall > 0:
            tf = np.clip((t - (dur - 0.12)) / 0.18, 0, 1)
            pitch = pitch * 2 ** (-fall * tf ** 2 / 12)
        x = saw(f0 * pitch)
        x = np.roll(x, dly); x[:dly] = 0
        p = spread * ((2 * v / max(1, n_voices - 1)) - 1) if n_voices > 1 else 0
        p += RNG.normal(0, 0.08)
        th = (np.clip(p, -1, 1) + 1) * np.pi / 4
        out[0] += x * np.cos(th); out[1] += x * np.sin(th)
    # amplitude envelope with a little attack overshoot
    A = adsr(n, att, 0.25, 0.78, rel, dur_s=dur)
    A *= 1 + 0.25 * vel * np.exp(-((t - att * 1.3) / (att + 0.02)) ** 2)
    if fall > 0:
        A *= np.where(t > dur - 0.05, np.clip(1 - (t - dur + 0.05) / 0.2, 0, 1) ** 1.5, 1.0)
    Ab = np.clip(A, 0, None) / A.max()
    hmax = hlo + (hhi - hlo) * (0.35 + 0.65 * vel) * bright
    fc = f0 * (hlo + (hmax - hlo) * Ab ** 1.4)
    fc = np.minimum(fc, cap * (0.6 + 0.4 * vel) * bright)
    fc = np.maximum(fc, 120)
    y = tv_filter(out, fc, q=0.6, block=96, stages=2) * A
    # breath noise at the attack + faint sustained air
    nb = noise(n)
    be = np.exp(-t / 0.03) * (t >= 0) + 0.12 * A
    br = bpf(nb, bf * 0.7, bf * 1.8) * be * bl * (0.5 + vel)
    y[0] += br; y[1] += br
    return (y.T / n_voices ** 0.5) * (0.35 + 0.65 * vel)


# ============================================================================ STRINGS
def strings(ms, dur, vel=0.6, attack=0.3, release=0.6, n_voices=5, spread=0.85, bright=1.0,
            vib_depth=11, pan_center=0.0):
    """Sustained bowed-string ensemble chord (list of midi). Stereo."""
    n = int((dur + release + 0.05) * SR)
    t = np.arange(n) / SR
    out = np.zeros((2, n))
    for m in ms:
        f0 = float(hz(m))
        for v in range(n_voices):
            det = 2 ** (RNG.normal(0, 7) / 1200)
            pitch = det * drift(n, 3.0, 0.9) * _vib(n, RNG.uniform(4.6, 6.0), vib_depth * RNG.uniform(0.6, 1.2),
                                                    RNG.uniform(0.15, 0.5), 0.6)
            x = saw(f0 * pitch)
            dly = int(RNG.uniform(0, 0.03) * SR)
            x = np.roll(x, dly); x[:dly] = 0
            p = pan_center + spread * ((2 * v / max(1, n_voices - 1)) - 1) + RNG.normal(0, 0.1)
            th = (np.clip(p, -1, 1) + 1) * np.pi / 4
            out[0] += x * np.cos(th); out[1] += x * np.sin(th)
    A = adsr(n, attack, 0.4, 0.9, release, dur_s=dur, curve=2.0)
    A = A ** 1.3
    fmid = float(hz(np.mean(ms)))
    fc = np.minimum(fmid * (2.5 + 5.5 * A * vel * bright), 5500 * bright)
    fc = np.maximum(fc, 200)
    y = tv_filter(out, fc, q=0.55, block=256, stages=2) * A
    # bow noise / rosin air
    bn = bpf(noise(n), 2500, 7000) * A * 0.015 * vel
    y[0] += bn; y[1] += np.roll(bn, 37)
    return y.T / np.sqrt(len(ms) * n_voices) * (0.4 + 0.6 * vel)


_spic_cache = {}


def spiccato(m, vel=0.7, length=0.15, variant=None):
    """Short bowed (spiccato) ensemble note with a Karplus-Strong 'bite' layer. Stereo."""
    vb = int(round(vel * 4))
    var = RNG.integers(0, 3) if variant is None else variant
    key = (m, vb, var, round(length, 2))
    if key in _spic_cache:
        return _spic_cache[key]
    f0 = float(hz(m))
    n = int((length + 0.25) * SR)
    t = np.arange(n) / SR
    out = np.zeros((2, n))
    nv = 3
    for v in range(nv):
        det = 2 ** (RNG.normal(0, 6) / 1200)
        x = saw(f0 * det * 2 ** (-12 * np.exp(-t / 0.02) / 1200))
        dly = int(RNG.uniform(0, 0.008) * SR)
        x = np.roll(x, dly); x[:dly] = 0
        th = (np.clip(0.5 * (v - 1) + RNG.normal(0, 0.1), -1, 1) + 1) * np.pi / 4
        out[0] += x * np.cos(th); out[1] += x * np.sin(th)
    A = np.minimum(t / 0.006, 1) * np.where(t < length, np.exp(-t / (length * 0.9)),
                                            np.exp(-length / (length * 0.9)) * np.exp(-(t - length) / 0.05))
    fc = f0 * (2.0 + 9 * vb / 4 * np.exp(-t / 0.05)) + 300
    fc = np.minimum(fc, 7000)
    y = tv_filter(out, fc, q=0.6, block=64, stages=2) * A / np.sqrt(nv)
    ks = karplus(f0, n / SR, bright=0.35 + 0.1 * vb, decay=0.985)
    ks = ks / (np.abs(ks).max() + 1e-9) * np.exp(-t / 0.12)
    scr = bpf(noise(n), 1500, 4500) * np.exp(-t / 0.006) * 0.06
    y[0] += 0.28 * ks + scr; y[1] += 0.28 * ks + np.roll(scr, 11)
    y = y.T * (0.3 + 0.7 * vel)
    _spic_cache[key] = y
    return y


# ============================================================================ MALLETS / KEYS
def glock(m, vel=0.7, length=2.5):
    f0 = float(hz(m))
    n = int(length * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    for r, a, d in [(1, 1.0, 1.9), (2.76, 0.42, 0.55), (5.40, 0.18, 0.22), (8.93, 0.07, 0.09)]:
        f = f0 * r
        if f < SR * 0.45:
            y += a * np.sin(2 * np.pi * f * t + RNG.random()) * np.exp(-t * 6.9 / (d * (1.5 - 0.5 * (f0 / 2000))))
    y *= np.minimum(t / 0.0008, 1)
    y += hpf(noise(n), 3000) * np.exp(-t / 0.0015) * 0.15
    return y * vel


def bellpiano(m, vel=0.6, dur=1.0, length=None):
    """Felt-piano / celesta hybrid: stretched harmonic partials, two-stage decay, unison beating."""
    f0 = float(hz(m))
    length = length or dur + 2.5
    n = int(length * SR)
    t = np.arange(n) / SR
    y = np.zeros(n)
    B = 0.00035
    bright = 0.55 + 0.35 * vel
    for k in range(1, 13):
        fk = k * f0 * np.sqrt(1 + B * k * k)
        if fk > 12000:
            break
        a = (1 / k ** 1.25) * bright ** (k - 1)
        tp = 0.9 / (1 + 0.45 * k) * (440 / f0) ** 0.3
        ta = 5.0 / (1 + 0.35 * k) * (440 / f0) ** 0.3
        env = 0.72 * np.exp(-t / tp) + 0.28 * np.exp(-t / ta)
        det = 2 ** (RNG.uniform(0.3, 1.2) / 1200)
        ph = RNG.random() * 6.28
        y += a * env * 0.5 * (np.sin(2 * np.pi * fk * t + ph) + np.sin(2 * np.pi * fk * det * t + ph))
    # bell sheen partial
    y += 0.12 * vel * np.sin(2 * np.pi * f0 * 4.07 * t) * np.exp(-t / 0.7)
    y *= np.minimum(t / 0.002, 1)
    y += lpf(noise(n), 2500) * np.exp(-t / 0.006) * 0.05 * vel
    # damper
    off = t > dur
    y[off] *= np.exp(-(t[off] - dur) / 0.25)
    return y * vel * 0.5


# ============================================================================ PERCUSSION (modal)
def _resonator(x, f, t60, amp):
    r = np.exp(-6.91 / (t60 * SR))
    w = 2 * np.pi * f / SR
    return amp * signal.lfilter([0, r * np.sin(w)], [1, -2 * r * np.cos(w), r * r], x)


def mallet_excitation(n, hits, width_ms=6.0, noise_amt=0.15):
    """hits: list of (sample_offset, vel)."""
    x = np.zeros(n)
    for off, v in hits:
        w = int(width_ms * (1.4 - 0.6 * v) / 1000 * SR)
        if off + w >= n or off < 0:
            continue
        x[off:off + w] += v * np.sin(np.pi * np.arange(w) / w) * (np.pi / (2 * w))
    if noise_amt:
        x += lpf(noise(n), 3000) * signal.lfilter([1], [1, -0.995], np.abs(x)) * 0.003 * noise_amt
    return x


TIMP_MODES = [(1.0, 1.0, 3.0), (1.504, 0.55, 2.0), (1.742, 0.35, 1.5), (2.0, 0.28, 1.3), (2.245, 0.18, 1.0),
              (2.494, 0.13, 0.85), (2.8, 0.08, 0.7), (3.02, 0.06, 0.6)]


def timpani(m, hits, length, bright=1.0):
    """hits: list of (time_s, vel) relative to start; returns mono."""
    n = int(length * SR)
    f0 = float(hz(m))
    ex = mallet_excitation(n, [(int(ts * SR), v) for ts, v in hits], width_ms=7.0 / bright)
    y = np.zeros(n)
    for r, a, d in TIMP_MODES:
        y += _resonator(ex, f0 * r, d, a * bright ** (r - 1))
    y += _resonator(ex, f0 * 0.55, 0.22, 0.9)  # air thump
    y = hpf(y, 35)
    return y * 0.6


def tom(f0, vel=0.8, length=1.2, decay=0.6, bend=1.5):
    n = int(length * SR); t = np.arange(n) / SR
    f = f0 * (1 + (bend - 1) * np.exp(-t / 0.035))
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 6.9 / decay)
    for r, a, d in [(1.59, 0.35, 0.35), (2.14, 0.22, 0.25), (2.30, 0.15, 0.2), (2.65, 0.1, 0.15)]:
        y += a * np.sin(2 * np.pi * np.cumsum(f * r) / SR) * np.exp(-t * 6.9 / (decay * d / 0.6))
    y += bpf(noise(n), 200, 2500) * np.exp(-t / 0.012) * 0.5
    y *= np.minimum(t / 0.001, 1)
    return np.tanh(y * 1.3 * vel) * 0.8


def taiko(f0=58, vel=1.0, length=2.0):
    n = int(length * SR); t = np.arange(n) / SR
    f = f0 * (1 + 0.45 * np.exp(-t / 0.05))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 6.9 / 1.3)
    for r, a, d in [(1.59, 0.3, 0.5), (2.14, 0.18, 0.35), (2.65, 0.1, 0.25)]:
        body += a * np.sin(2 * np.pi * np.cumsum(f * r) / SR) * np.exp(-t * 6.9 / d)
    slap = bpf(noise(n), 120, 1100) * np.exp(-t / 0.025) * 0.9
    click = hpf(noise(n), 1500) * np.exp(-t / 0.003) * 0.25
    y = body + slap + click
    y *= np.minimum(t / 0.0015, 1)
    return np.tanh(y * 1.5 * vel) * 0.85


_cym_cache = {}


def crash(length=4.5, variant=0, bright=1.0):
    key = (round(length, 2), variant, bright)
    if key in _cym_cache:
        return _cym_cache[key]
    rng = np.random.default_rng(100 + variant)
    n = int(length * SR); t = np.arange(n) / SR
    out = np.zeros((n, 2))
    for ch in range(2):
        y = np.zeros(n)
        fr = np.exp(rng.uniform(np.log(380), np.log(13000), 70))
        for f in fr:
            d = rng.uniform(0.8, 3.8) * (1.2 if f > 3000 else 0.8)
            y += rng.uniform(0.3, 1) * (f / 1000) ** -0.25 * np.sin(2 * np.pi * f * t + rng.random() * 6.3) * np.exp(-t * 6.9 / d)
        y = y / 70 ** 0.5 * 0.35
        nz = rng.standard_normal(n)
        wash = hpf(lpf(nz, 11000 * bright), 2500) * (0.55 * np.exp(-t * 6.9 / 3.8) + 0.8 * np.exp(-t / 0.06))
        y = y + wash
        y *= np.minimum(t / 0.0012, 1)
        # partials: slow 'bloom' - high content arrives a little later
        out[:, ch] = hpf(y, 400)
    out = out / np.abs(out).max() * 0.8
    _cym_cache[key] = out
    return out


def reverse_cymbal(length=2.4, variant=3):
    c = crash(length + 0.3, variant)[:int(length * SR)]
    r = c[::-1].copy()
    t = np.arange(len(r)) / SR
    r *= (t / t[-1])[:, None] ** 1.5
    fade_edges(r, 0.05, 0.005)
    return r


def ride_bell(length=1.2, vel=0.6):
    n = int(length * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    for f, a, d in [(2340, 1, 1.0), (3180, .6, .7), (5420, .35, .45), (7600, .2, .3), (1230, .3, .6)]:
        y += a * np.sin(2 * np.pi * f * t) * np.exp(-t * 6.9 / d)
    y += hpf(noise(n), 6000) * np.exp(-t / 0.2) * 0.3
    return y * vel * 0.3


# ============================================================================ DRUM KIT
def kick(vel=1.0, f_end=48, f_start=190, decay=0.38, click=0.4, length=0.6, drive=1.4):
    n = int(length * SR); t = np.arange(n) / SR
    f = f_end + (f_start - f_end) * np.exp(-t / 0.028)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 6.9 / decay)
    y += hpf(bpf(noise(n), 2500, 9000), 1000) * np.exp(-t / 0.0025) * click
    y += np.sin(2 * np.pi * 3200 * t) * np.exp(-t / 0.002) * click * 0.5
    y = np.tanh(y * drive) / np.tanh(drive)
    y *= np.minimum(t / 0.0005, 1)
    return fade_edges(y * vel, 0.0002, 0.02)


def snare(vel=1.0, tone=190, decay=0.2, snappy=0.8, length=0.7, body=0.9):
    n = int(length * SR); t = np.arange(n) / SR
    f = tone * (1 + 0.25 * np.exp(-t / 0.01))
    b = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.06)
    b += 0.5 * np.sin(2 * np.pi * np.cumsum(f * 1.72) / SR) * np.exp(-t / 0.035)
    nz = bpf(noise(n), 1200, 10000) * np.exp(-t * 6.9 / decay) * snappy
    nz += bpf(noise(n), 3000, 12000) * np.exp(-t / 0.004) * 0.6
    y = body * b + nz
    y *= np.minimum(t / 0.0007, 1)
    return fade_edges(np.tanh(y * 1.2 * vel) * 0.8, 0.0002, 0.02)


def clap(vel=1.0, length=0.5):
    n = int(length * SR); t = np.arange(n) / SR
    env = np.zeros(n)
    for k, o in enumerate([0, 0.009, 0.019, 0.028]):
        i = int(o * SR)
        env[i:] += np.exp(-(t[:n - i]) / (0.004 if k < 3 else 0.12)) * (1 if k < 3 else 1.1)
    y = bpf(noise(n), 900, 5500) * env
    return fade_edges(y * vel * 0.5, 0.0005, 0.02)


def hat(vel=0.6, open_=False, length=None):
    length = length or (0.5 if open_ else 0.12)
    n = int(length * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    for f in [317, 421, 529, 613, 797, 881]:
        y += np.sign(np.sin(2 * np.pi * f * 7.3 * t + RNG.random() * 6))
    y = y * 0.15 + noise(n) * 0.5
    y = hpf(bpf(y, 6000, 15000), 7000)
    d = 0.25 if open_ else 0.035
    y *= np.exp(-t * 6.9 / (d * 6.9 / 3)) * np.minimum(t / 0.0005, 1)
    return fade_edges(y * vel * 0.5, 0.0002, 0.01)


def shaker(vel=0.4, length=0.1):
    n = int(length * SR); t = np.arange(n) / SR
    env = np.sin(np.pi * np.clip(t / 0.07, 0, 1)) ** 2
    return bpf(noise(n), 4000, 12000) * env * vel * 0.3


# ============================================================================ SYNTHS
def synthbass(m, dur, vel=0.8, cut=900, res=0.9, sub=0.7, env_amt=1.0, drive=1.5):
    f0 = float(hz(m))
    n = int((dur + 0.06) * SR); t = np.arange(n) / SR
    x = 0.6 * saw(f0 * 2 ** (RNG.normal(0, 3) / 1200), n) + 0.5 * saw(f0 * 2 ** (RNG.normal(0, 3) / 1200) * 1.004, n)
    fc = 90 + f0 * 1.5 + cut * env_amt * vel * np.exp(-t / 0.09) + cut * 0.25
    y = tv_filter(x, fc, q=res, block=64, stages=2)
    y += sub * np.sin(2 * np.pi * f0 * t)
    A = adsr(n, 0.003, 0.15, 0.75, 0.04, dur_s=dur)
    y = np.tanh(y * A * drive) / np.tanh(drive)
    return y * vel


def supersaw(m, dur, vel=0.7, voices=5, det_c=14, cut=4000, attack=0.01, release=0.25, glide_from=None):
    f0 = float(hz(m))
    n = int((dur + release + 0.02) * SR); t = np.arange(n) / SR
    out = np.zeros((2, n))
    ptc = np.ones(n)
    if glide_from is not None:
        ptc = 2 ** ((glide_from - m) * np.exp(-t / 0.03) / 12)
    for v in range(voices):
        c = det_c * ((2 * v / (voices - 1)) - 1) if voices > 1 else 0
        x = saw(f0 * ptc * 2 ** (c / 1200) * drift(n, 2))
        th = (np.clip(0.8 * ((2 * v / max(1, voices - 1)) - 1), -1, 1) + 1) * np.pi / 4
        out[0] += x * np.cos(th); out[1] += x * np.sin(th)
    sq = pulse(f0 * ptc, 0.5, n) * 0.5
    out += sq
    A = adsr(n, attack, 0.3, 0.8, release, dur_s=dur)
    fc = np.minimum(cut * (0.5 + 0.5 * A), 12000)
    y = tv_filter(out, fc, q=0.7, block=128, stages=2) * A
    return y.T / np.sqrt(voices) * vel


def riser(length, f_lo=250, f_hi=7000, pitch=True, m0=50, curve=2.0):
    n = int(length * SR); t = np.arange(n) / SR
    fr = t / length
    fc = f_lo * (f_hi / f_lo) ** (fr ** 1.3)
    nz = np.stack([noise(n), noise(n)])
    y = tv_filter(nz, fc, q=2.5, kind='bp', block=128, stages=1)
    if pitch:
        f = float(hz(m0)) * 2 ** (fr ** 1.5 * 1.0)  # one octave rise
        s = np.zeros((2, n))
        for d in (-12, 0, 12):
            s[0] += saw(f * 2 ** (d / 1200)); s[1] += saw(f * 2 ** (-d / 1200) * 1.002)
        s = tv_filter(s, np.minimum(f * 6, 9000), q=0.8, block=256, stages=2)
        y = y + 0.18 * s
    A = fr ** curve
    y = y * A
    fade_edges(y.T, 0.01, 0.003)
    return y.T * 0.5


def impact(vel=1.0, length=3.0):
    n = int(length * SR); t = np.arange(n) / SR
    f = 32 + 55 * np.exp(-t / 0.18)
    boom = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 6.9 / 1.8)
    hit = lpf(noise(n), 2500) * np.exp(-t / 0.05) * 0.6
    y = np.tanh((boom + hit) * 2.0) * 0.8
    y *= np.minimum(t / 0.001, 1)
    return y * vel


def guitar_di(ms, dur, vel=0.8, mute=False, strum_ms=7, up=False):
    """Clean DI of a power chord/strum (to be distorted at track level). Mono."""
    n = int((dur + (0.03 if mute else 0.12)) * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    order = ms[::-1] if up else ms
    for i, m in enumerate(order):
        f0 = float(hz(m)) * 2 ** (RNG.normal(0, 3) / 1200)
        ptc = drift(n, 2.5, 1.5) * 2 ** (10 * np.exp(-t / 0.01) / 1200)
        x = 0.65 * saw(f0 * ptc) + 0.35 * pulse(f0 * ptc * 1.001, 0.3)
        d = int((i * strum_ms + RNG.uniform(0, 2)) / 1000 * SR)
        x = np.roll(x, d); x[:d] = 0
        y += x
    if mute:
        fc = 250 + 1100 * vel * np.exp(-t / 0.02)
        A = np.minimum(t / 0.002, 1) * np.exp(-t / 0.075)
    else:
        fc = 1200 + 3500 * vel * np.exp(-t / 0.15)
        A = adsr(n, 0.002, 0.8, 0.55, 0.06, dur_s=dur)
    y = tv_filter(y, fc, q=0.7, block=64, stages=1) * A
    y += hpf(noise(n), 2000) * np.exp(-t / 0.004) * 0.3 * vel
    return y * vel / np.sqrt(len(ms))


def amp_sim(di, drive=18.0, tight=140, cab_lp=4800):
    """Pre-EQ -> 4x oversampled asymmetric tanh -> cab sim EQ. di (n,) -> (n,)"""
    from mblib import oversampled
    x = hpf(di, tight, 2)
    x = eq(x, [('peak', 900, 0.8, 4.0)])

    def ws(u):
        u = u * drive
        return np.tanh(u + 0.15) - np.tanh(0.15) + 0.2 * np.tanh(u * 0.3)
    y = oversampled(ws, x[:, None], os=4)[:, 0]
    # cab: 4x12-ish: resonant low bump, mid scoop, steep top roll-off
    y = eq(y, [('hp', 85, 0.7, 0), ('peak', 110, 1.2, 3.0), ('peak', 420, 1.0, -4.0),
               ('peak', 2400, 1.2, -3.5), ('peak', 3600, 2.0, 2.0),
               ('lp', cab_lp, 0.9, 0), ('lp', cab_lp * 1.1, 0.7, 0)])
    return y * 0.4


# ============================================================================ extra (Race Day Live)
_ride_cache = {}


def ride(vel=0.6, length=1.6, variant=0):
    key = (variant, round(length, 2))
    if key not in _ride_cache:
        rng = np.random.default_rng(300 + variant)
        n = int(length * SR); t = np.arange(n) / SR
        y = np.zeros(n)
        for f in np.exp(rng.uniform(np.log(2500), np.log(11000), 30)):
            y += rng.uniform(0.3, 1) * np.sin(2 * np.pi * f * t + rng.random() * 6.3) * np.exp(-t * 6.9 / rng.uniform(0.6, 1.6))
        y = y / 30 ** 0.5 * 0.4
        y += hpf(rng.standard_normal(n), 5000) * (0.25 * np.exp(-t / 0.35) + 0.6 * np.exp(-t / 0.008))
        y += 0.35 * ride_bell(length, 1.0)[:n] * 2
        y *= np.minimum(t / 0.0006, 1)
        _ride_cache[key] = hpf(y, 1500) * 0.5
    return _ride_cache[key] * vel


def pluck(m, dur, vel=0.6, cut=2500, decay=0.18):
    """Synth pluck: detuned saw pair + square, fast filter envelope."""
    f0 = float(hz(m))
    n = int((dur + 0.25) * SR); t = np.arange(n) / SR
    x = saw(f0 * 2 ** (6 / 1200), n) + saw(f0 * 2 ** (-6 / 1200), n) + 0.6 * pulse(f0, 0.35, n)
    fc = 200 + cut * vel * np.exp(-t / (decay * 0.5)) + f0
    y = tv_filter(x, fc, q=1.2, block=64, stages=2)
    A = np.minimum(t / 0.002, 1) * np.exp(-t / decay)
    return y * A * vel * 0.4


def downlifter(length=2.0):
    n = int(length * SR); t = np.arange(n) / SR
    fr = t / length
    fc = 6000 * (200 / 6000) ** (fr ** 0.7)
    nz = np.stack([noise(n), noise(n)])
    y = tv_filter(nz, fc, q=1.5, kind='bp', block=128, stages=1)
    A = (1 - fr) ** 2 * np.minimum(t / 0.01, 1)
    return (y * A).T * 0.5


def gated_snare_verb(sig, length=0.28):
    """Gated-reverb flavour for the big broadcast snare: dense short noise tail following the hit."""
    n = len(sig) + int(length * SR)
    t = np.arange(int(length * SR)) / SR
    tail = bpf(noise(len(t)), 300, 7000) * np.where(t < length * 0.75, 1.0, np.exp(-(t - length * 0.75) / 0.015))
    tail = tail * 0.12
    out = np.zeros(n)
    out[:len(sig)] += sig
    out[int(0.012 * SR):int(0.012 * SR) + len(tail)] += tail[:n - int(0.012 * SR)]
    return out
