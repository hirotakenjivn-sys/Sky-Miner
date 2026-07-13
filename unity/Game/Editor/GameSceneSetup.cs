// バッチモードから空の 2D シーンを作り、ビルド設定に登録する(ゲーム本体)。
// 実行: Unity ... -executeMethod SpaceMining.Game.GameSceneSetup.CreateMapScene
// これにより「プロジェクトを開いて Play」だけで宇宙マップが起動する
// (天体等は SpaceMapController の RuntimeInitializeOnLoadMethod が自動生成)。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceMining.Game
{
    public static class GameSceneSetup
    {
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = SceneDir + "/Map.unity";

        public static void CreateMapScene()
        {
            if (!AssetDatabase.IsValidFolder(SceneDir))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 640f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.backgroundColor = new Color(0.043f, 0.063f, 0.125f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            // 縦画面固定・iOS 最小バージョン(企画書:iOS16+、縦画面のスマホゲーム)
            // 横回転は許可しない(放置ゲームは縦持ち前提)。
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.iOS.targetOSVersionString = "16.0";

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameSceneSetup] created & registered {ScenePath}");
        }
    }
}
