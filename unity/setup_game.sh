#!/usr/bin/env bash
# ゲーム本体(Phase 1〜)の Unity プロジェクトを作成し、スクリプトと
# データJSON(天体マスタ + レイアウト, StreamingAssets)を配置して
# バッチモードでコンパイル + 起動シーン生成まで行う。
#
# 前提: Unity Hub で Personal ライセンスを認証済みであること。
# 実行: bash unity/setup_game.sh
#
# ズーム PoC(unity/setup_project.sh / SpaceMiningPoc)とは別プロジェクト。
# 本スクリプトが生成する SpaceMiningGame が Phase 1 以降のゲーム本体。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.79f1/Unity.app/Contents/MacOS/Unity"
PROJ="$ROOT/unity/SpaceMiningGame"
CREATE_LOG="$ROOT/unity/game_create.log"
COMPILE_LOG="$ROOT/unity/game_compile.log"

if [ ! -x "$UNITY" ]; then
  echo "Unity エディタが見つかりません: $UNITY" >&2; exit 1
fi

# 1) プロジェクト生成(素の 2D。URP は後で Package Manager から。マップは Built-in で動く)
if [ ! -d "$PROJ" ]; then
  echo "→ プロジェクト作成: $PROJ"
  "$UNITY" -batchmode -createProject "$PROJ" -quit -logFile "$CREATE_LOG" || {
    echo "createProject 失敗。$CREATE_LOG を確認(多くは License 未認証)"; tail -20 "$CREATE_LOG"; exit 1; }
fi

# 2) スクリプト配置(ランタイム / エディタ拡張を分離。Editor 配下は player ビルド除外)
mkdir -p "$PROJ/Assets/Game" "$PROJ/Assets/Game/Editor" "$PROJ/Assets/StreamingAssets"
cp "$ROOT/unity/Game/BodyMaster.cs" \
   "$ROOT/unity/Game/MapLayout.cs" \
   "$ROOT/unity/Game/CelestialBody.cs" \
   "$ROOT/unity/Game/GameData.cs" \
   "$ROOT/unity/Game/BalanceOverride.cs" \
   "$ROOT/unity/Game/ShipStats.cs" \
   "$ROOT/unity/Game/Fleet.cs" \
   "$ROOT/unity/Game/ResourcePrices.cs" \
   "$ROOT/unity/Game/Market.cs" \
   "$ROOT/unity/Game/MarketPanel.cs" \
   "$ROOT/unity/Game/Inventory.cs" \
   "$ROOT/unity/Game/UiIcons.cs" \
   "$ROOT/unity/Game/UiKit.cs" \
   "$ROOT/unity/Game/UiRoot.cs" \
   "$ROOT/unity/Game/BodyPanel.cs" \
   "$ROOT/unity/Game/StorePanel.cs" \
   "$ROOT/unity/Game/UpgradeCurve.cs" \
   "$ROOT/unity/Game/UpgradePanel.cs" \
   "$ROOT/unity/Game/GameState.cs" \
   "$ROOT/unity/Game/SaveSystem.cs" \
   "$ROOT/unity/Game/OfflinePanel.cs" \
   "$ROOT/unity/Game/SpeedCurve.cs" \
   "$ROOT/unity/Game/FleetSimulator.cs" \
   "$ROOT/unity/Game/SpriteBank.cs" \
   "$ROOT/unity/Game/Starfield.cs" \
   "$ROOT/unity/Game/SpaceMapController.cs" "$PROJ/Assets/Game/"
cp "$ROOT/unity/Game/Editor/GameSceneSetup.cs" \
   "$ROOT/unity/Game/Editor/GameSmokeTest.cs" "$PROJ/Assets/Game/Editor/"

# 3) データJSON を StreamingAssets へ(真実源:
#    レイアウト = poc/map_layout/export_layout.py / 天体マスタ = tools/export_balance.py)
cp "$ROOT/data/map/map_layout.json"      "$PROJ/Assets/StreamingAssets/map_layout.json"
cp "$ROOT/data/balance/bodies.json"      "$PROJ/Assets/StreamingAssets/bodies.json"
cp "$ROOT/data/balance/ships.json"       "$PROJ/Assets/StreamingAssets/ships.json"
cp "$ROOT/data/balance/resources.json"   "$PROJ/Assets/StreamingAssets/resources.json"
cp "$ROOT/data/balance/speed_curve.json" "$PROJ/Assets/StreamingAssets/speed_curve.json"
cp "$ROOT/data/balance/upgrade_curve.json" "$PROJ/Assets/StreamingAssets/upgrade_curve.json"
cp "$ROOT/data/market/latest.json"       "$PROJ/Assets/StreamingAssets/latest.json"

# 4) コンパイル + 起動シーン生成 + 縦画面/iOS16 を一括適用
echo "→ コンパイル & シーン生成(batchmode)"
"$UNITY" -batchmode -quit -projectPath "$PROJ" \
  -executeMethod SpaceMining.Game.GameSceneSetup.CreateMapScene \
  -logFile "$COMPILE_LOG" || {
    echo "コンパイル/シーン生成でエラー。ログ末尾:"; tail -30 "$COMPILE_LOG"; exit 1; }

# 5) 生成物の確認(batchmode はコンパイルエラーでも 0 を返すことがあるので明示チェック)
if grep -qE "error CS[0-9]" "$COMPILE_LOG"; then
  echo "C# コンパイルエラー:"; grep -E "error CS[0-9]" "$COMPILE_LOG" | sort -u; exit 1; fi
if [ ! -f "$PROJ/Assets/Scenes/Map.unity" ]; then
  echo "起動シーンが生成されていません。$COMPILE_LOG を確認"; exit 1; fi

echo "✓ セットアップ完了: $PROJ"
echo "  Unity Hub でこのフォルダを開き、Assets/Scenes/Map を開いて Play するだけ。"
echo "  マウスホイール=ズーム / 左ドラッグ=パン / 天体クリック=選択。"
