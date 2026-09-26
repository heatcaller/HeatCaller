"""Per-soundscape venue / crowd definitions."""
import numpy as np
from dsp import FS, ffilt, lp, hp, bump


def low_shelf(f, fc, db):
    return 10 ** (db / 20 / np.sqrt(1 + (np.asarray(f) / fc) ** 4))

import events as ev
from engine import render_claps, render_pa, place_events

CLUB = {'male': 0.66, 'female': 0.26, 'teen': 0.04, 'child': 0.04}
MOTOR = {'male': 0.64, 'female': 0.31, 'child': 0.05}
ARCADE = {'male': 0.42, 'female': 0.26, 'teen': 0.27, 'child': 0.05}
ARENA = {'male': 0.58, 'female': 0.3, 'teen': 0.07, 'child': 0.05}


def _bus_finish(V, bus):
    d, s = bus.finish()
    return d + V.rev(s[0], s[1])


# ------------------------------------------------------------ bed extras
def pitlane_bed(V, L, rng, a_s):
    c = {'room': ev.room_tone(rng, L, lp_hz=6500, hp_hz=45, tilt=0.8)}
    motors = ev.EventBus(L, 18.0)
    chirps = ev.EventBus(L, 1.0)
    for run in range(2):
        t0 = int((run * 0.5 + rng.uniform(0.02, 0.2)) * L)
        dur = rng.uniform(10, 14)
        for car in range(int(rng.integers(1, 4))):
            d = rng.uniform(5, 9)
            x = ev.rc_motor_run(rng, dur - rng.uniform(0, 3))
            x = ffilt(x[None], lambda f: V.dist_resp(f, d))[0]
            motors.add(x * np.float32(rng.uniform(0.6, 1.0)), t0 + int(rng.uniform(0, 2) * FS),
                       rng.uniform(-0.5, 0.5), 1 / d, V.P['send'] * 1.5)
        for k in range(int(rng.integers(1, 3))):
            chirps.add(ev.decoder_chirp(rng), t0 + int(rng.uniform(3, dur - 1) * FS), 0.45, 1 / 12, V.P['send'] * 2)
    c['motors'] = _bus_finish(V, motors)
    c['chirps'] = _bus_finish(V, chirps)
    c['bedclaps'] = render_claps(V, L, rng, dict(n=5, dist=(3, 10), rate=(3.0, 4.5), episodes=2, join=0.6,
                                                 ep_dur=(1.5, 2.8)), a_s)
    return c


def arcade_bed(V, L, rng, a_s):
    return {
        'room': ev.room_tone(rng, L, lp_hz=4500, hp_hz=35, tilt=0.9),
        'hum': ev.mains_hum(rng, L, 60.0),
        'fans': ev.fan_whir(rng, L, 4),
        'bedclaps': render_claps(V, L, rng, dict(n=9, dist=(4, 20), rate=(4.2, 6.0), episodes=3, join=0.6,
                                                 ep_dur=(1.8, 3.2)), a_s),
    }


def podium_bed(V, L, rng, a_s):
    return {
        'wind': ev.wind(rng, L),
        'flags': ev.flag_flutter(rng, L, 4),
        'pa': render_pa(V, L, rng, dict(phrases=2, gap=(9, 16), dist=150, send=1.6)),
        'bedclaps': render_claps(V, L, rng, dict(n=14, dist=(20, 70), rate=(3.0, 4.5), episodes=2, join=0.7,
                                                 ep_dur=(2.0, 3.5)), a_s),
        'bedwhistle': place_events(V, L, rng, lambda r: ev.whistle(r), 1, (60, 120)),
    }


def broadcast_bed(V, L, rng, a_s):
    return {
        'wind': ev.wind(rng, L, 0.8),
        'air': ev.room_tone(rng, L, lp_hz=3000, hp_hz=40, tilt=1.0),
        'pa': render_pa(V, L, rng, dict(phrases=3, gap=(6, 11), dist=110, send=2.2)),
        'bedclaps': render_claps(V, L, rng, dict(n=16, dist=(20, 70), rate=(3.2, 4.8), episodes=2, join=0.7,
                                                 ep_dur=(2.0, 3.5)), a_s),
        'bedwhistle': place_events(V, L, rng, lambda r: ev.whistle(r), 2, (50, 110)),
    }


def synthwave_bed(V, L, rng, a_s):
    return {
        'air': ev.room_tone(rng, L, lp_hz=3500, hp_hz=30, tilt=1.1),
        'rumble': ev.rumble(rng, L, 75),
        'bedclaps': render_claps(V, L, rng, dict(n=12, dist=(20, 60), rate=(3.2, 4.8), episodes=2, join=0.7,
                                                 ep_dur=(2.0, 3.0)), a_s),
        'bedwhistle': place_events(V, L, rng, lambda r: ev.whistle(r), 1, (30, 70)),
    }


