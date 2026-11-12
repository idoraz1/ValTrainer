"""ValTrainer i18n string extractor (dev tool; lives outside the Godot project on purpose).

    py extract.py [--project <ValTrainerGodot folder>] [--todo-only [Folder]] [--quiet]

Scans <project>/src/**/*.cs and writes
  <project>/translations/en.json        every translatable key (key = value; plurals as {"one", "other"})
  <project>/translations/_sources.json  key -> where it is used ("src/UI/MenuScreen.cs:282", at most 6)
  <tools>/i18n-todo.txt                 per folder: literals that look user-facing but reach no wrapper or sink,
                                        interpolations / concatenations passed to a sink unwrapped, unwrapped tooltips,
                                        and comparisons (==, StartsWith, Contains, case) on English text

What counts as translatable (see docs/research/i18n-howto.md):
  * L.T("..") L.N("..") L.C("ctx", "..") (key "ctx|.."), L.F($"..") (key = composite format), L.P(n, $"one", $"other")
  * plain literals (or ternaries / ?? of literals) passed straight to a sink: Gfx.Text/TextC/TextR/TextFit (3rd arg),
    Gfx.TextW (2nd), Btn/Lbl/Selector/Segmented/CodeRow/FolderRow/Info/Keys/Row/TL (label arg), Heading (both),
    Segmented's option array, note:/Note = lambdas, Label/Sub/Title/Caption/Text/Note/Tag = "..." assignments, and the
    first argument of Banner(..) / Refuse(..) (also qualified: G.Banner, Kit.Refuse).
"""
import argparse
import json
import os
import re
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from csharp_lex import lex, Tok  # noqa: E402

TOOLS = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PROJECT = os.path.normpath(os.path.join(TOOLS, '..', '..', 'ValTrainerGodot'))

GFX_SINKS = {'Text': 2, 'TextC': 2, 'TextR': 2, 'TextFit': 2, 'TextW': 1}
CALL_SINKS = {'Btn': [0], 'Lbl': [0], 'Heading': [0, 1], 'Selector': [0], 'Segmented': [0], 'CodeRow': [0],
              'FolderRow': [0], 'Info': [0], 'Keys': [0], 'Row': [0], 'TL': [1], 'TC': [1], 'TR': [1]}
# sinks also when called on an object (G.Banner(..), GameSession.Banner, Kit.Refuse(..), k.Refuse(..)): the HUD draws
# the banner / refusal text through L.T, so a plain literal there is a key
MEMBER_SINKS = {'Banner': [0], 'Refuse': [0]}
PROP_SINKS = {'Label', 'Sub', 'Title', 'Caption', 'Text', 'Note', 'Tag'}
LAMBDA_ARGS = {'note'}
# arguments of these calls are never UI text
NOT_UI_CALLS = {'Info', 'Error', 'Exception', 'Print', 'PrintErr', 'PushError', 'PushWarning', 'PrintRich', 'WriteLine', 'Write',
                'Has', 'After', 'DevAfter', 'GetNode', 'GetNodeOrNull', 'FindChild', 'FindChildren', 'Play', 'nameof',
                'AddThemeColorOverride', 'AddThemeConstantOverride', 'AddThemeFontOverride', 'AddThemeFontSizeOverride',
                'AddThemeStyleboxOverride', 'AddThemeIconOverride', 'SetColor', 'SetFont', 'SetFontSize', 'SetStylebox',
                'SetConstant', 'Raw', 'TextRaw', 'RawW', 'Load', 'GetFileAsString', 'Combine', 'GetEnvironmentVariable',
                'Trace', 'Check', 'XT', 'P', 'Event', 'Get', 'TryGetValue', 'GetProperty', 'TryGetProperty', 'Split',
                'Replace', 'Join', 'StartsWith', 'EndsWith', 'Contains', 'IndexOf', 'Trim', 'TrimEnd', 'TrimStart',
                'ToString', 'Parse', 'TryParse', 'Format', 'Matches', 'Match', 'Regex', 'IsMatch', 'Equals', 'Compare',
                'HasMethod', 'Call', 'CallDeferred', 'Connect', 'EmitSignal', 'SetMeta', 'GetMeta', 'SetShaderParameter',
                'GetShaderParameter', 'SetInstanceShaderParameter', 'ActionAddEvent', 'InputMap', 'IsActionPressed',
                'IsActionJustPressed', 'Throw', 'Describe', 'Find', 'FindAll', 'Assert', 'Fail', 'Note', 'Dump'}
