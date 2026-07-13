# -*- coding: utf-8 -*-
"""オフライン差分計算 PoC のユニットテスト。

CLAUDE.md「手計算ケースと突き合わせる」に従い、各ケースの期待値を
コメントで手計算し、実装と一致することを検証する。
実行: python3 -m unittest -v poc/offline_sim/test_offline_sim.py
"""
from __future__ import annotations

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(__file__))
from offline_sim import (Body, Ship, Refinery,       # noqa: E402
                         simulate_offline)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def _body(dist=600.0, rid="iron_ore", price=2762.0, ore=False):
    return Body("天体", distance_km=dist, resource_id=rid,
                price_nova_kg=price, is_ore=ore)


def _ship(body, M=3.0, C=180.0, L=120.0, route=False, name="船"):
    return Ship(name, body=body, mining_rate_kg_s=M, cargo_capacity_kg=C,
                load_unload_sec=L, route_to_refinery=route)


class TestBaseline(unittest.TestCase):
    """S=60km/min, 距離600 → 片道600s / 採掘60s / L120s / 周期1380s。"""

    def test_no_budget_binding(self):
        # 売却時刻 = 1260 + 1380k → 1260,2640,4020,5400,6780(<=7200)= 5便
        ship = _ship(_body())
        r = simulate_offline([ship], unified_speed_km_min=60,
                             offline_sec=7200, mining_budget_sec=7200)
        self.assertEqual(len(r.trips), 5)
        self.assertEqual(r.trips_per_ship["船"], 5)
        self.assertAlmostEqual(r.delivered_kg["iron_ore"], 900.0)
        # 売上 = 900 × 2762
        self.assertAlmostEqual(r.revenue_nova, 900 * 2762)
        # 採掘停止 = min(budget,T) = 7200
        self.assertAlmostEqual(r.mining_stopped_at_s, 7200)

    def test_first_trip_times(self):
        ship = _ship(_body())
        r = simulate_offline([ship], 60, 7200)
        times = [round(t.time_s) for t in r.trips]
        self.assertEqual(times, [1260, 2640, 4020, 5400, 6780])


class TestBudget(unittest.TestCase):
    def test_budget_stops_new_trips(self):
        # budget=3000: body到着 600(full660)→trip, 1980(full2040)→trip,
        #   3360(>=3000)→凍結。= 2便。
        ship = _ship(_body())
        r = simulate_offline([ship], 60, offline_sec=7200,
                             mining_budget_sec=3000)
        self.assertEqual(len(r.trips), 2)
        self.assertAlmostEqual(r.delivered_kg["iron_ore"], 360.0)
        self.assertAlmostEqual(r.mining_stopped_at_s, 3000)

    def test_partial_mining_freezes(self):
        # budget=2000: 到着600(full660<=2000)→trip1、到着1980(<2000 だが
        #   full2040>2000)→満杯前に凍結=未搬入。= 1便。
        ship = _ship(_body())
        r = simulate_offline([ship], 60, 7200, mining_budget_sec=2000)
        self.assertEqual(len(r.trips), 1)

    def test_return_after_budget_still_sells(self):
        # budget=700: 到着600(full660<=700)→満杯。帰還・売却は移動扱いで
        #   budget後(station1260)でも成立=1便。次の到着1980>=700→凍結。
        ship = _ship(_body())
        r = simulate_offline([ship], 60, 7200, mining_budget_sec=700)
        self.assertEqual(len(r.trips), 1)
        self.assertAlmostEqual(r.trips[0].time_s, 1260)

    def test_budget_clamped_to_offline(self):
        # T=1500 < budget2h。 T以降のイベントは無視。
        #   売却1260(<=1500)→trip1。次1980>1500→無視。= 1便。
        ship = _ship(_body())
        r = simulate_offline([ship], 60, offline_sec=1500)
        self.assertEqual(len(r.trips), 1)
        self.assertAlmostEqual(r.mining_stopped_at_s, 1500)


class TestMultiShip(unittest.TestCase):
    def test_two_ships_independent(self):
        # 船1: 距離600 → 5便(上記)
        # 船2: 距離1200 → 片道1200s,周期2580s。売却2460,5040(<=7200)= 2便
        s1 = _ship(_body(dist=600, rid="iron_ore"), name="船1")
        s2 = _ship(_body(dist=1200, rid="nickel", price=431320), name="船2")
        r = simulate_offline([s1, s2], 60, 7200)
        self.assertEqual(r.trips_per_ship["船1"], 5)
        self.assertEqual(r.trips_per_ship["船2"], 2)
        self.assertEqual(len(r.trips), 7)
        self.assertAlmostEqual(r.delivered_kg["iron_ore"], 900.0)
        self.assertAlmostEqual(r.delivered_kg["nickel"], 360.0)
        self.assertAlmostEqual(r.revenue_nova, 900 * 2762 + 360 * 431320)


