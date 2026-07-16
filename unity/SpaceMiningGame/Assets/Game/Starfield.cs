// 背景スターフィールド(手続き星空)。マップの最背面に敷く、描画のみのコンポーネント。
//
// 方針:
//   - カメラの子として画面に固定(パンで星は動かない=深宇宙の遠景)。
//   - orthographicSize に合わせて毎フレーム覆う大きさへリスケール(=星は画面上一定サイズ)。
//   - sortingOrder を大きく負にして全スプライト(天体・船・リング)より背面に描く。
//   - PNG(art/bg/starfield.png)があれば SpriteBank 経由でそちらを使う。
//   - アニメ(2026-07-15):背景を微かに明滅(ネビュラの呼吸)+ 前面にきらめく星を重ねる。
//
// カメラ・座標・入力・LOD には一切触れない(orthographicSize/position を「読む」だけ)。
// SpaceMapController とは独立に自動生成される(Play で自動的に背景が付く)。
using UnityEngine;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class Starfield : MonoBehaviour
    {
        const float CoverFactor = 1.15f;   // 画面をわずかに超えて覆う余裕
        const int SortingOrder = -1000;    // すべての前景スプライトより背面
        const int TwinkleCount = 30;       // きらめく星の数

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<Starfield>() != null) return;
            var go = new GameObject("Starfield");
            go.AddComponent<Starfield>();
        }

        Camera _cam;
        SpriteRenderer _sr;
        // きらめき星(カメラ固定・画面正規化位置)。
        SpriteRenderer[] _tw;
        float[] _nx, _ny, _phase, _speed, _size, _hue;
        static Sprite _dot;

        void EnsureBuilt()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;   // カメラ未生成なら次フレーム再試行

            if (_sr == null)
            {
                var go = new GameObject("StarfieldQuad");
                go.transform.SetParent(_cam.transform, false);
                _sr = go.AddComponent<SpriteRenderer>();
                _sr.sprite = SpriteBank.Starfield();
                _sr.sortingOrder = SortingOrder;
                go.transform.localPosition = new Vector3(0f, 0f, 5f);
                go.transform.localRotation = Quaternion.identity;
            }

            if (_tw == null)
            {
                if (_dot == null) _dot = MakeDot();
                _tw = new SpriteRenderer[TwinkleCount];
                _nx = new float[TwinkleCount]; _ny = new float[TwinkleCount];
                _phase = new float[TwinkleCount]; _speed = new float[TwinkleCount];
                _size = new float[TwinkleCount]; _hue = new float[TwinkleCount];
                for (int i = 0; i < TwinkleCount; i++)
                {
                    var g = new GameObject("twinkle");
                    g.transform.SetParent(_cam.transform, false);
                    var sr = g.AddComponent<SpriteRenderer>();
                    sr.sprite = _dot;
                    sr.sortingOrder = SortingOrder + 1;   // 星空の少し前・でも前景よりは背面
                    _tw[i] = sr;
                    _nx[i] = Random.Range(-0.5f, 0.5f);
                    _ny[i] = Random.Range(-0.5f, 0.5f);
                    _phase[i] = Random.Range(0f, 6.28318f);
                    _speed[i] = Random.Range(0.6f, 2.2f);       // 明滅の速さ
                    _size[i] = Random.Range(0.006f, 0.016f);    // 画面高に対する径
                    _hue[i] = Random.value;                     // わずかに色付き(青/白/橙)
                }
            }
        }

        // カメラのズーム(orthographicSize)に追従して常に画面を覆う大きさへ + アニメ。
        void LateUpdate()
        {
            EnsureBuilt();
            if (_sr == null) return;
            float h = 2f * _cam.orthographicSize;
            float w = h * _cam.aspect;
            _sr.transform.localScale = new Vector3(w * CoverFactor, h * CoverFactor, 1f);

            float t = Time.time;
            // 背景を微かに明滅(ネビュラの呼吸)。
            float breathe = 0.92f + 0.08f * Mathf.Sin(t * 0.25f);
            _sr.color = new Color(breathe, breathe, breathe, 1f);

            if (_tw == null) return;
            for (int i = 0; i < _tw.Length; i++)
            {
                var trs = _tw[i].transform;
                trs.localPosition = new Vector3(_nx[i] * w, _ny[i] * h, 5f);
                float d = _size[i] * h;
                trs.localScale = new Vector3(d, d, 1f);
                // 明滅:0.15〜1.0 を sin で。二乗して"チカッ"と鋭く。
                float s = 0.5f + 0.5f * Mathf.Sin(t * _speed[i] + _phase[i]);
                float a = 0.15f + 0.85f * s * s;
                Color c = _hue[i] < 0.6f ? Color.white
                        : _hue[i] < 0.85f ? new Color(0.75f, 0.85f, 1f)   // 青白
                                          : new Color(1f, 0.85f, 0.7f);   // 淡橙
                c.a = a;
                _tw[i].color = c;
            }
        }

        // 柔らかい光点(中心白→外側フェード)。
        static Sprite MakeDot()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) + (y - r + 0.5f) * (y - r + 0.5f));
                    float a = Mathf.Clamp01(1f - d / r);
                    a = a * a;   // 中心に集中
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }
    }
}
