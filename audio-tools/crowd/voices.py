"""Source-filter talker synthesis for crowd babble and cheering.

Each talker = glottal pulse train (Rosenberg-style flow derivative, polyBLEP
anti-aliased, per-talker F0 with jitter/shimmer and intonation contours) passed
through a time-varying 4-formant cascade envelope applied in a circular STFT,
plus aspiration and fricative / burst noise. Phones are laid out on a 4 ms
frame grid (syllables, consonant onsets/codas, pauses, cheers, laughs).
"""
import numpy as np
from dsp import FS, HOP, FR, NB, FREQS, NOISE_SCALE, stft_c, istft_c, pan_gains

# adult-male reference formants (Hillenbrand/Peterson-Barney ballpark)
VOW = {
    'i': (270, 2290, 3010), 'I': (390, 1990, 2550), 'e': (530, 1840, 2480),
    'ae': (660, 1720, 2410), 'a': (730, 1090, 2440), 'o': (570, 840, 2410),
    'U': (440, 1020, 2240), 'u': (300, 870, 2240), 'A': (640, 1190, 2390),
    'r': (490, 1350, 1690), '@': (500, 1500, 2500), 'E': (580, 1800, 2500),
}
DIPH = {'ai': ('a', 'I'), 'ei': ('e', 'i'), 'ou': ('o', 'U'), 'au': ('a', 'U'), 'oi': ('o', 'I')}
SPEECH_V = ['@', 'I', 'e', 'ae', 'A', 'a', 'i', 'o', 'U', 'u', 'r', 'E', 'ai', 'ei', 'ou', 'au']
SPEECH_VP = np.array([18, 12, 8, 7, 9, 6, 7, 5, 3, 4, 4, 5, 3, 3, 3, 2], float)
SPEECH_VP /= SPEECH_VP.sum()

CONS = {
    's': dict(k='fric', fc=6200, fw=0.40, lvl=0.30, loc=(330, 1700, 2600)),
    'sh': dict(k='fric', fc=3300, fw=0.45, lvl=0.28, loc=(330, 1900, 2500)),
    'f': dict(k='fric', fc=5000, fw=1.20, lvl=0.08, loc=(330, 1000, 2300)),
    'th': dict(k='fric', fc=5500, fw=1.30, lvl=0.06, loc=(330, 1400, 2500)),
    'h': dict(k='asp', lvl=0.22),
    'z': dict(k='vfric', fc=6000, fw=0.45, lvl=0.16, loc=(300, 1700, 2600)),
    'v': dict(k='vfric', fc=4500, fw=1.20, lvl=0.06, loc=(300, 1000, 2300)),
    'p': dict(k='stop', fc=1400, fw=1.30, lvl=0.35, loc=(300, 900, 2200), vl=True),
    't': dict(k='stop', fc=4500, fw=0.80, lvl=0.45, loc=(300, 1800, 2600), vl=True),
    'k': dict(k='stop', fc=2300, fw=0.60, lvl=0.40, loc=(300, 2000, 2400), vl=True),
    'b': dict(k='stop', fc=1200, fw=1.30, lvl=0.15, loc=(300, 900, 2200), vl=False),
    'd': dict(k='stop', fc=4000, fw=0.80, lvl=0.18, loc=(300, 1800, 2600), vl=False),
    'g': dict(k='stop', fc=2200, fw=0.60, lvl=0.18, loc=(300, 2000, 2400), vl=False),
    'm': dict(k='nasal', loc=(280, 1000, 2300)),
    'n': dict(k='nasal', loc=(280, 1500, 2500)),
    'l': dict(k='liq', loc=(360, 1100, 2800)),
    'r': dict(k='liq', loc=(360, 1200, 1600)),
    'w': dict(k='liq', loc=(310, 750, 2200)),
    'y': dict(k='liq', loc=(280, 2200, 2900)),
}
ONSETS = ['t', 'n', 's', 'r', 'l', 'd', 'k', 'm', 'w', 'h', 'th', 'b', 'p', 'g', 'f', 'y', 'v', 'z', 'sh']
ONSET_P = np.array([10, 8, 8, 6, 6, 6, 5, 5, 5, 5, 6, 4, 4, 3, 3, 2, 2, 2, 2], float)
ONSET_P /= ONSET_P.sum()
CODAS = ['n', 't', 's', 'r', 'l', 'd', 'k', 'm', 'z', 'th']
CODA_P = np.array([10, 9, 7, 5, 5, 5, 4, 4, 3, 2], float)
CODA_P /= CODA_P.sum()