# files whose literals are dev / test / CLI output (no todo items; L.* calls inside still count)
DEV_FILE = re.compile(r'(CultureCheck|CoachCli|SimAim|CoachFake|.*Test|.*Check|.*Dev|PreviewCheck|DeathmatchSpawnCheck|'
                      r'WeaponCheck|MovementCheck|KeybindTest|CoachWarmUp|Boot|L|LangDetect)\.cs$')
LOGIC_CALLS = {'StartsWith', 'EndsWith', 'Contains', 'Equals', 'IndexOf'}


def looks_ui(s):
    """Heuristic: does a plain literal look like text a player reads?"""
    if len(s) < 2 or not re.search(r'[A-Za-z]{2}', s):
        return False
    if re.match(r'^(res|user|uid|http|https|file)://', s) or re.search(r'\.(cs|json|tscn|tres|png|ogg|wav|glb|txt|ini|yaml|exe|zip|vtt|vtr|md|log|gdshader|import)\b', s):
        return False
    if s.startswith('--') or s.startswith('#') or '\\' in s or s.startswith('/'):
        return False
    if ' ' not in s:
        if re.fullmatch(r'[A-Z][A-Z0-9â€™\'!?.â€¦%&+-]{1,}', s) and len(s) >= 3:  # ALLCAPS word: a label
            return True
        if re.fullmatch(r'[A-Z][a-z]+[.!?â€¦]?', s) and len(s) >= 4:  # Capitalized word
            return True
        return False
    if re.fullmatch(r'[a-z0-9_.:/\- ]+', s) and ' ' not in s.strip():
        return False
    return True


