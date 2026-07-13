# -*- coding: utf-8 -*-
"""FRED fetcher(design §1, §3):天然ガス・WTI・鉄鉱石・石炭。

FRED API の observations エンドポイントから最新観測値を取得する。
- APIキーは config.fred_api_key(または環境変数 FRED_API_KEY)。
- タイムアウト15秒×リトライ3回・指数バックオフ(design §3-1)。
- 欠測(".")は無視して直近の実測値を返す。
- キー未設定・失敗時は None を返し、呼び出し側が stale 継続にフォールバック。
"""
from __future__ import annotations

import os
import time
import urllib.parse
import urllib.request
import json as _json

FRED_URL = "https://api.stlouisfed.org/fred/series/observations"


def fetch_latest(series_id: str, api_key: str, timeout: int = 15,
                 retries: int = 3):
    """(value: float, obs_date: str) を返す。取得不能なら None。"""
    if not api_key:
        return None
    params = {
        "series_id": series_id,
        "api_key": api_key,
        "file_type": "json",
        "sort_order": "desc",
        "limit": 10,   # 直近から遡り、最初の実測値を採用
    }
    url = FRED_URL + "?" + urllib.parse.urlencode(params)
    last_err = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(url, timeout=timeout) as resp:
                data = _json.loads(resp.read().decode("utf-8"))
            for obs in data.get("observations", []):
                v = obs.get("value")
                if v not in (None, ".", ""):
                    return float(v), obs["date"]
            return None
        except Exception as e:
            last_err = e
            time.sleep(2 ** attempt)  # 1s,2s,4s
    print(f"[fred] {series_id} 取得失敗: {last_err}")
    return None


def resolve_api_key(cfg: dict) -> str:
    return cfg.get("fred_api_key") or os.environ.get("FRED_API_KEY", "")
