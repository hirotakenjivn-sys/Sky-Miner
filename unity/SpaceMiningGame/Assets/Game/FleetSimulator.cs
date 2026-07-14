// 船の自動ループ(状態機械)+ 移動・航路描画・残時間チップ。
// 企画書7章:船は方針を一度出せば
//   待機 → 移動(往路)→ 採掘 → 帰還(復路)→ 売却 → 再出発
// を自動で繰り返す。入力は Fleet の割り当て(天体パネルの派遣結果)。
//
// 移動時間は統一速度S(速度カーブ)で算出:片道 = 実距離 ÷ S。S は到達フロンティアで決まり、
// 航行研究(Phase 2)で成長する。航路は派遣中の天体へシアン線、船に残時間チップを添える。
//
// この段階の割り切り(M1「数値は仮でよい」):
//   - 採掘船の単独ループのみ。輸送船=常駐+ピストンのペア運用は次イテレーション。
//   - 収入は着艦時「資源単価 × 積載 = 金額」で暫定計上(単価は基準値。当日市況連動はPhase2)。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    // オフライン復帰時の結果(留守中に何便帰還し、どの資源が何個増えたか)
    public class OfflineResult
    {
        public double seconds;
        public int trips;
        public readonly List<(string name, int count)> gains = new List<(string, int)>();
        public bool HasGains => trips > 0 && gains.Count > 0;
    }

    [DisallowMultipleComponent]
    public class FleetSimulator : MonoBehaviour
    {
        const double OfflineBudgetSeconds = 7200;   // 採掘は最大2時間ぶん(移動は無制限。GEM延長は将来)

        enum Phase { Idle, Outbound, Mining, Return, Load, Unload }

        // 現地ストレージ(天体ごとのバッファ)。採掘船が常駐して掘り込み、輸送船が搬出する。
        // 満杯で採掘停止(企画書8章)。基準容量は暫定値(xlsx未定義。将来 強化軸として真実源化)。
        // 仮:採掘船積載の数便ぶん(バランス暫定スケール適用済み)
        const float LocalStorageCap = 1500f * BalanceOverride.ShipCapacityScale;
        // 現地ストレージ(天体→資源id→個数)
        readonly Dictionary<int, Dictionary<string, int>> _localStore
            = new Dictionary<int, Dictionary<string, int>>();
        Dictionary<string, int> Local(int no)
        {
            if (!_localStore.TryGetValue(no, out var d)) { d = new Dictionary<string, int>(); _localStore[no] = d; }
            return d;
        }
        int LocalTotal(int no)
        {
            int t = 0;
            if (_localStore.TryGetValue(no, out var d)) foreach (var v in d.Values) t += v;
            return t;
        }
        public double LocalStoreKg(int no) => LocalTotal(no);
        public float LocalStoreCap => LocalStorageCap;

        class Runtime
        {
            public Phase phase = Phase.Idle;
            public float elapsed;      // 現フェーズの経過秒
            public float rollTimer;    // 採掘抽選のタイマー(5秒ごと)
            public int rollsDone;      // このセッションで消化した抽選回数
            public readonly System.Collections.Generic.Dictionary<string, int> cargo
                = new System.Collections.Generic.Dictionary<string, int>();  // 資源id→個数
            public int cargoTotal;
            public float mineProgress; // 採掘進捗 0..1(頭上ゲージ用)
            public float spawnAccum;   // 採掘VFXの生成タイマー
            public float robotTimer;   // 採掘ロボのコマ送りタイマー
            public Vector2 worldPos;
            public bool visible;
        }

        const float RollInterval = 5f;   // 5秒ごとに1回抽選(採掘速度強化で短縮)
        const int RollsPerSession = 4;   // 1セッションの抽選回数(採掘時間 = 4×間隔)。個数を使い切る単位
        // 資源ごとの「基準個数/session」(効率Lv1時)。A案([[yield-balance-model]]):
        //   惑星ごと固定予算 B、収入資源は個数 ∝ 単価^(−γ) を Σ(個数×単価)=B に正規化(高単価=伝説級レア)。
        //   バルク素材は予算から外して固定個数。効率強化(CargoMult)で個数を倍化(上限なし=天井なし)。
        readonly Dictionary<int, List<(ResourcePrice res, double baseCount)>> _rollTable
            = new Dictionary<int, List<(ResourcePrice, double)>>();
        // 最内周(採掘可能天体の最小 画面距離)。距離連動の予算 B の基準。
        double _refScreenDist;

        // 採掘中に飛び散る鉱石片(手続き生成の簡易VFX。L3採掘現場演出の軽量版)。
        class OreBit
        {
            public Transform tr;
            public SpriteRenderer sr;
            public Vector2 vel;
            public float life, maxLife;
            public bool active;
        }

        SpaceMapController _ctrl;
        readonly Dictionary<Ship, Runtime> _rt = new Dictionary<Ship, Runtime>();
        readonly Dictionary<Ship, Transform> _marker = new Dictionary<Ship, Transform>();
        readonly Dictionary<Ship, Transform> _robot = new Dictionary<Ship, Transform>();  // 採掘ロボのスプライト
        const float RobotFps = 8f;
        readonly Dictionary<int, LineRenderer> _routes = new Dictionary<int, LineRenderer>();
        readonly List<string> _arrivalLog = new List<string>();
        readonly List<OreBit> _oreBits = new List<OreBit>();
        const int MaxOreBits = 48;
        static readonly Color OreColor = new Color(0.85f, 0.62f, 0.32f, 1f);

        // 同一天体に複数隻いる時、採掘位置を被らせないためのオフセット(天体中心からのずれ)
        readonly Dictionary<Ship, Vector2> _shipOffset = new Dictionary<Ship, Vector2>();

        // 「+数量」が下から上へフェードするポップ(到着=通常サイズ / 採掘=極小)
        // resId は表示アイコンの色分け用(採掘個数の計算には一切影響しない表示メタデータ)。
        class Popup { public Vector2 worldPos; public string text; public float age, maxLife, scale; public string resId; }
        readonly List<Popup> _popups = new List<Popup>();

        static readonly Color MinerColor = new Color(0.37f, 0.83f, 0.94f, 1f);      // cyan
        static readonly Color TransportColor = new Color(0.71f, 0.49f, 0.96f, 1f);  // gem
        static readonly Color RouteColor = new Color(0.37f, 0.83f, 0.94f, 0.55f);

        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;

        // ⟲リセット用:全船のマーカーと実行時状態を破棄(次フレームで現艦隊ぶんを再生成)。
        public void ResetShipVisuals()
        {
            foreach (var kv in _marker) if (kv.Value != null) Destroy(kv.Value.gameObject);
            foreach (var kv in _robot) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _marker.Clear(); _robot.Clear(); _rt.Clear(); _shipOffset.Clear();
        }

        // 採掘中の船の位置に「つるはしロボ」アニメを再生(art/mining/robot.png があれば)。
        // 無ければ非表示(従来の鉱石片VFX+着地縮小のまま)。描画のみ・採掘計算には不干渉。
        void UpdateMiningRobot(Ship ship, Runtime rt, Vector2 spot)
        {
            var frames = SpriteBank.MiningRobotFrames();
            bool show = rt.phase == Phase.Mining && frames != null && frames.Length > 0;
            if (!_robot.TryGetValue(ship, out var tr))
            {
                if (!show) return;
                var go = new GameObject($"robot_{ship.Id}");
                go.transform.SetParent(_ctrl.MapRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 7;   // 着地した船より前面
                tr = go.transform;
                _robot[ship] = tr;
            }
            var rsr = tr.GetComponent<SpriteRenderer>();
            if (rsr.enabled != show) rsr.enabled = show;
            if (!show) return;
            rt.robotTimer += Time.deltaTime * _ctrl.State.TimeScale;
            // ピンポン再生(0→末→0)。前進コマ→後退コマで正味の移動が相殺され「その場で採掘」に見える。
            int nf = frames.Length;
            int f = nf <= 1 ? 0 : (int)(rt.robotTimer * RobotFps) % (2 * (nf - 1));
            if (f >= nf) f = 2 * (nf - 1) - f;
            rsr.sprite = frames[f];
            tr.position = new Vector3(spot.x, spot.y, -0.1f);
            tr.localScale = Vector3.one * _ctrl.CurrentIconWorld * 0.45f;
        }

        // オフライン進行:不在秒数ぶん、派遣中の各船が何便こなせたかを期待値で計算して在庫へ加算。
        // 採掘は2時間バジェット、移動は無制限(=Tを2hで頭打ち)。抽選は期待値(目標×積載)で確定的に。
        public OfflineResult ApplyOffline(double elapsedSec)
        {
            var res = new OfflineResult();
            double T = System.Math.Min(elapsedSec, OfflineBudgetSeconds);
            res.seconds = T;
            if (T < 1) return res;

            var byId = new Dictionary<string, int>();
            var nameById = new Dictionary<string, string>();
            float cargoMult = (float)_ctrl.CargoMult;
            float interval = RollInterval / Mathf.Max(0.05f, (float)_ctrl.MineRateMult);
            float session = RollsPerSession * interval;

            foreach (var ship in _ctrl.Fleet.Ships)
            {
                if (ship.AssignedBodyNo == CelestialBody.StationNo) continue;
                var body = _ctrl.Data.ByNo(ship.AssignedBodyNo);
                if (body == null || body.Master == null) continue;

                float oneway = Oneway(body);
                float tripTime = 2f * oneway + session;
                int trips = (int)(T / tripTime);
                if (trips <= 0) continue;
                res.trips += trips;

                var rolls = RollsFor(body);
                foreach (var (r, baseCount) in rolls)
                {
                    if (!_ctrl.IsResourceUnlocked(r.id)) continue;   // 解禁済みの採掘資源のみ
                    double expPerTrip = baseCount * cargoMult;       // 1便=1セッションの期待個数(天井なし)
                    int total = (int)System.Math.Round(trips * expPerTrip);
                    if (total <= 0) continue;
                    byId.TryGetValue(r.id, out int cur); byId[r.id] = cur + total;
                    nameById[r.id] = r.name_ja;
                    _ctrl.Inventory.Add(r.id, r.name_ja, total);
                }
            }
            foreach (var kv in byId) res.gains.Add((nameById[kv.Key], kv.Value));
            return res;
        }

        void Update()
        {
            if (_ctrl == null || _ctrl.Fleet == null) return;
            float dt = Time.deltaTime * _ctrl.State.TimeScale;
            var stats = _ctrl.Fleet.Stats;
            Vector2 station = Vector2.zero;

            // ペア運用(中盤スキル解禁後にのみ発生)= 同一天体に専用採掘船と輸送船が両方居る時。
            // 採掘船は常駐、輸送船がピストン。序盤は宇宙船=Transportのみなのでペアは成立せず単独ループ。
            var minerBodies = new HashSet<int>();
            var transportBodies = new HashSet<int>();
            foreach (var s in _ctrl.Fleet.Ships)
            {
                if (s.AssignedBodyNo == CelestialBody.StationNo) continue;
                if (s.Type == ShipType.Miner) minerBodies.Add(s.AssignedBodyNo);
                else transportBodies.Add(s.AssignedBodyNo);
            }
            var pairedBodies = new HashSet<int>(minerBodies);
            pairedBodies.IntersectWith(transportBodies);

            ComputeShipOffsets();   // 同一天体の複数隻を被らせない

            // 待機(未派遣)船は地球(ステーション)周囲に駐機表示する(item④)。複数はリング状に並べる。
            int idleCount = 0;
            foreach (var s in _ctrl.Fleet.Ships)
                if (s.AssignedBodyNo == CelestialBody.StationNo) idleCount++;
            int idleSeen = 0;

            foreach (var ship in _ctrl.Fleet.Ships)
            {
                var rt = GetRuntime(ship);
                int target = ship.AssignedBodyNo;

                if (target == CelestialBody.StationNo)
                {
                    rt.phase = Phase.Idle; rt.elapsed = 0; rt.cargo.Clear(); rt.cargoTotal = 0;
                    // 地球のすぐ外周に駐機(1隻=真上、複数=均等リング)。機首は外向き(SetMarker)。
                    float ringR = _ctrl.CurrentIconWorld * 0.95f;
                    float a = (idleCount <= 1 ? 90f : 90f + idleSeen * (360f / idleCount)) * Mathf.Deg2Rad;
                    Vector2 dockPos = station + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR;
                    rt.worldPos = dockPos; rt.visible = true;
                    SetMarker(ship, dockPos, true);
                    UpdateMiningRobot(ship, rt, dockPos);   // 待機=採掘してないのでロボは隠れる
                    idleSeen++;
                    continue;
                }

                var body = _ctrl.Data.ByNo(target);
                if (body == null || body.Master == null) continue;

                bool paired = pairedBodies.Contains(target);
                if (ship.Type == ShipType.Transport)
                {
                    if (paired) TransportPiston(ship, rt, body, dt, stats, station);
                    else SoloMiner(ship, rt, body, dt, stats, station);
                }
                else // 専用採掘船
                {
                    if (paired) ResidentMine(ship, rt, body, dt, stats);
                    else SoloMiner(ship, rt, body, dt, stats, station);
                }
            }

            UpdateOreBits(dt);
            UpdatePopups(dt);
            UpdateRoutes(station);
            RefreshHud();   // uGUI HUD の数値/表示更新
        }

        // 同一天体に複数隻いる時、天体中心から放射状にずらして採掘位置を分散させる。
        void ComputeShipOffsets()
        {
            _shipOffset.Clear();
            var byBody = new Dictionary<int, List<Ship>>();
            foreach (var s in _ctrl.Fleet.Ships)
            {
                if (s.AssignedBodyNo == CelestialBody.StationNo) continue;
                if (!byBody.TryGetValue(s.AssignedBodyNo, out var list))
                { list = new List<Ship>(); byBody[s.AssignedBodyNo] = list; }
                list.Add(s);
            }
            // 分散半径は惑星の大きさを超えない(惑星半径=アイコン径×0.5。その内側に収める)
            float r = _ctrl.CurrentIconWorld * 0.28f;
            foreach (var kv in byBody)
            {
                var list = kv.Value; int n = list.Count;
                if (n <= 1) { _shipOffset[list[0]] = Vector2.zero; continue; }
                for (int i = 0; i < n; i++)
                {
                    float ang = (i / (float)n) * Mathf.PI * 2f;
                    _shipOffset[list[i]] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
            }
        }

        // その船の採掘位置(天体中心 + 被り回避オフセット)
        Vector2 Spot(Ship ship, CelestialBody body)
            => body.Pos + (_shipOffset.TryGetValue(ship, out var o) ? o : Vector2.zero);

        // 画面(対数配置)上の距離=原点(ステーション)から天体アイコンまでの大きさ。
        // 2-ii:移動時間・予算B ともにこれに連動させ、遠方の「這うような飛行/10分便」を解消する。
        float ScreenDist(CelestialBody body) => body.Pos.magnitude;

        // 片道秒 = 画面距離 ÷ 視覚速度(全船統一・一定の画面速度で自然に飛ぶ)。
        float Oneway(CelestialBody body)
            => Mathf.Max(0.3f, (float)(ScreenDist(body) / BalanceOverride.VisualTravelSpeedWorldPerSec));

        // 最内周(採掘可能天体の最小 画面距離)。距離連動の予算 B の基準にする。
        double RefScreenDist()
        {
            if (_refScreenDist <= 0)
            {
                double min = double.MaxValue;
                foreach (var b in _ctrl.Data.Bodies)
                    if (!b.IsStation && b.Master != null && b.Pos.magnitude > 0)
                        min = System.Math.Min(min, b.Pos.magnitude);
                _refScreenDist = min < double.MaxValue ? min : 1.0;
            }
            return _refScreenDist;
        }

        // 惑星ごとの1セッション平均収入予算 B = B0 × (画面距離 / 最内周画面距離)^β。遠い=旨い。
        double PlanetBudget(CelestialBody body)
            => BalanceOverride.YieldBudgetBase
             * System.Math.Pow(System.Math.Max(1.0, ScreenDist(body) / RefScreenDist()),
                               BalanceOverride.YieldBudgetDistanceExp);

        // 天体の抽選テーブル(資源, 基準個数/session)。A案の予算配分([[yield-balance-model]])。
        List<(ResourcePrice res, double baseCount)> RollsFor(CelestialBody body)
        {
            if (_rollTable.TryGetValue(body.No, out var r)) return r;
            var matched = _ctrl.Prices.MatchBodyResources(body.Resources);
            double B = PlanetBudget(body);
            double gamma = BalanceOverride.YieldRarityGamma;
            double thr = BalanceOverride.BulkPriceThreshold;
            // 収入資源(単価≥閾値)の正規化分母 Σ 単価^(1−γ)
            double denom = 0;
            foreach (var res in matched)
                if (res.nova_per_kg >= thr) denom += System.Math.Pow(res.nova_per_kg, 1.0 - gamma);
            var list = new List<(ResourcePrice, double)>();
            foreach (var res in matched)
            {
                double baseCount;
                if (res.nova_per_kg < thr)
                    baseCount = BalanceOverride.BulkBaseCountPerSession;   // バルク=固定(暫定)
                else
                    baseCount = denom > 0 ? B * System.Math.Pow(res.nova_per_kg, -gamma) / denom : 0;
                list.Add((res, baseCount));
            }
            _rollTable[body.No] = list;
            return list;
        }

        // 1回の抽選。ロボットが解禁済みの資源だけを対象に、期待個数モデルで加算する(天井なし)。
        //   この抽選の期待個数 e = 基準個数 × 積載強化 ÷ セッション抽選回数。
        //   個数 = 整数部(確定) + 端数(確率で+1)。→ 効率が高いほど大量に、超レアは端数確率で稀に出る。
        void RollInto(CelestialBody body, Dictionary<string, int> cargo, ref int total, Vector2 pos)
        {
            var rolls = RollsFor(body);
            double cargoMult = _ctrl.CargoMult;
            foreach (var (res, baseCount) in rolls)
            {
                if (!_ctrl.IsResourceUnlocked(res.id)) continue;   // 解禁済みの採掘資源のみ
                double e = baseCount * cargoMult / RollsPerSession;
                int n = (int)e;
                if (UnityEngine.Random.value < e - n) n++;         // 端数を確率で+1(上限なし)
                if (n <= 0) continue;
                cargo.TryGetValue(res.id, out int c);
                cargo[res.id] = c + n;
                total += n;
                _popups.Add(new Popup { worldPos = pos, text = $"+{n} {res.name_ja}",
                    age = 0f, maxLife = 1.0f, scale = 0.5f, resId = res.id });     // 極小
            }
        }

        // ------------------------------------------------------------ 採掘VFX(鉱石片)
        void EmitOre(Runtime rt, Vector2 pos, float dt)
        {
            const float interval = 0.18f;   // この間隔ごとに1片
            rt.spawnAccum += dt;
            int guard = 0;
            while (rt.spawnAccum >= interval && guard++ < 8)
            {
                rt.spawnAccum -= interval;
                SpawnOre(pos);
            }
        }

        void SpawnOre(Vector2 pos)
        {
            var b = GetFreeOreBit();
            if (b == null) return;
            float ic = _ctrl.CurrentIconWorld;
            Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
            // 小さく・飛散範囲も狭く
            b.vel = dir * (ic * UnityEngine.Random.Range(0.35f, 0.9f)) + Vector2.up * (ic * 0.3f);
            b.maxLife = UnityEngine.Random.Range(0.3f, 0.5f);
            b.life = b.maxLife;
            b.tr.position = new Vector3(pos.x, pos.y, 0);
            b.tr.localScale = Vector3.one * ic * 0.07f;
            b.sr.color = OreColor;
            b.sr.enabled = true;
            b.active = true;
        }

        OreBit GetFreeOreBit()
        {
            foreach (var b in _oreBits) if (!b.active) return b;
            if (_oreBits.Count >= MaxOreBits) return null;
            var go = new GameObject("orebit");
            go.transform.SetParent(_ctrl.MapRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _ctrl.CircleSprite;
            sr.sortingOrder = 5;
            sr.enabled = false;
            var nb = new OreBit { tr = go.transform, sr = sr };
            _oreBits.Add(nb);
            return nb;
        }

        void UpdateOreBits(float dt)
        {
            float ic = _ctrl.CurrentIconWorld;
            foreach (var b in _oreBits)
            {
                if (!b.active) continue;
                b.life -= dt;
                if (b.life <= 0f) { b.active = false; b.sr.enabled = false; continue; }
                b.tr.position += (Vector3)(b.vel * dt);
                b.vel *= Mathf.Max(0f, 1f - 2f * dt);   // 空気抵抗で減速
                float a = Mathf.Clamp01(b.life / b.maxLife);
                var c = OreColor; c.a = a; b.sr.color = c;
                b.tr.localScale = Vector3.one * ic * 0.07f * a;
            }
        }

        void UpdatePopups(float dt)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                _popups[i].age += dt;
                if (_popups[i].age >= _popups[i].maxLife) _popups.RemoveAt(i);
            }
        }

        // 単独ループ:1隻で 飛ぶ→掘る(固定回数の抽選セッション)→帰る→在庫格納→再出発。
        void SoloMiner(Ship ship, Runtime rt, CelestialBody body, float dt, ShipStats stats, Vector2 station)
        {
            float oneway = Oneway(body); // 画面距離比例(2-ii)
            float interval = RollInterval / Mathf.Max(0.05f, (float)_ctrl.MineRateMult); // 採掘速度で短縮
            if (rt.phase == Phase.Idle || rt.phase == Phase.Load || rt.phase == Phase.Unload)
            { rt.phase = Phase.Outbound; rt.elapsed = 0; rt.rollTimer = 0; rt.rollsDone = 0; rt.cargo.Clear(); rt.cargoTotal = 0; }
            rt.elapsed += dt;

            Vector2 spot = Spot(ship, body);   // 被り回避オフセット込みの採掘位置
            Vector2 p;
            switch (rt.phase)
            {
                case Phase.Outbound:
                    p = Vector2.Lerp(station, spot, Mathf.Clamp01(rt.elapsed / oneway));
                    if (rt.elapsed >= oneway) { rt.phase = Phase.Mining; rt.elapsed = 0; rt.rollTimer = 0; rt.rollsDone = 0; }
                    break;
                case Phase.Mining:
                    p = spot;
                    EmitOre(rt, spot, dt);   // 採掘してる感の粒子
                    rt.rollTimer += dt;      // 間隔ごとに抽選(セッションの回数を消化)
                    while (rt.rollTimer >= interval && rt.rollsDone < RollsPerSession)
                    {
                        rt.rollTimer -= interval;
                        RollInto(body, rt.cargo, ref rt.cargoTotal, spot);
                        rt.rollsDone++;
                    }
                    // 抽選の合間の経過も足してゲージを連続的に(カクつき解消)。0→1 を滑らかに満たす。
                    rt.mineProgress = Mathf.Clamp01((rt.rollsDone + rt.rollTimer / interval) / RollsPerSession);
                    if (rt.rollsDone >= RollsPerSession) { rt.phase = Phase.Return; rt.elapsed = 0; }
                    break;
                default: // Return
                    p = Vector2.Lerp(spot, station, Mathf.Clamp01(rt.elapsed / oneway));
                    if (rt.elapsed >= oneway)
                    {
                        DepositCargo(body, rt.cargo);
                        rt.cargo.Clear(); rt.cargoTotal = 0;
                        rt.phase = Phase.Outbound; rt.elapsed = 0;
                    }
                    break;
            }
            rt.worldPos = p; rt.visible = true;
            // 機首を進行方向へ。往路=惑星へ / 着地(採掘)=そこから90°反時計回り / 帰路=180°(地球へ)。
            float faceOut = Mathf.Atan2(spot.y, spot.x) * Mathf.Rad2Deg - 90f;
            float facing = rt.phase == Phase.Mining ? faceOut + 90f
                         : rt.phase == Phase.Return ? faceOut + 180f
                         : faceOut;
            // 採掘中は着地して小さく(=惑星に降りた表現)。往復中は通常サイズ。
            SetMarker(ship, p, true, rt.phase == Phase.Mining ? LandedScale : 1f, facing);
            UpdateMiningRobot(ship, rt, spot);
        }

        // 採掘船・常駐:天体に留まり現地ストレージへ抽選で掘り込む。満杯で採掘停止。
        void ResidentMine(Ship ship, Runtime rt, CelestialBody body, float dt, ShipStats stats)
        {
            Vector2 spot = Spot(ship, body);
            int capUnits = Mathf.Max(1, Mathf.RoundToInt(LocalStorageCap));
            float interval = RollInterval / Mathf.Max(0.05f, (float)_ctrl.MineRateMult);
            rt.phase = Phase.Mining; rt.worldPos = spot; rt.visible = true;
            if (LocalTotal(body.No) < capUnits)
            {
                rt.rollTimer += dt;
                while (rt.rollTimer >= interval && LocalTotal(body.No) < capUnits)
                {
                    rt.rollTimer -= interval;
                    int dummy = 0;
                    RollInto(body, Local(body.No), ref dummy, spot);
                }
                EmitOre(rt, spot, dt);   // 満杯停止中は飛散なし
            }
            rt.mineProgress = (float)LocalTotal(body.No) / capUnits;
            SetMarker(ship, spot, true);
        }

        // 輸送船・ピストン:ステーション⇔天体を往復し、現地ストレージを搬出してホーム在庫へ。
        void TransportPiston(Ship ship, Runtime rt, CelestialBody body, float dt, ShipStats stats, Vector2 station)
        {
            float oneway = Oneway(body); // 画面距離比例(2-ii)
            float half = Mathf.Max(1f, stats.load_unload_sec * 0.5f);
            int capUnits = Mathf.Max(1, Mathf.RoundToInt(
                stats.transport_capacity_kg * (float)_ctrl.CargoMult * BalanceOverride.ShipCapacityScale));
            if (rt.phase == Phase.Idle || rt.phase == Phase.Mining)
            { rt.phase = Phase.Outbound; rt.elapsed = 0; rt.cargo.Clear(); rt.cargoTotal = 0; }
            rt.elapsed += dt;

            Vector2 spot = Spot(ship, body);
            Vector2 p;
            switch (rt.phase)
            {
                case Phase.Outbound:
                    p = Vector2.Lerp(station, spot, Mathf.Clamp01(rt.elapsed / oneway));
                    if (rt.elapsed >= oneway) { rt.phase = Phase.Load; rt.elapsed = 0; rt.cargo.Clear(); rt.cargoTotal = 0; }
                    break;
                case Phase.Load:
                    p = spot;
                    if (rt.cargoTotal <= 0)
                        rt.cargoTotal = TakeFromLocal(Local(body.No), rt.cargo, capUnits);
                    if (rt.cargoTotal > 0 && rt.elapsed >= half) { rt.phase = Phase.Return; rt.elapsed = 0; }
                    break;
                case Phase.Return:
                    p = Vector2.Lerp(spot, station, Mathf.Clamp01(rt.elapsed / oneway));
                    if (rt.elapsed >= oneway) { rt.phase = Phase.Unload; rt.elapsed = 0; }
                    break;
                default: // Unload
                    p = station;
                    if (rt.elapsed >= half)
                    {
                        DepositCargo(body, rt.cargo);
                        rt.cargo.Clear(); rt.cargoTotal = 0; rt.phase = Phase.Outbound; rt.elapsed = 0;
                    }
                    break;
            }
            rt.worldPos = p; rt.visible = true;
            SetMarker(ship, p, true);
        }

        // 現地ストレージから最大 max 個を貪欲に取り出して cargo へ。取り出せた総数を返す。
        int TakeFromLocal(Dictionary<string, int> local, Dictionary<string, int> cargo, int max)
        {
            int taken = 0;
            var keys = new List<string>(local.Keys);
            foreach (var k in keys)
            {
                if (taken >= max) break;
                int take = Mathf.Min(local[k], max - taken);
                if (take <= 0) continue;
                local[k] -= take; if (local[k] <= 0) local.Remove(k);
                cargo.TryGetValue(k, out int c); cargo[k] = c + take;
                taken += take;
            }
            return taken;
        }

        // 派遣中の天体へシアンの航路線を張る(割り当てのある天体のみ表示)
        void UpdateRoutes(Vector2 station)
        {
            var active = new HashSet<int>();
            foreach (var s in _ctrl.Fleet.Ships)
                if (s.AssignedBodyNo != CelestialBody.StationNo) active.Add(s.AssignedBodyNo);

            float width = Mathf.Max(1f, _ctrl.CurrentIconWorld * 0.06f);
            foreach (var no in active)
            {
                var body = _ctrl.Data.ByNo(no);
                if (body == null) continue;
                if (!_routes.TryGetValue(no, out var lr)) { lr = MakeRoute(no); _routes[no] = lr; }
                lr.widthMultiplier = width;
                lr.SetPosition(0, new Vector3(station.x, station.y, 0.5f));
                lr.SetPosition(1, new Vector3(body.Pos.x, body.Pos.y, 0.5f));
                if (!lr.enabled) lr.enabled = true;
            }
            // 非アクティブ航路は隠す
            foreach (var kv in _routes)
                if (!active.Contains(kv.Key) && kv.Value.enabled) kv.Value.enabled = false;
        }

        LineRenderer MakeRoute(int no)
        {
            var go = new GameObject($"route_{no}");
            go.transform.SetParent(_ctrl.MapRoot, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = RouteColor;
            lr.sortingOrder = 0; // 天体アイコンより後ろ、リングと同層
            return lr;
        }

        // 着艦=ホーム在庫へ資源ごとに格納(NOVAは店で売るまで増えない)。到着ポップは合計個数。
        void DepositCargo(CelestialBody body, Dictionary<string, int> cargo)
        {
            int total = 0;
            foreach (var kv in cargo)
            {
                if (kv.Value <= 0) continue;
                var rp = _ctrl.Prices.ById(kv.Key);
                string name = rp?.name_ja ?? kv.Key;
                _ctrl.Inventory.Add(kv.Key, name, kv.Value);
                total += kv.Value;
            }
            if (total <= 0) return;
            _arrivalLog.Insert(0, $"+{total}");
            if (_arrivalLog.Count > 5) _arrivalLog.RemoveAt(_arrivalLog.Count - 1);
            // ステーション上に「+合計」を下から上へフェードアウトで出す(通常サイズ)
            _popups.Add(new Popup { worldPos = Vector2.zero, text = $"+{total}", age = 0f, maxLife = 1.2f, scale = 1f });
        }

        Runtime GetRuntime(Ship s)
        {
            if (!_rt.TryGetValue(s, out var rt)) { rt = new Runtime(); _rt[s] = rt; }
            return rt;
        }

        const float LandedScale = 0.5f;   // 着地(採掘中)は宇宙船を小さく=惑星に降りた表現

        void SetMarker(Ship ship, Vector2 pos, bool visible, float scaleMul = 1f, float? facingDeg = null)
        {
            if (!_marker.TryGetValue(ship, out var tr))
            {
                var go = new GameObject($"ship_{ship.Id}_{ship.Type}");
                go.transform.SetParent(_ctrl.MapRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                // 円マーカー→宇宙船シルエット(PNG art/ships/… があればそれ、無ければ手続きくさび形)。
                // 手続き版はグレースケールなので採掘/輸送の色で tint(色分けは維持)。
                bool shipIsArt;
                sr.sprite = SpriteBank.Ship(ship.Type, out shipIsArt);
                sr.color = shipIsArt ? Color.white
                                     : (ship.Type == ShipType.Miner ? MinerColor : TransportColor);
                sr.sortingOrder = 6;
                tr = go.transform;
                _marker[ship] = tr;
            }
            var srr = tr.GetComponent<SpriteRenderer>();
            if (srr.enabled != visible) srr.enabled = visible;
            if (!visible) return;
            tr.position = new Vector3(pos.x, pos.y, 0);
            tr.localScale = Vector3.one * _ctrl.CurrentIconWorld * 0.85f * scaleMul;
            // 機首(スプライトは +Y=機首)を向ける。facingDeg 指定があればそれ(進行方向/着地の向き)、
            // 無ければ原点(地球)から外向きの放射方向。描画のみ・移動計算には不干渉。
            if (facingDeg.HasValue)
            {
                tr.rotation = Quaternion.Euler(0f, 0f, facingDeg.Value);
            }
            else if (pos.sqrMagnitude > 1e-3f)
            {
                float ang = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg - 90f;
                tr.rotation = Quaternion.Euler(0f, 0f, ang);
            }
        }

        // ------------------------------------------------------------ HUD(uGUI)+ 採掘ゲージ/ポップ(OnGUI)
        // 採掘ゲージ・到着ポップはワールド座標→画面のオーバーレイ演出なので OnGUI のまま(要件どおり維持)。
        // HUD(残高・在庫・早送り・リセット・下部メニューバー・帰還ログ)は uGUI(Canvas)へ移行。
        GUIStyle _sPopup; int _sPopupBase;
        Texture2D _texGaugeBg, _texGaugeFill, _texGaugeFrame;
        bool _gaugeReady;

        Text _hudNova, _hudInv, _hudFf, _hudLog;
        RectTransform _bottomBar;
        bool _hudBuilt;

        void Start() { BuildHud(); }

        void BuildHud()
        {
            if (_hudBuilt || _ctrl == null) return;
            _hudBuilt = true;
            var root = UiRoot.Instance.Root;

            // 上部中央:金貨+残高(NOVA)/ 鉱石+在庫合計。ラベルはクリックを吸わない(裏でマップをパン可能)。
            var top = UiKit.Node("HudTop", root);
            top.anchorMin = new Vector2(0.5f, 1); top.anchorMax = new Vector2(0.5f, 1); top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(800, 200); top.anchoredPosition = new Vector2(0, -36);

            var coin = UiKit.Icon("Coin", top, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.5f, 1f), -8, -46, 66, 66, 1f);
            _hudNova = UiKit.Label("Nova", top, "0", UiKit.FTitle, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_hudNova.rectTransform, new Vector2(0.5f, 1f), 8, -46, 400, 80, 0f);

            var cube = UiKit.Icon("Cube", top, UiKit.Cube);
            UiKit.Place(cube.rectTransform, new Vector2(0.5f, 1f), -8, -130, 48, 48, 1f);
            _hudInv = UiKit.Label("Inv", top, "0", UiKit.FSub, UiKit.Sub, TextAnchor.MiddleLeft);
            UiKit.Place(_hudInv.rectTransform, new Vector2(0.5f, 1f), 8, -130, 400, 60, 0f);

            // 右上:リセット ⟲ / 早送り » ×n
            var reset = UiKit.Button("Reset", root, UiKit.Panel2);
            var rRt = reset.GetComponent<RectTransform>();
            rRt.anchorMin = new Vector2(1, 1); rRt.anchorMax = new Vector2(1, 1); rRt.pivot = new Vector2(1, 1);
            rRt.sizeDelta = new Vector2(96, 96); rRt.anchoredPosition = new Vector2(-36, -36);
            var rT = UiKit.Label("r", reset.transform, "⟲", UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(rT.rectTransform);
            reset.onClick.AddListener(() => _ctrl.ResetProgress());

            var ff = UiKit.Button("Ff", root, UiKit.Panel2);
            var fRt = ff.GetComponent<RectTransform>();
            fRt.anchorMin = new Vector2(1, 1); fRt.anchorMax = new Vector2(1, 1); fRt.pivot = new Vector2(1, 1);
            fRt.sizeDelta = new Vector2(230, 96); fRt.anchoredPosition = new Vector2(-36, -148);
            _hudFf = UiKit.Label("ffT", ff.transform, "» ×1", UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_hudFf.rectTransform);
            ff.onClick.AddListener(() => _ctrl.State.TimeScale = _ctrl.State.TimeScale <= 1f ? 8f
                : _ctrl.State.TimeScale < 30f ? 30f : 1f);

            // 左上:帰還ログ(鉱石名 +数量。単位なし)
            _hudLog = UiKit.Label("Log", root, "", UiKit.FSub, UiKit.Sub, TextAnchor.UpperLeft);
            var lRt = _hudLog.rectTransform;
            lRt.anchorMin = new Vector2(0, 1); lRt.anchorMax = new Vector2(0, 1); lRt.pivot = new Vector2(0, 1);
            lRt.sizeDelta = new Vector2(520, 320); lRt.anchoredPosition = new Vector2(40, -240);

            // 下部メニューバー:店(金貨)/ 強化(▲)/ 市況(▲▼)。3等分・排他トグル。
            _bottomBar = UiKit.Node("BottomBar", root);
            _bottomBar.anchorMin = new Vector2(0, 0); _bottomBar.anchorMax = new Vector2(1, 0); _bottomBar.pivot = new Vector2(0.5f, 0);
            _bottomBar.offsetMin = new Vector2(24, 24); _bottomBar.offsetMax = new Vector2(-24, 200);

            BuildBarButton(0f,      1f / 3f, UiKit.Coin, null,  () => _ctrl.ToggleStore());
            BuildBarButton(1f / 3f, 2f / 3f, null,       "▲",  () => _ctrl.ToggleUpgrade());
            BuildBarButton(2f / 3f, 1f,      null,       "▲▼", () => _ctrl.ToggleMarket());
        }

        void BuildBarButton(float ax0, float ax1, Sprite icon, string glyph, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UiKit.Button("Bar", _bottomBar, UiKit.Panel2);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(ax0, 0); rt.anchorMax = new Vector2(ax1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(10, 0); rt.offsetMax = new Vector2(-10, 0);
            if (icon != null)
            {
                var im = UiKit.Icon("i", btn.transform, icon);
                UiKit.Place(im.rectTransform, new Vector2(0.5f, 0.5f), 0, 0, 78, 78, 0.5f);
            }
            else
            {
                var t = UiKit.Label("g", btn.transform, glyph, UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(t.rectTransform);
            }
            btn.onClick.AddListener(onClick);
        }

        void RefreshHud()
        {
            if (!_hudBuilt) return;
            _hudNova.text = $"{_ctrl.State.Nova:#,0}";
            _hudInv.text = $"{_ctrl.Inventory.TotalKg:#,0}";
            string sp = _ctrl.State.TimeScale <= 1f ? "×1" : $"×{_ctrl.State.TimeScale:0}";
            _hudFf.text = $"» {sp}";
            // 天体シート(下部)表示中は下部バーを隠す(重なり回避。既存挙動)。
            bool barOn = _ctrl.Selected == null;
            if (_bottomBar.gameObject.activeSelf != barOn) _bottomBar.gameObject.SetActive(barOn);
            string log = _arrivalLog.Count > 0 ? string.Join("\n", _arrivalLog) : "";
            if (_hudLog.text != log) _hudLog.text = log;
        }

        void OnGUI()
        {
            if (_ctrl == null) return;
            // 全画面パネル表示中は IMGUI のゲージ/ポップを描かない(uGUIパネルの前面に透けて出るのを防ぐ)。
            if (_ctrl.AnyFullPanelOpen) return;
            if (!_gaugeReady)
            {
                _gaugeReady = true;
                int f = Mathf.RoundToInt(Screen.height * 0.026f);
                _sPopup = new GUIStyle { fontSize = Mathf.RoundToInt(f * 1.0f), fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.95f, 0.80f, 0.42f) } };
                _sPopupBase = _sPopup.fontSize;
                _texGaugeBg    = Tex(new Color(0.04f, 0.06f, 0.12f, 0.85f));
                _texGaugeFill  = Tex(new Color(0.37f, 0.83f, 0.94f, 1f));   // cyan
                _texGaugeFrame = Tex(new Color(0.17f, 0.20f, 0.32f, 1f));
            }
            DrawMiningGauges();   // 採掘中の船の頭上に進捗ゲージ(ワールド追従)
            DrawPopups();         // 到着時の「+数量」上昇フェード(ワールド追従)
        }

        // 採掘中(phase==Mining)の船の頭上に進捗ゲージを描く。
        void DrawMiningGauges()
        {
            // ゲージ長は採掘ロボット(船マーカー)の画面径に連動し、その 1/2 とする。
            float pxPerWorld = Screen.height / (2f * _ctrl.Cam.orthographicSize);
            float markerPx = _ctrl.CurrentIconWorld * 0.42f * pxPerWorld;  // マーカーの画面径
            float gw = markerPx * 0.5f;                 // = ロボットの半分の長さ
            float gh = Mathf.Max(2f, gw * 0.16f);
            foreach (var kv in _rt)
            {
                var rt = kv.Value;
                if (!rt.visible || rt.phase != Phase.Mining) continue;
                Vector3 sp = _ctrl.Cam.WorldToScreenPoint(new Vector3(rt.worldPos.x, rt.worldPos.y, 0));
                if (sp.z < 0) continue;
                float gx = sp.x - gw * 0.5f;
                float gy = Screen.height - sp.y - markerPx * 0.5f - gh - 4f;   // マーカーの上端の少し上
                float prog = Mathf.Clamp01(rt.mineProgress);
                GUI.DrawTexture(new Rect(gx - 1, gy - 1, gw + 2, gh + 2), _texGaugeFrame);
                GUI.DrawTexture(new Rect(gx, gy, gw, gh), _texGaugeBg);
                GUI.DrawTexture(new Rect(gx, gy, gw * prog, gh), _texGaugeFill);
            }
        }

        // 到着時の「+数量」+鉱石アイコンを、ステーション上で下から上へ上昇+フェード。
        void DrawPopups()
        {
            if (_popups.Count == 0) return;
            float riseMax = Screen.height * 0.08f;
            foreach (var p in _popups)
            {
                Vector3 sp = _ctrl.Cam.WorldToScreenPoint(new Vector3(p.worldPos.x, p.worldPos.y, 0));
                if (sp.z < 0) continue;
                float scale = p.scale <= 0f ? 1f : p.scale;
                _sPopup.fontSize = Mathf.Max(8, Mathf.RoundToInt(_sPopupBase * scale));
                float h = _sPopup.fontSize * 1.4f;
                float t = Mathf.Clamp01(p.age / p.maxLife);
                float gy = Screen.height - sp.y - riseMax * scale * t;   // 上昇(小さいものは控えめ)
                float tw = _sPopup.CalcSize(new GUIContent(p.text)).x;
                float lx = sp.x - (tw + h) * 0.5f;

                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 1f - t);        // フェードアウト
                GUI.Label(new Rect(lx, gy, tw, h), p.text, _sPopup);
                // 資源別アイコン(採掘ポップ)。到着合計(resId=null)は汎用キューブ(=質量)。
                var icon = string.IsNullOrEmpty(p.resId) ? UiIcons.Cube : UiIcons.ResourceIcon(p.resId);
                UiIcons.Draw(icon, lx + tw + 4f, gy, h, h * 0.7f);
                GUI.color = prev;
            }
            _sPopup.fontSize = _sPopupBase;   // 復元
        }

        static Texture2D Tex(Color c)
        { var tx = new Texture2D(1, 1); tx.SetPixel(0, 0, c); tx.Apply(); return tx; }
    }
}
