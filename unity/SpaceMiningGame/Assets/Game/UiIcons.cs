// 言語非依存UIの共有アイコン(手続き生成テクスチャ)。単位テキスト(kg/NOVA等)の代わりに
// 金貨=通貨、鉱石キューブ=質量 を数字の左に添える。絵文字は使わない(豆腐回避)。
// 方針: ui-language-agnostic メモリ参照。
using UnityEngine;

namespace SpaceMining.Game
{
    public static class UiIcons
    {
        static Texture2D _coin, _cube, _lock, _bar;

        public static Texture2D Coin => _coin != null ? _coin
            : (_coin = Circle(new Color(0.96f, 0.78f, 0.28f)));   // 金貨=通貨(NOVA)
        public static Texture2D Cube => _cube != null ? _cube
            : (_cube = Solid(new Color(0.60f, 0.64f, 0.72f)));    // 鉱石=質量
        public static Texture2D Bar => _bar != null ? _bar
            : (_bar = Solid(new Color(0.37f, 0.83f, 0.94f)));     // シアンバー=採掘速度(ゲージと同色)
        public static Texture2D Lock => _lock != null ? _lock
            : (_lock = Padlock(new Color(0.90f, 0.84f, 0.55f)));  // 南京錠=未開放

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
    }
}
