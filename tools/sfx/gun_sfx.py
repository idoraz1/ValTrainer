#!/usr/bin/env python3
"""
ValTrainer gun sound builder (v1.18: all 20 guns).

Cuts single shots out of CC0 field recordings and processes them into game-ready one-shot WAVs (48 kHz, 16-bit,
mono), and builds the shotgun / revolver / machine-gun reloads, the Bucky pump and the Odin / Ares spin-up. The
output is deterministic (fixed-seed noise only), so re-running with the same sources reproduces the files bit for bit.

Sources (all CC0 1.0):
  LIB  "The Free Firearm Sound Library" ("Prepared SFX Library") by Ben Jaszczak, Brian Nelson, Kevin Heras and
       Matthew Nanney, via OpenGameArt <https://opengameart.org/content/the-free-firearm-sound-library>. 96 kHz /
       24-bit stereo takes; every take used here is a "near distance, front of shooter, left/right" one (Prepared
       Master Sheet.csv), like the AK-47 C_28P / AR-15 D_32P / Tikka W_29P / Walther X_39P takes already in the game.
  FS   Freesound: #163455 "Shotgun Shot" by LeMudCrab, #482122 "Clean Machine Gun Burst" by qubodup. Checked and
       not used: #49512 "357Mag.wav" by Jon285 (each shot sits at full scale for ~200 ms, i.e. it is clipped, over a
       -36 dB noise floor) and #687399 "shotgun Gunshot.mp3" by ONgiaunbg (decodes to +4.6 dB over full scale: clipped
       before encoding); the library's clean takes won.
  OUT  files already in assets/audio/weapons: pistol_reload.wav ("Handgun Reload Sound Effect" by zer0_sol, OGA),
       rifle_reload_airsoft.wav and gun_reload_airsoft.wav ("Gun reload sounds" by SpringySpringo, OGA).

Every shot clip: one shot from the take (onset = first sample over 30 % of the local peak; the clip starts 4 ms
before it), the stereo pair mixed to mono, optional pitch (resampling, like a tape-speed change: higher = shorter),
filters, an exponential tail (the recordings' range echo, shortened for fast guns), a fade-out over the last
min(0.25 s, 30 %), peak-normalized to -1 dBFS, then set to a loudness target = RMS of the first 100 ms after the
onset (the Vandal's AK clips measure -20.0 dB, the Phantom's -21.0, the Classic's -23.7, the Operator's -16.3). When
a target needs more than the -1 dBFS ceiling allows, a tanh soft limiter at the ceiling adds the drive (punch).

  key         files                  source take (shot onsets, s)                chain
  shorty      shorty_shot_01..02     LIB CD (Charles Daly pump, 12 ga) H_21P     pitch 0.93, low shelf +5 dB @110 Hz,
                                     (0.464, 3.075)                              tail 0.28 s, 1.2 s, target -16.0
  frenzy      frenzy_shot_01..02     LIB Carl Gustav M45 9 mm G_31P              pitch 1.09, HP 150 Hz, peak +2 dB @3.5 kHz,
                                     (0.310, 3.501)                              tail 0.09, 0.75 s, -23.0
  ghost       ghost_shot_01..02      LIB Ruger Mark III .22 R_35P (0.481, 4.482) suppressed: LP 2.2 + 2.86 kHz, HP 160 Hz, tail
                                                                                 0.045, + 3.5 kHz action click, 0.6 s, -31.0
  bandit      bandit_shot_01..02     LIB Bersa .380 F_47P (0.334, 4.409)          pitch 0.94, low shelf +3 dB @150 Hz, peak +2 dB
                                                                                 @2.5 kHz, tail 0.20, 0.9 s, -21.5
  sheriff     sheriff_shot_01..02    LIB Smith & Wesson 642 .38 revolver V_27P   pitch 0.92, HP 60 Hz, low shelf +4 dB @120 Hz,
                                     (0.805, 6.218)                              tail 0.24, 1.0 s, -19.5
  headhunter  headhunter_shot_01..02 LIB 1911 .45 A_42P (0.941, 5.001)           pitch 0.97, low shelf +3 dB @130 Hz, tail 0.22, 0.9 s, -20.5
  stinger     stinger_shot_01..03    LIB PPSh 7.62x25 P_30P (0.968, 4.387, 8.242) pitch 1.05, HP 120 Hz, peak +2 dB @3.2 kHz,
                                                                                 tail 0.08, 0.75 s, -22.0
  spectre     spectre_shot_01..03    LIB Carl Gustav M45 9 mm G_31P              suppressed: pitch 1.04, LP 2.8 + 3.64 kHz, HP 140 Hz,
                                     (0.310, 3.501, 6.727)                       tail 0.06, + click, 0.6 s, -28.5
  bucky       bucky_shot_01..02      LIB Winchester Model 12 (pump) K_22P        low shelf +2 dB @120 Hz, tail 0.30, 1.2 s, -16.5
                                     (0.842, 7.445)
  judge       judge_shot_01..02      LIB Benelli Nova O_21P (0.431, 3.465)       + FS 163455 LeMudCrab shot layered at -5 dB,
                                                                                 pitch 1.03, tail 0.16, 1.0 s, -17.0
  bulldog     bulldog_shot_01..02    LIB Marlin 336 .30-30 I_22P (0.740, 7.003)  peak +3 dB @260 Hz (low-mid), HP 60 Hz,
                                                                                 tail 0.18, 1.0 s, -20.5
  guardian    guardian_shot_01..02   LIB Norinco SKS 7.62x39 U_14P               peak +2 dB @3 kHz, tail 0.25, 1.0 s, -19.5
                                     (3.577, 6.755)
  warden      warden_shot_01..02     LIB Springfield 1917 .30-06 B_24P           pitch 1.03, HP 70 Hz, tail 0.20, 1.0 s, -19.5
                                     (1.308, 6.731)
  marshal     marshal_shot_01..02    LIB Savage 10 .300 Blackout T_27P           tail 0.45, 1.3 s, -17.0
                                     (0.950, 4.968)
  outlaw      outlaw_shot_01..02     LIB Mosin Nagant 7.62x54R M_21P             pitch 0.97, low shelf +4 dB @90 Hz, tail 0.45,
                                     (1.033, 5.019)                              1.3 s, -16.0
  ares        ares_shot_01..02       LIB Winchester Model 1894 .32 L_23P          pitch 0.96, HP 80 Hz, tail 0.07, + FS 482122
                                     (0.572, 4.123)                              qubodup burst shot 1 / 3 (LP 5 kHz) at -7 dB, 0.75 s, -20.5
  odin        odin_shot_01..02       LIB Arisaka .30-06 E_25P (0.523, 4.030)      pitch 0.93, low shelf +3 dB @100 Hz, tail 0.08,
                                                                                 + FS 482122 shot 2 / 4 (LP 3.5 kHz) at -7 dB, 0.75 s, -19.5
  pump        shotgun_pump.wav       LIB Model 12 K_22P: pump back 4.25-4.40 s,  the 0.55 s gap closed to 0.12 s, 0.45 s,
                                     forward 4.70-4.92 s                         peak -3 dBFS
  reload_shotgun shotgun_reload.wav  LIB Model 12 K_22P: shell clicks 3.26-3.40  3 shell inserts (pitch 0.97/1.0/1.03) at 0.05 /
                                     + the pump above                            0.42 / 0.79 s, then the pump at 1.20 s; 1.8 s
  reload_revolver revolver_reload.wav OUT pistol_reload.wav: release click      release (pitch 1.3), swing out, 5 synthesized
                                     0.08-0.30 s, insert clack 1.02-1.17 s       brass tinkles, insert (pitch 1.1), close (pitch
                                                                                 0.85), hammer cock (click at pitch 1.5); 2.0 s
  reload_lmg  lmg_reload.wav         OUT rifle_reload_airsoft.wav + the charging  pitch 0.85, low shelf +4 dB @200 Hz, then the clack
                                     clack of gun_reload_airsoft.wav (1.22-1.56)  at pitch 0.8; 2.35 s
  spinup      heavy_spinup.wav       synthesized here (no samples)               motor 70 -> 400 Hz harmonics + gear whine + band noise, 1.0 s
  spindown    heavy_spindown.wav     synthesized here (no samples)               400 -> 60 Hz, decaying, 0.8 s

Usage:
    py -I gun_sfx.py "<...>/Prepared SFX Library" "<...>/gun-sfx-downloads" [OUT] [--ffmpeg PATH] [--only key ...]
OUT defaults to <ValTrainerGodot>/assets/audio/weapons. ffmpeg (decoding + resampling) defaults to the copy under
tools/ffmpeg. After writing, it prints a check table (peak, onset, RMS of the first 100 ms, length, spectral centroid)
and fails if a file clips or two keys share the same audio.
"""
from __future__ import annotations

