# -*- coding: utf-8 -*-
"""Flask アプリ(design §4 配信API + §5 管理コンソール)。

  管理者用(Phase1 の主産物):
    GET  /                     ウォッチリスト(生値 vs ゲーム値・前日比・クランプ・保留)
    GET  /instrument/<id>      90日チャート(自己完結インラインSVG)
    GET/POST /admin/input      手動入力フォーム(当日値 upsert)
    POST /admin/approve        保留の承認(pending=0)
    POST /admin/run-batch      日次バッチを手動実行

  ゲーム向け配信:
    GET  /v1/prices/latest     署名付き最新価格(dist/latest.json をそのまま返す)
    GET  /v1/prices/history?id=&days=   図鑑チャート用
"""
from __future__ import annotations

import datetime as dt
import json

from flask import (Flask, Response, redirect, render_template_string,
                   request, url_for)

import db
import batch

app = Flask(__name__)

# ---------------------------------------------------------------- helpers
def latest_date(conn):
    row = conn.execute("SELECT MAX(date) d FROM game_prices").fetchone()
    return row["d"]


def sparkline(points, w=160, h=36, pad=3):
    """nova_per_kg 系列を自己完結インラインSVGに(外部CDN不使用)。"""
    vals = [p for p in points if p is not None]
    if len(vals) < 2:
        return '<svg width="%d" height="%d"></svg>' % (w, h)
    lo, hi = min(vals), max(vals)
    span = (hi - lo) or 1
    n = len(vals)
    pts = []
    for i, v in enumerate(vals):
        x = pad + i * (w - 2 * pad) / (n - 1)
        y = h - pad - (v - lo) / span * (h - 2 * pad)
        pts.append(f"{x:.1f},{y:.1f}")
    up = vals[-1] >= vals[0]
    color = "#38bdf8" if up else "#f97316"
    return (f'<svg width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<polyline fill="none" stroke="{color}" stroke-width="1.5" '
            f'points="{" ".join(pts)}"/></svg>')


# ---------------------------------------------------------------- admin console
CONSOLE_TMPL = """
<!doctype html><html lang="ja"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>市況モニター(管理)</title>
<style>
 body{font-family:-apple-system,"Hiragino Sans",sans-serif;background:#0b1020;color:#e5e7eb;margin:0;padding:16px}
 h1{font-size:18px} .muted{color:#94a3b8;font-size:12px}
 table{border-collapse:collapse;width:100%;font-size:13px;margin-top:8px}
 th,td{padding:6px 8px;border-bottom:1px solid #1e293b;text-align:right;white-space:nowrap}
 th:first-child,td:first-child,td.l{text-align:left}
 .up{color:#38bdf8} .down{color:#f97316}
 .badge{font-size:11px;padding:1px 6px;border-radius:6px}
 .pending{background:#7c2d12;color:#fed7aa} .clamp{background:#334155;color:#cbd5e1}
 .stale{background:#3f3f46;color:#fde68a}
 a{color:#7dd3fc;text-decoration:none} .toolbar{margin:10px 0;display:flex;gap:10px;flex-wrap:wrap}
 button,input,select{background:#111827;color:#e5e7eb;border:1px solid #334155;border-radius:6px;padding:6px 10px;font-size:13px}
 button{cursor:pointer} form.inline{display:inline}
 .card{background:#0f172a;border:1px solid #1e293b;border-radius:10px;padding:12px;margin-bottom:12px}
</style></head><body>
<h1>市況モニター <span class="muted">配信日: {{date}} / 基準日: {{base_date}} / 1USD={{usd_vnd}}NOVA</span></h1>
<div class="toolbar">
  <form class="inline" method="post" action="{{url_for('run_batch')}}">
    <button>日次バッチ実行(今日)</button></form>
  <a href="{{url_for('admin_input')}}"><button>手動入力</button></a>
  <a href="/v1/prices/latest" target="_blank"><button>配信JSON(latest)</button></a>
  <span class="muted">clamped={{clamped_ct}} / stale={{stale_ct}} / pending={{pending_ct}}</span>
</div>
{% if pending %}
<div class="card"><b>保留中(妥当性チェック超過・要承認)</b>
<table><tr><th class="l">銘柄</th><th>当日raw</th><th>前日比</th><th></th></tr>
{% for p in pending %}<tr><td class="l">{{p.name_ja}} <span class="muted">{{p.id}}</span></td>
<td>{{'%.4g'|format(p.raw_value)}}</td>
<td class="{{'up' if p.chg>=0 else 'down'}}">{{'%+.1f'|format(p.chg*100)}}%</td>
<td><form class="inline" method="post" action="{{url_for('approve')}}">
<input type="hidden" name="id" value="{{p.id}}"><input type="hidden" name="date" value="{{date}}">
<button>承認</button></form></td></tr>{% endfor %}
</table></div>
{% endif %}
<table>
<tr><th class="l">銘柄</th><th>連動</th><th>生値(USD/kg)</th><th>ゲーム値(NOVA/kg)</th>
<th>前日比</th><th>フラグ</th><th>90日</th></tr>
{% for r in rows %}
<tr>
 <td class="l"><a href="{{url_for('instrument', iid=r.id)}}">{{r.name_ja}}</a>
   <span class="muted">{{r.id}}</span></td>
 <td class="l muted">{{r.link_type}}</td>
 <td>{{ '%.4g'|format(r.usd_per_kg) if r.usd_per_kg is not none else '—' }}</td>
 <td>{{ '{:,.0f}'.format(r.nova_per_kg) }}</td>
 <td class="{{'up' if r.change_1d>=0 else 'down'}}">{{'%+.2f'|format(r.change_1d*100)}}%</td>
 <td>{% if r.clamped %}<span class="badge clamp">clamp</span>{% endif %}
     {% if r.stale_days>0 %}<span class="badge stale">stale{{r.stale_days}}</span>{% endif %}</td>
 <td>{{ r.spark|safe }}</td>
</tr>
{% endfor %}
</table>
<p class="muted">生値=無加工(社内用)/ ゲーム値=日次±{{clamp_daily}}・基準日比±{{clamp_total}}クランプ後。
価格は実市況を参考にしたゲーム内換算値(NOVA)。出典クレジットを表示のこと。</p>
</body></html>
"""


