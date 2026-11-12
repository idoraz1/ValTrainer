#!/usr/bin/env python3
"""
ValTrainer skin sound generator (v1.19 skins 2, skins2-design-1.19.md section 4.3).

Renders the sound set of every skin line that has one (Lattice, Rainfront, Strata, Escapement, Phosphor Trace,
Calibrated) as 44.1 kHz 16-bit mono WAVs into <ValTrainerGodot>/assets/audio/skins/<line>/, then MEASURES every file
against the factory sounds it plays with and fails (exit code 1) if any rule is broken.

100 % our own synthesis: no samples, no recordings, no third-party or Riot audio, no voices. The DSP helpers come from
our flash_sfx.py (same folder). The factory clips in assets/audio/weapons are only READ, for their timing and loudness.
Output is deterministic (fixed seeds per file), so re-running reproduces the same files bit for bit.

Files per line (SkinAudio.cs loads them as skin.<line>.<name>):
  fire_<class>.wav     class = sidearm / smg / suppressed / shotgun / rifle / sniper / heavy (gun table below)
  reload_<key>.wav     key = rifle / pistol / shotgun / revolver / lmg (the factory reload keys)
  bolt.wav, pump.wav   after-shot bolt (Operator, Marshal's lever) / pump (Bucky)
  equip.wav            equip flourish (the factory equip is silent)
  k1..k5.wav           escalating kill sounds that REPLACE the factory kill1..kill5 (lines with KillSounds)
  kill.wav             the kill effect's sound;  fin.wav / fin_lite.wav  the finisher's (full / lite)
  counter.wav          Phosphor Trace's kill-counter relay click
Calibrated (Exclusive) has only kill / fin / fin_lite.

The rules (Riot's public skin-audio rules made measurable; checked on the written files, worst case over every
factory clip, variant and channel the layer can play with):
  layers (fire, bolt)  the factory sound always plays unchanged; the layer is silent until >= 9 ms after the factory
                       onset (first sample over 30 % of its peak), even at the +2.5 % shot pitch; peak >= 12 dB under
                       the factory peak (18 dB for suppressed guns); factory+layer within +0.5 dB of factory alone in
                       RMS over the first 100 ms after the onset, in K-weighted loudness (BS.1770 filter, ungated, the
                       whole clip) and in peak; no longer than the factory clip and -40 dB by 60 % of it; automatic
                       guns (Def.Auto, >= 8 rounds/s) at most 60 ms even at the -2.5 % pitch; high-passed at 60 Hz
  reload               exactly the factory clip's length; every layer event starts 0-40 ms after a factory transient
                       (mag-out / mag-in, found by onset detection); peak >= 12 dB under; +0.5 dB loudness cap
  equip                <= 0.6 s, peak >= 12 dB under the Vandal's shot (both play at the shot gain)
  k1..k5               onset <= 2 ms, length within 10 % and RMS within 1 dB of factory kill1..5, pitch rising 1 -> 5
  kill / fin / lite    <= 0.5 s / 2.0 s / 1.0 s, peak >= 10 dB under factory kill5 (counter click: >= 23.5 dB)
  every file           no clipping, no two files share the same audio
The factory kill1..5 and bolt are synthesized in game (Sfx.cs); this script re-implements them (same recipe, own noise)
or reads the game's own render from --factory DIR (made by the game's dev flag --dump-kills DIR).

Usage:
    py -I skin_sfx.py                       # render + check  (project found next to tools/, or pass --project DIR)
    py -I skin_sfx.py --only honeycomb,phosphor
    py -I skin_sfx.py --check               # measure the files already on disk, write nothing (CI)
    py -I skin_sfx.py --review DIR          # also write listing.txt + spectrogram sheets (layer and factory+layer)
    py -I skin_sfx.py --factory DIR         # use the game's kill1..5.wav / bolt.wav from --dump-kills DIR
"""
from __future__ import annotations

import argparse
import hashlib
import os
import sys
import wave
import zlib

import numpy as np
from scipy import signal

HERE = os.path.dirname(os.path.abspath(__file__))


def _helpers_dir() -> str:
    if "--helpers" in sys.argv:
        i = sys.argv.index("--helpers")
        if i + 1 < len(sys.argv):
            return os.path.abspath(sys.argv[i + 1])
    return HERE


sys.path.insert(0, _helpers_dir())  # py -I leaves the script folder off sys.path; flash_sfx.py lives next to us
from flash_sfx import (SR, T, white, pink, saw, square, sweep, env, fades, sat, norm, lp, hp, bp, svf,  # noqa: E402
                       resonators, impulses, bubble, bubbles, modal, reverb, place, phase, smooth)

# ----------------------------------------------------------------------------------------------- tables (from the game)

# Gun -> fire class (SkinAudio.cs ClassOf). Frenzy is a machine pistol: it shares the SMG layer, so the sidearm layer
# (semi-auto pistols) may ring longer than the 60 ms auto cap.
CLASSES = {
    "sidearm": ["classic", "bandit", "sheriff", "headhunter"],
    "smg": ["frenzy", "stinger"],
    "suppressed": ["ghost", "spectre"],
    "shotgun": ["shorty", "bucky", "judge"],
    "rifle": ["bulldog", "guardian", "phantom", "vandal", "warden"],
    "sniper": ["marshal", "outlaw", "operator"],          # + Tour De Force (Operator clip; Chamber's guns aren't skinnable)
    "heavy": ["ares", "odin"],
}
AUTO = {"frenzy", "stinger", "spectre", "bulldog", "phantom", "vandal", "ares", "odin"}   # Core/Weapons.cs: Auto && Rps >= 8
SUPPRESSED = {"ghost", "spectre"}
SHOT_FILES = {"classic": "pistol_shot", "vandal": "rifle_ak_shot", "phantom": "rifle_ar_shot", "operator": "sniper_bolt_shot"}
RELOADS = {"rifle": "rifle_reload_airsoft.wav", "pistol": "pistol_reload.wav", "shotgun": "shotgun_reload.wav",
           "revolver": "revolver_reload.wav", "lmg": "lmg_reload.wav"}
BOLTS = {"bolt": None, "pump": "shotgun_pump.wav"}     # None = synthesized in game (Sfx.cs "bolt"; Marshal's "lever" falls back to it)
KILL_SEMIS = [0, 2, 4, 7, 12]                          # Sfx.cs: kill1..5 = rising C-major arpeggio on 660 Hz
FULL_LINES = ["honeycomb", "rainfront", "strata", "escapement", "phosphor"]
LINES = FULL_LINES + ["calibrated"]
KILL_LINES = {"honeycomb", "rainfront", "strata", "escapement", "phosphor"}
SHOT_PITCH_JITTER = 0.025                              # GameSession: 1 +- 2.5 %

# ----------------------------------------------------------------------------------------------- I/O


def read_wav(path: str) -> tuple[int, np.ndarray]:
    """(rate, float array [channels, samples])."""
    with wave.open(path, "rb") as w:
        sr, ch, sw, n = w.getframerate(), w.getnchannels(), w.getsampwidth(), w.getnframes()
        raw = w.readframes(n)
    assert sw == 2, path
    x = np.frombuffer(raw, dtype="<i2").astype(float) / 32768.0
    return sr, x.reshape(-1, ch).T.copy()


def write_wav(path: str, x: np.ndarray) -> None:
    pcm = np.round(np.clip(x, -1, 32767 / 32768) * 32768).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def quantize(x: np.ndarray) -> np.ndarray:
    return np.round(np.clip(x, -1, 32767 / 32768) * 32768) / 32768.0


