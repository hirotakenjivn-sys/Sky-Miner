#!/usr/bin/env bash
# シームレスズーム PoC の Unity プロジェクトを作成し、スクリプトと
# レイアウトJSON(StreamingAssets)を配置してバッチモードでコンパイル確認する。
#
# 前提: Unity Hub で Personal ライセンスを認証済みであること
#       (未認証だと -createProject が License エラーで失敗する)。
# 実行: bash unity/setup_project.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.79f1/Unity.app/Contents/MacOS/Unity"
PROJ="$ROOT/unity/SpaceMiningPoc"
LOG="$ROOT/unity/unity_create.log"

if [ ! -x "$UNITY" ]; then
  echo "Unity エディタが見つかりません: $UNITY" >&2; exit 1
fi

# 1) プロジェクト生成(2D URP テンプレートは CLI 指定不可のため素の 2D で作り、
#    URP は後で Package Manager から入れる。ズーム PoC は URP 不要で動く)
if [ ! -d "$PROJ" ]; then
  echo "→ プロジェクト作成: $PROJ"
  "$UNITY" -batchmode -createProject "$PROJ" -quit -logFile "$LOG" || {
    echo "createProject 失敗。$LOG を確認(多くは License 未認証)"; tail -20 "$LOG"; exit 1; }
fi

# 2) スクリプト配置(ランタイム / エディタ拡張を分離。Editor 配下は player ビルド除外)
mkdir -p "$PROJ/Assets/ZoomPoc" "$PROJ/Assets/ZoomPoc/Editor" "$PROJ/Assets/StreamingAssets"
cp "$ROOT/unity/ZoomPoc/MapLayout.cs" "$ROOT/unity/ZoomPoc/ZoomPocBootstrap.cs" "$PROJ/Assets/ZoomPoc/"
cp "$ROOT/unity/ZoomPoc/Editor/SceneSetup.cs" "$PROJ/Assets/ZoomPoc/Editor/"

# 3) レイアウトJSON を StreamingAssets へ(真実源: export_layout.py の生成物)
cp "$ROOT/data/map/map_layout.json" "$PROJ/Assets/StreamingAssets/map_layout.json"

# 4) コンパイル + 起動シーン生成 + ビルド設定/縦画面/iOS16 を一括適用
echo "→ コンパイル & シーン生成(batchmode)"
"$UNITY" -batchmode -quit -projectPath "$PROJ" \
  -executeMethod SpaceMining.ZoomPoc.SceneSetup.CreateMapScene \
  -logFile "$ROOT/unity/unity_compile.log" || {
    echo "コンパイル/シーン生成でエラー。ログ末尾:"; tail -30 "$ROOT/unity/unity_compile.log"; exit 1; }

# 5) 生成物の確認(コンパイルエラーは batchmode が 0 を返すことがあるので明示チェック)
if grep -qE "error CS[0-9]" "$ROOT/unity/unity_compile.log"; then
  echo "C# コンパイルエラー:"; grep -E "error CS[0-9]" "$ROOT/unity/unity_compile.log" | sort -u; exit 1; fi
if [ ! -f "$PROJ/Assets/Scenes/Map.unity" ]; then
  echo "起動シーンが生成されていません。$ROOT/unity/unity_compile.log を確認"; exit 1; fi

echo "✓ セットアップ完了: $PROJ"
echo "  Unity Hub でこのフォルダを開き、Assets/Scenes/Map を開いて Play するだけ。"
