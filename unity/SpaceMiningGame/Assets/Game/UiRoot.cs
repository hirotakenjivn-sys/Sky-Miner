// uGUI のルート:Canvas(Screen Space - Overlay)+ CanvasScaler + GraphicRaycaster + EventSystem を
// コードで一度だけ生成する。全パネル/HUD はこの Canvas 配下に階層を構築する。
//
// CanvasScaler 設定(要件):Scale With Screen Size / Reference 1170×2532 / Match Width Or Height=0.5。
// これで全 iPhone サイズに自動スケールし、アスペクト比ズレ・文字大小の不整合が消える。
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class UiRoot : MonoBehaviour
    {
        static UiRoot _inst;
        public static UiRoot Instance
        {
            get
            {
                if (_inst == null)
                {
                    var found = FindAnyObjectByType<UiRoot>();
                    _inst = found != null ? found : new GameObject("UiRoot").AddComponent<UiRoot>();
                }
                return _inst;
            }
        }

        public Canvas Canvas { get; private set; }
        public RectTransform Root => (RectTransform)Canvas.transform;

        void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(gameObject); return; }
            _inst = this;
            Build();
        }

        void Build()
        {
            if (Canvas != null) return;
            Canvas = gameObject.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;   // マップ(ワールド空間)より前面

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiKit.RefW, UiKit.RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();   // 旧Input(本プロジェクトは入力システム未導入)でタッチも拾う
        }

        // ============================================================ 縦スクロールビュー生成
        // 親内をいっぱいに埋める ScrollRect を作り、行を積む content(上詰め)を返す。
        // 使い方: var (sr, content) = UiRoot.MakeScroll(parent); …content 配下に行を足す→SetContentHeight。
        public static (ScrollRect sr, RectTransform content) MakeScroll(RectTransform parent)
        {
            var srRt = UiKit.Node("Scroll", parent);
            UiKit.Stretch(srRt);
            var sr = srRt.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            // ビューポート(マスク)
            var vpRt = UiKit.Node("Viewport", srRt);
            UiKit.Stretch(vpRt);
            vpRt.pivot = new Vector2(0, 1);
            var vpImg = vpRt.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0.003f);   // ほぼ透明。Mask/raycast の下地
            vpRt.gameObject.AddComponent<RectMask2D>();

            // コンテンツ(上詰め・横いっぱい・高さは可変)
            var content = UiKit.Node("Content", vpRt);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0, 0); content.offsetMax = new Vector2(0, 0);
            content.sizeDelta = new Vector2(0, 0);

            sr.viewport = vpRt;
            sr.content = content;
            return (sr, content);
        }

        // content の高さを設定(上詰め維持)。
        public static void SetContentHeight(RectTransform content, float h)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, h);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
        }

        // ============================================================ 共有ツールチップ(鉱物/素材名)
        // 全パネル共通。UiKit.HookTip(icon, name) からアイコンにホバー(PointerEnter/Exit)で表示。
        RectTransform _tipBox; Text _tipText; bool _tipOn;

        void EnsureTip()
        {
            if (_tipBox != null) return;
            var box = UiKit.Solid("Tooltip", Root, UiKit.Panel2, raycast: false);
            _tipBox = box.rectTransform;
            _tipBox.anchorMin = _tipBox.anchorMax = new Vector2(0, 0);
            _tipBox.pivot = new Vector2(0.5f, 0f);
            _tipText = UiKit.Label("T", _tipBox, "", UiKit.FSub, UiKit.Txt, TextAnchor.MiddleCenter);
            UiKit.Stretch(_tipText.rectTransform, 22, 8, 22, 8);
            box.gameObject.SetActive(false);
        }

        public void ShowTip(string text)
        {
            EnsureTip();
            _tipText.text = text;
            _tipBox.sizeDelta = new Vector2(Mathf.Max(140f, _tipText.preferredWidth + 48f), 76f);
            _tipBox.gameObject.SetActive(true);
            _tipBox.SetAsLastSibling();
            _tipOn = true;
            PositionTip();
        }

        public void HideTip()
        {
            if (_tipBox != null) _tipBox.gameObject.SetActive(false);
            _tipOn = false;
        }

        void Update() { if (_tipOn) PositionTip(); }   // カーソル追従

        void PositionTip()
        {
            if (_tipBox == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Root, Input.mousePosition, null, out var lp);   // Overlay なので camera=null
            // Root のローカル(pivot中心)→ 左下アンカー座標へ。カーソルの少し上に。
            _tipBox.anchoredPosition = new Vector2(lp.x - Root.rect.xMin, lp.y - Root.rect.yMin + 44f);
        }
    }
}
