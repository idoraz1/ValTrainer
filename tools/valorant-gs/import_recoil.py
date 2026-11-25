"""Recoil / spread importer: valorant.gs weapon curves -> ValTrainer per-bullet tables (dev tool).

    python tools/valorant-gs/import_recoil.py [--fetch] [--json <weapons.json>] [--summary]

Source: https://valorant.gs (unofficial VALORANT database, values read from the game files), JSON Data API
https://valorant.gs/api/v1/weapons/ . Its terms ask for a link back and no high-volume scraping or wholesale
republishing as your own; the Data API page says the extraction is "free to reuse; a link back to valorant.gs is the only
ask". One request per patch is plenty.

What it does, per gun (the 20 buyable guns; Chamber's guns are not in the API):
  * samples the StabilityComponent firing curves (x = consecutive shots, linear between keys, held flat before the first
    and after the last key, like an Unreal curve with constant extrapolation) at whole bullets 0, 1, 2 ... and writes the
    Pitch / Yaw / Spread tables ValTrainer's WeaponController indexes by heat;
  * copies the scalar stability values that ValTrainer models: recovery time, tap efficiency, first-shot error (hip /
    aimed), ADS error (x mul + add) and recoil multipliers, crouch and running multipliers, movement error (walking keys,
    running, crouching, jumping);
  * writes ValTrainerGodot/src/Core/ValorantGs.g.cs (never edit it by hand: re-run this script after a patch) and, with
    --summary, prints a Markdown table for the research notes.

A value the API leaves out is a blueprint default the site can't see (inherited, not zero), so nothing is written for it
and Weapons.cs keeps its own number. The recoil numbers are written exactly as the files hold them (degrees); the
calibration lives in Weapons.cs (RecoilScale), not here.

--fetch downloads the current weapons and meta tables into ValTrainerGodot/docs/research/valorant-gs/ first
(weapons-<version>.json, meta-<version>.json). Output files are written with LF line endings.
"""
import argparse
import glob
import json
import math
import os
import sys
import urllib.request

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(TOOLS, '..', '..'))
DOCS = os.path.join(ROOT, 'ValTrainerGodot', 'docs', 'research', 'valorant-gs')
OUT = os.path.join(ROOT, 'ValTrainerGodot', 'src', 'Core', 'ValorantGs.g.cs')
API = 'https://valorant.gs/api/v1/'

# Walk speed as a fraction of the run speed in ValTrainer (Mover.WalkMul): WalkErr is the walking curve there.
WALK_MUL = 0.55


def fetch():
    os.makedirs(DOCS, exist_ok=True)
    def get(table):
        req = urllib.request.Request(API + table + '/', headers={'User-Agent': 'ValTrainer-importer/1.0'})
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.load(r)
    meta = get('meta')
    weapons = get('weapons')
    ver = meta.get('version', 'unknown')
    for name, data in (('meta', meta), ('weapons', weapons)):
        path = os.path.join(DOCS, f'{name}-{ver}.json')
        with open(path, 'w', encoding='utf-8', newline='') as f:
            json.dump(data, f, indent=1, ensure_ascii=False)
            f.write('\n')
        print(f'wrote {os.path.relpath(path, ROOT)}', file=sys.stderr)
    return os.path.join(DOCS, f'weapons-{ver}.json')


def latest_json():
    files = sorted(glob.glob(os.path.join(DOCS, 'weapons-*.json')))
    if not files:
        sys.exit('no weapons-*.json in ' + DOCS + ' (run with --fetch)')
    return files[-1]


def sample(keys, x):
    """An Unreal-style curve: linear between keys, constant outside them."""
    if not keys:
        return 0.0
    if x <= keys[0][0]:
        return keys[0][1]
    if x >= keys[-1][0]:
        return keys[-1][1]
    for (x0, y0), (x1, y1) in zip(keys, keys[1:]):
        if x0 <= x <= x1:
            return y0 if x1 == x0 else y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return keys[-1][1]


def fnum(v):
    s = f'{v:.4f}'.rstrip('0').rstrip('.')
    if s in ('-0', ''):
        s = '0'
    return s + 'f'


def farr(vals):
    return 'new[] { ' + ', '.join(fnum(v) for v in vals) + ' }'