import argparse
import glob
import hashlib
import os
import subprocess
import sys
import wave

import numpy as np
from scipy import signal

SR = 48000
CEIL = 10 ** (-1 / 20)  # -1 dBFS
FF = "ffmpeg"

# ----------------------------------------------------------------------------------------------- shot table
# key: (out prefix, source ("lib/<folder>/<file>" | "fs/<file>"), onsets s, length s, target RMS dB, chain dict)
# chain: pitch, hp, lp (Hz, 4th-order Butterworth), lp2 (cascade it twice), shelf (f, dB), peak (f, dB, q),
#        tail (tau s, applied after 15 ms), click (dB: high-passed copy of the first 3 ms = a suppressor's action noise),
#        layer (source, shot per variant: None = the file's first onset, int = the n-th shot of a machine-gun burst,
#        gain dB relative to the main shot, low-pass Hz or None)
SHOTS: dict[str, tuple] = {
    "shorty": ("shorty", "lib/CD/H_21P.wav", [0.464, 3.075], 1.2, -16.0,
               dict(pitch=0.93, shelf=(110, 5), tail=0.28)),
    "frenzy": ("frenzy", "lib/Carl Gustav M45/G_31P.wav", [0.310, 3.501], 0.75, -23.0,
               dict(pitch=1.09, hp=150, peak=(3500, 2, 1.0), tail=0.09)),
    "ghost": ("ghost", "lib/Ruger Mark III/R_35P.wav", [0.481, 4.482], 0.6, -31.0,
              dict(lp=2200, lp2=True, hp=160, tail=0.045, click=-16)),
    "bandit": ("bandit", "lib/Bersa/F_47P.wav", [0.334, 4.409], 0.9, -21.5,
               dict(pitch=0.94, shelf=(150, 3), peak=(2500, 2, 1.0), tail=0.20)),
    "sheriff": ("sheriff", "lib/Smith & Wesson 642/V_27P.wav", [0.805, 6.218], 1.0, -19.5,
                dict(pitch=0.92, hp=60, shelf=(120, 4), tail=0.24)),
    "headhunter": ("headhunter", "lib/1911/A_42P.wav", [0.941, 5.001], 0.9, -20.5,
                   dict(pitch=0.97, shelf=(130, 3), tail=0.22)),
    "stinger": ("stinger", "lib/PPSh/P_30P.wav", [0.968, 4.387, 8.242], 0.75, -22.0,
                dict(pitch=1.05, hp=120, peak=(3200, 2, 1.0), tail=0.08)),
    "spectre": ("spectre", "lib/Carl Gustav M45/G_31P.wav", [0.310, 3.501, 6.727], 0.6, -28.5,
                dict(pitch=1.04, lp=2800, lp2=True, hp=140, tail=0.06, click=-15)),
    "bucky": ("bucky", "lib/Model 12/K_22P.wav", [0.842, 7.445], 1.2, -16.5,
              dict(shelf=(120, 2), tail=0.30)),
    "judge": ("judge", "lib/Nova/O_21P.wav", [0.431, 3.465], 1.0, -17.0,
              dict(pitch=1.03, tail=0.16, layer=("fs/163455__lemudcrab__shotgun-shot.wav", [None, None], -5, None))),
    "bulldog": ("bulldog", "lib/Marlin 336/I_22P.wav", [0.740, 7.003], 1.0, -20.5,
                dict(hp=60, peak=(260, 3, 0.9), tail=0.18)),
    "guardian": ("guardian", "lib/SKS/U_14P.wav", [3.577, 6.755], 1.0, -19.5,
                 dict(peak=(3000, 2, 1.0), tail=0.25)),
    "warden": ("warden", "lib/1917/B_24P.wav", [1.308, 6.731], 1.0, -19.5,
               dict(pitch=1.03, hp=70, tail=0.20)),
    "marshal": ("marshal", "lib/Savage 10 .300 Blackout/T_27P.wav", [0.950, 4.968], 1.3, -17.0,
                dict(tail=0.45)),
    "outlaw": ("outlaw", "lib/Mosin Nagant/M_21P.wav", [1.033, 5.019], 1.3, -16.0,
               dict(pitch=0.97, shelf=(90, 4), tail=0.45)),
    "ares": ("ares", "lib/Model 1894/L_23P.wav", [0.572, 4.123], 0.75, -20.5,
             dict(pitch=0.96, hp=80, tail=0.07, layer=("fs/482122__qubodup__clean-machine-gun-burst.wav", [0, 2], -7, 5000))),
    "odin": ("odin", "lib/Arisaka/E_25P.wav", [0.523, 4.030], 0.75, -19.5,
             dict(pitch=0.93, shelf=(100, 3), tail=0.08, layer=("fs/482122__qubodup__clean-machine-gun-burst.wav", [1, 3], -7, 3500))),
}

