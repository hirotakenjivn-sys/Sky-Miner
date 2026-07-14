// 工場(合金クラフト。MVP)。在庫の素材(鉱石/金属/中間製品)から合金を1個ずつ生産する。
//   - プレイヤーが施設パネルで製品を1つ選ぶ(State.FactorySelected)と、その製品を
//     FactoryUnitsPerSec[個/秒]で、在庫に十分な入力素材がある限り生産する。
//   - 1個生産ごとに入力を「正規化した比率」ぶん消費し、製品を在庫へ +1。不足なら停止(待つ)。
//   - 入力に中間製品(steel 等)が来る = 多段クラフト(先に steel を作ってから stainless)。
//   - 製品の売値は固定(SalePrice)。店(在庫列挙)でそのまま売れる。前日比は 0(SpaceMapController)。
//   - オフライン中も継続(不在秒×能力ぶん、入力在庫を上限に生産)。
// レシピの割合は現実の合金組成参照(refining.json 由来)。精錬所とは独立の施設。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    [DisallowMultipleComponent]
    public class Factory : MonoBehaviour
    {
        // 入力素材(id + 生の比率。合計は 1 でなくてよい。使用時に正規化)。
        public struct Ingredient { public string id; public double ratio; }

        public class Recipe
        {
            public string productId;      // 製品id(ローマ字)
            public string productName;    // 表示名(日本語)
            public double salePrice;      // 売値[NOVA/kg 相当]。固定
            public Ingredient[] inputs;   // 生の比率(入力)
            public Ingredient[] norm;     // 正規化済み(合計=1)。1個あたりの消費量
        }

        // レシピ定義(現実の合金組成参照)。入力idは resources.json のid or 精錬品id or 中間製品id。
        static readonly Recipe[] _recipes = Build(new[]
        {
            R("steel",     "鋼",        22355.0,       In("iron_refined",0.95), In("coal",0.05)),
            R("brass",     "真鍮",      115000.0,      In("copper",0.65), In("zinc",0.35)),
            R("stainless", "ステンレス", 65567.0,       In("steel",0.72), In("chromium",0.18), In("nickel_refined",0.10)),
            R("die_steel", "ダイス鋼",  170759.0,      In("steel",0.85), In("chromium",0.12), In("molybdenum",0.01), In("vanadium",0.005)),
            R("hss",       "ハイス",    755925.0,      In("steel",0.80), In("tungsten",0.06), In("molybdenum",0.05), In("chromium",0.04), In("vanadium",0.02), In("cobalt",0.05)),
            R("carbide",   "超硬合金",  6073584.0,     In("tungsten",0.85), In("cobalt",0.10), In("coal",0.05)),
        });

        static readonly Dictionary<string, Recipe> _byProduct = BuildIndex();

        static Ingredient In(string id, double ratio) => new Ingredient { id = id, ratio = ratio };
        static Recipe R(string id, string name, double price, params Ingredient[] inputs)
            => new Recipe { productId = id, productName = name, salePrice = price, inputs = inputs };

        // 生の比率を正規化(合計=1)して norm を埋める。
        static Recipe[] Build(Recipe[] recipes)
        {
            foreach (var r in recipes)
            {
                double sum = 0;
                foreach (var i in r.inputs) sum += i.ratio;
                if (sum <= 0) sum = 1;
                r.norm = new Ingredient[r.inputs.Length];
                for (int k = 0; k < r.inputs.Length; k++)
                    r.norm[k] = new Ingredient { id = r.inputs[k].id, ratio = r.inputs[k].ratio / sum };
            }
            return recipes;
        }

        static Dictionary<string, Recipe> BuildIndex()
        {
            var d = new Dictionary<string, Recipe>();
            foreach (var r in _recipes) d[r.productId] = r;
            return d;
        }

        // ── 静的照会(SpaceMapController.PriceOf / パネルが使う)
        public static IReadOnlyList<Recipe> Recipes => _recipes;
        public static bool IsProduct(string id) => id != null && _byProduct.ContainsKey(id);
        public static double SalePrice(string id) => _byProduct.TryGetValue(id, out var r) ? r.salePrice : 0;
        public static string ProductName(string id) => _byProduct.TryGetValue(id, out var r) ? r.productName : id;
        public static Recipe Get(string id) => id != null && _byProduct.TryGetValue(id, out var r) ? r : null;

        // 在庫に「あと1個」ぶんの入力素材が揃っているか(パネルの充足表示に使う)。
        public static bool CanCraftOne(Inventory inv, string productId)
        {
            var r = Get(productId);
            if (r == null) return false;
            foreach (var ing in r.norm)
                if (inv.KgOf(ing.id) < ing.ratio - 1e-9) return false;
            return true;
        }

        SpaceMapController _ctrl;
        double _accum;   // 端数個の繰り越し
        public void Bind(SpaceMapController ctrl) => _ctrl = ctrl;

        void Update()
        {
            // 未購入 or 未選択なら停止。
            if (_ctrl == null || !_ctrl.State.FactoryUnlocked) return;
            string sel = _ctrl.State.FactorySelected;
            if (string.IsNullOrEmpty(sel)) return;

            float dt = Time.deltaTime * _ctrl.State.TimeScale;
            _accum += BalanceOverride.FactoryUnitsPerSec * dt;
            int budget = (int)_accum;
            if (budget <= 0) return;
            _accum -= Craft(_ctrl.Inventory, sel, budget);
            // 入力待ちで budget を消化できない間の繰り越しが暴走しないよう軽くクランプ。
            if (_accum > 5) _accum = 5;
        }

        // オフライン復帰:不在秒×能力ぶん、選択中の製品を生産(入力在庫を上限に)。生産個数を返す。
        public int ApplyOffline(double elapsedSec)
        {
            if (_ctrl == null || !_ctrl.State.FactoryUnlocked || elapsedSec < 1) return 0;
            string sel = _ctrl.State.FactorySelected;
            if (string.IsNullOrEmpty(sel)) return 0;
            int budget = (int)(BalanceOverride.FactoryUnitsPerSec * elapsedSec);
            return budget > 0 ? Craft(_ctrl.Inventory, sel, budget) : 0;
        }

        // 選択中の製品を最大 budget 個生産。1個ごとに正規化比率ぶん入力を消費し、製品を +1。
        // 入力が足りなくなったら停止。実際に生産した個数を返す。
        static int Craft(Inventory inv, string productId, int budget)
        {
            var r = Get(productId);
            if (r == null || budget <= 0) return 0;
            int done = 0;
            while (done < budget)
            {
                bool ok = true;
                foreach (var ing in r.norm)
                    if (inv.KgOf(ing.id) < ing.ratio - 1e-9) { ok = false; break; }
                if (!ok) break;
                foreach (var ing in r.norm) inv.Take(ing.id, ing.ratio);
                inv.Add(r.productId, r.productName, 1);
                done++;
            }
            return done;
        }
    }
}