def to_rate(x: np.ndarray, sr_from: int, sr_to: int) -> np.ndarray:
    if sr_from == sr_to:
        return x
    g = np.gcd(sr_from, sr_to)
    return signal.resample_poly(x, sr_to // g, sr_from // g)


def seed(*parts) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32("/".join(map(str, parts)).encode()))


def db(v: float) -> float:
    return 20 * np.log10(max(v, 1e-12))


def rms(x: np.ndarray) -> float:
    return float(np.sqrt(np.mean(x ** 2))) if len(x) else 0.0


def peak(x: np.ndarray) -> float:
    return float(np.max(np.abs(x))) if len(x) else 0.0


def onset30(x: np.ndarray) -> int:
    """gun_sfx.py's onset: first sample over 30 % of the clip's peak."""
    return int(np.argmax(np.abs(x) >= 0.3 * peak(x)))


def first_nonzero(x: np.ndarray) -> int:
    nz = np.nonzero(np.abs(x) >= 0.5 / 32768)[0]
    return int(nz[0]) if len(nz) else len(x)


def _biquad_k(sr: int):
    """ITU-R BS.1770 K-weighting (high shelf +4 dB @1.5 kHz, high-pass 38 Hz) for any rate (RBJ forms)."""
    A, w0 = 10 ** (4.0 / 40), 2 * np.pi * 1500 / sr
    al = np.sin(w0) / (2 / np.sqrt(2))
    c, sA = np.cos(w0), np.sqrt(A)
    b1 = [A * ((A + 1) + (A - 1) * c + 2 * sA * al), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - 2 * sA * al)]
    a1 = [(A + 1) - (A - 1) * c + 2 * sA * al, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - 2 * sA * al]
    w0 = 2 * np.pi * 38 / sr
    al, c = np.sin(w0) / (2 * 0.5), np.cos(w0)
    b2 = [(1 + c) / 2, -(1 + c), (1 + c) / 2]
    a2 = [1 + al, -2 * c, 1 - al]
    return (np.array(b1) / a1[0], np.array(a1) / a1[0]), (np.array(b2) / a2[0], np.array(a2) / a2[0])


def kloud(x: np.ndarray, sr: int) -> float:
    """K-weighted loudness of the whole clip, ungated (LUFS-like; short clips are shorter than BS.1770's blocks)."""
    (b1, a1), (b2, a2) = _biquad_k(sr)
    y = signal.lfilter(b2, a2, signal.lfilter(b1, a1, x))
    return -0.691 + 10 * np.log10(np.mean(y ** 2) + 1e-20)


# ----------------------------------------------------------------------------------------------- factory sounds

class Factory:
    """Everything read from the game: shot clips per class, reloads, bolts, synthesized kills."""

    def __init__(self, project: str, synth_dir: str | None):
        self.wdir = os.path.join(project, "assets", "audio", "weapons")
        self.shots: dict[str, list[tuple[str, int, np.ndarray]]] = {}
        for cls, guns in CLASSES.items():
            lst = []
            for g in guns:
                stem = SHOT_FILES.get(g, g + "_shot")
                for i in range(1, 4):
                    p = os.path.join(self.wdir, f"{stem}_{i:02d}.wav")
                    if os.path.isfile(p):
                        sr, x = read_wav(p)
                        lst.append((f"{g}:{os.path.basename(p)}", sr, x))
            if not lst:
                raise SystemExit(f"no factory shots for class {cls} in {self.wdir}")
            self.shots[cls] = lst
        self.reloads = {k: read_wav(os.path.join(self.wdir, f)) for k, f in RELOADS.items()}
        self.synth_dir = synth_dir
        self.bolts = {}
        for k, f in BOLTS.items():
            if f:
                self.bolts[k] = read_wav(os.path.join(self.wdir, f))
            else:
                self.bolts[k] = self._synth("bolt", factory_bolt)
        self.kills = [self._synth(f"kill{k}", lambda k=k: factory_kill(k)) for k in range(1, 6)]
        vand = [x for name, sr, x in self.shots["rifle"] if name.startswith("vandal:")]
        self.vandal_peak = min(peak(c) for x in vand for c in x)

    def _synth(self, name, fn):
        if self.synth_dir:
            p = os.path.join(self.synth_dir, name + ".wav")
            if os.path.isfile(p):
                return read_wav(p)
        return SR, fn()[None, :]

    def shot_info(self, cls: str) -> dict:
        lst = self.shots[cls]
        guns = CLASSES[cls]
        return {
            "peak": min(peak(c) for _, _, x in lst for c in x),
            "onset": max(onset30(c) / sr for _, sr, x in lst for c in x),
            "len": min(x.shape[1] / sr for _, sr, x in lst),
            "auto": any(g in AUTO for g in guns),
            "suppressed": all(g in SUPPRESSED for g in guns),
        }


def _env_cs(t, atk, tau):
    """Sfx.cs Env()."""
    return np.where(t < 0, 0.0, np.where(t < atk, t / max(atk, 1e-9), np.exp(-(t - atk) / tau)))


def _onepole_hp(x, fc):
    a = 1 - np.exp(-2 * np.pi * fc / SR)
    return x - signal.lfilter([a], [1, -(1 - a)], x)


def _sfx_add(seconds, f):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    fade = np.minimum(1.0, (n - np.arange(n)) / 300.0)
    return np.clip(f(t) * fade, -1, 1)


def factory_kill(k: int) -> np.ndarray:
    """Sfx.cs kill1..5 (same recipe; the noise term uses our own generator)."""
    f = 660 * 2 ** (KILL_SEMIS[k - 1] / 12)
    ace = k == 5
    r = np.random.default_rng(7000 + k)

    def fn(t):
        S = lambda hz: np.sin(2 * np.pi * hz * t)
        s = (S(f) + 0.4 * S(2 * f) + 0.15 * S(3 * f)) * _env_cs(t, 0.002, 0.15) * 0.45 \
            + _onepole_hp(r.uniform(-1, 1, len(t)), 6000) * _env_cs(t, 0, 0.06) * 0.08 + S(90) * _env_cs(t, 0, 0.04) * 0.35
        if ace:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                s = s + S(f * m) * _env_cs(t - 0.04 * i, 0.002, 0.3) * 0.25
        return s
    return _sfx_add(0.8 if ace else 0.45, fn)


def factory_bolt() -> np.ndarray:
    r = np.random.default_rng(7100)

    def fn(t):
        nz = r.uniform(-1, 1, len(t))
        return (bp(nz, 2500, 3) * _env_cs(t, 0, 0.006) * 0.5 + bp(nz, 700, 2) * _env_cs(t, 0, 0.015) * 0.3) * 1.4
    return _sfx_add(0.2, fn)


# ----------------------------------------------------------------------------------------------- onset detection

def events_of(x: np.ndarray, sr: int, rel_db: float = 24.0, keep: int = 4, merge: float = 0.07) -> list[float]:
    """Transient times (s): 5 ms RMS frames rising >= 6 dB over the previous 30 ms and within rel_db of the loudest
    frame; candidates closer than `merge` chain into one event (its first frame); the `keep` loudest events, by time.
    Each time is refined to the first sample over 30 % of the next 30 ms's peak."""
    hop = max(1, int(0.005 * sr))
    nf = len(x) // hop
    e = np.array([db(rms(x[i * hop:(i + 1) * hop])) for i in range(nf)])
    top = e.max()
    cands = []
    for i in range(nf):
        before = np.max(e[max(0, i - 6):i]) if i > 0 else -120.0       # a clip that starts on its transient counts
        if e[i] - before >= 6 and e[i] > top - rel_db:
            cands.append(i)
    groups: list[list[int]] = []
    for i in cands:
        if groups and (i - groups[-1][-1]) * hop / sr <= merge:
            groups[-1].append(i)
        else:
            groups.append([i])
    scored = []
    for g in groups:
        a = max(0, (g[0] - 1) * hop)
        b = min(len(x), g[-1] * hop + int(0.03 * sr))
        seg = x[a:b]
        loud = np.max(e[g[0]:min(nf, g[-1] + 6)])
        refined = a + int(np.argmax(np.abs(seg) >= 0.3 * peak(seg)))
        scored.append((loud, refined / sr))
    scored.sort(reverse=True)
    return sorted(t for _, t in scored[:keep])


