// 宇宙マップ本実装(ゲーム Phase 1 / mvp_dev_plan.md「宇宙マップ」)。
//
// Phase 0 の検証済みシームレスズーム(単一シーングラフ + LOD3段)を土台に、
// ゲーム用の天体データ(GameData: 天体マスタ ⋈ レイアウト)を描画し、
// MVP5天体のゲート表示とタップ選択を加えたもの。
//
// 設計方針(企画書5章「完全シームレス」/14章 MVPスコープ):
//   - ノードは一度だけ生成し、毎フレームは可視/スケール/ラベルだけ更新する
//     (Instantiate/Destroy しない)。座標は付録A(対数リング)を読むだけ。
//   - MVP スコープゲート: 地球[拠点]・月・エロス・水星・火星のみ操作可能。
//     MVP対象外の天体は減光 + 🔒 で「未開放」表示、選択不可。
//   - タップ選択: パン(ドラッグ)と区別し、天体をタップしたら OnBodySelected を発火。
//     天体パネル/派遣UI(次タスク)がこのイベントを購読する。
//
// シーンに本コンポーネントが無くても Play 時に自動生成する(開いて Play で動く)。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class SpaceMapController : MonoBehaviour
    {
        [Header("ズーム範囲(orthographicSize = 表示縦幅の半分・ワールドpx)")]
        public float maxOrthoFactor = 1.15f;   // × world_extent
        public float minOrtho = 40f;
        public float startOrthoFactor = 0.9f;

        [Header("LOD 閾値(orthoSize / world_extent の比)")]
        public float lodOverviewAbove = 0.60f; // これ以上 = L1 オーバービュー
        public float lodDetailBelow = 0.18f;   // これ以下 = L3 詳細(衛星+ラベル)

        [Header("見た目")]
        // アイコンは「ワールド固定サイズ」を基本にする → ズームインで惑星が大きくなる
        //(接近感)。付録A の icon_d=24px / min_dR=60px なので固定でも重ならない。
        [Tooltip("アイコンのワールド固定径[px]。ズームインでこの実サイズのまま画面上で拡大する")]
        public float baseIconWorld = 40f;
        [Tooltip("引ききった時に点が消えないための画面下限(orthoSize×この係数を径の下限に)")]
        public float iconMinScreenFactor = 0.02f;
        public float labelScreenFactor = 0.5f;
        public Color ringColor = new Color(0.12f, 0.16f, 0.23f, 1f);
        public Color planetColor = new Color(0.90f, 0.91f, 0.93f, 1f);
        public Color clusterColor = new Color(0.96f, 0.62f, 0.04f, 1f);
        public Color moonColor = new Color(0.66f, 0.55f, 0.98f, 1f);
        public Color mvpColor = new Color(0.22f, 0.74f, 0.98f, 1f);
        public Color stationColor = new Color(0.40f, 0.85f, 0.55f, 1f);
        public Color lockedColor = new Color(0.35f, 0.38f, 0.45f, 1f); // MVP対象外(未開放)
        public Color selectColor = new Color(1.00f, 0.86f, 0.30f, 1f); // 選択リング

        [Header("入力")]
        public float scrollZoomSpeed = 0.15f;  // エディタ: マウスホイール
        public float pinchZoomSpeed = 0.01f;    // 実機: 2本指ピンチ
        [Tooltip("タップ判定: この画面px以上ドラッグしたらパン扱い(選択しない)")]
        public float tapMaxDragPx = 12f;
        [Tooltip("タップ判定: この秒数以上の長押しは選択しない")]
        public float tapMaxHold = 0.4f;
        [Tooltip("タップ当たり判定の甘さ(アイコン画面径に対する倍率)")]
        public float tapHitFactor = 1.6f;

        // 天体が選択されたとき発火(次タスク: 天体パネル/派遣UI が購読)
        public event Action<CelestialBody> OnBodySelected;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<SpaceMapController>() != null) return;
            var go = new GameObject("SpaceMapController");
            go.AddComponent<SpaceMapController>();
        }

        Camera _cam;
        GameData _data;
        Fleet _fleet;
        ResourcePrices _prices;
        Market _market;
        SpeedCurve _speed;
        Inventory _inventory;
        StorePanel _store;
        UpgradeCurve _upgradeCurve;
        UpgradePanel _upgrade;
        MarketPanel _marketPanel;
        OfflinePanel _offline;
        FleetSimulator _sim;
        Refinery _refinery;
        float _saveTimer;
        Sprite _circle;
        Sprite _ringSprite;
        float _maxOrtho;
        int _lodTier = 2;
        float _fpsSmooth = 60f;

        // 選択リング(1個を使い回して選択天体へ移動)
        Transform _selRing;
        CelestialBody _selected;

        GameState _state;

        public GameData Data => _data;
        public Fleet Fleet => _fleet;
        public ResourcePrices Prices => _prices;
        public Market Market => _market;
        public SpeedCurve Speed => _speed;

        // 当日市況の売却単価(市況があればそれ、無ければ基準価格へフォールバック)。
        // 金属(精錬品)は 元鉱石の当日単価 × RefineFactor で二層価格にする。
        public double PriceOf(string id)
        {
            if (Refinery.IsRefinedId(id))
                return PriceOf(Refinery.OreOf(id)) * BalanceOverride.RefineFactor;
            var m = _market?.Get(id);
            if (m != null) return m.nova_per_kg;
            return _prices.ById(id)?.nova_per_kg ?? 0;
        }
        // 前日比(比率)。金属は元鉱石の前日比を流用。市況が無い/該当なしは 0。
        public double Change1d(string id)
        {
            if (Refinery.IsRefinedId(id)) return Change1d(Refinery.OreOf(id));
            return _market?.Get(id)?.change_1d ?? 0;
        }

        // 採掘資源の解禁(市況パネルで解禁。天体に出現する資源を当日単価の安い順)
        readonly List<string> _unlockOrder = new List<string>();
        public IReadOnlyList<string> UnlockOrder => _unlockOrder;
        public bool IsResourceUnlocked(string id) => _state.UnlockedResources.Contains(id);

        // アンロック済みの惑星が産出する資源id(重複排除)。市況パネルの表示・解禁対象はこれに限定する。
        public HashSet<string> UnlockedBodyResourceIds()
        {
            var set = new HashSet<string>();
            foreach (var b in _data.Bodies)
            {
                if (b.IsStation || b.Master == null || !b.Unlocked) continue;
                foreach (var r in _prices.MatchBodyResources(b.Resources)) set.Add(r.id);
            }
            return set;
        }

        // 次に解禁できる資源(単価の安い順)。ただしアンロック済み惑星が産出するものに限る。
        public string NextLockedResource()
        {
            var avail = UnlockedBodyResourceIds();
            foreach (var id in _unlockOrder)
                if (avail.Contains(id) && !_state.UnlockedResources.Contains(id)) return id;
            return null;
        }
        // 次に解禁できる資源の費用。共通の強化曲線(解禁数で増加)。産出量でバランス調整する前提の暫定。
        public double NextUnlockCost()
        {
            var c = _upgradeCurve.CostToNext(_state.UnlockedResources.Count);
            return c.HasValue ? c.Value * BalanceOverride.UpgradeCostScale : 0;
        }
        public bool TryUnlockNext()
        {
            var id = NextLockedResource();
            return id != null && TryUnlock(id);
        }

        // 初期解禁=初期解放済みの惑星(=月)が産出する最安資源(=鉄)。
        // これが無いと開始直後に月で何も採れず詰むため、必ず1資源を解禁しておく。
        public void EnsureInitialUnlock()
        {
            if (_state.UnlockedResources.Count > 0) return;
            var id = NextLockedResource();                                  // 月の産出資源のうち最安ロック
            if (id == null && _unlockOrder.Count > 0) id = _unlockOrder[0]; // 念のためのフォールバック
            if (id != null) _state.UnlockedResources.Add(id);
        }

        // 指定した資源を解禁する。コストは共通曲線(NextUnlockCost)。
        public bool TryUnlock(string id)
        {
            if (string.IsNullOrEmpty(id) || _state.UnlockedResources.Contains(id)) return false;
            double cost = NextUnlockCost();
            if (_state.Nova < cost) return false;
            _state.Nova -= cost;
            _state.UnlockedResources.Add(id);
            return true;
        }

        void BuildUnlockOrder()
        {
            var seen = new HashSet<string>();
            var list = new List<(string id, double price)>();
            foreach (var b in _data.Bodies)
            {
                if (b.Master == null) continue;
                foreach (var r in _prices.MatchBodyResources(b.Resources))
                    if (seen.Add(r.id)) list.Add((r.id, r.nova_per_kg));   // 基準単価(市況で揺れない)
            }
            list.Sort((a, b) => a.price.CompareTo(b.price));   // 基準単価の安い順(解禁順は日々変わらない)
            _unlockOrder.Clear();
            foreach (var e in list) _unlockOrder.Add(e.id);
        }
        public Inventory Inventory => _inventory;
        public StorePanel Store => _store;
        public UpgradeCurve UpgradeCurve => _upgradeCurve;
        public UpgradePanel Upgrade => _upgrade;
        public MarketPanel MarketUI => _marketPanel;
        public OfflinePanel Offline => _offline;
        public FleetSimulator Sim => _sim;

        // 全画面パネル(店/強化/市況/オフライン)がどれか開いているか。
        // IMGUI描画(採掘ゲージ/ポップ)を前面に出さないためのガードに使う。
        public bool AnyFullPanelOpen =>
            (_store != null && _store.IsOpen) || (_upgrade != null && _upgrade.IsOpen)
            || (_marketPanel != null && _marketPanel.IsOpen) || (_offline != null && _offline.IsOpen);

        // 強化の効果倍率(採掘速度・積載)。FleetSimulator が基準値に掛ける。
        public double MineRateMult => _upgradeCurve.EffectMult(_state.MineLevel);
        public double CargoMult => _upgradeCurve.EffectMult(_state.CargoLevel);
        public GameState State => _state;
        public CelestialBody Selected => _selected;
        public Sprite CircleSprite => _circle;
        public Camera Cam => _cam;
        public Transform MapRoot => transform;               // ノード/リングと同じ親(パン/ズーム追従)
        public float CurrentIconWorld => IconWorldSize();    // 現在のアイコン径(船マーカーの基準)

        class BodyView
        {
            public CelestialBody body;
            public Transform tr;        // ルート(位置のみ、scale=1)
            public Transform icon;
            public SpriteRenderer sr;
            public TextMesh label;
            public Transform labelTr;
            public TextMesh lockMark;   // 🔒(MVP対象外のみ)
        }
        readonly List<BodyView> _views = new List<BodyView>();

        // ------------------------------------------------------------ 構築
        void Start()
        {
            _data = GameData.Load();
            _fleet = new Fleet(ShipStats.Load());
            _prices = ResourcePrices.Load();
            _market = Market.Load();               // 当日市況(無ければ基準価格でフォールバック)
            _speed = SpeedCurve.Load();
            _upgradeCurve = UpgradeCurve.Load();
            BuildUnlockOrder();                    // 採掘資源の解禁順
            _inventory = new Inventory();
            _state = new GameState(startNova: 0);
            _maxOrtho = _data.Layout.world_extent * maxOrthoFactor;

            SetupCamera();
            _circle = MakeCircleSprite(64);
            _ringSprite = MakeRingSprite(64, 0.14f);
            DrawRings();
            BuildBodies();
            BuildSelectionRing();

            // 天体パネル/派遣UI(このコントローラの子として生成し、選択を購読)
            var panelGo = new GameObject("BodyPanel");
            panelGo.transform.SetParent(transform, false);
            panelGo.AddComponent<BodyPanel>().Bind(this);

            // 商品取引所(店)= 手動売却UI
            var storeGo = new GameObject("StorePanel");
            storeGo.transform.SetParent(transform, false);
            _store = storeGo.AddComponent<StorePanel>();
            _store.Bind(this);

            // 強化パネル(採掘速度・積載のアップグレード)
            var upGo = new GameObject("UpgradePanel");
            upGo.transform.SetParent(transform, false);
            _upgrade = upGo.AddComponent<UpgradePanel>();
            _upgrade.Bind(this);

            // 船の自動ループ(状態機械)。Fleet の割り当てを入力に駆動する
            var simGo = new GameObject("FleetSimulator");
            simGo.transform.SetParent(transform, false);
            _sim = simGo.AddComponent<FleetSimulator>();
            _sim.Bind(this);

            // 精錬所(在庫の鉱石→金属を背景で処理)
            var refGo = new GameObject("Refinery");
            refGo.transform.SetParent(transform, false);
            _refinery = refGo.AddComponent<Refinery>();
            _refinery.Bind(this);

            // 本日の市況パネル
            var mktGo = new GameObject("MarketPanel");
            mktGo.transform.SetParent(transform, false);
            _marketPanel = mktGo.AddComponent<MarketPanel>();
            _marketPanel.Bind(this);

            // オフライン復帰ダイアログ
            var offGo = new GameObject("OfflinePanel");
            offGo.transform.SetParent(transform, false);
            _offline = offGo.AddComponent<OfflinePanel>();
            _offline.Bind(this);

            // セーブ復元 → 不在時間ぶんのオフライン進行を計算して提示
            double elapsed = SaveSystem.Load(this);
            EnsureInitialUnlock();   // 初期解禁=月の最安資源(=鉄)。未解禁だと初手で詰む
            if (elapsed >= 1)
            {
                var result = _sim.ApplyOffline(elapsed);
                _refinery.ApplyOffline(elapsed);   // 留守中も精錬継続(在庫の鉱石→金属)
                if (result.HasGains) _offline.Show(result);
            }

            _cam.orthographicSize = Mathf.Clamp(
                _data.Layout.world_extent * startOrthoFactor, minOrtho, _maxOrtho);
        }

        // 定期オートセーブ + アプリ中断/終了時に保存
        void LateUpdate()
        {
            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer >= 5f) { _saveTimer = 0f; SaveSystem.Save(this); }
        }
        void OnApplicationPause(bool paused) { if (paused && _state != null) SaveSystem.Save(this); }
        void OnApplicationQuit() { if (_state != null) SaveSystem.Save(this); }

        // テスト用:進捗をリセット(セーブ削除 + 初期状態へ)
        public void ResetProgress()
        {
            SaveSystem.Clear();
            _state.Nova = 0; _state.MineLevel = 1; _state.CargoLevel = 1;
            _state.DedicatedMinerUnlocked = false; _state.AutoSellUnlocked = false;
            _state.UnlockedResources.Clear();
            _inventory.Clear();
            _fleet.ResetToInitial();      // 増設した宇宙船を初期1隻へ + 全船待機
            _sim?.ResetShipVisuals();     // 旧マーカーを破棄(次フレームで再生成)
            foreach (var b in _data.Bodies)
                if (!b.IsStation) b.Unlocked = b.Master != null && b.Master.unlock_price_nova == 0;
            EnsureInitialUnlock();   // 惑星の解放を戻した後に初期解禁(=月の鉄)を確定
            Deselect();
        }

        void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.transform.position = new Vector3(0, 0, -10);
            _cam.backgroundColor = new Color(0.043f, 0.063f, 0.125f, 1f); // #0b1020
            _cam.clearFlags = CameraClearFlags.SolidColor;
        }

        void DrawRings()
        {
            foreach (var rr in _data.Layout.ring_radius)
            {
                var go = new GameObject($"ring_{rr.band}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.widthMultiplier = 1.2f;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = lr.endColor = ringColor;
                const int seg = 96;
                lr.positionCount = seg;
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(
                        Mathf.Cos(a) * rr.radius, Mathf.Sin(a) * rr.radius, 1f));
                }
            }
        }

        void BuildBodies()
        {
            foreach (var b in _data.Bodies)
                _views.Add(CreateBodyView(b));
        }

        BodyView CreateBodyView(CelestialBody b)
        {
            var go = new GameObject($"{b.No}_{b.Name}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(b.Pos.x, b.Pos.y, 0);

            var iconGo = new GameObject("icon");
            iconGo.transform.SetParent(go.transform, false);
            var sr = iconGo.AddComponent<SpriteRenderer>();
            // スプライト差し替えパイプライン: PNG(art/bodies/…)があればそれ、無ければ陰影付き球。
            // 手続き球はグレースケールなので既存の種別色(ColorFor)で tint する。PNG はフルカラーなので
            // 白 tint(MVP対象外は減光)。色割り当てロジック自体は変更しない。
            bool bodyIsArt;
            sr.sprite = SpriteBank.Body(b, out bodyIsArt);
            sr.color = BodyTint(b, bodyIsArt);
            sr.sortingOrder = b.IsMoon ? 1 : 2;

            var lgo = new GameObject("label");
            lgo.transform.SetParent(go.transform, false);
            var tm = lgo.AddComponent<TextMesh>();
            tm.text = LabelFor(b, _lodTier);
            tm.anchor = TextAnchor.MiddleLeft;
            tm.fontSize = 64;
            tm.characterSize = 0.02f;
            tm.color = b.IsMvp ? new Color(0.90f, 0.93f, 0.98f, 1f)
                               : new Color(0.55f, 0.58f, 0.64f, 1f);
            var lmrInit = lgo.GetComponent<MeshRenderer>();
            lmrInit.sortingOrder = 5;
            lmrInit.enabled = false;   // 既定は非表示(選択時のみ ApplyLod が有効化)

            // MVP対象外は 🔒 を重ねる(未開放を明示)
            TextMesh lockTm = null;
            if (!b.IsMvp)
            {
                var kgo = new GameObject("lock");
                kgo.transform.SetParent(go.transform, false);
                lockTm = kgo.AddComponent<TextMesh>();
                lockTm.text = "🔒";
                lockTm.anchor = TextAnchor.MiddleCenter;
                lockTm.fontSize = 64;
                lockTm.characterSize = 0.02f;
                lockTm.color = new Color(0.85f, 0.87f, 0.92f, 0.9f);
                kgo.GetComponent<MeshRenderer>().sortingOrder = 4;
            }

            return new BodyView
            {
                body = b, tr = go.transform, icon = iconGo.transform,
                sr = sr, label = tm, labelTr = lgo.transform, lockMark = lockTm
            };
        }

        void BuildSelectionRing()
        {
            var go = new GameObject("__selection_ring");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _ringSprite;
            sr.color = selectColor;
            sr.sortingOrder = 3;
            go.SetActive(false);
            _selRing = go.transform;
        }

        // 天体アイコンの tint。手続き球はグレースケール→種別色を乗算。PNGアートは素の色(白)、
        // ただし MVP対象外は減光して「未開放」を保つ(既存の減光挙動を維持)。
        Color BodyTint(CelestialBody b, bool isArt)
        {
            if (!isArt) return ColorFor(b);                      // 手続き球: 従来どおり種別色で着色
            return b.IsMvp ? Color.white : new Color(0.45f, 0.47f, 0.52f, 1f);
        }

        Color ColorFor(CelestialBody b)
        {
            if (!b.IsMvp) return lockedColor;      // MVP対象外は最優先で減光
            if (b.IsStation) return stationColor;
            if (b.IsClusterParent) return clusterColor;
            if (b.IsMoon) return moonColor;
            return mvpColor;                       // MVP 惑星/小惑星
        }

        string LabelFor(CelestialBody b, int tier)
        {
            if (b.IsClusterParent && tier == 1)
                return $"{b.Name} ⑂{b.Node.cluster_size - 1}";
            return b.Name;
        }

        // アイコンのワールド径。基本はワールド固定(=ズームインで画面上拡大)だが、
        // 引ききった時に点が消えないよう orthoSize に対する下限を設ける。
        float IconWorldSize()
            => Mathf.Max(baseIconWorld, _cam.orthographicSize * iconMinScreenFactor);

        // ------------------------------------------------------------ 毎フレーム
        void Update()
        {
            HandleZoom();
            HandlePanAndTap();
            ApplyLod();
            UpdateSelectionRing();
            _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.1f);
        }

        void HandleZoom()
        {
            float ortho = _cam.orthographicSize;

            if (Input.touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0), t1 = Input.GetTouch(1);
                Vector2 p0 = t0.position - t0.deltaPosition;
                Vector2 p1 = t1.position - t1.deltaPosition;
                float prevMag = (p0 - p1).magnitude;
                float curMag = (t0.position - t1.position).magnitude;
                float delta = prevMag - curMag; // 指を開く=負=ズームイン
                ortho += delta * pinchZoomSpeed * (ortho / 100f);
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
                ortho -= scroll * scrollZoomSpeed * ortho;

            _cam.orthographicSize = Mathf.Clamp(ortho, minOrtho, _maxOrtho);
        }

        // パン(ドラッグ)とタップ(選択)を同一ポインタで判別する。
        // 押下 → 離すまでの移動量と保持時間が閾値内なら「タップ」= 選択。
        Vector3 _lastPanWorld;
        Vector2 _downScreen;
        float _downTime;
        bool _pointerDown;
        bool _draggedPastThreshold;
        void HandlePanAndTap()
        {
            bool multiTouch = Input.touchCount >= 2;
            bool down = Input.GetMouseButton(0) && !multiTouch;

            if (down && !_pointerDown)
            {
                // 押下がUI領域で始まったらマップは無視(UI側が処理する)
                if (PointerOverUI()) return;
                // 押下開始
                _pointerDown = true;
                _draggedPastThreshold = false;
                _downScreen = Input.mousePosition;
                _downTime = Time.unscaledTime;
                _lastPanWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
            }
            else if (down && _pointerDown)
            {
                // ドラッグ中 = パン
                Vector3 w = _cam.ScreenToWorldPoint(Input.mousePosition);
                Vector3 d = _lastPanWorld - w;
                _cam.transform.position += new Vector3(d.x, d.y, 0);
                _lastPanWorld = _cam.ScreenToWorldPoint(Input.mousePosition);

                if (((Vector2)Input.mousePosition - _downScreen).magnitude > tapMaxDragPx)
                    _draggedPastThreshold = true;
            }
            else if (!down && _pointerDown)
            {
                // 離した: タップ条件を満たせば選択
                _pointerDown = false;
                bool quick = (Time.unscaledTime - _downTime) <= tapMaxHold;
                if (!_draggedPastThreshold && quick)
                    TrySelectAt(_downScreen);
            }
        }

        // 画面座標に最も近い可視・選択可能な天体を当たり判定して選択。
        void TrySelectAt(Vector2 screenPos)
        {
            Vector3 world = _cam.ScreenToWorldPoint(screenPos);
            float iconWorld = IconWorldSize();
            float hitR = Mathf.Max(iconWorld * tapHitFactor, _cam.orthographicSize * 0.02f);

            BodyView best = null;
            float bestSq = hitR * hitR;
            foreach (var v in _views)
            {
                if (!v.sr.enabled) continue;                 // 非表示(LODで畳まれた衛星等)は対象外
                float sq = ((Vector2)v.tr.position - (Vector2)world).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = v; }
            }
            if (best == null) { Deselect(); return; }  // 何もない所をタップ = 選択解除

            Select(best.body);
        }

        void Deselect()
        {
            if (_selected == null) return;
            _selected = null;
            OnBodySelected?.Invoke(null);
        }

        void Select(CelestialBody b)
        {
            // MVP対象外はトーストのみ(選択・パネルは開かない)
            if (!b.IsMvp)
            {
                ShowToast($"{b.Name} は現在のバージョンでは未開放です");
                return;
            }
            _selected = b;
            OnBodySelected?.Invoke(b);
        }

        // 天体パネルの閉じるボタン等から選択解除する
        public void CloseSelection() => Deselect();

        // UI(パネル/HUD)が占める画面領域。ここでのポインタ操作はマップに拾わせない
        //(IMGUI のボタン押下をマップのタップ/パンが横取りして誤動作するのを防ぐ)。
        float _uiBottomInset;  // 下シート(天体パネル)の高さ[px]
        float _hudBottomInset; // 下部メニューバー(店/強化/市況)の高さ[px]
        float _uiTopInset;     // 上部HUDの高さ[px]
        public void SetPanelInset(float bottomPx) => _uiBottomInset = bottomPx;
        public void SetHudBottomInset(float bottomPx) => _hudBottomInset = bottomPx;
        public void SetHudTopInset(float topPx) => _uiTopInset = topPx;
        bool PointerOverUI()
        {
            // 全画面モーダル(店・強化・市況・オフライン)表示中はマップ入力を完全に止める
            if ((_store != null && _store.IsOpen) || (_upgrade != null && _upgrade.IsOpen)
                || (_offline != null && _offline.IsOpen) || (_marketPanel != null && _marketPanel.IsOpen)) return true;
            // uGUI 化した HUD/シートは EventSystem がクリックを吸う。その上にポインタがあればマップは無視。
            var es = EventSystem.current;
            if (es != null)
            {
                if (es.IsPointerOverGameObject()) return true;                 // マウス(エディタ)
                for (int i = 0; i < Input.touchCount; i++)                     // タッチ(実機)
                    if (es.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return true;
            }
            // 旧 IMGUI 用の inset フォールバック(現在は基本 0)。
            float y = Input.mousePosition.y;   // 画面下=0 の座標系
            return y < Mathf.Max(_uiBottomInset, _hudBottomInset) || y > Screen.height - _uiTopInset;
        }

        // 下部メニューの排他トグル(1枚だけ開く)。開いているものを押したら閉じる。
        public void ToggleStore()   { bool w = _store.IsOpen;       CloseAllPanels(); if (!w) _store.Toggle(); }
        public void ToggleUpgrade() { bool w = _upgrade.IsOpen;     CloseAllPanels(); if (!w) _upgrade.Toggle(); }
        public void ToggleMarket()  { bool w = _marketPanel.IsOpen; CloseAllPanels(); if (!w) _marketPanel.Toggle(); }
        // 全オーバーレイ(3メニュー+天体シート)を閉じる。2枚同時表示を防ぐ。
        public void CloseAllPanels()
        {
            _store?.Close(); _upgrade?.Close(); _marketPanel?.Close();
            Deselect();
        }

        void ApplyLod()
        {
            float ortho = _cam.orthographicSize;
            float ratio = ortho / _data.Layout.world_extent;
            _lodTier = ratio >= lodOverviewAbove ? 1 : (ratio <= lodDetailBelow ? 3 : 2);

            float iconScale = IconWorldSize();   // ワールド固定径(ズームインで画面上拡大)
            float labelChar = ortho * labelScreenFactor * 0.01f; // ラベルは画面一定サイズ

            foreach (var v in _views)
            {
                bool visible = v.body.IsMoon ? (_lodTier == 3) : true;
                if (v.sr.enabled != visible) v.sr.enabled = visible;

                // 天体名ラベルは常時非表示。選択(タップ)された天体のみ名前を出す
                //(ユーザー決定 2026-07-13。概観をアイコンだけで見せ、識別はタップで)。
                bool labelOn = visible && ReferenceEquals(v.body, _selected);
                var lmr = v.label.GetComponent<MeshRenderer>();
                if (lmr.enabled != labelOn) lmr.enabled = labelOn;

                v.icon.localScale = Vector3.one * iconScale;
                if (labelOn)
                {
                    v.label.text = LabelFor(v.body, _lodTier);
                    v.label.characterSize = labelChar;
                    // アイコン右脇に置く(アイコンがワールド固定径なので径に追従)
                    v.labelTr.localPosition = new Vector3(iconScale * 0.65f, 0f, 0f);
                }

                // 🔒 は詳細寄り(L2/L3)かつアイコン可視時のみ、アイコン中心に重ねる
                if (v.lockMark != null)
                {
                    var kmr = v.lockMark.GetComponent<MeshRenderer>();
                    bool lockOn = visible && _lodTier >= 2;
                    if (kmr.enabled != lockOn) kmr.enabled = lockOn;
                    if (lockOn) v.lockMark.characterSize = labelChar * 0.8f;
                }
            }
        }

        void UpdateSelectionRing()
        {
            if (_selected == null) { if (_selRing.gameObject.activeSelf) _selRing.gameObject.SetActive(false); return; }
            // 選択天体が畳まれて非表示なら選択リングも隠す
            bool vis = _selected.IsMoon ? (_lodTier == 3) : true;
            if (_selRing.gameObject.activeSelf != vis) _selRing.gameObject.SetActive(vis);
            if (!vis) return;
            float iconScale = IconWorldSize();
            _selRing.position = new Vector3(_selected.Pos.x, _selected.Pos.y, 0);
            _selRing.localScale = Vector3.one * iconScale * 2.1f;
        }

        // ------------------------------------------------------------ 簡易トースト / 選択情報
        string _toast; float _toastUntil;
        void ShowToast(string msg) { _toast = msg; _toastUntil = Time.unscaledTime + 2.2f; }
        public void Toast(string msg) => ShowToast(msg);

        // 天体パネル(次タスク)が入るまでの暫定表示。選択情報と FPS/LOD を出す。
        void OnGUI()
        {
            var style = new GUIStyle
            {
                fontSize = 26,
                normal = { textColor = _fpsSmooth >= 58f ? Color.green :
                    (_fpsSmooth >= 45f ? Color.yellow : Color.red) }
            };
            string tier = _lodTier == 1 ? "L1 概観" : _lodTier == 2 ? "L2 中間" : "L3 詳細";
            GUI.Label(new Rect(12, 10, 700, 34),
                $"FPS {_fpsSmooth:00.0}  |  {tier}  |  orthoSize {_cam.orthographicSize:0}" +
                $"  |  天体 {_views.Count}", style);

            // 選択天体の情報/派遣UIは BodyPanel が描画する。

            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
            {
                var ts = new GUIStyle { fontSize = 26, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.85f, 0.4f, 1f) } };
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 40), _toast, ts);
            }
        }

        // ------------------------------------------------------------ スプライト生成
        static Sprite MakeCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f, edge = 1.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) +
                                         (y - r + 0.5f) * (y - r + 0.5f));
                    float a = Mathf.Clamp01((r - d) / edge);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }

        // 中空リング(選択ハイライト用)。thickness = 半径に対する肉厚比。
        static Sprite MakeRingSprite(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f, edge = 1.5f;
            float inner = r * (1f - thickness);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) +
                                         (y - r + 0.5f) * (y - r + 0.5f));
                    float aOut = Mathf.Clamp01((r - d) / edge);
                    float aIn = Mathf.Clamp01((d - inner) / edge);
                    float a = Mathf.Min(aOut, aIn);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }
    }
}
