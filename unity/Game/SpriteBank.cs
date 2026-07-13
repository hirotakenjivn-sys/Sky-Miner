// スプライト・パイプライン(差し替え可能アート基盤 / option②)。
//
// 目的: 「所定フォルダに PNG を置くだけで本番アートに差し替わる」状態を作る。
//   Assets/Resources/art/bodies/<key>.png … 天体(<no> または 種別キー)
//   Assets/Resources/art/ships/<key>.png  … 船(miner / transport)
//   Assets/Resources/art/resources/<id>.png … 資源アイコン(resources.json の id)
//   Assets/Resources/art/ui/<key>.png     … UIアイコン(coin / cube / bar / lock)
//   Assets/Resources/art/bg/starfield.png … 背景スターフィールド
// PNG が見つからなければ、当座の見栄え用「手続きプレースホルダ」へ自動フォールバックする。
//
// 命名規約・差し替え手順は Assets/Resources/art/README.md を参照。
//
// 描画のみ(座標・入力・バランス・状態機械には一切触れない)。呼び出し側は
// このバンクが返す Sprite を SpriteRenderer/Image に差すだけ。
//
// 【色の扱い】天体・船の手続きプレースホルダは「グレースケール(輝度)」で生成し、
// 呼び出し側が既存の種別色(ColorFor / MinerColor 等)で tint(乗算)する。これにより
// 既存の色割り当てロジックをそのまま活かせる。PNG アート採用時は tint を白にして素の絵を出す
// (呼び出し側が out isArt を見て判断)。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    public static class SpriteBank
    {
        // ------------------------------------------------------------ 公開 API
        // 天体スプライト。PNG(art/bodies/<no> → art/bodies/<種別キー>)優先、無ければ手続き球。
        // isArt=true のとき PNG(フルカラー)なので呼び出し側は tint=白にする。
        public static Sprite Body(CelestialBody b, out bool isArt)
        {
            string type = TypeKey(b);
            // 個体指定(art/bodies/<no>.png)→ 種別(art/bodies/planet.png 等)の順で PNG を探す
            var png = b.IsStation ? LoadPng("art/bodies/station")
                                  : (LoadPng($"art/bodies/{b.No}") ?? LoadPng($"art/bodies/{type}"));
            if (png != null) { isArt = true; return png; }
            isArt = false;
            return ProcBody(type);
        }

        // 船マーカー。PNG(art/ships/<key>)優先、無ければ手続きの宇宙船シルエット(グレースケール)。
        public static Sprite Ship(ShipType t, out bool isArt)
        {
            string key = t == ShipType.Miner ? "miner" : "transport";
            var png = LoadPng($"art/ships/{key}");
            if (png != null) { isArt = true; return png; }
            isArt = false;
            return ProcShip(key);
        }

        // 資源アイコン(uGUI/ワールド共用の Sprite)。PNG(art/resources/<id>)優先、無ければ手続き。
        public static Sprite Resource(string id, out bool isArt)
        {
            var png = LoadPng($"art/resources/{id}");
            if (png != null) { isArt = true; return png; }
            isArt = false;
            if (!_resSprite.TryGetValue(id, out var sp))
            {
                sp = ToSprite(UiIcons.ResourceIcon(id));
                _resSprite[id] = sp;
            }
            return sp;
        }

        // UIアイコン(coin/cube/bar/lock)。PNG(art/ui/<key>)優先、無ければ既存 UiIcons テクスチャ。
        public static Sprite Ui(string key, out bool isArt)
        {
            var png = LoadPng($"art/ui/{key}");
            if (png != null) { isArt = true; return png; }
            isArt = false;
            if (!_uiSprite.TryGetValue(key, out var sp))
            {
                Texture2D t = key switch
                {
                    "coin" => UiIcons.Coin,
                    "cube" => UiIcons.Cube,
                    "bar"  => UiIcons.Bar,
                    "lock" => UiIcons.Lock,
                    _      => UiIcons.Cube,
                };
                sp = ToSprite(t);
                _uiSprite[key] = sp;
            }
            return sp;
        }

        // 背景スターフィールド。PNG(art/bg/starfield)優先、無ければ手続き星空。
        public static Sprite Starfield()
        {
            var png = LoadPng("art/bg/starfield");
            if (png != null) return png;
            return _starfield ??= ProcStarfield();
        }

        // 天体 → 種別キー(PNG 命名・手続きの分岐に使う)
        public static string TypeKey(CelestialBody b)
        {
            if (b.IsStation) return "station";
            switch (b.TypeLabel)
            {
                case "惑星":     return "planet";
                case "衛星":     return "moon";
                case "準惑星":   return "dwarf";
                case "小惑星":   return "asteroid";
                case "彗星":     return "comet";
                case "外縁天体": return "tno";
                default:         return "planet";
            }
        }

        // ------------------------------------------------------------ 内部: 読み込み/キャッシュ
        static readonly Dictionary<string, Sprite> _png = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> _procBody = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> _procShip = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> _resSprite = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> _uiSprite = new Dictionary<string, Sprite>();
        static Sprite _starfield;

        // Resources から Sprite を読む(結果はキャッシュ。存在しなければ null)。
        // PNG は Import 設定が「Sprite (2D and UI)」であることが前提(README 参照)。
        static Sprite LoadPng(string path)
        {
            if (_png.TryGetValue(path, out var sp)) return sp;
            sp = Resources.Load<Sprite>(path);
            if (sp == null)
            {
                // Texture Type=Sprite でない(=Default取込)場合でも動くよう、Texture として読んで
                // 実行時に Sprite 化する。→ ユーザーは取込設定を変えず PNG を置くだけでよい。
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                    // PPU=最大寸法 → native≈1ワールド単位(手続きスプライトの規約に合わせる)。
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), Mathf.Max(tex.width, tex.height));
            }
            _png[path] = sp;   // null もキャッシュして再探索を避ける
            return sp;
        }

        static Sprite ToSprite(Texture2D t)
            => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width);

        // ------------------------------------------------------------ 採掘ロボのアニメ
        // art/mining/robot.png(正方コマを横に並べたスプライトシート)を実行時にコマ分割して返す。
        // 例: 256×256 を 8 コマ = 2048×256。PNG が無ければ null(=ロボ非表示・従来の採掘演出のまま)。
        // ピクセルアートは Import で Filter Mode=Point にすると綺麗(README 参照)。
        static Sprite[] _robotFrames; static bool _robotChecked;
        public static Sprite[] MiningRobotFrames()
        {
            if (_robotChecked) return _robotFrames;
            _robotChecked = true;
            var sheet = LoadPng("art/mining/robot");
            if (sheet == null) { _robotFrames = null; return null; }
            var tex = sheet.texture;
            int h = tex.height;
            int n = tex.width / Mathf.Max(1, h);   // 正方コマ横並びのコマ数
            if (n < 2)
            {
                // 横帯でない(=1枚絵)ときは切り抜かず全体を1コマ静止として使う。
                _robotFrames = new[] { sheet };
                return _robotFrames;
            }
            _robotFrames = new Sprite[n];
            for (int i = 0; i < n; i++)
                _robotFrames[i] = Sprite.Create(tex, new Rect(i * h, 0, h, h),
                                                new Vector2(0.5f, 0.08f), h);   // 足元ピボット(地面に立つ)
            return _robotFrames;
        }

        // ------------------------------------------------------------ 手続き: 天体(陰影付き球)
        // グレースケール輝度で球を描く(呼び出し側が種別色で tint)。種別ごとに:
        //   moon=小さめ+クレータ / dwarf=環 / asteroid=いびつ+小さめ / comet=コマ(淡い光冠) /
        //   station=基地ハブ(二重リング) / planet・tno=素の球。
        static Sprite ProcBody(string key)
        {
            if (_procBody.TryGetValue(key, out var cached)) return cached;

            const int n = 128;
            float half = n * 0.5f;
            // テクスチャ内でディスクが占める割合(=見かけの大きさ)。座標/スケールは変えず、
            // 絵の中身の大きさで「衛星は小さめ」等を表現する(ApplyLod の localScale は不変)。
            float discFrac =
                key == "moon"     ? 0.70f :
                key == "asteroid" ? 0.74f :
                key == "dwarf"    ? 0.60f :
                key == "comet"    ? 0.56f :
                key == "station"  ? 0.82f : 0.90f;

            var light = new Vector3(-0.42f, 0.46f, 0.78f).normalized;  // 左上からの光
            var px = new Color32[n * n];

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // -1..1 正規化座標(中心原点)
                float ux = (x + 0.5f - half) / half;
                float uy = (y + 0.5f - half) / half;
                float ang = Mathf.Atan2(uy, ux);

                // 小惑星はいびつな輪郭(角度ノイズで半径を揺らす)
                float rFrac = discFrac;
                if (key == "asteroid")
                    rFrac *= 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(ang * 5f + 1.3f)
                                                     * Mathf.Cos(ang * 3f));

                float d = Mathf.Sqrt(ux * ux + uy * uy);
                float L = 0f, A = 0f;

                if (d <= rFrac)
                {
                    // 球面法線 → ランバート陰影(リムが暗い立体感)
                    float u = d / rFrac;
                    float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                    var nrm = new Vector3(ux / rFrac, uy / rFrac, nz);
                    float ndl = Mathf.Clamp01(Vector3.Dot(nrm, light));
                    L = Mathf.Lerp(0.28f, 1.0f, Mathf.Pow(ndl, 0.85f));

                    // 衛星クレータ(数個の暗い円)
                    if (key == "moon") L *= Crater(ux, uy, rFrac);
                    // 拠点ハブ:明るい二重リング(基地マーク)
                    if (key == "station") L = Mathf.Clamp01(L * StationHub(d, rFrac));

                    // ふち AA
                    float aa = Mathf.Clamp01((rFrac - d) * half / 1.5f);
                    A = aa;
                }
                else if (key == "dwarf")
                {
                    // 環(準惑星)。y を潰した楕円座標でリング帯を判定(球の外側のみ)
                    float ry = uy / 0.34f;
                    float rd = Mathf.Sqrt(ux * ux + ry * ry);
                    if (rd >= 0.72f && rd <= 0.98f)
                    {
                        float band = 1f - Mathf.Abs((rd - 0.85f) / 0.13f);   // 中央で濃い
                        L = 0.62f;
                        A = Mathf.Clamp01(band) * 0.85f;
                    }
                }
                else if (key == "comet")
                {
                    // コマ(淡い光冠)。ディスク外へゆるく減衰
                    float halo = Mathf.Clamp01(1f - (d - rFrac) / (0.42f));
                    if (halo > 0f) { L = 0.9f; A = halo * halo * 0.5f; }
                }

                byte l8 = (byte)(Mathf.Clamp01(L) * 255f);
                px[y * n + x] = new Color32(l8, l8, l8, (byte)(Mathf.Clamp01(A) * 255f));
            }

            var tex = MakeTex(n, px);
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            _procBody[key] = sprite;
            return sprite;
        }

        // 衛星クレータ:固定位置の暗い円をいくつか。戻り値=輝度倍率(0.7〜1)。
        static float Crater(float ux, float uy, float rFrac)
        {
            float m = 1f;
            m *= CraterAt(ux, uy, -0.22f, 0.10f, 0.16f * rFrac);
            m *= CraterAt(ux, uy, 0.18f, -0.20f, 0.13f * rFrac);
            m *= CraterAt(ux, uy, 0.05f, 0.28f, 0.10f * rFrac);
            return m;
        }
        static float CraterAt(float ux, float uy, float cx, float cy, float r)
        {
            float d = Mathf.Sqrt((ux - cx) * (ux - cx) + (uy - cy) * (uy - cy));
            if (d >= r) return 1f;
            return Mathf.Lerp(0.72f, 1f, d / r);   // 中心ほど暗い
        }

        // 拠点の基地ハブ:リム寄りに明るい帯を1本(基地の輪)。戻り値=輝度倍率。
        static float StationHub(float d, float rFrac)
        {
            float t = d / rFrac;                    // 0..1
            float ring = 1f - Mathf.Abs((t - 0.74f) / 0.08f);
            if (ring > 0f) return 1f + ring * 0.35f;   // 帯を少し明るく(tint 前提で緑が締まる)
            float core = 1f - Mathf.Abs(t / 0.14f);
            if (core > 0f) return 1f + core * 0.25f;   // 中央コアも少し明るく
            return 1f;
        }

        // ------------------------------------------------------------ 手続き: 船(くさび形シルエット)
        // グレースケール輝度で描く(呼び出し側が Miner/Transport 色で tint)。+Y が機首。
        static Sprite ProcShip(string key)
        {
            if (_procShip.TryGetValue(key, out var cached)) return cached;

            const int n = 64;
            bool transport = key == "transport";
            var px = new Color32[n * n];

            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = (x + 0.5f) / n;          // 0..1
                float fy = (y + 0.5f) / n;          // 0..1(上=機首側)
                float cx = fx - 0.5f;               // 中心からの横位置 -0.5..0.5

                bool on;
                float halfWidth;
                if (!transport)
                {
                    // 採掘船:すらりとした三角(機首 0.94 → 尾 0.10)
                    float t = Mathf.InverseLerp(0.94f, 0.10f, fy); // 機首0 → 尾1
                    if (t < 0f || t > 1f) { on = false; halfWidth = 0f; }
                    else { halfWidth = Mathf.Lerp(0.02f, 0.30f, t); on = Mathf.Abs(cx) <= halfWidth; }
                }
                else
                {
                    // 輸送船:寸胴のくさび(機首 0.90 → 尾 0.14)+ 左右カーゴの張り出し
                    float t = Mathf.InverseLerp(0.90f, 0.14f, fy);
                    if (t < 0f || t > 1f) { on = false; halfWidth = 0f; }
                    else
                    {
                        halfWidth = Mathf.Lerp(0.06f, 0.34f, t);
                        on = Mathf.Abs(cx) <= halfWidth;
                        // 胴中央のカーゴ張り出し(横に少し広い矩形)
                        if (fy > 0.28f && fy < 0.62f && Mathf.Abs(cx) <= 0.42f) on = true;
                    }
                }

                float L = 0f, A = 0f;
                if (on)
                {
                    // 中央を明るく、縁を暗く(申し訳程度の陰影)。機首側をやや明るく。
                    float shade = 1f - Mathf.Abs(cx) / 0.5f * 0.5f;
                    shade *= Mathf.Lerp(0.85f, 1f, fy);
                    L = Mathf.Clamp01(0.55f + 0.45f * shade);
                    A = 1f;
                    // コックピット窓(機首寄りの暗点)
                    float wy = fy - 0.72f, wd = Mathf.Sqrt(cx * cx + wy * wy);
                    if (wd < 0.09f) L *= 0.5f;
                }
                byte l8 = (byte)(L * 255f);
                px[y * n + x] = new Color32(l8, l8, l8, (byte)(A * 255f));
            }

            var tex = MakeTex(n, px);
            var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            _procShip[key] = sprite;
            return sprite;
        }

        // ------------------------------------------------------------ 手続き: 背景スターフィールド
        // 透明背景に3階級の星を散らし、淡い星雲の靄を数点。tint は白(そのまま表示)。
        static Sprite ProcStarfield()
        {
            const int n = 512;
            var px = new Color32[n * n];
            // 決定的乱数(毎回同じ星空)
            var rng = new System.Random(20260714);

            // 淡い星雲(数点の色付きガウス靄)
            int nebulae = 4;
            var neb = new (float x, float y, float r, Color c)[nebulae];
            for (int i = 0; i < nebulae; i++)
            {
                float hue = (float)rng.NextDouble();
                neb[i] = ((float)rng.NextDouble() * n, (float)rng.NextDouble() * n,
                          n * (0.18f + 0.16f * (float)rng.NextDouble()),
                          Color.HSVToRGB(hue, 0.55f, 1f));
            }
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < nebulae; i++)
                {
                    float dx = x - neb[i].x, dy = y - neb[i].y;
                    float dd = (dx * dx + dy * dy) / (neb[i].r * neb[i].r);
                    float a = Mathf.Exp(-dd) * 0.10f;   // かなり淡く
                    r += neb[i].c.r * a; g += neb[i].c.g * a; b += neb[i].c.b * a;
                }
                float aa = Mathf.Clamp01(Mathf.Max(r, Mathf.Max(g, b)));
                px[y * n + x] = new Color(r, g, b, aa);
            }

            // 星(点)。密度と明るさに3階級。
            int stars = 1400;
            for (int i = 0; i < stars; i++)
            {
                int x = rng.Next(n), y = rng.Next(n);
                double roll = rng.NextDouble();
                float bright = roll < 0.75 ? 0.5f : roll < 0.95 ? 0.8f : 1f;
                float tintHue = (float)rng.NextDouble();
                // 大半は白、たまに寒色/暖色を薄く混ぜる
                Color c = Color.Lerp(Color.white, Color.HSVToRGB(tintHue, 0.5f, 1f), 0.25f) * bright;
                Blend(px, n, x, y, c, bright);
                if (bright >= 1f)   // 明るい星は十字にわずかににじませる
                {
                    Blend(px, n, x + 1, y, c, 0.35f); Blend(px, n, x - 1, y, c, 0.35f);
                    Blend(px, n, x, y + 1, c, 0.35f); Blend(px, n, x, y - 1, c, 0.35f);
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        static void Blend(Color32[] px, int n, int x, int y, Color c, float a)
        {
            if (x < 0 || x >= n || y < 0 || y >= n) return;
            int i = y * n + x;
            Color bg = px[i];
            float na = Mathf.Clamp01(bg.a + a);
            Color outc = Color.Lerp(bg, c, a); outc.a = na;
            px[i] = outc;
        }

        static Texture2D MakeTex(int n, Color32[] px)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }
    }
}