# ----------------------------------------------------------------------------------------------- small instruments

def buf(dur: float) -> np.ndarray:
    return np.zeros(int(round(dur * SR)))


def put(out: np.ndarray, t0: float, grain: np.ndarray, gain: float = 1.0) -> None:
    place(out, int(round(t0 * SR)), gain * grain)


def noise_click(r, dur=0.008, fc=3000.0, q=2.0, tau=0.0015):
    t = T(dur)
    return bp(white(len(t), r), fc, q) * env(t, 0.0002, tau)


def fm_bell(dur, fc, ratio, index, tau, itau=None, atk=0.001):
    t = T(dur)
    i_env = index * np.exp(-t / (itau or tau * 0.4))
    return np.sin(2 * np.pi * fc * t + i_env * np.sin(2 * np.pi * fc * ratio * t)) * env(t, atk, tau)


def brown(n, r):
    y = np.cumsum(r.standard_normal(n))
    y = hp(y, 20, order=2)
    return y / (peak(y) + 1e-12)


def thump(dur=0.06, f=90.0, tau=0.04):
    t = T(dur)
    return np.sin(2 * np.pi * f * t) * env(t, 0.0005, tau)


def bl_square(f, dur, lp_hz=6000):
    t = T(dur)
    return lp(square(f, len(t), max_h=25), lp_hz)


# ----------------------------------------------------------------------------------------------- the lines
# Each line: fire(cls, dur, r) -> voice starting at its own t=0 (the framework places it after the factory onset);
# reload(key, n, events, r) -> a whole clip of n samples with sounds at the factory's transients (also used for the
# bolt / pump keys); equip, killk(k, f, dur), kill, fin, fin_lite (+ counter). Levels are set by the framework.

CLASS_SCALE = {"sniper": 0.85, "shotgun": 0.9, "heavy": 0.92, "rifle": 1.0, "sidearm": 1.05, "smg": 1.1, "suppressed": 1.12}


class Lattice:
    """Lattice (honeycomb): hex / crystal geometry with warm light in the walls - glass ticks and bells."""
    fire_db, bolt_db, reload_db = -16.0, -16.0, -15.0

    def fire(self, cls, dur, r):
        t = T(dur)
        s = CLASS_SCALE[cls]
        d = 0.009 if dur < 0.06 else 0.025
        return modal(t, [3100 * s, 4700 * s, 6230 * s], [d, d * 0.8, d * 0.5], [1.0, 0.7, 0.25], r=r)

    def clack(self, r, f=1200.0):
        t = T(0.08)
        body = bp(noise_click(r, 0.08, f * 2, 2.0, 0.002), f, 8) * 3
        glass = modal(t, [f * 2.76, f * 4.1], [0.02, 0.012], [0.35, 0.15], r=r)
        return body + glass

    def reload(self, key, n, events, r):
        out = np.zeros(n)
        if key in BOLTS:
            for i, t0 in enumerate(events):
                put(out, t0, modal(T(0.06), [2600, 3900], [0.018, 0.012], [1, 0.5], r=r))
            return out
        for i, t0 in enumerate(events):
            last = i == len(events) - 1
            f = 1200 if i == 0 else 1500 if last else 1350
            put(out, t0, self.clack(r, f), 1.0 if (i == 0 or last) else 0.6)
        a, b = events[0], min(n / SR - 0.01, events[-1] + 0.25)
        t = np.arange(n) / SR
        hum = lp(saw(sweep(110, 220, t, max(b - a, 0.05), a), n, 30), 800)
        shape = np.clip((t - a) / 0.06, 0, 1) * np.clip((b - t) / 0.12, 0, 1)
        return out + 0.1 * hum * shape

    def equip(self, r):
        out = buf(0.35)
        for i, f in enumerate([1318.5, 1661.2, 1975.5]):       # E6, G#6, B6
            put(out, 0.09 * i, modal(T(0.16), [f, f * 2.76, f * 5.4], [0.05, 0.025, 0.012], [1, 0.4, 0.15], r=r))
        return out

    def killk(self, k, f, dur, r):
        t = T(dur)
        s = modal(t, [f, 2.76 * f, 5.40 * f, 8.93 * f], [0.18, 0.09, 0.05, 0.03], [1, 0.45, 0.2, 0.08], atk=0.001)
        s += 0.5 * thump(dur, 90, 0.04)
        if k == 5:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                s += 0.5 * modal(t, [f * m, 2.76 * f * m], [0.3, 0.1], [1, 0.3], start=0.04 * i, atk=0.002)
        return s

    def kill(self, r):
        t = T(0.4)
        out = np.zeros(len(t))
        for i in range(5):
            f = np.exp(r.uniform(np.log(3000), np.log(7000)))
            out += modal(t, [f, f * 1.53], [r.uniform(0.05, 0.1), 0.03], [r.uniform(0.6, 1.0), 0.3],
                         start=0.025 * i + r.uniform(0, 0.012), r=r)
        return reverb(out, 0.18, 0.4)

    def _swell(self, dur, root, r):
        t = T(dur)
        out = np.zeros(len(t))
        for f, a in [(root, 1.0), (root * 1.5, 0.8), (root * 2, 0.55)]:
            idx = 0.6 + 1.6 * smooth(t / dur)
            out += a * np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * 1.4 * t))
        return out * smooth(t / dur) ** 2 * (1 + 0.15 * np.sin(2 * np.pi * 5.5 * t))

    def _shatter(self, dur, r, n=9):
        t = T(dur)
        out = 0.5 * hp(white(len(t), r), 3000) * env(t, 0.001, 0.05)
        for i in range(n):
            f = np.exp(r.uniform(np.log(2500), np.log(8000)))
            out += modal(t, [f], [r.uniform(0.04, 0.12)], [r.uniform(0.3, 0.8)], start=r.uniform(0, dur * 0.5), r=r)
        return out

    def fin(self, r):
        out = buf(1.6)
        put(out, 0, self._swell(1.08, 330, r))
        put(out, 1.05, self._shatter(0.55, r), 1.1)
        return fades(reverb(out, 0.2, 0.7), 0.002, 0.05)

    def fin_lite(self, r):
        out = buf(1.0)
        put(out, 0, self._swell(0.62, 330, r) * 0.9)
        put(out, 0.6, self._shatter(0.4, r, 5), 0.9)
        return fades(reverb(out, 0.15, 0.5), 0.002, 0.04)


