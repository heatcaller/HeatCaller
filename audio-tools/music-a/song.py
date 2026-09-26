"""Arrangement helpers: timeline, stereo buses, auto gain-staging, stem report."""
import numpy as np
from dsp import SR, pan, db
from analyze import lufs


class Arr:
    def __init__(self, bpm, n_bars, tail_s=4.0):
        self.bpm = bpm
        self.spb = 60.0 / bpm
        self.bar_s = 4 * self.spb
        self.n_bars = n_bars
        self.N = int((n_bars * self.bar_s + tail_s) * SR)
        self.buses = {}

    def t(self, bar, beat=0.0):
        return bar * self.bar_s + beat * self.spb

    def bus(self, name):
        if name not in self.buses:
            self.buses[name] = np.zeros((2, self.N), dtype=np.float32)
        return self.buses[name]

    def add(self, name, sig, t, p=0.0, gain=1.0):
        b = self.bus(name)
        s = int(round(t * SR))
        if s >= self.N:
            return
        if sig.ndim == 1:
            sig = pan(sig, p)
        if s < 0:
            sig = sig[:, -s:]; s = 0
        e = min(self.N, s + sig.shape[1])
        b[:, s:e] += (sig[:, :e - s] * gain).astype(np.float32)

    def section_loudness(self, name, t0, t1):
        b = self.buses[name][:, int(t0 * SR):int(t1 * SR)].astype(np.float64)
        return lufs(b)


def auto_balance(arr, targets, t0, t1, ref=-14.0):
    """Set each bus so its loudness in [t0, t1] sits at ref + target (relative LU).
    targets: {bus: relative_db}. Returns applied gains (dB)."""
    gains = {}
    for name, rel in targets.items():
        if name not in arr.buses:
            continue
        L = arr.section_loudness(name, t0, t1)
        if not np.isfinite(L):
            continue
        g = ref + rel - L
        arr.buses[name] *= np.float32(db(g))
        gains[name] = g
    return gains