class Extractor:
    def __init__(self, project):
        self.project = project
        self.keys = {}            # key -> str or {"one","other"}
        self.sources = defaultdict(list)
        self.todo = defaultdict(list)   # folder -> [(file, line, kind, text)]
        self.counts = defaultdict(lambda: defaultdict(int))  # folder -> kind -> n

    # ------------------------------------------------------------------
    def run(self):
        src = os.path.join(self.project, 'src')
        for root, _, files in os.walk(src):
            for f in sorted(files):
                if f.endswith('.cs'):
                    self.scan_file(os.path.join(root, f))

    def folder_of(self, rel):
        parts = rel.replace('\\', '/').split('/')
        # src/UI/x.cs -> UI ; src/Modes/Agents/x.cs -> Modes/Agents ; src/Main.cs -> (root)
        if len(parts) <= 2:
            return '(root)'
        return '/'.join(parts[1:-1][:2])

    def scan_file(self, path):
        rel = os.path.relpath(path, self.project).replace('\\', '/')
        text = open(path, encoding='utf-8-sig').read()
        try:
            toks = lex(text)
        except SyntaxError as e:
            print(f'warning: {rel}: {e}', file=sys.stderr)
            return
        self.rel, self.folder = rel, self.folder_of(rel)
        self.dev = bool(DEV_FILE.search(os.path.basename(path)))
        self.used = set()   # id() of str tokens consumed or excluded
        self.walk(toks)
        if not self.dev:
            self.report_leftovers(toks)

    # ------------------------------------------------------------------
    def add(self, key, tok, kind, value=None):
        if not key or not re.search(r'[A-Za-z]', key if isinstance(key, str) else key):
            return
        if key not in self.keys:
            self.keys[key] = value if value is not None else key
        src = f'{self.rel}:{tok.line}'
        if src not in self.sources[key] and len(self.sources[key]) < 6:
            self.sources[key].append(src)
        self.counts[self.folder][kind] += 1

    def todo_add(self, tok, kind, text):
        if self.dev:
            return
        self.todo[self.folder].append((self.rel, tok.line, kind, text))

    @staticmethod
    def args(toks, i):
        """toks[i] is '(' : returns (list of argument token lists, index after ')')."""
        depth = 0
        cur, out = [], []
        j = i
        while j < len(toks):
            t = toks[j]
            if t.kind == 'p' and t.val in '([{':
                depth += 1
                if depth > 1:
                    cur.append(t)
            elif t.kind == 'p' and t.val in ')]}':
                depth -= 1
                if depth == 0:
                    if cur or out:
                        out.append(cur)
                    return out, j + 1
                cur.append(t)
            elif t.kind == 'p' and t.val == ',' and depth == 1:
                out.append(cur); cur = []
            else:
                cur.append(t)
            j += 1
        return out, j

    @staticmethod
    def depth0(arg):
        """Tokens of an argument at nesting depth 0 (outside any (), [], {})."""
        d = 0
        for t in arg:
            if t.kind == 'p' and t.val in '([{':
                d += 1
            elif t.kind == 'p' and t.val in ')]}':
                d -= 1
            elif d == 0:
                yield t

    def mark_all(self, toks):
        for t in toks:
            if t.kind in ('str', 'istr'):
                self.used.add(id(t))
                for h in t.holes:
                    self.mark_all(h)

    def strip_named(self, arg):
        """'note: () => "x"' -> (name, tokens after the colon)."""
        if len(arg) >= 2 and arg[0].kind == 'id' and arg[1].kind == 'p' and arg[1].val == ':':
            return arg[0].val, arg[2:]
        return None, arg

    def sink_arg(self, arg, what):
        """A plain literal / ternary of literals reaching a sink -> keys. Interpolation or concatenation -> todo."""
        _, arg = self.strip_named(arg)
        body = self.lambda_body(arg)
        d0 = list(self.depth0(body))
        lits = [t for t in d0 if t.kind == 'str']
        istr = [t for t in d0 if t.kind == 'istr']
        concat = any(t.kind == 'p' and t.val == '+' for t in d0)
        if istr and not self.wrapped(body):
            for t in istr:
                self.used.add(id(t))
                if re.search(r'[A-Za-z]{2}', t.val):
                    self.todo_add(t, 'interpolation at a sink (use L.F)', t.val)
        if concat and lits:
            for t in lits:
                self.used.add(id(t))
                if looks_ui(t.val):
                    self.todo_add(t, f'concatenation at a sink ({what}): use L.F', t.val)
            return
        for t in lits:
            self.used.add(id(t))
            self.add(t.val, t, 'sink')

    @staticmethod
    def lambda_body(arg):
        # only a '=>' at depth 0 starts a lambda body (the arms of a switch expression sit inside its braces)
        d = 0
        for k in range(len(arg)):
            t = arg[k]
            if t.kind == 'p' and t.val in '([{':
                d += 1
            elif t.kind == 'p' and t.val in ')]}':
                d -= 1
            elif d == 0 and t.kind == 'p' and t.val == '=>':
                return arg[k + 1:]
        return arg

    @staticmethod
    def wrapped(body):
        return len(body) >= 3 and body[0].kind == 'id' and body[0].val == 'L'

    # ------------------------------------------------------------------
    def walk(self, toks):
        n = len(toks)
        i = 0
        while i < n:
            t = toks[i]
            if t.kind == 'istr':
                for h in t.holes:
                    self.walk(h)
            nxt = toks[i + 1] if i + 1 < n else None
            # L.X( ... )
            if t.kind == 'id' and t.val == 'L' and nxt and nxt.val == '.' and i + 3 < n and toks[i + 2].kind == 'id' and toks[i + 3].val == '(':
                fn = toks[i + 2].val
                args, _ = self.args(toks, i + 3)
                self.l_call(fn, args, toks[i + 2])
                i += 3
                continue
            # Gfx.Text*( ... )
            if t.kind == 'id' and t.val == 'Gfx' and nxt and nxt.val == '.' and i + 3 < n and toks[i + 2].val in GFX_SINKS and toks[i + 3].val == '(':
                args, _ = self.args(toks, i + 3)
                k = GFX_SINKS[toks[i + 2].val]
                if k < len(args):
                    self.sink_arg(args[k], 'Gfx.' + toks[i + 2].val)
                i += 3
                continue
            prev = toks[i - 1] if i > 0 else None
            # Btn( ... ), Selector( ... ) ... (unqualified or this.)
            if t.kind == 'id' and t.val in CALL_SINKS and nxt and nxt.val == '(' and not (prev and prev.val == '.' and toks[i - 2].val not in ('this', 'base')):
                args, _ = self.args(toks, i + 1)
                for k in CALL_SINKS[t.val]:
                    if k < len(args):
                        self.sink_arg(args[k], t.val)
                if t.val == 'Segmented' and len(args) > 1:
                    for s in args[1]:
                        if s.kind == 'str':
                            self.used.add(id(s)); self.add(s.val, s, 'sink')
                for a in args:
                    name, rest = self.strip_named(a)
                    if name in LAMBDA_ARGS:
                        self.sink_arg(rest, name + ':')
            # G.Banner("..", c) / Kit.Refuse("..") (qualified or not)
            if t.kind == 'id' and t.val in MEMBER_SINKS and nxt and nxt.val == '(':
                args, _ = self.args(toks, i + 1)
                for k in MEMBER_SINKS[t.val]:
                    if k < len(args):
                        self.sink_arg(args[k], t.val)
            # Prop = "..." (object initializers and assignments)
            # (Text only in object initializers: `x.Text = ..` is usually a Godot Label / LineEdit, which needs L.T itself)
            if t.kind == 'id' and t.val in PROP_SINKS and nxt and nxt.kind == 'p' and nxt.val == '=' and not (t.val == 'Text' and prev and prev.val == '.'):
                expr = self.until_end(toks, i + 2)
                self.sink_arg(expr, t.val + ' =')
            # TooltipText = "..." unwrapped / PlaceholderText
            if t.kind == 'id' and t.val in ('TooltipText', 'PlaceholderText') and nxt and nxt.val == '=':
                expr = self.until_end(toks, i + 2)
                if not self.wrapped(expr):
                    for s in self.depth0(expr):
                        if s.kind in ('str', 'istr') and re.search(r'[A-Za-z]{2}', s.val):
                            self.used.add(id(s))
                            self.todo_add(s, f'{t.val} not wrapped (L.T / L.F)', s.val)
            # calls whose arguments are never UI text (logs, node paths, theme names, Raw ...)
            if t.kind == 'id' and t.val in NOT_UI_CALLS and nxt and nxt.val == '(':
                args, _ = self.args(toks, i + 1)
                if t.val in LOGIC_CALLS:
                    for a in args:
                        for s in a:
                            if s.kind == 'str' and looks_ui(s.val) and ' ' in s.val:
                                self.todo_add(s, f'logic on text: {t.val}(...)', s.val)
                for a in args:
                    self.mark_all(a)
            # == "..." / != "..." / case "..."
            if t.kind == 'p' and t.val in ('==', '!=') and nxt and nxt.kind == 'str':
                self.used.add(id(nxt))
                if looks_ui(nxt.val) and ' ' in nxt.val:
                    self.todo_add(nxt, f'logic on text: {t.val}', nxt.val)
            if t.kind == 'id' and t.val == 'case' and nxt and nxt.kind == 'str':
                self.used.add(id(nxt))
            # attributes [Export("..")]: skip
            if t.kind == 'p' and t.val == '[' and nxt and nxt.kind == 'id' and i + 2 < n and toks[i + 2].val == '(' and (prev is None or prev.val in (';', '{', '}', ']')):
                args, _ = self.args(toks, i + 2)
                for a in args:
                    self.mark_all(a)
            i += 1

    @staticmethod
    def until_end(toks, j):
        """Expression tokens from j to the next ',' ';' or unbalanced closer at depth 0."""
        out, d = [], 0
        while j < len(toks):
            t = toks[j]
            if t.kind == 'p':
                if t.val in '([{':
                    d += 1
                elif t.val in ')]}':
                    if d == 0:
                        break
                    d -= 1
                elif t.val in ',;' and d == 0:
                    break
            out.append(t)
            j += 1
        return out

    def l_call(self, fn, args, tok):
        if fn in ('T', 'N') and args:
            body = args[0]
            for s in self.depth0(body):
                if s.kind == 'str':
                    self.used.add(id(s)); self.add(s.val, s, 'L.' + fn)
                elif s.kind == 'istr':
                    self.used.add(id(s))
                    self.todo_add(s, f'interpolation in L.{fn} (use L.F)', s.val)
        elif fn == 'C' and len(args) >= 2:
            ctx = [s for s in args[0] if s.kind == 'str']
            en = [s for s in self.depth0(args[1]) if s.kind == 'str']
            for c in ctx:
                self.used.add(id(c))
                for s in en:
                    self.used.add(id(s)); self.add(c.val + '|' + s.val, s, 'L.C', s.val)   # value = the English word, not the key
        elif fn == 'F' and args:
            for s in self.depth0(args[0]):
                if s.kind in ('istr', 'str'):
                    self.used.add(id(s)); self.add(s.val, s, 'L.F')
        elif fn == 'P' and len(args) >= 3:
            one = [s for s in self.depth0(args[1]) if s.kind in ('istr', 'str')]
            other = [s for s in self.depth0(args[2]) if s.kind in ('istr', 'str')]
            if one and other:
                self.used.add(id(one[0])); self.used.add(id(other[0]))
                self.add(one[0].val + '|' + other[0].val, one[0], 'L.P', {'one': one[0].val, 'other': other[0].val})
        elif fn in ('Raw',):
            for a in args:
                self.mark_all(a)

    # ------------------------------------------------------------------
    def report_leftovers(self, toks):
        for t in self.flat(toks):
            if id(t) in self.used:
                continue
            if t.kind == 'str' and looks_ui(t.val):
                self.todo_add(t, 'literal (not wrapped / not at a sink)', t.val)
            elif t.kind == 'istr' and re.search(r'[A-Za-z]{2}[^{}]*\s', re.sub(r'\{[^}]*\}', '', t.val)):
                self.todo_add(t, 'interpolated (not wrapped)', t.val)

    def flat(self, toks):
        for t in toks:
            yield t
            for h in t.holes:
                yield from self.flat(h)

    # ------------------------------------------------------------------
    def write(self, todo_only=None, quiet=False):
        tdir = os.path.join(self.project, 'translations')
        if todo_only is None:
            os.makedirs(tdir, exist_ok=True)
            ordered = {'_meta': {'code': 'en', 'name': 'English', 'english': 'English', 'status': 'source',
                                 'note': 'Generated by i18n-tools/extract.py: every translatable key (key = value). Do not edit.'}}
            for k in sorted(self.keys, key=lambda x: (x.lower(), x)):
                ordered[k] = self.keys[k]
            dump(os.path.join(tdir, 'en.json'), ordered)
            dump(os.path.join(tdir, '_sources.json'), {k: self.sources[k] for k in sorted(self.sources, key=lambda x: (x.lower(), x))})
        lines = ['ValTrainer i18n todo (generated by i18n-tools/extract.py; heuristic worklist, not a gate)', '']
        folders = sorted(self.todo) if todo_only in (None, '') else [f for f in sorted(self.todo) if f == todo_only or f.startswith(todo_only + '/')]
        for f in folders:
            items = self.todo[f]
            lines.append(f'== src/{f}: {len(items)} item(s)')
            by_kind = defaultdict(list)
            for rel, line, kind, text in items:
                by_kind[kind].append((rel, line, text))
            for kind in sorted(by_kind):
                lines.append(f'  -- {kind}: {len(by_kind[kind])}')
                for rel, line, text in by_kind[kind]:
                    lines.append(f'     {rel}:{line}  {json.dumps(text, ensure_ascii=False)[:150]}')
            lines.append('')
        out = os.path.join(TOOLS, 'i18n-todo.txt')
        with open(out, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write('\n'.join(lines))
        if not quiet:
            nplural = sum(1 for v in self.keys.values() if isinstance(v, dict))
            if todo_only is None:
                print(f'{len(self.keys)} keys ({nplural} plural) -> translations/en.json, translations/_sources.json')
            print('found per folder (sink = plain literal at a draw helper):')
            for f in sorted(self.counts):
                c = self.counts[f]
                print(f'  {f:16} ' + '  '.join(f'{k} {v}' for k, v in sorted(c.items())) + f'   | todo {len(self.todo.get(f, []))}')
            for f in sorted(set(self.todo) - set(self.counts)):
                print(f'  {f:16} (nothing wrapped)   | todo {len(self.todo[f])}')
            print(f'todo list -> {out}')


def dump(path, obj):
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(obj, fh, ensure_ascii=False, indent=2)
        fh.write('\n')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--project', default=DEFAULT_PROJECT, help='the Godot project folder (contains src/ and translations/)')
    ap.add_argument('--todo-only', nargs='?', const='', default=None, metavar='FOLDER',
                    help='only write the todo report (optionally for one folder, e.g. UI or Modes/Agents); en.json is left alone')
    ap.add_argument('--quiet', action='store_true')
    a = ap.parse_args()
    ex = Extractor(os.path.abspath(a.project))
    ex.run()
    ex.write(a.todo_only, a.quiet)


if __name__ == '__main__':
    main()
