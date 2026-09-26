"""Crowd layer renderer: populations, scheduling (conversation / cheering),
applause, whistles, horns, stomps, venue textures, per-venue reverb, mixing,
loudness normalisation and seamless-loop export."""
import time
import numpy as np
from dsp import (FS, HOP, FR, NB, FREQS, ffilt, lp, hp, bump, air_abs, slow_curve, pan_gains,
                 StereoReverb, lufs, true_peak_db, limit_circular, write_wav16)
from voices import Talker, Track, Writer, speech, laugh, cheer, render_talker, Buses
import events as ev

LOG = print


# ------------------------------------------------------------ populations
def pick_kind(rng, demo):
    ks = list(demo)
    p = np.array([demo[k] for k in ks], float)
    return ks[rng.choice(len(ks), p=p / p.sum())]


def make_groups(rng, spec):
    """spec: list of dict(n=groups, size=(a,b), dist=(d0,d1), pan=(p0,p1), demo={...})"""
    groups = []
    for s in spec:
        for _ in range(s['n']):
            k = int(rng.integers(s['size'][0], s['size'][1] + 1))
            d = float(np.exp(rng.uniform(np.log(s['dist'][0]), np.log(s['dist'][1]))))
            p = rng.uniform(*s['pan'])
            g = []
            for j in range(k):
                g.append(Talker(rng, pick_kind(rng, s['demo']), d * rng.uniform(0.9, 1.1),
                                float(np.clip(p + rng.normal(0, 0.06), -1, 1))))
            groups.append(g)
    return groups


def make_far(rng, n, demo):
    return [Talker(rng, pick_kind(rng, demo), 100.0, 0.0) for _ in range(n)]


# ------------------------------------------------------------- scheduling
def schedule_conversation(group, M, shift, arousal, rng, cfg):
    tracks = [Track(M) for _ in group]
    ws = [Writer(tr, tk, rng, eff=cfg.get('eff', 0.0)) for tr, tk in zip(tracks, group)]
    solo = len(group) == 1
    m = int(rng.integers(0, int(FR * (cfg['solo_gap'] if solo else 3))))
    end = M - int(FR * 0.6)
    last = -1
    while m < end:
        a = float(arousal[(m + shift) % M])
        i = int(rng.integers(len(group)))
        if len(group) > 1 and i == last and rng.random() < 0.75:
            i = (i + 1 + int(rng.integers(len(group) - 1))) % len(group)
        n_syl = int(np.clip(rng.gamma(2.0, 3.2), 2, 22))
        if m + n_syl * FR * 0.3 > end:
            break
        amp = rng.lognormal(0, 0.25) * (0.75 + 0.5 * a)
        if rng.random() < cfg.get('pop', 0.0):
            amp *= rng.uniform(1.6, 2.4)
        if rng.random() < cfg.get('cheer_p', 0.0):
            w = ws[i]
            w.eff = 0.6
            e = cheer(w, m, rng.choice(cfg.get('cheer_kinds', ['whoop', 'yeah', 'hey', 'comeon'])), amp * 1.4)
            w.eff = cfg.get('eff', 0.0)
        else:
            e = speech(ws[i], m, n_syl, amp)
        last = i
        m = e
        if rng.random() < cfg['laugh'] * (0.6 + 0.8 * a):
            ends = [m]
            who = [j for j in range(len(group)) if rng.random() < 0.6] or [i]
            for j in who:
                if m + FR * 2.2 < end:
                    ends.append(laugh(ws[j], m + int(rng.integers(0, 25)), amp * rng.uniform(0.7, 1.1)))
            m = max(ends)
        if solo:
            gap = rng.exponential(cfg['solo_gap']) * (1.5 - a)
        else:
            gap = 0.1 + rng.exponential(cfg['gap']) * (1.4 - a)
        if rng.random() < cfg.get('lull', 0.0):
            gap += rng.uniform(2, 6)
        m += int(FR * gap)
    return tracks


def schedule_cheering(tk, M, shift, arousal, rng, cfg):
    tr = Track(M)
    w = Writer(tr, tk, rng, eff=cfg['eff'] * rng.uniform(0.75, 1.0))
    kinds, p = cfg['kinds']
    p = np.array(p, float) / np.sum(p)
    m = int(rng.integers(0, int(FR * 2)))
    end = M - int(FR * 2.8)
    while m < end:
        a = float(arousal[(m + shift) % M])
        if rng.random() > cfg['activity'] * a ** 0.8:
            m += int(FR * rng.uniform(0.3, 1.2))
            continue
        kind = kinds[rng.choice(len(kinds), p=p)]
        amp = (0.45 + 0.55 * a) * rng.lognormal(0, 0.22)
        m = cheer(w, m, kind, amp)
        m += int(FR * (rng.uniform(0.12, 0.4) + rng.exponential(cfg['gap']) * (1.3 - a)))
    return tr


