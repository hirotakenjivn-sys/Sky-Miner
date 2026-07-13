# -*- coding: utf-8 -*-
"""instruments マスタと基準日価格を投入する。

真実源:
  - data/balance/resources.json(export_balance.py の出力)= id・連動区分・基準USD/NOVA単価
  - 下記 FRED_SERIES = 自動取得できる4系統の FRED シリーズIDと基準日の raw 値

価格モデル(基準日比スケール):
  usd_per_kg(当日) = base_usd_per_kg × (raw当日 / base_raw)
  → 単位($/MMBtu, $/bbl, $/t, $/toz…)の差を吸収し、基準日は必ずゲーム balance と一致。
"""
from __future__ import annotations

import datetime as dt
import json

import db

# FRED 自動取得の4系統(design §1)。base_raw は基準日(2026-07-11)の指標値。
# series_id は FRED のシリーズ。実運用前に最新値の妥当性を要確認(叩き台)。
FRED_SERIES = {
    "methane":     {"series": "DHHNGSP",     "unit": "$/MMBtu", "base_raw": 2.95},
    "hydrocarbon": {"series": "DCOILWTICO",  "unit": "$/bbl",   "base_raw": 71.97},
    "iron_ore":    {"series": "PIORECRUSDM", "unit": "$/t",     "base_raw": 105.14},
    "coal":        {"series": "PCOALAUUSDM", "unit": "$/t",     "base_raw": 129.75},
}


def build_instruments(resources: list) -> list:
    rows = []
    for r in resources:
        rid = r["id"]
        link = r["link_type"]          # daily|weekly|fixed
        src = r["source"]              # fred|derived|metalsapi|manual|fixed
        base_usd = r["usd_per_kg"]
        base_nova = r["nova_per_kg"]

        fetch_type = "manual"
        source = "manual"
        unit = "$/kg"
        base_raw = base_usd
        derive_from = None

        if rid in FRED_SERIES:
            fetch_type = "auto"
            source = "fred:" + FRED_SERIES[rid]["series"]
            unit = FRED_SERIES[rid]["unit"]
            base_raw = FRED_SERIES[rid]["base_raw"]
        elif src == "derived":
            fetch_type = "derived"
            # メタン氷 → メタンに追随
            derive_from = "methane"
            source = "derived:methane"
            base_raw = None
        elif link == "fixed":
            fetch_type = "fixed"
            source = "fixed"
            base_raw = None
        # それ以外(metalsapi/manual の日次・週次)は Phase1 では手動入力

        rows.append({
            "id": rid,
            "name_ja": r["name_ja"],
            "source": source,
            "fetch_type": fetch_type,
            "link_type": link,
            "unit_note": unit,
            "base_raw": base_raw,
            "base_usd_per_kg": base_usd,
            "base_nova_per_kg": base_nova,
            "derive_from": derive_from,
            "indicator": r.get("indicator"),
        })
    return rows


def seed(conn, base_date: str) -> int:
    with open(db.BALANCE_DIR / "resources.json", encoding="utf-8") as f:
        resources = json.load(f)
    with open(db.BALANCE_DIR / "params.json", encoding="utf-8") as f:
        params = json.load(f)

    db.init_schema(conn)

    # config
    db.set_config_value(conn, "base_date", base_date)
    db.set_config_value(conn, "base_usd_vnd", params["usd_vnd_rate"])
    cfg = db.load_config()
    db.set_config_value(conn, "clamp_daily", cfg["clamp_daily"])
    db.set_config_value(conn, "clamp_total", cfg["clamp_total"])
    db.set_config_value(conn, "validate_threshold", cfg["validate_threshold"])

    rows = build_instruments(resources)
    conn.executemany(
        """INSERT INTO instruments
           (id,name_ja,source,fetch_type,link_type,unit_note,
            base_raw,base_usd_per_kg,base_nova_per_kg,derive_from,indicator)
           VALUES(:id,:name_ja,:source,:fetch_type,:link_type,:unit_note,
                  :base_raw,:base_usd_per_kg,:base_nova_per_kg,:derive_from,:indicator)
           ON CONFLICT(id) DO UPDATE SET
             name_ja=excluded.name_ja, source=excluded.source,
             fetch_type=excluded.fetch_type, link_type=excluded.link_type,
             unit_note=excluded.unit_note, base_raw=excluded.base_raw,
             base_usd_per_kg=excluded.base_usd_per_kg,
             base_nova_per_kg=excluded.base_nova_per_kg,
             derive_from=excluded.derive_from, indicator=excluded.indicator""",
        rows)

    # 基準日の生値(prices)を投入(自動系=base_raw、固定/手動=base_usd_per_kg を raw とする)
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    price_rows = []
    for r in rows:
        raw = r["base_raw"] if r["base_raw"] is not None else r["base_usd_per_kg"]
        usd = r["base_usd_per_kg"]
        price_rows.append((r["id"], base_date, raw, usd, "seed:base_date", 0, now))
    conn.executemany(
        """INSERT INTO prices(instrument_id,date,raw_value,usd_per_kg,
                              source_note,pending,created_at)
           VALUES(?,?,?,?,?,?,?)
           ON CONFLICT(instrument_id,date) DO NOTHING""",
        price_rows)

    conn.commit()
    return len(rows)


if __name__ == "__main__":
    cfg = db.load_config()
    conn = db.connect()
    n = seed(conn, cfg["base_date"])
    autos = conn.execute(
        "SELECT COUNT(*) c FROM instruments WHERE fetch_type='auto'").fetchone()["c"]
    print(f"seeded {n} instruments (auto={autos}) + base-date prices "
          f"@ {cfg['base_date']}")
