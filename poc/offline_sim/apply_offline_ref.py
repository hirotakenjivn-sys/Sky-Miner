# -*- coding: utf-8 -*-
"""オフライン差分計算(現行モデル)の Python 参照実装。

`unity/Game/FleetSimulator.cs` の `ApplyOffline` を1対1でミラーし、手計算テストの
「仕様の真実源」にする。旧 `offline_sim.py`(kg連続採掘・着艦即売却・精錬のイベント
キュー詳細版)とは別物で、こちらは現行の確定仕様に追従する:

- 採掘は確率抽選の**個数**モデル。オフラインは期待値で確定的に計算([[mining-probabilistic]])。
- 収入は在庫格納のみ(着艦即売却は廃止 [[sell-model-change]])。→ 本参照は「資源id→個数」を返す。
- 産出個数バランスは A案・距離連動B([[yield-balance-model]]):
    B = B0 × max(1, 距離/最内周距離)^α、baseCount_i = B × 単価^(−γ) / Σ(単価^(1−γ))(収入資源)。
    バルク(単価<閾値)は固定個数。効率(CargoMult)で個数を倍化(天井なし)。
- **採掘バジェット**:最初の 2h ウォールクロックのみ採掘(不在が長くても超過分は掘らない)。
    移動は無制限=飛行中の便の帰還は成立するが、期待値近似では `trips = ⌊min(elapsed, budget)/tripTime⌋`。

数値定数は `BalanceOverride`/`FleetSimulator` に一致させること(下記 DEFAULTS)。ズレたら要更新。
"""
from __future__ import annotations
from dataclasses import dataclass, field


# ---- BalanceOverride / FleetSimulator のミラー定数(C#と一致させる)----
# 【2-ii・2026-07-14】移動時間・予算Bは「画面距離(body.Pos の大きさ)」連動に変更。
@dataclass(frozen=True)
class Consts:
    visual_travel_wps: float = 120.0       # VisualTravelSpeedWorldPerSec(片道=画面距離÷これ)
    yield_budget_base: float = 6000.0      # YieldBudgetBase (B0)
    yield_dist_exp: float = 1.4            # YieldBudgetDistanceExp (β)
    rarity_gamma: float = 1.5             # YieldRarityGamma
    bulk_price_threshold: float = 500.0   # BulkPriceThreshold
    bulk_base_count: float = 2.0          # BulkBaseCountPerSession
    rolls_per_session: int = 4            # RollsPerSession
    roll_interval: float = 5.0            # RollInterval
    offline_budget_sec: float = 7200.0    # OfflineBudgetSeconds (2h)
    min_oneway_sec: float = 0.3           # FleetSimulator の下限
    min_mine_rate: float = 0.05           # Mathf.Max(0.05, MineRateMult)


DEFAULTS = Consts()


@dataclass
class BodyDef:
    """採掘対象天体。screen_dist = 原点(ステーション)からの画面距離(body.Pos.magnitude)。
    resources = [(resource_id, base_price_nova_kg), ...]"""
    screen_dist: float
    resources: list = field(default_factory=list)


def planet_budget(screen_dist: float, ref_screen_dist: float, c: Consts = DEFAULTS) -> float:
    return c.yield_budget_base * max(1.0, screen_dist / ref_screen_dist) ** c.yield_dist_exp


def base_counts(body: BodyDef, ref_screen_dist: float, c: Consts = DEFAULTS) -> dict:
    """資源id→基準個数/session(効率Lv1時)。C# RollsFor と一致。"""
    B = planet_budget(body.screen_dist, ref_screen_dist, c)
    denom = sum(p ** (1 - c.rarity_gamma)
                for _, p in body.resources if p >= c.bulk_price_threshold)
    out = {}
    for rid, p in body.resources:
        if p < c.bulk_price_threshold:
            out[rid] = c.bulk_base_count
        else:
            out[rid] = (B * p ** (-c.rarity_gamma) / denom) if denom > 0 else 0.0
    return out


def apply_offline(
    ships: list,               # [BodyDef, ...] 各船の派遣先(StationのぶんはNoneで除外済み前提)
    unlocked_res: set,         # 解禁済み資源id
    elapsed_sec: float,
    ref_screen_dist: float,    # 最内周(採掘可能天体の最小 画面距離)
    mine_rate_mult: float = 1.0,   # CargoMult 同様、UpgradeCurve.EffectMult(MineLevel)
    cargo_mult: float = 1.0,       # UpgradeCurve.EffectMult(CargoLevel)
    c: Consts = DEFAULTS,
) -> dict:
    """現行 C# ApplyOffline のミラー。資源id→獲得個数(整数)を返す。"""
    T = min(elapsed_sec, c.offline_budget_sec)
    gains: dict = {}
    if T < 1:
        return gains
    interval = c.roll_interval / max(c.min_mine_rate, mine_rate_mult)
    session = c.rolls_per_session * interval
    for body in ships:
        if body is None:
            continue
        oneway = max(c.min_oneway_sec, body.screen_dist / c.visual_travel_wps)
        trip_time = 2 * oneway + session
        trips = int(T / trip_time)   # ⌊⌋
        if trips <= 0:
            continue
        bc = base_counts(body, ref_screen_dist, c)
        for rid, base in bc.items():
            if rid not in unlocked_res:
                continue
            exp_per_trip = base * cargo_mult
            total = round(trips * exp_per_trip)   # C# は Math.Round(銀行丸め)
            if total <= 0:
                continue
            gains[rid] = gains.get(rid, 0) + int(total)
    return gains


if __name__ == "__main__":
    # デモ:月(画面距離120・iron_ore のみ解禁)2h放置・効率Lv1
    moon = BodyDef(screen_dist=120.0, resources=[("iron_ore", 2762.0)])
    g = apply_offline([moon], unlocked_res={"iron_ore"}, elapsed_sec=7200,
                      ref_screen_dist=120.0)
    print("月2h放置 (iron_ore):", g)
