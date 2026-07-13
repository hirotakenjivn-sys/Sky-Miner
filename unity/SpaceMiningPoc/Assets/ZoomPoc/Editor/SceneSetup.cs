// バッチモードから空の 2D シーンを作り、ビルド設定に登録する。
// 実行: Unity ... -executeMethod SpaceMining.ZoomPoc.SceneSetup.CreateMapScene
// これにより「プロジェクトを開いて Play」だけで PoC が起動する
// (ノード等は ZoomPocBootstrap の RuntimeInitializeOnLoadMethod が自動生成)。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceMining.ZoomPoc
{
    public static class SceneSetup
    {
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = SceneDir + "/Map.unity";

        public static void CreateMapScene()
        {
            if (!AssetDatabase.IsValidFolder(SceneDir))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // オーソグラフィックのメインカメラ(Bootstrap 側でも生成するが、
            // シーンに1つ置いておけば Play 前のシーンビューでも中心が分かる)
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 640f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor = new Color(0.043f, 0.063f, 0.125f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            EditorSceneManager.SaveScene(scene, ScenePath);

            // ビルド設定へ登録(先頭 = 起動シーン)
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            // 縦画面・iOS 最小バージョン(実機60fps計測の前提)
            PlayerSettings.defaultInterfaceOrientation =
                UIOrientation.Portrait;
            PlayerSettings.iOS.targetOSVersionString = "16.0";

            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneSetup] created & registered {ScenePath}");
        }
    }
}
