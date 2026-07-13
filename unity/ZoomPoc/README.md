# シームレスズーム PoC(ゲーム Phase 0)

`mvp_dev_plan.md` Phase 0 の最重要リスク検証。**完了条件: 実機(iPhone 12相当)で
L1↔L3 ピンチが 60fps**。

## 何を検証するか
- 単一シーングラフ + LOD3段(L1概観 / L2中間 / L3詳細)で、50天体を
  破棄・再生成せずに**シームレスにズーム**できるか。
- 座標系は付録A(対数リング)。C# で座標を再計算せず、`ring_layout.py` →
  `export_layout.py` が書き出す `data/map/map_layout.json` を読むだけ。
  → 配置の正しさは Python 側のユニットテスト(R2〜R5)が保証。

## ファイル
| ファイル | 役割 |
|---|---|
| `MapLayout.cs` | `map_layout.json` のデータモデル + StreamingAssets ローダー |
| `ZoomPocBootstrap.cs` | 実行時に全構築(カメラ・リング・50ノード・LOD・ピンチ・FPS表示)。`RuntimeInitializeOnLoadMethod` でシーンに無くても Play 時に自動生成 |
| `Editor/SceneSetup.cs` | 起動シーン生成+ビルド設定/縦画面/iOS16 を一括適用(batchmode 実行) |
| `../setup_project.sh` | プロジェクト生成 + スクリプト/JSON配置 + コンパイル + シーン生成 |

## セットアップ手順
### 0) 前提(一度だけ)
Unity Hub で **Personal ライセンスを認証**(無料)。
`open -a "Unity Hub"` → サインイン → Preferences ▸ Licenses ▸ Add ▸ Get a free personal license。

### 1) レイアウトJSONを最新化
```bash
python3 poc/map_layout/export_layout.py   # data/map/map_layout.json 更新
```

### 2) プロジェクト作成 + 配置 + コンパイル確認
```bash
bash unity/setup_project.sh
```
`unity/SpaceMiningPoc/` が生成され、スクリプトと `Assets/StreamingAssets/map_layout.json`
が入り、バッチモードでコンパイルが通ることを確認する。

### 3) シーンに配置して Play(エディタ)
1. Unity Hub で `unity/SpaceMiningPoc` を開く
2. 空シーンを作成(File ▸ New Scene ▸ Basic 2D)
3. 空 GameObject を作成し `ZoomPocBootstrap` を Add Component
4. Play。マウスホイールでズーム、左ドラッグでパン。左上に FPS/LODティア表示。

### 4) iOS 実機ビルド(60fps 計測)
1. Edit ▸ Project Settings ▸ Player:
   - Default Orientation = **Portrait**(縦画面固定)
   - Target minimum iOS = **16.0**
2. File ▸ Build Settings ▸ iOS ▸ Switch Platform ▸ Build(Xcode プロジェクト生成)
3. Xcode で実機(iPhone 12相当)へ。左上 FPS が**ピンチ操作中に 58〜60 を維持**すれば合格。
   - `xcode-select` が CommandLineTools を指している場合:
     `sudo xcode-select -s /Applications/Xcode.app` で実機ビルド可能に。

## 判定
- **合格**: L1↔L3 ピンチが 60fps(下限機種で 30fps 死守)。→ 本実装(Phase 1)へ。
- **不合格**: 企画書5章「完全シームレス」を LOD 段階遷移方式へ設計後退(mvp_dev_plan §3)。

## チューニング(Inspector)
`ZoomPocBootstrap` の公開フィールドで調整可能:
- `iconScreenFactor` … アイコンの画面上サイズ(セマンティックズームの一定サイズ感)
- `lodOverviewAbove` / `lodDetailBelow` … LOD 切替の閾値(orthoSize/world_extent 比)
- `densityMultiplier` … 2以上で天体を複製し負荷ヘッドルームを確認(実機でのストレステスト)