class Rainfront:
    """Rainfront: rain beading on a matte gun - wet spritz, droplet bloops, rain hiss."""
    fire_db, bolt_db, reload_db = -18.0, -17.0, -15.0

    def fire(self, cls, dur, r):
        t = T(dur)
        s = CLASS_SCALE[cls]
        hiss = lp(hp(white(len(t), r), 4000 * s, order=4), 8000 * s, order=4) * env(t, 0.002, min(0.012, dur / 3))
        drop = bubble(2200 * s, dur, 0.3, 0.008)
        return hiss + 0.25 * drop[:len(t)]

    def reload(self, key, n, events, r):
        out = np.zeros(n)
        for i, t0 in enumerate(events):
            last = i == len(events) - 1
            if key in BOLTS:
                put(out, t0, bubble(1500 + 200 * i, 0.08, 0.4, 0.015))
                continue
            f0 = 900 if i == 0 else 1300 if last else 1100
            put(out, t0, bubble(f0, 0.16, 0.6, 0.03))
            if last:
                put(out, t0 + 0.04, bubble(f0 * 1.45, 0.1, 0.5, 0.018), 0.5)
        return out

    def equip(self, r):
        t = T(0.5)
        swell = lp(pink(len(t), r), 3000) * smooth(t / 0.28) * np.clip((0.5 - t) / 0.2, 0, 1)
        out = 0.6 * swell
        for i in range(6):
            put(out, 0.05 + 0.07 * i + r.uniform(0, 0.03), bubble(r.uniform(2000, 4000), 0.06, 0.4, 0.01), 0.35)
        return out

    def killk(self, k, f, dur, r):
        out = buf(dur)
        put(out, 0, bubble(f, dur, 0.25, 0.07, atk=0.0008))
        put(out, 0.07, bubble(f * 1.5, 0.3, 0.3, 0.035), 0.35)
        put(out, 0, thump(0.08, 90, 0.04), 0.5)
        if k == 5:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                put(out, 0.05 * i, bubble(f * m, 0.5, 0.2, 0.09), 0.5)
        return out

    def kill(self, r):
        t = T(0.35)
        n = len(t)
        burst = bubbles(t, 900 * np.exp(-t / 0.05), r, 600, 2500)
        splash = bp(white(n, r), 2500, 0.7) * env(t, 0.002, 0.05)
        drops = bubbles(t, 60 * (t > 0.08), r, 1500, 3500, decay=(0.01, 0.025))
        return 0.9 * burst + 0.8 * splash + 0.4 * drops

    def fin(self, r):
        t = T(1.8)
        n = len(t)
        hiss = lp(hp(pink(n, r), 1500), 9000) * smooth(t / 0.6) * np.clip((1.8 - t) / 0.6, 0, 1)
        drops = resonators(impulses(t, 120 * smooth(t / 0.4), r), [2200, 3300, 4700], 25) * 0.6
        roll = lp(hp(brown(n, r), 35), 120) * np.exp(-((t - 1.0) / 0.4) ** 2)
        return fades(0.8 * hiss + drops * np.clip((1.7 - t) / 0.4, 0, 1) + 0.2 * roll, 0.002, 0.05)

    def fin_lite(self, r):
        t = T(1.0)
        n = len(t)
        drips = bubbles(t, 25 * np.clip((0.85 - t) / 0.3, 0, 1), r, 900, 2600, decay=(0.015, 0.04))
        hiss = lp(hp(pink(n, r), 2000), 8000) * smooth(t / 0.2) * np.clip((1.0 - t) / 0.5, 0, 1)
        return fades(drips + 0.45 * hiss, 0.002, 0.04)


class Strata:
    """Strata: sedimentary rock - stone thocks, grinding slides, pebble rattles, rumble and crumble."""
    fire_db, bolt_db, reload_db = -14.0, -15.0, -14.0
    BODY = {"sniper": 150, "shotgun": 160, "heavy": 170, "rifle": 180, "sidearm": 210, "smg": 230, "suppressed": 240}

    def fire(self, cls, dur, r):
        t = T(dur)
        n = len(t)
        f = self.BODY[cls]
        exc = white(n, r) * env(t, 0.0003, 0.002)
        thock = bp(exc, f, 4) * 8 * env(t, 0.001, min(0.04, dur / 2.5))
        grit = bp(impulses(t, 3000 * np.exp(-t / 0.01), r), 2500, 1.5) * 1.5 * (t < 0.03)
        return thock + grit

    def clack(self, r, scale=1.0, dur=0.12):
        t = T(dur)
        return modal(t, [400 * scale, 950 * scale, 1700 * scale], [0.05, 0.03, 0.02], [1, 0.6, 0.35], r=r) \
            + 0.4 * noise_click(r, dur, 2500, 1.2, 0.002)

    def reload(self, key, n, events, r):
        out = np.zeros(n)
        for i, t0 in enumerate(events):
            last = i == len(events) - 1
            if key in BOLTS:
                put(out, t0, self.clack(r, 1.3, 0.06), 0.8)
            elif i == 0 and len(events) > 1:
                dur = min(0.3, max(0.08, events[1] - t0 - 0.02))
                tt = T(dur)
                g = svf(brown(len(tt), r), sweep(400, 900, tt, dur), 2.0, "bp") * env(tt, 0.02, dur / 2)
                put(out, t0, g)
                put(out, t0, self.clack(r, 0.8, 0.05), 0.5)
            else:
                put(out, t0, self.clack(r, 1.0 if last else 1.15), 1.0 if last else 0.6)
        return out

    def equip(self, r):
        t = T(0.45)
        imp = impulses(t, 300 * np.exp(-t / 0.15), r, power=1.5)
        return resonators(imp, [1100, 1800, 2700], 12) * 3

    def killk(self, k, f, dur, r):
        t = T(dur)
        out = np.zeros(len(t))
        for j in range(k):
            out += modal(t, [f, 2.3 * f, 3.9 * f], [0.05, 0.03, 0.015], [1, 0.4, 0.2], start=0.055 * j, atk=0.0008)
            put(out, 0.055 * j, noise_click(r, 0.02, 2.5 * f, 1.5, 0.002), 0.35)
        out += 0.5 * thump(dur, 90, 0.04)
        if k == 5:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                out += 0.4 * modal(t, [f * m], [0.22], [1], start=0.3 + 0.04 * i, atk=0.002)
        return out

    def kill(self, r):
        t = T(0.45)
        n = len(t)
        crack = hp(white(n, r), 2000) * env(t, 0.0002, 0.004) * 1.5
        body = modal(t, [400, 950, 1700], [0.06, 0.035, 0.02], [1, 0.6, 0.4])
        pebbles = resonators(impulses(t, 220 * np.exp(-(t - 0.05) / 0.12) * (t > 0.05), r), [1500, 2200, 3100], 15) * 2
        return crack + body + pebbles

    def fin(self, r):
        t = T(1.95)
        n = len(t)
        rumble = hp(lp(white(n, r), 60, order=4), 35, order=4)
        rumble = sat(rumble / (peak(rumble) + 1e-9) * 2, 1.5) * smooth(t / 0.4) * np.clip((1.3 - t) / 0.5, 0, 1)
        crumble = resonators(impulses(t, 260 * np.clip((t - 0.85) / 0.15, 0, 1) * np.clip((1.9 - t) / 0.6, 0, 1), r),
                             [320, 700, 1300, 2400], 9) * 2.5
        dust = lp(pink(n, r), 2500) * np.clip((t - 0.9) / 0.2, 0, 1) * np.clip((1.95 - t) / 0.7, 0, 1) * 0.25
        return fades(0.5 * rumble + crumble + dust, 0.002, 0.05)

    def fin_lite(self, r):
        t = T(1.0)
        n = len(t)
        crack = self.kill(r)
        crumble = resonators(impulses(t, 150 * np.clip((0.9 - t) / 0.5, 0, 1), r), [500, 1100, 2200], 10) * 2
        dust = lp(pink(n, r), 2500) * smooth(t / 0.1) * np.clip((1.0 - t) / 0.6, 0, 1) * 0.2
        out = crumble + dust
        put(out, 0, crack, 0.8)
        return fades(out, 0.002, 0.04)


