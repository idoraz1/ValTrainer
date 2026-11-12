"""Compare line endings / BOM of every changed .cs file against a reference copy: py eolcheck.py <copy> <reference>"""
import os,sys
a,b=sys.argv[1],sys.argv[2]
bad=0
for root,_,files in os.walk(os.path.join(a,'src')):
    for f in files:
        if not f.endswith('.cs'): continue
        p=os.path.join(root,f); rel=os.path.relpath(p,a); q=os.path.join(b,rel)
        x=open(p,'rb').read()
        crlf=x.count(b'\r\n'); lf=x.count(b'\n')-crlf
        if os.path.exists(q):
            y=open(q,'rb').read()
            if x==y: continue
            rc=y.count(b'\r\n'); rl=y.count(b'\n')-rc
            want='CRLF' if rc>=rl else 'LF'
            bom_ok = x.startswith(b'\xef\xbb\xbf')==y.startswith(b'\xef\xbb\xbf')
        else:
            want='CRLF'; bom_ok=True
        have='CRLF' if lf==0 else ('LF' if crlf==0 else f'MIXED({crlf}/{lf})')
        if have!=want or not bom_ok:
            bad+=1; print(f'{rel}: {have} (want {want}){"" if bom_ok else " BOM differs"}')
print('eol ok' if bad==0 else f'{bad} files with wrong endings')
