#!/usr/bin/env python3
# 進行シミュ(現行モデル・2026-07-14 更新)。C#の式をミラーし、序盤ペースの破綻をチェックする。
# 現行:2-ii=移動/予算Bは「画面距離」連動 / 強化は 採掘効率(個数×)+ 宇宙船増設(初期1隻) /
#       精錬所(購入で 鉄/Ni/Ti が金属化=売値2倍) / 現コスト係数。
# 「常に一番稼げる惑星を全船で掘る」前提で ROI 貪欲に購入し、初回強化/2隻目/精錬所/エロス到達を見る。
import json, math

ROOT = "/Users/hirotakenji/Desktop/space-mining-game"
def _list(d): return d if isinstance(d, list) else next(v for v in d.values() if isinstance(v, list))
blist = _list(json.load(open(f"{ROOT}/data/balance/bodies.json")))
rlist = _list(json.load(open(f"{ROOT}/data/balance/resources.json")))
levels = json.load(open(f"{ROOT}/data/balance/upgrade_curve.json"))["levels"]
layout = json.load(open(f"{ROOT}/data/map/map_layout.json"))["nodes"]

# ── BalanceOverride 現行値(ミラー)
V = 120.0            # VisualTravelSpeedWorldPerSec(片道=画面距離÷V)
B0 = 6000.0          # YieldBudgetBase
BETA = 1.4           # YieldBudgetDistanceExp(画面距離連動)
GAMMA = 1.5          # YieldRarityGamma
BULK_THR, BULK = 500.0, 2.0
ROLLS, ROLL_INT = 4, 5.0
UPG = 0.00025        # UpgradeCostScale
UNLK = 0.0025        # UnlockPriceScale
SHIP_MULT = 5.0      # ShipCostMult(宇宙船増設=強化コスト×5)
MAX_SHIPS = 6
REFINE_FACTOR = 2.0
REFINERY_COST = 300000.0
REFINABLE = {"iron_ore", "nickel", "titanium"}   # 精錬所購入で売値×2

price = {r['id']: r['nova_per_kg'] for r in rlist}
rname = {r['id']: r['name_ja'] for r in rlist}
sd = {n['name']: math.hypot(n['x'], n['y']) for n in layout}   # 画面距離(天体名→原点距離)
REF_SD = min(v for v in sd.values() if v > 1e-6)

def match(text):
    out, seen = [], set()
    for tok in text.replace(',', '・').split('・'):
        base = tok.split('(')[0].strip()
        if not base: continue
        for r in rlist:
            nm = r.get('name_ja')
            if nm and (base == nm or base.startswith(nm) or nm.startswith(base)):
                if r['id'] not in seen: seen.add(r['id']); out.append(r['id'])
                break
    return out

mine = [b for b in blist if b.get('type') != 'station' and b.get('name_ja') != '地球']
byno = {b['no']: b for b in mine}
bres = {b['no']: match(b['resources']) for b in mine}
sd_of = {b['no']: sd.get(b['name_ja'], REF_SD) for b in mine}
unlock_order = sorted({rid for no in bres for rid in bres[no]}, key=lambda rid: price[rid])

def eff(level): return levels[min(max(level, 1), len(levels)) - 1]['effect_mult']
def cost_to_next(level): return None if level >= len(levels) else levels[level]['cost'] * UPG

def budget(no): return B0 * max(1.0, sd_of[no] / REF_SD) ** BETA
def base_count(no, rid):
    p = price[rid]
    if p < BULK_THR: return BULK
    ms = bres[no]; denom = sum(price[x] ** (1 - GAMMA) for x in ms if price[x] >= BULK_THR)
    return budget(no) * p ** (-GAMMA) / denom if denom > 0 else 0.0
def oneway(no): return max(0.3, sd_of[no] / V)
def sess(): return ROLLS * ROLL_INT   # 採掘速度強化は廃止=固定20s

