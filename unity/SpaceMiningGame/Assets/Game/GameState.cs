// ゲーム進行状態(この段階では所持NOVAと実行時の時間倍率のみ)。
// セーブ・オフライン進行・GEM等はそれぞれのPhaseで拡張する。
namespace SpaceMining.Game
{
    public class GameState
    {
        public double Nova;          // 所持クレジット(NOVA)
        public float TimeScale = 1f; // 実行時の早送り(検証用。既定1)

        // 中盤スキル(拡張機能)。序盤は宇宙船1種のみ、解禁で効率化。既定は未解禁。
        public bool DedicatedMinerUnlocked = false; // 専用採掘船+輸送ピストンのペア運用
        public bool AutoSellUnlocked = false;       // 着艦時の自動売却

        // 施設(店で購入して解禁)。未購入なら該当施設は稼働しない。
        public bool RefineryUnlocked = false;       // 精錬所(鉱石→金属)。未購入なら鉱石は鉱石のまま
        public bool FactoryUnlocked = false;        // 工場(合金クラフト)
        public string FactorySelected = null;       // 工場で生産中の製品id(null=停止)

        // 強化レベル(1始まり。共通の強化曲線を軸ごとに参照)
        public int MineLevel = 1;   // 採掘速度(抽選間隔を短縮)
        public int CargoLevel = 1;  // 積載量(1セッションの目標個数=当たり率に倍率)
        // 解禁済みの採掘資源id(市況パネルで解禁。初期は先頭1資源のみ)
        public System.Collections.Generic.List<string> UnlockedResources
            = new System.Collections.Generic.List<string>();

        public GameState(double startNova = 0)
        {
            Nova = startNova;
        }
    }
}
