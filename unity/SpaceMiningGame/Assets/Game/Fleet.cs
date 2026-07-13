// 艦隊モデル。有限の船を天体へ割り当てる(派遣戦略の中核。企画書7章)。
//
// このタスク(天体パネル/派遣UI)では「割り当て状態」の管理までを持つ。
// 実際の飛行(飛ぶ→掘る→満杯→帰る→売る→再出発の自動ループ)は次タスクで
// この Fleet の割り当てを入力に実装する。
//
// 初期艦隊 = 採掘船2 + 輸送船1。企画書7章では初期隻数は【要検討(2〜3隻)】で、
// 推奨構成(採掘船2+輸送船1でチュートリアル成立)を既定値に採用している。
// 隻数はゲーム設計ノブ(経済数値ではない)なのでここに置くが、確定時に要見直し。
using System.Collections.Generic;
using System.Linq;

namespace SpaceMining.Game
{
    // Transport = 序盤の基本「宇宙船」(採掘ロボ搭載の輸送船。1隻で飛行→採掘→帰還)。
    // Miner = 専用採掘船(天体に常駐)。中盤スキル解禁後に登場し、輸送船とペア運用する。
    public enum ShipType { Miner, Transport }

    public class Ship
    {
        public int Id;
        public ShipType Type;
        // 割り当て先の天体 no。CelestialBody.StationNo(-1)= ステーション待機(未派遣)。
        public int AssignedBodyNo = CelestialBody.StationNo;

        public bool IsIdle => AssignedBodyNo == CelestialBody.StationNo;
    }

    public class Fleet
    {
        // 初期艦隊構成:序盤は宇宙船(=Transport型)のみ。専用採掘船(Miner)は中盤スキルで登場。
        public const int InitialMiners = 0;
        public const int InitialTransports = 3;

        public readonly ShipStats Stats;
        public readonly List<Ship> Ships = new List<Ship>();

        public Fleet(ShipStats stats,
                     int miners = InitialMiners, int transports = InitialTransports)
        {
            Stats = stats;
            int id = 0;
            for (int i = 0; i < miners; i++)
                Ships.Add(new Ship { Id = id++, Type = ShipType.Miner });
            for (int i = 0; i < transports; i++)
                Ships.Add(new Ship { Id = id++, Type = ShipType.Transport });
        }

        public int TotalCount(ShipType t) => Ships.Count(s => s.Type == t);
        public int IdleCount(ShipType t) => Ships.Count(s => s.Type == t && s.IsIdle);
        public int AssignedCount(int bodyNo, ShipType t)
            => Ships.Count(s => s.Type == t && s.AssignedBodyNo == bodyNo);

        // ある天体への割り当て隻数を desired に合わせる(idle から補充 / 余剰を idle へ戻す)。
        // 返り値 = 実際に到達した割り当て隻数(idle 不足で desired に届かない場合あり)。
        public int SetAssignment(int bodyNo, ShipType t, int desired)
        {
            desired = desired < 0 ? 0 : desired;
            var here = Ships.Where(s => s.Type == t && s.AssignedBodyNo == bodyNo).ToList();
            if (desired < here.Count)
            {
                // 余剰を待機へ戻す
                for (int i = 0; i < here.Count - desired; i++)
                    here[i].AssignedBodyNo = CelestialBody.StationNo;
            }
            else if (desired > here.Count)
            {
                // 待機船から補充(足りなければあるだけ)
                var idle = Ships.Where(s => s.Type == t && s.IsIdle).ToList();
                int add = System.Math.Min(desired - here.Count, idle.Count);
                for (int i = 0; i < add; i++)
                    idle[i].AssignedBodyNo = bodyNo;
            }
            return AssignedCount(bodyNo, t);
        }

        // この天体へ desired を割り当てる上限(現在ここに居る数 + 待機数)
        public int MaxAssignable(int bodyNo, ShipType t)
            => AssignedCount(bodyNo, t) + IdleCount(t);
    }
}
