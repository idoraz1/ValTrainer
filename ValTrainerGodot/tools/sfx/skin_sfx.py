#!/usr/bin/env python3
"""
ValTrainer skin sound generator, skins 3 (v1.19, docs/research/skins3-contracts.md).

HYBRID, not pure synthesis: every sound is layered from CC0 RECORDINGS (Kenney and OpenGameArt packs, see CREDITS.md)
plus processing: pitch / time shifts, filtering, shaped-noise excitation, detuned partials, convolution with short
room / plate impulse responses synthesized here, gentle saturation, EQ (mud cut 200-400 Hz, presence lift 2-5 kHz),
stereo tails and mono transients. No Riot audio, no AI-made audio.

Output (44.1/48 kHz; SkinAudio.cs loads skin.<line>.<event>, variants <event>_<n> are a round-robin group):
  assets/audio/skins/_core/<factory shot>.wav    each factory shot's 0-8 ms transient + body below 300 Hz (+ a little of
                                                 its own mids where needed): the "core bus" of the full fire replacement
  assets/audio/skins/<line>/
    fire_<class>_<1..5>.ogg  the skin's fire (mid band + tail, stereo) that plays with the core; class = SkinAudio.ClassOf
    reload_<key>.ogg         reload layer on the factory reload (key = rifle/pistol/shotgun/revolver/lmg)
    equip.ogg, inspect.ogg   equip flourish, inspect flourish
    hit_body_<1..5>.wav      body hit (replaces the factory "body"), mono
    hit_head_<1..5>.wav      headshot (replaces the factory "head"), mono; dominant partial F#7 (2960 Hz) on every line
    kill1..kill5.ogg         escalating kill sounds (replace the factory kill1..5 at the same length and loudness)
    kill.ogg                 the kill effect's sound
  Apex lines (foundry, aurora, orrery) also:
    reload_special_a.ogg     the special reload's start (plays at the reload start)
    reload_special_b.ogg     its end: the seat / lock hit is RS_B_HIT s into the file (SkinAudio plays it so the hit
                             lands at the end of the factory reload, whatever its duration)
    fin.ogg                  finisher;  counter.ogg  kill-counter tick

The rules (measured on the WRITTEN files after decoding; the run fails loudly, exit code 1, if any is broken):
  fire     core + skin within +-1 LU of the factory shot (K-weighted, 200 ms from the onset) for EVERY gun of the class
           and every variant; auto classes: the dry skin is 20 dB down by 110 ms (spray timing); onset not before the
           factory's
  hits     body: <= 80 ms (-40 dB), >= 85 % of the energy below 1.5 kHz, no tonal partial above 2 kHz
           head: dominant partial 2-4 kHz and within 50 cents of 2960 Hz (same pitch class on every line), rings >= 200 ms
           (to -30 dB); head centroid >= 2x body, head decay >= 3x body; every body/head variant within +-1 dB of the
           common target (K-weighted 100 ms); onset (30 % of peak) within 2 ms of the factory hit's
  kills    kill<n>: length within 10 % and loudness within 1 dB of factory kill<n>; centroid rises 1 -> 5
  cosmetic reload layers: the factory clip's length, momentary max >= 6 dB under it; equip >= 10 dB under the Vandal;
           inspect >= 8 dB under the factory headshot; kill / fin >= 4 / 2 dB under factory kill5; reload_special >= 4 dB
           under the reload; counter >= 18 dB under kill5
  every    no clipping, no two files with the same audio; lines differ from each other (fingerprint distance)

Usage (run with py -I; the sources are untrusted data, only decoded by ffmpeg):
    py -I skin_sfx.py --sources DIR [--project DIR] [--ffmpeg EXE] [--cache DIR] [--only glasswork,orrery]
    py -I skin_sfx.py --sources DIR --check          # measure the files on disk, write nothing
    py -I skin_sfx.py --sources DIR --preview DIR    # also render <line>.wav montages + _all-hits.wav for listening
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import wave
import zlib
from fractions import Fraction

import numpy as np
from scipy import signal

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)  # py -I leaves the script folder off sys.path; skin_src.py lives next to us
import skin_src as SS  # noqa: E402

SR = SS.SR  # 48 kHz

# ----------------------------------------------------------------------------------------------- tables (from the game)

LINES = ["glasswork", "thunderhead", "basalt", "oscilloscope", "rally", "frostbite", "hive", "foundry", "aurora", "orrery"]
APEX = {"foundry", "aurora", "orrery"}
OLD_SETS = ["honeycomb", "rainfront", "strata", "escapement", "phosphor", "calibrated"]
CLASSES = {  # SkinAudio.ClassOf
    "sidearm": ["classic", "bandit", "sheriff", "headhunter"],
    "smg": ["frenzy", "stinger"],
    "suppressed": ["ghost", "spectre"],
    "shotgun": ["shorty", "bucky", "judge"],
    "rifle": ["bulldog", "guardian", "phantom", "vandal", "warden"],
    "sniper": ["marshal", "outlaw", "operator"],
    "heavy": ["ares", "odin"],
}
SHOT_FILES = {"classic": "pistol_shot", "vandal": "rifle_ak_shot", "phantom": "rifle_ar_shot", "operator": "sniper_bolt_shot"}
RELOADS = {"rifle": "rifle_reload_airsoft.wav", "pistol": "pistol_reload.wav", "shotgun": "shotgun_reload.wav",
           "revolver": "revolver_reload.wav", "lmg": "lmg_reload.wav"}
KILL_SEMIS = [0, 2, 4, 7, 12]
VARIANTS = 5
OFF = 0.004            # the factory shots' onset (gun_sfx.py pre-roll): the skin's fire lands here
HEAD_HZ = 2960.0       # F#7: the headshot's dominant partial on every line
RS_B_HIT = 0.30        # reload_special_b: the seat / lock hit, seconds into the file (SkinAudio.ReloadSpecialHit)
OGG_Q = "5"

CLS = {  # dry = the skin's body length, tail = its tail, wet = plate send
    "sidearm": dict(dry=0.060, tail=0.42, wet=0.30, auto=False),
    "smg": dict(dry=0.040, tail=0.24, wet=0.20, auto=True),
    "suppressed": dict(dry=0.035, tail=0.20, wet=0.16, auto=True, dark=True),
    "shotgun": dict(dry=0.090, tail=0.60, wet=0.30, auto=False),
    "rifle": dict(dry=0.050, tail=0.34, wet=0.24, auto=True),
    "sniper": dict(dry=0.120, tail=1.00, wet=0.34, auto=False),
    "heavy": dict(dry=0.045, tail=0.28, wet=0.20, auto=True),
}

# source packs (folder names under the sources dir)
KI, KS, KU = "kenney-impact-sounds/", "kenney-sci-fi-sounds/", "kenney-interface-sounds/"
MW, C1, WS, BF = "oga-100-cc0-metal-and-wood-sfx/", "oga-100-cc0-sfx/", "oga-40-cc0-water-splash-slime-sfx/", "oga-75-cc0-breaking-falling-hit-sfx/"
MC, CW, ED, EC, RN = ("oga-7-mechanical-clicks-and-buzzes/", "oga-clock-wind-sounds/", "oga-electronic-device-loop/",
                      "oga-equipment-clicks-iii/", "oga-rain-loopable/")
USED_PACKS: set[str] = set()

# ----------------------------------------------------------------------------------------------- DSP


def N(d: float) -> int:
    return int(round(d * SR))


def T(d: float) -> np.ndarray:
    return np.arange(N(d)) / SR


def R(*parts) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32("/".join(map(str, parts)).encode()))


def _bw(x, kind, f, order=2):
    if len(np.atleast_1d(x)) == 0 or np.asarray(x).shape[-1] == 0:
        return x
    sos = signal.butter(order, np.asarray(f) / (SR / 2), btype=kind, output="sos")
    return signal.sosfilt(sos, x, axis=-1)


def hp(x, f, o=2):
    return _bw(x, "highpass", f, o)


def lp(x, f, o=2):
    return _bw(x, "lowpass", min(f, SR * 0.45), o)


def bpf(x, f1, f2, o=2):
    return _bw(x, "bandpass", [f1, min(f2, SR * 0.45)], o)


def _rbj(kind, f, g, q):
    A, w = 10 ** (g / 40), 2 * np.pi * f / SR
    al, c = np.sin(w) / (2 * q), np.cos(w)
    if kind == "peq":
        b = [1 + al * A, -2 * c, 1 - al * A]; a = [1 + al / A, -2 * c, 1 - al / A]
    elif kind == "hs":
        sA = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * c + sA), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sA)]
        a = [(A + 1) - (A - 1) * c + sA, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sA]
    else:  # ls
        sA = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * c + sA), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sA)]
        a = [(A + 1) + (A - 1) * c + sA, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sA]
    return np.array(b) / a[0], np.array(a) / a[0]


def peq(x, f, g, q=1.0):
    b, a = _rbj("peq", f, g, q)
    return signal.lfilter(b, a, x, axis=-1)


def hsh(x, f, g):
    b, a = _rbj("hs", f, g, 0.707)
    return signal.lfilter(b, a, x, axis=-1)


def lsh(x, f, g):
    b, a = _rbj("ls", f, g, 0.707)
    return signal.lfilter(b, a, x, axis=-1)


def tone(x):
    """The common finishing EQ: cut the 200-400 Hz mud, lift 2-5 kHz presence."""
    return peq(peq(x, 290, -3.0, 0.9), 3400, 2.0, 0.8)


def pitch(x, r: float):
    """Varispeed by ratio r (> 1 = higher and shorter), like a tape / sampler pitch shift."""
    if abs(r - 1) < 1e-4:
        return x
    fr = Fraction(r).limit_denominator(400)
    return signal.resample_poly(x, fr.denominator, fr.numerator, axis=-1)


def fit(x, n: int):
    x = np.asarray(x)
    if x.shape[-1] >= n:
        return x[..., :n].copy()
    pad = [(0, 0)] * (x.ndim - 1) + [(0, n - x.shape[-1])]
    return np.pad(x, pad)


def fade(x, fin=0.0004, fout=0.006):
    x = np.array(x, dtype=float)
    n = x.shape[-1]
    a, b = min(N(fin), n), min(N(fout), n)
    if a > 0:
        x[..., :a] *= np.linspace(0, 1, a)
    if b > 0:
        x[..., n - b:] *= np.linspace(1, 0, b) ** 2
    return x


def env(n: int, atk=0.0005, tau=0.05, hold=0.0, start=0.0):
    t = np.arange(n) / SR - start
    e = np.where(t < 0, 0.0, np.where(t < atk, t / max(atk, 1e-9), 1.0))
    d = np.clip(t - atk - hold, 0, None)
    return e * np.exp(-d / tau)


def sat(x, d=1.5):
    p = np.max(np.abs(x)) + 1e-12
    return np.tanh(d * x / p) / np.tanh(d) * p


def pk(x) -> float:
    return float(np.max(np.abs(x))) if np.size(x) else 0.0


def norm(x, p=1.0):
    return x * (p / (pk(x) + 1e-12))


def st(x):
    x = np.asarray(x)
    return np.vstack([x, x]) if x.ndim == 1 else x


def mono(x):
    x = np.asarray(x)
    return x.mean(axis=0) if x.ndim == 2 else x


def put(out, t: float, x, g=1.0):
    """Adds x into out at time t (mono into stereo goes to both channels)."""
    i = N(t)
    if i >= out.shape[-1]:
        return
    if out.ndim == 2 and np.ndim(x) == 1:
        x = st(x)
    if out.ndim == 1 and np.ndim(x) == 2:
        x = mono(x)
    m = min(x.shape[-1], out.shape[-1] - i)
    out[..., i:i + m] += g * x[..., :m]


def buf(d, stereo=True):
    return np.zeros((2, N(d))) if stereo else np.zeros(N(d))


def conv(x, ir):
    """x mono or stereo, ir mono or stereo; stereo ir on a mono x decorrelates (stereo tail)."""
    x, ir = np.asarray(x), np.asarray(ir)
    if ir.ndim == 1:
        return signal.fftconvolve(x, ir if x.ndim == 1 else ir[None, :], axes=-1) if x.ndim == 2 else signal.fftconvolve(x, ir)
    xs = st(x)
    return np.vstack([signal.fftconvolve(xs[c], ir[c]) for c in range(2)])


def plate(rt: float, damp=7000.0, seed=0, pre=0.004, lowcut=250.0, length=None):
    """A synthesized plate / room: decorrelated stereo exponentially decaying noise with faster-decaying highs."""
    L = length or min(rt * 1.1, 3.0)
    t = T(L)
    chans = []
    for c in range(2):
        r = R("plate", seed, c)
        nz = r.standard_normal(len(t))
        lo = lp(nz, 1800, 2)
        hi = lp(nz - lo, damp, 2)
        y = lo * np.exp(-6.91 * t / rt) + hi * np.exp(-6.91 * t / (rt * 0.55))
        y *= np.minimum(1, t / 0.004)
        chans.append(hp(y, lowcut))
    ir = np.pad(np.vstack(chans), ((0, 0), (N(pre), 0)))
    return ir / np.sqrt(np.sum(ir ** 2) / 2)


def room_mono(rt=0.25, damp=7000, seed=0):
    return plate(rt, damp, seed, pre=0.002)[0]


def tv_bp(x, f0, f1, q=1.5, block=256):
    """Band-pass whose centre glides f0 -> f1 (exponential) over the clip, block-wise with carried state."""
    out = np.zeros_like(x)
    n = len(x)
    zi = np.zeros((1, 2))
    for i in range(0, n, block):
        k = i / max(n - 1, 1)
        fc = f0 * (f1 / f0) ** k
        w = 2 * np.pi * fc / SR
        al = np.sin(w) / (2 * q)
        b = np.array([al, 0, -al]) / (1 + al)
        a = np.array([1, -2 * np.cos(w) / (1 + al), (1 - al) / (1 + al)])
        out[i:i + block], zi[0] = signal.lfilter(b, a, x[i:i + block], zi=zi[0])
    return out


def pink(n, r):
    w = np.fft.rfft(r.standard_normal(n))
    f = np.arange(len(w)); f[0] = 1
    y = np.fft.irfft(w / np.sqrt(f), n)
    return y / (pk(y) + 1e-12)


def crackle(d, rate, tau, r, hpf=3000.0, lpf=14000.0, grain=0.0006, power=2.2):
    """Sparse clicks (sparks, frost, static) with decaying density rate*exp(-t/tau) and heavy-tailed amplitudes."""
    n = N(d)
    t = np.arange(n) / SR
    dens = rate * np.exp(-t / tau) / SR
    hit = r.random(n) < dens
    amp = np.zeros(n)
    amp[hit] = r.random(hit.sum()) ** power * np.sign(r.standard_normal(hit.sum()))
    g = r.standard_normal(N(grain) + 8) * np.exp(-np.arange(N(grain) + 8) / (N(grain) / 3 + 1))
    y = np.convolve(amp, g)[:n]
    return bpf(y, hpf, lpf)


def modal(d, freqs, taus, amps, r=None, atk=0.0004):
    t = T(d)
    y = np.zeros(len(t))
    for f, tau, a in zip(freqs, taus, amps):
        ph = r.uniform(0, 2 * np.pi) if r is not None else 0.0
        y += a * np.sin(2 * np.pi * f * t + ph) * env(len(t), atk, tau)
    return y


# ----------------------------------------------------------------------------------------------- sources

_src_cache: dict = {}


def S(rel: str, start=0.0, dur=None, r=1.0, thresh=-40.0, fadeout=0.004):
    """A source recording: mono, onset-trimmed (first sample within `thresh` dB of the peak, minus 1 ms), sliced, pitched by
    r, peak-normalized."""
    USED_PACKS.add(rel.split("/")[0])
    key = (rel, start, dur, r, thresh)
    if key in _src_cache:
        return _src_cache[key].copy()
    x = hp(SS.load(rel), 25, 2)
    p = pk(x)
    i = int(np.argmax(np.abs(x) >= p * 10 ** (thresh / 20)))
    x = x[max(0, i - N(0.001)):]
    x = x[N(start):]
    if dur is not None:
        x = x[:N(dur / r)]
    x = pitch(x, r)
    x = fade(norm(x), 0.0002, fadeout)
    _src_cache[key] = x
    return x.copy()


def seg(rel: str, d: float, r: np.random.Generator, skip=0.3):
    """A random segment of a long texture file (rain, hum, thruster)."""
    USED_PACKS.add(rel.split("/")[0])
    x = hp(SS.load(rel), 25, 2)
    n = N(d)
    lo = N(skip)
    i = int(r.integers(lo, max(lo + 1, len(x) - n - 1)))
    return fit(norm(x[i:i + n]), n)


_grains: dict = {}


def grains(rel: str, k=24, d=0.03, gap=0.03):
    """The k loudest separated transients of a recording, each cut to d seconds (clicks, ticks, relay snaps)."""
    key = (rel, k, d, gap)
    if key in _grains:
        return _grains[key]
    USED_PACKS.add(rel.split("/")[0])
    x = hp(SS.load(rel), 60, 2)
    hop = N(0.001)
    nf = len(x) // hop
    e = np.array([np.sqrt(np.mean(x[i * hop:(i + 1) * hop] ** 2) + 1e-20) for i in range(nf)])
    edb = 20 * np.log10(e)
    picked: list[int] = []
    for rise, rng_db in ((9, 36), (6, 40), (4, 45), (2.5, 50)):
        cand = [i for i in range(6, nf) if edb[i] - edb[i - 6:i - 1].max() > rise and edb[i] > edb.max() - rng_db]
        for i in sorted(cand, key=lambda i: -edb[i]):
            if all(abs(i - j) * hop > N(gap) for j in picked):
                picked.append(i)
            if len(picked) >= k:
                break
        if len(picked) >= min(k, 4):
            break
    out = []
    for i in sorted(picked):
        a = max(0, i * hop - N(0.0015))
        g = x[a:a + N(d)]
        j = int(np.argmax(np.abs(g) >= 0.2 * pk(g)))
        g = x[a + max(0, j - N(0.0004)):a + max(0, j - N(0.0004)) + N(d)]
        out.append(fade(norm(g), 0.0002, d * 0.4))
    if not out:
        raise SystemExit(f"no transients found in {rel}")
    _grains[key] = out
    return out


def dominant(x, lo=1000.0, hi=9000.0, d=0.25):
    s = x[:N(d)]
    sp = np.abs(np.fft.rfft(s * np.hanning(len(s)), 1 << 16))
    f = np.fft.rfftfreq(1 << 16, 1 / SR)
    m = (f >= lo) & (f <= hi)
    return float(f[m][np.argmax(sp[m])])


# ----------------------------------------------------------------------------------------------- the factory

def read_wav(path):
    with wave.open(path, "rb") as w:
        sr, ch, sw, n = w.getframerate(), w.getnchannels(), w.getsampwidth(), w.getnframes()
        raw = w.readframes(n)
    assert sw == 2, path
    x = np.frombuffer(raw, dtype="<i2").astype(float) / 32768.0
    x = x.reshape(-1, ch).T
    if sr != SR:
        g = np.gcd(sr, SR)
        x = signal.resample_poly(x, SR // g, sr // g, axis=-1)
    return x


def onset30(x):
    m = np.abs(mono(x))
    return int(np.argmax(m >= 0.3 * m.max())) if m.max() > 0 else 0


def _env_cs(t, atk, tau):
    return np.where(t < 0, 0.0, np.where(t < atk, t / max(atk, 1e-9), np.exp(-(t - atk) / tau)))


def _sfx(seconds, fn):
    sr = 44100
    n = int(seconds * sr)
    t = np.arange(n) / sr
    y = np.clip(fn(t, sr) * np.minimum(1.0, (n - np.arange(n)) / 300.0), -1, 1)
    return signal.resample_poly(y, 160, 147)


def _onepole_lp(x, fc, sr):
    a = 1 - np.exp(-2 * np.pi * fc / sr)
    return signal.lfilter([a], [1, -(1 - a)], x)


def _bp_cs(x, fc, q, sr):
    w = 2 * np.pi * fc / sr
    al = np.sin(w) / (2 * q)
    a0 = 1 + al
    return signal.lfilter([al / a0, 0, -al / a0], [1, -2 * np.cos(w) / a0, (1 - al) / a0], x)


def _sweep(f0, f1, dur, t, sr):
    k = np.clip(t / dur, 0, 1)
    return np.sin(np.cumsum(2 * np.pi * f0 * (f1 / f0) ** k / sr))


def factory_body():
    r = np.random.default_rng(7200)

    def fn(t, sr):
        return (_sweep(240, 160, 0.09, t, sr) * _env_cs(t, 0, 0.025) * 0.5
                + _bp_cs(r.uniform(-1, 1, len(t)), 1200, 1, sr) * _env_cs(t, 0, 0.015) * 0.3) * 0.9
    return _sfx(0.09, fn)


def factory_head():
    r = np.random.default_rng(7300)

    def fn(t, sr):
        S_ = lambda hz: np.sin(2 * np.pi * hz * t)
        nz = r.uniform(-1, 1, len(t))
        h = nz - _onepole_lp(nz, 4000, sr)
        return np.tanh(1.2 * (h * _env_cs(t, 0, 0.002) * 0.25 + S_(650) * _env_cs(t, 0.001, 0.12) * 0.45
                              + S_(180) * _env_cs(t, 0, 0.02) * 0.4 + S_(1950) * _env_cs(t, 0.001, 0.09) * 0.5
                              + S_(5380) * _env_cs(t, 0.001, 0.04) * 0.18 + S_(1794) * _env_cs(t, 0.001, 0.07) * 0.2)) * 0.6
    return _sfx(0.35, fn)


def factory_kill(k):
    f = 660 * 2 ** (KILL_SEMIS[k - 1] / 12)
    ace = k == 5
    r = np.random.default_rng(7000 + k)

    def fn(t, sr):
        S_ = lambda hz: np.sin(2 * np.pi * hz * t)
        nz = r.uniform(-1, 1, len(t))
        s = (S_(f) + 0.4 * S_(2 * f) + 0.15 * S_(3 * f)) * _env_cs(t, 0.002, 0.15) * 0.45 \
            + (nz - _onepole_lp(nz, 6000, sr)) * _env_cs(t, 0, 0.06) * 0.08 + S_(90) * _env_cs(t, 0, 0.04) * 0.35
        if ace:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                s = s + S_(f * m) * _env_cs(t - 0.04 * i, 0.002, 0.3) * 0.25
        return s
    return _sfx(0.8 if ace else 0.45, fn)


class Shot:
    def __init__(self, gun, path):
        self.gun, self.path, self.name = gun, path, os.path.basename(path)
        self.x = mono(read_wav(path))
        self.on = onset30(self.x)
        lo = lp(self.x, 300, 4)
        self.low, self.mid = lo, self.x - lo   # exact complement
        self.m = 0.0                            # how much of its own mid band the core keeps (solved in solve_cores)
        self.c = 1.0                            # the core's gain (solved in solve_cores; never under -4.5 dB)

    def core(self, m=None, c=None, trim=True):
        m = self.m if m is None else m
        c = self.c if c is None else c
        n = len(self.x)
        w = np.ones(n)
        a, b = self.on + N(0.008), self.on + N(0.014)
        w[b:] = 0
        w[a:b] = 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, b - a))
        y = w * self.x + (1 - w) * (self.low + m * self.mid)
        if not trim:
            return y * c
        e = np.abs(y)
        k = np.nonzero(e > pk(y) * 10 ** (-62 / 20))[0]
        end = min(n, (k[-1] if len(k) else n) + N(0.02))
        return fade(y[:end], 0, 0.01) * c


class Factory:
    def __init__(self, project):
        self.wdir = os.path.join(project, "assets", "audio", "weapons")
        self.shots: dict[str, list[Shot]] = {}
        for cls, guns in CLASSES.items():
            lst = []
            for g in guns:
                stem = SHOT_FILES.get(g, g + "_shot")
                for i in range(1, 4):
                    p = os.path.join(self.wdir, f"{stem}_{i:02d}.wav")
                    if os.path.isfile(p):
                        lst.append(Shot(g, p))
            if not lst:
                raise SystemExit(f"no factory shots for class {cls} in {self.wdir}")
            self.shots[cls] = lst
        self.reloads = {k: mono(read_wav(os.path.join(self.wdir, f))) for k, f in RELOADS.items()}
        self.body, self.head = factory_body(), factory_head()
        self.kills = [factory_kill(k) for k in range(1, 6)]

    def all_shots(self):
        seen = {}
        for lst in self.shots.values():
            for s in lst:
                seen[s.name] = s
        return list(seen.values())


# ----------------------------------------------------------------------------------------------- measurements

def _kw_coeffs():
    A, w0 = 10 ** (4.0 / 40), 2 * np.pi * 1500 / SR
    al = np.sin(w0) / (2 / np.sqrt(2)); c, sA = np.cos(w0), np.sqrt(A)
    b1 = [A * ((A + 1) + (A - 1) * c + 2 * sA * al), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - 2 * sA * al)]
    a1 = [(A + 1) - (A - 1) * c + 2 * sA * al, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - 2 * sA * al]
    w0 = 2 * np.pi * 38 / SR
    al, c = np.sin(w0) / (2 * 0.5), np.cos(w0)
    b2 = [(1 + c) / 2, -(1 + c), (1 + c) / 2]; a2 = [1 + al, -2 * c, 1 - al]
    return (np.array(b1) / a1[0], np.array(a1) / a1[0]), (np.array(b2) / a2[0], np.array(a2) / a2[0])


_KW = _kw_coeffs()


def kw(x):
    (b1, a1), (b2, a2) = _KW
    return signal.lfilter(b2, a2, signal.lfilter(b1, a1, x, axis=-1), axis=-1)


def loud(x, t0=0.0, d=None, weighted=None):
    """K-weighted loudness (LUFS-like, ungated) of [t0, t0+d]; mono counts as a centred stereo source."""
    y = st(x) if weighted is None else weighted
    if weighted is None:
        y = kw(y)
    a = N(t0)
    b = y.shape[-1] if d is None else min(y.shape[-1], a + N(d))
    s = y[:, a:b]
    if s.shape[-1] == 0:
        return -120.0
    return -0.691 + 10 * np.log10(np.sum(np.mean(s ** 2, axis=-1)) + 1e-20)


def mom_max(x, win=0.1):
    y = kw(st(x))
    best = -120.0
    for a in range(0, max(1, y.shape[-1] - N(win) + 1), N(0.01)):
        best = max(best, loud(None, a / SR, win, weighted=y))
    return best


def env_db(x, frame=0.005):
    m = mono(x)
    hop = N(frame)
    nf = max(1, len(m) // hop)
    e = np.array([np.sqrt(np.mean(m[i * hop:(i + 1) * hop] ** 2) + 1e-20) for i in range(nf)])
    return 20 * np.log10(e), hop


def t_down(x, dbdown):
    """Seconds from the onset until the 5 ms envelope first falls dbdown under its peak (after the peak)."""
    e, hop = env_db(x)
    top = int(np.argmax(e))
    below = np.nonzero(e[top:] < e[top] - dbdown)[0]
    end = (top + below[0]) if len(below) else len(e)
    return max(0.0, end * hop / SR - onset30(x) / SR)


def t_last(x, dbdown):
    e, hop = env_db(x, 0.002)
    k = np.nonzero(e > e.max() - dbdown)[0]
    return (k[-1] + 1) * hop / SR - onset30(x) / SR


def spectrum(x, t0=0.0, d=None):
    m = mono(x)[N(t0):]
    if d is not None:
        m = m[:N(d)]
    nf = 1 << int(np.ceil(np.log2(max(len(m), 4096))))
    sp = np.abs(np.fft.rfft(m * np.hanning(len(m)), nf)) ** 2
    return np.fft.rfftfreq(nf, 1 / SR), sp


def centroid(x, t0=0.0, d=None):
    f, p = spectrum(x, t0, d)
    m = f > 40
    return float((f[m] * p[m]).sum() / (p[m].sum() + 1e-20))


def frac_below(x, fc):
    f, p = spectrum(x)
    return float(p[(f > 20) & (f < fc)].sum() / (p[f > 20].sum() + 1e-20))


def tonal_above(x, fc):
    """Strongest peak prominence above fc (dB over the 1/3-octave median), only for peaks within 45 dB of the spectrum max."""
    f, p = spectrum(x)
    pdb = 10 * np.log10(p + 1e-20)
    top = pdb[f > 40].max()
    idx = signal.find_peaks(pdb)[0]
    worst = 0.0
    for i in idx:
        if f[i] < fc or f[i] > 16000 or pdb[i] < top - 45:
            continue
        band = (f > f[i] * 2 ** (-1 / 6)) & (f < f[i] * 2 ** (1 / 6))
        worst = max(worst, pdb[i] - np.median(pdb[band]))
    return worst


def fingerprint(x, d=0.3):
    f, p = spectrum(x, onset30(x) / SR, d)
    edges = np.geomspace(150, 16000, 21)
    e = np.array([p[(f >= a) & (f < b)].sum() + 1e-20 for a, b in zip(edges[:-1], edges[1:])])
    v = 10 * np.log10(e)
    ed, _ = env_db(x, 0.01)
    ed = ed - ed.max()
    tail = np.array([np.mean(fit(ed, 40)[i:i + 5]) for i in range(0, 40, 5)])
    return np.concatenate([v - v.mean(), 0.5 * np.clip(tail, -60, 0)])


# ----------------------------------------------------------------------------------------------- the lines

def jit(r, db=1.5):
    return 10 ** (r.uniform(-db, db) / 20)


class Line:
    """A line's recipe. Subclasses pick the recordings and the processing; the base class lays them out."""
    id = ""
    drive = 1.4           # saturation
    mid_gain = 0.55       # the gun's own mid band, line-coloured
    mat_gain = 0.45       # the gun's mid burst convolved with the line's material
    sw_gain = 0.8         # the themed recorded sweetener
    rt = 0.55             # plate time (x class tail factor)
    damp = 7000.0
    wet = 1.0
    head_partials = [(1.0, 0.085, 1.0)]   # (ratio to 2960, tau, amp); the first is the dominant
    head_twin = 0.0012
    thump = 0.6

    # --- recipe pieces (mono unless noted) ---
    def color(self, m):
        return m

    def mat(self, v):
        raise NotImplementedError

    def sweet(self, cls, v, r):
        raise NotImplementedError

    def texture(self, cls, v, r, d):
        """Stereo tail texture (rain, crackle, shimmer…), d seconds."""
        return buf(d)

    def body_src(self, v):
        raise NotImplementedError

    def head_ping(self, v):
        raise NotImplementedError

    def head_click(self, v, r):
        return hp(r.standard_normal(N(0.004)) * env(N(0.004), 0.0001, 0.0008), 2500)

    def elem(self, i, r, k=1):
        """One themed hit for kills / reload layers (mono or stereo)."""
        raise NotImplementedError

    def killfx(self, r):
        raise NotImplementedError

    def equip(self, r):
        raise NotImplementedError

    def inspect(self, r):
        raise NotImplementedError

    # --- builders ---
    def fire(self, cls, v, fac: Factory):
        P = CLS[cls]
        r = R(self.id, "fire", cls, v)
        shots = fac.shots[cls]
        sh = shots[v % len(shots)]
        a = sh.on - N(OFF)
        m = sh.mid[max(0, a):] if a >= 0 else np.pad(sh.mid, (-a, 0))
        dlen = N(OFF + P["dry"] * 3)
        m = fit(m, dlen) * env(dlen, 0.0005, P["dry"] * 0.45, hold=OFF + P["dry"] * 0.4)
        burst = m[:N(OFF + 0.03)] * env(N(OFF + 0.03), 0.0003, 0.007, start=OFF)
        dry = np.zeros(N(OFF + P["tail"] + P["dry"]))
        put(dry, 0, self.color(m), self.mid_gain * jit(r))
        mt = conv(burst, self.mat(v))
        put(dry, 0, norm(mt, pk(m)), self.mat_gain * jit(r))
        sw = self.sweet(cls, v, r)
        put(dry, OFF, norm(sw, pk(m)), self.sw_gain * jit(r))
        if P.get("dark"):
            dry = lp(dry, 4500, 2)
        dry = sat(tone(hp(dry, 280, 2)), self.drive)
        ir = plate(self.rt * (0.5 + P["tail"]), self.damp, seed=zlib.crc32(f"{self.id}{cls}".encode()) % 997, pre=0.006)
        wet = conv(dry, ir)[:, :len(dry)]
        out = st(dry) + wet * P["wet"] * self.wet
        tex = self.texture(cls, v, r, P["tail"] + P["dry"])
        if P.get("dark"):
            tex = lp(tex, 5000, 2)
        put(out, OFF, tex, pk(m) * jit(r))
        out = pitch(out, 1 + r.uniform(-0.025, 0.025))
        out = fit(out, N(OFF + P["tail"] + P["dry"]))
        return fade(out, 0, 0.03)

    def hit_body(self, v):
        r = R(self.id, "body", v)
        x = self.body_src(v)
        x = lp(hp(x, 70, 2), 1150, 4)
        th = np.sin(np.cumsum(2 * np.pi * (95 + 85 * np.exp(-T(0.09) / 0.012)) / SR)) * env(N(0.09), 0.0004, 0.016)
        y = fit(x, N(0.09)) + self.thump * th * pk(x)
        y = sat(y * env(len(y), 0.0003, 0.011, hold=0.018), 1.3)
        y = lp(y, 1500, 2)
        return fade(fit(y, N(0.082)), 0.0002, 0.004)

    def hit_head(self, v):
        r = R(self.id, "head", v)
        f0 = HEAD_HZ * (1 + r.uniform(-0.006, 0.006))
        d = 0.52
        y = np.zeros(N(d))
        fr, taus, amps = zip(*self.head_partials)
        md = modal(d, [f0 * q for q in fr], taus, [a * (jit(r, 1.0) if i else 1.0) for i, a in enumerate(amps)], r)
        md += self.head_twin and modal(d, [f0 * (1 + self.head_twin)], [taus[0] * 0.9], [0.35], r)
        put(y, 0, md, 1.0)
        ping = self.head_ping(v)
        dm = dominant(ping, 1200, 9000)
        ping = hp(pitch(ping, float(np.clip(f0 / dm, 0.4, 2.6))), 1400, 2)
        put(y, 0.0003, norm(ping), 0.45 * jit(r, 1.0))
        put(y, 0, norm(self.head_click(v, r)), 0.5)
        y = y + 0.12 * conv(y, room_mono(0.22, 9000, seed=v))[:len(y)]
        y = sat(peq(y, 300, -4, 0.8), 1.15)
        return fade(y, 0.0002, 0.05)

    def kill_n(self, k, fac: Factory):
        r = R(self.id, "killn", k)
        ref = fac.kills[k - 1]
        d = len(ref) / SR
        out = buf(d)
        hits = [1, 2, 3, 4, 6][k - 1]
        gap = 0.06 if k < 5 else 0.045
        for i in range(hits):
            e = self.elem(i, r, k)
            put(out, i * gap, e, (0.75 + 0.25 * (i == hits - 1)) * jit(r, 1.0))
        if k >= 3:
            put(out, 0.02, self.texture("rifle", k, r, d - 0.03), 0.25 * (k - 2))
        out = lp(out, 2200 * 1.6 ** (k - 1), 2)        # brighter with every kill
        out = out + conv(mono(out), plate(0.35 + 0.08 * k, self.damp, seed=k))[:, :out.shape[-1]] * 0.25
        return fade(fit(out, len(ref)), 0.0002, 0.03)

    def reload_layer(self, key, fac: Factory):
        x = fac.reloads[key]
        r = R(self.id, "reload", key)
        out = buf(len(x) / SR)
        for i, t in enumerate(events_of(x, SR, keep=4)):
            e = self.elem(i + 2, r, 1)
            put(out, t + 0.004, hp(e, 600, 2), 0.8 * jit(r, 1.5))
        return fade(out, 0.0002, 0.05)

    def counter(self, r):
        return fit(st(self.elem(0, r, 1)), N(0.14)) * 0.4

    def fin(self, r):
        raise NotImplementedError

    def rs_a(self, r):
        raise NotImplementedError

    def rs_b(self, r):
        raise NotImplementedError