# ----------------------------------------------------------------------------------------------- io


def load(path: str, pitch: float = 1.0) -> np.ndarray:
    """Any audio file as float stereo at SR / pitch (so that playing it at SR shifts the pitch by `pitch`)."""
    rate = int(round(SR / pitch))
    out = subprocess.run([FF, "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(rate), "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(out, np.float32).reshape(-1, 2).astype(np.float64)


def write_wav(path: str, x: np.ndarray) -> None:
    pcm = np.round(np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def read_wav(path: str) -> np.ndarray:
    with wave.open(path) as w:
        ch = w.getnchannels()
        d = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float64) / 32768
    return d.reshape(-1, ch).mean(1)


# ----------------------------------------------------------------------------------------------- dsp


def onset(m: np.ndarray, t: float | None) -> int:
    """First sample over 30 % of the local peak near `t` (None = the whole file)."""
    if t is None:
        a, b = 0, len(m)
    else:
        a, b = max(0, int((t - 0.03) * SR)), min(len(m), int((t + 0.10) * SR))
    seg = np.abs(m[a:b])
    return a + int(np.argmax(seg > 0.3 * seg.max()))


def biquad_shelf(f: float, db: float) -> tuple[np.ndarray, np.ndarray]:
    """RBJ low shelf, S = 1."""
    A = 10 ** (db / 40)
    w = 2 * np.pi * f / SR
    al = np.sin(w) / 2 * np.sqrt(2)
    c = np.cos(w)
    b = [A * ((A + 1) - (A - 1) * c + 2 * np.sqrt(A) * al), 2 * A * ((A - 1) - (A + 1) * c),
         A * ((A + 1) - (A - 1) * c - 2 * np.sqrt(A) * al)]
    a = [(A + 1) + (A - 1) * c + 2 * np.sqrt(A) * al, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - 2 * np.sqrt(A) * al]
    return np.array(b) / a[0], np.array(a) / a[0]


def biquad_peak(f: float, db: float, q: float) -> tuple[np.ndarray, np.ndarray]:
    A = 10 ** (db / 40)
    w = 2 * np.pi * f / SR
    al = np.sin(w) / (2 * q)
    b = [1 + al * A, -2 * np.cos(w), 1 - al * A]
    a = [1 + al / A, -2 * np.cos(w), 1 - al / A]
    return np.array(b) / a[0], np.array(a) / a[0]


def hp(x, f, order=2):
    return signal.sosfilt(signal.butter(order, f, "highpass", fs=SR, output="sos"), x)


def lp(x, f, order=4):
    return signal.sosfilt(signal.butter(order, f, "lowpass", fs=SR, output="sos"), x)


def fades(x: np.ndarray, fade_out: float | None = None) -> np.ndarray:
    x = x.copy()
    n_in = int(0.001 * SR)
    x[:n_in] *= np.linspace(0, 1, n_in)
    n_out = int((fade_out if fade_out is not None else min(0.25, 0.3 * len(x) / SR)) * SR)
    if n_out > 0:
        x[-n_out:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, n_out))
    return x


def rms100(x: np.ndarray) -> float:
    o = onset(x, None)
    seg = x[o:o + int(0.1 * SR)]
    return 20 * np.log10(np.sqrt(np.mean(seg ** 2)) + 1e-12)


def level(x: np.ndarray, target: float) -> np.ndarray:
    """Peak-normalize to -1 dBFS, then reach `target` RMS(100 ms): plain gain while the peak allows it, else drive a
    tanh limiter at the ceiling (at most +12 dB of drive)."""
    x = x / np.max(np.abs(x)) * CEIL
    if rms100(x) >= target:
        return x * 10 ** ((target - rms100(x)) / 20)
    lo, hi = 1.0, 4.0
    lim = lambda g: CEIL * np.tanh(g * x / CEIL)
    for _ in range(40):
        mid = (lo + hi) / 2
        if rms100(lim(mid)) < target:
            lo = mid
        else:
            hi = mid
    return lim(hi)


def cut(path: str, t: float | None, length: float, pitch: float = 1.0) -> np.ndarray:
    st = load(path, pitch)
    m = st.mean(1)
    o = onset(m, None if t is None else t / pitch)
    a = max(0, o - int(0.004 * SR))
    seg = m[a:a + int(length * SR)]
    return np.pad(seg, (0, int(length * SR) - len(seg)))


def burst_shots(path: str) -> list[int]:
    """Onsets of the single shots inside a machine-gun burst (envelope peaks, >= 45 ms apart)."""
    m = load(path).mean(1)
    env = signal.sosfiltfilt(signal.butter(2, 120, "lowpass", fs=SR, output="sos"), np.abs(hp(m, 1500)))
    peaks, _ = signal.find_peaks(env, distance=int(0.045 * SR), height=0.35 * env.max())
    starts = []
    for p in peaks:
        a = max(0, p - int(0.02 * SR))
        seg = np.abs(m[a:p + 1])
        starts.append(a + int(np.argmax(seg > 0.3 * seg.max())))
    return starts


def shot(src: str, t: float, length: float, ch: dict, layer_x: np.ndarray | None) -> np.ndarray:
    p = ch.get("pitch", 1.0)
    x = cut(src, t, length, p)
    if layer_x is not None:
        x = x / np.max(np.abs(x)) + layer_x[:len(x)]
    raw = x.copy()
    if "hp" in ch:
        x = hp(x, ch["hp"])
    if "lp" in ch:
        x = lp(x, ch["lp"])
        if ch.get("lp2"):
            x = lp(x, ch["lp"] * 1.3)
    if "shelf" in ch:
        x = signal.lfilter(*biquad_shelf(*ch["shelf"]), x)
    if "peak" in ch:
        x = signal.lfilter(*biquad_peak(*ch["peak"]), x)
    if "click" in ch:
        # a suppressor's action noise: the first 3 ms of the raw shot, high-passed (slide / bolt clack, no boom)
        n = int(0.003 * SR)
        c = hp(raw, 3500, 4)
        c[n:] *= np.exp(-np.arange(len(c) - n) / (0.002 * SR))
        x = x / np.max(np.abs(x)) + c / (np.max(np.abs(c)) + 1e-12) * 10 ** (ch["click"] / 20) * 3
    if "tail" in ch:
        n0 = int(0.019 * SR)  # 4 ms pre-roll + 15 ms of full body
        env = np.ones(len(x))
        env[n0:] = np.exp(-np.arange(len(x) - n0) / (ch["tail"] * SR))
        x = x * env
    return fades(x)


def build_shots(lib: str, fs: str, out: str, only: list[str] | None) -> dict[str, list[np.ndarray]]:
    res = {}
    burst_cache: dict[str, list[int]] = {}
    for key, (prefix, src, ts, length, target, ch) in SHOTS.items():
        if only and key not in only:
            continue
        path = resolve(src, lib, fs)
        clips = []
        for i, t in enumerate(ts):
            lx = None
            if "layer" in ch:
                lsrc, lts, ldb, llp = ch["layer"]
                lpath = resolve(lsrc, lib, fs)
                p = ch.get("pitch", 1.0)
                if lts[i] is None:
                    lx = cut(lpath, None, length, p)
                else:
                    starts = burst_cache.setdefault(lpath, burst_shots(lpath))
                    s0 = starts[lts[i]] / SR
                    period = (starts[lts[i] + 1] - starts[lts[i]]) / SR
                    lx = cut(lpath, s0, length, p)
                    nlen = int(period / p * 0.9 * SR)
                    lx[nlen:] = 0
                    nf = int(0.008 * SR)
                    lx[nlen - nf:nlen] *= np.linspace(1, 0, nf)
                if llp:
                    lx = lp(lx, llp)
                lx = lx / (np.max(np.abs(lx)) + 1e-12) * 10 ** (ldb / 20)
            x = level(shot(path, t, length, ch, lx), target)
            clips.append(x)
            write_wav(os.path.join(out, f"{prefix}_shot_{i + 1:02d}.wav"), x)
        res[key] = clips
    return res


def resolve(src: str, lib: str, fs: str) -> str:
    kind, rel = src.split("/", 1)
    return os.path.join(lib if kind == "lib" else fs, *rel.split("/"))


# ----------------------------------------------------------------------------------------------- mechanics


def seg(m: np.ndarray, t0: float, t1: float, pitch: float = 1.0) -> np.ndarray:
    """A gated slice [t0, t1) s of a mono signal at SR (5 ms cosine fades), optionally re-pitched."""
    s = m[int(t0 * SR):int(t1 * SR)].copy()
    if pitch != 1.0:
        s = signal.resample_poly(s, 1000, int(round(1000 * pitch)))
    n = min(int(0.005 * SR), len(s) // 2)
    w = 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, n))
    s[:n] *= w
    s[-n:] *= w[::-1]
    return s


