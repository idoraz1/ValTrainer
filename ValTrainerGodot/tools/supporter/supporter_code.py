#!/usr/bin/env python3
"""
ValTrainer supporter codes (dev tool for the maintainer; lives outside the Godot project).

A supporter code unlocks every skin, level and variant, now and in future versions (Settings -> About -> Supporter code).
It is an ECDSA P-256 signature over the supporter's name: the game checks it offline with the PUBLIC key built into
src/Core/Supporter.cs, so there is no server, no account and no network request. The PRIVATE key stays on the maintainer's
PC (default: Desktop\\ValTrainer-support\\supporter-key\\) and must never be committed. Back it up: without it no new codes
can be made for this public key (codes already given out keep working).

    py -I supporter_code.py init                    # once: make the key pair, print the public key for Supporter.cs
    py -I supporter_code.py make "Name"             # a code for one supporter (also logged to issued.csv)
    py -I supporter_code.py check CODE              # verify a code with the public key
    options: --keydir DIR

Code layout (before base32): version 1 (1 byte) | name length (1) | name (UTF-8, <= 32 bytes) | days since 2026-01-01
(2, little endian) | 4 random bytes | signature r||s (64 bytes, ECDSA P-256 over SHA-256 of everything before it).
Shown as "VT-" + unpadded RFC 4648 base32 in groups of 5.
"""
from __future__ import annotations

import argparse
import base64
import csv
import datetime as dt
import os
import secrets
import sys

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature, encode_dss_signature

EPOCH = dt.date(2026, 1, 1)
DEFAULT_KEYDIR = os.path.join(os.path.expanduser("~"), "Desktop", "ValTrainer-support", "supporter-key")


def key_paths(keydir):
    return os.path.join(keydir, "supporter_private.pem"), os.path.join(keydir, "supporter_public.txt")


def public_b64(pub) -> str:
    return base64.b64encode(pub.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)).decode()


def load_private(keydir):
    priv_path, _ = key_paths(keydir)
    if not os.path.isfile(priv_path):
        raise SystemExit(f"no private key in {keydir}: run 'init' first")
    with open(priv_path, "rb") as f:
        return serialization.load_pem_private_key(f.read(), password=None)


def encode(raw: bytes) -> str:
    s = base64.b32encode(raw).decode().rstrip("=")
    return "VT-" + "-".join(s[i:i + 5] for i in range(0, len(s), 5))


def decode(code: str) -> bytes:
    s = code.strip().upper().replace(" ", "").replace("-", "")
    if s.startswith("VT"):
        s = s[2:]
    return base64.b32decode(s + "=" * (-len(s) % 8))


def make(priv, name: str, when: dt.date | None = None) -> str:
    nb = name.strip().encode("utf-8")
    if not 1 <= len(nb) <= 32:
        raise SystemExit("the name must be 1-32 bytes of UTF-8")
    days = ((when or dt.date.today()) - EPOCH).days
    payload = bytes([1, len(nb)]) + nb + days.to_bytes(2, "little") + secrets.token_bytes(4)
    r, s = decode_dss_signature(priv.sign(payload, ec.ECDSA(hashes.SHA256())))
    return encode(payload + r.to_bytes(32, "big") + s.to_bytes(32, "big"))


def check(pub, code: str):
    raw = decode(code)
    if len(raw) < 2 + 1 + 2 + 4 + 64 or raw[0] != 1:
        return None
    n = raw[1]
    payload, sig = raw[:-64], raw[-64:]
    if len(payload) != 2 + n + 2 + 4:
        return None
    try:
        pub.verify(encode_dss_signature(int.from_bytes(sig[:32], "big"), int.from_bytes(sig[32:], "big")), payload, ec.ECDSA(hashes.SHA256()))
    except InvalidSignature:
        return None
    return payload[2:2 + n].decode("utf-8"), EPOCH + dt.timedelta(days=int.from_bytes(payload[2 + n:4 + n], "little"))


def main() -> int:
    ap = argparse.ArgumentParser(description="ValTrainer supporter codes")
    ap.add_argument("--keydir", default=DEFAULT_KEYDIR)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("init")
    m = sub.add_parser("make"); m.add_argument("name")
    c = sub.add_parser("check"); c.add_argument("code")
    a = ap.parse_args()
    priv_path, pub_path = key_paths(a.keydir)
    if a.cmd == "init":
        if os.path.exists(priv_path):
            print(f"a key already exists in {a.keydir} (not replaced)")
            priv = load_private(a.keydir)
        else:
            os.makedirs(a.keydir, exist_ok=True)
            priv = ec.generate_private_key(ec.SECP256R1())
            with open(priv_path, "wb") as f:
                f.write(priv.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
            print(f"new private key: {priv_path}  (keep it private, back it up, never commit it)")
        pk = public_b64(priv.public_key())
        with open(pub_path, "w", encoding="utf-8") as f:
            f.write(pk + "\n")
        print("public key for src/Core/Supporter.cs (Supporter.PublicKey):")
        print(pk)
        return 0
    if a.cmd == "make":
        priv = load_private(a.keydir)
        code = make(priv, a.name)
        assert check(priv.public_key(), code) is not None
        log = os.path.join(a.keydir, "issued.csv")
        new = not os.path.exists(log)
        with open(log, "a", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            if new:
                w.writerow(["date", "name", "code"])
            w.writerow([dt.date.today().isoformat(), a.name.strip(), code])
        print(code)
        return 0
    if a.cmd == "check":
        if not os.path.isfile(pub_path):
            raise SystemExit(f"no public key in {a.keydir}")
        with open(pub_path, encoding="utf-8") as f:
            pub = serialization.load_der_public_key(base64.b64decode(f.read().strip()))
        r = check(pub, a.code)
        print(f"valid: {r[0]} (issued {r[1]})" if r else "INVALID")
        return 0 if r else 1
    return 1


if __name__ == "__main__":
    sys.exit(main())
