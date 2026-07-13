// 実行時データ経路のスモークテスト(バッチモードから起動)。
// 実行: Unity ... -executeMethod SpaceMining.Game.GameSmokeTest.RunDataLoad
//
// コンパイルが通っても実行時は別問題:
//   - JsonUtility のベア配列ラップ({"items":[...]})が正しく効くか
//   - StreamingAssets からの実ファイル読込が通るか
//   - no 結合・地球拠点合成・MVP/開放ゲートが期待どおりか
// を Play を開く前に確認する。異常時は exit code 1。
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceMining.Game
{
    public static class GameSmokeTest
    {
        public static void RunDataLoad()
        {
            int fail = 0;
            void Check(bool ok, string msg)
            {
                Debug.Log($"[SmokeTest] {(ok ? "OK  " : "FAIL")} {msg}");
                if (!ok) fail++;
            }

            GameData data;
            try
            {
                data = GameData.Load();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SmokeTest] GameData.Load 例外: {e}");
                EditorApplication.Exit(1);
                return;
            }

            // 拠点 + 50天体 = 51
            Check(data.Bodies.Count == 51, $"天体総数 = {data.Bodies.Count}(期待 51)");

            // 先頭が地球拠点・原点
            var st = data.Station;
            Check(st != null && st.IsStation, "地球拠点が存在");
            Check(st != null && st.Pos == Vector2.zero, "拠点は原点");
            Check(st != null && st.Unlocked, "拠点は開放済");

            // MVP5 = 地球・月・エロス・水星・火星
            var mvp = data.MvpBodies().Select(b => b.Name).OrderBy(x => x).ToArray();
            var mvpJoined = string.Join("・", data.MvpBodies().Select(b => b.Name));
            Check(mvp.Length == 5, $"MVP天体数 = {mvp.Length}(期待 5): {mvpJoined}");
            foreach (var name in new[] { "地球", "月", "エロス", "水星", "火星" })
                Check(mvp.Contains(name), $"MVPに {name} を含む");

            // 初期解放 = 地球・月 のみ(price==0)
            var unlocked = data.Bodies.Where(b => b.Unlocked).Select(b => b.Name).OrderBy(x => x).ToArray();
            Check(unlocked.Length == 2, $"初期解放数 = {unlocked.Length}(期待 2): {string.Join("・", unlocked)}");

            // 結合サンプル: 月(no=0)がマスタとノード両方を持つ
            var moon = data.ByNo(0);
            Check(moon != null && moon.Master != null && moon.Node != null, "月(no=0)がマスタ⋈ノード結合済");
            if (moon != null)
                Debug.Log($"[SmokeTest] 月: 距離{moon.DistanceKm:#,0}km 資源[{moon.Resources}] pos({moon.Pos.x:0},{moon.Pos.y:0})");

            // 巨大数値が型で欠損していないか(外縁天体の距離・開放額)
            var far = data.Bodies.Where(b => b.Master != null).OrderByDescending(b => b.DistanceKm).First();
            Check(far.DistanceKm > 2_000_000_000L, $"最遠 {far.Name} 距離 {far.DistanceKm:#,0}km(int32超が保持)");
            var pricey = data.Bodies.Where(b => b.Master != null).OrderByDescending(b => b.UnlockPriceNova).First();
            Check(pricey.UnlockPriceNova > 2_000_000_000L, $"最高額 {pricey.Name} 開放 {pricey.UnlockPriceNova:#,0}NOVA(long保持)");

            Debug.Log($"[SmokeTest] === 結果: {(fail == 0 ? "全PASS" : $"{fail}件FAIL")} ===");
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