def convert(w):
    """One gun: a dict of C# member name -> (C# type, value text, comment)."""
    st = w.get('stats') or {}
    bp = w.get('blueprint') or {}
    stab = bp.get('stability') or {}
    cv = stab.get('curves') or {}
    mag = st.get('magazine') or bp.get('magazineAmmo') or 1
    out = {}
    notes = []

    curves = {k: cv[k] for k in ('pitch', 'yaw', 'error') if cv.get(k)}
    max_x = max((c[-1][0] for c in curves.values()), default=0.0)
    n = max(1, min(mag, math.ceil(max_x - 1e-6) + 1))
    heat_cap = max(1, min(mag, math.ceil(max_x - 1e-6)))
    for src, name in (('pitch', 'Pitch'), ('yaw', 'Yaw'), ('error', 'Spread')):
        if src in curves:
            keys = curves[src]
            vals = [sample(keys, i) for i in range(n)]
            out[name] = ('float[]', farr(vals), 'keys ' + ' '.join(f'{k[0]:g}:{k[1]:g}' for k in keys))
    if curves:
        out['HeatCap'] = ('int', str(heat_cap), f'last curve key at {max_x:g} shots (magazine {mag})')
        out['Bullets'] = ('int', str(n), 'table length')
    if 'error' in curves:
        out['MaxSpread'] = ('float', fnum(max(k[1] for k in curves['error'])), 'largest error key')

    if st.get('firstBulletAccuracy') is not None:
        out['FirstShotHip'] = ('float', fnum(st['firstBulletAccuracy']), 'stats.firstBulletAccuracy')
    ads_stats = st.get('ads') or {}
    if ads_stats.get('firstBulletAccuracy') is not None:
        out['FirstShotAds'] = ('float', fnum(ads_stats['firstBulletAccuracy']), 'stats.ads.firstBulletAccuracy')
    if stab.get('recoveryTime') is not None:
        out['RecoveryTime'] = ('float', fnum(stab['recoveryTime']), 'stability.recoveryTime')
    if stab.get('tapEfficiency') is not None:
        out['TapEfficiency'] = ('float', fnum(stab['tapEfficiency']), 'stability.tapEfficiency')

    ads = cv.get('ads') or {}
    if 'error' in ads:
        out['AdsErrorMul'] = ('float', fnum(ads['error']), 'curves.ads.error')
    if 'errorAdd' in ads:
        out['AdsErrorAdd'] = ('float', fnum(ads['errorAdd']), 'curves.ads.errorAdd')
    if 'pitch' in ads:
        out['AdsPitchMul'] = ('float', fnum(ads['pitch']), 'curves.ads.pitch')
    if 'yaw' in ads:
        out['AdsYawMul'] = ('float', fnum(ads['yaw']), 'curves.ads.yaw')

    states = cv.get('states') or {}
    cr = states.get('crouching') or {}
    if 'error' in cr:
        out['CrouchSpreadMul'] = ('float', fnum(cr['error']), 'states.crouching.error')
    if 'pitch' in cr:
        out['CrouchRecoilMul'] = ('float', fnum(cr['pitch']), 'states.crouching.pitch')
        if cr.get('yaw', cr['pitch']) != cr['pitch']:
            notes.append(f"crouching yaw {cr['yaw']} differs from pitch {cr['pitch']}: ValTrainer has one crouch recoil multiplier")
    run = states.get('running') or {}
    if 'pitch' in run:
        out['RunPitchMul'] = ('float', fnum(run['pitch']), 'states.running.pitch')
    if 'yaw' in run:
        out['RunYawMul'] = ('float', fnum(run['yaw']), 'states.running.yaw')
    for s in ('jumping', 'ascender', 'ladder'):
        if s in states:
            notes.append(f'{s} recoil ' + ', '.join(f'{k} x{v:g}' for k, v in states[s].items()))

    moves = {m['state']: m['keys'] for m in cv.get('movement') or []}
    if 'walking' in moves:
        keys = [k for k in moves['walking'] if k[0] > 0 or k[1] != 0]
        out['WalkErrKeys'] = ('float[]', farr([v for k in keys for v in k]), 'walking keys (speed fraction, deg) after (0, 0)')
        out['WalkErr'] = ('float', fnum(sample(moves['walking'], WALK_MUL)), f'walking curve at {WALK_MUL} of the run speed')
    if 'running' in moves:
        out['RunErr'] = ('float', fnum(moves['running'][-1][1]), 'running')
    if 'crouching' in moves:
        out['CrouchWalkErr'] = ('float', fnum(moves['crouching'][-1][1]), 'crouching (crouch-walk speed)')
    if 'jumping' in moves:
        out['AirErr'] = ('float', fnum(moves['jumping'][-1][1]), 'jumping')

    # Present in the files, not modelled by WeaponController (listed in the research notes).
    extra = []
    if cv.get('decay'):
        extra.append('movement error decay ' + ' '.join(f'{k[0]:g}:{k[1]:g}' for k in cv['decay']))
    if cv.get('landing'):
        extra.append('landing ' + ' '.join(f'{k[0]:g}s:{k[1]:g}' for k in cv['landing']))
    if cv.get('tagging'):
        extra.append('tagging ' + ' '.join(f'{k[0]:g}s:{k[1]:g}' for k in cv['tagging']))
    if 'ascender' in moves:
        extra.append('ascender error ' + ' '.join(f'{k[0]:g}:{k[1]:g}' for k in moves['ascender']))
    rr = cv.get('recoilRates')
    if rr:
        extra.append(f"recoil rates {rr.get('increase')} up / {rr.get('decay')} down")
    return out, notes, extra


