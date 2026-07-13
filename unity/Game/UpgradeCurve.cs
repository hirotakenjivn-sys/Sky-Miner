// 強化コスト曲線(xlsx「強化コスト曲線」→ data/balance/upgrade_curve.json)。
// 1本の曲線を複数の強化軸(採掘速度・積載・ステーション等)で共有する。
// level は 1 始まり(Lv1 = 基準・effect_mult=1)。Lv L へ上げる費用 = levels[L-1].cost。
using System;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable] public class UpgradeLevel
    {
        public int lv;
        public double cost;
        public double cumulative_cost;
        public double effect_mult;
    }

    [Serializable] class UpgradeParams
    {
        public double C0_lv1_cost, cost_growth_per_lv, effect_growth_per_lv, milestone_multiplier;
    }

    [Serializable] class UpgradeCurveDto
    {
        public UpgradeParams @params;
        public UpgradeLevel[] levels;
    }

    public class UpgradeCurve
    {
        UpgradeLevel[] _levels;
        public int MaxLevel => _levels.Length;

        public static UpgradeCurve Load(string fileName = "upgrade_curve.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"強化曲線JSONが見つかりません: {path}");
            var dto = JsonUtility.FromJson<UpgradeCurveDto>(File.ReadAllText(path));
            if (dto == null || dto.levels == null || dto.levels.Length == 0)
                throw new InvalidDataException("強化曲線JSONの解析に失敗しました。");
            return new UpgradeCurve { _levels = dto.levels };
        }

        // 現在レベルの効果倍率(採掘/積載に掛ける)
        public double EffectMult(int level)
        {
            int i = Mathf.Clamp(level, 1, _levels.Length) - 1;
            return _levels[i].effect_mult;
        }

        // 現在レベルから次レベルへ上げる費用。最大なら null(=これ以上不可)。
        public double? CostToNext(int level)
        {
            if (level >= _levels.Length) return null;
            return _levels[level].cost;   // levels[level] は lv=level+1
        }

        public bool IsMax(int level) => level >= _levels.Length;
    }
}