_F2 = FREQS.astype(np.float32) ** 2
_LOGF = np.log2(np.maximum(FREQS, 20.0)).astype(np.float32)
_HPV = (FREQS / np.sqrt(FREQS ** 2 + 140.0 ** 2)).astype(np.float32)   # gentle 1st-order HP
# empirical correction so conversational speech matches the long-term average
# speech spectrum (Byrne et al. 1994: ~-5 dB @1k, -11 @2k, -16 @4k re 500 Hz)
_CORR_F = [100, 500, 1000, 2000, 3000, 4000, 5000, 8000, 16000]
_CORR_DB = [0, 0, 4, 8, 12, 18, 22, 24, 20]
_CORR = (10 ** (np.interp(np.log2(np.maximum(FREQS, 50)), np.log2(_CORR_F), _CORR_DB) / 20)).astype(np.float32)


class Talker:
    """A virtual person: fixed voice + position (shared by bed and roar)."""

    def __init__(self, rng, kind, dist, pan):
        self.kind = kind
        self.dist = dist
        self.pan = pan
        if kind == 'male':
            self.f0 = float(np.clip(rng.lognormal(np.log(118), 0.13), 85, 165))
            self.vt = rng.uniform(0.95, 1.05)
            self.oq = rng.uniform(0.55, 0.65)
            self.breath = rng.uniform(0.03, 0.08)
            self.shout_max = 420
        elif kind == 'female':
            self.f0 = float(np.clip(rng.lognormal(np.log(208), 0.11), 160, 265))
            self.vt = rng.uniform(1.12, 1.2)
            self.oq = rng.uniform(0.62, 0.72)
            self.breath = rng.uniform(0.06, 0.13)
            self.shout_max = 560
        elif kind == 'teen':
            self.f0 = float(np.clip(rng.lognormal(np.log(190), 0.25), 120, 300))
            self.vt = rng.uniform(1.08, 1.22)
            self.oq = rng.uniform(0.58, 0.7)
            self.breath = rng.uniform(0.05, 0.12)
            self.shout_max = 620
        else:  # child
            self.f0 = float(np.clip(rng.lognormal(np.log(270), 0.1), 220, 340))
            self.vt = rng.uniform(1.25, 1.35)
            self.oq = rng.uniform(0.62, 0.72)
            self.breath = rng.uniform(0.07, 0.14)
            self.shout_max = 700
        self.rate = rng.uniform(0.85, 1.15)          # speech-rate factor
        self.jitter = rng.uniform(0.004, 0.012)
        self.shimmer = rng.uniform(0.04, 0.09)
        self.shout_mult = rng.uniform(1.55, 2.2)      # F0 raise when cheering
        self.fscale = rng.uniform(0.96, 1.04)         # individual formant scatter
        self.seed = int(rng.integers(1 << 31))


class Track:
    """Frame-rate control parameters for one talker over the loop."""

    def __init__(self, M):
        self.M = M
        self.f0 = np.full(M, np.nan, np.float32)
        self.F = np.full((3, M), np.nan, np.float32)
        self.va = np.zeros(M, np.float32)
        self.asp = np.zeros(M, np.float32)
        self.fa = np.zeros(M, np.float32)
        self.fc = np.full(M, 4000, np.float32)
        self.fw = np.full(M, 0.8, np.float32)
        self.eff = np.zeros(M, np.float32)
        self.vib = np.zeros(M, np.float32)