def run(horizon=3600):
    nova = 0.0; cargo = [1]; ships = [1]; refinery = [False]
    ures = set(); ubody = set(b['no'] for b in mine if b['unlock_price_nova'] == 0)

    def ubres():
        s = set()
        for no in ubody: s.update(bres[no])
        return s
    def next_locked():
        av = ubres()
        for rid in unlock_order:
            if rid in av and rid not in ures: return rid
        return None
    r0 = next_locked()
    if r0: ures.add(r0)  # 初期=最安(鉄)

    def trip_income(no):
        cm = eff(cargo[0]); inc = 0.0
        for rid in bres[no]:
            if rid in ures:
                mult = REFINE_FACTOR if (refinery[0] and rid in REFINABLE) else 1.0
                inc += base_count(no, rid) * cm * price[rid] * mult
        return inc
    def rate1(no): return trip_income(no) / (2 * oneway(no) + sess())
    def best1(): return max(rate1(no) for no in ubody)
    def income(): return ships[0] * best1()   # 全船を最良天体に

    def options():
        o = []
        rid = next_locked()
        if rid is not None:
            o.append(('解禁:' + rname[rid], cost_to_next(len(ures)) or 0, ('res', rid)))
        if cost_to_next(cargo[0]) is not None:
            o.append(('採掘効率->%d' % (cargo[0]+1), cost_to_next(cargo[0]), ('cargo', None)))
        if ships[0] < MAX_SHIPS:
            o.append(('宇宙船->%d隻' % (ships[0]+1), (cost_to_next(ships[0]) or 0) * SHIP_MULT, ('ship', None)))
        if not refinery[0]:
            o.append(('精錬所', REFINERY_COST, ('refinery', None)))
        for b in mine:
            no = b['no']
            if no not in ubody and b['unlock_price_nova'] > 0:
                o.append(('天体:'+b['name_ja'], b['unlock_price_nova']*UNLK, ('body', no)))
        return o
    def hyp(ka):
        k, a = ka
        if k == 'res': ures.add(a); r = income(); ures.discard(a); return r
        if k == 'cargo': cargo[0]+=1; r = income(); cargo[0]-=1; return r
        if k == 'ship': ships[0]+=1; r = income(); ships[0]-=1; return r
        if k == 'refinery': refinery[0]=True; r = income(); refinery[0]=False; return r
        if k == 'body': ubody.add(a); r = income(); ubody.discard(a); return r
    def commit(ka):
        k, a = ka
        if k=='res': ures.add(a)
        elif k=='cargo': cargo[0]+=1
        elif k=='ship': ships[0]+=1
        elif k=='refinery': refinery[0]=True
        elif k=='body': ubody.add(a)

    t=0.0; buys=0; log=[]; ms={}
    MVP={b['no']:b['name_ja'] for b in mine if b['name_ja'] in ['エロス','水星','火星','ケレス']}
    while t < horizon and buys < 400:
        cur = income()
        scored = [(((hyp(ka)-cur)/cost if cost>0 else 0), name, cost, ka, hyp(ka)-cur) for name,cost,ka in options()]
        pos = [s for s in scored if s[4] > 1e-9]
        if pos: pos.sort(reverse=True); _,name,cost,ka,_ = pos[0]
        else:
            bo=sorted([s for s in scored if s[3][0]=='body'], key=lambda s:s[2])
            if not bo: break
            _,name,cost,ka,_ = bo[0]
        if nova < cost:
            dt=(cost-nova)/cur if cur>0 else 1e18
            if t+dt > horizon: log.append((t,'STOP 次%s(%.0f) rate=%.0f/s'%(name,cost,cur))); break
            t+=dt; nova+=cur*dt
        nova-=cost; commit(ka); buys+=1
        if ka[0]=='ship' and ships[0]==2 and '2隻目' not in ms: ms['2隻目']=t
        if ka[0]=='refinery' and '精錬所' not in ms: ms['精錬所']=t
        if ka[0]=='body' and ka[1] in MVP and MVP[ka[1]] not in ms: ms[MVP[ka[1]]]=t
        log.append((t, 'BUY %-16s cost=%11.0f rate=%9.0f/s'%(name,cost,income())))
    return dict(t=t, rate=income(), cargo=cargo[0], ships=ships[0], refinery=refinery[0],
                nres=len(ures), nbody=len(ubody), buys=buys, log=log, ms=ms,
                first=(log[0][0] if log else None))

def hms(s): s=int(s); return f"{s//3600}:{(s%3600)//60:02d}:{s%60:02d}"

if __name__ == '__main__':
    print("=== 天体別 1隻rate(全資源解禁・効率Lv1・精錬なし)===")
    base=None
    for b in sorted(mine, key=lambda b: sd_of[b['no']]):
        if b['name_ja'] not in ['月','エロス','水星','火星','ケレス']: continue
        no=b['no']
        inc=sum(base_count(no,rid)*price[rid] for rid in bres[no])
        ow=oneway(no); rt=inc/(2*ow+20)
        if b['name_ja']=='月': base=rt
        print(f"  {b['name_ja']:>4} 画面距離{sd_of[no]:>6.0f} 片道{ow:>5.1f}s rate{rt:>9,.0f}/s ({rt/base:.2f}x)")

    print("\n=== 進行タイムライン(1h・ROI貪欲)===")
    r=run(3600)
    for tt,msg in r['log'][:36]: print(f"[{hms(tt)}] {msg}")
    print(f"\n初回購入: {hms(r['first']) if r['first'] else '-'}")
    for k in ['2隻目','精錬所','エロス','水星','火星']:
        print(f"  {k}: {hms(r['ms'][k]) if k in r['ms'] else '1h内未到達'}")
    print(f"1h時点: rate={r['rate']:,.0f}/s 効率Lv={r['cargo']} 船={r['ships']} 精錬所={r['refinery']} 解禁資源={r['nres']} 天体={r['nbody']} 購入={r['buys']}")
