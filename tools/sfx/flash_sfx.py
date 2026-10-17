#!/usr/bin/env python3
"""
ValTrainer flash sound generator.

Renders every agent-flash sound (throw / flight / windup / pop per agent, Gekko's Dizzy set, and the universal
"flashed" ring) as 44.1 kHz 16-bit mono WAVs by layered synthesis: filtered noise, FM, pitch sweeps, granular
crackle, modal resonators, saturation and a small Schroeder reverb.

100 % our own work: no samples, no recordings, no third-party or Riot audio, no voices. The character of each
sound follows the *descriptions* in the research notes (fire / rock / robot / hawk / glass / metal / goo).
Output is deterministic (fixed seeds), so re-running reproduces the same files bit for bit.

Usage:
    python flash_sfx.py                 # write WAVs to <ValTrainerGodot>/assets/audio/flash
    python flash_sfx.py --out DIR       # write somewhere else
    python flash_sfx.py --review DIR    # also write a listing + spectrogram sheets for checking by eye

Windup risers last EXACTLY the in-game windup and peak at their last few milliseconds, so they can be started
at (pop time - windup) and the peak lands on the pop.
"""
from __future__ import annotations

import argparse
import os
import sys
import wave

import numpy as np
from scipy import signal

SR = 44100

# ----------------------------------------------------------------------------------------------- basics

def T(dur: float) -> np.ndarray:
    return np.arange(int(round(dur * SR))) / SR


def R(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


def white(n: int, r: np.random.Generator) -> np.ndarray:
    return r.uniform(-1.0, 1.0, n)


def pink(n: int, r: np.random.Generator) -> np.ndarray:
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1.0, -2.494956002, 2.017265875, -0.522189400]
    y = signal.lfilter(b, a, r.standard_normal(n + 2000))[2000:]
    return y / (np.max(np.abs(y)) + 1e-9)


def arr(v, n: int) -> np.ndarray:
    return np.broadcast_to(np.asarray(v, dtype=float), (n,)).astype(float)


def phase(freq) -> np.ndarray:
    return 2 * np.pi * np.cumsum(freq) / SR


def sine(freq, n: int, ph0: float = 0.0) -> np.ndarray:
    return np.sin(phase(arr(freq, n)) + ph0)


def saw(freq, n: int, max_h: int = 80) -> np.ndarray:
    """Band-limited saw (additive), frequency may vary per sample."""
    f = arr(freq, n)
    ph = phase(f)
    out = np.zeros(n)
    for k in range(1, max_h + 1):
        mask = (k * f) < 0.45 * SR
        if not mask.any():
            break
        out += mask * np.sin(k * ph) / k
    return out * (2 / np.pi)


def square(freq, n: int, max_h: int = 40) -> np.ndarray:
    f = arr(freq, n)
    ph = phase(f)
    out = np.zeros(n)
    for k in range(1, max_h + 1, 2):
        mask = (k * f) < 0.45 * SR
        if not mask.any():
            break
        out += mask * np.sin(k * ph) / k
    return out * (4 / np.pi)


def sweep(f0: float, f1: float, t: np.ndarray, dur: float, start: float = 0.0) -> np.ndarray:
    """Exponential glide f0 -> f1 over dur seconds starting at `start`."""
    k = np.clip((t - start) / dur, 0, 1)
    return f0 * (f1 / f0) ** k


def env(t: np.ndarray, atk: float, tau: float, start: float = 0.0) -> np.ndarray:
    """Linear attack then exponential decay; zero before start."""
    x = t - start
    out = np.where(x < atk, x / max(atk, 1e-9), np.exp(-np.maximum(x - atk, 0) / tau))
    return np.where(x < 0, 0.0, out)