class Escapement:
    """Escapement: watchmaker's steel - tinks, ticks, ratchets, the mainspring's zing and a small bell."""
    fire_db, bolt_db, reload_db = -18.0, -16.0, -15.0

    def fire(self, cls, dur, r):
        t = T(dur)
        s = CLASS_SCALE[cls]
        return modal(t, [5200 * s, 7900 * s, 11300 * min(s, 1.0)], [0.015, 0.01, 0.006], [1, 0.6, 0.2], r=r)

    def tick(self, r, f=4000.0, dur=0.03, body=2200.0):
        t = T(dur)
        return noise_click(r, dur, f, 4, 0.0012) * 2 + modal(t, [body], [0.006], [0.4], r=r)

    def reload(self, key, n, events, r):
        out = np.zeros(n)
        end = n / SR
        for i, t0 in enumerate(events):
            last = i == len(events) - 1
            if key in BOLTS:
                put(out, t0, self.tick(r, 4200, 0.03, 2400))
            elif last:
                dur = max(0.05, min(0.4, end - t0 - 0.02))
                train = buf(dur)
                for j in range(int(dur * 25)):
                    put(train, j / 25, self.tick(r, 3000, 0.012, 1800), 0.9 * np.exp(-j / 12))
                put(out, t0, train * np.clip((dur - T(dur)) / 0.03, 0, 1))
            elif i == 0:
                tt = T(0.16)
                zing = (np.sin(phase(sweep(1800, 4200, tt, 0.12))) + 0.3 * np.sin(phase(sweep(2900, 6800, tt, 0.12)))) \
                    * env(tt, 0.002, 0.05)
                put(out, t0, zing)
                put(out, t0, self.tick(r, 3500, 0.03, 2000), 0.6)
            else:
                put(out, t0, self.tick(r), 0.6)
        return out

    def equip(self, r):
        out = buf(0.45)
        put(out, 0.0, self.tick(r, 4500, 0.04, 3100))
        put(out, 0.22, self.tick(r, 2200, 0.05, 1600), 0.9)
        return out

    def killk(self, k, f, dur, r):
        out = buf(dur)
        for j in range(k):
            put(out, 0.045 * j, self.tick(r, 4000 + 300 * j, 0.03, 2200), 0.5)
        tb = 0.045 * k
        tau = 0.35 if k == 5 else 0.2
        put(out, tb, fm_bell(dur - tb, f, 3.5, 2.2, tau, 0.05))
        if k == 5:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                put(out, tb + 0.04 * i, fm_bell(dur - tb - 0.04 * i, f * m, 3.5, 1.2, 0.3, 0.04), 0.45)
        return out

    def kill(self, r):
        out = buf(0.5)
        put(out, 0.0, self.tick(r, 4200, 0.03, 2400))
        put(out, 0.06, self.tick(r, 3600, 0.03, 2000), 0.9)
        put(out, 0.12, fm_bell(0.38, 1800, 3.5, 1.8, 0.12, 0.04), 1.2)
        return out

    def _accel(self, until, r, a=6.0, b=10.0):
        out = buf(until + 0.05)
        j, tt = 0, 0.0
        while tt < until:
            put(out, tt, self.tick(r, 4200 if j % 2 == 0 else 3000, 0.03, 2400 if j % 2 == 0 else 1700), 0.6 + 0.4 * tt / until)
            j += 1
            tt = (-a + np.sqrt(a * a + 4 * b * j)) / (2 * b)      # phase a t + b t^2 = j
        return out

    def fin(self, r):
        out = buf(1.95)
        put(out, 0, self._accel(0.72, r))
        bell = fm_bell(1.2, 220, 3.5, 3.0, 0.4, 0.12) + 0.4 * fm_bell(1.2, 440, 2.0, 1.0, 0.3, 0.1)
        put(out, 0.75, bell, 1.6)
        return fades(reverb(out, 0.15, 0.6), 0.002, 0.05)

    def fin_lite(self, r):
        out = buf(1.0)
        put(out, 0, self._accel(0.55, r, 7, 14))
        put(out, 0.6, fm_bell(0.4, 440, 3.5, 2.0, 0.15, 0.06), 1.3)
        return fades(out, 0.002, 0.04)


class Phosphor:
    """Phosphor Trace: lab oscilloscope / CRT phosphor - blips, relay clicks, power-on thunk, whine-down."""
    fire_db, bolt_db, reload_db = -16.0, -16.0, -15.0
    BLIP = {"sniper": 1800, "shotgun": 2000, "heavy": 2200, "rifle": 2400, "sidearm": 2600, "smg": 2700, "suppressed": 2900}

    def fire(self, cls, dur, r):
        d = min(dur, 0.012)
        t = T(d)
        blip = bl_square(self.BLIP[cls], d) * np.clip(t / 0.0005, 0, 1) * np.clip((d - t) / 0.0025, 0, 1)
        out = np.zeros(int(round(dur * SR)))
        put(out, 0, blip)
        return out

    def relay(self, r, scale=1.0):
        out = buf(0.03)
        put(out, 0.0, noise_click(r, 0.02, 3500 * scale, 3, 0.001) * 2 + thump(0.02, 1200 * scale, 0.003) * 0.4)
        put(out, 0.004, noise_click(r, 0.02, 2800 * scale, 3, 0.001), 1.2)
        return out

    def beep(self, f, dur, atk=0.002):
        t = T(dur)
        return (np.sin(2 * np.pi * f * t) + 0.2 * np.sin(4 * np.pi * f * t)) * np.clip(t / atk, 0, 1) * np.clip((dur - t) / 0.004, 0, 1)

    def reload(self, key, n, events, r):
        out = np.zeros(n)
        for i, t0 in enumerate(events):
            last = i == len(events) - 1
            if key in BOLTS:
                put(out, t0, self.relay(r, 1.1), 0.9)
                continue
            put(out, t0, self.relay(r, 1.0 if (i == 0 or last) else 1.2), 1.0 if (i == 0 or last) else 0.6)
            if last:
                for j, f in enumerate([1000, 1500, 2250]):           # the "seek": rising fifths, one gesture
                    put(out, t0 + 0.02 + 0.022 * j, self.beep(f, 0.02), 0.35)
        return out

    def equip(self, r):
        t = T(0.42)
        n = len(t)
        thunk = (np.sin(2 * np.pi * 60 * t) + 0.5 * lp(saw(120, n, 20), 900)) * env(t, 0.003, 0.08) * (t < 0.25)
        whine = np.sin(phase(sweep(1000, 6000, t, 0.3, 0.05))) * smooth((t - 0.05) / 0.05) * np.clip((0.42 - t) / 0.08, 0, 1)
        return thunk + 0.08 * whine * (t > 0.05)

    def killk(self, k, f, dur, r):
        out = buf(dur)
        beep_len = 0.11
        t = T(dur)
        sq = bl_square(f, dur, 5000) * np.clip(t / 0.002, 0, 1) * np.where(t < beep_len, 1.0, np.exp(-(t - beep_len) / 0.05))
        out += 0.8 * sq
        bits = seed("phosphor-chirp", k).integers(0, 2, 12)
        for j, b in enumerate(bits):
            put(out, 0.12 + 0.006 * j, self.beep(3100 if b else 2200, 0.006, 0.0005), 0.18)
        put(out, 0, thump(0.08, 90, 0.04), 0.5)
        if k == 5:
            for i, m in enumerate([1.26, 1.5, 2.0], 1):
                tt = T(dur - 0.04 * i)
                put(out, 0.04 * i, bl_square(f * m, len(tt) / SR, 5000) * env(tt, 0.002, 0.3), 0.3)
        return out

    def kill(self, r):
        t = T(0.25)
        return (np.sin(2 * np.pi * 1000 * t) + 0.15 * np.sin(2 * np.pi * 2000 * t)) * smooth(t / 0.02) * np.clip((0.25 - t) / 0.08, 0, 1)

    def _down(self, dur, f0, sweep_dur, r):
        t = T(dur)
        n = len(t)
        whine = (np.sin(phase(sweep(f0, 200, t, sweep_dur))) * 0.8) * np.clip((sweep_dur + 0.05 - t) / 0.1, 0, 1)
        hum = lp(saw(50, n, 12), 400) * smooth((t - 0.05) / 0.1) * np.clip((dur - t) / (dur - 0.3), 0, 1) * 0.5
        static = lp(impulses(t, 40 * (t < 0.3), r), 4000) * 1.5
        return fades(whine + hum + static, 0.002, 0.04)

    def fin(self, r):
        return self._down(1.8, 8000, 0.6, r)

    def fin_lite(self, r):
        return self._down(0.8, 4000, 0.35, r)

    def counter(self, r):
        return self.relay(r, 1.3)[:int(0.02 * SR)]


