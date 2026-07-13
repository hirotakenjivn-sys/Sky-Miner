// ゲームデータの読み込みと結合。天体マスタ(bodies.json)とレイアウト
// (map_layout.json)を no で突き合わせ、ランタイム天体リストを構築する。
// 地球拠点は bodies.json に存在しないため原点に合成する。
//
// 使い方: var data = GameData.Load(); data.Bodies を描画・派遣に使う。
using System.Collections.Generic;
using UnityEngine;

namespace SpaceMining.Game
{
    public class GameData
    {
        public MapLayout Layout;                 // リング半径・world_extent 等
        public List<CelestialBody> Bodies;       // 拠点 + 50天体(no 昇順、先頭が拠点)
        readonly Dictionary<int, CelestialBody> _byNo = new Dictionary<int, CelestialBody>();

        public CelestialBody Station => _byNo.TryGetValue(CelestialBody.StationNo, out var s) ? s : null;
        public CelestialBody ByNo(int no) => _byNo.TryGetValue(no, out var b) ? b : null;

        public static GameData Load()
        {
            var layout = MapLayout.Load();
            var masters = BodyMasterTable.Load();

            // no → マスタ の索引
            var masterByNo = new Dictionary<int, BodyMaster>(masters.Length);
            foreach (var m in masters) masterByNo[m.no] = m;

            var data = new GameData { Layout = layout, Bodies = new List<CelestialBody>() };

            // 拠点を先頭に
            var station = CelestialBody.MakeStation();
            data.Bodies.Add(station);
            data._byNo[station.No] = station;

            // レイアウトノード順に結合(マスタ欠落はデータ不整合として明示エラー)
            foreach (var node in layout.nodes)
            {
                if (!masterByNo.TryGetValue(node.no, out var master))
                {
                    Debug.LogError(
                        $"[GameData] レイアウト no={node.no}({node.name}) に対応する" +
                        "天体マスタ(bodies.json)がありません。export の同期を確認してください。");
                    continue;
                }
                var body = CelestialBody.FromMasterAndNode(master, node);
                data.Bodies.Add(body);
                data._byNo[body.No] = body;
            }

            return data;
        }

        // MVP スコープ内の天体(拠点含む)
        public IEnumerable<CelestialBody> MvpBodies()
        {
            foreach (var b in Bodies)
                if (b.IsMvp) yield return b;
        }
    }
}