def events_of(x, sr, keep=4, merge=0.07, rel_db=24.0):
    hop = max(1, int(0.005 * sr))
    nf = len(x) // hop
    e = np.array([20 * np.log10(np.sqrt(np.mean(x[i * hop:(i + 1) * hop] ** 2) + 1e-20)) for i in range(nf)])
    top = e.max()
    cands = [i for i in range(nf) if e[i] - (np.max(e[max(0, i - 6):i]) if i > 0 else -120) >= 6 and e[i] > top - rel_db]
    groups: list[list[int]] = []
    for i in cands:
        if groups and (i - groups[-1][-1]) * hop / sr <= merge:
            groups[-1].append(i)
        else:
            groups.append([i])
    scored = []
    for g in groups:
        a = max(0, (g[0] - 1) * hop)
        s = x[a:min(len(x), g[-1] * hop + int(0.03 * sr))]
        scored.append((np.max(e[g[0]:min(nf, g[-1] + 6)]), (a + int(np.argmax(np.abs(s) >= 0.3 * pk(s)))) / sr))
    scored.sort(reverse=True)
    return sorted(t for _, t in scored[:keep])


# ---- shared pieces

def whoosh(d, f0, f1, r, q=1.2, atk=0.3):
    n = N(d)
    y = tv_bp(pink(n, r), f0, f1, q)
    return y * env(n, d * atk, d * 0.35, hold=0)


