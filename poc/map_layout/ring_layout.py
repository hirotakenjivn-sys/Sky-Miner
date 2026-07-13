# -*- coding: utf-8 -*-
"""対数リング配置 PoC(ゲーム Phase 0 / 付録A の座標系)。

企画書 付録A の放射状・対数距離リング配置を、実データ(data/balance/bodies.json)
から計算する基準実装。ズーム PoC(Unityで60fps測定)の"どこに何を置くか"の数理を
Unity 非依存で確定し、そのまま C# へ移植する。

== 配置ルール(付録A R1〜R6)==
- R1 対数距離リング:r = R0 + K×log10(D/D_月)。ただし実装は「バンド=リング」で、
    バンドを距離順に並べ、隣接リング間隔は log 比例、ただし下限 min_ΔR を保証する
    (R2 の重なり回避を満たすため)。実距離は別カラムで保持(付録A実装メモ)。
- R2 リング間隔の下限:ΔR ≥ アイコン径 × 2.5。
- R3 クラスター表現:惑星系は親がリング上の1点を占有し、衛星は親周囲のサブリング
    (半径 ≤ ΔR×0.4)。ズームアウト時は「土星圏⑦」のように1アイコンへ集約(セマンティックズーム)。
- R4 角度配置:同一リング内は等角+隣接リングと半ピッチ千鳥。重なり回避を数値検証。
- R5 LOD:最遠アウト時はバンド代表(クラスター集約)のみ。
- R6 縦画面:リング全体を縦長ビューポートに内接(パン許容)。

出力はワールド座標(px相当)。カメラ(ズーム)は Unity 側。
"""
from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, field

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

# 衛星と判定する区分(親の周囲サブリングへ)
MOON_TYPES = {"衛星"}


@dataclass
class LayoutParams:
    R0: float = 120.0          # B1(月)リング半径
    K: float = 60.0            # 対数スケール係数
    min_dR: float = 60.0       # リング間隔の下限(= icon_d×2.5)
    icon_d: float = 24.0       # 天体アイコン径
    label_w: float = 60.0      # ラベル幅(弧長試算用)
    margin: float = 20.0       # マージン
    sub_ring_ratio: float = 0.4  # サブリング半径 = ΔR×ratio(R3)
    cluster_collapse_px: float = 80.0  # クラスター展開閾値(画面px, R3)

    @property
    def arc_per_slot(self) -> float:
        return self.icon_d + self.label_w + self.margin


@dataclass
class Placed:
    no: int
    name: str
    band: str
    type: str
    real_distance_km: float
    ring_radius: float
    angle_deg: float
    x: float
    y: float
    role: str                  # 'standalone' | 'cluster_parent' | 'moon'
    parent_no: int | None = None
    cluster_size: int = 1      # 親の場合:1(親)+衛星数


@dataclass
class LayoutResult:
    params: LayoutParams
    placed: list = field(default_factory=list)      # Placed(50件)
    ring_radius: dict = field(default_factory=dict)  # band -> radius
    bumped_bands: list = field(default_factory=list)  # min_dR で押し広げたバンド

    def by_no(self):
        return {p.no: p for p in self.placed}

    def overview_nodes(self):
        """ズームアウト時に見えるノード(標準天体+クラスター親)。衛星は集約。"""
        return [p for p in self.placed if p.role != "moon"]


def load_bodies():
    with open(os.path.join(ROOT, "data", "balance", "bodies.json")) as f:
        return json.load(f)


def _band_rep_distance(bodies):
    """バンドごとの代表距離(最遠)。リング半径の log 計算に使う。"""
    rep = {}
    for b in bodies:
        rep[b["band"]] = max(rep.get(b["band"], 0), b["distance_km"])
    return rep


def _ring_radii(bands_ordered, rep, p: LayoutParams):
    """バンドを距離順に並べ、log比例+下限 min_dR でリング半径を決める。"""
    radii = {}
    bumped = []
    d_moon = rep[bands_ordered[0]]
    prev_band = None
    for i, band in enumerate(bands_ordered):
        if i == 0:
            radii[band] = p.R0
        else:
            log_gap = p.K * (math.log10(rep[band] / d_moon)
                             - math.log10(rep[prev_band] / d_moon))
            gap = max(log_gap, p.min_dR)
            if log_gap < p.min_dR:
                bumped.append(band)
            radii[band] = radii[prev_band] + gap
        prev_band = band
    return radii, bumped


def _cluster_band(band_bodies):
    """バンド内で親子関係を決める。衛星は直前の非衛星(惑星/準惑星)に属す。

    戻り値: slots = [(primary_body, [moon_bodies...]) or (standalone_body, [])]
    """
    slots = []
    last_primary_idx = None
    for b in band_bodies:
        if b["type"] in MOON_TYPES and last_primary_idx is not None:
            slots[last_primary_idx][1].append(b)
        else:
            slots.append((b, []))
            last_primary_idx = len(slots) - 1
    return slots