class Calibrated:
    """Calibrated (Exclusive): the trainer's measuring instrument - measurement beeps, a scan shimmer, a stamp."""

    def kill(self, r):
        out = buf(0.25)
        t = T(0.06)
        b = np.sin(2 * np.pi * 2000 * t) * np.clip(t / 0.003, 0, 1) * np.clip((0.06 - t) / 0.006, 0, 1)
        put(out, 0.0, b)
        put(out, 0.14, b)
        return out

    def _shimmer(self, dur, r):
        t = T(dur)
        fc = sweep(600, 2400, t, dur * 0.85)
        x = np.sin(phase(fc) + 1.5 * np.sin(phase(fc * 2.01))) * smooth(t / (dur * 0.6)) * np.clip((dur - t) / 0.15, 0, 1)
        return x * (1 + 0.2 * np.sin(2 * np.pi * 9 * t))

    def fin(self, r):
        out = buf(1.8)
        put(out, 0, self._shimmer(0.85, r), 0.8)
        t = T(0.9)
        stamp = np.sin(phase(sweep(90, 55, t, 0.15))) * env(t, 0.001, 0.07) + lp(white(len(t), r), 700) * env(t, 0.001, 0.04) * 0.5
        put(out, 0.85, stamp + 0.3 * noise_click(r, 0.9, 1500, 1.5, 0.004))
        return fades(reverb(out, 0.12, 0.5), 0.002, 0.05)

    def fin_lite(self, r):
        return fades(self._shimmer(1.0, r), 0.002, 0.04)


RECIPES = {"honeycomb": Lattice(), "rainfront": Rainfront(), "strata": Strata(), "escapement": Escapement(),
           "phosphor": Phosphor(), "calibrated": Calibrated()}

# ----------------------------------------------------------------------------------------------- measuring

MARGIN = 0.1        # dB kept under each loudness limit when fitting gains


def layer_deltas(layer: np.ndarray, fac: list[tuple[str, int, np.ndarray]], first100: bool = True) -> dict:
    """Worst-case deltas of factory+layer vs factory over every clip and channel (layer at SR, factory any rate)."""
    worst = {"rms100": -99.0, "kloud": -99.0, "sumpeak": -99.0, "margin": 99.0, "gap_ms": 99.0, "tail": -99.0,
             "longer_ms": -99.0, "who": ""}
    lay_on = first_nonzero(layer) / SR
    for name, sr, x in fac:
        L = to_rate(layer, SR, sr)
        for c in x:
            n = len(c)
            Lc = np.zeros(n)
            m = min(n, len(L))
            Lc[:m] = L[:m]
            on = onset30(c)
            mix = c + Lc
            if first100:
                a, b = on, min(n, on + int(0.1 * sr))
                d100 = db(rms(mix[a:b])) - db(rms(c[a:b]))
            else:
                d100 = db(rms(mix)) - db(rms(c))
            dk = kloud(mix, sr) - kloud(c, sr)
            dp = db(peak(mix)) - db(peak(c))
            marg = db(peak(c)) - db(peak(layer))
            gap = (lay_on - on / sr) * 1000
            tail_at = int(0.6 * n)
            tail = db(peak(Lc[tail_at:])) - db(peak(layer)) if peak(layer) > 0 else -99
            longer = (len(layer) / SR - n / sr) * 1000
            for k, v, worse in [("rms100", d100, max), ("kloud", dk, max), ("sumpeak", dp, max), ("margin", marg, min),
                                ("gap_ms", gap, min), ("tail", tail, max), ("longer_ms", longer, max)]:
                nv = worse(worst[k], v)
                if nv != worst[k] and k in ("rms100", "kloud", "margin"):
                    worst["who"] = name
                worst[k] = nv
    return worst


def fit_gain(x: np.ndarray, fac, first100=True, limit=0.5) -> np.ndarray:
    for _ in range(80):
        w = layer_deltas(quantize(x), fac, first100)
        if max(w["rms100"], w["kloud"], w["sumpeak"]) <= limit - MARGIN:
            return x
        x = x * 10 ** (-0.25 / 20)
    return x


def low_energy_db(x: np.ndarray, fc: float = 45.0) -> float:
    """Energy below fc relative to the total (dB): the 60 Hz high-pass check."""
    X = np.abs(np.fft.rfft(x, n=max(len(x), 8192))) ** 2
    f = np.fft.rfftfreq(max(len(x), 8192), 1 / SR)
    return 10 * np.log10((X[f < fc].sum() + 1e-20) / (X.sum() + 1e-20))


def dom_freq(x: np.ndarray, lo=300.0, hi=4000.0) -> float:
    X = np.abs(np.fft.rfft(x, n=1 << 16))      # no window: these sounds decay, and a window would hide their attack
    f = np.fft.rfftfreq(1 << 16, 1 / SR)
    m = (f >= lo) & (f <= hi)
    return float(f[m][np.argmax(X[m])])


# ----------------------------------------------------------------------------------------------- rendering

def hp60(x: np.ndarray) -> np.ndarray:
    return signal.sosfilt(signal.butter(4, 60, "high", fs=SR, output="sos"), x)


def end_fade(x: np.ndarray, ms: float = 6.0) -> np.ndarray:
    x = x.copy()
    k = min(len(x), int(ms / 1000 * SR))
    if k > 0:
        x[-k:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, k))
    return x


def render_fire(line: str, cls: str, fac: Factory) -> np.ndarray:
    rec = RECIPES[line]
    info = fac.shot_info(cls)
    start = info["onset"] + 0.0105                                   # >= 9 ms after the latest onset, + pitch margin
    total = 0.0575 if info["auto"] else min(info["len"], 0.16 if cls not in ("sniper", "shotgun") else 0.2)
    total = min(total, 0.6 * info["len"])
    x = np.zeros(int(round(total * SR)))
    voice = rec.fire(cls, total - start, seed(line, "fire", cls))
    put(x, start, voice)
    x = end_fade(hp60(x), 8.0)
    x[:int(start * SR)] = 0.0
    target = db(info["peak"]) + rec.fire_db - (6.0 if info["suppressed"] else 0.0)
    x = norm(x, target)
    return fit_gain(x, fac.shots[cls])


