#!/usr/bin/env python3
"""Render crowd loops.  Usage:  python3 render.py [Pack ...] [--layer bed|roar]
Writes  ../../soundpacks/<Pack>/{Ambience,CrowdRoar}.wav  and previews."""
import os
import sys
import time
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dsp import FS, write_wav16, read_wav, lufs, true_peak_db  # noqa: E402
from engine import render_layer  # noqa: E402
from packs import PACKS  # noqa: E402

AUDIO = os.path.abspath(os.path.join(HERE, '..', '..'))
OUT = os.path.join(AUDIO, 'soundpacks')
PREV = os.path.join(AUDIO, 'previews')


def preview(pack):
    _, bed = read_wav(os.path.join(OUT, pack, 'Ambience.wav'))
    _, roar = read_wav(os.path.join(OUT, pack, 'CrowdRoar.wav'))
    n = 20 * FS
    t = np.arange(n) / FS
    g = np.clip((t - 6) / 3, 0, 1) * np.clip((17 - t) / 3, 0, 1)
    g = np.sin(g * np.pi / 2) ** 2
    y = bed[:, :n] + roar[:, :n] * g
    tp = true_peak_db(y)
    if tp > -1.0:
        y *= 10 ** ((-1.0 - tp) / 20)
    os.makedirs(PREV, exist_ok=True)
    write_wav16(os.path.join(PREV, f'{pack}-preview.wav'), y, np.random.default_rng(0))


def main():
    args = sys.argv[1:]
    layers = ['bed', 'roar']
    if '--layer' in args:
        i = args.index('--layer')
        layers = [args[i + 1]]
        del args[i:i + 2]
    packs = args or list(PACKS)
    for p in packs:
        os.makedirs(os.path.join(OUT, p), exist_ok=True)
        for layer in layers:
            t0 = time.time()
            x = render_layer(PACKS[p], layer)
            fn = os.path.join(OUT, p, 'Ambience.wav' if layer == 'bed' else 'CrowdRoar.wav')
            write_wav16(fn, x, np.random.default_rng(PACKS[p]['seed'] + 99))
            _, y = read_wav(fn)
            print(f'{p} {layer}: {y.shape[1] / FS:.2f}s  {lufs(y):.2f} LUFS  TP {true_peak_db(y):.2f} dBTP  '
                  f'({time.time() - t0:.0f}s)', flush=True)
        if os.path.exists(os.path.join(OUT, p, 'Ambience.wav')) and os.path.exists(os.path.join(OUT, p, 'CrowdRoar.wav')):
            preview(p)


if __name__ == '__main__':
    main()