def stereo_pair(fn):
    """Calls fn(channel) twice for a decorrelated stereo texture."""
    return np.vstack([fn(0), fn(1)])


def ratchet(n, rate, gl, r, accel=0.0, start_gain=1.0, end_gain=1.0):
    """A gear ratchet / ticking train from recorded click grains."""
    d = sum(1 / (rate * (1 + accel * i / max(n - 1, 1))) for i in range(n)) + 0.06
    out = np.zeros(N(d))
    t = 0.0
    for i in range(n):
        g = gl[int(r.integers(len(gl)))]
        put(out, t, pitch(g, r.uniform(0.94, 1.06)), (start_gain + (end_gain - start_gain) * i / max(n - 1, 1)) * jit(r, 1.5))
        t += 1 / (rate * (1 + accel * i / max(n - 1, 1)))
    return out


def tail_mix(x, rt, damp, wet, seed=0, pre=0.006):
    ir = plate(rt, damp, seed, pre)
    w = conv(mono(x), ir)
    out = fit(st(x), w.shape[-1]) + wet * w
    return out


def finish(x, d, fout=0.05):
    return fade(fit(st(x), N(d)), 0.0003, fout)


# ================================================================ Signature

class Glasswork(Line):
    id = "glasswork"; damp = 10000; rt = 0.6; drive = 1.25
    head_partials = [(1.0, 0.09, 1.0), (0.52, 0.05, 0.30), (1.69, 0.04, 0.22), (2.71, 0.025, 0.12)]

    def color(self, m):
        return lp(peq(peq(m, 2600, 3, 1.5), 5200, 2, 1.5), 11000, 2)

    def mat(self, v):
        return S(KI + f"impactGlass_light_00{v % 5}.ogg", dur=0.06, r=1.15)

    def sweet(self, cls, v, r):
        y = hp(S(KU + ["glass_002", "glass_005", "glass_006", "glass_001", "glass_002"][v] + ".ogg", r=1.2 + 0.04 * v), 900)
        if cls in ("shotgun", "sniper"):
            y = fit(y, N(0.4)) + 0.5 * fit(S(KI + f"impactGlass_medium_00{v}.ogg", r=1.1), N(0.4))
        return y

    def texture(self, cls, v, r, d):
        def ch(c):
            g = S(BF + f"bfh1_glass_falling_0{1 + (v + c) % 4}.ogg", start=0.03 + 0.037 * c)
            return fit(hp(g, 2500), N(d)) * env(N(d), 0.01, d * 0.35)
        return stereo_pair(ch) * 0.16

    def body_src(self, v):
        return S(KI + f"impactGlass_heavy_00{v}.ogg", r=0.62)

    def head_ping(self, v):
        return S(KU + ["glass_001", "glass_002", "glass_005", "glass_006", "glass_001"][v] + ".ogg")

    def elem(self, i, r, k=1):
        src = [KI + f"impactGlass_light_00{(i + k) % 5}.ogg", KU + ["glass_002.ogg", "glass_005.ogg", "glass_006.ogg"][i % 3]]
        y = S(src[i % 2], r=1.0 + 0.05 * k + r.uniform(-0.03, 0.03))
        return hp(y, 700)

    def killfx(self, r):
        out = buf(1.0)
        put(out, 0, S(BF + "bfh1_glass_breaking_03.ogg"), 0.9)
        put(out, 0.0, S(KI + "impactGlass_heavy_002.ogg", r=1.1), 0.5)
        put(out, 0.12, self.texture("sniper", 2, r, 0.8), 1.3)
        return finish(tail_mix(out, 0.8, 10000, 0.25), 1.0)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, hp(S(KU + "scratch_005.ogg", r=0.7), 2000), 0.25)
        put(out, 0.07, S(KU + "glass_001.ogg", r=1.1), 0.7)
        put(out, 0.16, S(KU + "glass_006.ogg", r=1.3), 0.4)
        return finish(tail_mix(out, 0.6, 10000, 0.3), 0.7)

    def inspect(self, r):
        out = buf(2.0)
        rub = tv_bp(pink(N(1.6), r), 2200, 3400, 8) * env(N(1.6), 0.5, 0.5)
        put(out, 0.1, st(rub), 0.35)
        for i, t in enumerate([0.15, 0.62, 1.05, 1.32]):
            put(out, t, self.elem(i, r, 2), 0.5 - 0.06 * i)
        put(out, 0.4, self.texture("sniper", 4, r, 1.2), 0.6)
        return finish(tail_mix(out, 0.9, 10000, 0.3), 2.0, 0.3)


