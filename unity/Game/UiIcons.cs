// 言語非依存UIの共有アイコン(手続き生成テクスチャ)。単位テキスト(kg/NOVA等)の代わりに
// 金貨=通貨、鉱石キューブ=質量 を数字の左に添える。絵文字は使わない(豆腐回避)。
// 方針: ui-language-agnostic メモリ参照。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    public static class UiIcons
    {
        static Texture2D _coin, _cube, _lock, _bar;
        static readonly Dictionary<string, Texture2D> _resIcons = new Dictionary<string, Texture2D>();

        public static Texture2D Coin => _coin != null ? _coin
            : (_coin = MakeCoin());                              // 金貨=通貨(NOVA)。陰影+リム+ハイライト
        public static Texture2D Cube => _cube != null ? _cube
            : (_cube = IsoCube(new Color(0.62f, 0.66f, 0.74f))); // 鉱石=質量。等角3面キューブ
        public static Texture2D Bar => _bar != null ? _bar
            : (_bar = Solid(new Color(0.37f, 0.83f, 0.94f)));    // シアンバー=採掘速度(ゲージと同色)
        public static Texture2D Lock => _lock != null ? _lock
            : (_lock = Padlock(new Color(0.90f, 0.84f, 0.55f))); // 南京錠=未開放

        // 資源ごとの簡易アイコン(id→色相ハッシュ+形バリエーション)。市況/店/オフライン/ポップ用。
        // 手続きプレースホルダ。PNG 差し替えは SpriteBank.Resource(art/resources/<id>.png)経由。
        public static Texture2D ResourceIcon(string id)
        {
            if (_resIcons.TryGetValue(id, out var t)) return t;
            t = MakeResource(id);
            _resIcons[id] = t;
            return t;
        }

        // 数字の左に小アイコンを描き、テキスト開始xを返す。
        // size を渡さない場合は行高の 0.8 倍で正方に描く。
        public static float Draw(Texture2D icon, float x, float rowY, float rowH, float size = 0f)
        {
            if (size <= 0f) size = rowH * 0.8f;
            float iy = rowY + (rowH - size) * 0.5f;
            GUI.DrawTexture(new Rect(x, iy, size, size), icon);
            return x + size + size * 0.3f;
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        // 南京錠のシルエット(下=本体の角丸矩形、上=シャックルの半リング)。言語非依存の「未開放」。
        static Texture2D Padlock(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool on = false;
                    // 本体(下側の矩形。y は下=0)
                    if (x >= 8 && x < 24 && y >= 3 && y < 17) on = true;
                    // シャックル(上側の半リング)
                    float dx = x - 15.5f, dy = y - 18f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (y >= 17 && d <= 7.5f && d >= 4.5f) on = true;
                    px[y * n + x] = on ? c : new Color(c.r, c.g, c.b, 0f);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D Circle(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = n * 0.5f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - r + 0.5f) * (x - r + 0.5f) + (y - r + 0.5f) * (y - r + 0.5f));
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    px[y * n + x] = new Color(c.r, c.g, c.b, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // 金貨:放射状の陰影 + 明るいリム + 左上ハイライト(手描きを崩さず質感を上げる)。
        static Texture2D MakeCoin()
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var baseC = new Color(0.98f, 0.80f, 0.30f);
            var deep  = new Color(0.72f, 0.50f, 0.12f);   // 縁の濃い金
            var rim   = new Color(1.00f, 0.92f, 0.55f);   // 明るいリム
            float half = n * 0.5f, r = half - 1f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    if (a <= 0f) { px[y * n + x] = new Color(0, 0, 0, 0); continue; }
                    float u = d / r;                       // 0中心..1縁
                    Color c = Color.Lerp(baseC, deep, u * u);
                    // 縁の明るいリング(リム)
                    float ring = 1f - Mathf.Abs((u - 0.86f) / 0.10f);
                    if (ring > 0f) c = Color.Lerp(c, rim, Mathf.Clamp01(ring) * 0.8f);
                    // 左上ハイライト
                    float hlx = (dx + r * 0.32f), hly = (dy - r * 0.32f);
                    float hd = Mathf.Sqrt(hlx * hlx + hly * hly) / (r * 0.55f);
                    if (hd < 1f) c = Color.Lerp(c, Color.white, (1f - hd) * 0.45f);
                    px[y * n + x] = new Color(c.r, c.g, c.b, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // 等角3面キューブ(上=明・左=中・右=暗)。ベタ矩形より「鉱石の塊」らしい質感。
        static Texture2D IsoCube(Color c)
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top   = c * 1.15f; top.a = 1f;
            var left  = c * 0.85f; left.a = 1f;
            var right = c * 0.62f; right.a = 1f;
            var px = new Color32[n * n];
            // 正規化座標。中心基準の簡易アイソメ・キューブ。
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f;   // -0.5..0.5
                    float fy = (y + 0.5f) / n - 0.5f;   // 下=負
                    Color? face = IsoFace(fx, fy, top, left, right);
                    px[y * n + x] = face.HasValue ? (Color32)face.Value : new Color32(0, 0, 0, 0);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
        // 等角キューブの面判定。菱形の上半分=天面、下半分の左右=側面。範囲外は null。
        static Color? IsoFace(float x, float y, Color top, Color left, Color right)
        {
            const float w = 0.40f;   // 横半幅
            const float h = 0.20f;   // 菱形の縦半分(天面の傾き)
            const float bh = 0.24f;  // 側面の高さ
            float ax = Mathf.Abs(x);
            // 天面(上向き菱形): |x|/w + (y-上げ)/h <= 1、y>=0 側
            float topCy = bh * 0.5f;
            if (y >= topCy - 0.001f)
            {
                float ry = y - topCy;
                if (ax / w + ry / h <= 1f && ry <= h + 0.001f) return top;
            }
            // 側面(菱形の下辺から bh ぶん下へ伸びる縦壁)
            if (y < topCy)
            {
                // その x での天面下辺の y(菱形の下側の稜線)
                float edgeY = topCy - h * (1f - ax / w);
                if (ax <= w && y <= edgeY + 0.001f && y >= edgeY - bh)
                    return x < 0f ? left : right;
            }
            return null;
        }

        // 資源アイコン:id の「種別」で形を描き分ける(シルエットで区別)。
        //   精錬金属(*_refined)   → なめらかな金属バー(かまぼこ形)+つや。RefinedColor で色。
        //   工場製品(steel 等)    → 合金ブロック/延べ板(2.5D)+固有色。ProductColor で色。
        //   通常の鉱石(既存)      → ゴツゴツ塊。id ハッシュで色相と形(0キューブ/1宝石/2インゴット)。
        // 色はいずれも id をキーに決定的(乱数不可)。
        static Texture2D MakeResource(string id)
        {
            if (Refinery.IsRefinedId(id)) return MetalBar(RefinedColor(id));   // 精錬金属
            if (Factory.IsProduct(id))    return AlloyBlock(ProductColor(id), id == "carbide"); // 工場製品

            // ── 通常の鉱石(現状維持):id ハッシュで色相と形
            int h = Hash(id);
            float hue = (h & 0xFFFF) / 65535f;
            var col = Color.HSVToRGB(hue, 0.55f, 0.95f);
            int shape = (h >> 16) % 3;
            return shape switch
            {
                1 => Gem(col),
                2 => Ingot(col),
                _ => IsoCube(col),
            };
        }

        // 精錬金属の色(元鉱石の色相を継承しつつ現実寄り。id キーの固定テーブル=決定的)。
        //   鉄=中間グレー / ニッケル=淡緑灰 / チタン=青灰。未知の *_refined は鉄色で代替。
        static Color RefinedColor(string id) => id switch
        {
            "iron_refined"     => new Color(0.69f, 0.71f, 0.74f),   // 灰(素の鉄)
            "nickel_refined"   => new Color(0.73f, 0.79f, 0.72f),   // 淡緑灰
            "titanium_refined" => new Color(0.66f, 0.72f, 0.83f),   // 青灰
            _                  => new Color(0.70f, 0.72f, 0.75f),
        };

        // 工場製品の色(製品ごとに固有・id キーの固定テーブル=決定的)。未知の製品は鋼色で代替。
        static Color ProductColor(string id) => id switch
        {
            "steel"     => new Color(0.44f, 0.51f, 0.61f),   // 鋼=青灰
            "brass"     => new Color(0.82f, 0.64f, 0.20f),   // 真鍮=金黄
            "stainless" => new Color(0.83f, 0.86f, 0.90f),   // ステンレス=白銀
            "die_steel" => new Color(0.31f, 0.33f, 0.38f),   // ダイス鋼=濃灰
            "hss"       => new Color(0.52f, 0.47f, 0.62f),   // ハイス=紫灰
            "carbide"   => new Color(0.16f, 0.17f, 0.21f),   // 超硬合金=黒(エッジ光沢は別途)
            _           => new Color(0.44f, 0.51f, 0.61f),
        };

        // 精錬金属バー(かまぼこ形/横長カプセル):なめらかな金属光沢。
        // 断面ドームの明暗グラデ + 鋭いスペキュラ筋 + 端の丸み。鉱石の「塊」とシルエットで区別。
        static Texture2D MetalBar(Color c)
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            const float hw = 0.40f;    // 横半長
            const float ry = 0.17f;    // 縦半厚(=端の半径)
            const float cyv = 0.00f;   // 縦中心
            const float coreHalf = hw - ry;   // 直線胴の半長(端はここから半円)
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f;
                    float fy = (y + 0.5f) / n - 0.5f - cyv;
                    // カプセル(丸端の横バー)までの距離
                    float qx = Mathf.Clamp(fx, -coreHalf, coreHalf);
                    float dx = fx - qx;
                    float dist = Mathf.Sqrt(dx * dx + fy * fy);
                    float a = Mathf.Clamp01((ry - dist) * n / 1.5f);   // ふち AA
                    if (a <= 0f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }

                    float v = fy / ry;                     // -1 下 .. +1 上(断面ドーム)
                    float g = Mathf.SmoothStep(0f, 1f, (v + 1f) * 0.5f);
                    Color cc = Color.Lerp(c * 0.70f, c * 1.14f, g);
                    // 上寄りの鋭いスペキュラ筋(金属のつや)
                    float sv = v - 0.42f;
                    float spec = Mathf.Exp(-(sv * sv) / (2f * 0.020f));
                    cc = Color.Lerp(cc, Color.white, spec * 0.60f);
                    // 上端リムをわずかに明るく、下端を締める(立体感)
                    if (v > 0.80f) cc = Color.Lerp(cc, Color.white, (v - 0.80f) / 0.20f * 0.25f);
                    if (v < -0.72f) cc *= 0.82f;
                    cc.a = a;
                    px[y * n + x] = cc;
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // 工場製品(合金ブロック/延べ板):2.5D の平たいビレット(上面=明・前面=胴)。
        // なめらかな面 + 稜線ハイライトで「加工品」らしさ。鉱石キューブ(3面)ともバーとも区別。
        // edgeSheen=true(超硬合金)はシルエット外周に白い光沢エッジを足す。
        static Texture2D AlloyBlock(Color c, bool edgeSheen)
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top   = c * 1.28f; top.a = 1f;     // 上面(受光)
            var front = c;         front.a = 1f;    // 前面(胴)
            // 面の配置(fx,fy は -0.5..0.5)
            const float fb = -0.24f, fm = 0.04f, ft = 0.26f;   // 前面下端 / 稜線 / 上面奥端
            const float fw = 0.34f, skew = 0.13f;              // 前面半幅 / 上面の奥行きずれ
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f;
                    float fy = (y + 0.5f) / n - 0.5f;
                    Color cc; bool on = false; float edge = 1f;

                    if (fy >= fb && fy < fm && Mathf.Abs(fx) <= fw)
                    {
                        // 前面(矩形):左をわずかに明るく縦に軽い陰影
                        float sh = 1f - (fx + fw) / (2f * fw) * 0.18f;
                        cc = front * sh; on = true;
                        edge = Mathf.Min(Mathf.Min(fx + fw, fw - fx), Mathf.Min(fy - fb, fm - fy));
                    }
                    else if (fy >= fm && fy <= ft)
                    {
                        // 上面(平行四辺形):奥へ行くほど右へ skew
                        float tt = (fy - fm) / (ft - fm);
                        float xoff = skew * tt;
                        float xlo = -fw + xoff, xhi = fw + xoff;
                        if (fx >= xlo && fx <= xhi)
                        {
                            // 上面に斜めの受光グラデ(奥ほど明るい)
                            float lit = 0.92f + 0.16f * tt;
                            cc = top * lit; on = true;
                            edge = Mathf.Min(Mathf.Min(fx - xlo, xhi - fx), Mathf.Min(fy - fm, ft - fy));
                        }
                        else cc = default;
                    }
                    else cc = default;

                    if (!on) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }

                    // 稜線(上面と前面の境目)を明るいハイライトで締める
                    if (fy >= fm - 0.02f && fy <= fm + 0.02f && Mathf.Abs(fx) <= fw)
                        cc = Color.Lerp(cc, Color.white, 0.45f);
                    // 超硬合金:外周エッジに光沢
                    if (edgeSheen && edge < 0.03f)
                        cc = Color.Lerp(cc, Color.white, (1f - edge / 0.03f) * 0.55f);
                    cc.a = 1f;
                    px[y * n + x] = cc;
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
        static int Hash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h & 0x7FFFFFFF;
            }
        }

        // 宝石(ダイヤ型):上面と下面カットの陰影。
        static Texture2D Gem(Color c)
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top = c * 1.2f; top.a = 1f;
            var bot = c * 0.7f; bot.a = 1f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f;
                    float fy = (y + 0.5f) / n - 0.5f;
                    float ax = Mathf.Abs(fx);
                    bool on = ax / 0.36f + Mathf.Abs(fy) / 0.44f <= 1f;   // 縦長ひし形
                    if (!on) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                    // 上半分は明るく、テーブル面(上部の水平カット)を白寄せ
                    Color cc = fy > 0.18f ? Color.Lerp(top, Color.white, 0.35f)
                                          : fy > 0f ? top : bot;
                    if (fx < -0.02f) cc *= 0.9f; else cc *= 1.02f;
                    cc.a = 1f;
                    px[y * n + x] = cc;
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        // インゴット(角丸の延べ棒):上面ハイライトの横長ブロック。
        static Texture2D Ingot(Color c)
        {
            const int n = 48;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var top = c * 1.15f; top.a = 1f;
            var body = c; body.a = 1f;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f;
                    float fy = (y + 0.5f) / n - 0.5f;
                    // 台形(下広がり)+ 角丸っぽい判定
                    float w = Mathf.Lerp(0.30f, 0.42f, Mathf.InverseLerp(0.22f, -0.22f, fy));
                    bool on = Mathf.Abs(fx) <= w && fy >= -0.24f && fy <= 0.22f;
                    if (!on) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                    Color cc = fy > 0.10f ? Color.Lerp(top, Color.white, 0.25f) : body;
                    cc.a = 1f;
                    px[y * n + x] = cc;
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
