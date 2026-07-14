// 精錬所(MVP)。持ち帰った鉱石を金属へ変換する二層価格の実装。
//   - 対象は 鉄・ニッケル・チタン の鉱石(CLAUDE.md「精錬はMVPに含む」)。
//   - 背景で自動処理:毎フレーム RefineUnitsPerSec×dt 個ぶん、在庫の鉱石を 1:1 で金属へ。
//     処理能力を超える鉱石は在庫に滞留 → 「安い鉱石を今売る/待って高い金属で売る」の選択。
//   - 金属の売値 = 鉱石の当日単価 × RefineFactor(回収率÷品位=2.0)。店でそのまま売れる。
//   - オフライン中も継続(不在秒×能力ぶん、在庫の鉱石を上限に変換)。
// 収入は在庫→店で手動売却のまま([[sell-model-change]])。精錬は「在庫の価値を上げる」だけ。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class Refinery : MonoBehaviour
    {
        // 鉱石id → (金属id, 金属表示名)。金属idは "<ore>_refined"。
        static readonly Dictionary<string, (string id, string name)> Recipes
            = new Dictionary<string, (string, string)>
        {
            { "iron_ore",  ("iron_refined",     "精鉄") },
            { "nickel",    ("nickel_refined",   "精ニッケル") },
            { "titanium",  ("titanium_refined", "精チタン") },
        };
        // 金属id → 元の鉱石id(売値=鉱石単価×RefineFactor を引くため)。
        static readonly Dictionary<string, string> OreByMetal = BuildReverse();
        static Dictionary<string, string> BuildReverse()
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in Recipes) d[kv.Value.id] = kv.Key;
            return d;
        }

        public static bool IsRefinedId(string id) => id != null && OreByMetal.ContainsKey(id);
        public static string OreOf(string refinedId) => OreByMetal.TryGetValue(refinedId, out var o) ? o : null;
        public static bool IsRefinable(string oreId) => oreId != null && Recipes.ContainsKey(oreId);

        SpaceMapController _ctrl;
        double _accum;   // 端数個の繰り越し
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;

        void Update()
        {
            // 未購入(RefineryUnlocked=false)なら稼働しない → 鉱石は鉱石のまま在庫に残る。
            if (_ctrl == null || !_ctrl.State.RefineryUnlocked) return;
            float dt = Time.deltaTime * _ctrl.State.TimeScale;
            _accum += BalanceOverride.RefineUnitsPerSec * dt;
            int budget = (int)_accum;
            if (budget <= 0) return;
            _accum -= Refine(_ctrl.Inventory, budget);
        }

        // オフライン復帰:不在秒×能力ぶん、在庫の鉱石を金属へ(在庫を上限に)。
        public int ApplyOffline(double elapsedSec)
        {
            if (_ctrl == null || !_ctrl.State.RefineryUnlocked || elapsedSec < 1) return 0;
            int budget = (int)(BalanceOverride.RefineUnitsPerSec * elapsedSec);
            return budget > 0 ? Refine(_ctrl.Inventory, budget) : 0;
        }

        // 在庫の鉱石を最大 budget 個、金属へ 1:1 変換。実際に精錬した個数を返す。
        static int Refine(Inventory inv, int budget)
        {
            int done = 0;
            foreach (var kv in Recipes)
            {
                if (done >= budget) break;
                string ore = kv.Key;
                int have = (int)inv.KgOf(ore);
                int take = Mathf.Min(budget - done, have);
                if (take <= 0) continue;
                inv.Take(ore, take);
                inv.Add(kv.Value.id, kv.Value.name, take);
                done += take;
            }
            return done;
        }
    }
}