# -------------------------------------------------------------- helpers
class Venue:
    def __init__(self, P, rng):
        self.P = P
        self.rev = StereoReverb(np.random.default_rng(P['seed'] + 7), cross=P.get('rev_cross', 0.6), **P['reverb'])

    def dist_resp(self, f, d):
        """Level-independent distance colouring: air absorption + crowd/ground shielding."""
        P = self.P
        r = air_abs(f, d, P.get('air_k', 0.004))
        d0 = P.get('shield_d', 8.0)
        if d > d0:
            fc = P.get('shield_fc', 9000) * (d0 / d) ** P.get('shield_exp', 0.45)
            r = r * lp(f, fc, 1)
        return r

    def dist_filt_nb(self, d):
        return self.dist_resp(FREQS.astype(np.float64), d).astype(np.float32)

    def gains(self, d):
        P = self.P
        gd = 1.0 / max(d, 1.0)
        gs = P['send'] * (1.0 / max(d, 1.0) ** P.get('send_dist_exp', 0.0))
        return gd, gs


def arousal_curves(rng, L, base, depth, fmax):
    M = L // HOP
    a = np.clip(base + depth * slow_curve(rng, M, FR, fmax, power=0.5), 0.12, 1.0)
    a_s = np.interp(np.arange(L) / HOP, np.arange(M + 1), np.append(a, a[0])).astype(np.float32)
    return a, a_s


def render_near_voices(V, groups, L, arousal, rng, mode, cfg):
    M = L // HOP
    buses = Buses(M)
    n = 0
    for g in groups:
        shift = int(rng.integers(M))
        if mode == 'bed':
            tracks = schedule_conversation(g, M, shift, arousal, rng, cfg)
        else:
            tracks = [schedule_cheering(tk, M, shift, arousal, rng, cfg) for tk in g]
        for tk, tr in zip(g, tracks):
            idx, S = render_talker(tk, tr, effort_oq=cfg.get('eff', 0.0))
            if idx is None:
                continue
            gd, gs = V.gains(tk.dist)
            buses.add(idx, S, shift, pan=tk.pan, gdry=gd, gsend=gs, filt=V.dist_filt_nb(tk.dist))
            n += 1
    dry, send = buses.to_time()
    return dry + V.rev(send[0], send[1]), n