@app.route("/")
def console():
    conn = db.connect()
    date = latest_date(conn)
    if not date:
        return ("game_prices が空です。先に "
                "<code>python3 server/seed.py &amp;&amp; python3 server/batch.py "
                "2026-07-11</code> を実行してください。")
    base_date = db.get_config_value(conn, "base_date")
    usd_vnd = db.get_config_value(conn, "base_usd_vnd")
    clamp_daily = db.get_config_value(conn, "clamp_daily")
    clamp_total = db.get_config_value(conn, "clamp_total")

    rows = conn.execute(
        "SELECT i.id,i.name_ja,i.link_type,g.nova_per_kg,g.change_1d,"
        "g.clamped,g.stale_days,p.usd_per_kg "
        "FROM game_prices g JOIN instruments i ON i.id=g.instrument_id "
        "LEFT JOIN prices p ON p.instrument_id=g.instrument_id AND p.date=g.date "
        "WHERE g.date=? ORDER BY g.nova_per_kg DESC", (date,)).fetchall()

    view = []
    for r in rows:
        hist = conn.execute(
            "SELECT nova_per_kg FROM game_prices WHERE instrument_id=? "
            "ORDER BY date DESC LIMIT 90", (r["id"],)).fetchall()
        series = [h["nova_per_kg"] for h in reversed(hist)]
        d = dict(r)
        d["spark"] = sparkline(series)
        view.append(d)

    pend = conn.execute(
        "SELECT p.instrument_id id, i.name_ja, p.raw_value "
        "FROM prices p JOIN instruments i ON i.id=p.instrument_id "
        "WHERE p.date=? AND p.pending=1", (date,)).fetchall()
    pend_view = []
    for p in pend:
        prev = conn.execute(
            "SELECT raw_value FROM prices WHERE instrument_id=? AND date<? "
            "AND pending=0 ORDER BY date DESC LIMIT 1",
            (p["id"], date)).fetchone()
        chg = (p["raw_value"] / prev["raw_value"] - 1) if prev else 0
        pend_view.append({**dict(p), "chg": chg})

    return render_template_string(
        CONSOLE_TMPL, date=date, base_date=base_date, usd_vnd=usd_vnd,
        clamp_daily=clamp_daily, clamp_total=clamp_total,
        rows=view, pending=pend_view,
        clamped_ct=sum(1 for r in view if r["clamped"]),
        stale_ct=sum(1 for r in view if r["stale_days"] > 0),
        pending_ct=len(pend_view))


INSTR_TMPL = """
<!doctype html><html lang="ja"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{name_ja}} 履歴</title>
<style>body{font-family:-apple-system,sans-serif;background:#0b1020;color:#e5e7eb;padding:16px}
a{color:#7dd3fc}table{border-collapse:collapse;font-size:13px;margin-top:10px}
td,th{padding:4px 10px;border-bottom:1px solid #1e293b;text-align:right}
td:first-child{text-align:left}</style></head><body>
<a href="{{url_for('console')}}">← 一覧</a>
<h1>{{name_ja}} <span style="color:#94a3b8;font-size:13px">{{iid}} / {{indicator}}</span></h1>
<div>{{ big|safe }}</div>
<table><tr><th>日付</th><th>NOVA/kg</th><th>前日比</th></tr>
{% for h in hist %}<tr><td>{{h.date}}</td><td>{{'{:,.0f}'.format(h.nova_per_kg)}}</td>
<td>{{'%+.2f'|format(h.change_1d*100)}}%</td></tr>{% endfor %}
</table></body></html>
"""


