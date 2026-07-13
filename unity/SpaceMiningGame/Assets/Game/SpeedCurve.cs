// 統一速度カーブ(xlsx「速度カーブ」→ data/balance/speed_curve.json)。
// 企画書:移動時間 = 実距離 ÷ 統一速度S(全船統一・線形式)。S は航行研究で成長する。
//
// この段階(研究システム未実装)の現在Sの決め方:
//   到達済み(Unlocked)天体の最上位バンドの required_speed_km_min を現在Sとする。
//   → 初期は月(B1)のみ開放なので S = B1 の 380,000 km/min(=月 片道1分)。
//   将来、航行研究Lvで S を直接持つように差し替え可能(ここが単一の算出点)。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable]
    public class SpeedBand
    {
        public string band;
        public double rep_distance_km;
        public double target_oneway_min;
        public double required_speed_km_min;   // このバンドがフロンティアの時の統一速度S
        public double ratio_vs_prev;
        public double arrival_day;
    }

    [Serializable]
    class SpeedBandWrapper { public SpeedBand[] items; }

    public class SpeedCurve
    {
        readonly List<SpeedBand> _bands = new List<SpeedBand>();
        readonly Dictionary<int, double> _speedByBandIndex = new Dictionary<int, double>();

        public static SpeedCurve Load(string fileName = "speed_curve.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"速度カーブJSONが見つかりません: {path}");
            string json = File.ReadAllText(path).Trim();
            var wrap = JsonUtility.FromJson<SpeedBandWrapper>("{\"items\":" + json + "}");
            if (wrap == null || wrap.items == null || wrap.items.Length == 0)
                throw new InvalidDataException("速度カーブJSONの解析に失敗しました。");

            var sc = new SpeedCurve();
            foreach (var b in wrap.items)
            {
                sc._bands.Add(b);
                int idx = BandIndex(b.band);
                if (idx > 0) sc._speedByBandIndex[idx] = b.required_speed_km_min;
            }
            return sc;
        }

        // "B1".."B10" → 1..10。範囲外/不正は 0。
        public static int BandIndex(string band)
        {
            if (string.IsNullOrEmpty(band) || band.Length < 2 || band[0] != 'B') return 0;
            return int.TryParse(band.Substring(1), out var n) ? n : 0;
        }

        // 現在の統一速度S[km/min]。到達済み天体の最上位バンドの required_speed。
        public double CurrentSpeedKmMin(GameData data)
        {
            int frontier = 1; // 最低でも B1
            foreach (var b in data.Bodies)
            {
                if (!b.Unlocked || b.Master == null) continue;
                int idx = BandIndex(b.Band);
                if (idx > frontier) frontier = idx;
            }
            return _speedByBandIndex.TryGetValue(frontier, out var s) && s > 0
                ? s : _speedByBandIndex.TryGetValue(1, out var s1) ? s1 : 380000.0;
        }

        // 実距離[km]の片道秒数 = 距離 ÷ S × 60。
        public float OneWaySeconds(double distanceKm, GameData data)
        {
            double s = CurrentSpeedKmMin(data);
            return (float)Math.Max(1.0, distanceKm / s * 60.0);
        }
    }
}