def smooth(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def fades(x: np.ndarray, fin: float = 0.002, fout: float = 0.004) -> np.ndarray:
    x = x.copy()
    a, b = int(fin * SR), int(fout * SR)
    if a > 0:
        x[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    if b > 0:
        x[-b:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, b))
    return x


def sat(x: np.ndarray, drive: float = 1.5) -> np.ndarray:
    return np.tanh(drive * x) / np.tanh(drive)


def flicker(n: int, r: np.random.Generator, rate: float) -> np.ndarray:
    """Smooth random LFO in [-1, 1] with roughly `rate` Hz of movement."""
    y = lp(r.standard_normal(n + 4000), rate, order=2)[4000:]
    return y / (np.max(np.abs(y)) + 1e-9)


def norm(x: np.ndarray, peak_db: float) -> np.ndarray:
    return x * (10 ** (peak_db / 20) / (np.max(np.abs(x)) + 1e-12))


def place(out: np.ndarray, start: int, grain: np.ndarray) -> None:
    if start >= len(out):
        return
    m = min(len(grain), len(out) - start)
    out[start:start + m] += grain[:m]


# ----------------------------------------------------------------------------------------------- filters

def lp(x, fc, order=2):
    return signal.sosfilt(signal.butter(order, min(fc, 0.45 * SR), "low", fs=SR, output="sos"), x)


def hp(x, fc, order=2):
    return signal.sosfilt(signal.butter(order, fc, "high", fs=SR, output="sos"), x)


def bp(x, fc, q):
    """RBJ constant-peak band-pass, works for very high Q (resonators). Array fc -> time-varying SVF."""
    if np.ndim(fc) > 0:
        return svf(x, fc, q, "bp")
    w = 2 * np.pi * fc / SR
    al = np.sin(w) / (2 * q)
    b = np.array([al, 0, -al])
    a = np.array([1 + al, -2 * np.cos(w), 1 - al])
    return signal.lfilter(b / a[0], a / a[0], x)


def svf(x, fc, q, mode="bp"):
    """Time-varying TPT state-variable filter (fc may be an array). mode: lp / bp / hp."""
    n = len(x)
    g = np.tan(np.pi * np.clip(arr(fc, n), 10, 0.45 * SR) / SR)
    k = 1.0 / arr(q, n)
    a1 = 1 / (1 + g * (g + k))
    a2 = g * a1
    a3 = g * a2
    xs, a1l, a2l, a3l, kl = x.tolist(), a1.tolist(), a2.tolist(), a3.tolist(), k.tolist()
    out = [0.0] * n
    ic1 = ic2 = 0.0
    m = {"lp": 0, "bp": 1, "hp": 2}[mode]
    for i in range(n):
        v3 = xs[i] - ic2
        v1 = a1l[i] * ic1 + a2l[i] * v3
        v2 = ic2 + a2l[i] * ic1 + a3l[i] * v3
        ic1 = 2 * v1 - ic1
        ic2 = 2 * v2 - ic2
        out[i] = v2 if m == 0 else v1 if m == 1 else xs[i] - kl[i] * v1 - v2
    return np.array(out)


def resonators(x, freqs, q, gains=None):
    gains = gains or [1.0] * len(freqs)
    return sum(g * bp(x, f, q) for f, g in zip(freqs, gains))


def crush(x, hold: int, bits: int):
    y = np.repeat(x[::hold], hold)[:len(x)]
    lv = 2 ** (bits - 1)
    return np.round(y * lv) / lv


# ----------------------------------------------------------------------------------------------- texture

def impulses(t, density, r, power=2.5):
    """Poisson clicks with per-sample density (clicks/s) and heavy-tailed amplitudes (fire/rock crackle)."""
    n = len(t)
    hit = r.random(n) < arr(density, n) / SR
    amp = r.random(n) ** power * r.choice([-1.0, 1.0], n)
    return hit * amp


def grains_env(imp, tau):
    """Turn an impulse train into decaying amplitude grains (for rustle / granular noise)."""
    a = np.exp(-1 / (tau * SR))
    return signal.lfilter([1.0], [1.0, -a], np.abs(imp))


def bubble(f0, dur, rise=0.4, decay=0.03, atk=0.001):
    """Minnaert-style bubble: sine whose pitch rises (by `rise` per decay time, capped) while it decays."""
    t = T(dur)
    f = f0 * (1 + rise * np.minimum(t / decay, 2.5))
    return np.sin(phase(f)) * env(t, atk, decay)


def bubbles(t, density, r, fmin, fmax, rise=(0.2, 0.6), decay=(0.012, 0.04), amp_decay=None):
    out = np.zeros(len(t))
    hits = np.nonzero(r.random(len(t)) < arr(density, len(t)) / SR)[0]
    for i in hits:
        f0 = np.exp(r.uniform(np.log(fmin), np.log(fmax)))
        d = r.uniform(*decay)
        a = r.uniform(0.3, 1.0) * (amp_decay[i] if amp_decay is not None else 1)
        place(out, i, a * bubble(f0, d * 5, r.uniform(*rise), d))
    return out


def modal(t, freqs, decays, amps, start=0.0, atk=0.0005, r=None):
    out = np.zeros(len(t))
    for i, (f, d, a) in enumerate(zip(freqs, decays, amps)):
        ph = r.uniform(0, 2 * np.pi) if r is not None else 0.0
        out += a * np.sin(2 * np.pi * f * (t - start) + ph) * env(t, atk, d, start)
    return out


# ----------------------------------------------------------------------------------------------- space

def _comb(x, D, g):
    y = x.copy()
    for k in range(D, len(x), D):
        e = min(k + D, len(x))
        y[k:e] += g * y[k - D:e - D]
    return y


def _allpass(x, D, g):
    y = np.zeros_like(x)
    y[:D] = -g * x[:D]
    for k in range(D, len(x), D):
        e = min(k + D, len(x))
        y[k:e] = -g * x[k:e] + x[k - D:e - D] + g * y[k - D:e - D]
    return y


def reverb(x, wet=0.25, rt=0.6, size=1.0, damp=6000.0, pre=0.006):
    """Small Schroeder room + early reflections. Output keeps the input length."""
    n = len(x)
    xp = np.concatenate([np.zeros(int(pre * SR)), x])[:n]
    er = np.zeros(n)
    for ms, g in [(7.1, 0.5), (11.3, 0.4), (17.9, 0.3), (23.3, 0.25), (31.7, 0.18)]:
        d = int(ms * size * SR / 1000)
        if d < n:
            er[d:] += g * xp[:n - d]
    tail = np.zeros(n)
    for ms in [29.7, 37.1, 41.1, 43.7, 47.9, 53.3]:
        D = int(ms * size * SR / 1000)
        tail += _comb(xp, D, 10 ** (-3 * D / SR / rt))
    for ms in [5.0, 1.7]:
        tail = _allpass(tail, int(ms * SR / 1000), 0.7)
    tail = lp(tail, damp)
    ref = np.sqrt(np.mean(x ** 2)) + 1e-12
    wetsig = 0.5 * er / (np.sqrt(np.mean(er ** 2)) + 1e-12) + tail / (np.sqrt(np.mean(tail ** 2)) + 1e-12)
    return x + wet * ref * wetsig


def riser_finish(x):
    """Risers: tiny fade-in, 3 ms fade-out so the last sample isn't a click; peak stays at the end."""
    return fades(x, 0.002, 0.003)


# =============================================================================================== PHOENIX
# fire: flare ignition, hissing crackling flame, rising sizzle, bright warm burst

def phoenix_throw():
    t, r = T(0.2), R(101); n = len(t)
    thump = sine(sweep(150, 55, t, 0.08), n) * env(t, 0.002, 0.035)
    whoomp = svf(white(n, r), sweep(2800, 450, t, 0.15), 0.8, "lp") * env(t, 0.004, 0.05)
    hiss = hp(white(n, r), 3500) * env(t, 0.001, 0.02)
    crack = bp(impulses(t, 900 * np.exp(-t / 0.06), r), 2600, 1.4) * 2.2 * np.exp(-t / 0.08)
    fire = bp(pink(n, r), 1300, 0.7) * env(t, 0.012, 0.07) * (1 + 0.6 * flicker(n, r, 30))
    x = sat(0.8 * thump + 1.1 * whoomp + 0.35 * hiss + crack + 0.6 * fire, 1.6)
    return norm(fades(reverb(x, 0.15, 0.35)), -3)


def phoenix_flight():
    t, r = T(1.6), R(102); n = len(t)
    roar = lp(pink(n, r), 380) * (0.8 + 0.4 * flicker(n, r, 9))
    centre = 2600 * (1 + 0.25 * flicker(n, r, 1.5)) * sweep(1.0, 0.8, t, 1.6)
    hiss = svf(white(n, r), centre, 1.3, "bp") * (0.75 + 0.35 * flicker(n, r, 14))
    crack = bp(impulses(t, 45, r), 3000, 1.8) * 4 + hp(impulses(t, 20, r), 2000) * 1.2
    shape = smooth(t / 0.08) * (1 - smooth((t - 0.35) / 1.25)) ** 1.4
    x = (1.0 * roar + 0.9 * hiss + crack) * shape
    return norm(fades(sat(x, 1.2), 0.002, 0.03), -10)


def phoenix_windup():
    t, r = T(0.60), R(103); n = len(t); u = t / t[-1]
    sizzle = svf(white(n, r), 1400 * 4.5 ** u, 1 + 3 * u, "bp")
    roar = svf(pink(n, r), 280 + 1200 * u, 0.8, "lp") * (0.8 + 0.3 * flicker(n, r, 12))
    crack = bp(impulses(t, 30 + 520 * u ** 2, r), 3400, 1.6) * 4
    tone = lp(saw(200 * 2 ** (1.7 * u), n), 2600) * u ** 1.5
    x = sat((0.9 * sizzle + 0.8 * roar + crack + 0.25 * tone) * (0.08 + 0.92 * u ** 2.4), 1.8)
    return norm(riser_finish(x), -4)


def phoenix_pop():
    t, r = T(0.62), R(104); n = len(t)
    crack = hp(white(n, r), 2000) * env(t, 0.0003, 0.006)
    body = lp(white(n, r), 1900) * env(t, 0.001, 0.05)
    thump = sine(sweep(120, 45, t, 0.12), n) * env(t, 0.001, 0.07)
    flare = svf(white(n, r), sweep(3200, 700, t, 0.4), 0.9, "bp") * env(t, 0.01, 0.15) * (1 + 0.5 * flicker(n, r, 25))
    crackle = bp(impulses(t, 500 * np.exp(-t / 0.14), r), 2600, 1.6) * 3 * np.exp(-t / 0.18)
    air = hp(white(n, r), 6500) * env(t, 0.001, 0.04)
    x = sat(1.4 * crack + 0.9 * body + 0.9 * thump + 0.9 * flare + crackle + 0.3 * air, 2.0)
    return norm(fades(reverb(x, 0.22, 0.6, 1.0, 5500), 0.0005, 0.05), -3)


# =============================================================================================== BREACH
# seismic: gauntlet thunk + electric charge, muffled rumble through rock, gritty electric riser, concussive crunch

def breach_throw():
    t, r = T(0.26), R(201); n = len(t)
    thunk = sine(sweep(95, 38, t, 0.1), n) * env(t, 0.001, 0.06)
    clank = modal(t, [310, 742, 1130, 1870, 2650], [0.06, 0.045, 0.03, 0.02, 0.012], [0.5, 0.4, 0.3, 0.2, 0.12], r=r)
    zap = bp(saw(110 * (1 + 0.04 * flicker(n, r, 40)), n), 1500, 3) * env(t, 0.01, 0.07, 0.02)
    spark = hp(impulses(t, 600 * env(t, 0.01, 0.05, 0.02), r), 3000) * 2
    burst = lp(white(n, r), 1200) * env(t, 0.001, 0.02)
    x = sat(0.8 * thunk + clank + 0.6 * zap + spark + 0.6 * burst, 1.3)
    return norm(fades(reverb(x, 0.12, 0.35)), -3)


def breach_flight():
    t, r = T(1.5), R(202); n = len(t)
    rum = lp(pink(n, r), 170) * (1 + 0.5 * flicker(n, r, 12))
    sub = sine(45 * (1 + 0.06 * flicker(n, r, 3)), n)
    rock = lp(bp(impulses(t, 70, r), 520, 1.5), 1500) * 6
    hum = lp(saw(60, n), 420)
    shape = smooth(t / 0.05) * (1 - smooth((t - 0.3) / 1.2)) ** 1.3
    grit = bp(white(n, r), 1100, 1.5) * (0.5 + 0.5 * flicker(n, r, 20)) ** 2
    x = (1.4 * rum + 0.5 * sub + rock + 0.25 * hum + 0.25 * grit) * shape
    return norm(fades(sat(x, 1.5), 0.002, 0.03), -10)


def breach_windup():
    t, r = T(0.50), R(203); n = len(t); u = t / t[-1]
    rum = svf(pink(n, r), 120 + 450 * u, 0.9, "lp") * (1 + 0.5 * flicker(n, r, 18))
    sub = sine(sweep(38, 70, t, 0.5), n)
    rock = bp(impulses(t, 40 + 900 * u ** 2, r), 900 + 1600 * u, 1.2) * 5
    whine = svf(saw(80 * 3 ** u, n), 600 * 4 ** u, 4, "bp")
    tone = sine(400 * 5 ** u, n) * u ** 2
    elec = hp(impulses(t, 30 + 500 * u ** 2, r), 3000) * 2
    x = (1.0 * rum + 0.6 * sub + rock + 0.8 * whine + 0.35 * tone + elec) * (0.1 + 0.9 * u ** 2.2)
    return norm(riser_finish(sat(x, 2.6)), -4)


def breach_pop():
    t, r = T(0.72), R(204); n = len(t)
    boom = sine(sweep(85, 32, t, 0.25), n) * env(t, 0.001, 0.15)
    crack = white(n, r) * env(t, 0.0003, 0.008)
    debris = impulses(t, 2500 * np.exp(-t / 0.08), r)
    crunch = bp(debris, 1200, 0.8) * 5 + hp(debris, 2500) * 2
    body = lp(white(n, r), 1000) * env(t, 0.002, 0.09)
    gravel = bp(white(n, r), 700, 1.0) * env(t, 0.01, 0.18) * (1 + 0.8 * flicker(n, r, 40))
    x = sat(1.3 * boom + 1.0 * crack + crunch + 0.9 * body + 0.4 * gravel, 3.0)
    return norm(fades(reverb(x, 0.3, 0.8, 1.3, 4000), 0.0005, 0.06), -3)


# =============================================================================================== KAY/O
# robot: servo chirp + latch click, (no flight audio), accelerating beeps over a digital charge, digital zap

def kayo_throw():
    t, r = T(0.16), R(301); n = len(t)
    servo = bp(square(sweep(600, 1400, t, 0.08), n), 2000, 1.2) * env(t, 0.003, 0.04)
    whir = bp(white(n, r), 3000, 3) * (0.6 + 0.4 * np.sin(2 * np.pi * 90 * t)) * env(t, 0.01, 0.04)
    clicks = modal(t, [3200, 5100, 7300], [0.004, 0.003, 0.002], [0.6, 0.4, 0.2]) + \
        modal(t, [2900, 4700], [0.004, 0.003], [0.4, 0.3], start=0.06)
    whoosh = svf(white(n, r), sweep(800, 2600, t, 0.12), 1.0, "bp") * env(t, 0.02, 0.05)
    x = 0.8 * servo + 0.4 * whir + clicks + 0.7 * whoosh
    return norm(fades(reverb(x, 0.1, 0.3)), -3)


def kayo_flight():
    # Riot removed KAY/O's in-flight audio (P 3.06): keep only a barely-audible electronic hum.
    t, r = T(1.2), R(302); n = len(t)
    hum = (sine(240, n) + 0.4 * sine(480, n)) * (0.8 + 0.2 * np.sin(2 * np.pi * 6 * t))
    air = hp(white(n, r), 4000) * 0.1
    x = (hum + air) * smooth(t / 0.05) * (1 - smooth((t - 0.2) / 1.0))
    return norm(fades(x, 0.002, 0.03), -36)


def kayo_beep_windup(dur, bounce, seed):
    t, r = T(dur), R(seed); n = len(t); u = t / t[-1]
    out = np.zeros(n)
    i0, i1 = (0.17, 0.028) if bounce else (0.075, 0.024)
    tb = 0.05 if bounce else 0.0
    onsets = []
    while tb < dur - 0.03:
        onsets.append(tb)
        tb += i0 * (i1 / i0) ** (tb / dur)
    for k, s in enumerate(onsets):
        prog = s / dur
        L = 0.045 if bounce and prog < 0.3 else 0.03
        gt = T(L)
        f = 1700 * (2600 / 1700) ** prog
        g = (np.sin(2 * np.pi * f * gt) + 0.25 * np.sin(2 * np.pi * 3 * f * gt)) * fades(np.ones(len(gt)), 0.001, 0.006)
        place(out, int(s * SR), g * (0.35 + 0.65 * prog))
    # the last ~40 ms the beeps fuse into one continuous rising tone that lands on the pop
    tail = smooth((t - (dur - 0.045)) / 0.03)
    out = out * (1 - tail) + tail * np.sin(phase(2600 * (1 + 0.15 * u))) * 1.0
    fc = sweep(300, 1600, t, dur)
    charge = np.sin(phase(fc) + 2.0 * np.sin(phase(fc * 1.5))) * u ** 2
    bits = hp(crush(white(n, r), 5, 4), 2000) * u ** 3
    x = out + 0.45 * charge + 0.18 * bits
    if bounce:   # sharp electronic chirp on first contact, quieter than the end
        chirp = np.sin(phase(sweep(3400, 1100, t, 0.025))) * env(t, 0.0005, 0.02)
        x += 0.55 * chirp + 0.35 * modal(t, [4100, 6300], [0.003, 0.002], [1, 0.6])
    x = x * (0.25 + 0.75 * u ** 1.6)
    return norm(riser_finish(sat(x, 1.3)), -4)


def kayo_windup():
    return kayo_beep_windup(0.30, False, 303)


def kayo_windup_bounce():
    return kayo_beep_windup(0.80, True, 304)


def kayo_pop():
    t, r = T(0.42), R(305); n = len(t)
    f = sweep(3600, 150, t, 0.12)
    zap = np.sin(phase(f) + 3 * np.sin(phase(f * 1.41))) * env(t, 0.0005, 0.08)
    bits = hp(crush(white(n, r), 6, 4), 800) * env(t, 0.0005, 0.05)
    thump = sine(sweep(95, 40, t, 0.1), n) * env(t, 0.001, 0.06)
    blips = crush(hp(impulses(t, 900 * np.exp(-t / 0.06), r), 1500), 3, 3) * 1.5 * np.exp(-t / 0.07)
    buzz = square(60, n) * env(t, 0.002, 0.05)
    snap = hp(white(n, r), 3000) * env(t, 0.0002, 0.004)
    x = sat(1.0 * zap + 0.6 * bits + 0.8 * thump + blips + 0.25 * buzz + 1.0 * snap, 2.0)
    return norm(fades(reverb(x, 0.15, 0.45, 0.9, 7000), 0.0003, 0.04), -3)


# =============================================================================================== SKYE
# hawk: synthesized raspy screech (FM + formant), wing flaps, leafy rustle, chime pop

def hawk_cry(t, f, r, rasp=0.9):
    n = len(t)
    f = f * (1 + 0.03 * flicker(n, r, 60))
    ph = phase(f)
    src = np.sin(ph + rasp * np.sin(0.5 * ph)) + 0.35 * np.sin(2 * ph) + 0.15 * np.sin(3 * ph)
    return 0.6 * src + 0.8 * bp(src, 3100, 2.5)


def rustle(t, density, r, tau=0.004):
    n = len(t)
    g = lp(grains_env(impulses(t, density, r, 1.5), tau), 350)
    return bp(white(n, r), 6000, 0.7) * g


def skye_throw():
    t, r = T(0.27), R(401); n = len(t)
    f = np.interp(t, [0, 0.04, 0.12, 0.2, 0.27], [1900, 3100, 2700, 1900, 1700])
    cry = hawk_cry(t, f, r) * smooth(t / 0.012) * (1 - smooth((t - 0.15) / 0.1))
    flaps = lp(white(n, r), 650) * (env(t, 0.012, 0.03) + 0.8 * env(t, 0.012, 0.03, 0.1))
    leaves = rustle(t, 600 * env(t, 0.005, 0.1), r)
    x = 0.8 * cry + 1.2 * flaps + 1.5 * leaves
    return norm(fades(reverb(x, 0.2, 0.5)), -3)


def skye_flight():
    t, r = T(1.8), R(402); n = len(t)
    beat = (0.5 + 0.5 * np.sin(2 * np.pi * 4.6 * t + 0.8 * flicker(n, r, 1))) ** 3
    wings = svf(white(n, r), 500 + 700 * beat, 0.9, "lp") * beat
    shimmer = sum(np.sin(phase(f * (1 + 0.004 * flicker(n, r, 0.7)))) * (0.6 + 0.4 * np.sin(2 * np.pi * tr * t + p))
                  for f, tr, p in [(5200, 9, 0), (6100, 11, 1), (7300, 7, 2), (4400, 13, 3)]) / 4
    leaves = rustle(t, 160, r)
    shape = smooth(t / 0.1) * (1 - smooth((t - 0.4) / 1.4)) ** 1.3
    x = (1.3 * wings + 0.12 * shimmer + 0.6 * leaves) * shape
    return norm(fades(x, 0.002, 0.03), -11)


def skye_windup():
    t, r = T(0.30), R(403); n = len(t); u = t / t[-1]
    f = 2000 * 1.8 ** u * (1 + 0.02 * u * np.sin(2 * np.pi * 30 * t))
    cry = hawk_cry(t, f, r, rasp=0.6 + 0.6 * u)
    shimmer = sum(np.sin(phase(fr * 1.5 ** u)) for fr in (4700, 5900, 7400)) / 3
    leaves = rustle(t, 200 + 900 * u, r)
    x = (0.9 * cry + 0.4 * shimmer * u + leaves) * (0.08 + 0.92 * u ** 1.8)
    return norm(riser_finish(sat(x, 1.3)), -4)


def skye_pop():
    t, r = T(0.62), R(404); n = len(t)
    ratios, amps, decs = [1, 2.0, 2.76, 4.07, 5.40], [1, 0.45, 0.6, 0.3, 0.22], [0.32, 0.22, 0.18, 0.11, 0.08]
    chime = modal(t, [1320 * k for k in ratios], decs, amps, r=r)
    chime += 0.6 * modal(t, [1980 * k for k in ratios], [d * 0.8 for d in decs], amps, start=0.015, r=r)
    air = bp(white(n, r), 5000, 0.7) * env(t, 0.0005, 0.03)
    leaves = rustle(t, 3000 * np.exp(-t / 0.08), r, 0.003)
    thump = sine(sweep(160, 70, t, 0.08), n) * env(t, 0.002, 0.04)
    snap = hp(white(n, r), 4000) * env(t, 0.0002, 0.003)
    x = sat(0.9 * chime + 0.8 * air + 1.4 * leaves + 0.6 * thump + 0.8 * snap, 1.4)
    return norm(fades(reverb(x, 0.3, 0.9, 1.0, 8000), 0.0003, 0.05), -3)


# =============================================================================================== YORU
# dimensional glass: glassy tear, ethereal shimmer, tick + rising glass whine, glass shatter

GLASS = [1, 1.53, 2.31, 3.1, 4.27]


def yoru_throw():
    t, r = T(0.2), R(501); n = len(t)
    nz = white(n, r)
    tear = svf(nz, sweep(7500, 2400, t, 0.12), 3, "bp") * env(t, 0.002, 0.05)
    D = int(SR / 2900)
    a = np.zeros(D + 1); a[0], a[D] = 1, -0.65
    glassy = signal.lfilter([1], a, tear)
    partials = modal(t, [3100, 4740, 6900], [0.025, 0.02, 0.015], [0.4, 0.3, 0.2], r=r)
    shh = hp(nz, 5000) * env(t, 0.001, 0.03)
    x = 1.0 * glassy + partials + 0.6 * shh
    return norm(fades(reverb(x, 0.25, 0.5, 0.8, 9000)), -3)


def yoru_flight():
    t, r = T(1.4), R(502); n = len(t)
    shimmer = sum(np.sin(phase(f * (1 + 0.01 * flicker(n, r, 0.6)))) * (0.6 + 0.4 * np.sin(2 * np.pi * tr * t + tr))
                  for f, tr in [(1870, 6), (2480, 8.5), (3310, 7), (4420, 10.5), (5900, 11)]) / 5
    air = svf(white(n, r), 4000 * (1 + 0.4 * flicker(n, r, 1)), 1.2, "bp")
    low = lp(pink(n, r), 300) * (0.7 + 0.3 * flicker(n, r, 2))
    shape = smooth(t / 0.15) * (1 - smooth((t - 0.3) / 1.1)) ** 1.3
    x = reverb((0.6 * shimmer + 0.35 * air + 0.5 * low) * shape, 0.5, 1.0, 1.1, 9000)
    return norm(fades(x, 0.002, 0.03), -12)


def yoru_windup():
    t, r = T(0.60), R(503); n = len(t); u = t / t[-1]
    tick = modal(t, [4200, 6100, 7900], [0.015, 0.01, 0.008], [1, 0.7, 0.5]) + hp(white(n, r), 3000) * env(t, 0.0002, 0.002)
    f = 900 * (2400 / 900) ** u
    trem_rate = 8 + 32 * u
    trem = 1 - 0.6 * (0.5 + 0.5 * np.sin(phase(trem_rate))) * (1 - smooth((u - 0.88) / 0.12))
    whine = sum(np.sin(phase(f * k)) / (1 + i) for i, k in enumerate(GLASS[:4])) * trem
    hiss = svf(white(n, r), 2000 * 4 ** u, 2, "bp")
    x = 0.12 * tick + (0.7 * whine + 0.5 * hiss) * (0.05 + 0.95 * u ** 2.2)
    return norm(riser_finish(x), -4)


def yoru_pop():
    t, r = T(0.62), R(504); n = len(t)
    crack = hp(white(n, r), 1500) * env(t, 0.0002, 0.004) + bp(white(n, r), 3000, 1) * env(t, 0.0005, 0.01)
    whomp = sine(sweep(130, 45, t, 0.1), n) * env(t, 0.001, 0.08)
    shards = np.zeros(n)
    for _ in range(140):
        t0 = r.exponential(0.07)
        f0 = np.exp(r.uniform(np.log(2500), np.log(9500)))
        d = r.uniform(0.008, 0.04)
        gt = T(d * 5)
        g = (np.sin(2 * np.pi * f0 * gt) + 0.5 * np.sin(2 * np.pi * f0 * 1.47 * gt)) * env(gt, 0.0003, d)
        place(shards, int(t0 * SR), g * r.uniform(0.3, 1) * np.exp(-t0 / 0.12))
    hiss = hp(white(n, r), 5000) * env(t, 0.001, 0.06)
    x = sat(1.2 * crack + 0.7 * whomp + 0.5 * shards + 0.5 * hiss, 1.5)
    return norm(fades(reverb(x, 0.3, 0.7, 0.9, 10000), 0.0003, 0.05), -3)


# =============================================================================================== VYSE
# liquid metal: shing, creaking metal vine, metallic scrape riser + crystalline ring, ringing metal burst

def vyse_throw():
    t, r = T(0.22), R(601); n = len(t)
    exc = white(n, r) * env(t, 0.001, 0.05)
    metal = resonators(exc, [1730, 2610, 3890, 5240, 7110], 60, [1, 0.8, 0.7, 0.5, 0.3])
    scrape = svf(white(n, r), sweep(3000, 6500, t, 0.1), 6, "bp") * env(t, 0.005, 0.06)
    ring = sine(3890, n) * env(t, 0.002, 0.08)
    x = 1.0 * metal + 0.6 * scrape + 0.25 * ring
    return norm(fades(reverb(x, 0.2, 0.5, 0.9, 9000)), -3)


def vyse_flight():
    t, r = T(1.2), R(602); n = len(t)
    creak_exc = white(n, r) * (0.5 + 0.5 * flicker(n, r, 6)) ** 2 + impulses(t, 25, r) * 3
    creak = resonators(creak_exc, [410, 690, 1130, 1580, 2470], 40, [1, 0.8, 0.6, 0.4, 0.3])
    shimmer = sine(5240 * (1 + 0.003 * flicker(n, r, 1)), n) * 0.03 * (0.5 + 0.5 * flicker(n, r, 3))
    shape = smooth(t / 0.06) * (1 - smooth((t - 0.25) / 0.95)) ** 1.3
    x = (creak + shimmer) * shape
    return norm(fades(x, 0.002, 0.03), -12)


def vyse_windup():
    t, r = T(0.50), R(603); n = len(t); u = t / t[-1]
    nz = white(n, r)
    scrape = svf(nz, 1200 * 4 ** u, 12, "bp") + 0.7 * svf(nz, 1700 * 4 ** u, 12, "bp")
    petals = resonators(impulses(t, 20 + 250 * u ** 2, r, 1.5), [2610, 3890, 5240], 50) * 3
    fc = 1500 * 2 ** u
    ring = np.sin(phase(fc) + 1.5 * u * np.sin(phase(fc * 1.414)))
    x = (1.2 * scrape + petals + 0.5 * ring * u ** 1.5) * (0.08 + 0.92 * u ** 2.2)
    return norm(riser_finish(sat(x, 1.4)), -4)


def vyse_pop():
    t, r = T(0.75), R(604); n = len(t)
    ratios = [1, 1.59, 2.14, 2.30, 2.65, 3.6, 4.15, 5.1, 6.3, 8.2]
    decs = np.linspace(0.42, 0.08, len(ratios))
    amps = [1, 0.8, 0.7, 0.6, 0.55, 0.45, 0.4, 0.3, 0.25, 0.2]
    plate = modal(t, [620 * k for k in ratios], decs, amps, r=r)
    plate += 0.5 * modal(t, [620 * k * 1.004 for k in ratios], decs, amps, r=r)   # detuned pair -> beating
    snap = hp(white(n, r), 2500) * env(t, 0.0003, 0.005)
    thump = sine(sweep(150, 60, t, 0.1), n) * env(t, 0.001, 0.05)
    clang = resonators(white(n, r) * env(t, 0.001, 0.02), [1730, 2610, 3890, 5240], 30) * 2
    x = sat(0.5 * plate + 1.2 * snap + 0.7 * thump + clang, 1.6)
    return norm(fades(reverb(x, 0.3, 1.0, 1.1, 8000), 0.0003, 0.06), -3)


# =============================================================================================== GEKKO / DIZZY
# cute alien goo: gloopy chirps, squelchy rising charge, bubbly hum, wet "bloop-pew", splorch, squeak + deflate

def dizzy_throw():
    t, r = T(0.4), R(701); n = len(t)
    f = np.interp(t, [0, 0.1, 0.16, 0.4], [520, 1500, 1250, 1350]) * (1 + 0.05 * np.sin(2 * np.pi * 25 * t))
    chirp = (np.sin(phase(f)) + 0.3 * np.sin(2 * phase(f))) * env(t, 0.01, 0.12)
    chirp = 0.5 * chirp + bp(chirp, 1800, 1.5)
    gloop = np.zeros(n)
    place(gloop, 0, bubble(300, 0.1, 0.8, 0.03))
    place(gloop, int(0.05 * SR), 0.8 * bubble(460, 0.1, 0.9, 0.025))
    whoosh = svf(white(n, r), sweep(600, 2200, t, 0.25), 1, "bp") * env(t, 0.05, 0.12)
    x = 0.9 * chirp + 0.9 * gloop + 0.7 * whoosh
    return norm(fades(reverb(x, 0.15, 0.4)), -3)


def dizzy_form():
    t, r = T(0.65), R(702); n = len(t); u = t / t[-1]
    src = saw(110 * (420 / 110) ** (u ** 1.3), n)
    wob = 0.5 * np.sin(phase(6 + 18 * u)) * (1 - smooth((u - 0.88) / 0.12))
    squelch = svf(src, (400 + 2200 * u ** 1.5) * (1 + wob), 6, "lp")
    bub = bubbles(t, 15 + 50 * u, r, 400, 1400)
    x = (0.8 * squelch + 0.35 * bub) * (0.2 + 0.8 * u ** 2.5) * (1 + 0.5 * smooth((u - 0.82) / 0.18))
    return norm(riser_finish(sat(x, 1.3)), -5)


def dizzy_active():
    t, r = T(1.0), R(703); n = len(t)
    f0 = 98 * (1 + 0.03 * flicker(n, r, 2))
    hum = lp(saw(f0, n), 700) + 0.5 * np.sin(2 * phase(f0))
    hum *= 0.75 + 0.25 * np.sin(2 * np.pi * 7 * t)
    bub = bp(bubbles(t, 18, r, 400, 1200), 900, 0.8)
    hover = bp(white(n, r), 300, 2) * (0.7 + 0.3 * flicker(n, r, 4))
    x = (0.7 * hum + 0.6 * bub + 0.5 * hover) * smooth(t / 0.05) * (1 - smooth((t - 0.85) / 0.15))
    return norm(fades(sat(x, 1.2)), -8)


def dizzy_blast(close: bool, seed: int):
    t, r = T(0.62), R(seed); n = len(t)
    bloop = np.sin(phase(sweep(260, 900, t, 0.05))) * env(t, 0.002, 0.04)
    fp = sweep(2400, 450, t, 0.14, 0.04)
    pew = np.sin(phase(fp) + 1.2 * np.sin(0.5 * phase(fp))) * env(t, 0.002, 0.08, 0.04)
    wet = svf(white(n, r), sweep(3000, 400, t, 0.12), 2, "lp") * env(t, 0.002, 0.05)
    tail = bubbles(t, 90 * np.where(t > 0.08, np.exp(-(t - 0.08) / 0.15), 0), r, 500, 1600, amp_decay=np.exp(-t / 0.25))
    x = 1.0 * bloop + 0.9 * pew + 0.5 * wet + 0.45 * tail
    if close:
        # a plasma blast flying at YOU: approaching whoosh, brighter, drier, harder
        rush = svf(white(n, r), sweep(1500, 5000, t, 0.12), 1.2, "bp") * env(t, 0.06, 0.05) * 0.9
        sizzle = hp(white(n, r), 4500) * env(t, 0.003, 0.05, 0.05) * 0.4
        x = sat(x + rush + sizzle + 0.4 * hp(x, 2500), 1.8)
        return norm(fades(reverb(x, 0.1, 0.4)), -3)
    x = lp(x, 2600)
    return norm(fades(reverb(x, 0.45, 0.8, 1.2, 3000)), -9)


def dizzy_spit():
    return dizzy_blast(False, 704)


def dizzy_incoming():
    return dizzy_blast(True, 704)


def dizzy_splat():
    t, r = T(0.5), R(705); n = len(t)
    thump = sine(sweep(160, 55, t, 0.08), n) * env(t, 0.001, 0.06)
    wet = svf(white(n, r), sweep(2500, 250, t, 0.15), 3, "lp") * env(t, 0.001, 0.09)
    sq = svf(saw(sweep(200, 90, t, 0.2), n), 1200 * (1 + 0.4 * np.sin(2 * np.pi * 18 * t)), 8, "lp") * env(t, 0.005, 0.08)
    drops = bubbles(t, 160 * np.exp(-t / 0.1), r, 600, 2000, decay=(0.008, 0.02), amp_decay=np.exp(-t / 0.12))
    x = sat(1.0 * thump + 1.0 * wet + 0.5 * sq + 0.5 * drops, 2.0)
    return norm(fades(reverb(x, 0.15, 0.4)), -3)


def dizzy_down():
    t, r = T(0.62), R(706); n = len(t)
    fs = sweep(1600, 2600, t, 0.08) * (1 + 0.05 * np.sin(2 * np.pi * 35 * t))
    squeak = np.sin(phase(fs)) * smooth(t / 0.005) * (1 - smooth((t - 0.07) / 0.03))
    pop = np.zeros(n)
    p0 = int(0.09 * SR)
    pop[p0:] = hp(white(n - p0, r), 1000) * env(np.arange(n - p0) / SR, 0.0005, 0.01)
    place(pop, p0, 0.8 * bubble(900, 0.08, 0.5, 0.02))
    td = t - 0.1
    deflate = svf(saw(np.where(td > 0, sweep(520, 110, td, 0.45), 520), n), sweep(1500, 400, td, 0.45), 4, "lp")
    deflate *= (0.6 + 0.4 * np.sin(2 * np.pi * 28 * t)) * env(t, 0.02, 0.18, 0.1)
    hiss = bp(white(n, r), 3000, 0.7) * env(t, 0.01, 0.2, 0.1)
    x = sat(0.7 * squeak + 1.0 * pop + 0.9 * deflate + 0.12 * hiss, 1.4)
    return norm(fades(reverb(x, 0.15, 0.4)), -4)


def dizzy_bubble():
    t, r = T(2.0), R(707); n = len(t); u = t / t[-1]
    bub = bubbles(t, 30 - 18 * u, r, 200, 900, decay=(0.02, 0.06))
    rum = lp(pink(n, r), 150) * (0.7 + 0.3 * flicker(n, r, 3))
    x = lp(0.9 * bub + 0.6 * rum, 700) * smooth(t / 0.03) * (1 - u) ** 1.5
    return norm(fades(x, 0.002, 0.02), -12)


# =============================================================================================== RING

def flash_ring():
    """The 'flashed' whine: thin ~4.1 kHz tone with a slow beat, 3.3 s, last 1 s fading out."""
    t, r = T(3.3), R(801); n = len(t)
    vib = 1 + 0.002 * np.sin(2 * np.pi * 5 * t)
    tone = np.sin(phase(4100 * vib)) + 0.45 * np.sin(phase(4113 * vib)) + 0.08 * np.sin(phase(8200 * vib))
    air = bp(white(n, r), 4100, 30) * 0.6
    shape = smooth(t / 0.04) * (1 - 0.2 * np.clip(t / 2.3, 0, 1)) * (1 - np.clip((t - 2.3) / 1.0, 0, 1)) ** 2
    return norm(fades((tone + air) * shape, 0.002, 0.01), -16)


# =============================================================================================== registry

SOUNDS = {
    "phoenix_throw": phoenix_throw, "phoenix_flight": phoenix_flight, "phoenix_windup": phoenix_windup, "phoenix_pop": phoenix_pop,
    "breach_throw": breach_throw, "breach_flight": breach_flight, "breach_windup": breach_windup, "breach_pop": breach_pop,
    "kayo_throw": kayo_throw, "kayo_flight": kayo_flight, "kayo_windup": kayo_windup, "kayo_windup_bounce": kayo_windup_bounce,
    "kayo_pop": kayo_pop,
    "skye_throw": skye_throw, "skye_flight": skye_flight, "skye_windup": skye_windup, "skye_pop": skye_pop,
    "yoru_throw": yoru_throw, "yoru_flight": yoru_flight, "yoru_windup": yoru_windup, "yoru_pop": yoru_pop,
    "vyse_throw": vyse_throw, "vyse_flight": vyse_flight, "vyse_windup": vyse_windup, "vyse_pop": vyse_pop,
    "dizzy_throw": dizzy_throw, "dizzy_form": dizzy_form, "dizzy_active": dizzy_active, "dizzy_spit": dizzy_spit,
    "dizzy_incoming": dizzy_incoming, "dizzy_splat": dizzy_splat, "dizzy_down": dizzy_down, "dizzy_bubble": dizzy_bubble,
    "flash_ring": flash_ring,
}

# Exact required lengths (seconds): risers must end on the pop.
WINDUPS = {"phoenix_windup": 0.60, "breach_windup": 0.50, "kayo_windup": 0.30, "kayo_windup_bounce": 0.80,
           "skye_windup": 0.30, "yoru_windup": 0.60, "vyse_windup": 0.50, "dizzy_form": 0.65}


def write_wav(path: str, x: np.ndarray) -> None:
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def default_out() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    for cand in (os.path.join(here, "..", "..", "ValTrainerGodot"), os.path.join(here, "..")):
        if os.path.isfile(os.path.join(cand, "project.godot")):
            return os.path.normpath(os.path.join(cand, "assets", "audio", "flash"))
    return os.path.join(here, "flash")


# ----------------------------------------------------------------------------------------------- review

def _cmap(v):
    stops = np.array([[0, 0, 0], [40, 10, 90], [150, 30, 120], [235, 100, 40], [255, 230, 120], [255, 255, 255]], float)
    xs = np.linspace(0, 1, len(stops))
    return np.stack([np.interp(v, xs, stops[:, c]) for c in range(3)], -1).astype(np.uint8)


def review(out_dir: str, rendered: dict) -> None:
    from PIL import Image, ImageDraw
    os.makedirs(out_dir, exist_ok=True)
    open(os.path.join(out_dir, ".gdignore"), "w").close()
    lines = [f"{'key':22s} {'dur s':>7s} {'samples':>8s} {'peak dB':>8s} {'rms dB':>7s} {'loudest@':>9s}"]
    W, H, EH = 420, 170, 50
    tiles = []
    for name, x in rendered.items():
        pk = 20 * np.log10(np.max(np.abs(x)) + 1e-12)
        rms = 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12)
        win = int(0.01 * SR)
        e = np.sqrt(np.convolve(x ** 2, np.ones(win) / win, "valid"))
        loud = (np.argmax(e) + win) / len(x)
        lines.append(f"{name:22s} {len(x) / SR:7.4f} {len(x):8d} {pk:8.2f} {rms:7.2f} {loud:9.1%}")
        f, tt, Z = signal.stft(x, SR, nperseg=1024, noverlap=1024 - 128)
        mag = 20 * np.log10(np.abs(Z) + 1e-9)
        keep = f <= 14000
        mag = mag[keep][::-1]
        v = np.clip((mag - (mag.max() - 80)) / 80, 0, 1)
        spec = Image.fromarray(_cmap(v)).resize((W, H))
        tile = Image.new("RGB", (W, H + EH + 16), (20, 20, 24))
        d = ImageDraw.Draw(tile)
        d.text((4, 2), f"{name}  {len(x) / SR:.3f}s  pk {pk:.1f}  rms {rms:.1f}", fill=(230, 230, 230))
        cols = np.array_split(np.abs(x), W)
        for i, c in enumerate(cols):
            h = int(np.max(c) * (EH - 4))
            d.line([(i, 16 + EH // 2 - h // 2), (i, 16 + EH // 2 + h // 2)], fill=(120, 200, 255))
        tile.paste(spec, (0, 16 + EH))
        tiles.append(tile)
    per = 9
    for s in range(0, len(tiles), per):
        group = tiles[s:s + per]
        tw, th = group[0].size
        sheet = Image.new("RGB", (3 * (tw + 6), ((len(group) + 2) // 3) * (th + 6)), (0, 0, 0))
        for i, tl in enumerate(group):
            sheet.paste(tl, ((i % 3) * (tw + 6), (i // 3) * (th + 6)))
        sheet.save(os.path.join(out_dir, f"sheet_{s // per + 1}.png"))
    lines.append("")
    for k, d in WINDUPS.items():
        if k in rendered:
            ok = len(rendered[k]) == int(round(d * SR))
            lines.append(f"windup {k:20s} want {d:.2f}s = {int(round(d * SR))} samples -> {'OK' if ok else 'WRONG'}")
    with open(os.path.join(out_dir, "listing.txt"), "w") as fh:
        fh.write("\n".join(lines) + "\n")
    print("\n".join(lines))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=None, help="output folder for the WAVs")
    ap.add_argument("--review", default=None, help="also write listing.txt + spectrogram sheets here")
    ap.add_argument("--only", nargs="*", help="render only these keys")
    a = ap.parse_args()
    out = a.out or default_out()
    os.makedirs(out, exist_ok=True)
    rendered = {}
    for name, fn in SOUNDS.items():
        if a.only and name not in a.only:
            continue
        x = fn()
        if name in WINDUPS:
            assert len(x) == int(round(WINDUPS[name] * SR)), (name, len(x))
        write_wav(os.path.join(out, name + ".wav"), x)
        rendered[name] = x
    print(f"wrote {len(rendered)} WAVs to {out}")
    if a.review:
        review(a.review, rendered)
    return 0


if __name__ == "__main__":
    sys.exit(main())
