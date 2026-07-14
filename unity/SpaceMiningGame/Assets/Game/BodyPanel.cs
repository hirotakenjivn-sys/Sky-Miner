// 天体詳細パネル / 派遣UI(下部シート)。天体を選択すると下からシート状に開き、
// 名前・種別・資源単価・派遣UI(Fleet へ割当)を出す。閉じは CloseSelection()。
//
// uGUI へ移行済み。選択の購読(OnBodySelected)・Fleet 割当・解放は従来ロジックのまま。
// マップの空タップで閉じる既存挙動はコントローラ側(Deselect)が担う。
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class BodyPanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        CelestialBody _body;
        int _pendingMiner, _pendingTransport;

        bool _built;
        RectTransform _panel, _inner, _tipBox;
        Text _tip;

        const float Pad = 40f;

        public void Bind(SpaceMapController ctrl)
        {
            _ctrl = ctrl;
            _ctrl.OnBodySelected += OnSelected;
        }
        void OnDestroy() { if (_ctrl != null) _ctrl.OnBodySelected -= OnSelected; }

        void OnSelected(CelestialBody b)
        {
            EnsureBuilt();
            _body = b;
            if (b == null) { _panel.gameObject.SetActive(false); return; }
            if (!b.IsStation)
            {
                _pendingMiner = _ctrl.Fleet.AssignedCount(b.No, ShipType.Miner);
                _pendingTransport = _ctrl.Fleet.AssignedCount(b.No, ShipType.Transport);
            }
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            Rebuild();
        }

        void Start() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            var root = UiRoot.Instance.Root;

            _panel = UiKit.Node("BodyPanel", root);
            UiKit.Frac(_panel, 0f, 0f, 1f, 0.42f);   // 下部シート
            _panel.gameObject.SetActive(false);
            var bg = _panel.gameObject.AddComponent<Image>();
            bg.sprite = UiKit.White; bg.color = UiKit.Panel; bg.raycastTarget = true;

            // 上辺のライン + グラブハンドル
            var line = UiKit.Solid("TopLine", _panel, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, 0, 3, 0, 0);
            var grab = UiKit.Solid("Grab", _panel, new Color(0.22f, 0.25f, 0.42f, 1f), raycast: false);
            var gr = grab.rectTransform;
            gr.anchorMin = new Vector2(0.5f, 1); gr.anchorMax = new Vector2(0.5f, 1); gr.pivot = new Vector2(0.5f, 1);
            gr.sizeDelta = new Vector2(90, 8); gr.anchoredPosition = new Vector2(0, -16);

            _inner = UiKit.Node("Inner", _panel);
            UiKit.Stretch(_inner, Pad, 40, Pad, 20);

            // 資源ホバー用ツールチップ(名前表示)。_panel直下=Rebuildで消えない。既定は非表示。
            var tipImg = UiKit.Solid("Tip", _panel, UiKit.Panel2, raycast: false);
            _tipBox = tipImg.rectTransform;
            _tipBox.anchorMin = _tipBox.anchorMax = new Vector2(0, 0);
            _tipBox.pivot = new Vector2(0.5f, 0f);
            _tip = UiKit.Label("TipTxt", _tipBox, "", UiKit.FSub, UiKit.Txt, TextAnchor.MiddleCenter);
            UiKit.Stretch(_tip.rectTransform, 16, 6, 16, 6);
            _tipBox.gameObject.SetActive(false);
        }

        // 資源アイコンにホバー(PointerEnter/Exit)で名前ツールチップを出す。
        void HookTip(RectTransform target, string text)
        {
            var et = target.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowTip(target, text));
            et.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => _tipBox.gameObject.SetActive(false));
            et.triggers.Add(exit);
        }

        void ShowTip(RectTransform target, string text)
        {
            _tip.text = text;
            float w = _tip.preferredWidth + 40f;
            _tipBox.sizeDelta = new Vector2(Mathf.Max(120f, w), 64f);
            // アイコンの真上に配置(_panelローカル座標)。
            Vector3 lp = _panel.InverseTransformPoint(target.TransformPoint(new Vector3(0, target.rect.height * 0.5f, 0)));
            _tipBox.anchoredPosition = new Vector2(lp.x - _panel.rect.xMin, lp.y - _panel.rect.yMin + 8f);
            _tipBox.SetAsLastSibling();
            _tipBox.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------ 再構築
        void Rebuild()
        {
            if (_inner == null || _body == null) return;
            for (int i = _inner.childCount - 1; i >= 0; i--) Destroy(_inner.GetChild(i).gameObject);

            float y = 0f;

            // 閉じる ✕(右上)
            var close = UiKit.Button("Close", _inner, UiKit.Panel2);
            var clRt = close.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(1, 1); clRt.anchorMax = new Vector2(1, 1); clRt.pivot = new Vector2(1, 1);
            clRt.sizeDelta = new Vector2(80, 80); clRt.anchoredPosition = new Vector2(0, 0);
            var cx = UiKit.Label("x", close.transform, "✕", UiKit.FName, UiKit.Sub, TextAnchor.MiddleCenter);
            UiKit.Stretch(cx.rectTransform);
            close.onClick.AddListener(() => _ctrl.CloseSelection());

            // タイトル(名前 · 種別)
            var titleCol = _body.IsMvp ? UiKit.Cyan : UiKit.Sub;
            var title = UiKit.Label("Title", _inner, $"{_body.Name}  ·  {_body.TypeLabel}", UiKit.FTitle, titleCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.TopBand(title.rectTransform, y, 80, 0, 90);
            y += 92;

            // サブ情報(帯 · 片道分′ · 距離)
            string sub = _body.IsStation ? _body.Band
                : $"{_body.Band}   ~{OneWayMin(_body)}′   {_body.DistanceKm:#,0}";
            var subL = UiKit.Label("Sub", _inner, sub, UiKit.FSub, UiKit.Sub);
            UiKit.TopBand(subL.rectTransform, y, 50, 0, 0);
            y += 64;

            // 資源単価(coin+名前+単価+前日比 を1資源1行で・言語非依存)
            y = BuildResources(y);
            y += 8;

            // 区切り
            var div = UiKit.Solid("Div", _inner, UiKit.Line, raycast: false);
            UiKit.TopBand(div.rectTransform, y, 2, 0, 0);
            y += 18;

            if (_body.IsStation)
            {
                var note = UiKit.Label("Note", _inner, "―", UiKit.FSub, UiKit.Sub, TextAnchor.UpperLeft);
                UiKit.TopBand(note.rectTransform, y, 120, 0, 0);
            }
            else if (!_body.Unlocked)
            {
                BuildUnlock(y);
            }
            else
            {
                BuildDispatch(y);
            }
        }

        float BuildResources(float y)
        {
            var matched = _body.IsStation ? null : _ctrl.Prices.MatchBodyResources(_body.Resources);
            if (matched != null && matched.Count > 0)
            {
                // 取れる資源は「アイコンだけ」で表示・基準単価の安い順。未解禁は灰色(item⑤)。
                matched.Sort((a, b) => a.nova_per_kg.CompareTo(b.nova_per_kg));
                const float sz = 72f, gap = 18f;
                var band = UiKit.Node("Resources", _inner);
                UiKit.TopBand(band, y, sz, 0, 0);
                float x = 0f;
                Color locked = new Color(0.42f, 0.44f, 0.52f, 1f);   // 未解禁=灰色ティント
                foreach (var r in matched)
                {
                    bool unlocked = _ctrl.IsResourceUnlocked(r.id);
                    var icon = UiKit.Icon($"R_{r.id}", band, UiKit.Resource(r.id),
                                          unlocked ? Color.white : locked);
                    UiKit.Place(icon.rectTransform, new Vector2(0, 0.5f), x, 0, sz, sz, 0f);
                    UiKit.HookTip(icon.gameObject, r.name_ja);   // ホバーで資源名(全画面共通)
                    x += sz + gap;
                }
                return y + sz + 8f;
            }
            // 単価の付く資源が無ければ収入レート(数字のみ)
            if (!_body.IsStation && _body.Master != null)
            {
                var band = UiKit.Node("Income", _inner);
                UiKit.TopBand(band, y, 52, 0, 0);
                var coin = UiKit.Icon("Coin", band, UiKit.Coin);
                UiKit.Place(coin.rectTransform, new Vector2(0, 0.5f), 0, 0, 40, 40, 0f);
                var v = UiKit.Label("Inc", band, $"{_body.Master.income_rate_nova_s:#,0}/s", UiKit.FSub, UiKit.Cyan);
                UiKit.Place(v.rectTransform, new Vector2(0, 0.5f), 52, 0, 400, 50, 0f);
                y += 56;
            }
            return y;
        }

        void BuildUnlock(float y)
        {
            long price = (long)(_body.UnlockPriceNova * BalanceOverride.UnlockPriceScale);
            bool afford = _ctrl.State.Nova >= price;
            var btn = UiKit.Button("Unlock", _inner, UiKit.Cyan);
            UiKit.TopBand(btn.GetComponent<RectTransform>(), y, 120, 0, 0);
            btn.interactable = afford;
            // 🔒 + 金貨 + 価格(ボタン中央)
            var lockI = UiKit.Icon("Lock", btn.transform, UiKit.Lock);
            UiKit.Place(lockI.rectTransform, new Vector2(0.5f, 0.5f), -120, 0, 56, 56, 0f);
            var coin = UiKit.Icon("Coin", btn.transform, UiKit.Coin);
            UiKit.Place(coin.rectTransform, new Vector2(0.5f, 0.5f), -50, 0, 56, 56, 0f);
            var pl = UiKit.Label("P", btn.transform, $"{price:#,0}", UiKit.FName, UiKit.Dark, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(pl.rectTransform, new Vector2(0.5f, 0.5f), 16, 0, 260, 70, 0f);
            btn.onClick.AddListener(() =>
            {
                if (_ctrl.State.Nova < price) return;
                _ctrl.State.Nova -= price;
                _body.Unlocked = true;
                _pendingMiner = 0; _pendingTransport = 0;
                Rebuild();
            });
        }

        void BuildDispatch(float y)
        {
            var f = _ctrl.Fleet;
            // 宇宙船(=Transport)。序盤はこの1種のみ。
            y = ShipRow(y, "宇宙船", ShipType.Transport, ref _pendingTransport, f);
            if (_ctrl.State.DedicatedMinerUnlocked)
            {
                y += 8;
                y = ShipRow(y, "専用採掘船", ShipType.Miner, ref _pendingMiner, f);
            }
            y += 10;

            int nowM = f.AssignedCount(_body.No, ShipType.Miner);
            int nowT = f.AssignedCount(_body.No, ShipType.Transport);

            // ペア運用(採掘船常駐+輸送船)中は現地ストレージ表示。満杯なら琥珀。
            if (nowM > 0 && nowT > 0 && _ctrl.Sim != null)
            {
                double ls = _ctrl.Sim.LocalStoreKg(_body.No);
                float cap = _ctrl.Sim.LocalStoreCap;
                bool full = ls >= cap - 0.5;
                var band = UiKit.Node("Local", _inner);
                UiKit.TopBand(band, y, 50, 0, 0);
                var cube = UiKit.Icon("Cube", band, UiKit.Cube);
                UiKit.Place(cube.rectTransform, new Vector2(0, 0.5f), 0, 0, 40, 40, 0f);
                var l = UiKit.Label("LS", band, $"{ls:#,0} / {cap:#,0}" + (full ? "  ●" : ""), UiKit.FSub, full ? UiKit.Amber : UiKit.Sub);
                UiKit.Place(l.rectTransform, new Vector2(0, 0.5f), 52, 0, 400, 50, 0f);
                y += 56;
            }
            y += 6;

            bool changed = _pendingMiner != nowM || _pendingTransport != nowT;
            int pendTotal = _pendingMiner + _pendingTransport;
            // 言語非依存: → =この編成で派遣 / × =全船引き揚げ
            string glyph = pendTotal == 0 ? "×" : "→";
            var dispatch = UiKit.Button("Dispatch", _inner, UiKit.Cyan);
            UiKit.TopBand(dispatch.GetComponent<RectTransform>(), y, 110, 0, 0);
            dispatch.interactable = changed;
            var dg = UiKit.Label("dg", dispatch.transform, glyph, UiKit.FTitle, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(dg.rectTransform);
            dispatch.onClick.AddListener(() =>
            {
                var pulled = new System.Collections.Generic.List<(int fromNo, int count)>();
                int gotM = f.SetAssignment(_body.No, ShipType.Miner, _pendingMiner, pulled);
                int gotT = f.SetAssignment(_body.No, ShipType.Transport, _pendingTransport, pulled);
                _pendingMiner = gotM; _pendingTransport = gotT;
                if (gotM + gotT == 0)
                    _ctrl.Toast($"{_body.Name} から全船を引き揚げました");
                else
                {
                    // 他天体から回した場合は「◯◯から N隻 移動」を併記(何が起きたか見える=ミス回避)
                    string moved = "";
                    foreach (var p in pulled)
                    {
                        var src = _ctrl.Data.ByNo(p.fromNo);
                        moved += $" / {(src != null ? src.Name : "他天体")}から{p.count}隻移動";
                    }
                    _ctrl.Toast($"{_body.Name} へ 宇宙船{gotT} を派遣{moved}");
                }
                Rebuild();
            });
        }

        float ShipRow(float y, string name, ShipType type, ref int pending, Fleet f)
        {
            int max = f.MaxAssignable(_body.No, type);
            var band = UiKit.Node($"Ship_{type}", _inner);
            UiKit.TopBand(band, y, 96, 0, 0);

            // 宇宙船アイコン(本番PNGがあればそれ、無ければ手続きシルエット)
            var shipIcon = UiKit.Icon("ShipIcon", band, SpriteBank.Ship(type, out _), Color.white);
            UiKit.Place(shipIcon.rectTransform, new Vector2(0, 0.5f), 0, 0, 84, 84, 0f);
            var nameL = UiKit.Label("Name", band, name, UiKit.FName, UiKit.Txt);
            UiKit.Place(nameL.rectTransform, new Vector2(0, 0.5f), 92, 0, 230, 90, 0f);

            int p = pending;   // ローカルにコピー(ラムダから ref は不可)。押下時に再構築で反映。
            var minus = UiKit.Button("Minus", band, UiKit.Panel2);
            UiKit.Place(minus.GetComponent<RectTransform>(), new Vector2(0.34f, 0.5f), 0, 0, 84, 84, 0f);
            minus.interactable = p > 0;
            var mL = UiKit.Label("m", minus.transform, "−", UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(mL.rectTransform);
            minus.onClick.AddListener(() => { SetPending(type, Mathf.Max(0, p - 1)); Rebuild(); });

            var cnt = UiKit.Label("Cnt", band, p.ToString(), UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Place(cnt.rectTransform, new Vector2(0.34f, 0.5f), 94, 0, 110, 84, 0f);

            var plus = UiKit.Button("Plus", band, UiKit.Panel2);
            UiKit.Place(plus.GetComponent<RectTransform>(), new Vector2(0.34f, 0.5f), 214, 0, 84, 84, 0f);
            plus.interactable = p < max;
            var pL = UiKit.Label("p", plus.transform, "＋", UiKit.FName, UiKit.Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(pL.rectTransform);
            plus.onClick.AddListener(() => { SetPending(type, Mathf.Min(max, p + 1)); Rebuild(); });

            // 待機 idle/total(宇宙船アイコン + 数字。単位テキストなし)
            var sq = UiKit.Icon("Sq", band, SpriteBank.Ship(type, out _), Color.white);
            UiKit.Place(sq.rectTransform, new Vector2(0.72f, 0.5f), 0, 0, 46, 46, 0f);
            var it = UiKit.Label("IT", band, $"{f.IdleCount(type)}/{f.TotalCount(type)}", UiKit.FSub, UiKit.Sub);
            UiKit.Place(it.rectTransform, new Vector2(0.72f, 0.5f), 52, 0, 220, 50, 0f);

            return y + 100;
        }

        void SetPending(ShipType type, int v)
        {
            if (type == ShipType.Miner) _pendingMiner = v; else _pendingTransport = v;
        }

        int OneWayMin(CelestialBody b)
            => b.Master != null ? Mathf.RoundToInt(b.Master.oneway_min_at_unlock) : 0;
    }
}
