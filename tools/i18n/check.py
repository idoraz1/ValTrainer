"""ValTrainer translation checker (dev tool).

    py check.py <code> [<code> ...] | --all   [--project <folder>] [--sync] [--strict]

Compares translations/<code>.json with translations/en.json (run extract.py first) and reports
  errors   : broken placeholders ({0}, {1:0.00} â€¦ must match the English key: same indexes, same formats), unbalanced
             braces in a format string, plural entries without "one"/"other" (or a plural where the key is plain text,
             and the other way round), invalid JSON, a wrong _meta.code
  warnings : missing keys (shown in English), stale keys (no longer in the source), suspicious length (a translation of
             an ALL CAPS label more than 60% longer: buttons and tabs may get tight)
--sync     rewrites the catalogue: missing keys added with "" (= English), stale keys removed, keys sorted, _meta first.
--strict   missing keys count as errors.
Exit code = number of errors (0 = ok), so it can run in CI.
"""
import argparse
import json
import os
import re
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PROJECT = os.path.normpath(os.path.join(TOOLS, '..', '..', 'ValTrainerGodot'))
HOLE = re.compile(r'\{(\d+)(,\s*-?\d+)?(:[^{}]*)?\}')


def holes(s):
    """Placeholders of a composite format, ignoring doubled braces; plus whether stray braces are left."""
    t = s.replace('{{', '').replace('}}', '')
    found = sorted(set(m.group(0).replace(' ', '') for m in HOLE.finditer(t)))
    stray = '{' in HOLE.sub('', t) or '}' in HOLE.sub('', t)
    return found, stray


def load(path):
    with open(path, encoding='utf-8-sig') as fh:
        return json.load(fh)


def check(code, project, sync=False, strict=False):
    tdir = os.path.join(project, 'translations')
    en = load(os.path.join(tdir, 'en.json'))
    en.pop('_meta', None)
    path = os.path.join(tdir, f'{code}.json')
    errors, warnings = [], []
    try:
        cat = load(path)
    except FileNotFoundError:
        print(f'{code}: {path} not found')
        return 1
    except json.JSONDecodeError as e:
        print(f'{code}: invalid JSON: {e}')
        return 1
    meta = cat.get('_meta', {})
    if meta.get('code') != code:
        errors.append(f'_meta.code is {meta.get("code")!r}, expected {code!r}')
    keys = [k for k in cat if not k.startswith('_')]
    missing = [k for k in en if k not in cat or cat[k] in ('', None)]
    stale = [k for k in keys if k not in en]
    for k in keys:
        v = cat[k]
        if v in ('', None):
            continue
        ref = en.get(k)
        if isinstance(v, dict) or isinstance(ref, dict):
            if not isinstance(v, dict) or not isinstance(v.get('one'), str) or not isinstance(v.get('other'), str):
                errors.append(f'plural shape: {k!r} needs {{"one": â€¦, "other": â€¦}}' if isinstance(ref, dict) or ref is None
                              else f'plural shape: {k!r} is plain text in English')
                continue
            if ref is not None and not isinstance(ref, dict):
                errors.append(f'plural shape: {k!r} is plain text in English')
                continue
            want, _ = holes(ref['other'] if ref else k.split('|')[-1])
            for form in ('one', 'other'):
                got, stray = holes(v[form])
                if got != want and not (form == 'one' and set(got) <= set(want)):
                    errors.append(f'placeholders: {k!r} [{form}] has {got}, English has {want}')
                if stray:
                    errors.append(f'stray brace: {k!r} [{form}] = {v[form]!r} (write {{{{ }}}} for a literal brace)')
            continue
        if not isinstance(v, str):
            errors.append(f'value of {k!r} is not a string')
            continue
        want, _ = holes(k)
        got, stray = holes(v)
        if got != want:
            errors.append(f'placeholders: {k!r} -> {v!r}: has {got}, English has {want}')
        elif want and stray:
            errors.append(f'stray brace: {k!r} -> {v!r} (write {{{{ }}}} for a literal brace)')
        if re.fullmatch(r"[A-Z0-9 â€™'&/Â·.,:!?%+-]{3,}", k) and len(v) > len(k) * 1.6 + 2:
            warnings.append(f'long label ({len(v)} vs {len(k)} chars): {k!r} -> {v!r}')
    if strict:
        errors += [f'missing: {k!r}' for k in missing]
    print(f'{code}: {len(en) - len(missing)}/{len(en)} translated ({100 * (len(en) - len(missing)) / max(1, len(en)):.0f}%), '
          f'{len(missing)} missing, {len(stale)} stale, {len(errors)} error(s), {len(warnings)} warning(s)')
    for e in errors:
        print('  ERROR ' + e)
    for w in warnings:
        print('  warn  ' + w)
    for k in stale[:40]:
        print(f'  stale {k!r}')
    if len(stale) > 40:
        print(f'  â€¦ {len(stale) - 40} more stale')
    if sync:
        out = {'_meta': meta} if meta else {}
        for k in sorted(set(en) | set(k for k in keys if k in en), key=lambda x: (x.lower(), x)):
            if k in cat and cat[k] not in (None,):
                out[k] = cat[k]
            else:
                out[k] = {'one': '', 'other': ''} if isinstance(en[k], dict) else ''
        with open(path, 'w', encoding='utf-8', newline='\n') as fh:
            json.dump(out, fh, ensure_ascii=False, indent=2)
            fh.write('\n')
        print(f'  synced: +{len([k for k in en if k not in cat])} added, -{len(stale)} stale removed -> {path}')
    return len(errors)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('codes', nargs='*')
    ap.add_argument('--all', action='store_true', help='every translations/<code>.json except en.json and _*.json')
    ap.add_argument('--project', default=DEFAULT_PROJECT)
    ap.add_argument('--sync', action='store_true')
    ap.add_argument('--strict', action='store_true')
    a = ap.parse_args()
    project = os.path.abspath(a.project)
    codes = a.codes
    if a.all:
        tdir = os.path.join(project, 'translations')
        codes = sorted(f[:-5] for f in os.listdir(tdir) if f.endswith('.json') and f != 'en.json' and not f.startswith('_'))
    if not codes:
        ap.error('give a language code (fr, es, pt-BR, de) or --all')
    errs = sum(check(c, project, a.sync, a.strict) for c in codes)
    sys.exit(min(errs, 255))


if __name__ == '__main__':
    main()