# ------------------------------------------------------------------ writers
class Writer:
    def __init__(self, tr, tk, rng, eff=0.0):
        self.tr, self.tk, self.rng, self.eff = tr, tk, rng, eff
        self.M = tr.M

    def fmt(self, v, eff=None):
        eff = self.eff if eff is None else eff
        F1, F2, F3 = VOW[v]
        s = self.tk.vt * self.tk.fscale
        F1 = F1 * s + eff * 130 * s
        F2 = F2 * s + eff * 60 * s
        return np.array([F1, F2, F3 * s], np.float32)

    def loc(self, c):
        s = self.tk.vt * self.tk.fscale
        return np.array(CONS[c]['loc'], np.float32) * s

    def _cl(self, m0, n):
        m0 = max(0, int(m0))
        m1 = min(self.M, m0 + max(1, int(n)))
        return m0, m1

    def vowel(self, m, n, v, f0a, f0b, amp, att=3, rel=4, v2=None, eff=None, asp=0.0,
              vib=0.0, f0pts=None, env=None):
        m0, m1 = self._cl(m, n)
        k = m1 - m0
        if k <= 0:
            return m1
        eff = self.eff if eff is None else eff
        if v in DIPH:
            v, v2 = DIPH[v]
        Fa = self.fmt(v, eff)
        if v2 is not None:
            Fb = self.fmt(v2, eff)
            x = np.linspace(0, 1, k)
            x = x * x * (3 - 2 * x)
            self.tr.F[:, m0:m1] = Fa[:, None] * (1 - x) + Fb[:, None] * x
        else:
            self.tr.F[:, m0:m1] = Fa[:, None]
        if f0pts is not None:
            xs, ys = f0pts
            self.tr.f0[m0:m1] = np.interp(np.linspace(0, 1, k), xs, ys)
        else:
            self.tr.f0[m0:m1] = np.linspace(f0a, f0b, k)
        e = np.ones(k, np.float32) if env is None else np.interp(np.linspace(0, 1, k), *env).astype(np.float32)
        a = min(att, k)
        r = min(rel, k)
        e[:a] *= np.linspace(0.05, 1, a)
        e[k - r:] *= np.linspace(1, 0.05, r)
        self.tr.va[m0:m1] = np.maximum(self.tr.va[m0:m1], amp * e)
        self.tr.eff[m0:m1] = eff
        self.tr.vib[m0:m1] = vib
        if asp:
            self.tr.asp[m0:m1] = np.maximum(self.tr.asp[m0:m1], asp * amp * e)
        return m1

    def cons(self, m, c, amp, f0, next_v=None, dur_scale=1.0):
        """Write consonant c starting at frame m; returns end frame."""
        rng = self.rng
        d = CONS[c]
        k = d['k']
        tr = self.tr
        if k == 'asp':
            n = int(FR * rng.uniform(0.04, 0.07) * dur_scale)
            m0, m1 = self._cl(m, n)
            tr.asp[m0:m1] = np.maximum(tr.asp[m0:m1], d['lvl'] * amp * np.hanning(m1 - m0 + 2)[1:-1])
            if next_v:
                tr.F[:, m0:m1] = self.fmt(next_v if next_v not in DIPH else DIPH[next_v][0])[:, None]
            tr.f0[m0:m1] = f0
            return m1
        L3 = self.loc(c)
        if k in ('fric', 'vfric'):
            n = int(FR * rng.uniform(0.06, 0.1) * (0.75 if k == 'vfric' else 1) * dur_scale)
            m0, m1 = self._cl(m, n)
            w = np.hanning(m1 - m0 + 4)[2:-2] ** 0.5
            tr.fa[m0:m1] = d['lvl'] * amp * w * rng.uniform(0.7, 1.3)
            tr.fc[m0:m1] = d['fc'] * rng.uniform(0.9, 1.1) * self.tk.vt ** 0.5
            tr.fw[m0:m1] = d['fw']
            tr.F[:, m0:m1] = L3[:, None]
            tr.f0[m0:m1] = f0
            if k == 'vfric':
                tr.va[m0:m1] = np.maximum(tr.va[m0:m1], 0.25 * amp * w)
            return m1
        if k == 'stop':
            ncl = int(FR * rng.uniform(0.03, 0.055) * dur_scale)
            m0, m1 = self._cl(m, ncl)
            tr.F[:, m0:m1] = L3[:, None]
            tr.f0[m0:m1] = f0
            if not d['vl']:
                tr.va[m0:m1] = np.maximum(tr.va[m0:m1], 0.06 * amp)
            b0, b1 = self._cl(m1, 2)
            tr.fa[b0:b1] = d['lvl'] * amp * np.array([1.0, 0.5])[:b1 - b0]
            tr.fc[b0:b1] = d['fc'] * self.tk.vt ** 0.5
            tr.fw[b0:b1] = d['fw']
            tr.F[:, b0:b1] = L3[:, None]
            tr.f0[b0:b1] = f0
            end = b1
            if d['vl']:
                na = int(FR * rng.uniform(0.02, 0.045))
                a0, a1 = self._cl(b1, na)
                tr.asp[a0:a1] = np.maximum(tr.asp[a0:a1], 0.18 * amp * np.linspace(1, 0.3, a1 - a0))
                tr.F[:, a0:a1] = L3[:, None]
                tr.f0[a0:a1] = f0
                end = a1
            return end
        # nasal / liquid / glide: voiced, weaker, formants at locus
        n = int(FR * rng.uniform(0.035, 0.065) * dur_scale)
        m0, m1 = self._cl(m, n)
        lvl = 0.35 if k == 'nasal' else 0.6
        tr.va[m0:m1] = np.maximum(tr.va[m0:m1], lvl * amp)
        tr.F[:, m0:m1] = L3[:, None]
        tr.f0[m0:m1] = f0
        return m1