def layout(bodies=None, params: LayoutParams | None = None) -> LayoutResult:
    p = params or LayoutParams()
    if bodies is None:
        bodies = load_bodies()

    # バンドを距離順に(B1..B10 は既に順序どおりだが代表距離で厳密化)
    rep = _band_rep_distance(bodies)
    bands_ordered = sorted(rep, key=lambda b: rep[b])
    radii, bumped = _ring_radii(bands_ordered, rep, p)

    res = LayoutResult(params=p, ring_radius=radii, bumped_bands=bumped)

    # バンドごとに配置
    for bi, band in enumerate(bands_ordered):
        band_bodies = [b for b in bodies if b["band"] == band]
        slots = _cluster_band(band_bodies)
        n = len(slots)
        r = radii[band]
        pitch = 360.0 / n if n else 0.0
        # バンドごとの基準角を黄金角(137.5°)で回す。天体1個のバンドが偶数=0°/奇数=180°に
        # 揃って「直列」に見えるのを防ぎ、散らばった星図の見た目にする(半ピッチ千鳥を置換)。
        # 画面距離=ring_radius は不変なので移動時間・予算B([[travel-time-screen-based]])に影響しない。
        GOLDEN_DEG = 137.50776405
        phase = (bi * GOLDEN_DEG) % 360.0

        for si, (primary, moons) in enumerate(slots):
            ang = phase + si * pitch
            rad = math.radians(ang)
            px = r * math.cos(rad)
            py = r * math.sin(rad)
            role = "cluster_parent" if moons else "standalone"
            res.placed.append(Placed(
                no=primary["no"], name=primary["name_ja"], band=band,
                type=primary["type"], real_distance_km=primary["distance_km"],
                ring_radius=r, angle_deg=ang % 360, x=px, y=py,
                role=role, cluster_size=1 + len(moons)))
            # 衛星:親周囲のサブリング(R3)
            if moons:
                # ΔR(このリングと次リングの間隔)を推定(無ければ min_dR)
                nb = bands_ordered[bi + 1] if bi + 1 < len(bands_ordered) else None
                dR = (radii[nb] - r) if nb else p.min_dR
                sub_r = dR * p.sub_ring_ratio
                mpitch = 360.0 / len(moons)
                for mi, m in enumerate(moons):
                    mang = mi * mpitch
                    mrad = math.radians(mang)
                    mx = px + sub_r * math.cos(mrad)
                    my = py + sub_r * math.sin(mrad)
                    res.placed.append(Placed(
                        no=m["no"], name=m["name_ja"], band=band,
                        type=m["type"], real_distance_km=m["distance_km"],
                        ring_radius=r, angle_deg=(ang) % 360, x=mx, y=my,
                        role="moon", parent_no=primary["no"]))
    return res


# --------------------------------------------------------- 検証ユーティリティ
def min_pairwise_distance(points):
    """(x,y) リストの最小ペア間距離。O(n^2)(50点なので十分)。"""
    md = float("inf")
    pa = pb = None
    for i in range(len(points)):
        for j in range(i + 1, len(points)):
            dx = points[i][0] - points[j][0]
            dy = points[i][1] - points[j][1]
            d = math.hypot(dx, dy)
            if d < md:
                md, pa, pb = d, i, j
    return md, pa, pb


def export_svg(res: LayoutResult, path: str, expanded_band: str | None = "B7"):
    """配置を目視確認するための SVG を書き出す(デバッグ用・ローカル)。

    expanded_band を指定するとそのバンドのクラスターを展開(衛星も描画)。
    """
    p = res.params
    R = max(res.ring_radius.values()) + 40
    W = H = int(2 * R + 40)
    cx = cy = W / 2
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" '
             f'viewBox="0 0 {W} {H}"><rect width="{W}" height="{H}" fill="#0b1020"/>']
    # リング(薄い円)
    for band, r in res.ring_radius.items():
        parts.append(f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" '
                     f'stroke="#1e293b" stroke-width="1"/>')
    # ステーション(中心)
    parts.append(f'<circle cx="{cx}" cy="{cy}" r="6" fill="#38bdf8"/>')
    parts.append(f'<text x="{cx+8}" y="{cy-6}" fill="#7dd3fc" '
                 f'font-size="11" font-family="sans-serif">Station</text>')

    def draw(pl, color, rr):
        x, y = cx + pl.x, cy + pl.y
        parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{rr}" fill="{color}"/>')
        label = pl.name + (f" ⑂{pl.cluster_size-1}"
                           if pl.role == "cluster_parent" else "")
        parts.append(f'<text x="{x+rr+2:.1f}" y="{y+3:.1f}" fill="#cbd5e1" '
                     f'font-size="9" font-family="sans-serif">{label}</text>')

    for pl in res.placed:
        if pl.role == "moon":
            if pl.band == expanded_band:
                draw(pl, "#a78bfa", 3)
            continue
        if pl.role == "cluster_parent":
            draw(pl, "#f59e0b", 6)
        else:
            draw(pl, "#e5e7eb", 5)
    parts.append("</svg>")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("".join(parts))


if __name__ == "__main__":
    res = layout()
    print(f"配置天体数: {len(res.placed)}")
    print(f"リング半径: " + ", ".join(
        f"{b}={r:.0f}" for b, r in sorted(res.ring_radius.items(),
                                          key=lambda kv: kv[1])))
    if res.bumped_bands:
        print(f"min_ΔR で押し広げたバンド(実距離が近接): {res.bumped_bands}")
    ov = res.overview_nodes()
    print(f"オーバービュー・ノード(クラスター集約後): {len(ov)}")
    pts = [(p.x, p.y) for p in ov]
    md, ia, ib = min_pairwise_distance(pts)
    print(f"オーバービュー最小ペア間距離: {md:.1f}px "
          f"({ov[ia].name}×{ov[ib].name}) / アイコン径 {res.params.icon_d}")
    # クラスター一覧
    for p in res.placed:
        if p.role == "cluster_parent":
            print(f"  クラスター {p.band} {p.name}: 衛星{p.cluster_size-1}")
    svg_path = os.path.join(ROOT, "build", "map_layout.svg")
    export_svg(res, svg_path, expanded_band="B7")
    print(f"SVG(目視確認・B7土星圏を展開): {os.path.relpath(svg_path, ROOT)}")
