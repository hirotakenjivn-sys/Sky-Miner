// 「本日の市況」パネル + 採掘資源の解禁。アンロック済み惑星が産出する資源のみ・基準単価の安い順。
// 鉄=解禁済み、他は🔒+減光。安い順の先頭ロック1件だけに解禁ボタン(金貨+費用)。押すと確認モーダル。
//
// uGUI へ移行済み。並び=UnlockedBodyResourceIds()、次解禁=NextLockedResource()、費用=NextUnlockCost()、
// 確定=TryUnlock(id)。ロジックは従来通り。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class MarketPanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        bool _open, _built;
        RectTransform _panel, _content, _confirm;
        Text _confirmName, _confirmCost;
        string _pendingUnlockId;

        const float Pad = 40f, HeaderH = 96f, RowH = 150f;

        public bool IsOpen => _open;
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;
        public void Toggle() { if (_open) Close(); else Open(); }
        public void Close() { _open = false; _pendingUnlockId = null; if (_panel != null) _panel.gameObject.SetActive(false); }

        void Open() { EnsureBuilt(); _open = true; _panel.gameObject.SetActive(true); _panel.SetAsLastSibling(); HideConfirm(); Refresh(); }
        void Start() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            var root = UiRoot.Instance.Root;

            _panel = UiKit.Node("MarketPanel", root);
            UiKit.Stretch(_panel);
            _panel.gameObject.SetActive(false);

            var back = UiKit.Solid("Backdrop", _panel, UiKit.Backdrop, raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Close);

            var box = UiKit.Solid("Box", _panel, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.04f, 0.13f, 0.96f, 0.87f);
            var boxRt = box.rectTransform;

            // ヘッダ:▲▼ + 日付
            var header = UiKit.Node("Header", boxRt);
            UiKit.TopBand(header, Pad, HeaderH, Pad, Pad);
            var title = UiKit.Label("Title", header, $"▲▼  {_ctrl.Market?.Date}", UiKit.FTitle, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0, 0.5f), 0, 0, 700, HeaderH, 0f);
            var close = UiKit.Button("Close", header, UiKit.Panel2);
            UiKit.Place(close.GetComponent<RectTransform>(), new Vector2(1, 0.5f), 0, 0, HeaderH, HeaderH, 1f);
            var cx = UiKit.Label("x", close.transform, "✕", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
            UiKit.Stretch(cx.rectTransform);
            close.onClick.AddListener(Close);

            var line = UiKit.Solid("Line", boxRt, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, Pad + HeaderH + 8, 2, Pad, Pad);

            var scrollHost = UiKit.Node("ScrollHost", boxRt);
            UiKit.Stretch(scrollHost, Pad, Pad + HeaderH + 20, Pad, Pad);
            var (_, content) = UiRoot.MakeScroll(scrollHost);
            _content = content;

            BuildConfirm();
        }

        // 解禁確認モーダル(🔒 + 資源名 + 費用 + ✓/✕)
        void BuildConfirm()
        {
            _confirm = UiKit.Node("Confirm", _panel);
            UiKit.Stretch(_confirm);
            _confirm.gameObject.SetActive(false);

            var back = UiKit.Solid("CBackdrop", _confirm, UiKit.Backdrop, raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(HideConfirm);   // ダイアログ外タップでキャンセル

            var box = UiKit.Solid("CBox", _confirm, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.17f, 0.38f, 0.83f, 0.62f);
            var boxRt = box.rectTransform;

            // 見出し:🔒 + 資源名
            var lockI = UiKit.Icon("Lock", boxRt, UiKit.Lock);
            UiKit.Place(lockI.rectTransform, new Vector2(0, 1f), Pad, -Pad - 30, 64, 64, 0f);
            _confirmName = UiKit.Label("Name", boxRt, "", UiKit.FTitle, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_confirmName.rectTransform, new Vector2(0, 1f), Pad + 80, -Pad - 42, 480, 80, 0f);

            // 費用:金貨 + 数字
            var coin = UiKit.Icon("Coin", boxRt, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), Pad, 0, 56, 56, 0f);
            _confirmCost = UiKit.Label("Cost", boxRt, "", UiKit.FName, UiKit.Txt);
            UiKit.Place(_confirmCost.rectTransform, new Vector2(0, 0.5f), Pad + 70, 0, 480, 70, 0f);

            // ✓ / ✕
            var ok = UiKit.Button("OK", boxRt, UiKit.Green);
            UiKit.Place(ok.GetComponent<RectTransform>(), new Vector2(0, 0), Pad, Pad + 40, 0, 96, 0f);
            var okRt = ok.GetComponent<RectTransform>();
            okRt.anchorMin = new Vector2(0.06f, 0); okRt.anchorMax = new Vector2(0.48f, 0);
            okRt.pivot = new Vector2(0.5f, 0); okRt.sizeDelta = new Vector2(0, 96);
            okRt.anchoredPosition = new Vector2(0, Pad);
            var okT = UiKit.Label("okT", ok.transform, "✓", UiKit.FTitle, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(okT.rectTransform);
            ok.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(_pendingUnlockId)) _ctrl.TryUnlock(_pendingUnlockId);
                HideConfirm(); Refresh();
            });

            var cancel = UiKit.Button("Cancel", boxRt, UiKit.Panel2);
            var caRt = cancel.GetComponent<RectTransform>();
            caRt.anchorMin = new Vector2(0.52f, 0); caRt.anchorMax = new Vector2(0.94f, 0);
            caRt.pivot = new Vector2(0.5f, 0); caRt.sizeDelta = new Vector2(0, 96);
            caRt.anchoredPosition = new Vector2(0, Pad);
            var caT = UiKit.Label("caT", cancel.transform, "✕", UiKit.FTitle, UiKit.Sub, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(caT.rectTransform);
            cancel.onClick.AddListener(HideConfirm);
        }

        void ShowConfirm(string id, double cost)
        {
            _pendingUnlockId = id;
            var rp = _ctrl.Prices.ById(id);
            _confirmName.text = rp?.name_ja ?? id;
            bool afford = _ctrl.State.Nova >= cost;
            _confirmCost.text = $"{cost:#,0}";
            _confirmCost.color = afford ? UiKit.Txt : UiKit.Sub;
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
        }
        void HideConfirm() { _pendingUnlockId = null; if (_confirm != null) _confirm.gameObject.SetActive(false); }

        // ------------------------------------------------------------ リスト再構築
        void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);

            var list = SortedEntries();
            UiRoot.SetContentHeight(_content, Mathf.Max(list.Count, 1) * RowH);
            string nextId = _ctrl.NextLockedResource();
            double unlockCost = _ctrl.NextUnlockCost();

            for (int i = 0; i < list.Count; i++)
                BuildRow(list[i], i, nextId, unlockCost);
        }

        void BuildRow((string id, string name, double price, double change, bool unlocked) e, int index, string nextId, double unlockCost)
        {
            var row = UiKit.Node($"Row_{e.id}", _content);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0, -(index + 1) * RowH + 6); row.offsetMax = new Vector2(0, -index * RowH);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.White; bg.color = UiKit.Row; bg.raycastTarget = true;

            Color nameCol = e.unlocked ? UiKit.Txt : UiKit.Sub;

            float nameX = 16f;
            if (!e.unlocked)
            {
                var lockI = UiKit.Icon("Lock", row, UiKit.Lock);
                UiKit.Place(lockI.rectTransform, new Vector2(0, 0.5f), 16, 0, RowH * 0.42f, RowH * 0.42f, 0f);
                nameX = 16 + RowH * 0.42f + 8;
            }
            var name = UiKit.Label("Name", row, e.name, UiKit.FName, nameCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(name.rectTransform, new Vector2(0, 0.5f), nameX, 0, 360, RowH * 0.7f, 0f);

            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.34f, 0.5f), 0, 0, RowH * 0.40f, RowH * 0.40f, 0f);
            var priceL = UiKit.Label("Price", row, $"{e.price:#,0}", UiKit.FNum, nameCol);
            UiKit.Place(priceL.rectTransform, new Vector2(0.34f, 0.5f), RowH * 0.40f + 8, 0, 260, RowH * 0.7f, 0f);

            if (e.unlocked)
            {
                var chg = UiKit.Label("Chg", row, Market.ChangeText(e.change), UiKit.FSub, Market.ChangeColor(e.change), TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(chg.rectTransform, new Vector2(0.78f, 0.5f), 0, 0, 220, RowH * 0.7f, 0f);
            }
            else if (e.id == nextId)
            {
                var btn = UiKit.Button("Unlock", row, UiKit.Green);
                UiKit.Span(btn.GetComponent<RectTransform>(), 0.60f, 0.98f, 0, RowH * 0.62f);
                bool afford = _ctrl.State.Nova >= unlockCost;
                btn.interactable = afford;
                // 金貨 + 費用(ボタン中央)
                var bcoin = UiKit.Icon("bcoin", btn.transform, UiKit.Coin);
                UiKit.Place(bcoin.rectTransform, new Vector2(0.5f, 0.5f), -6, 0, RowH * 0.34f, RowH * 0.34f, 1f);
                var bcost = UiKit.Label("bcost", btn.transform, $"{unlockCost:#,0}", UiKit.FNum, UiKit.Dark, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(bcost.rectTransform, new Vector2(0.5f, 0.5f), 8, 0, 220, RowH * 0.6f, 0f);
                string id = e.id;
                btn.onClick.AddListener(() => ShowConfirm(id, unlockCost));
            }
        }

        List<(string id, string name, double price, double change, bool unlocked)> SortedEntries()
        {
            var list = new List<(string, string, double, double, bool)>();
            foreach (var id in _ctrl.UnlockedBodyResourceIds())
            {
                var rp = _ctrl.Prices.ById(id);
                string name = rp?.name_ja ?? id;
                list.Add((id, name, _ctrl.PriceOf(id), _ctrl.Change1d(id), _ctrl.IsResourceUnlocked(id)));
            }
            list.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            return list;
        }
    }
}
