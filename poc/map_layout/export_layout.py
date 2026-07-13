# -*- coding: utf-8 -*-
"""対数リング配置を Unity 用 JSON に書き出す(座標系の真実源は ring_layout.py)。

ズーム PoC(Unity)は座標計算を C# で再実装せず、この JSON を読むだけにする。
これにより ring_layout.py のユニットテスト(R2〜R5 成立)が配置の保証を担い、
Unity 側は「描画・LOD・カメラ操作の 60fps 検証」だけに責務を絞れる。

出力: data/map/map_layout.json
実行: python3 poc/map_layout/export_layout.py
"""
from __future__ import annotations

import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from ring_layout import LayoutParams, layout, load_bodies  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "data", "map", "map_layout.json")

# MVP で有効化する 5 天体(地球=ステーション拠点, 企画書14章)。
# 小惑星帯の代表は Phase 2 マイルストーンの開放目標「エロス(2,070万NOVA)」。
# PoC は 50 天体全ダミーを描画して 60fps を測るが、MVP フラグも持たせておく。
MVP_NAMES = {"月", "水星", "火星", "エロス"}  # 地球は中心ステーション扱い


def build():
    bodies = load_bodies()
    p = LayoutParams()
    res = layout(bodies, p)

    nodes = []
    for pl in res.placed:
        nodes.append({
            "no": pl.no,
            "name": pl.name,
            "band": pl.band,
            "type": pl.type,
            "x": round(pl.x, 3),
            "y": round(pl.y, 3),
            "ring_radius": round(pl.ring_radius, 3),
            "angle_deg": round(pl.angle_deg, 3),
            "role": pl.role,                 # standalone | cluster_parent | moon
            # Unity JsonUtility は null 不可。親なしは -1 で表現。
            "parent_no": pl.parent_no if pl.parent_no is not None else -1,
            "cluster_size": pl.cluster_size,  # 親=1+衛星数, その他=1
            "real_distance_km": pl.real_distance_km,
            "is_mvp": pl.name in MVP_NAMES,
        })

    doc = {
        "schema": "map_layout/v1",
        "coordinate_space": "world_px",
        "params": {
            "R0": p.R0, "K": p.K, "min_dR": p.min_dR,
            "icon_d": p.icon_d, "sub_ring_ratio": p.sub_ring_ratio,
            "cluster_collapse_px": p.cluster_collapse_px,
        },
        # ring 半径(薄い円の描画用)。距離順にソート。
        # Unity JsonUtility は辞書不可のため配列で持つ。
        "ring_radius": [
            {"band": b, "radius": round(r, 3)}
            for b, r in sorted(res.ring_radius.items(), key=lambda kv: kv[1])
        ],
        "bumped_bands": res.bumped_bands,
        "world_extent": round(max(res.ring_radius.values()), 3),
        "node_count": len(nodes),
        "overview_node_count": len(res.overview_nodes()),
        "nodes": nodes,
    }
    return doc


def main():
    doc = build()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
    rel = os.path.relpath(OUT, ROOT)
    print(f"→ {rel} 書き出し完了")
    print(f"   天体 {doc['node_count']} / オーバービュー {doc['overview_node_count']}"
          f" / world_extent {doc['world_extent']:.0f}px")
    if doc["bumped_bands"]:
        print(f"   min_ΔR 押し広げバンド: {doc['bumped_bands']}")


if __name__ == "__main__":
    main()
