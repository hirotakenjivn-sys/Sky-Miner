# -*- coding: utf-8 -*-
"""現行モデルのオフライン差分計算 参照実装のユニットテスト(手計算突き合わせ)。

`apply_offline_ref.py`(C# FleetSimulator.ApplyOffline のミラー)を、手計算ケース・
不変性(バジェット上限・加算性・単調性)・モデル整合(Σ個数×単価=予算B)で検証する。
実行: python3 -m unittest poc.offline_sim.test_apply_offline_ref
"""
import json
import os
import unittest

from poc.offline_sim.apply_offline_ref import (
    BodyDef, DEFAULTS, apply_offline, base_counts, planet_budget,
)

MOON_SD = 120.0        # 月の画面距離(map_layout: x=120,y=0)
IRON = ("iron_ore", 2762.0)
NICKEL = ("nickel", 431320.0)
WATER = ("water", 26.0)  # バルク(閾値500未満)


class TestBalanceModel(unittest.TestCase):
    def test_single_income_resource_takes_full_budget(self):
        # 収入資源が1種なら baseCount = B / 単価(全予算を担う)
        body = BodyDef(MOON_SD, [IRON])
        B = planet_budget(MOON_SD, MOON_SD)   # 最内周なので B0=6000
        self.assertAlmostEqual(B, 6000.0)
        bc = base_counts(body, ref_screen_dist=MOON_SD)
        self.assertAlmostEqual(bc["iron_ore"], 6000.0 / 2762.0, places=9)

    def test_income_sum_equals_budget(self):
        # モデル整合:収入資源の Σ(baseCount × 単価) は予算 B に一致
        body = BodyDef(MOON_SD, [IRON, NICKEL])
        B = planet_budget(MOON_SD, MOON_SD)
        bc = base_counts(body, ref_screen_dist=MOON_SD)
        total = sum(bc[rid] * price for rid, price in body.resources)
        self.assertAlmostEqual(total, B, places=4)

    def test_bulk_is_fixed_and_excluded_from_budget(self):
        # バルク(水=26)は固定個数で、収入予算の正規化には入らない
        body = BodyDef(MOON_SD, [IRON, WATER])
        bc = base_counts(body, ref_screen_dist=MOON_SD)
        self.assertEqual(bc["water"], DEFAULTS.bulk_base_count)  # 2.0固定
        # 鉄は単独で予算Bを担う(水は予算外)ので B/price のまま
        self.assertAlmostEqual(bc["iron_ore"], 6000.0 / 2762.0, places=9)

    def test_distance_budget_grows(self):
        near = planet_budget(MOON_SD, MOON_SD)
        far = planet_budget(MOON_SD * 100, MOON_SD)
        self.assertAlmostEqual(far / near, 100.0 ** DEFAULTS.yield_dist_exp, places=4)


