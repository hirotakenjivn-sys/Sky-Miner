// ホーム在庫。持ち帰った鉱石は着艦時にここへ貯まり、店(取引所)で売って初めてNOVAになる
//(2026-07-13 仕様変更。着艦即売却は廃止。自動売却は中盤スキルで解禁)。
using System.Collections.Generic;

namespace SpaceMining.Game
{
    public class InventoryEntry
    {
        public string Id;      // 資源id(resources.json)。単価はこのidで引く
        public string Name;    // 表示名
        public double Kg;      // 保有量[kg]
    }

    public class Inventory
    {
        readonly Dictionary<string, InventoryEntry> _byId = new Dictionary<string, InventoryEntry>();

        public IReadOnlyCollection<InventoryEntry> Entries => _byId.Values;
        public bool IsEmpty => _byId.Count == 0;

        public double TotalKg
        {
            get { double t = 0; foreach (var e in _byId.Values) t += e.Kg; return t; }
        }

        // 鉱石を追加。追加後の保有量を返す(着艦時の増分可視化に使う)。
        public double Add(string id, string name, double kg)
        {
            if (!_byId.TryGetValue(id, out var e))
            {
                e = new InventoryEntry { Id = id, Name = name, Kg = 0 };
                _byId[id] = e;
            }
            e.Kg += kg;
            return e.Kg;
        }

        public double KgOf(string id) => _byId.TryGetValue(id, out var e) ? e.Kg : 0;
        public void Clear() => _byId.Clear();

        // 全量を取り出す(売却)。取り出した量を返し、エントリを消す。
        public double TakeAll(string id)
        {
            if (!_byId.TryGetValue(id, out var e)) return 0;
            double kg = e.Kg;
            _byId.Remove(id);
            return kg;
        }

        // 指定量まで取り出す(部分売却)。実際に取り出せた量を返す。残0でエントリ削除。
        public double Take(string id, double kg)
        {
            if (kg <= 0 || !_byId.TryGetValue(id, out var e)) return 0;
            double take = kg < e.Kg ? kg : e.Kg;
            e.Kg -= take;
            if (e.Kg <= 1e-6) _byId.Remove(id);
            return take;
        }
    }
}
