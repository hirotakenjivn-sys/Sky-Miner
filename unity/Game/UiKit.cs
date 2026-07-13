// uGUI レスポンシブUIの共有基盤。色パレット/フォント/アイコンSprite/生成ヘルパーを提供する。
// 方針:
//   - 全パネルは Canvas + CanvasScaler(Reference 1170×2532, Match 0.5)配下にコードで階層構築する
//     (プログラマティックuGUI)。シーン/プレハブ手編集はしない([[responsive-ui-todo]] 解消)。
//   - 言語非依存([[ui-language-agnostic]]):テキストは最小限、単位は出さない。アイコン+数字+色。
//   - フォントは日本語対応の OS 動的フォント(Hiragino 等)。TMP は使わない(TMP Essentials 未導入前提)。
//   - アイコンは既存 UiIcons の Texture2D を Sprite 化して Image に流用。
//
// 参照解像度(1170×2532)を基準に px 値を置く。CanvasScaler が実機サイズへ自動スケールするため、
// 端末アスペクト比が変わっても比率は崩れない(IMGUI版の崩れ [[responsive-ui-todo]] を解消)。
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    public static class UiKit
    {
        // ---- 参照解像度(iPhone Pro 縦)----
        public const float RefW = 1170f;
        public const float RefH = 2532f;

        // ---- パレット(既存IMGUI/ui_mockup_v1.html を踏襲)----
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color Panel  = Hex(0x1B2138);
        public static readonly Color Panel2 = Hex(0x1E2540);
        public static readonly Color Row    = Hex(0x161C30);
        public static readonly Color Line   = Hex(0x2B3352);
        public static readonly Color Txt    = Hex(0xE8ECF8);
        public static readonly Color Sub    = Hex(0x8B93B5);
        public static readonly Color Cyan   = Hex(0x5FD4F0);
        public static readonly Color Green  = Hex(0x6FD98F);
        public static readonly Color Amber  = Hex(0xF2B24C);
        public static readonly Color Dark   = Hex(0x07242E);   // ボタン上の濃色テキスト
        public static readonly Color Ship   = new Color(0.71f, 0.49f, 0.96f, 1f);

        // ---- 参照px基準のフォントサイズ ----
        public const int FTitle = 60;
        public const int FName  = 44;
        public const int FNum   = 40;
        public const int FSub   = 34;

        // ---- フォント(日本語対応の動的OSフォント)----
        static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    // OS フォント(macOS/iOS は Hiragino が日本語をカバー)。動的生成でグリフを都度取得。
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Hiragino Sans", "Hiragino Kaku Gothic ProN", "Arial Unicode MS",
                                "Yu Gothic", "Meiryo", "Arial", "Helvetica" }, 40);
                    if (_font == null)
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // 保険(Unity6の既定)
                }
                return _font;
            }
        }

        // ---- アイコン Sprite(スプライト・パイプライン経由。PNG(art/ui/…)があれば差し替わる)----
        static Sprite _white;
        public static Sprite White  { get { if (_white == null)  _white  = MakeWhite(); return _white; } }
        public static Sprite Coin   => SpriteBank.Ui("coin", out _);
        public static Sprite Cube   => SpriteBank.Ui("cube", out _);
        public static Sprite Bar    => SpriteBank.Ui("bar",  out _);
        public static Sprite Lock   => SpriteBank.Ui("lock", out _);

        // 資源アイコン Sprite(uGUI パネル用)。PNG(art/resources/<id>.png)優先、無ければ手続き。
        public static Sprite Resource(string id) => SpriteBank.Resource(id, out _);

        static Sprite ToSprite(Texture2D t)
            => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        static Sprite MakeWhite()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        }

        // ============================================================ 生成ヘルパー
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // 単色パネル/行/矩形。raycast=true でクリック吸収(=マップ入力ブロック)。
        public static Image Solid(string name, Transform parent, Color color, bool raycast = true)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = White; img.type = Image.Type.Simple; img.color = color; img.raycastTarget = raycast;
            return img;
        }

        // アイコン(Sprite)。既定でクリックは吸わない。
        public static Image Icon(string name, Transform parent, Sprite sprite, Color? tint = null)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = tint ?? Color.white; img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        // テキスト(Legacy uGUI Text + 動的フォント)。単位テキストは基本使わない。
        public static Text Label(string name, Transform parent, string text, int fontSize, Color color,
                                 TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = fontSize; t.color = color;
            t.alignment = anchor; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ボタン(背景Image + Button)。子(アイコン/テキスト)は戻り値 .transform の下に足す。
        public static Button Button(string name, Transform parent, Color bg)
        {
            var img = Solid(name, parent, bg, raycast: true);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
            cb.selectedColor    = Color.white;
            cb.disabledColor    = new Color(1f, 1f, 1f, 0.35f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.06f;
            btn.colors = cb;
            return btn;
        }

        // ============================================================ RectTransform 配置
        // 親内でアンカー矩形を割合指定(0..1)。余白は割合で入るので比率不整合が起きない。
        public static RectTransform Frac(RectTransform rt, float ax0, float ay0, float ax1, float ay1)
        {
            rt.anchorMin = new Vector2(ax0, ay0); rt.anchorMax = new Vector2(ax1, ay1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        // 親いっぱいに伸ばし、四辺の余白[px]だけ内側へ。
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // 上端に固定高の帯(横は左右余白で伸縮)。top=上端からの距離, h=高さ。
        public static RectTransform TopBand(RectTransform rt, float top, float h, float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -top - h); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        // 点アンカー配置(行内要素向け)。anchor は親内の基準点(0..1)。x,y は基準点からのオフセット。
        // pivotX: 0=左寄せ / 1=右寄せ。
        public static RectTransform Place(RectTransform rt, Vector2 anchor, float x, float y, float w, float h, float pivotX = 0f)
        {
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(pivotX, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        // 横方向は親幅の割合 [ax0..ax1] で伸縮、縦は固定高 h、縦中央から y オフセット。
        // 行内で「幅が親に比例する要素(スライダー等)」に使う(px固定でないので比率が崩れない)。
        public static RectTransform Span(RectTransform rt, float ax0, float ax1, float y, float h)
        {
            rt.anchorMin = new Vector2(ax0, 0.5f); rt.anchorMax = new Vector2(ax1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0, h); rt.anchoredPosition = new Vector2(0, y);
            return rt;
        }

        // 横スライダー(Unity 既定構成を最小構成でコード生成)。0..max、整数刻み。
        public static Slider Slider(string name, Transform parent, float max, float value)
        {
            var root = Node(name, parent);
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Solid("BG", root, Line, raycast: true);
            bg.rectTransform.anchorMin = new Vector2(0, 0.35f); bg.rectTransform.anchorMax = new Vector2(1, 0.65f);
            bg.rectTransform.offsetMin = Vector2.zero; bg.rectTransform.offsetMax = Vector2.zero;

            var fillArea = Node("Fill Area", root);
            fillArea.anchorMin = new Vector2(0, 0.35f); fillArea.anchorMax = new Vector2(1, 0.65f);
            fillArea.offsetMin = Vector2.zero; fillArea.offsetMax = Vector2.zero;
            var fill = Solid("Fill", fillArea, Cyan, raycast: false);
            fill.rectTransform.anchorMin = new Vector2(0, 0); fill.rectTransform.anchorMax = new Vector2(1, 1);
            fill.rectTransform.sizeDelta = Vector2.zero; fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;

            var handleArea = Node("Handle Slide Area", root);
            handleArea.anchorMin = new Vector2(0, 0); handleArea.anchorMax = new Vector2(1, 1);
            handleArea.offsetMin = Vector2.zero; handleArea.offsetMax = Vector2.zero;
            var handle = Solid("Handle", handleArea, Txt, raycast: true);
            handle.rectTransform.anchorMin = new Vector2(0, 0); handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.sizeDelta = new Vector2(34, 0);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0; slider.maxValue = Mathf.Max(1, max);
            slider.wholeNumbers = true;
            slider.value = Mathf.Clamp(value, 0, Mathf.Max(1, max));
            return slider;
        }

        public static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
