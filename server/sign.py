# -*- coding: utf-8 -*-
"""Ed25519 署名(design §4)。配信 JSON を改ざん検知可能にする。

- keygen: keys/ed25519_private.pem, keys/ed25519_public.pem, keys/public_key.txt を生成
- sign_payload: dict の "sig"/"key_id" を除いた本文を正規化 JSON にして署名
- verify_payload: クライアント側検証と同じ手順(改ざん時 False)
"""
from __future__ import annotations

import base64
import json
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import (
    Ed25519PrivateKey, Ed25519PublicKey,
)

import db

KEY_ID = "k1"
PRIV_PATH = db.KEYS_DIR / "ed25519_private.pem"
PUB_PATH = db.KEYS_DIR / "ed25519_public.pem"
PUB_B64_PATH = db.KEYS_DIR / "public_key.txt"


def keygen(force: bool = False) -> None:
    db.KEYS_DIR.mkdir(parents=True, exist_ok=True)
    if PRIV_PATH.exists() and not force:
        return
    priv = Ed25519PrivateKey.generate()
    PRIV_PATH.write_bytes(priv.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ))
    pub = priv.public_key()
    PUB_PATH.write_bytes(pub.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ))
    raw = pub.public_bytes(
        encoding=serialization.Encoding.Raw,
        format=serialization.PublicFormat.Raw,
    )
    # アプリ同梱用の raw 公開鍵(base64)
    PUB_B64_PATH.write_text(base64.b64encode(raw).decode(), encoding="utf-8")


def _load_private() -> Ed25519PrivateKey:
    return serialization.load_pem_private_key(PRIV_PATH.read_bytes(), password=None)


def _canonical(payload: dict) -> bytes:
    """署名対象の正規化バイト列。sig/key_id を除き、キー順ソート・区切り固定。"""
    body = {k: v for k, v in payload.items() if k not in ("sig", "key_id")}
    return json.dumps(body, ensure_ascii=False, sort_keys=True,
                      separators=(",", ":")).encode("utf-8")


def sign_payload(payload: dict) -> dict:
    keygen()
    priv = _load_private()
    sig = priv.sign(_canonical(payload))
    payload["sig"] = base64.b64encode(sig).decode()
    payload["key_id"] = KEY_ID
    return payload


def verify_payload(payload: dict, pub_b64: str | None = None) -> bool:
    if pub_b64 is None:
        pub_b64 = PUB_B64_PATH.read_text(encoding="utf-8").strip()
    pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(pub_b64))
    try:
        pub.verify(base64.b64decode(payload["sig"]), _canonical(payload))
        return True
    except Exception:
        return False


if __name__ == "__main__":
    keygen(force=False)
    print("keys ready:", db.KEYS_DIR)
    print("public(base64):", PUB_B64_PATH.read_text().strip())
