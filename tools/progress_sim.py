#!/usr/bin/env python3
# 進行シミュ:C#の式をミラー。単独採掘船1隻で「常に一番稼げる解禁済み天体を掘る」前提。
# ROI貪欲に 解禁/強化/天体解禁 を買い、序盤ペースを検証。パラメータを差し替えて掃引できる。
import json

ROOT = "/Users/hirotakenji/Desktop/space-mining-game"
blist = (lambda d: d if isinstance(d, list) else next(v for v in d.values() if isinstance(v, list)))(json.load(open(f"{ROOT}/data/balance/bodies.json")))
rlist = (lambda d: d if isinstance(d, list) else next(v for v in d.values() if isinstance(v, list)))(json.load(open(f"{ROOT}/data/balance/resources.json")))
levels = json.load(open(f"{ROOT}/data/balance/upgrade_curve.json"))["levels"]

BASE_TRAVEL_KMPS = 250000.0
GAMMA = 1.5; YB_GROWTH = 1.35
BULK_THR, BULK_COUNT = 500.0, 2.0
ROLLS = 4; ROLL_INTERVAL = 5.0

price = {r['id']: r['nova_per_kg'] for r in rlist}
rname = {r['id']: r['name_ja'] for r in rlist}

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
mine_sorted = sorted(mine, key=lambda b: b['distance_km'])
rank = {b['no']: i for i, b in enumerate(mine_sorted)}
byno = {b['no']: b for b in mine}
bres = {b['no']: match(b['resources']) for b in mine}
unlock_order = sorted({rid for no in bres for rid in bres[no]}, key=lambda rid: price[rid])

def eff(level): return levels[min(max(level, 1), len(levels)) - 1]['effect_mult']

REF_DIST = min(b['distance_km'] for b in mine)   # 最内周(月)の距離=基準

def run(B0, UPG_SCALE, UNLOCK_SCALE, ALPHA=1.2, HORIZON=6*3600, verbose=False):
    # (c) 予算Bを距離連動に:B = B0 × (距離/基準)^α。α>1で遠い天体ほどrateが上がる。
    def budget(no): return B0 * (byno[no]['distance_km'] / REF_DIST) ** ALPHA
    def base_count(no, rid):
        p = price[rid]
        if p < BULK_THR: return BULK_COUNT
        ms = bres[no]; denom = sum(price[x]**(1-GAMMA) for x in ms if price[x] >= BULK_THR)
        return budget(no) * p**(-GAMMA) / denom if denom > 0 else 0.0
    def cost_to_next(level):
        return None if level >= len(levels) else levels[level]['cost'] * UPG_SCALE

    nova = 0.0; mine_lv = [1]; cargo_lv = [1]
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
    def next_unlock_cost(): return cost_to_next(len(ures)) or 0.0
    r0 = next_locked()
    if r0: ures.add(r0)   # 初期=月の最安(鉄)

    def oneway(no): return max(0.3, byno[no]['distance_km'] / BASE_TRAVEL_KMPS)
    def sess(): return ROLLS * (ROLL_INTERVAL / eff(mine_lv[0]))
    def trip_t(no): return 2*oneway(no) + sess()
    def trip_inc(no):
        cm = eff(cargo_lv[0]); return sum(base_count(no, rid)*cm*price[rid] for rid in bres[no] if rid in ures)
    def rate(no): return trip_inc(no)/trip_t(no)
    def best_rate(): return max(rate(no) for no in ubody)

    init_rate = best_rate(); init_res = [rname[x] for x in ures]

    def options():
        o = []
        rid = next_locked()
        if rid is not None: o.append(('unlock:'+rname[rid], next_unlock_cost(), ('res', rid)))
        if cost_to_next(mine_lv[0]) is not None: o.append(('mine->%d'%(mine_lv[0]+1), cost_to_next(mine_lv[0]), ('mine', None)))
        if cost_to_next(cargo_lv[0]) is not None: o.append(('cargo->%d'%(cargo_lv[0]+1), cost_to_next(cargo_lv[0]), ('cargo', None)))
        for b in mine:
            no = b['no']
            if no not in ubody and b['unlock_price_nova'] > 0:
                o.append(('body:'+b['name_ja'], b['unlock_price_nova']*UNLOCK_SCALE, ('body', no)))
        return o
    def hyp(ka):
        k, a = ka
        if k=='res': ures.add(a); r=best_rate(); ures.discard(a); return r
        if k=='mine': mine_lv[0]+=1; r=best_rate(); mine_lv[0]-=1; return r
        if k=='cargo': cargo_lv[0]+=1; r=best_rate(); cargo_lv[0]-=1; return r
        if k=='body': ubody.add(a); r=best_rate(); ubody.discard(a); return r
    def commit(ka):
        k, a = ka
        if k=='res': ures.add(a)
        elif k=='mine': mine_lv[0]+=1
        elif k=='cargo': cargo_lv[0]+=1
        elif k=='body': ubody.add(a)

    t=0.0; buys=0; log=[]; ms={}
    MVP={b['no']:b['name_ja'] for b in mine if b['name_ja'] in ['エロス','水星','火星','ケレス']}
    while t < HORIZON and buys < 500:
        cur=best_rate()
        scored=[(( (hyp(ka)-cur)/cost if cost>0 else 0), name, cost, ka, hyp(ka)-cur) for name,cost,ka in options()]
        pos=[s for s in scored if s[4] > 1e-9]
        if pos: pos.sort(reverse=True); _,name,cost,ka,dr = pos[0]
        else:
            bo=sorted([s for s in scored if s[3][0]=='body'], key=lambda s:s[2])
            if not bo: break
            _,name,cost,ka,dr = bo[0]
        if nova < cost:
            dt=(cost-nova)/cur if cur>0 else 1e18
            if t+dt > HORIZON: log.append((t,'STOP 次%s(cost=%.0f) rate=%.0f/s'%(name,cost,cur))); break
            t+=dt; nova+=cur*dt
        nova-=cost; commit(ka); buys+=1
        if not ures and next_locked(): ures.add(next_locked())
        log.append((t, 'BUY %-20s cost=%11.0f rate=%8.0f/s'%(name,cost,best_rate())))
        if ka[0]=='body' and ka[1] in MVP and MVP[ka[1]] not in ms: ms[MVP[ka[1]]]=t
    return dict(init_rate=init_rate, init_res=init_res, t=t, rate=best_rate(),
                mine_lv=mine_lv[0], cargo_lv=cargo_lv[0], nres=len(ures), nbody=len(ubody),
                buys=buys, log=log, ms=ms, first_buy=(log[0][0] if log else None), budget0=budget(0))

