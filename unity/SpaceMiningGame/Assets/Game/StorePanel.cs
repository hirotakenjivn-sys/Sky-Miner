// 商品取引所(店)= 手動売却UI。ホーム在庫の鉱石を当日市況で売って NOVA にする。
// 明細「単価 × 数量 = 金額」+ 前日比(▲▼)。売却で在庫を減らし NOVA を加算し、コイン演出+残高フラッシュ。
//
// uGUI(Canvas+CanvasScaler)へ移行済み。ScrollRect で在庫をスクロール、行に短いスライダーと
// 「合計金額(金貨+数字)を✓の左隣」に配置。呼ぶロジックは従来通り Inventory.Take / State.Nova。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class StorePanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        bool _open;
        RectTransform _panel;          // 全体ルート(SetActive で開閉)
        RectTransform _content;        // 在庫リストの中身(行を積む)
        Text _balance;                 // ヘッダ残高
        Image _coinIcon;               // 残高コイン(演出の飛び先)
        bool _built;

        readonly Dictionary<string, float> _sellVal = new Dictionary<string, float>(); // 資源id→売却数

        // 売却演出(コインが残高へ飛ぶ + 残高フラッシュ)
        RectTransform _fxLayer;
        readonly List<Coin> _coins = new List<Coin>();
        class Coin { public Image img; public Vector3 start, target; public float age, life; public bool active; }
        float _balFlash;

        const float Pad = 40f, HeaderH = 96f, RowH = 300f;

        public bool IsOpen => _open;
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;
        public void Toggle() { if (_open) Close(); else Open(); }
        public void Close() { _open = false; if (_panel != null) _panel.gameObject.SetActive(false); }

        void Open()
        {
            EnsureBuilt();
            _open = true;
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            Refresh();
        }

        void Start() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            var root = UiRoot.Instance.Root;

            _panel = UiKit.Node("StorePanel", root);
            UiKit.Stretch(_panel);
            _panel.gameObject.SetActive(false);

            // 暗幕(タップで閉じる)
            var back = UiKit.Solid("Backdrop", _panel, UiKit.Backdrop, raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Close);

            // パネル箱(中央・幅92%・高72%)
            var box = UiKit.Solid("Box", _panel, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.04f, 0.14f, 0.96f, 0.86f);
            var boxRt = box.rectTransform;

            // ヘッダ:金貨 + 残高 + ✕
            var header = UiKit.Node("Header", boxRt);
            UiKit.TopBand(header, Pad, HeaderH, Pad, Pad);
            _coinIcon = UiKit.Icon("Coin", header, UiKit.Coin);
            UiKit.Place(_coinIcon.rectTransform, new Vector2(0, 0.5f), 0, 0, HeaderH * 0.86f, HeaderH * 0.86f, 0f);
            _balance = UiKit.Label("Balance", header, "0", UiKit.FTitle, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_balance.rectTransform, new Vector2(0, 0.5f), HeaderH, 0, 640, HeaderH, 0f);

            var close = UiKit.Button("Close", header, UiKit.Panel2);
            UiKit.Place(close.GetComponent<RectTransform>(), new Vector2(1, 0.5f), 0, 0, HeaderH, HeaderH, 1f);
            var cx = UiKit.Label("x", close.transform, "✕", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
            UiKit.Stretch(cx.rectTransform);
            close.onClick.AddListener(Close);

            // 区切り線
            var line = UiKit.Solid("Line", boxRt, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, Pad + HeaderH + 10, 2, Pad, Pad);

            // 列見出しアイコン(鉱石=数量 / 金貨=単価)
            var colHead = UiKit.Node("ColHead", boxRt);
            UiKit.TopBand(colHead, Pad + HeaderH + 22, 44, Pad, Pad);
            var hCube = UiKit.Icon("hCube", colHead, UiKit.Cube);
            UiKit.Place(hCube.rectTransform, new Vector2(0.30f, 0.5f), 0, 0, 40, 40, 0f);
            var hCoin = UiKit.Icon("hCoin", colHead, UiKit.Coin);
            UiKit.Place(hCoin.rectTransform, new Vector2(0.54f, 0.5f), 0, 0, 40, 40, 0f);

            // スクロール領域(残り全体)
            var scrollHost = UiKit.Node("ScrollHost", boxRt);
            UiKit.Stretch(scrollHost, Pad, Pad + HeaderH + 74, Pad, Pad);
            var (_, content) = UiRoot.MakeScroll(scrollHost);
            _content = content;

            // 演出レイヤ(最前面)
            _fxLayer = UiKit.Node("Fx", _panel);
            UiKit.Stretch(_fxLayer);
            _fxLayer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        }

        // ------------------------------------------------------------ 在庫リスト再構築
        void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);

            var entries = new List<InventoryEntry>(_ctrl.Inventory.Entries);
            UiRoot.SetContentHeight(_content, Mathf.Max(entries.Count, 1) * RowH);

            if (entries.Count == 0)
            {
                var empty = UiKit.Label("Empty", _content, "―", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
                UiKit.Place(empty.rectTransform, new Vector2(0.5f, 1f), 0, -RowH * 0.5f, 400, RowH, 0.5f);
                return;
            }

            for (int i = 0; i < entries.Count; i++)
                BuildRow(entries[i], i);
        }

        void BuildRow(InventoryEntry e, int index)
        {
            float held = (float)e.Kg;
            double price = _ctrl.PriceOf(e.Id);
            double change = _ctrl.Change1d(e.Id);
            float sq = _sellVal.TryGetValue(e.Id, out float stored) ? Mathf.Clamp(stored, 0, held) : held;

            // 行コンテナ(上詰め)
            var row = UiKit.Node($"Row_{e.Id}", _content);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0, -(index + 1) * RowH + 8); row.offsetMax = new Vector2(0, -index * RowH);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.White; bg.color = UiKit.Row; bg.raycastTarget = true;

            float top = RowH * 0.24f, bottom = -RowH * 0.22f, lineH = RowH * 0.30f;

            // 上段:名前 / 数量(鉱石+数字) / 単価(金貨+数字) / 前日比
            var name = UiKit.Label("Name", row, e.Name, UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(name.rectTransform, new Vector2(0.02f, 0.5f), 0, top, 360, lineH, 0f);

            var cube = UiKit.Icon("Cube", row, UiKit.Cube);
            UiKit.Place(cube.rectTransform, new Vector2(0.30f, 0.5f), 0, top, lineH * 0.62f, lineH * 0.62f, 0f);
            var qty = UiKit.Label("Qty", row, $"{Mathf.RoundToInt(sq):#,0}", UiKit.FNum, UiKit.Txt);
            UiKit.Place(qty.rectTransform, new Vector2(0.30f, 0.5f), lineH * 0.62f + 10, top, 220, lineH, 0f);

            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.54f, 0.5f), 0, top, lineH * 0.62f, lineH * 0.62f, 0f);
            var priceL = UiKit.Label("Price", row, $"{price:#,0}", UiKit.FNum, UiKit.Txt);
            UiKit.Place(priceL.rectTransform, new Vector2(0.54f, 0.5f), lineH * 0.62f + 10, top, 220, lineH, 0f);

            var chg = UiKit.Label("Chg", row, Market.ChangeText(change), UiKit.FSub, Market.ChangeColor(change), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(chg.rectTransform, new Vector2(0.72f, 0.5f), 0, top, 260, lineH, 0f);

            // 下段:スライダー(短め)/ 合計金額(金貨+数字)/ ✓ボタン(右)
            var slider = UiKit.Slider("Sell", row, held, sq);
            UiKit.Span(slider.GetComponent<RectTransform>(), 0.03f, 0.37f, bottom, lineH * 0.9f);

            var amtCoin = UiKit.Icon("AmtCoin", row, UiKit.Coin);
            UiKit.Place(amtCoin.rectTransform, new Vector2(0.40f, 0.5f), 0, bottom, lineH * 0.62f, lineH * 0.62f, 0f);
            var amount = UiKit.Label("Amount", row, $"{price * sq:#,0}", UiKit.FNum, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(amount.rectTransform, new Vector2(0.40f, 0.5f), lineH * 0.62f + 10, bottom, 340, lineH, 0f);

            var sell = UiKit.Button("SellBtn", row, UiKit.Green);
            var sellRt = sell.GetComponent<RectTransform>();
            UiKit.Span(sellRt, 0.76f, 0.98f, bottom, lineH * 0.95f);
            var chk = UiKit.Label("chk", sell.transform, "✓", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(chk.rectTransform);

            // スライダー操作 → 数量/金額を即時更新 + 記憶
            string id = e.Id;
            slider.onValueChanged.AddListener(v =>
            {
                _sellVal[id] = v;
                qty.text = $"{Mathf.RoundToInt(v):#,0}";
                amount.text = $"{price * v:#,0}";
                sell.interactable = v >= 1f;
            });
            sell.interactable = sq >= 1f;

            // 売却
            sell.onClick.AddListener(() =>
            {
                float q = slider.value;
                if (q < 1f) return;
                Vector3 origin = sellRt.position;
                SellPartial(e, q, origin);
                Refresh();   // 在庫が減る/消えるので作り直し
            });
        }

        void SellPartial(InventoryEntry e, float qty, Vector3 originScreen)
        {
            double price = _ctrl.PriceOf(e.Id);
            double sold = _ctrl.Inventory.Take(e.Id, qty);
            if (sold <= 0) return;
            double amount = price * sold;
            _ctrl.State.Nova += amount;
            _sellVal.Remove(e.Id);
            _ctrl.Toast($"{e.Name}   {price:#,0} × {sold:#,0} = {amount:#,0}");
            SpawnCoins(originScreen);
        }

        // ------------------------------------------------------------ コイン演出
        void SpawnCoins(Vector3 originScreen)
        {
            _balFlash = 0.5f;
            Vector3 target = _coinIcon != null ? _coinIcon.rectTransform.position : originScreen;
            for (int i = 0; i < 10; i++)
            {
                var c = GetFreeCoin();
                c.img.gameObject.SetActive(true);
                c.img.color = Color.white;
                c.start = originScreen + (Vector3)(Random.insideUnitCircle * 40f);
                c.target = target;
                c.age = 0f; c.life = 0.55f; c.active = true;
                c.img.rectTransform.position = c.start;
            }
        }

        Coin GetFreeCoin()
        {
            foreach (var c in _coins) if (!c.active) return c;
            var img = UiKit.Icon($"fxcoin{_coins.Count}", _fxLayer, UiKit.Coin);
            UiKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), 0, 0, 46, 46, 0.5f);
            var nc = new Coin { img = img };
            _coins.Add(nc);
            return nc;
        }

        void Update()
        {
            if (!_open) return;
            if (_balance != null) _balance.text = $"{_ctrl.State.Nova:#,0}";

            float dt = Time.unscaledDeltaTime;
            if (_balFlash > 0f && _balance != null)
            {
                _balFlash -= dt;
                _balance.color = Color.Lerp(UiKit.Green, Color.white, Mathf.Clamp01(_balFlash / 0.5f));
                if (_balFlash <= 0f) _balance.color = UiKit.Green;
            }
            for (int i = 0; i < _coins.Count; i++)
            {
                var c = _coins[i];
                if (!c.active) continue;
                c.age += dt;
                float t = Mathf.Clamp01(c.age / c.life);
                float e = 1f - (1f - t) * (1f - t);   // ease-out
                c.img.rectTransform.position = Vector3.Lerp(c.start, c.target, e);
                var col = c.img.color; col.a = 1f - t; c.img.color = col;
                if (t >= 1f) { c.active = false; c.img.gameObject.SetActive(false); }
            }
        }
    }
}