class Thunderhead(Line):
    id = "thunderhead"; damp = 4200; rt = 0.75; drive = 1.5
    head_partials = [(1.0, 0.085, 1.0), (0.414, 0.03, 0.25), (2.13, 0.03, 0.18)]

    def color(self, m):
        return peq(peq(m, 700, 3, 0.8), 3000, -2, 1.0)

    def mat(self, v):
        return S(KS + f"explosionCrunch_00{v % 4}.ogg", dur=0.05)

    def sweet(self, cls, v, r):
        y = fit(bpf(S(KS + f"explosionCrunch_00{(v + 1) % 4}.ogg", dur=0.12, r=1.25), 400, 5000), N(0.12))
        y = y + 0.6 * norm(crackle(0.12, 2500, 0.025, r, 2500))
        return y

    def texture(self, cls, v, r, d):
        def ch(c):
            rain = seg(RN + "2.ogg", d, R(self.id, "rain", cls, v, c))
            return hp(rain, 1200) * env(N(d), 0.01, d * 0.4)
        out = stereo_pair(ch) * 0.22
        if cls in ("sniper", "shotgun"):
            put(out, 0.05, hp(S(KS + "lowFrequency_explosion_000.ogg", r=1.3), 250), 0.35)
        return out

    def body_src(self, v):
        x = S(KS + "lowFrequency_explosion_001.ogg", dur=0.09, r=1.5 + 0.08 * v)
        return x + 0.6 * fit(S(KI + f"impactSoft_medium_00{v}.ogg"), len(x))

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{(v + 2) % 5}.ogg")

    def head_click(self, v, r):
        return crackle(0.008, 9000, 0.004, r, 3000)

    def elem(self, i, r, k=1):
        y = buf(0.4, stereo=False)
        put(y, 0, norm(crackle(0.05, 4000, 0.012, r, 2000)), 0.8)
        put(y, 0.005, bpf(S(KS + f"explosionCrunch_00{(i + k) % 4}.ogg", dur=0.3, r=1.1 + 0.04 * k), 250, 6000), 0.7)
        return y

    def killfx(self, r):
        out = buf(1.1)
        put(out, 0, norm(crackle(0.15, 6000, 0.03, r, 1800)), 0.9)
        put(out, 0.01, S(KS + "explosionCrunch_002.ogg", r=0.95), 0.7)
        put(out, 0.06, hp(S(KS + "lowFrequency_explosion_000.ogg", r=1.15), 120), 0.8)
        put(out, 0.1, stereo_pair(lambda c: hp(seg(RN + "1.ogg", 1.0, R("tk", c)), 900) * env(N(1.0), 0.08, 0.35)), 0.4)
        return finish(tail_mix(out, 1.0, 4000, 0.2), 1.1, 0.25)

    def equip(self, r):
        out = buf(0.8)
        put(out, 0, fit(hp(S(KS + "lowFrequency_explosion_001.ogg", r=1.4), 150), N(0.8)) * env(N(0.8), 0.15, 0.2), 0.5)
        put(out, 0.05, norm(crackle(0.4, 900, 0.15, r, 2500)), 0.5)
        return finish(tail_mix(out, 0.7, 4000, 0.2), 0.8, 0.15)

    def inspect(self, r):
        out = buf(2.2)
        put(out, 0, stereo_pair(lambda c: hp(seg(RN + "3.ogg", 2.0, R("ti", c)), 700) * env(N(2.0), 0.6, 0.5)), 0.4)
        put(out, 0.5, norm(crackle(1.0, 400, 0.5, r, 2000)), 0.35)
        put(out, 0.9, hp(S(KS + "lowFrequency_explosion_000.ogg", r=1.0), 100), 0.45)
        return finish(tail_mix(out, 1.0, 4000, 0.2), 2.2, 0.35)


class Basalt(Line):
    id = "basalt"; damp = 3500; rt = 0.7; drive = 1.9
    head_partials = [(1.0, 0.07, 1.0), (0.37, 0.04, 0.30), (0.61, 0.035, 0.22), (1.83, 0.03, 0.12)]

    def color(self, m):
        return lp(peq(peq(m, 450, 4, 1.0), 1600, 2, 1.2), 7000, 2)

    def mat(self, v):
        return S(KI + f"impactMining_00{v % 5}.ogg", dur=0.07)

    def sweet(self, cls, v, r):
        src = [BF + "bfh1_rock_hit_01.ogg", BF + "bfh1_rock_breaking_02.ogg", BF + "bfh1_rock_breaking_03.ogg",
               BF + "bfh1_rock_falling_04.ogg", BF + "bfh1_rock_falling_08.ogg"][v]
        y = lp(S(src, dur=0.07), 6000)
        return fit(y, N(0.1)) + 0.5 * fit(S(KI + f"impactMining_00{(v + 2) % 5}.ogg", r=0.85), N(0.1))

    def texture(self, cls, v, r, d):
        def ch(c):
            g = S(BF + f"bfh1_rock_falling_0{5 + (v + c) % 2}.ogg", start=0.02 + 0.03 * c)
            grit = fit(hp(g, 1800), N(d)) * env(N(d), 0.005, d * 0.3)
            rum = bpf(R(self.id, cls, v, c).standard_normal(N(d)), 300, 700) * env(N(d), 0.01, 0.12) * 0.2
            return grit * 0.6 + rum
        return stereo_pair(ch) * 0.25

    def body_src(self, v):
        return S(KI + f"impactMining_00{v}.ogg", r=0.8)

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{v}.ogg")

    def head_click(self, v, r):
        return hp(S(BF + "bfh1_rock_hit_01.ogg", dur=0.006), 2500)

    def elem(self, i, r, k=1):
        src = [KI + f"impactMining_00{(i + k) % 5}.ogg", BF + "bfh1_rock_hit_01.ogg", BF + "bfh1_rock_breaking_03.ogg"][i % 3]
        return lp(S(src, r=0.95 + 0.04 * k + r.uniform(-0.03, 0.03)), 7000)

    def killfx(self, r):
        out = buf(1.0)
        put(out, 0, S(BF + "bfh1_rock_breaking_01.ogg"), 0.9)
        put(out, 0.08, S(BF + "bfh1_rock_falling_03.ogg"), 0.5)
        put(out, 0, lp(S(KI + "impactMining_000.ogg", r=0.7), 900), 0.6)
        return finish(tail_mix(out, 0.9, 3500, 0.25, pre=0.012), 1.0, 0.2)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, fit(hp(S(BF + "bfh1_rock_falling_06.ogg", r=0.8), 300), N(0.6)) * env(N(0.6), 0.08, 0.15), 0.4)
        put(out, 0.18, S(KI + "impactMining_002.ogg", r=0.9), 0.6)
        return finish(tail_mix(out, 0.7, 3500, 0.25), 0.7)

    def inspect(self, r):
        out = buf(2.0)
        grind = bpf(pink(N(1.4), r), 300, 2500) * env(N(1.4), 0.4, 0.4)
        put(out, 0.1, st(grind), 0.3)
        put(out, 0.2, self.texture("sniper", 1, r, 1.0), 0.7)
        for t in (0.25, 0.9, 1.4):
            put(out, t, self.elem(int(t * 10), r, 2), 0.5)
        return finish(tail_mix(out, 1.0, 3500, 0.25), 2.0, 0.3)


class Oscilloscope(Line):
    id = "oscilloscope"; damp = 8000; rt = 0.4; drive = 2.2
    head_partials = [(1.0, 0.08, 1.0), (1.0405, 0.05, 0.18), (0.9595, 0.05, 0.18), (0.5, 0.03, 0.15)]

    def color(self, m):
        return lp(peq(np.clip(m * 1.6, -pk(m), pk(m)), 1200, 3, 2.0), 9000, 2)

    def relay(self):
        return grains(EC + "equipment_clicks3.wav", 30, 0.03)

    def mat(self, v):
        return grains(MC + f"mechanical_clicks-0{1 + v % 5}.flac", 6, 0.04)[0]

    def sweet(self, cls, v, r):
        y = np.zeros(N(0.08))
        put(y, 0, hp(S(MC + "buzzerr-01.flac", dur=0.035, r=1 + 0.05 * v), 1500), 0.8)
        put(y, 0.018, self.relay()[v * 3 % len(self.relay())], 0.45)
        return y

    def texture(self, cls, v, r, d):
        def ch(c):
            h = bpf(seg(ED + "qubodup-edev.flac", d, R(self.id, cls, v, c), skip=0.05), 150, 3000)
            hum = sum(np.sin(2 * np.pi * f * T(d)) * a for f, a in ((360, 0.5), (480, 0.3), (600, 0.2)))
            return (h * 0.7 + hum * 0.15) * env(N(d), 0.004, min(0.08, d * 0.3))
        return stereo_pair(ch) * 0.25

    def body_src(self, v):
        return fit(S(MC + "mechanical_button-01.flac", r=0.75 + 0.05 * v), N(0.09)) + 0.7 * fit(grains(MC + "mechanical_clicks-02.flac", 8, 0.06)[v % len(grains(MC + "mechanical_clicks-02.flac", 8, 0.06))], N(0.09))

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{(v + 1) % 5}.ogg")

    def head_click(self, v, r):
        return self.relay()[(v * 5 + 1) % len(self.relay())][:N(0.006)]

    def elem(self, i, r, k=1):
        y = np.zeros(N(0.25))
        put(y, 0, self.relay()[(i * 7 + k) % len(self.relay())], 0.9)
        put(y, 0.008, hp(S(MC + "buzzerr-01.flac", dur=0.05, r=1.0 + 0.05 * k), 1000), 0.35)
        return y

    def killfx(self, r):
        out = buf(1.0)
        hum = seg(ED + "qubodup-edev.flac", 1.2, R("ok"), skip=0.05)
        drop = fit(pitch(hum, 1.4), N(0.9)) * np.linspace(1, 0, N(0.9)) ** 1.5
        put(out, 0, self.relay()[3], 0.9)
        put(out, 0.004, hp(S(MC + "buzzerr-01.flac"), 800), 0.7)
        put(out, 0.03, bpf(tv_bp(drop, 3000, 300, 2.0), 150, 6000), 0.9)
        return finish(tail_mix(out, 0.5, 8000, 0.2), 1.0)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, self.relay()[1], 0.8)
        put(out, 0.06, self.relay()[8], 0.6)
        hum = bpf(seg(ED + "qubodup-edev.flac", 0.6, R("oe"), skip=0.05), 120, 4000) * env(N(0.6), 0.12, 0.12)
        put(out, 0.08, hum, 0.6)
        return finish(tail_mix(out, 0.4, 8000, 0.2), 0.7)

    def inspect(self, r):
        out = buf(2.0)
        put(out, 0, ratchet(7, 9, self.relay(), r, accel=0.3, start_gain=0.6, end_gain=0.9), 0.6)
        put(out, 0.3, stereo_pair(lambda c: bpf(seg(ED + "qubodup-edev.flac", 1.5, R("oi", c), skip=0.05), 120, 5000) * env(N(1.5), 0.3, 0.4)), 0.35)
        put(out, 1.2, hp(S(MC + "buzzerr-01.flac", r=1.1), 900), 0.25)
        return finish(tail_mix(out, 0.5, 8000, 0.2), 2.0, 0.3)


