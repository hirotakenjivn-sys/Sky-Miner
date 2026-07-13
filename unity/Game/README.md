# 宇宙マップ(ゲーム本体 Phase 1)

`mvp_dev_plan.md` Phase 1 の最初のタスク「宇宙マップ(対数リング座標系・50天体マスタ読込・
MVP5天体有効化・千鳥配置)」の実装。Phase 0 の検証済みシームレスズーム(`unity/ZoomPoc/`)を
土台に、ゲーム用データと MVP ゲート・タップ選択を加えたもの。

## 構成(真実源)
- `BodyMaster.cs` — 天体マスタ(`data/balance/bodies.json`、xlsx 由来)の DTO/ローダー。
- `MapLayout.cs` — 対数リング座標(`data/map/map_layout.json`、`ring_layout.py` 由来)の DTO/ローダー。
- `CelestialBody.cs` — マスタ ⋈ レイアウトを `no` で結合したランタイム天体 + 開放二段ゲート。
- `GameData.cs` — 両JSONを読み結合し、地球拠点を原点に合成(拠点+50天体=51)。
- `SpaceMapController.cs` — 描画/ズーム/パン/LOD3段/MVPゲート表示/タップ選択。
- `Editor/GameSceneSetup.cs` — 起動シーン生成(縦画面・iOS16)。

座標・数値はここで再計算せず、JSON を読むだけ(数値ハードコード禁止・二重管理回避)。

## 生成と実行
```
bash unity/setup_game.sh          # SpaceMiningGame プロジェクトを生成+コンパイル
```
Unity Hub で `unity/SpaceMiningGame` を開き、`Assets/Scenes/Map` を Play。
- マウスホイール=ズーム / 左ドラッグ=パン / 天体クリック=選択。
- 左上に FPS/LOD、左下に選択天体の暫定情報(距離・開放状態・資源)。

## MVP スコープゲート(企画書14章)
- MVP5天体 = 地球[拠点]・月・エロス(小惑星帯)・水星・火星 のみ操作可能。
- MVP対象外の45天体は減光 + 🔒 表示、タップすると「未開放」トーストのみ。
- MVP内でも進行ゲート(`Unlocked`)は別: 月のみ初期解放(`unlock_price_nova==0`)、
  他はクレジット購入(Phase 2「天体アンロック」)まで未開放。

## 次タスクとの接続
`SpaceMapController.OnBodySelected(CelestialBody)` を天体パネル/派遣UI(Phase 1 次タスク)が
購読する。現状は暫定 OnGUI で選択情報を出しているだけ。

## 未検証
この環境では Unity 実行不可のため、`setup_game.sh` によるコンパイル確認は未実施。
ユーザーのMac(Unity 6000.0.79f1 導入済み)で実行し、`error CS` が出ないことを確認すること。
