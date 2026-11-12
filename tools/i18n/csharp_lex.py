"""A small C# lexer for the i18n tools: comments, strings (regular, verbatim, interpolated) and punctuation.

Interpolated strings come out as their *composite format*, exactly as the compiler hands it to FormattableString.Format:
holes numbered in order ({0}, {1,-5}, {2:0.00}), literal braces doubled, escapes decoded. The tokens inside each hole are
kept (Tok.holes) so calls nested in holes (L.T(...) inside L.F($"...")) are found too.
"""
from dataclasses import dataclass, field

ESC = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'", 'a': '\a', 'b': '\b', 'f': '\f', 'v': '\v', 'e': '\x1b'}
MULTI = ['??=', '<<=', '>>=', '=>', '==', '!=', '<=', '>=', '&&', '||', '??', '?.', '+=', '-=', '*=', '/=', '++', '--', '::', '->']


@dataclass
class Tok:
    kind: str           # 'str' (plain literal), 'istr' (interpolated), 'id', 'num', 'chr', 'p' (punctuation)
    val: str
    line: int
    holes: list = field(default_factory=list)   # istr: one token list per hole
    nholes: int = 0


class Lexer:
    def __init__(self, src, line=1):
        self.s = src
        self.i = 0
        self.line = line

    def err(self, msg):
        raise SyntaxError(f'line {self.line}: {msg}')

    def tokens(self):
        out = []
        s, n = self.s, len(self.s)
        while self.i < n:
            c = s[self.i]
            if c == '\n':
                self.line += 1; self.i += 1; continue
            if c in ' \t\r﻿':
                self.i += 1; continue
            if s.startswith('//', self.i):
                j = s.find('\n', self.i)
                self.i = n if j < 0 else j
                continue
            if s.startswith('/*', self.i):
                j = s.find('*/', self.i + 2)
                j = n if j < 0 else j + 2
                self.line += s.count('\n', self.i, j)
                self.i = j
                continue
            if c == '#' and self._line_start():
                j = s.find('\n', self.i)
                self.i = n if j < 0 else j
                continue
            # strings
            if c == '"' or (c in '$@' and self._str_prefix()):
                out.append(self._string())
                continue
            if c == "'":
                out.append(self._char()); continue
            if c.isalpha() or c == '_' or (c == '@' and self.i + 1 < n and (s[self.i + 1].isalpha() or s[self.i + 1] == '_')):
                j = self.i + 1
                while j < n and (s[j].isalnum() or s[j] == '_'):
                    j += 1
                out.append(Tok('id', s[self.i:j].lstrip('@'), self.line)); self.i = j; continue
            if c.isdigit() or (c == '.' and self.i + 1 < n and s[self.i + 1].isdigit()):
                j = self.i + 1
                while j < n and (s[j].isalnum() or s[j] in '._'):
                    j += 1
                out.append(Tok('num', s[self.i:j], self.line)); self.i = j; continue
            for m in MULTI:
                if s.startswith(m, self.i):
                    out.append(Tok('p', m, self.line)); self.i += len(m); break
            else:
                out.append(Tok('p', c, self.line)); self.i += 1
        return out

    def _line_start(self):
        j = self.i - 1
        while j >= 0 and self.s[j] in ' \t':
            j -= 1
        return j < 0 or self.s[j] == '\n'

    def _str_prefix(self):
        s, i = self.s, self.i
        for p in ('$@"', '@$"', '$"', '@"'):
            if s.startswith(p, i):
                return True
        return False

    def _char(self):
        s, i = self.s, self.i + 1
        if s[i] == '\\':
            i += 2
            while s[i] != "'":
                i += 1
        else:
            i += 1
        tok = Tok('chr', s[self.i:i + 1], self.line)
        self.i = i + 1
        return tok

    def _string(self):
        s = self.s
        line0 = self.line
        interp = verbatim = False
        while s[self.i] in '$@':
            if s[self.i] == '$':
                interp = True
            else:
                verbatim = True
            self.i += 1
        if s.startswith('"""', self.i):
            self.err('raw string literals are not supported')
        self.i += 1  # opening quote
        buf = []
        holes = []
        while True:
            if self.i >= len(s):
                self.err('unterminated string')
            c = s[self.i]
            if c == '"':
                if verbatim and s.startswith('""', self.i):
                    buf.append('"'); self.i += 2; continue
                self.i += 1
                break
            if c == '\n':
                self.line += 1
                if not verbatim:
                    self.err('newline in string')
            if c == '\\' and not verbatim:
                e = s[self.i + 1]
                if e == 'u':
                    buf.append(chr(int(s[self.i + 2:self.i + 6], 16))); self.i += 6; continue
                if e == 'x':
                    j = self.i + 2
                    while j < len(s) and j < self.i + 6 and s[j] in '0123456789abcdefABCDEF':
                        j += 1
                    buf.append(chr(int(s[self.i + 2:j], 16))); self.i = j; continue
                buf.append(ESC.get(e, e)); self.i += 2; continue
            if interp and c == '{':
                if s.startswith('{{', self.i):
                    buf.append('{{'); self.i += 2; continue
                expr, align, fmt = self._hole(verbatim)
                idx = len(holes)
                holes.append(Lexer(expr, self.line).tokens())
                buf.append('{' + str(idx) + (',' + align.strip() if align is not None else '') + (':' + fmt if fmt is not None else '') + '}')
                continue
            if interp and c == '}':
                if s.startswith('}}', self.i):
                    buf.append('}}'); self.i += 2; continue
                buf.append('}}'); self.i += 1; continue  # lone '}' (invalid C#), keep it escaped
            buf.append(c)
            self.i += 1
        val = ''.join(buf)
        if interp:
            return Tok('istr', val, line0, holes, len(holes))
        return Tok('str', val, line0)

    def _hole(self, verbatim):
        """At '{': returns (expression, alignment or None, format or None) and moves past the closing '}'."""
        s = self.s
        self.i += 1
        start = self.i
        depth = 0
        align_at = fmt_at = None
        while True:
            c = s[self.i]
            if c in '([{':
                depth += 1
            elif c in ')]':
                depth -= 1
            elif c == '}':
                if depth == 0:
                    break
                depth -= 1
            elif c == '"' or (c in '$@' and self._str_prefix()):
                save = self.line
                self._string()
                self.line = save
                continue
            elif c == "'":
                self._char(); continue
            elif c == '\n':
                self.line += 1
            elif depth == 0 and c == ',' and align_at is None:
                align_at = self.i
            elif depth == 0 and c == ':' and not s.startswith('::', self.i) and s[self.i - 1] != ':':
                fmt_at = self.i
                # the format runs to the closing brace (no nesting inside a format)
                j = s.find('}', self.i)
                fmt = s[self.i + 1:j]
                expr_end = align_at if align_at is not None else fmt_at
                align = s[align_at + 1:fmt_at] if align_at is not None else None
                expr = s[start:expr_end]
                self.i = j + 1
                return expr, align, fmt
            self.i += 1
        end = self.i
        expr_end = align_at if align_at is not None else end
        align = s[align_at + 1:end] if align_at is not None else None
        self.i += 1
        return s[start:expr_end], align, None


def lex(src):
    return Lexer(src).tokens()