def place(buf: np.ndarray, s: np.ndarray, t: float, gain: float = 1.0) -> None:
    a = int(t * SR)
    n = min(len(s), len(buf) - a)
    buf[a:a + n] += s[:n] * gain


def norm(x: np.ndarray, peak_db: float) -> np.ndarray:
    return x / (np.max(np.abs(x)) + 1e-12) * 10 ** (peak_db / 20)


def pump_parts(lib: str) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    m = load(os.path.join(lib, "Model 12", "K_22P.wav")).mean(1)
    back = norm(seg(m, 4.25, 4.40), 0)
    fwd = norm(seg(m, 4.70, 4.92), 0)
    shell = norm(seg(m, 3.26, 3.40), -4)
    return back, fwd, shell


def build_mech(lib: str, out: str) -> dict[str, np.ndarray]:
    res = {}
    back, fwd, shell = pump_parts(lib)

    pump = np.zeros(int(0.45 * SR))
    place(pump, back, 0.0, 0.85)
    place(pump, fwd, 0.12 + len(back) / SR - 0.05)
    res["shotgun_pump"] = fades(norm(pump, -3), 0.03)

    rel = np.zeros(int(1.8 * SR))
    for t, p in ((0.05, 0.97), (0.42, 1.0), (0.79, 1.03)):
        place(rel, signal.resample_poly(shell, 1000, int(round(1000 * p))), t)
    place(rel, back, 1.20, 0.85)
    place(rel, fwd, 1.20 + 0.12 + len(back) / SR - 0.05)
    res["shotgun_reload"] = fades(norm(rel, -1), 0.05)

    # Revolver: cylinder release click, swing out, brass falling, speed-loader insert, close, hammer cock (all from the
    # handgun reload's clicks and clacks, re-pitched, plus synthesized brass).
    pr = read_wav(os.path.join(out, "pistol_reload.wav"))
    click = seg(pr, 0.08, 0.22)
    rv = np.zeros(int(2.0 * SR))
    place(rv, norm(signal.resample_poly(click, 10, 13), -4), 0.03)  # release, pitch 1.3
    place(rv, norm(seg(pr, 0.08, 0.30), -6), 0.20)  # swing out
    rng = np.random.default_rng(642)
    t = np.arange(int(0.12 * SR)) / SR
    for k in range(5):
        f = 2900 + 300 * k + rng.uniform(-120, 120)
        tink = np.sin(2 * np.pi * f * t) + 0.5 * np.sin(2 * np.pi * f * 2.76 * t)
        tink *= np.exp(-t / rng.uniform(0.02, 0.04))
        tink += hp(rng.uniform(-1, 1, len(t)), 4000) * np.exp(-t / 0.003) * 0.6
        place(rv, tink / 1.5, 0.55 + 0.07 * k + rng.uniform(0, 0.03), 10 ** (rng.uniform(-24, -18) / 20))
    place(rv, norm(seg(pr, 1.02, 1.17, 1.1), -2), 1.08)  # insert
    place(rv, norm(seg(pr, 0.08, 0.26, 0.85), -3), 1.45)  # close
    place(rv, norm(signal.resample_poly(click, 2, 3), -8), 1.78)  # hammer cock, pitch 1.5
    res["revolver_reload"] = fades(norm(rv, -1), 0.03)

    # Machine gun: the rifle reload slowed and heavier, then a charging-handle clack.
    rr = read_wav(os.path.join(out, "rifle_reload_airsoft.wav"))
    rr = signal.resample_poly(rr, 1000, 850)
    rr = signal.lfilter(*biquad_shelf(200, 4), rr)
    ga = read_wav(os.path.join(out, "gun_reload_airsoft.wav"))
    lm = np.zeros(int(2.35 * SR))
    place(lm, norm(rr, -1), 0.0)
    place(lm, norm(seg(ga, 1.22, 1.56, 0.8), -2), 1.85)
    res["lmg_reload"] = fades(norm(lm, -1), 0.05)

    res["heavy_spinup"] = spin(1.0, up=True)
    res["heavy_spindown"] = spin(0.8, up=False)
    for k, x in res.items():
        write_wav(os.path.join(out, k + ".wav"), x)
    return res


