// 強化パネル。NOVAで採掘速度(シアンバー=MineLevel)・積載(鉱石キューブ=CargoLevel)をアップグレード。
// 各行 Lv・×効果(UpgradeCurve.EffectMult)・金貨コスト(CostToNext×UpgradeCostScale)・▲購入ボタン
//(NOVA不足時は無効)。uGUI へ移行済み。ロジック(State/UpgradeCurve)は従来通り。
using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class UpgradePanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        bool _open, _built;
        RectTransform _panel, _tracks;
        Text _balance;

        const float Pad = 40f, HeaderH = 96f, RowH = 220f;

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

            _panel = UiKit.Node("UpgradePanel", root);
            UiKit.Stretch(_panel);
            _panel.gameObject.SetActive(false);

            var back = UiKit.Solid("Backdrop", _panel, UiKit.Backdrop, raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Close);

            var box = UiKit.Solid("Box", _panel, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.04f, 0.30f, 0.96f, 0.70f);
            var boxRt = box.rectTransform;

            var header = UiKit.Node("Header", boxRt);
            UiKit.TopBand(header, Pad, HeaderH, Pad, Pad);
            var coin = UiKit.Icon("Coin", header, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), 0, 0, HeaderH * 0.86f, HeaderH * 0.86f, 0f);
            _balance = UiKit.Label("Balance", header, "0", UiKit.FTitle, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_balance.rectTransform, new Vector2(0, 0.5f), HeaderH, 0, 640, HeaderH, 0f);
            var close = UiKit.Button("Close", header, UiKit.Panel2);
            UiKit.Place(close.GetComponent<RectTransform>(), new Vector2(1, 0.5f), 0, 0, HeaderH, HeaderH, 1f);
            var cx = UiKit.Label("x", close.transform, "✕", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
            UiKit.Stretch(cx.rectTransform);
            close.onClick.AddListener(Close);

            var line = UiKit.Solid("Line", boxRt, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, Pad + HeaderH + 10, 2, Pad, Pad);

            _tracks = UiKit.Node("Tracks", boxRt);
            UiKit.Stretch(_tracks, Pad, Pad + HeaderH + 24, Pad, Pad);
        }

        void Refresh()
        {
            if (_tracks == null) return;
            for (int i = _tracks.childCount - 1; i >= 0; i--) Destroy(_tracks.GetChild(i).gameObject);
            // 採掘効率(1便の個数×)/ 宇宙船+1(艦隊増設)。採掘速度・積載は廃止(簡素化)。
            BuildTrack(0, UiKit.Cube, _ctrl.State.CargoLevel, lv => _ctrl.State.CargoLevel = lv);
            BuildShipTrack(1);
        }

        // 宇宙船の増設トラック。Lv=現在の隻数、効果=×隻数、コスト=曲線、上限で MAX。
        void BuildShipTrack(int index)
        {
            var curve = _ctrl.UpgradeCurve;
            int count = _ctrl.Fleet.TransportCount;
            var row = UiKit.Node($"Track{index}", _tracks);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
            float gap = 16f;
            row.offsetMin = new Vector2(0, -(index + 1) * (RowH + gap) + gap); row.offsetMax = new Vector2(0, -index * (RowH + gap));
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.White; bg.color = UiKit.Row; bg.raycastTarget = true;

            var ic = UiKit.Icon("Icon", row, SpriteBank.Ship(ShipType.Transport, out _), UiKit.Ship);
            UiKit.Place(ic.rectTransform, new Vector2(0, 0.5f), 20, 0, RowH * 0.42f, RowH * 0.42f, 0f);

            var lvL = UiKit.Label("Cnt", row, $"×{count}", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(lvL.rectTransform, new Vector2(0.24f, 0.5f), 0, 0, 220, RowH * 0.6f, 0f);

            bool max = count >= Fleet.MaxTransports;
            double cost = max ? 0 : curve.CostToNext(count).GetValueOrDefault()
                                    * BalanceOverride.UpgradeCostScale * BalanceOverride.ShipCostMult;

            if (max)
            {
                var maxL = UiKit.Label("Max", row, "MAX", UiKit.FNum, UiKit.Sub, TextAnchor.MiddleCenter);
                UiKit.Span(maxL.rectTransform, 0.74f, 0.98f, 0, RowH * 0.6f);
                return;
            }

            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.60f, 0.5f), 0, 0, RowH * 0.34f, RowH * 0.34f, 0f);
            var costL = UiKit.Label("Cost", row, $"{cost:#,0}", UiKit.FNum, UiKit.Sub);
            UiKit.Place(costL.rectTransform, new Vector2(0.60f, 0.5f), RowH * 0.34f + 8, 0, 240, RowH * 0.6f, 0f);

            var btn = UiKit.Button("Buy", row, UiKit.Green);
            UiKit.Span(btn.GetComponent<RectTransform>(), 0.80f, 0.98f, 0, RowH * 0.55f);
            btn.interactable = _ctrl.State.Nova >= cost;
            var up = UiKit.Label("up", btn.transform, "＋", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(up.rectTransform);
            double c = cost;
            btn.onClick.AddListener(() =>
            {
                if (_ctrl.State.Nova < c) return;
                if (!_ctrl.Fleet.AddTransport()) return;
                _ctrl.State.Nova -= c;
                Refresh();
            });
        }

        void BuildTrack(int index, Sprite icon, int level, Action<int> setLevel)
        {
            var curve = _ctrl.UpgradeCurve;
            var row = UiKit.Node($"Track{index}", _tracks);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
            float gap = 16f;
            row.offsetMin = new Vector2(0, -(index + 1) * (RowH + gap) + gap); row.offsetMax = new Vector2(0, -index * (RowH + gap));
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.White; bg.color = UiKit.Row; bg.raycastTarget = true;

            var ic = UiKit.Icon("Icon", row, icon);
            UiKit.Place(ic.rectTransform, new Vector2(0, 0.5f), 20, 0, RowH * 0.42f, RowH * 0.42f, 0f);

            var lvL = UiKit.Label("Lv", row, $"Lv {level}", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(lvL.rectTransform, new Vector2(0.24f, 0.5f), 0, 0, 220, RowH * 0.6f, 0f);

            var effL = UiKit.Label("Eff", row, $"×{curve.EffectMult(level):0.00}", UiKit.FNum, UiKit.Sub);
            UiKit.Place(effL.rectTransform, new Vector2(0.44f, 0.5f), 0, 0, 220, RowH * 0.6f, 0f);

            double? cost = curve.CostToNext(level);
            if (cost != null) cost = cost.Value * BalanceOverride.UpgradeCostScale;

            if (cost == null)
            {
                var maxL = UiKit.Label("Max", row, "MAX", UiKit.FNum, UiKit.Sub, TextAnchor.MiddleCenter);
                UiKit.Span(maxL.rectTransform, 0.74f, 0.98f, 0, RowH * 0.6f);
                return;
            }

            // コスト(金貨+数字)
            var coin = UiKit.Icon("Coin", row, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.60f, 0.5f), 0, 0, RowH * 0.34f, RowH * 0.34f, 0f);
            var costL = UiKit.Label("Cost", row, $"{cost.Value:#,0}", UiKit.FNum, UiKit.Sub);
            UiKit.Place(costL.rectTransform, new Vector2(0.60f, 0.5f), RowH * 0.34f + 8, 0, 240, RowH * 0.6f, 0f);

            // ▲購入
            var btn = UiKit.Button("Buy", row, UiKit.Green);
            UiKit.Span(btn.GetComponent<RectTransform>(), 0.80f, 0.98f, 0, RowH * 0.55f);
            btn.interactable = _ctrl.State.Nova >= cost.Value;
            var up = UiKit.Label("up", btn.transform, "▲", UiKit.FName, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(up.rectTransform);
            double c = cost.Value;
            btn.onClick.AddListener(() =>
            {
                if (_ctrl.State.Nova < c) return;
                _ctrl.State.Nova -= c;
                setLevel(level + 1);
                Refresh();
            });
        }

        void Update()
        {
            if (!_open || _balance == null) return;
            _balance.text = $"{_ctrl.State.Nova:#,0}";
        }
    }
}
