// オフライン復帰ダイアログ:留守中に N便帰還・どの資源が+何個 を提示(企画書:復帰時明細)。
// 言語非依存:経過時間・便数・鉱石をアイコン+数字で。閉じるは ✓。uGUI へ移行済み。OfflineResult を表示。
using UnityEngine;
using UnityEngine.UI;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class OfflinePanel : MonoBehaviour
    {
        SpaceMapController _ctrl;
        bool _open, _built;
        OfflineResult _res;
        RectTransform _panel, _content;
        Text _time, _trips;

        const float Pad = 48f;
        const float GainH = 84f;

        public bool IsOpen => _open;
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;

        public void Show(OfflineResult r)
        {
            EnsureBuilt();
            _res = r; _open = true;
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            Populate();
        }
        void Close() { _open = false; if (_panel != null) _panel.gameObject.SetActive(false); }
        void Start() { EnsureBuilt(); }

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            var root = UiRoot.Instance.Root;

            _panel = UiKit.Node("OfflinePanel", root);
            UiKit.Stretch(_panel);
            _panel.gameObject.SetActive(false);

            var back = UiKit.Solid("Backdrop", _panel, new Color(0, 0, 0, 0.65f), raycast: true);
            UiKit.Stretch(back.rectTransform);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.transition = Selectable.Transition.None;
            backBtn.onClick.AddListener(Close);

            var box = UiKit.Solid("Box", _panel, UiKit.Panel, raycast: true);
            UiKit.Frac(box.rectTransform, 0.07f, 0.25f, 0.93f, 0.75f);
            var boxRt = box.rectTransform;

            // 経過時間(大)
            _time = UiKit.Label("Time", boxRt, "0:00", UiKit.FTitle, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.TopBand(_time.rectTransform, Pad, 80, Pad, Pad);

            // N便(船色 + ×便数)
            var shipSq = UiKit.Solid("Ship", boxRt, UiKit.Ship, raycast: false);
            var shipRt = shipSq.rectTransform;
            shipRt.anchorMin = new Vector2(0, 1); shipRt.anchorMax = new Vector2(0, 1); shipRt.pivot = new Vector2(0, 1);
            shipRt.sizeDelta = new Vector2(56, 56); shipRt.anchoredPosition = new Vector2(Pad, -Pad - 100);
            _trips = UiKit.Label("Trips", boxRt, "×0", UiKit.FName, UiKit.Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            var trRt = _trips.rectTransform;
            trRt.anchorMin = new Vector2(0, 1); trRt.anchorMax = new Vector2(0, 1); trRt.pivot = new Vector2(0, 1);
            trRt.sizeDelta = new Vector2(400, 70); trRt.anchoredPosition = new Vector2(Pad + 70, -Pad - 93);

            var line = UiKit.Solid("Line", boxRt, UiKit.Line, raycast: false);
            UiKit.TopBand(line.rectTransform, Pad + 180, 2, Pad, Pad);

            // 資源獲得のスクロール(下端は✓ボタンぶん空ける)
            var scrollHost = UiKit.Node("ScrollHost", boxRt);
            UiKit.Stretch(scrollHost, Pad, Pad + 196, Pad, Pad + 150);
            var (_, content) = UiRoot.MakeScroll(scrollHost);
            _content = content;

            // ✓ 閉じる(下部)
            var ok = UiKit.Button("OK", boxRt, UiKit.Green);
            var okRt = ok.GetComponent<RectTransform>();
            okRt.anchorMin = new Vector2(0, 0); okRt.anchorMax = new Vector2(1, 0); okRt.pivot = new Vector2(0.5f, 0);
            okRt.offsetMin = new Vector2(Pad, Pad); okRt.offsetMax = new Vector2(-Pad, Pad + 110);
            var okT = UiKit.Label("okT", ok.transform, "✓", UiKit.FTitle, UiKit.Dark, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(okT.rectTransform);
            ok.onClick.AddListener(Close);
        }

        void Populate()
        {
            if (_res == null) return;
            _time.text = Mmss(_res.seconds);
            _trips.text = $"×{_res.trips}";
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            var gains = _res.gains;
            UiRoot.SetContentHeight(_content, Mathf.Max(gains.Count, 1) * GainH);
            for (int i = 0; i < gains.Count; i++)
            {
                var g = gains[i];
                var row = UiKit.Node($"Gain{i}", _content);
                row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1); row.pivot = new Vector2(0.5f, 1f);
                row.offsetMin = new Vector2(0, -(i + 1) * GainH); row.offsetMax = new Vector2(0, -i * GainH);

                var cube = UiKit.Icon("Cube", row, UiKit.Cube);
                UiKit.Place(cube.rectTransform, new Vector2(0, 0.5f), 0, 0, GainH * 0.6f, GainH * 0.6f, 0f);
                var name = UiKit.Label("Name", row, g.name, UiKit.FName, UiKit.Txt);
                UiKit.Place(name.rectTransform, new Vector2(0, 0.5f), GainH * 0.6f + 12, 0, 400, GainH, 0f);
                var cnt = UiKit.Label("Cnt", row, $"+{g.count:#,0}", UiKit.FName, UiKit.Green, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.Place(cnt.rectTransform, new Vector2(0.60f, 0.5f), 0, 0, 300, GainH, 0f);
            }
        }

        static string Mmss(double sec)
        {
            int t = Mathf.RoundToInt((float)sec);
            int h = t / 3600, m = (t % 3600) / 60, s = t % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
    }
}