# --------------------------------------------------------------- packs
PACKS = {
    'PitLane': dict(
        name='PitLane', seed=1101, bed_s=60, roar_s=40,
        reverb=dict(dur=1.5, rt={63: .55, 125: .5, 250: .48, 500: .46, 1000: .44, 2000: .40, 4000: .33,
                                 8000: .24, 16000: .15},
                    predelay=.003, onset=.006, er_lp=8000,
                    er=[(.004, .6), (.007, .45), (.011, .4), (.016, .3), (.022, .25), (.031, .2), (.043, .15)]),
        send=0.16, air_k=0.004, shield_d=6, shield_fc=11000,
        groups=[dict(n=20, size=(1, 4), dist=(1.5, 10), pan=(-0.95, 0.95), demo=CLUB)],
        far_n=0,
        bed=dict(gap=0.8, solo_gap=5, laugh=0.08, lull=0.1, pop=0.06, cheer_p=0.02, base=0.5, depth=0.2),
        roar=dict(kinds=(['comeon', 'yeah', 'woo', 'whoop', 'hey', 'go', 'aah', 'xspeech', 'yes'],
                         [18, 15, 10, 12, 10, 10, 8, 10, 7]), gap=0.35, activity=0.9, eff=0.8),
        claps=dict(n=28, dist=(1.5, 10), rate=(2.8, 4.8), on=5, off=2.5),
        whistles=3, whistle_d=(3, 10),
        bed_extra=pitlane_bed,
        bed_mix={'voices': 0, 'room': -15, 'motors': -17, 'chirps': -25, 'bedclaps': -11},
        roar_mix={'voices': 0, 'claps': -1.5, 'whistles': -8},
    ),
    'Arcade': dict(
        name='Arcade', seed=2202, bed_s=60, roar_s=40,
        reverb=dict(dur=3.0, rt={63: 1.6, 125: 1.45, 250: 1.3, 500: 1.2, 1000: 1.15, 2000: 1.0, 4000: .8,
                                 8000: .55, 16000: .3},
                    predelay=.008, onset=.015, er_lp=7000,
                    er=[(.009, .5), (.014, .45), (.021, .4), (.029, .35), (.038, .3), (.052, .25), (.066, .2)]),
        send=0.28, air_k=0.004, shield_d=8, shield_fc=9000,
        groups=[dict(n=20, size=(1, 4), dist=(2, 10), pan=(-0.95, 0.95), demo=ARCADE),
                dict(n=26, size=(1, 3), dist=(10, 25), pan=(-1, 1), demo=ARCADE)],
        far_n=36, far_copies=5, far_d=30, far_send=0.9, far_dry=0.3, far_demo=ARCADE,
        bed=dict(gap=0.7, solo_gap=3.5, laugh=0.12, lull=0.06, pop=0.06, cheer_p=0.05,
                 cheer_kinds=['whoop', 'yeah', 'hey', 'woo'], base=0.55, depth=0.2),
        roar=dict(kinds=(['woo', 'yeah', 'scream', 'aah', 'hey', 'go', 'whoop', 'xspeech', 'yes'],
                         [22, 18, 14, 12, 10, 8, 10, 6, 5]), gap=0.3, activity=0.95, eff=0.85),
        claps=dict(n=90, dist=(2, 25), rate=(4.0, 6.5), on=6, off=2, types_p=[0.2, 0.05, 0.3, 0.25, 0.15, 0.05]),
        rain=dict(n=160, rate=5.0, dist=30),
        whistles=7, whistle_d=(4, 25),
        bed_extra=arcade_bed,
        bed_mix={'voices': 0, 'far': -3, 'room': -14, 'hum': -30, 'fans': -21, 'bedclaps': -11},
        roar_mix={'voices': 0, 'far': -2, 'claps': 0, 'rain': -6, 'whistles': -8},
    ),
    'Podium': dict(
        name='Podium', seed=3303, bed_s=60, roar_s=40,
        reverb=dict(dur=3.0, rt={63: 1.3, 125: 1.2, 250: 1.1, 500: 1.0, 1000: .9, 2000: .75, 4000: .55,
                                 8000: .35, 16000: .2},
                    predelay=.012, onset=.03, er_lp=6000, tail_gain=0.8,
                    er=[(.011, .4), (.019, .3), (.033, .25)],
                    slaps=[(.17, .5, .02, 3000), (.26, .4, .03, 2400), (.39, .28, .04, 1800), (.55, .15, .05, 1400)]),
        send=0.10, air_k=0.005, shield_d=8, shield_fc=7000,
        groups=[dict(n=16, size=(1, 3), dist=(3, 12), pan=(-0.95, 0.95), demo=MOTOR),
                dict(n=45, size=(1, 2), dist=(12, 60), pan=(-1, 1), demo=MOTOR)],
        far_n=48, far_copies=10, far_d=120, far_send=0.6, far_dry=0.6, far_demo=MOTOR,
        bed=dict(gap=1.2, solo_gap=8, laugh=0.05, lull=0.15, pop=0.03, far_gap=1.5, base=0.5, depth=0.2),
        roar=dict(kinds=(['aah', 'yeah', 'woo', 'hey', 'comeon', 'go', 'whoop', 'xspeech'],
                         [26, 18, 14, 12, 8, 6, 8, 8]), gap=0.35, activity=0.9, eff=0.8),
        claps=dict(n=70, dist=(3, 40), rate=(3.0, 5.0), on=7, off=2),
        rain=dict(n=3000, rate=4.0, dist=90),
        whistles=10, whistle_d=(8, 80), horns=4, horn_d=(40, 150),
        bed_extra=podium_bed, roar_eq=lambda f: low_shelf(f, 260, 3.5),
        bed_mix={'voices': 0, 'far': 1, 'wind': -12, 'flags': -22, 'pa': -16, 'bedclaps': -12, 'bedwhistle': -12},
        roar_mix={'voices': 0, 'far': 1, 'claps': -1, 'rain': 0, 'whistles': -8, 'horns': -7},
    ),
    'Broadcast': dict(
        name='Broadcast', seed=4404, bed_s=60, roar_s=40,
        reverb=dict(dur=4.5, rt={63: 3.0, 125: 2.7, 250: 2.4, 500: 2.1, 1000: 1.9, 2000: 1.6, 4000: 1.2,
                                 8000: .8, 16000: .4},
                    predelay=.015, onset=.04, er_lp=6000,
                    er=[(.014, .4), (.025, .35), (.041, .3), (.06, .25)],
                    slaps=[(.29, .45, .03, 2600), (.52, .3, .05, 1800), (.78, .15, .07, 1300)]),
        send=0.2, air_k=0.005, shield_d=8, shield_fc=7500,
        groups=[dict(n=18, size=(1, 4), dist=(3, 12), pan=(-1, 1), demo=MOTOR),
                dict(n=55, size=(1, 3), dist=(12, 60), pan=(-1, 1), demo=MOTOR)],
        far_n=56, far_copies=12, far_d=100, far_send=1.0, far_dry=0.5, far_demo=MOTOR,
        bed=dict(gap=0.9, solo_gap=5, laugh=0.07, lull=0.08, pop=0.04, far_gap=1.0, base=0.55, depth=0.22),
        roar=dict(kinds=(['aah', 'yeah', 'woo', 'hey', 'comeon', 'go', 'whoop', 'xspeech'],
                         [22, 18, 14, 14, 10, 10, 8, 4]), gap=0.25, activity=0.95, eff=0.9),
        claps=dict(n=80, dist=(3, 40), rate=(3.2, 5.2), on=7, off=2),
        rain=dict(n=5000, rate=4.2, dist=80),
        whistles=14, whistle_d=(8, 80), horns=11, horn_d=(25, 120),
        bed_extra=broadcast_bed, roar_eq=lambda f: low_shelf(f, 260, 4.5),
        bed_mix={'voices': 0, 'far': 2, 'wind': -15, 'air': -17, 'pa': -14, 'bedclaps': -12, 'bedwhistle': -12},
        roar_mix={'voices': 0, 'far': 2, 'claps': -2, 'rain': 0, 'whistles': -7, 'horns': -4},
    ),
    'Synthwave': dict(
        name='Synthwave', seed=5505, bed_s=60, roar_s=40,
        reverb=dict(dur=9.0, rt={31.5: 7.5, 63: 7.0, 125: 6.3, 250: 5.5, 500: 4.8, 1000: 4.2, 2000: 3.4,
                                 4000: 2.4, 8000: 1.4, 16000: .7},
                    predelay=.025, onset=.08, er_lp=5000,
                    er=[(.028, .35), (.045, .3), (.067, .28), (.09, .25), (.12, .2), (.15, .15)]),
        send=0.38, air_k=0.004, shield_d=8, shield_fc=8000,
        groups=[dict(n=16, size=(1, 3), dist=(3, 12), pan=(-1, 1), demo=ARENA),
                dict(n=45, size=(1, 3), dist=(12, 50), pan=(-1, 1), demo=ARENA)],
        far_n=50, far_copies=10, far_d=70, far_send=1.4, far_dry=0.25, far_demo=ARENA,
        bed=dict(gap=0.9, solo_gap=5, laugh=0.06, lull=0.08, pop=0.04, far_gap=1.0, base=0.55, depth=0.22),
        roar=dict(kinds=(['woo', 'aah', 'yeah', 'hey', 'scream', 'go', 'whoop', 'comeon'],
                         [30, 22, 14, 8, 6, 8, 8, 4]), gap=0.25, activity=0.95, eff=0.9),
        claps=dict(n=60, dist=(3, 40), rate=(3.2, 5.0), on=6, off=2.5),
        rain=dict(n=2000, rate=4.0, dist=60),
        whistles=9, whistle_d=(8, 60), horns=10, horn_d=(20, 80),
        stomps=dict(n=80, runs=1),
        bed_extra=synthwave_bed, roar_eq=lambda f: low_shelf(f, 200, 2.0),
        bed_mix={'voices': 0, 'far': 1, 'air': -13, 'rumble': -16, 'bedclaps': -12, 'bedwhistle': -13},
        roar_mix={'voices': 0, 'far': 2, 'claps': -3, 'rain': -3, 'whistles': -8, 'horns': -5, 'stomps': -9},
    ),
}
