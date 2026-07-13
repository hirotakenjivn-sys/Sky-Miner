// 船の基準ステータス(xlsx「船・輸送設計」→ data/balance/ships.json)。
// Lv1 基準値。強化(Phase 2)はこの基準に係数を掛ける。数値はJSONを読むだけ。
using System;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable]
    public class ShipStats
    {
        public float miner_mining_rate_kg_s;      // 採掘船:採掘レート基準[kg/s]
        public float miner_capacity_kg;           // 採掘船:積載量[kg]
        public float transport_capacity_kg;       // 輸送船:積載量[kg]
        public float load_unload_sec;             // 積込+荷降ろし[秒/往復]
        public float build_cost_v_frontier_sec;   // 建造コスト = v_frontier × この秒数

        public static ShipStats Load(string fileName = "ships.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"船ステータスJSONが見つかりません: {path}\n" +
                    "tools/export_balance.py を実行し、data/balance/ships.json を " +
                    "Assets/StreamingAssets/ にコピーしてください。");
            var s = JsonUtility.FromJson<ShipStats>(File.ReadAllText(path));
            if (s == null || s.miner_capacity_kg <= 0)
                throw new InvalidDataException("船ステータスJSONの解析に失敗しました。");
            return s;
        }
    }
}
