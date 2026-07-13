# -*- coding: utf-8 -*-
"""対数リング配置 PoC のユニットテスト(付録A R1〜R5 の成立性検証)。

実行: python3 -m unittest poc.map_layout.test_ring_layout
"""
from __future__ import annotations

import math
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(__file__))
from ring_layout import (LayoutParams, layout, load_bodies,   # noqa: E402
                         min_pairwise_distance)

# 期待するバンド構成(付録A A-2)
EXPECTED_BAND_COUNT = {
    "B1": 1, "B2": 4, "B3": 2, "B4": 3, "B5": 6, "B6": 5,
    "B7": 8, "B8": 6, "B9": 4, "B10": 11,
}
EXPECTED_CLUSTERS = {   # 親名 -> 衛星数
    "火星": 2, "木星": 4, "土星": 7, "天王星": 5, "海王星": 3, "冥王星": 1,
}


class TestLayout(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.res = layout()
        cls.p = cls.res.params

    def test_all_50_placed(self):
        self.assertEqual(len(self.res.placed), 50)
        nos = sorted(p.no for p in self.res.placed)
        self.assertEqual(nos, list(range(50)))

    def test_band_composition(self):
        got = {}
        for p in self.res.placed:
            got[p.band] = got.get(p.band, 0) + 1
        self.assertEqual(got, EXPECTED_BAND_COUNT)

    def test_clusters(self):
        clusters = {p.name: p.cluster_size - 1
                    for p in self.res.placed if p.role == "cluster_parent"}
        self.assertEqual(clusters, EXPECTED_CLUSTERS)
        # 衛星は必ず親を持つ
        by_no = self.res.by_no()
        for p in self.res.placed:
            if p.role == "moon":
                self.assertIsNotNone(p.parent_no)
                self.assertIn(p.parent_no, by_no)

    def test_overview_node_count(self):
        # 50 - 集約された衛星数(2+4+7+5+3+1=22) = 28
        self.assertEqual(len(self.res.overview_nodes()), 28)

    def test_R2_ring_spacing(self):
        """R2: リング間隔 ΔR ≥ アイコン径×2.5(= min_dR)。単調増加。"""
        radii = sorted(self.res.ring_radius.values())
        for i in range(1, len(radii)):
            dR = radii[i] - radii[i - 1]
            self.assertGreaterEqual(dR + 1e-9, self.p.min_dR)

    def test_R4_overview_no_overlap(self):
        """R4: オーバービュー(クラスター集約後)でアイコンが重ならない。"""
        pts = [(p.x, p.y) for p in self.res.overview_nodes()]
        md, _, _ = min_pairwise_distance(pts)
        # 最小ペア間距離 ≥ アイコン径(重なりゼロ)
        self.assertGreaterEqual(md + 1e-9, self.p.icon_d)

    def test_R4_angular_arc_fits(self):
        """R4: 各リングで1スロットあたりの弧長がアイコン径以上(角度的に収まる)。"""
        # バンドごとにオーバービュー・スロット数を数える
        slots = {}
        for p in self.res.overview_nodes():
            slots.setdefault(p.band, []).append(p)
        for band, ps in slots.items():
            r = ps[0].ring_radius
            n = len(ps)
            arc = 2 * math.pi * r / n
            self.assertGreaterEqual(arc, self.p.icon_d,
                                    f"{band}: 弧長 {arc:.1f} < icon {self.p.icon_d}")

    def test_R3_subring_within_footprint(self):
        """R3: サブリング半径 ≤ ΔR×0.5(隣接リングへ食い込まない)。"""
        radii = sorted(self.res.ring_radius.values())
        min_dR = min(radii[i] - radii[i - 1] for i in range(1, len(radii)))
        sub_r = min_dR * self.p.sub_ring_ratio
        self.assertLessEqual(sub_r, min_dR * 0.5)

    def test_moon_distinct_positions(self):
        """同一クラスター内の衛星は相異なる座標(ズームで分離可能)。"""
        by_parent = {}
        for p in self.res.placed:
            if p.role == "moon":
                by_parent.setdefault(p.parent_no, []).append((p.x, p.y))
        for parent, pts in by_parent.items():
            if len(pts) > 1:
                md, _, _ = min_pairwise_distance(pts)
                self.assertGreater(md, 0.0)

    def test_real_distance_preserved(self):
        """実距離は別カラムで保持(付録A実装メモ)。ワールド半径とは別系統。"""
        by_no = self.res.by_no()
        bodies = {b["no"]: b for b in load_bodies()}
        for no, p in by_no.items():
            self.assertAlmostEqual(p.real_distance_km, bodies[no]["distance_km"])
        # 実距離の順序とリング半径の順序が単調一致(バンド代表で)
        moon = by_no[0]
        sedna = by_no[49]
        self.assertLess(moon.ring_radius, sedna.ring_radius)
        self.assertLess(moon.real_distance_km, sedna.real_distance_km)


class TestParams(unittest.TestCase):
    def test_tighter_icons_still_valid(self):
        """アイコン径を変えても R2/R4 が保たれる(min_dR を連動させる)。"""
        p = LayoutParams(icon_d=16, min_dR=16 * 2.5)
        res = layout(params=p)
        pts = [(x.x, x.y) for x in res.overview_nodes()]
        md, _, _ = min_pairwise_distance(pts)
        self.assertGreaterEqual(md + 1e-9, p.icon_d)


if __name__ == "__main__":
    unittest.main(verbosity=2)