def spin(dur: float, up: bool) -> np.ndarray:
    """Electric motor + gearbox of a minigun-style heavy: harmonic motor tone whose speed ramps up (or runs down), a gear
    whine at 7.3x, and band-passed air noise that follows the speed. Pure synthesis."""
    t = np.arange(int(dur * SR)) / SR
    if up:
        f = 70 + 330 * (1 - np.exp(-t / 0.33))
        amp = np.minimum(1, t / 0.03) * (0.45 + 0.55 * (1 - np.exp(-t / 0.25)))
    else:
        f = 60 + 340 * np.exp(-t / 0.28)
        amp = np.exp(-t / 0.35)
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = sum(np.sin(k * ph) / k for k in range(1, 7))
    whine = np.sin(7.3 * ph) * 0.35
    rng = np.random.default_rng(1306)
    noise = rng.uniform(-1, 1, len(t))
    air = np.zeros(len(t))
    blk = 1024
    for i in range(0, len(t), blk):  # band-pass that tracks the speed (block-wise)
        fc = min(9000, 4 * f[i] + 600)
        sos = signal.butter(2, [fc * 0.7, fc * 1.4], "bandpass", fs=SR, output="sos")
        air[i:i + blk] = signal.sosfilt(sos, noise[max(0, i - 2048):i + blk])[-len(noise[i:i + blk]):]
    x = (lp(tone, 3000) * 0.5 + whine + air * 1.5) * amp
    return fades(norm(x, -6), 0.06)


