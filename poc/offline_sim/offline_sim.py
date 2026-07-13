# -*- coding: utf-8 -*-
"""オフライン差分計算 PoC(ゲーム Phase 0 / 最重要ロジック)。

企画書11章・8章・CLAUDE.md の仕様をイベントキュー方式で再生する基準実装。
Unity(C#)へ移植する前の「手計算と突き合わせる仕様」として使う。

== モデル(確定仕様に基づく)==
- 収入は持ち帰り換金のみ。着艦(ステーション到着)の瞬間に売却(企画書8章)。
- 船は自動ループ:飛ぶ→掘る→満杯→帰る→売る→再出発(企画書6章)。
- 移動時間 = 実距離 ÷ 全船統一速度 S(線形式・例外なし。企画書7章)。
- オフライン中:**移動は無制限**、**採掘のみバジェット**(初期2h。企画書11章)。
    「5時間閉じた→有効採掘2時間+停止3時間」= バジェットは開始からの
    ウォールクロック上限。到達 t_arrive >= budget なら採掘しない(その場で凍結)。
    採掘中に budget を跨いだ便は満杯にならず凍結=そのオフライン中は未搬入(復帰後に継続)。
    ただし満杯後の帰還・売却は移動扱いなので budget 後でも成立する。
- 売却価格は「受取時点(=復帰時)の当日価格で一括」(企画書8章の簡素化)。
    → オフライン中に搬入された各便の kg を資源別に積算し、復帰時価格を掛ける。

== 精錬キュー(企画書8章)==
- 鉱石は精錬所へ回すと「金属」になり高く売れる(回収率 ρ、処理能力 R kg/時)。
- 精錬は station 側の処理で、採掘バジェットの対象外(継続稼働)とする。
    【要検討】採掘バジェットに精錬も含めるかは未決定。ここでは「含めない」を既定とし、
    refining_under_budget フラグで切替可能にした。
- FIFO 連続処理:搬入イベント時に「前回更新からの経過×R」だけ消費して金属化する。

すべて秒単位。角度・座標は扱わない(移動時間だけが本質)。
"""
from __future__ import annotations

import heapq
from dataclasses import dataclass, field


# ---------------------------------------------------------------- パラメータ
@dataclass
class Body:
    name: str
    distance_km: float
    resource_id: str          # 搬入資源(収入資源)
    price_nova_kg: float      # 受取時点の単価(鉱石として売る場合の単価)
    is_ore: bool = False      # True なら精錬対象(精錬所へ回せる)


@dataclass
class Ship:
    name: str
    body: Body
    mining_rate_kg_s: float    # M
    cargo_capacity_kg: float   # C
    load_unload_sec: float = 120.0   # 積込+荷降ろし/往復
    # 初期状態(オフライン開始時点)。既定=ステーションから出発直後。
    #   phase: 'outbound'|'mining'|'return'|'at_station'
    init_phase: str = "outbound"
    init_elapsed_sec: float = 0.0     # その phase の経過秒
    route_to_refinery: bool = False   # 鉱石を精錬所へ回すか


@dataclass
class Refinery:
    capacity_kg_h: float = 500.0
    recovery: float = 0.70            # ρ
    metal_price_nova_kg: float = 0.0  # 精錬後の金属の受取時点単価
    # 内部状態
    buffer_kg: float = 0.0            # 未処理の鉱石
    _last_update_s: float = 0.0
    consumed_kg: float = 0.0          # 累積処理(鉱石ベース)

    @property
    def rate_kg_s(self) -> float:
        return self.capacity_kg_h / 3600.0

    def advance(self, t: float) -> None:
        """時刻 t までバッファを処理して金属化する。"""
        if t <= self._last_update_s:
            return
        dt = t - self._last_update_s
        proc = min(self.buffer_kg, self.rate_kg_s * dt)
        self.buffer_kg -= proc
        self.consumed_kg += proc
        self._last_update_s = t

    def add_ore(self, t: float, kg: float) -> None:
        self.advance(t)
        self.buffer_kg += kg

    @property
    def metal_produced_kg(self) -> float:
        return self.consumed_kg * self.recovery


