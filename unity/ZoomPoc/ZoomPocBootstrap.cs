// シームレスズーム PoC(ゲーム Phase 0 / mvp_dev_plan.md)。
// 完了条件: 実機(iPhone 12相当)で L1↔L3 ピンチが 60fps。
//
// 設計方針(企画書5章「完全シームレス」):
//   - 単一シーングラフ + LOD3段。ノードは一度だけ生成し、毎フレームは
//     可視/スケール/ラベルだけ更新する(Instantiate/Destroy しない)。
//   - 座標系は付録A(対数リング)。真実源は ring_layout.py → map_layout.json。
//   - セマンティックズーム: 遠→オーバービュー(衛星は親へ集約)、近→全表示+ラベル。
//
// 使い方: 空シーンに空 GameObject を1つ作り、本スクリプトを付けて Play。
//   カメラが無ければ自動生成する。map_layout.json は StreamingAssets に配置。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.ZoomPoc
{
    [DisallowMultipleComponent]
    public class ZoomPocBootstrap : MonoBehaviour
    {
        [Header("ズーム範囲(orthographicSize = 表示縦幅の半分・ワールドpx)")]
        [Tooltip("最大ズームアウト。map 全体が縦に収まる目安 = world_extent×係数")]
        public float maxOrthoFactor = 1.15f;   // × world_extent
        [Tooltip("最小ズームイン(クラスター内へ潜る)")]
        public float minOrtho = 40f;
        [Tooltip("初期ズーム = world_extent×係数")]
        public float startOrthoFactor = 0.9f;

        [Header("LOD 閾値(orthoSize / world_extent の比)")]
        public float lodOverviewAbove = 0.60f; // これ以上 = L1 オーバービュー
        public float lodDetailBelow = 0.18f;   // これ以下 = L3 詳細(衛星+ラベル)

        [Header("見た目")]
        [Tooltip("アイコン径 = orthoSize×この係数(画面上ほぼ一定サイズに保つ)")]
        public float iconScreenFactor = 0.035f;
        public float labelScreenFactor = 0.5f;
        public Color ringColor = new Color(0.12f, 0.16f, 0.23f, 1f);
        public Color planetColor = new Color(0.90f, 0.91f, 0.93f, 1f);
        public Color clusterColor = new Color(0.96f, 0.62f, 0.04f, 1f);
        public Color moonColor = new Color(0.66f, 0.55f, 0.98f, 1f);
        public Color mvpColor = new Color(0.22f, 0.74f, 0.98f, 1f);
        public Color stationColor = new Color(0.22f, 0.74f, 0.98f, 1f);

        [Header("入力")]
        public float scrollZoomSpeed = 0.15f;   // エディタ: マウスホイール
        public float pinchZoomSpeed = 0.01f;     // 実機: 2本指ピンチ

        [Header("ストレステスト(任意)")]
        [Tooltip("1=実データ50天体のみ。2以上で複製してノード数を増やし負荷確認")]
        public int densityMultiplier = 1;

        // シーンに本コンポーネントが無くても Play 時に自動生成する。
        // → プロジェクトを開いて Play を押すだけで PoC が動く(手組み不要)。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<ZoomPocBootstrap>() != null) return;
            var go = new GameObject("ZoomPocBootstrap");
            go.AddComponent<ZoomPocBootstrap>();
        }

        Camera _cam;
        MapLayout _layout;
        Sprite _circle;
        float _maxOrtho;

        // ノード実体(単一シーングラフ。生成後は破棄しない)
        // ルート(tr)はスケール1固定。アイコンとラベルは別の子で個別スケール
        // → 親スケールがラベルに二重掛けされる問題を避ける。
        class NodeView
        {
            public MapNode data;
            public Transform tr;      // ルート(位置のみ、scale=1)
            public Transform icon;    // アイコン(SpriteRenderer)
            public SpriteRenderer sr;
            public TextMesh label;
            public Transform labelTr;
        }
        readonly List<NodeView> _views = new List<NodeView>();

        // FPS 計測
        float _fpsSmooth = 60f;
        int _lodTier = 2; // 1=overview,2=mid,3=detail(表示用)

        void Start()
        {
            _layout = MapLayout.Load();
            _maxOrtho = _layout.world_extent * maxOrthoFactor;

            SetupCamera();
            _circle = MakeCircleSprite(64);
            DrawRings();
            DrawStation();
            BuildNodes();

            _cam.orthographicSize = Mathf.Clamp(
                _layout.world_extent * startOrthoFactor, minOrtho, _maxOrtho);
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

        // ------------------------------------------------------------ 構築
        void DrawRings()
        {
            foreach (var rr in _layout.ring_radius)
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

        void DrawStation()
        {
            var go = new GameObject("Station(地球)");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _circle;
            sr.color = stationColor;
            go.transform.localScale = Vector3.one * 14f;
        }

        void BuildNodes()
        {
            int reps = Mathf.Max(1, densityMultiplier);
            for (int rep = 0; rep < reps; rep++)
            {
                // 複製時は微小オフセットで重なりを避ける(負荷確認用)
                float jitter = rep == 0 ? 0f : (rep * 3.1f);
                foreach (var n in _layout.nodes)
                    _views.Add(CreateNode(n, jitter));
            }
        }

        NodeView CreateNode(MapNode n, float jitter)
        {
            // ルート: 位置のみ。スケールは常に1(子がラベルに二重掛けされない)
            var go = new GameObject($"{n.no}_{n.name}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(n.x + jitter, n.y + jitter, 0);

            // アイコン(この子だけをズームに応じてスケール)
            var iconGo = new GameObject("icon");
            iconGo.transform.SetParent(go.transform, false);
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = _circle;
            sr.color = ColorFor(n);
            sr.sortingOrder = n.IsMoon ? 1 : 2;

            // ラベル(legacy TextMesh。TMP 依存を避けて PoC を軽くする)
            // characterSize は小さめ固定にし、見かけの大きさは localScale で制御。
            var lgo = new GameObject("label");
            lgo.transform.SetParent(go.transform, false);
            var tm = lgo.AddComponent<TextMesh>();
            tm.text = n.IsClusterParent ? $"{n.name} ⑂{n.cluster_size - 1}" : n.name;
            tm.anchor = TextAnchor.MiddleLeft;
            tm.fontSize = 64;
            tm.characterSize = 0.02f;
            tm.color = new Color(0.82f, 0.85f, 0.90f, 1f);
            lgo.GetComponent<MeshRenderer>().sortingOrder = 5;

            return new NodeView
            {
                data = n, tr = go.transform, icon = iconGo.transform,
                sr = sr, label = tm, labelTr = lgo.transform
            };
        }

        Color ColorFor(MapNode n)
        {
            if (n.is_mvp) return mvpColor;
            if (n.IsClusterParent) return clusterColor;
            if (n.IsMoon) return moonColor;
            return planetColor;
        }

        // ------------------------------------------------------------ 毎フレーム
        void Update()
        {
            HandleZoom();
            HandlePan();
            ApplyLod();
            _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.1f);
        }

        void HandleZoom()
        {
            float ortho = _cam.orthographicSize;

            // 実機: 2本指ピンチ
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

            // エディタ/デスクトップ: マウスホイール
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
                ortho -= scroll * scrollZoomSpeed * ortho;

            _cam.orthographicSize = Mathf.Clamp(ortho, minOrtho, _maxOrtho);
        }

        Vector3 _lastPan;
        bool _panning;
        void HandlePan()
        {
            // 1本指 / マウス左ドラッグでパン
            bool down = Input.GetMouseButton(0) && Input.touchCount < 2;
            if (down)
            {
                Vector3 w = _cam.ScreenToWorldPoint(Input.mousePosition);
                if (_panning)
                {
                    Vector3 d = _lastPan - w;
                    _cam.transform.position += new Vector3(d.x, d.y, 0);
                }
                _lastPan = _cam.ScreenToWorldPoint(Input.mousePosition);
                _panning = true;
            }
            else _panning = false;
        }

        void ApplyLod()
        {
            float ortho = _cam.orthographicSize;
            float ratio = ortho / _layout.world_extent;
            _lodTier = ratio >= lodOverviewAbove ? 1 : (ratio <= lodDetailBelow ? 3 : 2);

            // アイコン径・ラベルサイズはズームに比例 = 画面上ほぼ一定サイズ(セマンティックズーム)
            float iconScale = ortho * iconScreenFactor;
            float labelChar = ortho * labelScreenFactor * 0.01f;

            foreach (var v in _views)
            {
                bool visible = v.data.IsMoon ? (_lodTier == 3) : true;
                if (v.sr.enabled != visible) v.sr.enabled = visible;

                // 親の集約表記は L1 のみ。展開時(L2/L3)は素の名前。
                if (v.data.IsClusterParent)
                    v.label.text = _lodTier == 1
                        ? $"{v.data.name} ⑂{v.data.cluster_size - 1}"
                        : v.data.name;

                // ラベル密度をティア別に:
                //   L1 概観 … クラスター親のみ(土星圏⑦のような集約ラベル)
                //   L2 中間 … 衛星以外すべて(惑星・小惑星)
                //   L3 詳細 … 表示中の全ノード(衛星含む)
                bool labelOn = visible && (
                    _lodTier == 3 ? true :
                    _lodTier == 2 ? !v.data.IsMoon :
                                    v.data.IsClusterParent);
                var lmr = v.label.GetComponent<MeshRenderer>();
                if (lmr.enabled != labelOn) lmr.enabled = labelOn;

                // アイコンだけスケール(ルートは scale=1 のまま。ラベルへ二重掛けしない)
                v.icon.localScale = Vector3.one * iconScale;
                // ラベルは characterSize で見かけ一定サイズにし、アイコン右脇へ置く
                if (labelOn)
                {
                    v.label.characterSize = labelChar;
                    v.labelTr.localPosition = new Vector3(iconScale * 0.7f, 0f, 0f);
                }
            }
        }

        // ------------------------------------------------------------ FPS 表示
        void OnGUI()
        {
            var style = new GUIStyle
            {
                fontSize = 28,
                normal = { textColor = _fpsSmooth >= 58f ? Color.green :
                    (_fpsSmooth >= 45f ? Color.yellow : Color.red) }
            };
            string tier = _lodTier == 1 ? "L1 概観" : _lodTier == 2 ? "L2 中間" : "L3 詳細";
            GUI.Label(new Rect(12, 10, 600, 40),
                $"FPS {_fpsSmooth:00.0}  |  {tier}  |  orthoSize {_cam.orthographicSize:0}" +
                $"  |  nodes {_views.Count}", style);
        }

        // ------------------------------------------------------------ 円スプライト生成
        // アート未定でも動く円テクスチャを実行時生成(アンチエイリアス付き)。
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
            // pixelsPerUnit = size → スプライト1枚が 1 ワールドユニット径。localScale=径。
            return Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size);
        }
    }
}