# --------------------------------------------------------------- utterances
def speech(w, m, n_syl, amp, f0mul=1.0, rate=1.0, question=None):
    """Conversational utterance of n_syl syllables starting at frame m."""
    rng, tk = w.rng, w.tk
    base = tk.f0 * f0mul * rng.uniform(0.94, 1.08)
    r = tk.rate * rate
    q = (rng.random() < 0.15) if question is None else question
    pos = m
    for s in range(n_syl):
        x = s / max(1, n_syl - 1)
        decl = 1.08 - 0.2 * x
        stress = rng.random() < 0.35
        a = amp * (1.35 if stress else 1.0) * rng.lognormal(0, 0.15) * (1 - 0.25 * x)
        f0 = base * decl * (rng.uniform(1.1, 1.25) if stress else rng.uniform(0.96, 1.03))
        v = SPEECH_V[rng.choice(len(SPEECH_V), p=SPEECH_VP)]
        if rng.random() < 0.8:
            c = ONSETS[rng.choice(len(ONSETS), p=ONSET_P)]
            pos = w.cons(pos, c, a, f0, next_v=v, dur_scale=1 / r)
        nv = FR * (rng.uniform(0.09, 0.15) if not stress else rng.uniform(0.13, 0.21)) / r
        if s == n_syl - 1:
            nv *= 1.4
            f0b = f0 * (1.25 if q else 0.82)
        else:
            f0b = f0 * (0.93 if stress else 0.98)
        pos = w.vowel(pos, nv, v, f0, f0b, a, asp=0.1 if rng.random() < 0.2 else 0.0)
        if rng.random() < 0.4:
            c = CODAS[rng.choice(len(CODAS), p=CODA_P)]
            pos = w.cons(pos, c, a * 0.8, f0b, dur_scale=1 / r)
        if s < n_syl - 1 and rng.random() < 0.07:   # phrase break
            pos += int(FR * rng.uniform(0.12, 0.3))
        if pos >= w.M - 2:
            break
    return pos