def render_events(line: str, key: str, fac: Factory) -> np.ndarray:
    """Reload (key in RELOADS) or bolt / pump (key in BOLTS) layer: the factory clip's length, sounds at its transients."""
    rec = RECIPES[line]
    sr, x = fac.reloads[key] if key in RELOADS else fac.bolts[key]
    mono = x.mean(0)
    n = int(round(x.shape[1] / sr * SR))
    if key in BOLTS:
        g_on = min(onset30(c) for c in x) / sr
        evs = [t for t in events_of(mono, sr, rel_db=18, keep=2) if t >= g_on - 1e-9] or [g_on]
        # layer events: >= 9 ms after the latest per-channel onset of the event (fire rule), plus the pitch margin
        late_on = max(onset30(c) for c in x) / sr
        delays = [max(t, late_on) + 0.0105 for t in evs]
        lay = rec.reload(key, n, delays, seed(line, key))
        lay = hp60(lay)
        lay[:int(delays[0] * SR)] = 0.0
        db_rel = rec.bolt_db
    else:
        evs = events_of(mono, sr)
        lay = rec.reload(key, n, [t + 0.008 for t in evs], seed(line, key))
        lay = hp(lay, 40)
        db_rel = rec.reload_db
    lay = end_fade(lay, 10.0)
    lay = norm(lay, db(min(peak(c) for c in x)) + db_rel)
    # the game's bolt is noise-based (Sfx.cs): keep 0.2 dB more headroom than the re-implementation strictly needs
    return fit_gain(lay, [(key, sr, x)], first100=(key in BOLTS), limit=0.3 if key in BOLTS and BOLTS[key] is None else 0.5)


def match_kill(x: np.ndarray, ref: np.ndarray) -> np.ndarray:
    """Same length as the factory kill, RMS matched exactly, soft-limited under -0.5 dBFS if needed."""
    x = np.concatenate([x, np.zeros(max(0, len(ref) - len(x)))])[:len(ref)]
    x = end_fade(x, 300 / SR * 1000)
    target = rms(ref)
    for _ in range(8):
        x = x * target / (rms(x) + 1e-12)
        if peak(x) <= 0.94:
            break
        x = np.tanh(x * 1.1) / 1.1
    return x * target / (rms(x) + 1e-12)


def render_line(line: str, fac: Factory) -> dict[str, np.ndarray]:
    rec = RECIPES[line]
    out: dict[str, np.ndarray] = {}
    k5_peak = db(peak(fac.kills[4][1][0]))
    if line in FULL_LINES:
        for cls in CLASSES:
            out[f"fire_{cls}"] = render_fire(line, cls, fac)
        for key in RELOADS:
            out[f"reload_{key}"] = render_events(line, key, fac)
        for key in BOLTS:
            out[key] = render_events(line, key, fac)
        out["equip"] = norm(fades(rec.equip(seed(line, "equip")), 0.001, 0.01), db(fac.vandal_peak) - 14.0)
    if line in KILL_LINES:
        for k in range(1, 6):
            ref = fac.kills[k - 1][1][0]
            f = 660 * 2 ** (KILL_SEMIS[k - 1] / 12)
            out[f"k{k}"] = match_kill(rec.killk(k, f, len(ref) / SR, seed(line, "k", k)), ref)
    out["kill"] = norm(fades(rec.kill(seed(line, "kill")), 0.001, 0.01), k5_peak - 11.0)
    out["fin"] = norm(rec.fin(seed(line, "fin")), k5_peak - 11.0)
    out["fin_lite"] = norm(rec.fin_lite(seed(line, "fin_lite")), k5_peak - 12.0)
    if hasattr(rec, "counter"):
        out["counter"] = norm(fades(rec.counter(seed(line, "counter")), 0.0005, 0.004), k5_peak - 25.0)
    return out


# ----------------------------------------------------------------------------------------------- the check table

def check_line(line: str, files: dict[str, np.ndarray], fac: Factory) -> list[tuple[str, bool, str]]:
    rows = []
    k5_peak = db(peak(fac.kills[4][1][0]))

    def row(name, ok_list, text):
        fails = [why for ok, why in ok_list if not ok]
        rows.append((f"{line}/{name}", not fails, text + ("" if not fails else "  <- " + "; ".join(fails))))

    need = []
    if line in FULL_LINES:
        need += [f"fire_{c}" for c in CLASSES] + [f"reload_{k}" for k in RELOADS] + list(BOLTS) + ["equip"]
    if line in KILL_LINES:
        need += [f"k{k}" for k in range(1, 6)]
    need += ["kill", "fin", "fin_lite"] + (["counter"] if line == "phosphor" else [])
    for name in need:
        if name not in files:
            rows.append((f"{line}/{name}", False, "missing"))
    for name, x in files.items():
        clip = peak(x) >= 32767 / 32768 - 1e-9
        dur = len(x) / SR
        if name.startswith("fire_"):
            cls = name[5:]
            info = fac.shot_info(cls)
            w = layer_deltas(x, fac.shots[cls])
            need_marg = 18.0 if info["suppressed"] else 12.0
            gap_scaled = w["gap_ms"] / (1 + SHOT_PITCH_JITTER)
            dur_slow = dur / (1 - SHOT_PITCH_JITTER) * 1000
            lo = low_energy_db(x)
            checks = [(gap_scaled >= 9.0, f"onset {gap_scaled:.1f} ms < 9"), (w["margin"] >= need_marg, f"peak margin {w['margin']:.1f} < {need_marg:.0f}"),
                      (w["rms100"] <= 0.5, f"rms100 +{w['rms100']:.2f} dB"), (w["kloud"] <= 0.5, f"K-loud +{w['kloud']:.2f} dB"),
                      (w["sumpeak"] <= 0.5, f"peak +{w['sumpeak']:.2f} dB"), (w["longer_ms"] <= 0, "longer than factory"),
                      (w["tail"] <= -40, f"tail {w['tail']:.0f} dB at 60 %"), (lo <= -20, f"<45 Hz energy {lo:.0f} dB"),
                      (not info["auto"] or dur_slow <= 60.0, f"auto gun layer {dur_slow:.1f} ms > 60"), (not clip, "clips")]
            row(name, checks, f"{dur * 1000:5.1f} ms{' (auto)' if info['auto'] else ''}  onset +{gap_scaled:4.1f} ms  margin {w['margin']:4.1f} dB  "
                f"rms100 +{w['rms100']:.2f}  K +{w['kloud']:.2f}  pk +{w['sumpeak']:.2f}  [{len(fac.shots[cls])} clips]")
        elif name.startswith("reload_") or name in BOLTS:
            key = name[7:] if name.startswith("reload_") else name
            sr, fx = fac.reloads[key] if key in RELOADS else fac.bolts[key]
            fac_list = [(key, sr, fx)]
            w = layer_deltas(x, fac_list, first100=(key in BOLTS))
            fdur = fx.shape[1] / sr
            evs = events_of(fx.mean(0), sr, **({"rel_db": 18, "keep": 2} if key in BOLTS else {}))
            lev = events_of(x, SR, rel_db=30, keep=8, merge=0.15)    # a gesture (ratchet, seek beeps) counts once
            offs = [min((le - fe for fe in evs if le - fe >= -0.001), default=-1) * 1000 for le in lev]
            follow = all(0 <= o <= 40 for o in offs) and len(lev) > 0
            checks = [(abs(dur - fdur) <= 1.5 / SR, f"length {dur:.4f} != factory {fdur:.4f}"),
                      (w["margin"] >= 12, f"peak margin {w['margin']:.1f} < 12"), (w["rms100"] <= 0.5, f"rms +{w['rms100']:.2f} dB"),
                      (w["kloud"] <= 0.5, f"K-loud +{w['kloud']:.2f} dB"), (w["sumpeak"] <= 0.5, f"peak +{w['sumpeak']:.2f}"),
                      (follow, f"events off the factory transients: {['%.0f' % o for o in offs]} ms"), (not clip, "clips")]
            if key in BOLTS:
                gap_scaled = w["gap_ms"] / (1 + SHOT_PITCH_JITTER)
                checks.append((gap_scaled >= 9.0, f"onset {gap_scaled:.1f} ms < 9"))
            row(name, checks, f"{dur:6.3f} s = factory  events {len(lev)} at +{'/'.join('%.0f' % o for o in offs)} ms of {len(evs)} transients  "
                f"margin {w['margin']:4.1f} dB  {'rms100' if key in BOLTS else 'rms'} +{w['rms100']:.2f}  K +{w['kloud']:.2f}")
        elif name == "equip":
            marg = db(fac.vandal_peak) - db(peak(x))
            row(name, [(dur <= 0.6, f"{dur:.2f} s > 0.6"), (marg >= 12, f"margin {marg:.1f} < 12"), (not clip, "clips")],
                f"{dur:6.3f} s  peak {marg:4.1f} dB under the Vandal shot")
        elif name[0] == "k" and name[1:].isdigit():
            k = int(name[1:])
            ref = fac.kills[k - 1][1][0]
            on_ms = int(np.argmax(np.abs(x) >= 0.1 * peak(x))) / SR * 1000
            lr = len(x) / len(ref) - 1
            dr = db(rms(x)) - db(rms(ref))
            row(name, [(on_ms <= 2, f"onset {on_ms:.1f} ms"), (abs(lr) <= 0.1, f"length {lr:+.0%}"),
                       (abs(dr) <= 1, f"RMS {dr:+.2f} dB"), (not clip and peak(x) < 0.97, "clips")],
                f"{dur:6.3f} s ({lr:+.0%})  RMS {dr:+.2f} dB vs kill{k}  onset {on_ms:.1f} ms  pitch {dom_freq(x):.0f} Hz")
        elif name in ("kill", "fin", "fin_lite", "counter"):
            lim = {"kill": 0.5, "fin": 2.0, "fin_lite": 1.0, "counter": 0.05}[name]
            need_m = 23.5 if name == "counter" else 10.0
            marg = k5_peak - db(peak(x))
            row(name, [(dur <= lim, f"{dur:.2f} s > {lim}"), (marg >= need_m, f"margin {marg:.1f} < {need_m}"), (not clip, "clips")],
                f"{dur:6.3f} s  peak {marg:4.1f} dB under kill5")
        else:
            rows.append((f"{line}/{name}", False, "unexpected file"))
    if all(f"k{k}" in files for k in range(1, 6)):
        fs = [dom_freq(files[f"k{k}"]) for k in range(1, 6)]
        ok = all(b > a for a, b in zip(fs, fs[1:]))
        rows.append((f"{line}/k1..k5", ok, "pitch order " + " < ".join(f"{f:.0f}" for f in fs) + ("" if ok else "  <- not rising")))
    return rows


