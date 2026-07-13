// 対数リング配置(付録A)のレイアウトデータモデルとローダー。
// 座標系の真実源は poc/map_layout/ring_layout.py。これはその export_layout.py が
// 書き出す data/map/map_layout.json を StreamingAssets 経由で読むだけ。
// C# 側で座標を再計算しない(数値ハードコード禁止・二重管理回避)。
//
// 千鳥配置(隣接天体の重なり回避)は ring_layout.py 側で完結しており、
// この JSON の x/y/angle_deg/ring_radius に反映済み。ゲーム側は結果を読むだけ。
using System;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable]
    public class MapParams
    {
        public float R0;
        public float K;
        public float min_dR;
        public float icon_d;
        public float sub_ring_ratio;
        public float cluster_collapse_px;
    }

    [Serializable]
    public class RingRadius
    {
        public string band;
        public float radius;
    }

    [Serializable]
    public class MapNode
    {
        public int no;
        public string name;
        public string band;
        public string type;
        public float x;
        public float y;
        public float ring_radius;
        public float angle_deg;
        public string role;          // standalone | cluster_parent | moon
        public int parent_no;        // 親なし = -1
        public int cluster_size;     // 親 = 1 + 衛星数
        public double real_distance_km;
        public bool is_mvp;

        public bool IsMoon => role == "moon";
        public bool IsClusterParent => role == "cluster_parent";
        // ズームアウト時に見えるノード(衛星は親へ集約)
        public bool IsOverviewNode => role != "moon";
        public Vector2 Pos => new Vector2(x, y);
    }

    [Serializable]
    public class MapLayout
    {
        public string schema;
        public string coordinate_space;
        public MapParams @params;
        public RingRadius[] ring_radius;
        public string[] bumped_bands;
        public float world_extent;
        public int node_count;
        public int overview_node_count;
        public MapNode[] nodes;

        // StreamingAssets/map_layout.json を読む。
        public static MapLayout Load(string fileName = "map_layout.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"レイアウトJSONが見つかりません: {path}\n" +
                    "poc/map_layout/export_layout.py を実行し、生成物を " +
                    "Assets/StreamingAssets/ にコピーしてください。");
            string json = File.ReadAllText(path);
            var layout = JsonUtility.FromJson<MapLayout>(json);
            if (layout == null || layout.nodes == null || layout.nodes.Length == 0)
                throw new InvalidDataException("レイアウトJSONの解析に失敗、または nodes が空です。");
            return layout;
        }
    }
}
