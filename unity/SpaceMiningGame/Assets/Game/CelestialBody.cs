// 天体マスタ(BodyMaster)とレイアウトノード(MapNode)を no で結合した
// ランタイム天体モデル。派遣・採掘・開放判定の起点になる。
//
// 開放の二段ゲート:
//   1) MVP スコープゲート(IsMvp)…… 企画書14章の MVP5天体
//      (地球[拠点]・月・エロス[小惑星帯]・水星・火星)のみ Phase 1 で有効。
//      それ以外は「MVP対象外」として減光・非活性で表示する。
//   2) 進行ゲート(Unlocked)…… MVP 内でも開放クレジット購入(Phase 2)まで未開放。
//      月は unlock_price_nova == 0 のチュートリアル無償開放で初期解放。
using UnityEngine;

namespace SpaceMining.Game
{
    public enum BodyKind { Station, Master }

    public class CelestialBody
    {
        // 地球拠点の擬似番号(bodies.json に地球は無く、原点に合成するため)
        public const int StationNo = -1;

        public BodyKind Kind;
        public int No;
        public string Name;
        public string TypeLabel;     // 惑星/衛星/... または「拠点」
        public string Band;
        public Vector2 Pos;          // world_px 座標(拠点は原点)

        public BodyMaster Master;    // 拠点は null
        public MapNode Node;         // 拠点は null

        // MVP スコープ(Phase 1 で操作可能な集合)
        public bool IsMvp;
        // 進行ゲート(実際に派遣できるか)。Phase 1 では price==0 と拠点のみ true
        public bool Unlocked;

        public bool IsStation => Kind == BodyKind.Station;
        public bool IsMoon => Node != null && Node.IsMoon;
        public bool IsClusterParent => Node != null && Node.IsClusterParent;
        public bool IsOverviewNode => Node == null || Node.IsOverviewNode;

        public long UnlockPriceNova => Master?.unlock_price_nova ?? 0;
        public long DistanceKm => Master?.distance_km ?? 0;
        public string Resources => Master?.resources ?? "";

        // 地球拠点(原点・常時開放)。
        public static CelestialBody MakeStation() => new CelestialBody
        {
            Kind = BodyKind.Station,
            No = StationNo,
            Name = "地球",
            TypeLabel = "拠点",
            Band = "B0",
            Pos = Vector2.zero,
            IsMvp = true,
            Unlocked = true,
        };

        // マスタ + レイアウトノードから結合生成。
        public static CelestialBody FromMasterAndNode(BodyMaster m, MapNode n)
        {
            bool mvp = n.is_mvp;
            return new CelestialBody
            {
                Kind = BodyKind.Master,
                No = m.no,
                Name = m.name_ja,
                TypeLabel = m.type,
                Band = m.band,
                Pos = n.Pos,
                Master = m,
                Node = n,
                IsMvp = mvp,
                // 進行ゲート初期値: 無償開放(月)のみ解放。他はクレジット購入(Phase 2)待ち。
                Unlocked = m.unlock_price_nova == 0,
            };
        }
    }
}
