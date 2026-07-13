# -*- coding: utf-8 -*-
"""換算・クランプ(design §3-4,5 / 企画書9章)。

生値(usd_per_kg)→ ゲーム配信値(nova_per_kg)への変換:
  1. NOVA換算:nova_target = usd_per_kg × base_usd_vnd
     (固定直接指定=He3・希少物質は base_nova_per_kg を使用)
  2. クランプ:
     - 日次:前日 game 値 × (1 ± clamp_daily)
     - 基準日比:基準日 nova × (1 ± clamp_total)
  3. change_1d = nova / prev - 1
"""
from __future__ import annotations


def clamp(value, lo, hi):
    return max(lo, min(hi, value))


def to_game_nova(*, usd_per_kg, base_usd_per_kg, base_nova_per_kg,
                 prev_nova, base_usd_vnd, clamp_daily, clamp_total):
    """1銘柄・1日分の game 値を計算して返す。

    戻り値: (nova_per_kg, change_1d, clamped:bool)
    """
    # 基準日 NOVA(基準日比クランプの中心)
    if base_usd_per_kg is not None:
        base_nova = base_usd_per_kg * base_usd_vnd
        target = usd_per_kg * base_usd_vnd
    else:
        # 固定直接指定(He3・希少物質):市場なし=常に基準値
        base_nova = base_nova_per_kg
        target = base_nova_per_kg

    if prev_nova is None:
        prev_nova = base_nova

    clamped = False

    # 日次クランプ(前日比 ±clamp_daily)
    daily_lo = prev_nova * (1 - clamp_daily)
    daily_hi = prev_nova * (1 + clamp_daily)
    c1 = clamp(target, daily_lo, daily_hi)
    if c1 != target:
        clamped = True

    # 基準日比クランプ(±clamp_total)
    total_lo = base_nova * (1 - clamp_total)
    total_hi = base_nova * (1 + clamp_total)
    c2 = clamp(c1, total_lo, total_hi)
    if c2 != c1:
        clamped = True

    nova = c2
    change_1d = (nova / prev_nova - 1.0) if prev_nova else 0.0
    return nova, change_1d, clamped