def laugh(w, m, amp, f0mul=1.0):
    rng, tk = w.rng, w.tk
    n = int(rng.integers(3, 9))
    per = FR / rng.uniform(4.3, 5.8)
    f0 = min(tk.f0 * rng.uniform(1.3, 1.8) * f0mul, tk.shout_max)
    v = ['A', 'a', 'ae', 'e'][rng.integers(4)]
    a = amp * rng.uniform(0.9, 1.2)
    pos = m
    for i in range(n):
        ha = int(per * rng.uniform(0.3, 0.42))
        m0, m1 = w._cl(pos, ha)
        w.tr.asp[m0:m1] = np.maximum(w.tr.asp[m0:m1], 0.35 * a)
        w.tr.F[:, m0:m1] = w.fmt(v)[:, None]
        w.tr.f0[m0:m1] = f0
        nv = int(per * rng.uniform(0.4, 0.55))
        w.vowel(m1, nv, v, f0 * 1.03, f0 * 0.93, a, att=2, rel=3, asp=0.3)
        pos = int(pos + per * rng.uniform(0.92, 1.08))
        f0 *= rng.uniform(0.93, 0.98)
        a *= rng.uniform(0.8, 0.92)
        if pos >= w.M - 2:
            break
    return pos


CHEER_KINDS = ['aah', 'woo', 'yeah', 'hey', 'go', 'comeon', 'scream', 'yes', 'xspeech', 'whoop']