# ----------------------------------------------------------------------------------------------- check


# Reloads that were already in the game before this script (louder masters; not written here).
OLD_FILES = {"pistol_reload.wav", "rifle_reload_airsoft.wav", "gun_reload_airsoft.wav"}


def check(out: str) -> int:
    files = sorted(glob.glob(os.path.join(out, "*.wav")))
    print(f"{'file':28s} {'ch':>2s} {'len s':>6s} {'peak':>6s} {'onset ms':>8s} {'rms100':>7s} {'centroid':>8s}")
    hashes: dict[str, str] = {}
    bad = 0
    total = 0
    for f in files:
        total += os.path.getsize(f)
        with wave.open(f) as w:
            ch, sr, n = w.getnchannels(), w.getframerate(), w.getnframes()
            raw = w.readframes(n)
        x = np.frombuffer(raw, "<i2").astype(np.float64).reshape(-1, ch).mean(1) / 32768
        o = onset(x, None)
        s = x[o:o + int(0.1 * sr)]
        spec = np.abs(np.fft.rfft(s * np.hanning(len(s))))
        cen = (spec * np.fft.rfftfreq(len(s), 1 / sr)).sum() / spec.sum()
        pk = 20 * np.log10(np.max(np.abs(x)) + 1e-12)
        name = os.path.basename(f)
        print(f"{name:28s} {ch:2d} {n / sr:6.2f} {pk:6.1f} {o / sr * 1000:8.1f} {rms100(x) if sr == SR else 0:7.1f} {cen:8.0f}")
        h = hashlib.md5(raw).hexdigest()
        if h in hashes:
            print(f"  !! {name} is identical to {hashes[h]}")
            bad += 1
        hashes[h] = name
        if pk > -0.9 and name not in OLD_FILES:
            print(f"  !! {name} peaks over -1 dBFS")
            bad += 1
    print(f"{len(files)} files, {total / 1e6:.2f} MB, {bad} problem(s)")
    return bad


