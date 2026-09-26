"""Stem store + mixdown engine (render once, rebalance many times)."""
import os, json, time
import numpy as np
import mblib as M
from mblib import SR


class StemStore:
    def __init__(self, path, log=print):
        self.path = path
        os.makedirs(path, exist_ok=True)
        self.log = log

    def save(self, tr, hp=None, lp=None, bands=None, comp=None, width=None):
        x = tr.buf
        if hp:
            x = M.hpf(x, hp, 2)
        if lp:
            x = M.lpf(x, lp, 2)
        if bands:
            x = M.eq(x, bands)
        if comp:
            x = M.compress(x, **comp)
        if width is not None:
            x = M.width(x, width)
        x = x.astype(np.float32)
        np.save(os.path.join(self.path, tr.name + '.npy'), x)
        act = np.abs(x).max(axis=1) > 1e-4
        rms = 20 * np.log10(np.sqrt(np.mean(x[act] ** 2)) + 1e-9) if act.any() else -99
        self.log(f'  saved stem {tr.name:14s} active-rms {rms:6.1f} dBFS')

    def load(self, name):
        return np.load(os.path.join(self.path, name + '.npy'), mmap_mode='r')

    def has(self, names):
        return all(os.path.exists(os.path.join(self.path, n + '.npy')) for n in names)


def automation(n, tm, points):
    """points: list of (beat, dB) -> per-sample linear gain (linear dB interpolation between points)."""
    if not points:
        return None
    ts = np.array([float(tm.t(b)) for b, _ in points]) * SR
    db = np.array([d for _, d in points], dtype=float)
    idx = np.arange(0, n, 64)
    g = np.interp(idx, ts, db)
    g = np.interp(np.arange(n), idx, g)
    return (10 ** (g / 20)).astype(np.float32)


def mixdown(store, cfg, n, tm, duck_k, bus_proc, hall_ir, returns, delay_s, log=print, master_auto=None):
    """cfg: name -> dict(bus, gain, hall, dly, duck, auto)."""
    buses = {}
    hall = np.zeros((n, 2), np.float32)
    dly = np.zeros((n, 2), np.float32)
    report = {}
    for name, c in cfg.items():
        x = np.array(store.load(name)[:n], dtype=np.float32)
        if len(x) < n:
            x = np.pad(x, ((0, n - len(x)), (0, 0)))
        g = np.float32(10 ** (c.get('gain', 0) / 20))
        x *= g
        if c.get('eq'):
            x = M.eq(x, c['eq'])
        au = automation(n, tm, c.get('auto'))
        if au is not None:
            x *= au[:, None]
        if c.get('duck'):
            x *= (1 - c['duck'] * (1 - duck_k)).astype(np.float32)[:, None]
        act = np.abs(x).max(axis=1) > 1e-4
        report[name] = round(float(20 * np.log10(np.sqrt(np.mean(x[act] ** 2)) + 1e-9)), 1) if act.any() else -99
        b = c.get('bus', 'orch')
        if b not in buses:
            buses[b] = np.zeros((n, 2), np.float32)
        buses[b] += x
        if c.get('hall'):
            hall += x * np.float32(c['hall'])
        if c.get('dly'):
            dly += x * np.float32(c['dly'])
        del x
    log('  stem levels ' + json.dumps(report))
    total = np.zeros((n, 2), np.float32)
    for b, x in buses.items():
        p = bus_proc.get(b)
        if p:
            x = M.compress(x, **p)
        total += x
    wet = M.convolve_stereo(M.hpf(hall, returns.get('hall_hp', 150), 2), hall_ir)
    if returns.get('hall_eq'):
        wet = M.eq(wet, returns['hall_eq'])
    total += wet * np.float32(10 ** (returns.get('hall_db', -7) / 20))
    if dly.any():
        d = M.pingpong(dly, delay_s, fb=returns.get('dly_fb', 0.42), taps=6, lp_hz=4000, hp_hz=400)
        d = d + M.convolve_stereo(d * 0.3, hall_ir)
        total += d * np.float32(10 ** (returns.get('dly_db', -10) / 20))
    au = automation(n, tm, master_auto)
    if au is not None:
        total *= au[:, None]
    return total, report