# ---------------------------------------------------------------- 結果
@dataclass
class TripRecord:
    time_s: float
    ship: str
    body: str
    resource_id: str
    kg: float
    routed_to_refinery: bool


@dataclass
class OfflineResult:
    offline_sec: float
    mining_budget_sec: float
    mining_stopped_at_s: float
    trips: list = field(default_factory=list)          # TripRecord
    delivered_kg: dict = field(default_factory=dict)   # resource_id -> kg(直売分)
    refined_metal_kg: dict = field(default_factory=dict)  # resource_id -> 金属kg
    revenue_nova: float = 0.0
    revenue_breakdown: dict = field(default_factory=dict)  # 明細
    trips_per_ship: dict = field(default_factory=dict)

    def summary(self) -> str:
        n = len(self.trips)
        return (f"{n}便帰還 / 売上 {self.revenue_nova:,.0f} NOVA "
                f"(採掘停止 {self.mining_stopped_at_s/3600:.2f}h)")


# ---------------------------------------------------------------- シミュレータ
# イベント種別
_ARRIVE_BODY = 0
_ARRIVE_STATION = 1


def simulate_offline(
    ships: list,
    unified_speed_km_min: float,
    offline_sec: float,
    mining_budget_sec: float = 7200.0,
    receipt_prices: dict | None = None,
    refinery: Refinery | None = None,
    refining_under_budget: bool = False,
) -> OfflineResult:
    """オフライン期間 [0, offline_sec] を再生し、搬入便・売上を返す。

    receipt_prices: {resource_id: nova_per_kg} 復帰時価格で上書き(None なら Body/Refinery の値)。
    """
    S = unified_speed_km_min
    T = offline_sec
    B = min(mining_budget_sec, T)  # 実際に採掘が止まる時刻
    prices = receipt_prices or {}

    res = OfflineResult(offline_sec=T, mining_budget_sec=mining_budget_sec,
                        mining_stopped_at_s=B)
    for s in ships:
        res.trips_per_ship[s.name] = 0

    def oneway_sec(body: Body) -> float:
        # 移動時間 = 距離 ÷ 統一速度(km/min)→ 秒
        return (body.distance_km / S) * 60.0

    def mine_sec(sh: Ship) -> float:
        return sh.cargo_capacity_kg / sh.mining_rate_kg_s

    # イベントキュー: (time, seq, kind, ship_index)
    pq: list = []
    seq = 0

    def push(t: float, kind: int, si: int):
        nonlocal seq
        heapq.heappush(pq, (t, seq, kind, si))
        seq += 1

    # 初期イベントを各船の初期状態から生成
    for i, sh in enumerate(ships):
        t_out = oneway_sec(sh.body)
        t_ret = t_out
        t_mine = mine_sec(sh)
        e = sh.init_elapsed_sec
        if sh.init_phase == "outbound":
            push(t_out - e, _ARRIVE_BODY, i)
        elif sh.init_phase == "mining":
            # 残り採掘後に帰還 → 到着
            rem = max(0.0, t_mine - e)
            # 満杯時刻に帰還開始、到着は +t_ret。ただし採掘バジェット判定は下で。
            # 「すでに採掘中」= body に居るので ARRIVE_BODY を now(=0)相当で再評価
            push(0.0, _ARRIVE_BODY, i)  # elapsed を持たせるため下で補正
            sh._init_mining_elapsed = e  # type: ignore
        elif sh.init_phase == "return":
            push(max(0.0, t_ret - e), _ARRIVE_STATION, i)
        elif sh.init_phase == "at_station":
            # 積込後に出発 → body 到着
            rem_l = max(0.0, sh.load_unload_sec - e)
            push(rem_l + t_out, _ARRIVE_BODY, i)
        else:
            raise ValueError(f"unknown init_phase: {sh.init_phase}")

    def sell(t: float, sh: Ship):
        """ステーション到着=売却(受取時点価格)。搬入を記録。"""
        body = sh.body
        kg = sh.cargo_capacity_kg
        routed = sh.route_to_refinery and body.is_ore and refinery is not None
        res.trips.append(TripRecord(t, sh.name, body.name, body.resource_id,
                                    kg, routed))
        res.trips_per_ship[sh.name] += 1
        if routed:
            refinery.add_ore(t, kg)
        else:
            res.delivered_kg[body.resource_id] = \
                res.delivered_kg.get(body.resource_id, 0.0) + kg

    # メインループ
    while pq:
        t, _, kind, i = heapq.heappop(pq)
        if t > T:
            continue  # 復帰後のイベントは無視
        sh = ships[i]
        t_out = oneway_sec(sh.body)
        t_ret = t_out
        t_mine = mine_sec(sh)

        if kind == _ARRIVE_BODY:
            # 採掘バジェット判定:到着が B 以降なら採掘不可(凍結)
            elapsed_mining = getattr(sh, "_init_mining_elapsed", 0.0)
            sh._init_mining_elapsed = 0.0  # 一度きり
            if t >= B:
                continue  # 凍結:このオフライン中はもう搬入しない
            remaining_mine = max(0.0, t_mine - elapsed_mining)
            full_time = t + remaining_mine
            if full_time > B:
                continue  # 満杯前にバジェット切れ → 凍結(未搬入)
            # 満杯 → 帰還(移動はバジェット外なので B 超過でも到着・売却は成立)
            push(full_time + t_ret, _ARRIVE_STATION, i)

        elif kind == _ARRIVE_STATION:
            sell(t, sh)
            # 再出発(積込 L 後に outbound)→ 次の body 到着
            push(t + sh.load_unload_sec + t_out, _ARRIVE_BODY, i)

    # 精錬所を復帰時刻まで進める
    if refinery is not None:
        refinery.advance(T)
        metal_kg = refinery.metal_produced_kg
        if metal_kg > 0:
            # 精錬対象の資源idは、精錬に回した船の resource_id を代表として集約
            ore_ids = {s.body.resource_id for s in ships
                       if s.route_to_refinery and s.body.is_ore}
            key = next(iter(ore_ids)) if len(ore_ids) == 1 else "refined_metal"
            res.refined_metal_kg[key] = res.refined_metal_kg.get(key, 0.0) + metal_kg

    # 売上(受取時点価格で一括)
    def price_of(rid: str, fallback: float) -> float:
        return prices.get(rid, fallback)

    # 直売分
    body_by_res = {}
    for s in ships:
        body_by_res.setdefault(s.body.resource_id, s.body)
    for rid, kg in res.delivered_kg.items():
        p = price_of(rid, body_by_res[rid].price_nova_kg)
        amt = kg * p
        res.revenue_nova += amt
        res.revenue_breakdown[f"{rid}(直売)"] = {"kg": kg, "price": p, "nova": amt}
    # 精錬分
    if refinery is not None:
        for rid, kg in res.refined_metal_kg.items():
            p = price_of(rid + "_metal", refinery.metal_price_nova_kg)
            amt = kg * p
            res.revenue_nova += amt
            res.revenue_breakdown[f"{rid}(精錬)"] = {"kg": kg, "price": p, "nova": amt}

    return res


if __name__ == "__main__":
    # デモ:月採掘船1隻・2時間放置
    moon = Body("月", distance_km=380000, resource_id="iron_ore",
                price_nova_kg=2762, is_ore=True)
    ship = Ship("採掘船1", body=moon, mining_rate_kg_s=3.0, cargo_capacity_kg=180)
    r = simulate_offline([ship], unified_speed_km_min=380000,  # 月片道1分
                         offline_sec=7200)
    print(r.summary())
    for tr in r.trips:
        print(f"  t={tr.time_s:6.0f}s {tr.ship} {tr.body} {tr.kg:.0f}kg")
