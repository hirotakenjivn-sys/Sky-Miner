# アート差し替えパイプライン(Sky-Miner)

ゲーム内の見た目は現在すべて**手続き生成プレースホルダ**です。
このフォルダに **PNG を所定の名前で置くだけで、そのまま本番アートへ差し替わります**
(コード変更不要)。PNG が無いキーは自動的に手続きプレースホルダにフォールバックします。

解決は `SpriteBank.cs`(`Resources.Load<Sprite>("art/…")`)が一元管理します。

---

## フォルダ構成と命名規約

```
Assets/Resources/art/
├── bodies/     天体      <no>.png（個体指定）または <種別キー>.png（種別一括）
├── ships/      船        miner.png / transport.png
├── resources/  資源      <資源id>.png
├── ui/         UIアイコン coin.png / cube.png / bar.png / lock.png
├── bg/         背景      starfield.png
└── mining/     採掘ロボ  robot.png（アニメのスプライトシート）
```

### mining/（採掘ロボのアニメ)
- **`mining/robot.png`** … つるはしで採掘する可愛いロボの**スプライトシート**。
  **正方コマを横に並べた1枚**にする(実行時に自動でコマ分割・ループ再生、8fps)。
  - 例: 1コマ 64×64 を 8 コマ → **512×64** の帯。コマ数はコード側が `幅÷高さ` で自動判定(正方前提)。
  - 採掘中の惑星上に表示される。宇宙船は到着すると**着地して小さくなり**、その横でロボが掘る想定。
  - PNG が無ければロボは出ず、従来の鉱石片エフェクト+着地縮小のみ。

### bodies/（天体）
解決順(先に見つかったものを使う):

1. `bodies/<no>.png` … **その天体だけ**を差し替え(`no` は `bodies.json` の番号)
   例: `bodies/0.png`=月, `bodies/6.png`=水星, `bodies/7.png`=火星, `bodies/1.png`=エロス
2. `bodies/<種別キー>.png` … **同じ種別すべて**を差し替え
3. どちらも無ければ手続きの陰影付き球

地球拠点は番号を持たないため **`bodies/station.png`** で差し替えます。

種別キー(`bodies.json` の `type` → キー):

| type | キー | 手続きプレースホルダの特徴 |
|---|---|---|
| 惑星 | `planet` | 陰影付き球(フルサイズ) |
| 衛星 | `moon` | 小さめの球+クレータ |
| 準惑星 | `dwarf` | 球+環(リング) |
| 小惑星 | `asteroid` | いびつな小さい岩 |
| 彗星 | `comet` | 小さい核+淡いコマ(光冠) |
| 外縁天体 | `tno` | 陰影付き球 |
| (拠点) | `station` | 緑の基地ハブ(二重リング) |

### ships/（船）
- `miner.png` … 採掘船 / `transport.png` … 輸送船
- **PNG は「+Y(上)が機首」**で描くと進行方向へ正しく回転します(原点=地球から外向きに自動回転)。

### resources/（資源アイコン）
- `<資源id>.png`(`resources.json` の `id`)。全33種:
  `iron_ore, coal, methane, methane_ice, hydrocarbon, lumber, lead, chromium, aluminum,
  zinc, copper, nickel, vanadium, cobalt, tin, molybdenum, tungsten, uranium, silver,
  palladium, platinum, gold, rhodium, water, sulfur, nitrogen_ice, carbonaceous,
  ammonia, hydrogen, titanium, helium, helium3, exotic_matter`
- 例: `resources/gold.png`, `resources/iron_ore.png`

### ui/（UIアイコン）
- `coin.png`(通貨NOVA)/ `cube.png`(質量)/ `bar.png`(採掘速度バー)/ `lock.png`(未開放)

### bg/（背景）
- `starfield.png` … 星空。カメラ固定・ズーム追従で画面全体を覆います。
  正方形(例 1024×1024 / 2048×2048)推奨。

---

## PNG のインポート設定(重要)

各 PNG は Unity インスペクタで以下にしてください(既定の 2D テンプレなら自動で近い設定):

- **Texture Type: `Sprite (2D and UI)`** ← これが必須(`SpriteBank` は `Sprite` として読む)
- Sprite Mode: `Single`(採掘ロボのシートも Single でOK。コード側で分割します)
- Pixels Per Unit: 任意(表示サイズはコード側が制御するため見た目に影響しません)
- Alpha: 透過 PNG 推奨(天体・船・資源・UIは背景を透過に)
- **リアル志向(採用)**:`Filter Mode: Bilinear` / `Generate Mip Maps: ON`
  にするとズームイン/アウトの両方で滑らかに出ます。
- **ズーム前提で高解像度**に:天体はシームレスズームで画面いっぱいまで拡大されるため、
  **1024〜2048px 四方**を推奨(512だと寄った時に甘くなる)。`Max Size` を 2048 に。

> Texture Type が `Default` のままだと `Resources.Load<Sprite>` が null を返し、
> プレースホルダのままになります。差し替わらない時はまずここを確認してください。

---

## 色(tint)の扱い

手続きプレースホルダの **天体・船はグレースケール**で、コード側が種別色
(惑星色/衛星色/採掘船シアン/輸送船パープル等)を掛けて着色しています。

- **フルカラーの PNG を置くと自動的に tint=白(素の絵)** になります
  (MVP対象外の天体だけは「未開放」表現のため PNG でも減光されます)。
- つまり **PNG 側で最終的な色を塗ってOK**。プレースホルダの色に縛られません。

資源アイコン・UIアイコン・背景の PNG は常に素の色で表示されます。

---

## 差し替えの手順(まとめ)

1. 上表の名前で PNG を対応フォルダへコピー。
2. Unity に戻ると自動インポート。Texture Type を `Sprite (2D and UI)` に。
3. Play(またはシーン再ロード)で反映。個体 > 種別 > 手続き の順で解決されます。

複数を一気に用意しなくてOK。用意したキーだけ本番アートに、残りはプレースホルダのまま動きます。