def render_far_voices(V, talkers, L, arousal, rng, mode, cfg, copies, far_d, swell=(0.0, 0.0), a_s=None):
    """Diffuse far-field crowd: talkers rendered to a mono bus, then decorrelated
    circularly-shifted copies spread across the stereo field (copies x talkers
    virtual voices), heavily distance-filtered and mostly reverberant."""
    M = L // HOP
    bus = Buses(M, mono=True)
    for tk in talkers:
        shift = int(rng.integers(M))
        if mode == 'bed':
            tr = schedule_conversation([tk], M, shift, arousal, rng, cfg)[0]
        else:
            tr = schedule_cheering(tk, M, shift, arousal, rng, cfg)
        idx, S = render_talker(tk, tr, effort_oq=cfg.get('eff', 0.0), max_bins=None)
        if idx is None:
            continue
        bus.add(idx, S, shift)
    x = bus.to_time()
    out = np.zeros((2, L), np.float32)
    glob = np.zeros(L, np.float32)
    if a_s is not None and swell[0]:
        glob = (a_s - a_s.mean()) / (a_s.std() + 1e-9) * swell[0]
    for c in range(copies):
        # each copy = a stand section with its own slow level drift (+ shared swell)
        sec = slow_curve(rng, L // 64, FS / 64, 0.05) if swell[1] else np.zeros(L // 64, np.float32)
        sec = np.repeat(sec, 64)[:L]
        g = (10 ** ((glob + swell[1] * sec) / 20)).astype(np.float32)
        for ch in range(2):
            out[ch] += np.roll(x, int(rng.integers(L))) * g * np.float32(rng.uniform(0.6, 1.0))
    out = ffilt(out, lambda f: V.dist_resp(f, far_d))
    P = V.P
    wet = V.rev(out[0] * P['far_send'], out[1] * P['far_send'])
    return out * P.get('far_dry', 0.5) + wet


def activity_sessions(rng, L, arousal_s, mean_on, mean_off):
    """Per-person on/off envelope (0..1, sample rate), circular."""
    env = np.zeros(L, np.float32)
    t = int(rng.integers(L))
    total = 0
    on = rng.random() < 0.7
    while total < L:
        a = float(arousal_s[t % L])
        dur = int(FS * (rng.exponential(mean_on) * (0.4 + a) if on else rng.exponential(mean_off) * (1.4 - a)))
        dur = max(dur, int(0.3 * FS))
        if on:
            seg = np.ones(dur, np.float32)
            r = min(int(0.6 * FS), dur // 2)
            seg[:r] = np.linspace(0, 1, r)
            seg[dur - r:] = np.linspace(1, 0, r)
            i = (t + np.arange(dur)) % L
            env[i] = np.maximum(env[i], seg)
        t += dur
        total += dur
        on = not on
    return env


def render_claps(V, L, rng, cfg, arousal_s):
    """Individual clappers (unique hand-configuration banks) grouped by distance."""
    pool = ev.ClapperPool(rng, cfg.get('types_p'))
    groups = {'near': ev.EventBus(L, 0.2), 'mid': ev.EventBus(L, 0.2), 'far': ev.EventBus(L, 0.2)}
    dist_of = {'near': None, 'mid': None, 'far': None}
    dsum = {k: [] for k in groups}
    if cfg.get('episodes'):
        ep_times = [int(x) for x in (np.arange(cfg['episodes']) + rng.uniform(0.1, 0.6)) * L / cfg['episodes']]
        ep_durs = rng.uniform(*cfg.get('ep_dur', (2.0, 4.0)), size=cfg['episodes'])
    for i in range(cfg['n']):
        d = float(np.exp(rng.uniform(np.log(cfg['dist'][0]), np.log(cfg['dist'][1]))))
        gname = 'near' if d < 6 else ('mid' if d < 25 else 'far')
        dsum[gname].append(d)
        bank = pool.clapper_bank()
        rate = rng.uniform(*cfg['rate'])
        pan = rng.uniform(-0.95, 0.95)
        gd, gs = V.gains(d)
        g0 = rng.lognormal(0, 0.3)
        if cfg.get('episodes'):
            # short spontaneous clap-alongs: a few people join shared episodes
            env = np.zeros(L, np.float32)
            for j, t0 in enumerate(ep_times):
                if rng.random() < cfg.get('join', 0.7):
                    n = int(FS * ep_durs[j] * rng.uniform(0.6, 1.0))
                    s0 = t0 + int(FS * rng.uniform(0, 0.6))
                    seg = np.hanning(n).astype(np.float32) ** 0.4
                    env[(s0 + np.arange(n)) % L] = seg
        else:
            env = activity_sessions(rng, L, arousal_s, cfg.get('on', 6.0), cfg.get('off', 2.0))
        times = ev.clap_train(rng, 0, L / FS, rate, jitter=cfg.get('jitter', 0.05))
        for t in times:
            i0 = int(t * FS) % L
            e = env[i0]
            if e < 0.02:
                continue
            amp = g0 * e * rng.lognormal(0, 0.12) * (0.6 + 0.4 * arousal_s[i0])
            groups[gname].add(bank[int(rng.integers(len(bank)))] * np.float32(amp), i0, pan, gd, gs)
    out = np.zeros((2, L), np.float32)
    for k, bus in groups.items():
        if not dsum[k]:
            continue
        dm = float(np.mean(dsum[k]))
        d, s = bus.finish(post=lambda f, dm=dm: V.dist_resp(f, dm))
        out += d + V.rev(s[0], s[1])
    return out


def render_rain(V, L, rng, cfg, arousal_s):
    bank = [ev.make_clap(rng, ev.CLAP_TYPES[i % 6]) for i in range(24)]
    act = np.clip(arousal_s, 0, 1) ** 1.5
    x = ev.applause_rain(rng, L, cfg['n'], cfg['rate'], act, bank)
    x = ffilt(x, lambda f: V.dist_resp(f, cfg['dist']))
    P = V.P
    return x * P.get('far_dry', 0.5) + V.rev(x[0] * P['far_send'], x[1] * P['far_send'])


def place_events(V, L, rng, makers, count, dist, arousal_s=None, pan=(-0.9, 0.9), weight_pow=2.0):
    bus = ev.EventBus(L, 8.0)
    ds = []
    for i in range(count):
        # onset time weighted towards high-arousal moments
        for _ in range(20):
            t0 = int(rng.integers(L))
            if arousal_s is None or rng.random() < arousal_s[t0] ** weight_pow:
                break
        d = float(np.exp(rng.uniform(np.log(dist[0]), np.log(dist[1]))))
        ds.append(d)
        x = makers(rng)
        x = ffilt(x[None], lambda f: V.dist_resp(f, d))[0]
        gd, gs = V.gains(d)
        bus.add(x * np.float32(rng.lognormal(0, 0.25)), t0, rng.uniform(*pan), gd, gs)
    d, s = bus.finish()
    return d + V.rev(s[0], s[1])


def render_stomps(V, L, rng, cfg, arousal_s):
    """Bleacher stomping: unsynchronised thunder at peaks + loosely synced runs."""
    bank = [ev.stomp(rng) for _ in range(16)]
    bus = ev.EventBus(L, 0.5)
    act = np.clip((arousal_s - np.percentile(arousal_s, 55)) / (arousal_s.max() - np.percentile(arousal_s, 55) + 1e-9), 0, 1) ** 1.5
    for p in range(cfg['n']):
        rate = rng.uniform(2.5, 4.0)
        pan = rng.uniform(-1, 1)
        d = rng.uniform(8, 40)
        gd, gs = V.gains(d)
        t = rng.uniform(0, 1 / rate)
        while t < L / FS:
            i0 = int(t * FS)
            if rng.random() < act[i0]:
                bus.add(bank[int(rng.integers(16))] * np.float32(rng.lognormal(0, 0.3) * act[i0]), i0, pan, gd, gs)
            t += 1 / rate * (1 + 0.15 * rng.standard_normal())
    # synced runs: crowd stomping together
    for r in range(cfg.get('runs', 1)):
        start = rng.uniform(0, L / FS)
        dur = rng.uniform(6, 9)
        bpm_t = 60 / rng.uniform(128, 140)
        k = 0
        t = 0.0
        while t < dur:
            fade = min(1, t / 2.0, (dur - t) / 2.0)
            for p in range(cfg['n'] // 2):
                tt = start + t + rng.normal(0, 0.03)
                d = rng.uniform(8, 40)
                gd, gs = V.gains(d)
                bus.add(bank[int(rng.integers(16))] * np.float32(fade * 0.8 * rng.lognormal(0, 0.3)),
                        int(tt * FS) % L, rng.uniform(-1, 1), gd, gs)
            t += bpm_t
    d, s = bus.finish(post=lambda f: V.dist_resp(f, 20))
    return d + V.rev(s[0], s[1])


def render_pa(V, L, rng, cfg):
    """Distant PA announcer: phrases through a PA voicing + delay towers + venue."""
    M = L // HOP
    tk = Talker(rng, 'male', 60.0, 0.0)
    tk.f0 = rng.uniform(100, 125)
    tr = Track(M)
    w = Writer(tr, tk, rng, eff=0.45)
    m = int(FR * rng.uniform(1, 4))
    for i in range(cfg['phrases']):
        for j in range(int(rng.integers(2, 4))):
            m = speech(w, m, int(rng.integers(5, 12)), 1.0, f0mul=1.1, rate=0.9) + int(FR * rng.uniform(0.25, 0.6))
            if m > M - FR * 4:
                break
        m += int(FR * rng.uniform(*cfg['gap']))
        if m > M - FR * 6:
            break
    idx, S = render_talker(tk, tr, effort_oq=0.45)
    b = Buses(M, mono=True)
    b.add(idx, S, int(rng.integers(M)))
    x = b.to_time()
    x = ffilt(x[None], lambda f: hp(f, 320, 3) * lp(f, 4200, 3) * bump(f, 2000, 6, 0.7))[0]
    x = np.tanh(2.0 * x / (np.abs(x).max() + 1e-9)).astype(np.float32)
    out = np.zeros((2, L), np.float32)
    for dly, g, pn, fc in [(0.0, 1.0, 0.0, 4000), (0.13, 0.7, -0.6, 3000), (0.24, 0.6, 0.6, 2600),
                           (0.41, 0.45, -0.3, 2000), (0.62, 0.3, 0.4, 1600)]:
        y = np.roll(x, int(dly * FS))
        y = ffilt(y[None], lambda f, fc=fc: lp(f, fc, 2))[0] * g
        gl, gr = pan_gains(pn)
        out[0] += y * gl
        out[1] += y * gr
    out = ffilt(out, lambda f: V.dist_resp(f, cfg.get('dist', 120)))
    return out * 0.3 + V.rev(out[0] * cfg.get('send', 1.5), out[1] * cfg.get('send', 1.5))


# ------------------------------------------------------------------ layers
def render_layer(P, layer):
    t_start = time.time()
    seed = P['seed'] + (0 if layer == 'bed' else 5000)
    rng = np.random.default_rng(seed)
    pop_rng = np.random.default_rng(P['seed'])          # same people for both layers
    groups = make_groups(pop_rng, P['groups'])
    far = make_far(pop_rng, P['far_n'], P['far_demo']) if P.get('far_n') else []
    V = Venue(P, rng)
    L = int(P['bed_s' if layer == 'bed' else 'roar_s'] * FS)
    L -= L % HOP
    comps = {}
    if layer == 'bed':
        cfg = P['bed']
        a, a_s = arousal_curves(rng, L, cfg.get('base', 0.5), cfg.get('depth', 0.2), cfg.get('swell_f', 0.06))
        comps['voices'], n = render_near_voices(V, groups, L, a, rng, 'bed', cfg)
        LOG(f'  {P["name"]} bed: {n} near/mid talkers rendered ({time.time() - t_start:.0f}s)')
        if far:
            fcfg = dict(cfg, solo_gap=cfg.get('far_gap', 1.2), laugh=cfg['laugh'] * 0.5, pop=0)
            comps['far'] = render_far_voices(V, far, L, a, rng, 'bed', fcfg, P['far_copies'], P['far_d'],
                                             cfg.get('far_swell', (1.8, 2.5)), a_s)
            LOG(f'  far layer: {len(far)} talkers x {P["far_copies"]} ({time.time() - t_start:.0f}s)')
        extra = P['bed_extra'](V, L, rng, a_s)
        comps.update(extra)
        mix = P['bed_mix']
        target = P.get('bed_lufs', -20.0)
    else:
        cfg = P['roar']
        a, a_s = arousal_curves(rng, L, cfg.get('base', 0.74), cfg.get('depth', 0.17), cfg.get('swell_f', 0.14))
        comps['voices'], n = render_near_voices(V, groups, L, a, rng, 'roar', cfg)
        LOG(f'  {P["name"]} roar: {n} near/mid talkers rendered ({time.time() - t_start:.0f}s)')
        if far:
            comps['far'] = render_far_voices(V, far, L, a, rng, 'roar', cfg, P['far_copies'], P['far_d'],
                                             cfg.get('far_swell', (1.5, 2.5)), a_s)
            LOG(f'  far layer ({time.time() - t_start:.0f}s)')
        comps['claps'] = render_claps(V, L, rng, P['claps'], a_s)
        if P.get('rain'):
            comps['rain'] = render_rain(V, L, rng, P['rain'], a_s)
        if P.get('whistles'):
            comps['whistles'] = place_events(V, L, rng, lambda r: ev.whistle(r), P['whistles'], P.get('whistle_d', (6, 60)), a_s)
        if P.get('horns'):
            comps['horns'] = place_events(V, L, rng, lambda r: ev.horn_pattern(r), P['horns'], P.get('horn_d', (20, 80)), a_s)
        if P.get('stomps'):
            comps['stomps'] = render_stomps(V, L, rng, P['stomps'], a_s)
        mix = P['roar_mix']
        target = P.get('roar_lufs', -16.0)
    # ---- mix by relative loudness targets
    ref = lufs(comps['voices'])
    out = np.zeros((2, L), np.float32)
    report = {}
    for k, x in comps.items():
        lk = lufs(x)
        g = 10 ** ((ref + mix.get(k, -10) - lk) / 20)
        out += x * np.float32(g)
        report[k] = round(lk - ref, 1)
    eq = P.get(layer + '_eq', lambda f: 1.0)
    out = ffilt(out, lambda f: hp(f, P.get('hp', 28), 2) * eq(f))
    out -= out.mean(axis=1, keepdims=True)
    # ---- loudness normalisation + circular true-peak limiting
    lim_db = 0.0
    for _ in range(4):
        out *= np.float32(10 ** ((target - lufs(out)) / 20))
        if true_peak_db(out) <= -2.3:
            break
        out, gmin = limit_circular(out, -2.6)
        lim_db = min(lim_db, gmin)
    LOG(f'  {P["name"]} {layer}: comps(pre-gain LU rel voices)={report} limiter min gain {lim_db:.1f} dB '
        f'({time.time() - t_start:.0f}s)')
    return out
