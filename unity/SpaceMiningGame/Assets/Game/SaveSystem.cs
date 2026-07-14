// 進捗の保存/復元(PlayerPrefs + JsonUtility)。放置ゲームの前提。
// 保存内容:所持NOVA・強化レベル・資源解禁段階・スキル・在庫・派遣割り当て・開放天体・保存時刻。
// 復元時に「保存からの経過秒」を返し、呼び出し側(FleetSimulator.ApplyOffline)がオフライン進行を計算する。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable] class SaveInv { public string id; public string name; public double kg; }

    [Serializable] class SaveData
    {
        public double nova;
        public int mineLevel = 1, cargoLevel = 1;
        public bool dedicatedMiner, autoSell;
        public bool refineryUnlocked, factoryUnlocked;
        public string factorySelected;
        public List<SaveInv> inventory = new List<SaveInv>();
        public List<int> shipAssign = new List<int>();
        public List<int> unlocked = new List<int>();               // 開放天体No
        public List<string> unlockedResources = new List<string>(); // 解禁済み採掘資源id
        public long savedUnix;
    }

    public static class SaveSystem
    {
        const string Key = "spacemining_save_v1";

        public static void Save(SpaceMapController c)
        {
            var d = new SaveData
            {
                nova = c.State.Nova,
                mineLevel = c.State.MineLevel,
                cargoLevel = c.State.CargoLevel,
                dedicatedMiner = c.State.DedicatedMinerUnlocked,
                autoSell = c.State.AutoSellUnlocked,
                refineryUnlocked = c.State.RefineryUnlocked,
                factoryUnlocked = c.State.FactoryUnlocked,
                factorySelected = c.State.FactorySelected,
                savedUnix = Now(),
            };
            foreach (var e in c.Inventory.Entries)
                d.inventory.Add(new SaveInv { id = e.Id, name = e.Name, kg = e.Kg });
            foreach (var s in c.Fleet.Ships) d.shipAssign.Add(s.AssignedBodyNo);
            foreach (var b in c.Data.Bodies)
                if (!b.IsStation && b.Unlocked) d.unlocked.Add(b.No);
            d.unlockedResources.AddRange(c.State.UnlockedResources);

            PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
            PlayerPrefs.Save();
        }

        // セーブがあれば復元し、保存からの経過秒を返す(無ければ 0)。
        public static double Load(SpaceMapController c)
        {
            if (!PlayerPrefs.HasKey(Key)) return 0;
            var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
            if (d == null) return 0;

            c.State.Nova = d.nova;
            c.State.MineLevel = Mathf.Max(1, d.mineLevel);
            c.State.CargoLevel = Mathf.Max(1, d.cargoLevel);
            c.State.DedicatedMinerUnlocked = d.dedicatedMiner;
            c.State.AutoSellUnlocked = d.autoSell;
            c.State.RefineryUnlocked = d.refineryUnlocked;
            c.State.FactoryUnlocked = d.factoryUnlocked;
            c.State.FactorySelected = string.IsNullOrEmpty(d.factorySelected) ? null : d.factorySelected;
            c.State.UnlockedResources.Clear();
            if (d.unlockedResources != null)
                c.State.UnlockedResources.AddRange(d.unlockedResources);

            if (d.inventory != null)
                foreach (var it in d.inventory) c.Inventory.Add(it.id, it.name, it.kg);
            if (d.shipAssign != null)
            {
                // 保存時の隻数に合わせて増設(初期1隻から復元。強化で増やした船を戻す)
                while (c.Fleet.Ships.Count < d.shipAssign.Count && c.Fleet.AddTransport()) { }
                for (int i = 0; i < d.shipAssign.Count && i < c.Fleet.Ships.Count; i++)
                    c.Fleet.Ships[i].AssignedBodyNo = d.shipAssign[i];
            }
            if (d.unlocked != null)
                foreach (var no in d.unlocked) { var b = c.Data.ByNo(no); if (b != null) b.Unlocked = true; }

            double elapsed = Now() - d.savedUnix;
            return elapsed < 0 ? 0 : elapsed;   // 時計を戻された等は0扱い(暫定。厳密なチート対策はPhase3)
        }

        public static void Clear() { PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
