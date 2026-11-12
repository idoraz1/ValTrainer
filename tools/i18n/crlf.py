"""Helpers for the i18n retrofit: edit a C# file keeping its line endings and BOM.
usage (python): from crlf import edit; edit(path, [(old, new), ...])  -- old/new written with \n line endings."""
import sys
def edit(p, pairs, count=1):
    raw=open(p,'rb').read()
    bom=raw.startswith(b'\xef\xbb\xbf')
    s=raw.decode('utf-8-sig')
    crlf='\r\n' in s
    s=s.replace('\r\n','\n')
    for a,b in pairs:
        if a not in s: raise SystemExit(f'{p}: not found: {a[:80]!r}')
        s=s.replace(a,b,count) if count else s.replace(a,b)
    if crlf: s=s.replace('\n','\r\n')
    open(p,'wb').write((b'\xef\xbb\xbf' if bom else b'')+s.encode('utf-8'))
def tocrlf(p):
    raw=open(p,'rb').read()
    s=raw.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n')
    open(p,'wb').write(s)
if __name__=='__main__':
    for p in sys.argv[1:]: tocrlf(p)