def cheer(w, m, kind, amp):
    """Excited vocalisation starting at frame m; returns end frame."""
    rng, tk = w.rng, w.tk
    base = min(tk.f0 * tk.shout_mult * rng.uniform(0.9, 1.1), tk.shout_max)
    eff = w.eff
    pos = m
    if kind == 'aah':
        if rng.random() < 0.5:
            pos = w.cons(pos, 'h', amp, base * 0.9)
        n = FR * rng.uniform(0.7, 2.3)
        v = ['a', 'ae', 'A', 'o'][rng.integers(4)]
        pk = rng.uniform(0.15, 0.35)
        pos = w.vowel(pos, n, v, 0, 0, amp, att=8, rel=int(FR * 0.12),
                      f0pts=([0, pk, 0.6, 1], np.array([0.82, 1.05, 1.0, 0.78]) * base),
                      env=([0, pk, 1], [0.8, 1.0, 0.7]), vib=rng.uniform(0.008, 0.02))
    elif kind == 'woo':
        pos = w.cons(pos, 'w', amp, base * 0.75)
        n = FR * rng.uniform(0.55, 1.6)
        pk = rng.uniform(0.3, 0.5)
        hi = rng.uniform(1.2, 1.4)
        pos = w.vowel(pos, n, 'u', 0, 0, amp, att=6, rel=int(FR * 0.1), v2='U' if rng.random() < 0.5 else None,
                      f0pts=([0, pk, pk + 0.2, 1], np.array([0.72, hi, hi * 0.95, 0.72]) * base),
                      env=([0, pk, 1], [0.6, 1.0, 0.75]), vib=rng.uniform(0.01, 0.025))
    elif kind == 'whoop':   # short rising "whoo!" / "hoo!"
        pos = w.cons(pos, 'h', amp, base)
        n = FR * rng.uniform(0.2, 0.4)
        pos = w.vowel(pos, n, 'u', 0, 0, amp, att=3, rel=6,
                      f0pts=([0, 0.7, 1], np.array([0.85, 1.4, 1.3]) * base))
    elif kind == 'yeah':
        pos = w.cons(pos, 'y', amp, base * 1.1)
        n = FR * rng.uniform(0.35, 1.0)
        rise = rng.random() < 0.3
        pts = ([0, 0.3, 1], np.array([1.1, 1.15, 1.3 if rise else 0.8]) * base)
        pos = w.vowel(pos, n, 'E', 0, 0, amp, v2='ae', att=4, rel=int(FR * 0.08), f0pts=pts,
                      vib=rng.uniform(0.0, 0.015))
    elif kind == 'hey':
        pos = w.cons(pos, 'h', amp, base)
        n = FR * rng.uniform(0.22, 0.45)
        pos = w.vowel(pos, n, 'e', base * 1.1, base * 0.98, amp, v2='I', att=3, rel=6)
    elif kind == 'go':
        reps = int(rng.integers(2, 5))
        f = base * 0.95
        for i in range(reps):
            pos = w.cons(pos, 'g', amp, f)
            pos = w.vowel(pos, FR * rng.uniform(0.17, 0.28), 'o', f, f * 0.95, amp, v2='U', att=2, rel=5)
            pos += int(FR * rng.uniform(0.07, 0.14))
            f *= rng.uniform(1.02, 1.07)
    elif kind == 'comeon':
        f = base
        pos = w.cons(pos, 'k', amp, f)
        pos = w.vowel(pos, FR * 0.09, 'A', f * 0.95, f, amp * 0.8, att=2, rel=2)
        pos = w.cons(pos, 'm', amp, f)
        pos = w.vowel(pos, FR * rng.uniform(0.28, 0.5), 'a', f * 1.12, f * 0.8, amp, att=3, rel=8)
        pos = w.cons(pos, 'n', amp * 0.8, f * 0.8)
    elif kind == 'scream':
        b = min(base * rng.uniform(1.2, 1.5), tk.shout_max * 1.2)
        pos = w.cons(pos, 'h', amp * 0.5, b)
        n = FR * rng.uniform(0.3, 0.9)
        v = ['i', 'ae', 'E'][rng.integers(3)]
        pos = w.vowel(pos, n, v, 0, 0, amp, att=4, rel=int(FR * 0.08), asp=0.12,
                      f0pts=([0, 0.3, 1], np.array([0.9, 1.1, 0.85]) * b), vib=0.02)
    elif kind == 'yes':
        pos = w.cons(pos, 'y', amp, base)
        pos = w.vowel(pos, FR * rng.uniform(0.2, 0.35), 'E', base * 1.1, base * 0.95, amp, att=3, rel=3)
        m0, m1 = w._cl(pos, FR * rng.uniform(0.15, 0.3))
        w.tr.fa[m0:m1] = 0.35 * amp * np.linspace(1, 0.4, m1 - m0)
        w.tr.fc[m0:m1] = 6000
        w.tr.fw[m0:m1] = 0.4
        w.tr.f0[m0:m1] = base
        pos = m1
    else:  # excited speech
        pos = speech(w, m, int(rng.integers(3, 8)), amp * 0.9, f0mul=min(tk.shout_mult * 0.8, tk.shout_max / tk.f0),
                     rate=1.15, question=False)
    return pos


# ---------------------------------------------------------------- renderer
def _fill(a):
    ok = ~np.isnan(a)
    if not ok.any():
        return np.full_like(a, 150.0)
    x = np.arange(len(a))
    return np.interp(x, x[ok], a[ok]).astype(np.float32)


def _smooth(a, n):
    if n <= 1:
        return a
    k = np.hanning(n + 2)[1:-1]
    k /= k.sum()
    ap = np.concatenate([a[-n:], a, a[:n]])
    return np.convolve(ap, k, mode='same')[n:-n].astype(np.float32)


def _glottal(p, dt, Tp, Tn):
    out = np.zeros_like(p)
    m1 = p < Tp
    out[m1] = 0.5 * np.pi / Tp * np.sin(np.pi * p[m1] / Tp)
    m2 = (~m1) & (p < Tp + Tn)
    out[m2] = -(np.pi / (2 * Tn)) * np.sin(0.5 * np.pi * (p[m2] - Tp) / Tn)
    J = np.pi / (2 * Tn)
    t = (p - (Tp + Tn) + 0.5) % 1.0 - 0.5
    a = (t >= 0) & (t < dt)
    x = t[a] / dt[a]
    out[a] += J / 2 * (2 * x - x * x - 1)
    b = (t < 0) & (t > -dt)
    x = t[b] / dt[b]
    out[b] += J / 2 * (x * x + 2 * x + 1)
    return out