class TestInitialState(unittest.TestCase):
    def test_already_mining(self):
        # 初期=採掘中30秒経過(t_mine60→残30)。t=0で満杯まで30s→帰還600s
        #   →売却630。以後 周期1380s: 630,2010,3390,4770,6150(<=7200)=5便。
        b = _body()
        ship = _ship(b, name="船")
        ship.init_phase = "mining"
        ship.init_elapsed_sec = 30
        r = simulate_offline([ship], 60, 7200)
        self.assertAlmostEqual(r.trips[0].time_s, 630)
        self.assertEqual(len(r.trips), 5)

    def test_returning(self):
        # 初期=帰還中(片道600のうち100経過→残500)。t=500 で売却=1便目。
        #   以後 周期1380: 500,1880,3260,4640,6020(<=7200)=5便。
        b = _body()
        ship = _ship(b, name="船")
        ship.init_phase = "return"
        ship.init_elapsed_sec = 100
        r = simulate_offline([ship], 60, 7200)
        self.assertAlmostEqual(r.trips[0].time_s, 500)
        self.assertEqual(len(r.trips), 5)


class TestRefining(unittest.TestCase):
    def test_refinery_fifo_queue(self):
        # 精錬所 720kg/h=0.2kg/s, ρ0.7。鉱石180kg×5便(直売なし)。
        # 手計算(advance ロジック):
        #  add(1260,180): consume0 buf180
        #  add(2640,180): dt1380×0.2=276cap, consume180(buf0) 累計180, +180
        #  add(4020,180): consume180 累計360 +180
        #  add(5400,180): consume180 累計540 +180
        #  add(6780,180): consume180 累計720 +180(buf180)
        #  final(7200): dt420×0.2=84 consume84 累計804 buf96
        # 金属 = 804×0.7 = 562.8kg
        b = _body(ore=True)
        ship = _ship(b, route=True, name="船")
        ref = Refinery(capacity_kg_h=720, recovery=0.7,
                       metal_price_nova_kg=11046)
        r = simulate_offline([ship], 60, 7200, refinery=ref)
        # 直売分は無し(全量精錬へ)
        self.assertNotIn("iron_ore", r.delivered_kg)
        self.assertAlmostEqual(ref.consumed_kg, 804.0, places=3)
        self.assertAlmostEqual(ref.buffer_kg, 96.0, places=3)
        self.assertAlmostEqual(r.refined_metal_kg["iron_ore"], 562.8, places=3)
        # 売上 = 562.8 × 11046
        self.assertAlmostEqual(r.revenue_nova, 562.8 * 11046, places=1)

    def test_refining_vs_direct_tradeoff(self):
        # 同条件で直売 vs 精錬(復帰時価格)。精錬は回収率で目減りするが
        #   金属単価が高い=二層価格の検証。ここでは金属単価を鉱石の4倍にして
        #   精錬の方が高収入になることを確認。
        b = _body(price=2762, ore=True)
        direct = simulate_offline([_ship(b, name="直")], 60, 7200)
        ref = Refinery(capacity_kg_h=100000, recovery=0.7,  # 全量即処理
                       metal_price_nova_kg=2762 * 4)
        smelt = simulate_offline([_ship(b, route=True, name="精")], 60, 7200,
                                 refinery=ref)
        # 直売 900kg×2762。精錬 900×0.7×(2762×4) > 直売
        self.assertGreater(smelt.revenue_nova, direct.revenue_nova)


class TestReceiptPricing(unittest.TestCase):
    def test_receipt_price_override(self):
        # 受取時点価格で上書き(オフライン中に相場が動いた想定)。
        ship = _ship(_body(price=2762))
        r = simulate_offline([ship], 60, 7200,
                             receipt_prices={"iron_ore": 3000})
        # 900kg × 3000(Bodyの2762ではなく受取価格)
        self.assertAlmostEqual(r.revenue_nova, 900 * 3000)


class TestIntegrationRealData(unittest.TestCase):
    """balance データ(月)と突き合わせ:月片道1分・周期5分・2hで24便。"""

    def test_moon_with_balance_speed(self):
        bal = os.path.join(ROOT, "data", "balance")
        if not os.path.exists(os.path.join(bal, "speed_curve.json")):
            self.skipTest("data/balance が無い(先に export_balance.py)")
        with open(os.path.join(bal, "speed_curve.json")) as f:
            speed = {s["band"]: s for s in json.load(f)}
        with open(os.path.join(bal, "bodies.json")) as f:
            bodies = {b["name_ja"]: b for b in json.load(f)}
        moon = bodies["月"]  # distance 380000, band B1
        S = speed["B1"]["required_speed_km_min"]  # 380000 → 片道1分
        # 片道 = 380000/380000*60 = 60s。周期 60+60+60+120 = 300s。
        b = Body("月", distance_km=moon["distance_km"], resource_id="iron_ore",
                 price_nova_kg=2762)
        ship = _ship(b)
        r = simulate_offline([ship], unified_speed_km_min=S, offline_sec=7200)
        # 売却 180 + 300k <= 7200 → k=0..23 = 24便
        self.assertEqual(len(r.trips), 24)
        self.assertAlmostEqual(r.delivered_kg["iron_ore"], 24 * 180)


if __name__ == "__main__":
    unittest.main(verbosity=2)