def default_out() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    for cand in (os.path.join(here, "..", "..", "ValTrainerGodot"), os.path.join(here, "..")):
        if os.path.isfile(os.path.join(cand, "project.godot")):
            return os.path.normpath(os.path.join(cand, "assets", "audio", "weapons"))
    return os.path.join(here, "weapons")


def default_ffmpeg() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    found = sorted(glob.glob(os.path.join(here, "..", "ffmpeg", "**", "ffmpeg.exe"), recursive=True))
    return os.path.normpath(found[-1]) if found else "ffmpeg"


def main() -> int:
    global FF
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("lib", help='the "Prepared SFX Library" folder')
    ap.add_argument("freesound", help="folder with the Freesound downloads (#49512, #163455, #482122)")
    ap.add_argument("out", nargs="?", default=None, help="output folder (default: assets/audio/weapons)")
    ap.add_argument("--ffmpeg", default=None)
    ap.add_argument("--only", nargs="*", help="build only these shot keys (and skip the mechanics)")
    ap.add_argument("--check-only", action="store_true", help="only print the check table")
    a = ap.parse_args()
    FF = a.ffmpeg or default_ffmpeg()
    out = a.out or default_out()
    os.makedirs(out, exist_ok=True)
    if not a.check_only:
        shots = build_shots(a.lib, a.freesound, out, a.only)
        mech = {} if a.only else build_mech(a.lib, out)
        print(f"wrote {sum(len(v) for v in shots.values())} shot clips and {len(mech)} other clips to {out}")
    return 1 if check(out) else 0


if __name__ == "__main__":
    sys.exit(main())