class Rally(Line):
    id = "rally"; damp = 6000; rt = 0.5; drive = 1.7
    head_partials = [(1.0, 0.08, 1.0), (0.27, 0.05, 0.28), (2.76, 0.02, 0.15)]

    def color(self, m):
        e = np.maximum(np.abs(m), 1e-4)
        g = (np.convolve(e, np.ones(48) / 48, "same") / (pk(m) + 1e-9) + 0.25) ** -0.4   # fast, punchy compression
        return peq(peq(m * g, 900, 3, 1.0), 4000, 2, 1.0)

    def mat(self, v):
        return S(MW + ["tools_02", "tools_07", "tools_08", "tools_11", "tools_12"][v] + ".ogg", dur=0.05)

    def sweet(self, cls, v, r):
        src = [MW + "metal_hit_01.ogg", MW + "metal_hit_04.ogg", MW + "metal_hit_05.ogg", BF + "bfh1_metal_hit_02.ogg", BF + "bfh1_metal_hit_06.ogg"][v]
        return lp(S(src, dur=0.07), 7000)

    def texture(self, cls, v, r, d):
        def ch(c):
            h = bpf(seg(KS + "thrusterFire_000.ogg", d * 1.2, R(self.id, cls, v, c), skip=0.5), 1500, 8000)
            h = pitch(h, 1.12)[:N(d)]
            return fit(h, N(d)) * env(N(d), 0.005, d * 0.22)
        return stereo_pair(ch) * 0.3

    def body_src(self, v):
        x = S(KI + f"impactSoft_heavy_00{v}.ogg", r=1.25)
        return x + 0.6 * fit(S(KI + f"impactPunch_medium_00{v}.ogg", thresh=-20), len(x))

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{v}.ogg")

    def head_click(self, v, r):
        return hp(S(MW + "tools_07.ogg", dur=0.006), 2500)

    def wrench(self, n, rate, r):
        gl = [hp(S(MW + f"tools_{i:02d}.ogg", dur=0.03), 900) for i in (2, 7, 8, 11, 12)]
        return ratchet(n, rate, gl, r, accel=0.15)

    def elem(self, i, r, k=1):
        y = np.zeros(N(0.3))
        put(y, 0, self.wrench(2 + k // 2, 38, r), 0.8)
        put(y, 0.02, hp(seg(KS + "thrusterFire_001.ogg", 0.25, r, skip=0.6), 2000) * env(N(0.25), 0.003, 0.05), 0.35)
        return y

    def killfx(self, r):
        out = buf(0.9)
        put(out, 0, self.wrench(9, 34, r), 0.8)
        put(out, 0.25, stereo_pair(lambda c: hp(seg(KS + "thrusterFire_002.ogg", 0.6, R("rk", c), skip=0.5), 1500) * env(N(0.6), 0.004, 0.12)), 0.5)
        return finish(tail_mix(out, 0.5, 6000, 0.2), 0.9)

    def equip(self, r):
        """Engine rev: an engine loop varispeeded up and back."""
        x = seg(KS + "engineCircular_001.ogg", 2.0, R("rev"), skip=0.3)
        d = 0.9
        n = N(d)
        sp = 0.65 + 0.8 * np.sin(np.pi * np.clip(np.arange(n) / n * 1.3, 0, 1)) ** 1.5
        pos = np.cumsum(sp)
        pos = pos[pos < len(x) - 2]
        y = np.interp(pos, np.arange(len(x)), x)
        y = hp(lp(y, 6000), 120) * env(len(y), 0.08, 0.25, hold=0.35)
        out = buf(d)
        put(out, 0, st(y), 0.8)
        put(out, 0.0, hp(S(MW + "tools_07.ogg"), 800), 0.3)
        return finish(tail_mix(out, 0.4, 6000, 0.15), d, 0.15)

    def inspect(self, r):
        out = buf(2.0)
        put(out, 0.0, self.equip(r), 0.7)
        put(out, 0.9, self.wrench(5, 30, r), 0.5)
        put(out, 1.2, stereo_pair(lambda c: hp(seg(KS + "thrusterFire_003.ogg", 0.6, R("ri", c), skip=0.8), 1500) * env(N(0.6), 0.01, 0.1)), 0.3)
        return finish(out, 2.0, 0.3)


class Frostbite(Line):
    id = "frostbite"; damp = 10000; rt = 0.6; drive = 1.3
    head_partials = [(1.0, 0.075, 1.0), (1.42, 0.03, 0.24), (2.3, 0.02, 0.14), (0.71, 0.03, 0.12)]

    def color(self, m):
        return hp(peq(peq(m, 3400, 3, 1.0), 600, -3, 1.0), 400, 2)

    def mat(self, v):
        return S(KI + f"impactGlass_light_00{(v + 2) % 5}.ogg", dur=0.05, r=1.5)

    def sweet(self, cls, v, r):
        y = hp(S(MW + f"wood_cracking_0{1 + v % 4}.ogg", r=1.45), 1500)
        return fit(y, N(0.12)) + 0.35 * fit(hp(S(KI + f"footstep_snow_00{v}.ogg"), 1000), N(0.12))

    def texture(self, cls, v, r, d):
        def ch(c):
            rr = R(self.id, cls, v, c)
            fr = crackle(d, 1800, d * 0.4, rr, 4000)
            wind = tv_bp(pink(N(d), rr), 700, 2200, 2.0) * env(N(d), 0.03, d * 0.5)
            return norm(fr) * 0.6 + norm(wind) * 0.35
        return stereo_pair(ch) * 0.2

    def body_src(self, v):
        return fit(S(KI + f"footstep_snow_00{v}.ogg", r=0.85), N(0.09)) + 0.7 * fit(S(KI + f"impactSoft_medium_00{v}.ogg"), N(0.09))

    def head_ping(self, v):
        return S(KI + f"impactGlass_light_00{v}.ogg")

    def head_click(self, v, r):
        return hp(S(MW + f"wood_cracking_0{1 + v % 4}.ogg", dur=0.006, r=1.4), 3000)

    def elem(self, i, r, k=1):
        y = hp(S(MW + f"wood_cracking_0{1 + (i + k) % 4}.ogg", r=1.3 + 0.04 * k + r.uniform(-0.03, 0.03)), 1200)
        return fit(y, N(0.25)) + 0.3 * fit(hp(S(KI + f"impactGlass_light_00{(i + k) % 5}.ogg", r=1.6), 2000), N(0.25))

    def killfx(self, r):
        out = buf(1.0)
        put(out, 0, hp(S(MW + "wood_breaking_02.ogg", r=1.3), 800), 0.8)
        put(out, 0.03, hp(S(BF + "bfh1_glass_breaking_05.ogg", r=1.2), 1500), 0.45)
        put(out, 0.05, self.texture("sniper", 0, r, 0.9), 1.4)
        return finish(tail_mix(out, 0.8, 10000, 0.25), 1.0, 0.2)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, st(whoosh(0.6, 600, 2500, r)), 0.35)
        put(out, 0.1, norm(crackle(0.5, 1200, 0.2, r, 4000)), 0.4)
        put(out, 0.05, self.elem(0, r, 1), 0.5)
        return finish(tail_mix(out, 0.6, 10000, 0.25), 0.7)

    def inspect(self, r):
        out = buf(2.2)
        put(out, 0, stereo_pair(lambda c: whoosh(2.0, 500, 1800, R("fi", c), atk=0.4)), 0.35)
        put(out, 0.2, self.texture("sniper", 3, r, 1.6), 1.0)
        for t in (0.3, 1.1):
            put(out, t, self.elem(int(t * 10), r, 2), 0.4)
        return finish(tail_mix(out, 0.9, 10000, 0.25), 2.2, 0.35)


class Hive(Line):
    id = "hive"; damp = 3500; rt = 0.45; drive = 1.5
    head_partials = [(1.0, 0.08, 1.0), (0.47, 0.06, 0.32), (0.83, 0.05, 0.24), (1.31, 0.03, 0.12)]

    def color(self, m):
        return lp(peq(peq(m, 550, 3, 0.9), 3000, -2, 1.0), 4500, 2)

    def mat(self, v):
        return lp(S(WS + f"slime_{[4, 8, 9, 10, 13][v]:02d}.ogg", dur=0.06, r=0.8), 3000)

    def sweet(self, cls, v, r):
        y = lp(S(C1 + f"plop_0{1 + v % 2}.ogg", r=0.7 + 0.04 * v), 2500)
        return fit(y, N(0.1)) + 0.4 * fit(lp(S(WS + f"slime_{[2, 4, 7, 13, 15][v]:02d}.ogg", r=0.9), 3500), N(0.1))

    def buzz(self, d, r, rate=32.0):
        x = seg(KS + f"forceField_00{int(r.integers(5))}.ogg", d, r, skip=0.15)
        t = T(d)
        am = 0.55 + 0.45 * np.sin(2 * np.pi * np.cumsum(rate * (1 + 0.08 * r.standard_normal(len(t)).cumsum() / np.sqrt(len(t)))) / SR)
        return bpf(pitch(x, 1.6)[:len(t)] if len(pitch(x, 1.6)) >= len(t) else fit(pitch(x, 1.6), len(t)), 180, 2500) * am

    def texture(self, cls, v, r, d):
        def ch(c):
            return self.buzz(d, R(self.id, cls, v, c)) * env(N(d), 0.008, min(0.07, d * 0.3))
        return stereo_pair(ch) * 0.3

    def body_src(self, v):
        return fit(lp(S(C1 + f"plop_0{1 + v % 2}.ogg", r=0.55 + 0.05 * v), 1200), N(0.09)) + 0.6 * fit(lp(S(WS + f"slime_{[4, 8, 9, 10, 13][v]:02d}.ogg", r=0.6), 1000), N(0.09))

    def head_ping(self, v):
        return S(KI + f"impactBell_heavy_00{v % 4}.ogg")

    def head_click(self, v, r):
        return hp(S(C1 + "plop_01.ogg", dur=0.006, r=1.5), 2000)

    def elem(self, i, r, k=1):
        y = np.zeros(N(0.3))
        put(y, 0, lp(S(C1 + f"plop_0{1 + (i + k) % 2}.ogg", r=0.75 + 0.05 * k + r.uniform(-0.03, 0.03)), 3500), 0.8)
        put(y, 0.01, self.buzz(0.18, r) * env(N(0.18), 0.01, 0.05), 0.5)
        return y

    def killfx(self, r):
        out = buf(1.1)
        sw = self.buzz(1.0, r, 28) * env(N(1.0), 0.25, 0.25)
        put(out, 0, stereo_pair(lambda c: self.buzz(1.0, R("hk", c), 30 + 4 * c) * env(N(1.0), 0.2, 0.25)), 0.6)
        put(out, 0, sw, 0.3)
        put(out, 0.02, lp(S(WS + "bubble_03.ogg", r=0.8), 4000), 0.5)
        put(out, 0.35, lp(S(C1 + "plop_02.ogg", r=0.6), 3000), 0.5)
        return finish(tail_mix(out, 0.5, 3500, 0.2), 1.1, 0.2)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, stereo_pair(lambda c: self.buzz(0.6, R("he", c), 30) * env(N(0.6), 0.2, 0.12)), 0.6)
        put(out, 0.2, lp(S(C1 + "plop_01.ogg", r=0.7), 3000), 0.5)
        return finish(tail_mix(out, 0.4, 3500, 0.2), 0.7)

    def inspect(self, r):
        out = buf(2.0)
        put(out, 0, stereo_pair(lambda c: self.buzz(1.8, R("hi", c), 26 + 5 * c) * env(N(1.8), 0.5, 0.5)), 0.45)
        for t, s in ((0.5, "bubble_01"), (1.2, "bubble_03")):
            put(out, t, lp(S(WS + s + ".ogg", r=0.7), 3500), 0.4)
        return finish(tail_mix(out, 0.5, 3500, 0.2), 2.0, 0.3)


# ================================================================ Apex

class Foundry(Line):
    id = "foundry"; damp = 5000; rt = 0.6; drive = 1.6
    head_partials = [(1.0, 0.12, 1.0), (0.33, 0.09, 0.30), (0.57, 0.07, 0.25), (1.24, 0.05, 0.18), (2.05, 0.03, 0.10)]

    def color(self, m):
        return peq(peq(m, 1800, 3, 3.0), 3900, 2, 3.0)

    def mat(self, v):
        return S(MW + f"hammer_0{2 + v % 3}.ogg", dur=0.04)

    def anvil(self, v, r=0.62, tau=0.06):
        x = S(KI + f"impactMetal_light_00{v % 5}.ogg", r=r)
        return x * env(len(x), 0.0002, tau)

    def sweet(self, cls, v, r):
        y = fit(S(KI + f"impactMetal_medium_00{v}.ogg", dur=0.06), N(0.12))
        return y + 0.45 * fit(self.anvil(v), N(0.12))

    def sparks(self, d, r, rate=900.0, tau=None):
        return crackle(d, rate, tau or d * 0.4, r, 3500, 15000, grain=0.0004, power=3)

    def texture(self, cls, v, r, d):
        def ch(c):
            rr = R(self.id, cls, v, c)
            roar = bpf(np.cumsum(rr.standard_normal(N(d))), 300, 900) * env(N(d), 0.004, 0.1)
            return norm(self.sparks(d, rr)) * 0.6 + norm(roar) * 0.25
        return stereo_pair(ch) * 0.22

    def body_src(self, v):
        return S(KI + f"impactMetal_heavy_00{v}.ogg", r=0.72)

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{v}.ogg")

    def head_click(self, v, r):
        return hp(S(MW + f"hammer_0{2 + v % 3}.ogg", dur=0.006), 2500)

    def strike(self, v, r, big=1.0):
        y = np.zeros(N(1.0))
        put(y, 0, S(MW + f"hammer_0{1 + v % 4}.ogg"), 0.8)
        put(y, 0, self.anvil(v, 0.55 + 0.05 * (v % 3), tau=0.18 * big), 0.6)
        put(y, 0.003, norm(self.sparks(0.5, r, 1200 * big, 0.12)), 0.35 * big)
        return y

    def elem(self, i, r, k=1):
        return self.strike(i + k, r, 0.5 + 0.1 * k)[:N(0.4)]

    def killfx(self, r):
        out = buf(1.1)
        put(out, 0, self.strike(1, r, 1.3), 0.9)
        put(out, 0.02, stereo_pair(lambda c: self.sparks(1.0, R("fk", c), 1500, 0.25)), 0.5)
        return finish(tail_mix(out, 0.8, 5000, 0.25), 1.1, 0.2)

    def quench(self, d, r):
        def ch(c):
            rr = R("quench", c, d)
            hiss = hp(rr.standard_normal(N(d)), 2500) * env(N(d), 0.02, d * 0.35)
            bub = fit(lp(S(WS + f"splash_0{3 + c}.ogg", r=1.3), 6000), N(d)) * 0.4
            return norm(hiss) * 0.7 + bub
        return stereo_pair(ch)

    def equip(self, r):
        out = buf(0.8)
        put(out, 0, st(whoosh(0.5, 300, 1200, r, q=0.8)), 0.4)
        put(out, 0.32, self.strike(2, r, 0.6), 0.6)
        return finish(tail_mix(out, 0.6, 5000, 0.25), 0.8)

    def inspect(self, r):
        out = buf(2.2)
        put(out, 0, stereo_pair(lambda c: self.sparks(2.0, R("fi", c), 220, 1.2)), 0.5)
        put(out, 0.1, st(bpf(np.cumsum(r.standard_normal(N(2.0))), 200, 800) * env(N(2.0), 0.6, 0.6)), 0.25)
        for t, v in ((0.4, 1), (0.75, 3), (1.35, 0)):
            put(out, t, self.strike(v, r, 0.5), 0.45)
        return finish(tail_mix(out, 0.8, 5000, 0.25), 2.2, 0.35)

    def fin(self, r):
        out = buf(2.6)
        for i, t in enumerate((0.0, 0.32, 0.56)):
            put(out, t, self.strike(i, r, 0.8 + 0.3 * i), 0.7 + 0.1 * i)
        put(out, 0.58, stereo_pair(lambda c: self.sparks(1.4, R("ff", c), 2500, 0.35)), 0.55)
        put(out, 0.95, self.quench(1.5, r), 0.45)
        return finish(tail_mix(out, 1.2, 5000, 0.25), 2.6, 0.4)

    def rs_a(self, r):
        out = buf(0.9)
        put(out, 0, st(whoosh(0.7, 250, 900, r, q=0.8)), 0.45)
        put(out, 0.05, self.strike(4, r, 0.4)[:N(0.6)], 0.4)
        return finish(tail_mix(out, 0.6, 5000, 0.2), 0.9, 0.2)

    def rs_b(self, r):
        out = buf(0.9)
        put(out, RS_B_HIT - 0.12, self.strike(3, r, 0.3)[:N(0.1)], 0.3)
        put(out, RS_B_HIT, self.strike(1, r, 0.8), 0.75)
        put(out, RS_B_HIT + 0.08, self.quench(0.5, r), 0.25)
        return finish(tail_mix(out, 0.6, 5000, 0.2), 0.9, 0.2)

    def counter(self, r):
        return finish(self.anvil(2, 0.9, 0.03)[:N(0.1)], 0.12) * 0.35