def hms(s):
    s=int(s); return f"{s//3600}:{(s%3600)//60:02d}:{s%60:02d}"

def body_rate_table(B0, ALPHA):
    mvp=['月','エロス','水星','火星','ケレス']; base=None; rows=[]
    for b in mine_sorted:
        if b['name_ja'] not in mvp: continue
        d=b['distance_km']; B=B0*(d/REF_DIST)**ALPHA
        ow=max(0.3,d/BASE_TRAVEL_KMPS); trip=2*ow+4*(5/1); rt=B/trip
        if b['name_ja']=='月': base=rt
        rows.append((b['name_ja'], d, B, ow, trip, rt))
    return rows, base

if __name__ == '__main__':
    print("### (c)距離連動B:α別の 天体rate(フル解禁・Lv1・cargo1)###")
    for ALPHA in [1.0, 1.2, 1.35, 1.5]:
        rows, base = body_rate_table(6000, ALPHA)
        s = "  ".join(f"{nm}:{rt/base:.2f}x" for nm,_,_,_,_,rt in rows)
        print(f"  α={ALPHA}:  {s}")
    print("  (>1.00x = 月より旨い=移動する動機あり)")

    print("\n### コスト係数×α スイープ(1h)###")
    print(f"{'B0':>6} {'upg':>8} {'unlk':>8} {'α':>4} | {'初回buy':>7} {'エロス':>8} {'水星':>8} {'買/1h':>6} {'rate@1h':>11} {'天体数':>5}")
    cfgs=[(6000,0.008,0.02,1.2),
          (6000,0.0003,0.0008,1.2),(6000,0.0003,0.0008,1.35),(6000,0.0003,0.0008,1.5),
          (6000,0.0004,0.001,1.35),(6000,0.0002,0.0005,1.35)]
    for B0,up,ul,al in cfgs:
        r=run(B0,up,ul,ALPHA=al,HORIZON=3600)
        f=lambda nm: hms(r['ms'][nm]) if nm in r['ms'] else '-'
        print(f"{B0:>6} {up:>8} {ul:>8} {al:>4} | {hms(r['first_buy']) if r['first_buy'] else '-':>7} {f('エロス'):>8} {f('水星'):>8} {r['buys']:>6} {r['rate']:>11,.0f} {r['nbody']:>5}")

    print("\n### 推奨候補の詳細タイムライン (B0=6000, upg=0.0003, unlk=0.0008, α=1.35, 2h) ###")
    r=run(6000,0.0003,0.0008,ALPHA=1.35,HORIZON=2*3600)
    for tt,msg in r['log'][:40]:
        print(f"[{hms(tt)}] {msg}")
    print(f"... 2h時点: rate={r['rate']:,.0f}/s mineLv={r['mine_lv']} cargoLv={r['cargo_lv']} 解禁資源={r['nres']} 天体={r['nbody']} 購入={r['buys']}")
    for nm in ['エロス','水星','火星','ケレス']:
        print(f"  {nm}到達: {hms(r['ms'][nm]) if nm in r['ms'] else '2h内未到達'}")