def _glottal_rms(Tp, Tn):
    p = (np.arange(8192) + 0.5) / 8192
    g = _glottal(p, np.full_like(p, 1e-9), Tp, Tn)
    return float(np.sqrt(np.mean(g ** 2)))


def _res(F, B):
    F2 = F * F
    return F2 / np.sqrt((F2 - _F2) ** 2 + (FREQS * B) ** 2)


_HP_CACHE = {}


def _hi_poles(s):
    """Fixed higher resonances F4..F25 of a ~17 cm tube (scaled): acts as the
    higher-pole correction so the cascade keeps a realistic HF envelope."""
    key = round(float(s), 3)
    if key not in _HP_CACHE:
        c = np.ones(NB, np.float64)
        for k in range(4, 26):
            Fk = (2 * k - 1) * 500.0 * key
            c *= _res(np.float64(Fk), 220.0 + 45.0 * (k - 4))
        _HP_CACHE[key] = c.astype(np.float32)
    return _HP_CACHE[key]


def render_talker(tk, tr, effort_oq=0.0, max_bins=None):
    """Render a talker Track to STFT frames. Returns (frame_idx, S[Ma, NB]).
    Output is normalised so a vowel with va=1 has unit RMS."""
    rng = np.random.default_rng(tk.seed + int(tr.va.sum() * 1000) % 100000)
    M = tr.M
    L = M * HOP
    active = (tr.va > 1e-4) | (tr.fa > 1e-4) | (tr.asp > 1e-4)
    if not active.any():
        return None, None
    act = np.convolve(active.astype(np.float32), np.ones(7), mode='same') > 0
    idx = np.nonzero(act)[0]

    f0 = _smooth(_fill(tr.f0), 5)
    F = np.stack([_smooth(_fill(tr.F[i]), 7) for i in range(3)])
    va = _smooth(tr.va, 3)
    eff = _smooth(tr.eff, 9)
    # jitter / shimmer / flutter / vibrato
    tfr = np.arange(M) / FR
    flutter = np.convolve(rng.standard_normal(M), np.hanning(60) / 30, mode='same')
    vibr = tr.vib * np.sin(2 * np.pi * rng.uniform(4.8, 6.2) * tfr + rng.uniform(0, 6.28))
    f0 = f0 * (1 + tk.jitter * rng.standard_normal(M) + 0.012 * flutter + vibr)
    va = va * np.clip(1 + tk.shimmer * rng.standard_normal(M), 0.5, 1.5)

    # time-domain glottal source over the active span only
    s0 = max(0, idx[0] * HOP - 512)
    s1 = min(L, (idx[-1] + 1) * HOP + 512)
    ts = np.arange(s0, s1, dtype=np.float64)
    tf = np.arange(M) * HOP
    f0s = np.interp(ts, tf, f0)
    dt = f0s / FS
    ph = np.cumsum(dt) + rng.random()
    p = ph % 1.0
    oq = np.clip(tk.oq - 0.18 * effort_oq, 0.35, 0.8)
    Tp, Tn = oq * 0.68, oq * 0.32
    g = _glottal(p, dt, Tp, Tn) / _glottal_rms(Tp, Tn)
    vs = np.interp(ts, tf, va)
    src = np.zeros(L, np.float32)
    src[s0:s1] = (g * vs).astype(np.float32)
    X = stft_c(src, idx)

    # formant envelope per active frame
    s = tk.vt
    Fi = F[:, idx]
    ei = eff[idx][:, None]
    B1 = (70 + 25 * ei) * s ** 0.5
    E = _res(Fi[0][:, None], B1) * _res(Fi[1][:, None], 95 * s ** 0.5) * _res(Fi[2][:, None], 150 * s ** 0.5)
    E *= _hi_poles(s * tk.fscale)[None, :]
    # spectral tilt: soft voices roll off above ~1.5 kHz; effort flattens it and adds presence
    lt = np.log1p((FREQS / 1500.0) ** 2)[None, :]
    pres = np.exp(-0.5 * ((_LOGF - np.log2(2600)) / 0.8) ** 2)[None, :]
    T = np.exp(-lt * (0.25 - 0.22 * ei) + pres * (0.45 * ei)) * (_HPV * _CORR)[None, :]
    ET = (E * T).astype(np.float32)
    Sv = X * ET

    # calibrate voiced gain on strongly voiced frames
    vi = va[idx]
    strong = vi > 0.3 * vi.max()
    pw = np.sum(np.abs(Sv[strong]) ** 2, 1) / (NB * NOISE_SCALE ** 2 * 2) / (vi[strong] ** 2 + 1e-9)
    gv = float(np.sqrt(np.median(pw))) + 1e-9

    # noise parts (aspiration/breathiness through the tract, frication by place)
    asp = tr.asp[idx] + tk.breath * va[idx]
    fa = tr.fa[idx]
    nz = (rng.standard_normal(X.shape).astype(np.float32) + 1j * rng.standard_normal(X.shape).astype(np.float32))
    nz *= NOISE_SCALE
    hpv = (FREQS / np.sqrt(FREQS ** 2 + 600 ** 2))[None, :]
    Ea = ET * hpv
    # normalise aspiration shape so its RMS gain matches the voiced gain
    ea_g = np.sqrt(np.mean(Ea ** 2, 1, keepdims=True)) + 1e-9
    Sn = (asp[:, None] * gv / ea_g) * Ea
    fm = fa > 1e-5
    if fm.any():
        fc = tr.fc[idx][fm][:, None]
        fw = tr.fw[idx][fm][:, None]
        G = np.exp(-0.5 * ((_LOGF[None, :] - np.log2(fc)) / fw) ** 2)
        G /= np.sqrt(np.mean(G ** 2, 1, keepdims=True)) + 1e-9
        Sn[fm] += (fa[fm][:, None] * gv) * G
    S = (Sv + nz * Sn) / gv
    if max_bins:
        S[:, max_bins:] = 0
    return idx, S.astype(np.complex64)


