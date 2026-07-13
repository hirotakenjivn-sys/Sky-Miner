// 背景スターフィールド(手続き星空)。マップの最背面に敷く、描画のみのコンポーネント。
//
// 方針:
//   - カメラの子として画面に固定(パンで星は動かない=深宇宙の遠景)。
//   - orthographicSize に合わせて毎フレーム覆う大きさへリスケール(=星は画面上一定サイズ)。
//   - sortingOrder を大きく負にして全スプライト(天体・船・リング)より背面に描く。
//   - PNG(art/bg/starfield.png)があれば SpriteBank 経由でそちらを使う。
//
// カメラ・座標・入力・LOD には一切触れない(orthographicSize/position を「読む」だけ)。
// SpaceMapController とは独立に自動生成される(Play で自動的に背景が付く)。
using UnityEngine;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class Starfield : MonoBehaviour
    {
        const float CoverFactor = 1.15f;   // 画面をわずかに超えて覆う余裕
        const int SortingOrder = -1000;    // すべての前景スプライトより背面

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<Starfield>() != null) return;
            var go = new GameObject("Starfield");
            go.AddComponent<Starfield>();
        }

        Camera _cam;
        SpriteRenderer _sr;

        void EnsureBuilt()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;   // カメラ未生成なら次フレーム再試行

            if (_sr == null)
            {
                var go = new GameObject("StarfieldQuad");
                go.transform.SetParent(_cam.transform, false);
                _sr = go.AddComponent<SpriteRenderer>();
                _sr.sprite = SpriteBank.Starfield();
                _sr.sortingOrder = SortingOrder;
                // カメラ前方・近クリップより奥(=前景スプライトの後ろは sortingOrder が保証)
                go.transform.localPosition = new Vector3(0f, 0f, 5f);
                go.transform.localRotation = Quaternion.identity;
            }
        }

        // カメラのズーム(orthographicSize)に追従して常に画面を覆う大きさへ。
        void LateUpdate()
        {
            EnsureBuilt();
            if (_sr == null) return;
            float h = 2f * _cam.orthographicSize * CoverFactor;
            float w = h * _cam.aspect;
            _sr.transform.localScale = new Vector3(w, h, 1f);
        }
    }
}