class TestApplyOffline(unittest.TestCase):
    def test_moon_1h_iron_handcomputed(self):
        # 月(画面距離120)・iron_ore のみ解禁・効率Lv1・1時間放置。
        # baseCount = 6000/2762 = 2.172339…
        # session = 4×5 = 20s, oneway = 120/120 = 1.0s, trip = 22.0s
        # trips = ⌊3600/22.0⌋ = ⌊163.63⌋ = 163
        # 個数 = round(163 × 2.172339) = round(354.09) = 354
        body = BodyDef(MOON_SD, [IRON])
        g = apply_offline([body], {"iron_ore"}, elapsed_sec=3600, ref_screen_dist=MOON_SD)
        self.assertEqual(g, {"iron_ore": 354})

    def test_budget_caps_at_2h(self):
        # 不在が長くても採掘は最初の2hのみ:10h放置 == 2h放置
        body = BodyDef(MOON_SD, [IRON])
        g2 = apply_offline([body], {"iron_ore"}, 7200, MOON_SD)
        g10 = apply_offline([body], {"iron_ore"}, 36000, MOON_SD)
        self.assertEqual(g2, g10)
        self.assertGreater(g2["iron_ore"], 0)

    def test_sub_budget_scales_up(self):
        body = BodyDef(MOON_SD, [IRON])
        g1 = apply_offline([body], {"iron_ore"}, 1800, MOON_SD)   # 30分
        g2 = apply_offline([body], {"iron_ore"}, 3600, MOON_SD)   # 1時間
        self.assertLess(g1["iron_ore"], g2["iron_ore"])

    def test_cargo_mult_scales_yield(self):
        body = BodyDef(MOON_SD, [IRON])
        g1 = apply_offline([body], {"iron_ore"}, 3600, MOON_SD, cargo_mult=1.0)
        g2 = apply_offline([body], {"iron_ore"}, 3600, MOON_SD, cargo_mult=2.0)
        # 天井なし:積載2倍で個数もほぼ2倍(丸め誤差±1程度)
        self.assertAlmostEqual(g2["iron_ore"], 2 * g1["iron_ore"], delta=2)

    def test_mine_rate_shortens_session_more_trips(self):
        body = BodyDef(MOON_SD, [IRON])
        g1 = apply_offline([body], {"iron_ore"}, 3600, MOON_SD, mine_rate_mult=1.0)
        g2 = apply_offline([body], {"iron_ore"}, 3600, MOON_SD, mine_rate_mult=2.0)
        # 採掘速度2倍→セッション短縮→便数増→個数増
        self.assertGreater(g2["iron_ore"], g1["iron_ore"])

    def test_locked_resource_excluded(self):
        # 鉄+ニッケルの天体で、鉄のみ解禁ならニッケルは獲得0(キーに現れない)
        body = BodyDef(MOON_SD, [IRON, NICKEL])
        g = apply_offline([body], {"iron_ore"}, 3600, MOON_SD)
        self.assertIn("iron_ore", g)
        self.assertNotIn("nickel", g)

    def test_unlocking_more_adds_keys(self):
        body = BodyDef(MOON_SD, [IRON, NICKEL])
        g = apply_offline([body], {"iron_ore", "nickel"}, 7200, MOON_SD)
        self.assertIn("iron_ore", g)
        # ニッケルは超高単価で伝説級レア→2hでは期待個数<0.5で round=0 の可能性大
        self.assertIn("iron_ore", g)  # 鉄は必ず出る

    def test_multiship_additive(self):
        body = BodyDef(MOON_SD, [IRON])
        one = apply_offline([body], {"iron_ore"}, 3600, MOON_SD)
        two = apply_offline([body, body], {"iron_ore"}, 3600, MOON_SD)
        self.assertEqual(two["iron_ore"], 2 * one["iron_ore"])

    def test_far_body_zero_trips(self):
        # trip_time > バジェット(2h)なら便数0で獲得なし
        far = BodyDef(screen_dist=120.0 * 8000.0, resources=[IRON])  # oneway=8000s
        g = apply_offline([far], {"iron_ore"}, 7200, MOON_SD)
        self.assertEqual(g, {})

    def test_below_one_second_empty(self):
        body = BodyDef(MOON_SD, [IRON])
        self.assertEqual(apply_offline([body], {"iron_ore"}, 0.5, MOON_SD), {})

    def test_station_ships_ignored(self):
        # None(=ステーション待機)の船は無視
        body = BodyDef(MOON_SD, [IRON])
        g = apply_offline([None, body, None], {"iron_ore"}, 3600, MOON_SD)
        self.assertEqual(g, {"iron_ore": 354})


class TestRealDataIntegration(unittest.TestCase):
    """実データ(bodies.json / resources.json / map_layout.json)で月2h放置を回し破綻しないか。
    画面距離(map_layout の x,y の大きさ)を移動・予算Bの基準に使う(2-ii)。"""

    @classmethod
    def setUpClass(cls):
        root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
        with open(os.path.join(root, "data/balance/bodies.json")) as f:
            b = json.load(f)
        with open(os.path.join(root, "data/balance/resources.json")) as f:
            r = json.load(f)
        with open(os.path.join(root, "data/map/map_layout.json")) as f:
            lay = json.load(f)
        cls.blist = b if isinstance(b, list) else next(v for v in b.values() if isinstance(v, list))
        cls.rlist = r if isinstance(r, list) else next(v for v in r.values() if isinstance(v, list))
        # 天体名 → 画面距離(原点からの大きさ)
        cls.sd = {n["name"]: (n["x"] ** 2 + n["y"] ** 2) ** 0.5 for n in lay["nodes"]}

    def _match(self, text):
        out, seen = [], set()
        for tok in text.replace(",", "・").split("・"):
            base = tok.split("(")[0].strip()
            if not base:
                continue
            for rr in self.rlist:
                nm = rr.get("name_ja")
                if nm and (base == nm or base.startswith(nm) or nm.startswith(base)):
                    if rr["id"] not in seen:
                        seen.add(rr["id"])
                        out.append((rr["id"], rr["nova_per_kg"]))
                    break
        return out

    def test_real_moon_2h_iron_dominates(self):
        moon = next(b for b in self.blist if b.get("name_ja") == "月")
        ref = min(self.sd.values())   # 最内周の画面距離
        body = BodyDef(self.sd["月"], self._match(moon["resources"]))
        # 全資源解禁で2h放置
        unlocked = {rid for rid, _ in body.resources}
        g = apply_offline([body], unlocked, 7200, ref)
        self.assertGreater(g.get("iron_ore", 0), 0, "鉄が採れているはず")
        # 鉄が個数で圧倒(ちまちま補填役)
        self.assertEqual(max(g, key=g.get), "iron_ore")


if __name__ == "__main__":
    unittest.main(verbosity=2)