# ----------------------------------------------------------------------------------------------- main

def find_project(arg: str | None) -> str:
    cands = [arg] if arg else [os.path.join(HERE, "..", "..", "ValTrainerGodot"), os.path.join(HERE, "..")]
    for c in cands:
        if c and os.path.isfile(os.path.join(c, "project.godot")):
            return os.path.normpath(c)
    raise SystemExit("ValTrainerGodot project not found: pass --project DIR")


def review(out_dir: str, files: dict[str, np.ndarray], fac: Factory) -> None:
    from flash_sfx import review as fx_review
    shown = dict(files)
    for key, x in files.items():
        line, name = key.split("/")
        if name in ("fire_rifle", "fire_sniper"):
            sr, f = [(sr, x2) for nm, sr, x2 in fac.shots[name[5:]] if nm.startswith(("vandal:", "operator:"))][0]
            fm = to_rate(f[0], sr, SR)
            mix = fm.copy()
            mix[:len(x)] += x
            shown[f"{line}/{name}+factory"] = mix
    pad = lambda v: np.concatenate([v, np.zeros(max(0, 2048 - len(v)))])     # the sheets' STFT needs >= 1024 samples
    fx_review(out_dir, {k.replace("/", "_"): pad(v) for k, v in shown.items()})


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", default=None, help="the ValTrainerGodot folder (default: next to tools/)")
    ap.add_argument("--only", default=None, help="comma-separated line ids")
    ap.add_argument("--check", action="store_true", help="measure the files on disk only; write nothing")
    ap.add_argument("--review", default=None, help="also write listing.txt + spectrogram sheets here")
    ap.add_argument("--factory", default=None, help="folder with the game's kill1..5.wav / bolt.wav (--dump-kills)")
    ap.add_argument("--helpers", default=None, help="folder of flash_sfx.py (default: next to this script)")
    a = ap.parse_args()
    project = find_project(a.project)
    fac = Factory(project, a.factory)
    lines = [s.strip() for s in a.only.split(",")] if a.only else LINES
    for ln in lines:
        if ln not in RECIPES:
            raise SystemExit(f"unknown line {ln}; known: {', '.join(LINES)}")
    root = os.path.join(project, "assets", "audio", "skins")
    written = 0
    if not a.check:
        for ln in lines:
            d = os.path.join(root, ln)
            os.makedirs(d, exist_ok=True)
            for name, x in render_line(ln, fac).items():
                write_wav(os.path.join(d, name + ".wav"), x)
                written += 1
        print(f"wrote {written} WAVs to {root}")
    # measure what is on disk (the real 16-bit files)
    all_rows, on_disk, hashes = [], {}, {}
    for ln in lines:
        d = os.path.join(root, ln)
        files = {}
        if os.path.isdir(d):
            for fn in sorted(os.listdir(d)):
                if fn.endswith(".wav"):
                    sr, x = read_wav(os.path.join(d, fn))
                    assert sr == SR and x.shape[0] == 1, fn
                    files[fn[:-4]] = x[0]
                    on_disk[f"{ln}/{fn[:-4]}"] = x[0]
                    h = hashlib.sha1(x[0].tobytes()).hexdigest()
                    hashes.setdefault(h, []).append(f"{ln}/{fn}")
        all_rows += check_line(ln, files, fac)
    for h, names in hashes.items():
        if len(names) > 1:
            all_rows.append(("duplicates", False, "same audio: " + ", ".join(names)))
    src = f"game render in {a.factory}" if a.factory and os.path.isfile(os.path.join(a.factory, "kill1.wav")) else "re-implemented Sfx.cs recipes"
    print(f"\nfactory kill1..5 / bolt: {src}")
    print(f"{'file':30s} {'':4s} measurement")
    for name, ok, text in all_rows:
        print(f"{name:30s} {'PASS' if ok else 'FAIL'} {text}")
    fails = [r for r in all_rows if not r[1]]
    total_kb = sum(len(x) * 2 for x in on_disk.values()) / 1024
    print(f"\n{len(all_rows) - len(fails)}/{len(all_rows)} PASS, {len(on_disk)} files, {total_kb:.0f} KB of PCM")
    if a.review:
        review(a.review, on_disk, fac)
    print("RESULT: " + ("PASS" if not fails else f"FAIL ({len(fails)})"))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
