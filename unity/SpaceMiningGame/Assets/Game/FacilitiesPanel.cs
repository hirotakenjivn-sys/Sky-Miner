// 施設パネル(精錬所・工場)。下部バーの「⚙」から開く全画面モーダル。
//   - 精錬所セクション:未購入なら 🔒+コスト+購入ボタン。購入済みなら 稼働中+対象鉱石アイコン+処理速度。
//   - 工場セクション:未購入なら購入ボタン。購入済みならレシピ一覧(製品/売値/入力素材アイコン列)。
//     各レシピに「生産」ボタン(タップで選択=生産開始。選択中はハイライト+稼働/素材待ち表示)。
// uGUI(Canvas+CanvasScaler)。作法は StorePanel/UpgradePanel を踏襲(暗幕タップ閉じ・排他は Controller 側)。
// ロジックは State(RefineryUnlocked/FactoryUnlocked/FactorySelected)と Factory の静的レシピを参照。
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class FacilitiesPanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        bool _open, _built;
        RectTransform _panel, _content;
        Text _balance;

        const float Pad = 40f, HeaderH = 96f, Gap = 16f;

        public bool IsOpen => _open;
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;
        public void Toggle() { if (_open) Close(); else Open(); }
        public void Close() { _open = false; if (_panel != null) _panel.gameObject.SetActive(false); }

        void Open() { EnsureBuilt(); _open = true; _panel.gameObject.SetActive(true); _panel.SetAsLastSibling(); Refresh(); }
        void Start() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            var root = UiRoot.Instance.Root;

            _panel = UiKit.Node("FacilitiesPanel", root);
            UiKit.Stretch(_panel);
            _panel.gameObject.SetActive(false);

            var back = UiKit.Solid("Backdrop", _panel, UiKit.Backdrop, raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Close);

            var box = UiKit.Solid("Box", _panel, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.04f, 0.10f, 0.96f, 0.90f);
            var boxRt = box.rectTransform;

            // ヘッダ:⚙ + 残高(金貨) + ✕
            var header = UiKit.Node("Header", boxRt);
            UiKit.TopBand(header, Pad, HeaderH, Pad, Pad);
            var gear = UiKit.Label("Gear", header, "⚙", UiKit.FTitle, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(gear.rectTransform, new Vector2(0, 0.5f), 0, 0, HeaderH, HeaderH, 0f);
            var coin = UiKit.Icon("Coin", header, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), HeaderH + 10, 0, HeaderH * 0.86f, HeaderH * 0.86f, 0f);
            _balance = UiKit.Label("Balance", header, "0", UiKit.FTitle, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_balance.rectTransform, new Vector2(0, 0.5f), HeaderH * 2 + 10, 0, 560, HeaderH, 0f);
            var close = UiKit.Button("Close", header, UiKit.Panel2);
            UiKit.Place(close.GetComponent<RectTransform>(), new Vector2(1, 0.5f), 0, 0, HeaderH, HeaderH, 1f);
            var cx = UiKit.Label("x", close.transform, "✕", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
            UiKit.Stretch(cx.rectTransform);
            close.onClick.AddListener(Close);

            var line = UiKit.Solid("Line", boxRt, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, Pad + HeaderH + 10, 2, Pad, Pad);

            var scrollHost = UiKit.Node("ScrollHost", boxRt);
            UiKit.Stretch(scrollHost, Pad, Pad + HeaderH + 24, Pad, Pad);
            var (_, content) = UiRoot.MakeScroll(scrollHost);
            _content = content;
        }

        // ------------------------------------------------------------ 中身再構築
        void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);

            float y = 0f;   // 上端からの距離[px](下方向が正)

            // 精錬所
            SectionHeader(ref y, "精錬所");
            RefineryRow(ref y);

            // 工場
            SectionHeader(ref y, "工場");
            if (!_ctrl.State.FactoryUnlocked)
                FactoryPurchaseRow(ref y);
            else
                foreach (var r in Factory.Recipes) RecipeRow(ref y, r);

            UiRoot.SetContentHeight(_content, y + 40);
        }

        // 上詰めで行(RectTransform)を1つ積む。color.a==0 なら背景なし。y を h+Gap ぶん進める。
        RectTransform AddRow(ref float y, float h, Color color)
        {
            var row = UiKit.Node("Row", _content);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0, -(y + h)); row.offsetMax = new Vector2(0, -y);
            if (color.a > 0f)
            {
                var bg = row.gameObject.AddComponent<Image>();
                bg.sprite = UiKit.White; bg.color = color; bg.raycastTarget = true;
            }
            y += h + Gap;
            return row;
        }

        void SectionHeader(ref float y, string title)
        {
            var row = AddRow(ref y, 90f, new Color(0, 0, 0, 0));
            var t = UiKit.Label("Sec", row, title, UiKit.FName, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(t.rectTransform, new Vector2(0, 0.5f), 20, 0, 600, 80, 0f);
        }

        // ---- 精錬所 ----
        void RefineryRow(ref float y)
        {
            var row = AddRow(ref y, 200f, UiKit.Row);
            if (!_ctrl.State.RefineryUnlocked)
            {
                var lockL = UiKit.Label("Lock", row, "🔒", UiKit.FName, UiKit.Sub, TextAnchor.MiddleLeft);
                UiKit.Place(lockL.rectTransform, new Vector2(0, 0.5f), 24, 40, 80, 70, 0f);
                var nameL = UiKit.Label("Name", row, "精錬所", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(nameL.rectTransform, new Vector2(0, 0.5f), 100, 40, 360, 70, 0f);

                double cost = BalanceOverride.RefineryUnlockCost;
                var coin = UiKit.Icon("Coin", row, UiKit.Coin);
                UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), 100, -40, 56, 56, 0f);
                var costL = UiKit.Label("Cost", row, $"{cost:#,0}", UiKit.FNum, UiKit.Sub);
                UiKit.Place(costL.rectTransform, new Vector2(0, 0.5f), 166, -40, 340, 60, 0f);

                var buy = UiKit.Button("Buy", row, UiKit.Green);
                UiKit.Span(buy.GetComponent<RectTransform>(), 0.72f, 0.96f, 0, 120);
                var bl = UiKit.Label("b", buy.transform, "🔓", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(bl.rectTransform);
                buy.interactable = _ctrl.State.Nova >= cost;
                buy.onClick.AddListener(() =>
                {
                    if (_ctrl.State.Nova < cost) return;
                    _ctrl.State.Nova -= cost;
                    _ctrl.State.RefineryUnlocked = true;
                    _ctrl.Toast("精錬所を建設しました");
                    Refresh();
                });
            }
            else
            {
                var nameL = UiKit.Label("Name", row, "精錬所", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(nameL.rectTransform, new Vector2(0, 0.5f), 24, 44, 300, 70, 0f);
                var on = UiKit.Label("On", row, "稼働中", UiKit.FSub, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(on.rectTransform, new Vector2(0, 0.5f), 24, -40, 260, 60, 0f);

                // 対象鉱石アイコン(鉄/Ni/Ti → 金属)。SpriteBank 手続きアイコンで表示。
                string[] ores = { "iron_ore", "nickel", "titanium" };
                float x = 300;
                foreach (var ore in ores)
                {
                    var ic = UiKit.Icon("Ore", row, UiKit.Resource(ore));
                    UiKit.Place(ic.rectTransform, new Vector2(0, 0.5f), x, 30, 64, 64, 0f);
                    UiKit.HookTip(ic.gameObject, _ctrl.NameOf(ore));
                    x += 74;
                }
                // 処理速度(» + 数値。単位は最小限)
                var spd = UiKit.Label("Spd", row, $"» {BalanceOverride.RefineUnitsPerSec:0.00}", UiKit.FSub, UiKit.Sub, TextAnchor.MiddleLeft);
                UiKit.Place(spd.rectTransform, new Vector2(0, 0.5f), 300, -40, 300, 60, 0f);
            }
        }

        // ---- 工場(未購入)----
        void FactoryPurchaseRow(ref float y)
        {
            var row = AddRow(ref y, 200f, UiKit.Row);
            var lockL = UiKit.Label("Lock", row, "🔒", UiKit.FName, UiKit.Sub, TextAnchor.MiddleLeft);
            UiKit.Place(lockL.rectTransform, new Vector2(0, 0.5f), 24, 40, 80, 70, 0f);
            var nameL = UiKit.Label("Name", row, "工場", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(nameL.rectTransform, new Vector2(0, 0.5f), 100, 40, 360, 70, 0f);

            double cost = BalanceOverride.FactoryUnlockCost;
            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), 100, -40, 56, 56, 0f);
            var costL = UiKit.Label("Cost", row, $"{cost:#,0}", UiKit.FNum, UiKit.Sub);
            UiKit.Place(costL.rectTransform, new Vector2(0, 0.5f), 166, -40, 340, 60, 0f);

            var buy = UiKit.Button("Buy", row, UiKit.Green);
            UiKit.Span(buy.GetComponent<RectTransform>(), 0.72f, 0.96f, 0, 120);
            var bl = UiKit.Label("b", buy.transform, "🔓", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(bl.rectTransform);
            buy.interactable = _ctrl.State.Nova >= cost;
            buy.onClick.AddListener(() =>
            {
                if (_ctrl.State.Nova < cost) return;
                _ctrl.State.Nova -= cost;
                _ctrl.State.FactoryUnlocked = true;
                _ctrl.Toast("工場を建設しました");
                Refresh();
            });
        }

        // ---- 工場(購入済み)レシピ行 ----
        void RecipeRow(ref float y, Factory.Recipe r)
        {
            bool selected = _ctrl.State.FactorySelected == r.productId;
            var row = AddRow(ref y, 300f, selected ? UiKit.Panel2 : UiKit.Row);

            if (selected)
            {
                var hl = UiKit.Solid("Hl", row, UiKit.Cyan, raycast: false);
                UiKit.Place(hl.rectTransform, new Vector2(0, 0.5f), 0, 0, 10, 300, 0f);
            }

            float top = 95f, bot = -95f;

            // 上段:製品アイコン + 名前 + 売値(金貨+数値)
            var pic = UiKit.Icon("P", row, UiKit.Resource(r.productId));
            UiKit.Place(pic.rectTransform, new Vector2(0, 0.5f), 24, top, 90, 90, 0f);
            UiKit.HookTip(pic.gameObject, r.productName);
            var nameL = UiKit.Label("Name", row, r.productName, UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(nameL.rectTransform, new Vector2(0, 0.5f), 130, top, 320, 70, 0f);
            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.52f, 0.5f), 0, top, 56, 56, 0f);
            var priceL = UiKit.Label("Price", row, $"{r.salePrice:#,0}", UiKit.FNum, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(priceL.rectTransform, new Vector2(0.52f, 0.5f), 64, top, 360, 60, 0f);

            // 中段:入力素材アイコン列(左端に「←」で製品に流し込むニュアンス)
            var arrow = UiKit.Label("Arr", row, "←", UiKit.FName, UiKit.Sub, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(arrow.rectTransform, new Vector2(0, 0.5f), 24, 0, 50, 60, 0f);
            float x = 90;
            foreach (var ing in r.inputs)
            {
                var ic = UiKit.Icon($"In_{ing.id}", row, UiKit.Resource(ing.id));
                UiKit.Place(ic.rectTransform, new Vector2(0, 0.5f), x, 0, 60, 60, 0f);
                UiKit.HookTip(ic.gameObject, _ctrl.NameOf(ing.id));
                x += 70;
            }

            // 下段:充足状態(選択中のみ)+ 生産/選択ボタン(右)
            if (selected)
            {
                bool canMake = Factory.CanCraftOne(_ctrl.Inventory, r.productId);
                var st = UiKit.Label("St", row, canMake ? "稼働中" : "素材待ち", UiKit.FSub,
                    canMake ? UiKit.Green : UiKit.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(st.rectTransform, new Vector2(0, 0.5f), 24, bot, 300, 60, 0f);
            }
            // 在庫の製品数(あれば)
            double held = _ctrl.Inventory.KgOf(r.productId);
            if (held >= 1)
            {
                var q = UiKit.Label("Q", row, $"×{held:#,0}", UiKit.FSub, UiKit.Sub, TextAnchor.MiddleLeft);
                UiKit.Place(q.rectTransform, new Vector2(0.40f, 0.5f), 0, bot, 220, 60, 0f);
            }

            var btn = UiKit.Button("Prod", row, selected ? UiKit.Cyan : UiKit.Green);
            UiKit.Span(btn.GetComponent<RectTransform>(), 0.74f, 0.96f, bot, 110);
            var bl = UiKit.Label("b", btn.transform, selected ? "■" : "▶", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(bl.rectTransform);
            string pid = r.productId;
            btn.onClick.AddListener(() =>
            {
                // タップ=選択(生産開始)。選択中の製品を再タップで停止(null)。工場は1製品ずつ生産。
                _ctrl.State.FactorySelected = (_ctrl.State.FactorySelected == pid) ? null : pid;
                Refresh();
            });
        }

        void Update()
        {
            if (!_open || _balance == null) return;
            _balance.text = $"{_ctrl.State.Nova:#,0}";
            // 注:購入ボタンの有効/無効と充足表示は開いた時点/操作時に確定する(0.5s毎の全再構築は
            //     スクロール位置がリセットされ操作を妨げるため行わない)。必要なら再度開いて反映。
        }
    }
}
