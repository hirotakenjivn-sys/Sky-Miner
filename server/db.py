# -*- coding: utf-8 -*-
"""SQLite 接続・設定・パスの共通ヘルパ。"""
from __future__ import annotations

import json
import sqlite3
from pathlib import Path

SERVER_DIR = Path(__file__).resolve().parent
ROOT = SERVER_DIR.parent
DATA_DIR = SERVER_DIR / "data"          # sqlite db(gitignore)
DIST_DIR = SERVER_DIR / "dist"          # 生成 JSON(gitignore)
KEYS_DIR = SERVER_DIR / "keys"          # Ed25519 鍵(gitignore)
BALANCE_DIR = ROOT / "data" / "balance"  # export_balance.py の出力
SCHEMA_SQL = SERVER_DIR / "schema.sql"
DB_PATH = DATA_DIR / "market.sqlite3"


def load_config() -> dict:
    cfg_path = SERVER_DIR / "config.json"
    if not cfg_path.exists():
        cfg_path = SERVER_DIR / "config.example.json"
    with open(cfg_path, encoding="utf-8") as f:
        return json.load(f)


def connect() -> sqlite3.Connection:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


def init_schema(conn: sqlite3.Connection) -> None:
    with open(SCHEMA_SQL, encoding="utf-8") as f:
        conn.executescript(f.read())
    conn.commit()


def get_config_value(conn, key, default=None):
    row = conn.execute("SELECT value FROM config WHERE key=?", (key,)).fetchone()
    return row["value"] if row else default


def set_config_value(conn, key, value) -> None:
    conn.execute(
        "INSERT INTO config(key,value) VALUES(?,?) "
        "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
        (key, str(value)))