class Buses:
    """STFT-domain accumulation buses (dry L/R + reverb send L/R, or mono)."""

    def __init__(self, M, mono=False):
        self.M = M
        self.mono = mono
        shape = (M, NB)
        if mono:
            self.m = np.zeros(shape, np.complex64)
        else:
            self.dL = np.zeros(shape, np.complex64)
            self.dR = np.zeros(shape, np.complex64)
            self.sL = np.zeros(shape, np.complex64)
            self.sR = np.zeros(shape, np.complex64)

    def add(self, idx, S, shift, pan=0.0, gdry=1.0, gsend=0.0, filt=None, itd=0.00025):
        rows = (idx + shift) % self.M
        if filt is not None:
            S = S * filt[None, :].astype(np.float32)
        if self.mono:
            self.m[rows] += S * np.float32(gdry)
            return
        gl, gr = pan_gains(pan)
        tau = itd * pan
        ph = np.exp(-2j * np.pi * FREQS * abs(tau)).astype(np.complex64)
        phL = ph if tau > 0 else np.ones_like(ph)
        phR = ph if tau < 0 else np.ones_like(ph)
        self.dL[rows] += S * (np.float32(gdry * gl) * phL)
        self.dR[rows] += S * (np.float32(gdry * gr) * phR)
        if gsend:
            self.sL[rows] += S * (np.float32(gsend * gl) * phL)
            self.sR[rows] += S * (np.float32(gsend * gr) * phR)

    def to_time(self):
        if self.mono:
            x = istft_c(self.m)
            del self.m
            return x
        out = [istft_c(b) for b in (self.dL, self.dR, self.sL, self.sR)]
        del self.dL, self.dR, self.sL, self.sR
        return np.stack(out[:2]), np.stack(out[2:])
