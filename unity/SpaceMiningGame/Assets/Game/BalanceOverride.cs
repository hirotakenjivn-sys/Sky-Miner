// 暫定プレイテスト・バランス調整(2026-07-13)。
// xlsx真実源の値は「フルゲームの充足率100%」で組んであり数値が大きい。序盤の手触りを
// 「1便=数kg」の小さいレンジで確かめるため、ここで一括スケールする。
//
// ★これは仮の見た目調整であり、最終バランスではない。正式なリバランスは
//   xlsx(船・輸送設計/天体マスタ/強化コスト曲線)を調整して export し直す
//   ([[balance-tuning-todo]] メモリ参照)。係数はこの1ファイルに集約。
namespace SpaceMining.Game
{
    public static class BalanceOverride
    {
        // 宇宙船の積載:500kg × 0.004 = 2kg(1便=1〜3kg規模の起点)
        public const float ShipCapacityScale = 0.004f;
        // 採掘レート:3kg/s × 0.067 ≒ 0.2kg/s(現在は確率抽選採掘のため未使用)
        public const float MineRateScale = 0.067f;
        // 【2-ii・2026-07-14】移動時間を「実距離線形」→「画面距離ベース」へ変更(ユーザー承認)。
        //   CLAUDE.md「移動時間=実距離÷統一速度(線形)」の確定仕様を上書きする。理由:対数配置の
        //   マップ上で実距離線形だと遠方の船が這うように見える(画面距離は圧縮・時間は線形)。
        //   → 片道 = 画面距離(body.Pos の原点からの大きさ) ÷ この視覚速度[world/秒]。
        //   V=120 で 月片道≈1.0s / エロス≈1.9s / 火星≈2.9s / ケレス≈3.4s(一定の画面速度=自然な飛行)。
        public const double VisualTravelSpeedWorldPerSec = 120.0;
        // 旧:実距離線形の速度[km/s]。2-ii移行後は未使用(参照が消えたら削除可)。
        public const double BaseTravelSpeedKmPerSec = 250000.0;
        // 8倍速だが採掘セッション時間は固定のため1便全体は約4倍速→収入レート約4倍。
        // 実時間のペースを保つよう、価格は約4倍に引き上げる(要調整)。
        // 進行シミュ(2026-07-13)でリバランス:旧値は新産出モデルの収入に対しコスト過大だった。
        // 天体解禁 = 実距離が延びるほど予算Bが増える(下記)ので、解禁費用も相応に。
        public const double UnlockPriceScale = 0.006;   // 天体解禁費 = unlock_price_nova × これ
        public const double UpgradeCostScale = 0.0005;  // 強化/資源解禁費 = 曲線 × これ
        // 宇宙船の増設費 = 曲線 × UpgradeCostScale × これ(船は収入をほぼ倍化するので割高。要調整)
        public const double ShipCostMult = 8.0;

        // ── 産出個数バランス(A案:惑星ごと固定予算B・高単価は伝説級レア。[[yield-balance-model]])
        // すべて暫定値。正式には進行シミュ(充足率)と合わせて xlsx で調整する。
        public const double YieldBudgetBase = 6000.0;    // 最内周天体(月)の1セッション平均収入 B0
        // 予算Bを「画面距離」連動に:B = B0 × (画面距離 / 最内周画面距離)^この指数。>1で「遠い=旨い」。
        // 2-ii後は移動も画面距離ベースでtrip時間がほぼ揃うため、β=1.4 で月比 エロス2.3×/火星3.8×/ケレス4.6× rate。
        public const double YieldBudgetDistanceExp = 1.4;
        public const double YieldRarityGamma = 1.5;      // 個数 ∝ 単価^(−γ)。大きいほど高単価が伝説級レア
        public const double BulkPriceThreshold = 500.0;  // これ未満の単価=バルク素材(収入予算Bから除外)
        public const double BulkBaseCountPerSession = 2.0; // バルクの暫定固定個数/session(将来 体積単位で再設計)
    }
}
