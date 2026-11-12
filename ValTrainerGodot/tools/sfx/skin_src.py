#!/usr/bin/env python3
"""
Source loader for skin_sfx.py: decodes the CC0 source recordings (.ogg / .flac / .wav) to float arrays.

The sources are UNTRUSTED data (downloaded packs, see CREDITS.md): they are only ever decoded, by ffmpeg, to raw
32-bit float PCM at 44.1 kHz. Nothing from them is executed or imported. Run this module with `py -I`, keep it outside
the source folder and pass the source folder as an argument.

    py -I skin_src.py --sources DIR [--ffmpeg EXE] [--cache DIR] survey [filter]
"""
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import subprocess
import sys

import numpy as np

SR = 48000   # the factory gun clips are 48 kHz; everything is decoded and rendered at this rate
_FF: str | None = None
_CACHE: str | None = None
_ROOT: str | None = None
_mem: dict[str, np.ndarray] = {}


def find_ffmpeg(given: str | None = None) -> str:
    cands = [given, os.environ.get("FFMPEG"), shutil.which("ffmpeg")]
    la = os.environ.get("LOCALAPPDATA", "")
    wg = os.path.join(la, "Microsoft", "WinGet", "Packages")
    if os.path.isdir(wg):
        for d in sorted(os.listdir(wg)):
            if d.lower().startswith("gyan.ffmpeg"):
                for root, _dirs, files in os.walk(os.path.join(wg, d)):
                    if "ffmpeg.exe" in files:
                        cands.append(os.path.join(root, "ffmpeg.exe"))
    for c in cands:
        if c and os.path.isfile(c):
            return c
    raise SystemExit("ffmpeg not found: pass --ffmpeg EXE or set FFMPEG")


def setup(sources: str, ffmpeg: str | None = None, cache: str | None = None) -> None:
    global _FF, _CACHE, _ROOT
    _ROOT = os.path.abspath(sources)
    if not os.path.isdir(_ROOT):
        raise SystemExit(f"sources folder not found: {_ROOT}")
    _FF = find_ffmpeg(ffmpeg)
    _CACHE = cache
    if cache:
        os.makedirs(cache, exist_ok=True)


def path_of(rel: str) -> str:
    """`pack/file` -> the file under <pack>/extracted/ (searched once)."""
    assert _ROOT, "skin_src.setup() first"
    pack, _, name = rel.partition("/")
    base = os.path.join(_ROOT, pack, "extracted")
    direct = os.path.join(base, name)
    if os.path.isfile(direct):
        return direct
    for root, _dirs, files in os.walk(base):
        if name in files:
            return os.path.join(root, name)
    raise FileNotFoundError(rel)


def load(rel: str, mono: bool = True) -> np.ndarray:
    """Decoded source `pack/file` at 44.1 kHz: mono float array (or [ch, n] with mono=False)."""
    key = rel + ("|m" if mono else "|s")
    if key in _mem:
        return _mem[key].copy()
    p = path_of(rel)
    x = None
    cpath = None
    if _CACHE:
        h = hashlib.sha1((p + str(os.path.getsize(p)) + str(SR)).encode()).hexdigest()[:16]
        cpath = os.path.join(_CACHE, h + ".npy")
        if os.path.isfile(cpath):
            x = np.load(cpath, allow_pickle=False)
    if x is None:
        out = subprocess.run([_FF, "-v", "error", "-nostdin", "-i", p, "-f", "f32le", "-acodec", "pcm_f32le", "-ar", str(SR), "-"],
                             capture_output=True, check=True).stdout
        ch = _channels(p)
        x = np.frombuffer(out, dtype="<f4").astype(np.float64)
        x = x[: len(x) // ch * ch].reshape(-1, ch).T.copy()
        x = np.nan_to_num(x)
        if cpath:
            np.save(cpath, x, allow_pickle=False)
    _mem[rel + "|s"] = x
    m = x.mean(axis=0)
    _mem[rel + "|m"] = m
    return (m if mono else x).copy()


def _channels(p: str) -> int:
    r = subprocess.run([_FF, "-v", "info", "-nostdin", "-i", p, "-f", "null", "-t", "0", "-"], capture_output=True, text=True,
                       errors="replace")
    for line in r.stderr.splitlines():
        if "Audio:" in line:
            s = line.split("Audio:", 1)[1]
            if "stereo" in s:
                return 2
            if "mono" in s:
                return 1
            for part in s.split(","):
                part = part.strip()
                if part.endswith("channels"):
                    try:
                        return int(part.split()[0])
                    except ValueError:
                        pass
    return 1


def listing() -> list[str]:
    assert _ROOT
    out = []
    for pack in sorted(os.listdir(_ROOT)):
        base = os.path.join(_ROOT, pack, "extracted")
        if not os.path.isdir(base):
            continue
        for root, _dirs, files in os.walk(base):
            for f in sorted(files):
                if f.lower().endswith((".ogg", ".wav", ".flac", ".mp3")):
                    out.append(f"{pack}/{f}")
    return out


def stats(x: np.ndarray) -> dict:
    a = np.abs(x)
    pk = float(a.max()) if len(a) else 0.0
    on = int(np.argmax(a >= 0.3 * pk)) if pk > 0 else 0
    hop = 441
    nf = max(1, len(x) // hop)
    e = np.array([np.sqrt(np.mean(x[i * hop:(i + 1) * hop] ** 2) + 1e-20) for i in range(nf)])
    edb = 20 * np.log10(e + 1e-12)
    top = int(np.argmax(edb))
    below = np.nonzero(edb[top:] < edb[top] - 30)[0]
    t30 = (below[0] if len(below) else nf - top) * hop / SR
    seg = x[on:on + 8192]
    if len(seg) < 64:
        seg = x
    w = np.hanning(len(seg))
    sp = np.abs(np.fft.rfft(seg * w))
    f = np.fft.rfftfreq(len(seg), 1 / SR)
    cen = float((sp * f).sum() / (sp.sum() + 1e-12))
    dom = float(f[np.argmax(sp)])
    flat = float(np.exp(np.mean(np.log(sp + 1e-12))) / (np.mean(sp) + 1e-12))
    return {"dur": len(x) / SR, "peak": pk, "onset": on / SR, "t30": t30, "cen": cen, "dom": dom, "flat": flat}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--sources", required=True)
    ap.add_argument("--ffmpeg")
    ap.add_argument("--cache")
    ap.add_argument("cmd", choices=["survey"])
    ap.add_argument("filter", nargs="?", default="")
    a = ap.parse_args()
    setup(a.sources, a.ffmpeg, a.cache)
    print(f"{'file':58s} {'dur':>6s} {'pkdB':>6s} {'on':>6s} {'t30':>6s} {'cen':>6s} {'dom':>6s} {'flat':>5s}")
    for rel in listing():
        if a.filter and a.filter not in rel:
            continue
        try:
            s = stats(load(rel))
        except Exception as e:  # noqa: BLE001
            print(f"{rel:58s} ERROR {e}")
            continue
        print(f"{rel:58s} {s['dur']:6.2f} {20*np.log10(s['peak']+1e-12):6.1f} {s['onset']*1000:6.1f} {s['t30']:6.2f} "
              f"{s['cen']:6.0f} {s['dom']:6.0f} {s['flat']:5.2f}")


if __name__ == "__main__":
    sys.exit(main())