@app.route("/instrument/<iid>")
def instrument(iid):
    conn = db.connect()
    it = conn.execute("SELECT * FROM instruments WHERE id=?", (iid,)).fetchone()
    if not it:
        return "not found", 404
    hist = conn.execute(
        "SELECT date,nova_per_kg,change_1d FROM game_prices "
        "WHERE instrument_id=? ORDER BY date DESC LIMIT 90", (iid,)).fetchall()
    series = [h["nova_per_kg"] for h in reversed(hist)]
    return render_template_string(
        INSTR_TMPL, iid=iid, name_ja=it["name_ja"], indicator=it["indicator"],
        big=sparkline(series, w=520, h=120), hist=hist)


INPUT_TMPL = """
<!doctype html><html lang="ja"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>手動入力</title>
<style>body{font-family:-apple-system,sans-serif;background:#0b1020;color:#e5e7eb;padding:16px}
a{color:#7dd3fc}label{display:block;margin:8px 0 4px}
input,select{background:#111827;color:#e5e7eb;border:1px solid #334155;border-radius:6px;padding:8px;width:100%;max-width:360px}
button{background:#1d4ed8;color:#fff;border:0;border-radius:6px;padding:9px 16px;margin-top:12px;cursor:pointer}
.msg{color:#86efac;margin:8px 0}</style></head><body>
<a href="{{url_for('console')}}">← 一覧</a><h1>手動入力(当日値 upsert)</h1>
{% if msg %}<div class="msg">{{msg}}</div>{% endif %}
<form method="post">
<label>銘柄</label>
<select name="id">{% for i in insts %}<option value="{{i.id}}">{{i.name_ja}} ({{i.id}}, {{i.unit_note}})</option>{% endfor %}</select>
<label>日付</label><input name="date" value="{{today}}">
<label>生値(instruments.unit_note の単位。手動系は $/kg)</label>
<input name="raw" type="number" step="any" placeholder="例: 16.4">
<button>保存</button></form>
<p style="color:#94a3b8;font-size:12px">保存後 usd_per_kg = base_usd_per_kg ×(raw/base_raw)で再計算。
日次バッチを実行するとゲーム値へ反映される。</p>
</body></html>
"""


@app.route("/admin/input", methods=["GET", "POST"])
def admin_input():
    conn = db.connect()
    msg = None
    if request.method == "POST":
        iid = request.form["id"]
        date = request.form["date"].strip()
        raw = float(request.form["raw"])
        it = conn.execute("SELECT * FROM instruments WHERE id=?", (iid,)).fetchone()
        base_raw = it["base_raw"] if it["base_raw"] else it["base_usd_per_kg"]
        usd = it["base_usd_per_kg"] * (raw / base_raw)
        now = dt.datetime.now(dt.timezone.utc).isoformat()
        conn.execute(
            "INSERT INTO prices(instrument_id,date,raw_value,usd_per_kg,"
            "source_note,pending,created_at) VALUES(?,?,?,?,?,0,?) "
            "ON CONFLICT(instrument_id,date) DO UPDATE SET "
            "raw_value=excluded.raw_value,usd_per_kg=excluded.usd_per_kg,"
            "pending=0,source_note='manual'",
            (iid, date, raw, usd, "manual", now))
        conn.commit()
        msg = f"{it['name_ja']} @ {date} = raw {raw}(usd/kg {usd:.4g})を保存"
    insts = conn.execute(
        "SELECT id,name_ja,unit_note FROM instruments ORDER BY name_ja").fetchall()
    return render_template_string(
        INPUT_TMPL, insts=insts, today=dt.date.today().isoformat(), msg=msg)


@app.route("/admin/approve", methods=["POST"])
def approve():
    conn = db.connect()
    conn.execute("UPDATE prices SET pending=0 WHERE instrument_id=? AND date=?",
                 (request.form["id"], request.form["date"]))
    conn.commit()
    return redirect(url_for("console"))


@app.route("/admin/run-batch", methods=["POST"])
def run_batch():
    batch.run(None)
    return redirect(url_for("console"))


# ---------------------------------------------------------------- game API
@app.route("/v1/prices/latest")
def api_latest():
    f = db.DIST_DIR / "latest.json"
    if not f.exists():
        return Response('{"error":"no latest.json; run batch"}',
                        status=503, mimetype="application/json")
    return Response(f.read_text(encoding="utf-8"), mimetype="application/json",
                    headers={"Cache-Control": "max-age=3600"})


@app.route("/v1/prices/history")
def api_history():
    iid = request.args.get("id", "")
    days = int(request.args.get("days", 90))
    conn = db.connect()
    rows = conn.execute(
        "SELECT date,nova_per_kg,change_1d FROM game_prices "
        "WHERE instrument_id=? ORDER BY date DESC LIMIT ?",
        (iid, days)).fetchall()
    if not rows:
        return Response('{"error":"unknown id"}', status=404,
                        mimetype="application/json")
    obj = {"id": iid, "history": [dict(r) for r in reversed(rows)]}
    return Response(json.dumps(obj, ensure_ascii=False),
                    mimetype="application/json")


if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5057, debug=True)
