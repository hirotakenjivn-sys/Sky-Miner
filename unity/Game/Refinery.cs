// 精錬所(MVP)。持ち帰った鉱石を金属へ変換する二層価格の実装。
//   - 対象は 鉄・ニッケル・チタン の鉱石(CLAUDE.md「精錬はMVPに含む」)。
//   - 背景で自動処理:鉱石 RefineInputPerOutput 個 → 金属 1 個 の濃縮(2026-07-14 変更)。
//     毎秒 RefineUnitsPerSec 個ぶんの鉱石を濃縮に回す。流入が上回れば在庫に滞留 →
//     「安い鉱石を今売る/待って高い金属で売る」の選択。端数の鉱石は消費せず残す。
//   - 金属の売値 = 鉱石の当日単価 × RefineInputPerOutput × RefineFactor。店でそのまま売れる。
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
        // 金属id → 表示名(精鉄/精ニッケル/精チタン)。ツールチップ用。
        public static string MetalName(string metalId)
        {
            foreach (var kv in Recipes) if (kv.Value.id == metalId) return kv.Value.name;
            return metalId;
        }
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
            // 金属1個ぶん(鉱石 RefineInputPerOutput 個)の予算が貯まるまで待つ。
            if (_accum < BalanceOverride.RefineInputPerOutput) return;
            int budgetOre = (int)_accum;
            int usedOre = Refine(_ctrl.Inventory, budgetOre);
            _accum -= usedOre;
            // 鉱石不足で1個も濃縮できなかった時は、貯めすぎず「1個ぶん構えて」在庫到着に即応。
            if (usedOre == 0) _accum = BalanceOverride.RefineInputPerOutput;
        }

        // オフライン復帰:不在秒×能力ぶん、在庫の鉱石を金属へ濃縮(在庫を上限に)。消費した鉱石数を返す。
        public int ApplyOffline(double elapsedSec)
        {
            if (_ctrl == null || !_ctrl.State.RefineryUnlocked || elapsedSec < 1) return 0;
            int budgetOre = (int)(BalanceOverride.RefineUnitsPerSec * elapsedSec);
            return budgetOre >= BalanceOverride.RefineInputPerOutput ? Refine(_ctrl.Inventory, budgetOre) : 0;
        }

        // 在庫の鉱石を最大 budgetOre 個ぶん濃縮:鉱石 N 個 → 金属 1 個(N=RefineInputPerOutput)。
        // 端数の鉱石は消費せず在庫に残す。実際に消費した鉱石数を返す。
        static int Refine(Inventory inv, int budgetOre)
        {
            int ratio = BalanceOverride.RefineInputPerOutput;
            int usedOre = 0;
            foreach (var kv in Recipes)
            {
                if (budgetOre - usedOre < ratio) break;   // 残予算で金属1個も作れない
                string ore = kv.Key;
                int have = (int)inv.KgOf(ore);
                int metals = Mathf.Min(have / ratio, (budgetOre - usedOre) / ratio);
                if (metals <= 0) continue;
                int take = metals * ratio;
                inv.Take(ore, take);
                inv.Add(kv.Value.id, kv.Value.name, metals);
                usedOre += take;
            }
            return usedOre;
        }
    }
}
