// 天体マスタ(付録A / xlsx「天体マスタ」シート)のデータモデルとローダー。
// 真実源は docs/credit_economy_model.xlsx → tools/export_balance.py が書き出す
// data/balance/bodies.json。C# 側で数値をハードコードせず、この JSON を読むだけ。
//
// bodies.json は先頭がベア配列 [ {...}, {...} ] のため、Unity の JsonUtility では
// そのままトップレベル配列をパースできない。テキストを {"items":[...]} で包んでから
// FromJson する定石で回避する(BodyMasterTable.Load 参照)。
using System;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    // bodies.json の 1 行。フィールド名は JSON のキーと厳密一致させる
    // (JsonUtility は名前一致でマッピングするため)。数値レンジは実データに合わせて型を選定:
    //   distance_km      … 最大 1.3e10(int32 上限超え)→ long
    //   unlock_price_nova … 最大 5e14 → long
    //   income_rate_nova_s… 小数あり最大 2.2e9 → double
    [Serializable]
    public class BodyMaster
    {
        public int no;                     // 天体番号(レイアウトとの結合キー)
        public string band;                // リング帯 B1..B10
        public string name_ja;             // 和名(表示名)
        public string type;                // 惑星/衛星/小惑星/準惑星/彗星/外縁天体
        public long distance_km;           // 地球からの実距離[km]
        public int target_unlock_day;      // 進行シミュ上の想定開放日
        public double income_rate_nova_s;  // 収入資源の単位時間採掘レート[NOVA/s]
        public int delta_t_day;            // オフライン採掘バジェット関連の想定日数差
        public long unlock_price_nova;     // 開放クレジット[NOVA](0 = チュートリアル無償)
        public int oneway_min_at_unlock;   // 開放時点の片道航行分[min]
        public string resources;           // 産出資源(和名・中黒区切りの説明文)
        public string note;                // 備考

        public string Name => name_ja;
    }

    // JsonUtility の配列ラップ用ラッパー
    [Serializable]
    class BodyMasterWrapper
    {
        public BodyMaster[] items;
    }

    public static class BodyMasterTable
    {
        // StreamingAssets/bodies.json を読み、天体マスタ配列を返す。
        // iOS では StreamingAssets はアプリバンドル内でファイルパス直読み可能。
        public static BodyMaster[] Load(string fileName = "bodies.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"天体マスタJSONが見つかりません: {path}\n" +
                    "tools/export_balance.py を実行し、data/balance/bodies.json を " +
                    "Assets/StreamingAssets/ にコピーしてください。");

            string json = File.ReadAllText(path).Trim();
            // ベア配列を {"items":[...]} で包む(JsonUtility の制約回避)
            string wrapped = "{\"items\":" + json + "}";
            var table = JsonUtility.FromJson<BodyMasterWrapper>(wrapped);
            if (table == null || table.items == null || table.items.Length == 0)
                throw new InvalidDataException("天体マスタJSONの解析に失敗、または空です。");
            return table.items;
        }
    }
}