def cs_name(slug):
    return slug[:1].upper() + slug[1:]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--fetch', action='store_true', help='download the current tables first')
    ap.add_argument('--json', help='weapons JSON (default: the newest docs/research/valorant-gs/weapons-*.json)')
    ap.add_argument('--summary', action='store_true', help='print a Markdown summary to stdout')
    a = ap.parse_args()
    path = fetch() if a.fetch else (a.json or latest_json())
    weapons = json.load(open(path, encoding='utf-8'))
    meta_path = path.replace('weapons-', 'meta-')
    meta = json.load(open(meta_path, encoding='utf-8')) if os.path.exists(meta_path) else {}
    build = meta.get('build', '?')

    lines = [
        '// <auto-generated>',
        '// Generated by tools/valorant-gs/import_recoil.py from ' + os.path.relpath(path, ROOT).replace('\\', '/') + '.',
        '// Do not edit by hand: re-run the script after a patch.',
        '// </auto-generated>',
        '',
        'namespace ValTrainer.Core;',
        '',
        '/// <summary>',
        '/// VALORANT weapon recoil and error data, courtesy of <a href="https://valorant.gs">valorant.gs</a> (unofficial database,',
        '/// values read from the game files; JSON Data API, build ' + build + '). Per-bullet tables are the StabilityComponent',
        '/// firing curves sampled at whole bullets (x = consecutive shots, linear between keys). Values are exactly as the files',
        '/// hold them; <see cref="RecoilScale"/> calibrates recoil. A value the files leave out (a blueprint default) is absent',
        '/// here and <see cref="Weapons"/> keeps its own. See docs/research/recoil-1.24.md.',
        '/// </summary>',
        'public static class ValorantGs',
        '{',
        f'    public const string Build = "{build}";',
    ]
    summary = []
    for w in weapons:
        if w.get('category') == 'Melee' or w['slug'] == 'melee':
            continue
        out, notes, extra = convert(w)
        lines.append('')
        lines.append(f'    /// <summary>{w["name"]} (valorant.gs/weapons/{w["slug"]}).</summary>')
        lines.append(f'    public static class {cs_name(w["slug"])}')
        lines.append('    {')
        for name, (typ, val, comment) in out.items():
            kw = 'static readonly' if typ.endswith('[]') else 'const'
            lines.append(f'        /// <summary>{comment}</summary>')
            lines.append(f'        public {kw} {typ} {name} = {val};')
        for nt in notes + extra:
            lines.append(f'        // not modelled: {nt}')
        lines.append('    }')
        summary.append((w['name'], out, notes, extra))
    lines.append('}')
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write('\n'.join(lines) + '\n')
    print(f'wrote {os.path.relpath(OUT, ROOT)} ({len(summary)} guns, build {build})', file=sys.stderr)

    if a.summary:
        print('| Gun | Pitch max | Yaw range | Spread | Recovery / tap | Move err walk-run-crouch-air | ADS err / recoil |')
        print('|---|---|---|---|---|---|---|')
        for name, out, _, _ in summary:
            def arr(k):
                return [float(x.rstrip('f')) for x in out[k][1][8:-2].split(', ')] if k in out else None
            p, y, s = arr('Pitch'), arr('Yaw'), arr('Spread')
            g = lambda k: out[k][1].rstrip('f') if k in out else '-'
            print(f"| {name} | {max(p) if p else '-'} | {(f'{min(y):g} to {max(y):g}') if y else '-'} | "
                  f"{(f'{s[0]:g} to {max(s):g}') if s else '-'} | {g('RecoveryTime')} / {g('TapEfficiency')} | "
                  f"{g('WalkErr')}-{g('RunErr')}-{g('CrouchWalkErr')}-{g('AirErr')} | "
                  f"x{g('AdsErrorMul')} {g('AdsErrorAdd')} / x{g('AdsPitchMul')} x{g('AdsYawMul')} |")


if __name__ == '__main__':
    main()
