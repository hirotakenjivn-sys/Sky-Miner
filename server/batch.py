# -*- coding: utf-8 -*-
"""日次バッチ(design §3)。

  取得(FRED) → 妥当性チェック(±閾値=保留) → クランプ → NOVA換算
  → latest.json / history_{id}.json 生成 → Ed25519 署名 → 通知

使い方:
  python3 server/batch.py                # 今日(ローカル)を処理
  python3 server/batch.py 2026-07-11     # 指定日を処理
生値は prices、ゲーム配信値は game_prices に保存。JSON は server/dist/ に出力。
"""
from __future__ import annotations

import datetime as dt
import json
import sys

import db
import convert
import notify
import sign
from fetchers import fred


def _prev_game_nova(conn, iid, date):
    row = conn.execute(
        "SELECT nova_per_kg FROM game_prices "
        "WHERE instrument_id=? AND date<? ORDER BY date DESC LIMIT 1",
        (iid, date)).fetchone()
    return row["nova_per_kg"] if row else None


def _last_fresh_price(conn, iid, date):
    """target 以前で pending=0 の最新の生値(raw,usd,その日付)。"""
    return conn.execute(
        "SELECT date, raw_value, usd_per_kg FROM prices "
        "WHERE instrument_id=? AND date<=? AND pending=0 "
        "ORDER BY date DESC LIMIT 1",
        (iid, date)).fetchone()


def _prev_raw(conn, iid, date):
    row = conn.execute(
        "SELECT raw_value FROM prices "
        "WHERE instrument_id=? AND date<? AND pending=0 "
        "ORDER BY date DESC LIMIT 1",
        (iid, date)).fetchone()
    return row["raw_value"] if row else None


def fetch_phase(conn, cfg, date, base_date):
    """自動系を取得・保存し、失敗/保留を集計。derived を親から算出。"""
    api_key = fred.resolve_api_key(cfg)
    threshold = float(db.get_config_value(conn, "validate_threshold", 0.30))
    now = dt.datetime.now(dt.timezone.utc).isoformat()

    failures, pendings = [], []

    insts = conn.execute("SELECT * FROM instruments").fetchall()

    # --- 1st pass: auto(FRED) ---
    for it in insts:
        if it["fetch_type"] != "auto":
            continue
        # 既に当日値がある(手動投入等)ならスキップ
        exists = conn.execute(
            "SELECT 1 FROM prices WHERE instrument_id=? AND date=?",
            (it["id"], date)).fetchone()
        if exists:
            continue
        series = it["source"].split(":", 1)[1]
        got = fred.fetch_latest(series, api_key)
        if got is None:
            failures.append(it["id"])
            continue
        raw, _obs_date = got
        usd = it["base_usd_per_kg"] * (raw / it["base_raw"])
        # 妥当性チェック:前日 raw 比 ±threshold 超は保留(承認まで前日値を使う)
        prev = _prev_raw(conn, it["id"], date)
        pending = 0
        if prev and abs(raw / prev - 1.0) > threshold:
            pending = 1
            pendings.append((it["id"], raw / prev - 1.0))
        conn.execute(
            "INSERT INTO prices(instrument_id,date,raw_value,usd_per_kg,"
            "source_note,pending,created_at) VALUES(?,?,?,?,?,?,?) "
            "ON CONFLICT(instrument_id,date) DO UPDATE SET "
            "raw_value=excluded.raw_value, usd_per_kg=excluded.usd_per_kg, "
            "pending=excluded.pending, source_note=excluded.source_note",
            (it["id"], date, raw, usd, it["source"], pending, now))

    # --- 2nd pass: fixed / derived を当日行として確定 ---
    for it in insts:
        if it["fetch_type"] == "fixed":
            exists = conn.execute(
                "SELECT 1 FROM prices WHERE instrument_id=? AND date=?",
                (it["id"], date)).fetchone()
            if not exists:
                conn.execute(
                    "INSERT INTO prices(instrument_id,date,raw_value,usd_per_kg,"
                    "source_note,pending,created_at) VALUES(?,?,?,?,?,0,?)",
                    (it["id"], date, it["base_usd_per_kg"],
                     it["base_usd_per_kg"], "fixed", now))
        elif it["fetch_type"] == "derived":
            parent = it["derive_from"]
            pf = _last_fresh_price(conn, parent, date)
            if pf is None:
                failures.append(it["id"])
                continue
            # 親の usd/kg 比を自分の基準に適用(同一指標に追随)
            parent_inst = conn.execute(
                "SELECT base_usd_per_kg FROM instruments WHERE id=?",
                (parent,)).fetchone()
            ratio = pf["usd_per_kg"] / parent_inst["base_usd_per_kg"]
            usd = it["base_usd_per_kg"] * ratio
            conn.execute(
                "INSERT INTO prices(instrument_id,date,raw_value,usd_per_kg,"
                "source_note,pending,created_at) VALUES(?,?,?,?,?,0,?) "
                "ON CONFLICT(instrument_id,date) DO UPDATE SET "
                "usd_per_kg=excluded.usd_per_kg",
                (it["id"], date, usd, usd, it["source"], now))

    conn.commit()
    return failures, pendings