class Aurora(Line):
    id = "aurora"; damp = 11000; rt = 0.85; drive = 1.2; wet = 1.35
    head_partials = [(1.0, 0.11, 1.0), (2.24, 0.04, 0.2), (3.41, 0.025, 0.1), (0.62, 0.04, 0.16)]
    head_twin = 0.0014

    def color(self, m):
        return hsh(peq(m, 500, -2, 1.0), 6000, 3)

    def mat(self, v):
        return S(KU + "glass_004.ogg", dur=0.04, r=0.8 + 0.03 * v)

    def tinkles(self, d, r, n, ratio=1.7, hpf=3000):
        y = np.zeros(N(d))
        for _ in range(n):
            g = S(BF + f"bfh1_glass_falling_0{1 + int(r.integers(4))}.ogg", start=float(r.uniform(0.0, 0.2)), dur=0.05)
            put(y, float(r.uniform(0, d * 0.7)), hp(pitch(g, ratio * r.uniform(0.9, 1.1)), hpf), r.uniform(0.3, 1.0))
        return y

    def sweet(self, cls, v, r):
        y = fit(self.tinkles(0.06, r, 5, 1.6), N(0.1))
        return y + 0.6 * fit(hp(S(KU + "glass_005.ogg", r=1.4), 2000), N(0.1))

    def shimmer(self, d, r, n=6):
        out = buf(d)
        for i in range(n):
            f = r.uniform(3200, 8000)
            beat = r.uniform(3, 7)
            t = T(d)
            y = (np.sin(2 * np.pi * f * t) + np.sin(2 * np.pi * (f + beat) * t)) * env(N(d), 0.015, d * 0.4)
            c = i % 2
            out[c] += y * 0.5
            out[1 - c] += y * 0.2
        return out / n

    def texture(self, cls, v, r, d):
        out = self.shimmer(d, R(self.id, cls, v), 6) * 0.5
        out += stereo_pair(lambda c: self.tinkles(d, R(self.id, cls, v, c), 6, 1.8) * env(N(d), 0.01, d * 0.35)) * 0.3
        return out * 0.6

    def body_src(self, v):
        return fit(S(KI + f"impactGlass_medium_00{v}.ogg", r=0.58), N(0.09)) + 0.5 * fit(S(KI + f"impactSoft_medium_00{v}.ogg"), N(0.09))

    def head_ping(self, v):
        return S(KU + ["glass_005", "glass_006", "glass_002", "glass_001", "glass_005"][v] + ".ogg")

    def elem(self, i, r, k=1):
        out = buf(0.35)
        put(out, 0, self.tinkles(0.1, r, 3 + k, 1.4 + 0.05 * k), 0.8)
        put(out, 0.005, hp(S(KU + ["glass_005.ogg", "glass_004.ogg", "glass_006.ogg"][i % 3], r=1.2 + 0.04 * k), 1500), 0.5)
        return out

    def killfx(self, r):
        out = buf(1.2)
        put(out, 0, stereo_pair(lambda c: self.tinkles(0.5, R("ak", c), 14, 1.6)), 0.7)
        put(out, 0.03, self.shimmer(1.1, r, 8), 0.8)
        put(out, 0.0, st(whoosh(0.9, 1500, 5000, r, q=1.0, atk=0.15)), 0.25)
        return finish(tail_mix(out, 1.2, 11000, 0.35), 1.2, 0.3)

    def equip(self, r):
        out = buf(0.8)
        put(out, 0, st(whoosh(0.7, 900, 4000, r, atk=0.45)), 0.35)
        put(out, 0.3, self.shimmer(0.5, r, 5), 0.6)
        put(out, 0.3, self.elem(0, r, 1), 0.4)
        return finish(tail_mix(out, 0.9, 11000, 0.3), 0.8, 0.2)

    def inspect(self, r):
        out = buf(2.4)
        put(out, 0, stereo_pair(lambda c: whoosh(2.2, 700, 3000, R("ai", c), atk=0.45)), 0.3)
        put(out, 0.4, self.shimmer(1.8, r, 8), 0.7)
        put(out, 0.5, stereo_pair(lambda c: self.tinkles(1.5, R("ai2", c), 10, 1.7)), 0.3)
        return finish(tail_mix(out, 1.2, 11000, 0.3), 2.4, 0.4)

    def fin(self, r):
        out = buf(2.8)
        put(out, 0, stereo_pair(lambda c: whoosh(0.9, 400, 3500, R("af", c), q=1.0, atk=0.85)), 0.5)
        casc = buf(1.6)
        for i in range(26):
            t = 0.6 * (1 - (1 - i / 26) ** 1.6)
            put(casc, t, self.tinkles(0.08, R("afc", i), 2, 1.5 + 0.02 * i), 0.5 + 0.02 * i)
        put(out, 0.75, casc, 0.9)
        put(out, 1.3, self.shimmer(1.5, r, 10), 0.9)
        return finish(tail_mix(out, 1.5, 11000, 0.35), 2.8, 0.5)

    def rs_a(self, r):
        out = buf(0.9)
        put(out, 0, st(whoosh(0.8, 2500, 800, r, q=1.0, atk=0.2)), 0.35)
        put(out, 0.02, self.elem(1, r, 1), 0.5)
        return finish(tail_mix(out, 0.8, 11000, 0.3), 0.9, 0.2)

    def rs_b(self, r):
        out = buf(0.9)
        put(out, RS_B_HIT - 0.25, st(whoosh(0.25, 900, 3500, r, atk=0.9)), 0.3)
        put(out, RS_B_HIT, self.elem(2, r, 3), 0.7)
        put(out, RS_B_HIT + 0.02, self.shimmer(0.5, r, 5), 0.5)
        return finish(tail_mix(out, 0.8, 11000, 0.3), 0.9, 0.2)

    def counter(self, r):
        return finish(hp(S(KU + "glass_006.ogg", r=1.5), 2000), 0.12) * 0.3


class Orrery(Line):
    id = "orrery"; damp = 7000; rt = 0.35; drive = 1.3
    head_partials = [(1.0, 0.075, 1.0), (0.5, 0.05, 0.3), (2.0, 0.025, 0.12), (1.5, 0.02, 0.08)]

    def ticks(self):
        return grains(CW + "tickloop-amp.wav", 12, 0.025, gap=0.08)

    def gears(self):
        return [S(CW + f"singlewind{i}.wav", thresh=-30) for i in (1, 2, 3)]

    def color(self, m):
        return lp(peq(peq(m, 1400, 2, 1.0), 3000, 2, 1.0), 10000, 2)

    def mat(self, v):
        return S(KI + f"impactWood_light_00{v}.ogg", dur=0.04)

    def brass(self, v, r=0.5, tau=0.05):
        x = S(KI + f"impactMetal_light_00{v % 5}.ogg", r=r)
        return x * env(len(x), 0.0002, tau)

    def sweet(self, cls, v, r):
        y = np.zeros(N(0.14))
        put(y, 0, hp(self.ticks()[v % len(self.ticks())], 1500), 1.0)            # the tick, locked to the shot
        put(y, 0.016, hp(self.gears()[v % 3][:N(0.03)], 800), 0.4)               # a gear tooth engaging
        put(y, 0.003, self.brass(v), 0.25)
        return y

    def texture(self, cls, v, r, d):
        out = buf(d)
        if not CLS[cls]["auto"]:
            put(out, 0.11, st(hp(self.ticks()[(v + 5) % len(self.ticks())], 1500)), 0.3)   # the escapement's tock
        return out

    def body_src(self, v):
        return S(KI + f"impactWood_medium_00{v}.ogg", r=0.9)

    def head_ping(self, v):
        return S(KI + f"impactMetal_light_00{(v + 3) % 5}.ogg")

    def head_click(self, v, r):
        return hp(self.ticks()[(v * 3) % len(self.ticks())][:N(0.006)], 2000)

    def elem(self, i, r, k=1):
        y = np.zeros(N(0.3))
        put(y, 0, hp(self.ticks()[(i * 5 + k) % len(self.ticks())], 1200), 0.9)
        if i % 2 == 1 or k >= 4:
            put(y, 0.012, hp(self.gears()[(i + k) % 3][:N(0.06)], 600), 0.45)
        put(y, 0.002, self.brass(i + k, 0.48 + 0.02 * k, 0.04), 0.2)
        return y

    def killfx(self, r):
        out = buf(0.9)
        put(out, 0, hp(self.gears()[0], 400), 0.7)
        put(out, 0.0, ratchet(6, 22, self.ticks(), r, accel=0.6), 0.6)
        put(out, 0.3, self.brass(1, 0.5, 0.15), 0.45)
        put(out, 0.3, lp(S(KI + "impactWood_heavy_001.ogg"), 3000), 0.5)
        return finish(tail_mix(out, 0.5, 7000, 0.2), 0.9)

    def equip(self, r):
        out = buf(0.7)
        put(out, 0, hp(self.gears()[1], 500), 0.5)
        put(out, 0.25, self.elem(1, r, 2), 0.6)
        return finish(tail_mix(out, 0.4, 7000, 0.2), 0.7)

    def inspect(self, r):
        out = buf(2.2)
        put(out, 0, hp(S(CW + "multiwind1.wav", thresh=-30), 400), 0.45)
        put(out, 0.9, ratchet(8, 4.5, self.ticks(), r, start_gain=0.6, end_gain=0.4), 0.6)
        put(out, 1.75, self.brass(2, 0.5, 0.12), 0.25)
        return finish(tail_mix(out, 0.5, 7000, 0.2), 2.2, 0.3)

    def fin(self, r):
        out = buf(2.6)
        put(out, 0, hp(S(CW + "multiwind2.wav", thresh=-30), 400), 0.55)
        put(out, 0.2, ratchet(16, 6, self.ticks(), r, accel=2.5, start_gain=0.4, end_gain=0.9), 0.7)
        put(out, 1.45, hp(self.gears()[2], 300), 0.7)
        put(out, 1.47, lp(S(KI + "impactWood_heavy_003.ogg"), 3000), 0.6)
        put(out, 1.5, self.brass(0, 0.5, 0.35), 0.5)
        put(out, 1.5, self.brass(3, 1.0, 0.25), 0.25)
        return finish(tail_mix(out, 0.9, 7000, 0.25), 2.6, 0.4)

    def rs_a(self, r):
        out = buf(0.8)
        put(out, 0, hp(self.gears()[0], 400), 0.55)
        put(out, 0.18, ratchet(4, 14, self.ticks(), r), 0.45)
        return finish(tail_mix(out, 0.4, 7000, 0.2), 0.8, 0.15)

    def rs_b(self, r):
        out = buf(0.8)
        put(out, RS_B_HIT - 0.2, ratchet(4, 20, self.ticks(), r, accel=0.5), 0.4)
        put(out, RS_B_HIT, hp(self.gears()[2][:N(0.08)], 400), 0.6)
        put(out, RS_B_HIT, self.elem(3, r, 5), 0.6)
        put(out, RS_B_HIT + 0.01, self.brass(4, 0.5, 0.12), 0.3)
        return finish(tail_mix(out, 0.5, 7000, 0.2), 0.8, 0.15)

    def counter(self, r):
        return finish(hp(self.ticks()[2], 1500), 0.08) * 0.35


DESIGNS = {c.id: c for c in (Glasswork(), Thunderhead(), Basalt(), Oscilloscope(), Rally(), Frostbite(), Hive(), Foundry(), Aurora(), Orrery())}

# ----------------------------------------------------------------------------------------------- render



def fire_window(sh: Shot):
    return sh.on / SR, 0.2


class FireFit:
    """K-weighted pieces of one factory shot's 200 ms window: core(m, c) = c * (A + m * B) is linear, so the loudness of
    core + g * skin is cheap to evaluate for any m, c, g."""

    def __init__(self, sh: Shot):
        self.sh = sh
        t0, d = fire_window(sh)
        self.a, self.b = N(t0), N(t0) + N(d)
        A, B = sh.core(0.0, 1.0, False), sh.core(1.0, 1.0, False) - sh.core(0.0, 1.0, False)
        self.kA, self.kB = self._w(A), self._w(B)
        self.Lf = loud(sh.x, t0, d)

    def _w(self, x):
        y = kw(st(x))
        return fit(y, self.b)[:, self.a:self.b]

    def kS(self, skin):
        return self._w(skin)

    def dev(self, kS, g=1.0, m=None, c=None):
        m = self.sh.m if m is None else m
        c = self.sh.c if c is None else c
        y = c * (self.kA + m * self.kB) + g * kS
        return -0.691 + 10 * np.log10(np.sum(np.mean(y ** 2, axis=-1)) + 1e-20) - self.Lf


CORE_TARGET_DB = -1.0   # the cores' mean gain per class after fitting (the factory transient stays near its level)


def fit_fire(fac: Factory, raw: dict):
    """raw[(line, cls, v)] = a skin fire before levelling. Per class: every skin is set to one standalone loudness
    (searched so the class's cores average CORE_TARGET_DB); each gun's core then keeps m of its own mids, or plays c lower
    (never under -4.5 dB), so its mean deviation over every line's skins is 0; then each skin's gain is nudged so its
    mean deviation over the class's guns is 0, and the cores are solved once more. Returns the levelled skins."""
    fits = {cls: [FireFit(sh) for sh in shots] for cls, shots in fac.shots.items()}
    cmin = 10 ** (-4.5 / 20)
    out = {}
    for cls, ff in fits.items():
        keys = [k for k in raw if k[1] == cls]
        if not keys:
            continue
        ks = {k: [f.kS(raw[k]) for f in ff] for k in keys}
        own = {k: loud(raw[k], OFF, 0.2) for k in keys}
        lmid = float(np.mean([loud(f.sh.mid, *fire_window(f.sh)) for f in ff]))
        gains = {}

        def solve_cores():
            for j, f in enumerate(ff):
                md = lambda m, c: float(np.mean([f.dev(ks[k][j], gains[k], m, c) for k in keys]))
                if md(0.0, 1.0) > 0:
                    lo_, hi_ = cmin, 1.0
                    for _ in range(22):
                        cc = (lo_ + hi_) / 2
                        lo_, hi_ = (lo_, cc) if md(0.0, cc) > 0 else (cc, hi_)
                    f.sh.m, f.sh.c = 0.0, lo_
                else:
                    lo_, hi_ = 0.0, 1.0
                    for _ in range(22):
                        mm = (lo_ + hi_) / 2
                        lo_, hi_ = (lo_, mm) if md(mm, 1.0) > 0 else (mm, hi_)
                    f.sh.m, f.sh.c = lo_, 1.0
            return float(np.mean([20 * np.log10(f.sh.c) for f in ff]))

        lo_, hi_ = -15.0, 6.0
        for _ in range(14):
            off = (lo_ + hi_) / 2
            for k in keys:
                gains[k] = 10 ** ((lmid + off - own[k]) / 20)
            cdb = solve_cores()
            lo_, hi_ = (lo_, off) if cdb < CORE_TARGET_DB else (off, hi_)
        for k in keys:
            gains[k] = 10 ** ((lmid + lo_ - own[k]) / 20)
        for _round in range(6):
            solve_cores()
            for k in keys:
                for _ in range(3):
                    dv = float(np.mean([f.dev(ks[k][j], gains[k]) for j, f in enumerate(ff)]))
                    gains[k] *= 10 ** (-dv / 20)
        solve_cores()
        for k in keys:  # last: centre each skin on its worst gun pair (min-max), not the mean, cores fixed
            for _ in range(3):
                dvs = [f.dev(ks[k][j], gains[k]) for j, f in enumerate(ff)]
                gains[k] *= 10 ** (-(max(dvs) + min(dvs)) / 2 / 20)
        out.update({k: raw[k] * gains[k] for k in keys})
    return out


def fire_dev(skin, sh: Shot, core):
    n = max(len(core), skin.shape[-1])
    tot = fit(st(core), n) + fit(skin, n)
    t0, d = fire_window(sh)
    return loud(tot, t0, d) - loud(sh.x, t0, d)


def hit_level(x):
    return loud(x, onset30(x) / SR, 0.1)


def clear_dir(p, remove=False):
    """Deletes the files in p (and p itself with remove=True; on some setups only the files can be deleted from here)."""
    if not os.path.isdir(p):
        return
    for dp, _dirs, files in os.walk(p, topdown=False):
        for f in files:
            os.remove(os.path.join(dp, f))
        if remove or dp != p:
            try:
                os.rmdir(dp)
            except OSError:
                print(f"note: could not remove the folder {dp}; its files are gone (delete it by hand)")


