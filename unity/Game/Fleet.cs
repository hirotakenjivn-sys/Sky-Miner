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
        // 初期艦隊構成:序盤は宇宙船(=Transport型)1隻のみ。強化で増設する(上限 MaxTransports)。
        // 専用採掘船(Miner)は中盤スキルで登場。
        public const int InitialMiners = 0;
        public const int InitialTransports = 1;
        public const int MaxTransports = 6;   // 宇宙船の増設上限(設計ノブ)

        public readonly ShipStats Stats;
        public readonly List<Ship> Ships = new List<Ship>();
        int _nextId;

        public Fleet(ShipStats stats,
                     int miners = InitialMiners, int transports = InitialTransports)
        {
            Stats = stats;
            for (int i = 0; i < miners; i++)
                Ships.Add(new Ship { Id = _nextId++, Type = ShipType.Miner });
            for (int i = 0; i < transports; i++)
                Ships.Add(new Ship { Id = _nextId++, Type = ShipType.Transport });
        }

        public int TransportCount => Ships.Count(s => s.Type == ShipType.Transport);

        // 宇宙船を1隻増設(待機状態で追加)。上限なら false。
        public bool AddTransport()
        {
            if (TransportCount >= MaxTransports) return false;
            Ships.Add(new Ship { Id = _nextId++, Type = ShipType.Transport });
            return true;
        }

        // 初期構成へ戻す(⟲リセット用):増設した宇宙船を除去し、全船を待機へ。
        public void ResetToInitial()
        {
            while (TransportCount > InitialTransports)
            {
                int idx = Ships.FindLastIndex(s => s.Type == ShipType.Transport);
                if (idx < 0) break;
                Ships.RemoveAt(idx);
            }
            foreach (var s in Ships) s.AssignedBodyNo = CelestialBody.StationNo;
        }

        public int TotalCount(ShipType t) => Ships.Count(s => s.Type == t);
        public int IdleCount(ShipType t) => Ships.Count(s => s.Type == t && s.IsIdle);
        public int AssignedCount(int bodyNo, ShipType t)
            => Ships.Count(s => s.Type == t && s.AssignedBodyNo == bodyNo);

        // ある天体への割り当て隻数を desired に合わせる。補充は 待機 → 他天体(隻数の多い順)の順。
        // 他天体から回した場合は pulled に「(移動元天体no, 隻数)」を積む(トースト表示用・null可)。
        // 返り値 = 実際に到達した割り当て隻数。
        public int SetAssignment(int bodyNo, ShipType t, int desired,
                                 List<(int fromNo, int count)> pulled = null)
        {
            desired = desired < 0 ? 0 : (desired > TotalCount(t) ? TotalCount(t) : desired);
            var here = Ships.Where(s => s.Type == t && s.AssignedBodyNo == bodyNo).ToList();
            if (desired < here.Count)
            {
                for (int i = 0; i < here.Count - desired; i++)
                    here[i].AssignedBodyNo = CelestialBody.StationNo;   // 余剰は待機へ
                return AssignedCount(bodyNo, t);
            }
            int need = desired - here.Count;
            // 1) 待機船から
            foreach (var s in Ships.Where(s => s.Type == t && s.IsIdle).ToList())
            {
                if (need <= 0) break;
                s.AssignedBodyNo = bodyNo; need--;
            }
            // 2) 足りなければ他天体から(隻数の多い順に1隻ずつ回す)
            while (need > 0)
            {
                var top = Ships.Where(s => s.Type == t && s.AssignedBodyNo != bodyNo
                                          && s.AssignedBodyNo != CelestialBody.StationNo)
                    .GroupBy(s => s.AssignedBodyNo)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                if (top == null) break;   // これ以上回せる船がない
                int fromNo = top.Key;
                top.First().AssignedBodyNo = bodyNo; need--;
                if (pulled != null)
                {
                    int idx = pulled.FindIndex(p => p.fromNo == fromNo);
                    if (idx >= 0) pulled[idx] = (fromNo, pulled[idx].count + 1);
                    else pulled.Add((fromNo, 1));
                }
            }
            return AssignedCount(bodyNo, t);
        }

        // この天体へ割り当て可能な上限 = 艦隊の総数(他天体からも回せるため)。
        public int MaxAssignable(int bodyNo, ShipType t) => TotalCount(t);
    }
}