def game_phase(conn, date, base_date):
    """全銘柄の game_prices(クランプ+NOVA)を算出・保存。"""
    base_usd_vnd = float(db.get_config_value(conn, "base_usd_vnd", 26300))
    clamp_daily = float(db.get_config_value(conn, "clamp_daily", 0.10))
    clamp_total = float(db.get_config_value(conn, "clamp_total", 0.50))

    insts = conn.execute("SELECT * FROM instruments").fetchall()
    target = dt.date.fromisoformat(date)

    for it in insts:
        fresh = _last_fresh_price(conn, it["id"], date)
        if fresh is None:
            # 生値が一切ない(seed 前など)→ 基準値を採用
            usd = it["base_usd_per_kg"]
            stale_days = 0
        else:
            usd = fresh["usd_per_kg"]
            stale_days = (target - dt.date.fromisoformat(fresh["date"])).days

        prev_nova = _prev_game_nova(conn, it["id"], date)
        nova, change_1d, clamped = convert.to_game_nova(
            usd_per_kg=usd,
            base_usd_per_kg=it["base_usd_per_kg"],
            base_nova_per_kg=it["base_nova_per_kg"],
            prev_nova=prev_nova,
            base_usd_vnd=base_usd_vnd,
            clamp_daily=clamp_daily, clamp_total=clamp_total)

        conn.execute(
            "INSERT INTO game_prices(date,instrument_id,nova_per_kg,change_1d,"
            "clamped,stale_days) VALUES(?,?,?,?,?,?) "
            "ON CONFLICT(date,instrument_id) DO UPDATE SET "
            "nova_per_kg=excluded.nova_per_kg, change_1d=excluded.change_1d, "
            "clamped=excluded.clamped, stale_days=excluded.stale_days",
            (date, it["id"], round(nova), round(change_1d, 5),
             int(clamped), stale_days))
    conn.commit()


def publish(conn, date, base_date):
    """latest.json / history_{id}.json を生成し署名して dist/ へ。"""
    db.DIST_DIR.mkdir(parents=True, exist_ok=True)
    base_usd_vnd = float(db.get_config_value(conn, "base_usd_vnd", 26300))
    base_iron = conn.execute(
        "SELECT base_usd_per_kg FROM instruments WHERE id='iron_ore'").fetchone()

    rows = conn.execute(
        "SELECT g.instrument_id id, g.nova_per_kg, g.change_1d, g.stale_days, "
        "g.clamped, p.usd_per_kg "
        "FROM game_prices g "
        "LEFT JOIN prices p ON p.instrument_id=g.instrument_id AND p.date=g.date "
        "WHERE g.date=? ORDER BY g.instrument_id", (date,)).fetchall()

    payload = {
        "date": date,
        "base": {
            "usd_vnd": base_usd_vnd,
            "iron_usd_kg": base_iron["base_usd_per_kg"] if base_iron else None,
            "base_date": base_date,
        },
        "prices": [
            {
                "id": r["id"],
                "nova_per_kg": r["nova_per_kg"],
                "usd_per_kg": r["usd_per_kg"],
                "change_1d": r["change_1d"],
                "stale_days": r["stale_days"],
                "clamped": bool(r["clamped"]),
            } for r in rows
        ],
    }
    sign.sign_payload(payload)
    (db.DIST_DIR / "latest.json").write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    # 図鑑チャート用 history(全期間)。銘柄ごとに1ファイル。
    for it in conn.execute("SELECT id FROM instruments").fetchall():
        hist = conn.execute(
            "SELECT date, nova_per_kg, change_1d FROM game_prices "
            "WHERE instrument_id=? ORDER BY date", (it["id"],)).fetchall()
        obj = {"id": it["id"],
               "history": [dict(h) for h in hist]}
        sign.sign_payload(obj)
        (db.DIST_DIR / f"history_{it['id']}.json").write_text(
            json.dumps(obj, ensure_ascii=False) + "\n", encoding="utf-8")

    return payload


def run(date: str | None = None):
    cfg = db.load_config()
    conn = db.connect()
    base_date = db.get_config_value(conn, "base_date", cfg["base_date"])
    if date is None:
        date = dt.date.today().isoformat()

    print(f"日次バッチ: date={date} base_date={base_date}")
    failures, pendings = fetch_phase(conn, cfg, date, base_date)
    game_phase(conn, date, base_date)
    payload = publish(conn, date, base_date)

    # 通知(design §3-8):取得失敗・保留発生
    lines = []
    if failures:
        lines.append(f"取得失敗 {len(failures)} 件(前日値で継続): "
                     + ", ".join(failures))
    if pendings:
        lines.append("妥当性チェックで保留(要承認):")
        for iid, ch in pendings:
            lines.append(f"  - {iid}: 前日比 {ch:+.1%}")
    clamped_ct = sum(1 for p in payload["prices"] if p["clamped"])
    stale_ct = sum(1 for p in payload["prices"] if p["stale_days"] > 0)
    print(f"  完了: 銘柄={len(payload['prices'])} "
          f"clamped={clamped_ct} stale={stale_ct} "
          f"failures={len(failures)} pending={len(pendings)}")
    if lines:
        notify.send(cfg, f"[market] {date} バッチ通知",
                    "\n".join(lines))

    # ヘルスチェック ping(design §3-9)
    hc = cfg.get("healthcheck_ping_url")
    if hc:
        try:
            import urllib.request
            urllib.request.urlopen(hc, timeout=10)
        except Exception as e:
            print(f"  healthcheck ping 失敗: {e}")
    return payload


if __name__ == "__main__":
    d = sys.argv[1] if len(sys.argv) > 1 else None
    run(d)