def write_wav(path, x):
    x = np.asarray(x)
    ch = 1 if x.ndim == 1 else x.shape[0]
    pcm = np.round(np.clip(x.T if ch > 1 else x, -1, 32767 / 32768) * 32768).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(ch); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def write_ogg(path, x):
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        write_wav(tmp, x)
        subprocess.run([SS._FF, "-v", "error", "-nostdin", "-y", "-i", tmp, "-c:a", "libvorbis", "-q:a", OGG_Q, path], check=True)
    finally:
        os.remove(tmp)


def decode(path):
    if path.endswith(".wav"):
        return read_wav(path)
    out = subprocess.run([SS._FF, "-v", "error", "-nostdin", "-i", path, "-f", "f32le", "-acodec", "pcm_f32le", "-ar", str(SR), "-"],
                         capture_output=True, check=True).stdout
    ch = SS._channels(path)
    x = np.frombuffer(out, dtype="<f4").astype(float)
    return x[:len(x) // ch * ch].reshape(-1, ch).T.copy()


def soft_limit(x, ceiling_db=-1.0):
    c = 10 ** (ceiling_db / 20)
    return c * np.tanh(x / c) if pk(x) > c * 0.7 else x


def match(x, target, measure, ceiling_db=-1.0):
    """Gain to `target` (dB by `measure`), soft-limiting peaks so a peaky sound can still reach a loud factory cue."""
    y = x
    g = 10 ** ((target - measure(x)) / 20)
    for _ in range(6):
        y = soft_limit(x * g, ceiling_db)
        g *= 10 ** ((target - measure(y)) / 20)
    return soft_limit(x * g, ceiling_db)


def tilt(x, g):
    return hsh(lsh(x, 400, -g), 2500, g)


def kill_targets(raw, step=1.05):
    """Centroid targets for kill1..5: as close to each kill's own as the tilt (+-12 dB, else +-18) can reach, every one a
    step brighter than the one before."""
    cen = [centroid(x, 0, 0.3) for x in raw]
    for g in (12.0, 18.0):
        lo_ = [centroid(tilt(x, -g), 0, 0.3) for x in raw]
        hi_ = [centroid(tilt(x, g), 0, 0.3) for x in raw]
        U = [0.0] * 5
        U[4] = hi_[4]
        for k in range(3, -1, -1):
            U[k] = min(hi_[k], U[k + 1] / step)
        t = []
        for k in range(5):
            want = cen[k] if k == 0 else max(cen[k], t[-1] * step)
            t.append(float(np.clip(want, lo_[k], U[k])))
        if all(b >= a * step * 0.999 for a, b in zip(t, t[1:])):
            return t
    return t


def tilt_to(x, cen_target):
    """Spectral tilt (shelves at 400 Hz / 2.5 kHz, opposite gains) until the centroid of the first 300 ms is cen_target."""
    lo_, hi_ = -18.0, 18.0
    y = x
    for _ in range(20):
        g = (lo_ + hi_) / 2
        y = tilt(x, g)
        if centroid(y, 0, 0.3) < cen_target:
            lo_ = g
        else:
            hi_ = g
    return y


def spray_gate(x):
    """Auto classes: the skin's fire must be 20 dB down by 110 ms after the onset (a spray's shots stay separate).
    Flat to 60 ms, then -24 dB per 45 ms (floor -60 dB): applied to the raw fire, before the loudness levelling."""
    t = np.arange(x.shape[-1]) / SR
    db = np.clip(-24.0 * np.clip((t - OFF - 0.06) / 0.045, 0, None), -60.0, 0.0)
    return x * 10 ** (db / 20)


def limit(x, ceiling_db=-0.8):
    c = 10 ** (ceiling_db / 20)
    p = pk(x)
    return x * (c / p) if p > c else x


def render_line(L: Line, fac: Factory, fires, out_dir, hit_target):
    files: dict[str, np.ndarray] = {}
    for cls in CLASSES:
        for v in range(VARIANTS):
            files[f"fire_{cls}_{v + 1}"] = fires[(L.id, cls, v)]
    for key in RELOADS:
        x = L.reload_layer(key, fac)
        files[f"reload_{key}"] = x * 10 ** ((mom_max(fac.reloads[key]) - 9 - mom_max(x)) / 20)
    r = R(L.id, "misc")
    vandal = fac.shots["rifle"][[s.gun for s in fac.shots["rifle"]].index("vandal")].x
    ref_head, ref_k5 = mom_max(fac.head), mom_max(fac.kills[4])
    files["equip"] = (x := L.equip(r)) * 10 ** ((mom_max(vandal) - 12 - mom_max(x)) / 20)
    files["inspect"] = (x := L.inspect(r)) * 10 ** ((ref_head - 10 - mom_max(x)) / 20)
    files["kill"] = (x := L.killfx(r)) * 10 ** ((ref_k5 - 5 - mom_max(x)) / 20)
    raw_k = [L.kill_n(k, fac) for k in range(1, 6)]
    targets = kill_targets(raw_k)
    for k in range(1, 6):
        x, want = raw_k[k - 1], targets[k - 1]   # escalation: each kill a step brighter (and denser), never a melody
        y = match(tilt_to(x, want), loud(fac.kills[k - 1]), loud)
        for _ in range(5):
            y = match(tilt_to(x, want * want / centroid(y, 0, 0.3)), loud(fac.kills[k - 1]), loud)
        if k > 1:
            prev = centroid(files[f"kill{k - 1}"], 0, 0.3)
            for _ in range(8):  # escalation must rise: brighten until it sits >= 4 % above the previous kill
                if centroid(y, 0, 0.3) >= prev * 1.04:
                    break
                y = match(hsh(y, 4000.0, 2.0), loud(fac.kills[k - 1]), loud)
        for _ in range(4):  # keep the factory loudness with peak room for the encoder: soften the transient, not the level
            if pk(y) <= 10 ** (-2.5 / 20):
                break
            y = match(sat(y, 1.8), loud(fac.kills[k - 1]), loud)
        files[f"kill{k}"] = y
    for v in range(VARIANTS):
        rr = R(L.id, "hitgain", v)
        for nm, fn in (("hit_body", L.hit_body), ("hit_head", L.hit_head)):
            x = fn(v)
            ref = fac.body if nm == "hit_body" else fac.head
            if nm == "hit_body":
                x = lp(x, 1500.0, 8)  # a body hit is dull: nothing tonal above 2 kHz (the headshot's ding lives there)
            late = onset30(x) - onset30(ref)  # land with the factory hit (onset within 2 ms)
            if late > N(0.001):
                x = x[..., late:]
            want_l = hit_target + rr.uniform(-0.25, 0.25)
            for _ in range(6):  # level the WRITTEN signal (mono, limited); a transient too spiky to get there is softened
                short = want_l - hit_level(limit(mono(x)))
                if short > 0.3 and pk(x) * 10 ** (short / 20) > 10 ** (-0.8 / 20):
                    x = sat(x, 1.0 + 0.4 * short)
                x = x * 10 ** ((want_l - hit_level(limit(mono(x)))) / 20)
            files[f"{nm}_{v + 1}"] = x
    if L.id in APEX:
        rel = fac.reloads["rifle"]
        files["reload_special_a"] = (x := L.rs_a(r)) * 10 ** ((mom_max(rel) - 6 - mom_max(x)) / 20)
        files["reload_special_b"] = (x := L.rs_b(r)) * 10 ** ((mom_max(rel) - 6 - mom_max(x)) / 20)
        files["fin"] = (x := L.fin(r)) * 10 ** ((ref_k5 - 3.5 - mom_max(x)) / 20)
        files["counter"] = (x := L.counter(r)) * 10 ** ((ref_k5 - 20 - mom_max(x)) / 20)
    clear_dir(out_dir)
    os.makedirs(out_dir, exist_ok=True)
    for name, x in files.items():
        if name.startswith("hit_"):
            write_wav(os.path.join(out_dir, name + ".wav"), limit(mono(x)))
        else:
            write_ogg(os.path.join(out_dir, name + ".ogg"), limit(x, -1.6))  # Vorbis overshoots transients


def render_cores(fac: Factory, core_dir):
    clear_dir(core_dir)
    os.makedirs(core_dir, exist_ok=True)
    for sh in fac.all_shots():
        write_wav(os.path.join(core_dir, sh.name), sh.core())


# ----------------------------------------------------------------------------------------------- checks

class Checker:
    def __init__(self):
        self.fails: list[str] = []
        self.rows: list[str] = []

    def req(self, ok, what):
        if not ok:
            self.fails.append(what)
        return ok


def load_set(d):
    out = {}
    for f in sorted(os.listdir(d)):
        if f.endswith((".wav", ".ogg")):
            out[os.path.splitext(f)[0]] = decode(os.path.join(d, f))
    return out


def check_all(fac: Factory, skins_dir, lines, hit_target):
    C = Checker()
    hashes = {}
    core_dir = os.path.join(skins_dir, "_core")
    cores = {}
    for sh in fac.all_shots():
        p = os.path.join(core_dir, sh.name)
        if C.req(os.path.isfile(p), f"_core/{sh.name} missing"):
            c = cores[sh.name] = mono(read_wav(p))
            c = c / (np.dot(c[sh.on:sh.on + N(0.008)], sh.x[sh.on:sh.on + N(0.008)]) / (np.dot(c[sh.on:sh.on + N(0.008)], c[sh.on:sh.on + N(0.008)]) + 1e-12) or 1)
            a = sh.on
            C.req(np.corrcoef(c[a:a + N(0.008)], sh.x[a:a + N(0.008)])[0, 1] > 0.99, f"_core/{sh.name}: transient changed")
    fon_body, fon_head = onset30(fac.body) / SR, onset30(fac.head) / SR
    prints = {}
    print(f"\n{'line':13s} {'fire dLU min..max':>17s} {'auto -20dB':>10s} | {'body LK':>7s} {'on':>5s} {'cen':>5s} {'t30':>5s} {'len':>5s} {'<1k5':>5s} "
          f"{'ton2k':>5s} | {'head LK':>7s} {'on':>5s} {'cen':>5s} {'t30':>5s} {'domHz':>6s} | {'kills dLK':>9s} {'size':>7s}")
    for lid in lines:
        d = os.path.join(skins_dir, lid)
        if not C.req(os.path.isdir(d), f"{lid}: no folder"):
            continue
        fs = load_set(d)
        need = [f"fire_{c}_{v + 1}" for c in CLASSES for v in range(VARIANTS)] + [f"reload_{k}" for k in RELOADS] + \
            ["equip", "inspect", "kill"] + [f"kill{k}" for k in range(1, 6)] + [f"hit_{h}_{v + 1}" for h in ("body", "head") for v in range(VARIANTS)]
        if lid in APEX:
            need += ["reload_special_a", "reload_special_b", "fin", "counter"]
        miss = [n for n in need if n not in fs]
        C.req(not miss, f"{lid}: missing {miss}")
        for n, x in fs.items():
            h = hashlib.sha1(np.round(x * 3000).astype(np.int32).tobytes()).hexdigest()
            C.req(h not in hashes, f"{lid}/{n}: same audio as {hashes.get(h)}")
            hashes[h] = f"{lid}/{n}"
            C.req(pk(x) < 0.995, f"{lid}/{n}: clips ({pk(x):.3f})")
        # fire
        devs, autos = [], []
        for cls, P in CLASSES.items():
            for v in range(VARIANTS):
                x = fs.get(f"fire_{cls}_{v + 1}")
                if x is None:
                    continue
                for sh in fac.shots[cls]:
                    dv = fire_dev(x, sh, cores[sh.name])
                    devs.append(dv)
                    C.req(abs(dv) <= 1.0, f"{lid}/fire_{cls}_{v + 1} with {sh.name}: {dv:+.2f} LU vs factory")
                C.req(onset30(x) / SR >= OFF - 0.0015, f"{lid}/fire_{cls}_{v + 1}: lands before the factory onset")
                if CLS[cls]["auto"]:
                    e, hop = env_db(x[:, :N(OFF + CLS[cls]["dry"] * 3)], 0.005)
                    e2, _ = env_db(x, 0.005)
                    i = N(0.11 + OFF) // hop
                    drop = e2.max() - (e2[i] if i < len(e2) else -120)
                    autos.append(drop)
                    C.req(drop >= 20, f"{lid}/fire_{cls}_{v + 1}: only {drop:.1f} dB down at 110 ms (spray timing)")
        # hits
        B = [fs[f"hit_body_{v + 1}"] for v in range(VARIANTS) if f"hit_body_{v + 1}" in fs]
        H = [fs[f"hit_head_{v + 1}"] for v in range(VARIANTS) if f"hit_head_{v + 1}" in fs]
        bstat = dict(lk=[], on=[], cen=[], t30=[], ln=[], fb=[], ton=[])
        hstat = dict(lk=[], on=[], cen=[], t30=[], dom=[])
        for i, x in enumerate(B):
            m = mono(x)
            bstat["lk"].append(hit_level(m)); bstat["on"].append(onset30(m) / SR)
            bstat["cen"].append(centroid(m, onset30(m) / SR, 0.08)); bstat["t30"].append(t_down(m, 30))
            bstat["ln"].append(t_last(m, 40)); bstat["fb"].append(frac_below(m, 1500)); bstat["ton"].append(tonal_above(m, 2000))
            C.req(bstat["ln"][-1] <= 0.080, f"{lid}/hit_body_{i + 1}: {bstat['ln'][-1] * 1000:.0f} ms long (> 80)")
            C.req(bstat["fb"][-1] >= 0.85, f"{lid}/hit_body_{i + 1}: only {bstat['fb'][-1] * 100:.0f} % below 1.5 kHz")
            C.req(bstat["ton"][-1] < 12, f"{lid}/hit_body_{i + 1}: tonal partial above 2 kHz ({bstat['ton'][-1]:.1f} dB)")
            C.req(abs(bstat["on"][-1] - fon_body) <= 0.002, f"{lid}/hit_body_{i + 1}: onset {bstat['on'][-1] * 1000:.1f} ms vs factory {fon_body * 1000:.1f}")
        for i, x in enumerate(H):
            m = mono(x)
            hstat["lk"].append(hit_level(m)); hstat["on"].append(onset30(m) / SR)
            hstat["cen"].append(centroid(m, onset30(m) / SR, 0.1)); hstat["t30"].append(t_down(m, 30))
            hstat["dom"].append(dominant(m, 100, 12000, 0.3))
            f = hstat["dom"][-1]
            C.req(2000 <= f <= 4000, f"{lid}/hit_head_{i + 1}: dominant {f:.0f} Hz not in 2-4 kHz")
            C.req(abs(1200 * np.log2(f / HEAD_HZ)) <= 50, f"{lid}/hit_head_{i + 1}: dominant {f:.0f} Hz off the F# pitch class")
            C.req(hstat["t30"][-1] >= 0.2, f"{lid}/hit_head_{i + 1}: rings {hstat['t30'][-1] * 1000:.0f} ms (< 200)")
            C.req(abs(hstat["on"][-1] - fon_head) <= 0.002, f"{lid}/hit_head_{i + 1}: onset {hstat['on'][-1] * 1000:.1f} ms vs factory {fon_head * 1000:.1f}")
        if B and H:
            for nm, lk in [("body", bstat["lk"]), ("head", hstat["lk"])]:
                for i, l_ in enumerate(lk):
                    C.req(abs(l_ - hit_target) <= 1.0, f"{lid}/hit_{nm}_{i + 1}: {l_ - hit_target:+.2f} dB vs the hit target")
            C.req(np.mean(hstat["cen"]) >= 2 * np.mean(bstat["cen"]), f"{lid}: head centroid < 2x body")
            C.req(min(hstat["t30"]) >= 3 * max(bstat["t30"]), f"{lid}: head decay < 3x body")
            C.req(abs(np.mean(hstat["lk"]) - np.mean(bstat["lk"])) <= 1.0, f"{lid}: body and head differ > 1 dB")
        # kills
        kd, cens = [], []
        for k in range(1, 6):
            x = fs.get(f"kill{k}")
            if x is None:
                continue
            ref = fac.kills[k - 1]
            C.req(abs(x.shape[-1] / len(ref) - 1) <= 0.1, f"{lid}/kill{k}: length {x.shape[-1] / SR:.2f} s vs {len(ref) / SR:.2f}")
            dk = loud(x) - loud(ref)
            kd.append(dk)
            C.req(abs(dk) <= 1.0, f"{lid}/kill{k}: {dk:+.2f} dB vs factory kill{k}")
            cens.append(centroid(x, 0, 0.3))
        C.req(all(b > a for a, b in zip(cens, cens[1:])), f"{lid}: kill1..5 centroid doesn't rise ({', '.join(f'{c:.0f}' for c in cens)})")
        # cosmetic levels
        vandal = [s.x for s in fac.shots["rifle"] if s.gun == "vandal"][0]
        rules = [(f"reload_{k}", mom_max(fac.reloads[k]) - 6) for k in RELOADS] + [
            ("equip", mom_max(vandal) - 10), ("inspect", mom_max(fac.head) - 8), ("kill", mom_max(fac.kills[4]) - 4)]
        if lid in APEX:
            rules += [("reload_special_a", mom_max(fac.reloads["rifle"]) - 4), ("reload_special_b", mom_max(fac.reloads["rifle"]) - 4),
                      ("fin", mom_max(fac.kills[4]) - 2), ("counter", mom_max(fac.kills[4]) - 18)]
        for n, cap in rules:
            if n in fs:
                C.req(mom_max(fs[n]) <= cap + 0.05, f"{lid}/{n}: {mom_max(fs[n]):.1f} LUFS momentary > cap {cap:.1f}")
        for k in RELOADS:
            if f"reload_{k}" in fs:
                C.req(abs(fs[f"reload_{k}"].shape[-1] - len(fac.reloads[k])) <= N(0.01), f"{lid}/reload_{k}: not the factory length")
        C.req(fs.get("equip", np.zeros((2, 1))).shape[-1] <= N(1.0), f"{lid}/equip too long")
        C.req(fs.get("inspect", np.zeros((2, 1))).shape[-1] <= N(2.5), f"{lid}/inspect too long")
        size = sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d) if f.endswith((".wav", ".ogg")))
        print(f"{lid:13s} {min(devs):+6.2f}..{max(devs):+5.2f} LU {min(autos):8.1f}dB | {np.mean(bstat['lk']):7.1f} {np.mean(bstat['on']) * 1000:4.1f}m "
              f"{np.mean(bstat['cen']):5.0f} {max(bstat['t30']) * 1000:4.0f}m {max(bstat['ln']) * 1000:4.0f}m {min(bstat['fb']) * 100:4.0f}% {max(bstat['ton']):5.1f} | "
              f"{np.mean(hstat['lk']):7.1f} {np.mean(hstat['on']) * 1000:4.1f}m {np.mean(hstat['cen']):5.0f} {min(hstat['t30']) * 1000:4.0f}m {np.mean(hstat['dom']):6.0f} | "
              f"{min(kd):+4.1f}..{max(kd):+4.1f} {size / 1e6:6.2f}MB")
        prints[lid] = {"fire": fingerprint(fs["fire_rifle_1"]), "body": fingerprint(fs["hit_body_1"], 0.08), "head": fingerprint(fs["hit_head_1"]),
                       "fire_s": fingerprint(fs["fire_sidearm_1"])}
    fb, fh = mono(fac.body), mono(fac.head)
    print(f"{'factory':13s} {'':>17s} {'':>10s} | {hit_level(fb):7.1f} {fon_body * 1000:4.1f}m {centroid(fb, fon_body, 0.08):5.0f} {t_down(fb, 30) * 1000:4.0f}m "
          f"{t_last(fb, 40) * 1000:4.0f}m {frac_below(fb, 1500) * 100:4.0f}% {tonal_above(fb, 2000):5.1f} | {hit_level(fh):7.1f} {fon_head * 1000:4.1f}m "
          f"{centroid(fh, fon_head, 0.1):5.0f} {t_down(fh, 30) * 1000:4.0f}m {dominant(fh, 100, 12000, 0.3):6.0f} |  (hit target {hit_target:.1f})")
    # distinctness: every pair of lines, and shots vs hits within a line
    ids = list(prints)
    if len(ids) > 1:
        print("\ndistinctness (fingerprint distance, dB RMS; lines must differ by >= 2.0, a shot from its hits by >= 4.0):")
        for kind in ("fire", "fire_s", "head", "body"):
            worst = (99.0, "")
            for i, a in enumerate(ids):
                for b in ids[i + 1:]:
                    dd = float(np.sqrt(np.mean((prints[a][kind] - prints[b][kind]) ** 2)))
                    worst = min(worst, (dd, f"{a}~{b}"))
            print(f"  {kind:6s} closest pair {worst[1]:26s} {worst[0]:5.2f}")
            C.req(worst[0] >= 2.0, f"{kind}: lines {worst[1]} too alike ({worst[0]:.2f})")
    for lid in ids:
        for h in ("head", "body"):
            fpf, fph = prints[lid]["fire"], prints[lid][h]
            dd = float(np.sqrt(np.mean((fpf - fph) ** 2)))
            C.req(dd >= 4.0, f"{lid}: fire and hit_{h} too alike ({dd:.2f})")
    return C


# ----------------------------------------------------------------------------------------------- preview

def preview(fac: Factory, skins_dir, lines, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    core_dir = os.path.join(skins_dir, "_core")
    cores = {sh.name: mono(read_wav(os.path.join(core_dir, sh.name))) for sh in fac.all_shots()}

    def gun(cls, name):
        return [s for s in fac.shots[cls] if s.gun == name]

    cues_all = []
    for lid in lines:
        fs = load_set(os.path.join(skins_dir, lid))
        out = buf(40.0)
        cues = []
        t = 0.4
        rr = R("preview", lid)
        last = {}

        def var(base):
            n = sum(1 for k in fs if k.startswith(base + "_") and k[len(base) + 1:].isdigit())
            i = int(rr.integers(n))
            if n > 1 and last.get(base) == i:
                i = (i + 1 + int(rr.integers(n - 1))) % n
            last[base] = i
            return fs[f"{base}_{i + 1}"]

        def burst(cls, g, shots, rate, skin):
            nonlocal t
            cues.append(f"{t:6.2f}s  {'SKIN' if skin else 'factory'} {g} x{shots}")
            for i in range(shots):
                sh = gun(cls, g)[i % len(gun(cls, g))]
                p = 1 + rr.uniform(-0.025, 0.025)
                if skin:
                    put(out, t, pitch(st(cores[sh.name]), p), 0.55)
                    put(out, t, pitch(var(f"fire_{cls}"), p), 0.55)
                else:
                    put(out, t, pitch(st(sh.x), p), 0.55)
                t += 1 / rate
            t += 0.7

        burst("rifle", "vandal", 6, 9.75, False); burst("rifle", "vandal", 6, 9.75, True)
        burst("sidearm", "classic", 3, 3.0, False); burst("sidearm", "classic", 3, 3.0, True)
        burst("sniper", "operator", 1, 1.0, False); burst("sniper", "operator", 1, 1.0, True)
        cues.append(f"{t:6.2f}s  reload (factory + skin layer)")
        put(out, t, st(fac.reloads["rifle"]), 0.6); put(out, t, fs["reload_rifle"], 0.6 * 0.8); t += len(fac.reloads["rifle"]) / SR + 0.4
        cues.append(f"{t:6.2f}s  equip"); put(out, t, fs["equip"], 0.55 * 0.8); t += fs["equip"].shape[-1] / SR + 0.4
        cues.append(f"{t:6.2f}s  inspect"); put(out, t, fs["inspect"], 0.8); t += fs["inspect"].shape[-1] / SR + 0.4
        cues.append(f"{t:6.2f}s  factory body x2, head x1")
        for i in range(2):
            put(out, t, st(fac.body), 0.8); t += 0.25
        put(out, t, st(fac.head), 1.0); t += 0.8
        cues.append(f"{t:6.2f}s  SKIN body x3, head x3")
        for i in range(3):
            put(out, t, st(var("hit_body")), 0.8); t += 0.25
        t += 0.2
        for i in range(3):
            put(out, t, st(var("hit_head")), 1.0); t += 0.5
        t += 0.3
        cues.append(f"{t:6.2f}s  kill1..kill5")
        for k in range(1, 6):
            put(out, t, fs[f"kill{k}"], 0.9); t += 0.55
        t += 0.4
        cues.append(f"{t:6.2f}s  kill effect"); put(out, t, fs["kill"], 0.9 * 0.8); t += fs["kill"].shape[-1] / SR + 0.4
        if lid in APEX:
            dur = 2.5
            cues.append(f"{t:6.2f}s  special reload ({dur} s, factory + special)")
            put(out, t, st(fac.reloads["rifle"]), 0.6)
            put(out, t, fs["reload_special_a"], 0.6 * 0.8)
            put(out, t + max(0.35, dur - RS_B_HIT), fs["reload_special_b"], 0.6 * 0.8)
            t += dur + 0.7
            cues.append(f"{t:6.2f}s  finisher"); put(out, t, fs["fin"], 0.9 * 0.8); t += fs["fin"].shape[-1] / SR + 0.3
        out = out[:, :N(t + 0.3)]
        out = out * (10 ** (-1 / 20) / pk(out))
        write_wav(os.path.join(out_dir, lid + ".wav"), out)
        cues_all.append(f"== {lid}.wav ({t + 0.3:.1f} s)\n" + "\n".join(cues))
    # all hits
    out = buf(60.0)
    t = 0.4
    cues = [f"{t:6.2f}s  factory body, body, head, head"]
    for x in (fac.body, fac.body, fac.head, fac.head):
        put(out, t, st(x), 1.0); t += 0.3 if len(x) < N(0.2) else 0.55
    t += 0.5
    for lid in lines:
        fs = load_set(os.path.join(skins_dir, lid))
        cues.append(f"{t:6.2f}s  {lid}: body, body, head, head")
        for nm in ("hit_body_1", "hit_body_2", "hit_head_1", "hit_head_2"):
            put(out, t, st(fs[nm]), 1.0); t += 0.3 if "body" in nm else 0.55
        t += 0.5
    out = out[:, :N(t)]
    out = out * (10 ** (-1 / 20) / pk(out))
    write_wav(os.path.join(out_dir, "_all-hits.wav"), out)
    cues_all.append(f"== _all-hits.wav ({t:.1f} s)\n" + "\n".join(cues))
    with open(os.path.join(out_dir, "_cues.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("Skin sound previews (skins 3). Times are where each part starts.\n\n" + "\n\n".join(cues_all) + "\n")


# ----------------------------------------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--project", default=os.path.normpath(os.path.join(HERE, "..", "..", "ValTrainerGodot")))
    ap.add_argument("--sources", required=True)
    ap.add_argument("--ffmpeg")
    ap.add_argument("--cache")
    ap.add_argument("--only")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--preview")
    a = ap.parse_args()
    if not os.path.isfile(os.path.join(a.project, "project.godot")):
        a.project = os.path.normpath(os.path.join(HERE, "..", ".."))
    if not os.path.isfile(os.path.join(a.project, "project.godot")):
        raise SystemExit("no project.godot: pass --project")
    SS.setup(a.sources, a.ffmpeg, a.cache)
    lines = a.only.split(",") if a.only else LINES
    skins = os.path.join(a.project, "assets", "audio", "skins")
    fac = Factory(a.project)
    hit_target = (hit_level(fac.body) + hit_level(fac.head)) / 2
    if not a.check:
        for old in OLD_SETS:
            p = os.path.join(skins, old)
            if os.path.isdir(p):
                clear_dir(p, remove=True)
                print(f"deleted the skins-2 set {old}")
        if a.only:
            print("note: --only: the cores are fitted to the rendered lines only (render every line before shipping)")
        raw = {(lid, cls, v): DESIGNS[lid].fire(cls, v, fac) for lid in lines for cls in CLASSES for v in range(VARIANTS)}
        raw = {k: spray_gate(x) if CLS[k[1]]["auto"] else x for k, x in raw.items()}
        fires = fit_fire(fac, raw)
        render_cores(fac, os.path.join(skins, "_core"))
        print("cores (own mids kept m / gain dB): " + ", ".join(f"{s.name.replace('_shot', '').replace('.wav', '')} {s.m:.2f}/{20 * np.log10(s.c):+.1f}" for s in fac.all_shots()))
        for lid in lines:
            render_line(DESIGNS[lid], fac, fires, os.path.join(skins, lid), hit_target)
            print(f"rendered {lid}")
    C = check_all(fac, skins, lines, hit_target)
    total = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(skins) for f in fs if f.endswith((".wav", ".ogg")))
    print(f"\nall skin audio: {total / 1e6:.2f} MB")
    if USED_PACKS:
        print("source packs used: " + ", ".join(sorted(USED_PACKS)))
    if a.preview:
        preview(fac, skins, lines, a.preview)
        print(f"previews written to {a.preview}")
    if C.fails:
        print(f"\nFAIL: {len(C.fails)} rule(s) broken:")
        for f in C.fails[:80]:
            print("  " + f)
        return 1
    print("\nPASS: every rule holds")
    return 0


if __name__ == "__main__":
    sys.exit(main())
