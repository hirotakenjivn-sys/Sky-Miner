# tools/ — 経済数値の書き出しパイプライン

CLAUDE.md 絶対ルール③(xlsx→CSV/JSON書き出しパイプライン)の実装。
経済数値の**唯一の真実源は `docs/credit_economy_model.xlsx`**。コードに数値をハードコードせず、
このパイプラインで xlsx を再計算 → JSON 化して取り込む。

## 使い方
```bash
pip3 install openpyxl formulas   # 依存(soffice があれば formulas は無くても可)
python3 tools/export_balance.py
```

## 動作(3ステップ)
1. **再計算**:xlsx の数式セルを評価してキャッシュ値を確定し、
   `build/credit_economy_model.recalc.xlsx`(値スナップショット)を生成。
   - `soffice`(LibreOffice headless)があればそれで再計算(`--convert-to xlsx`)。
   - 無ければ `formulas` ライブラリで xlsx の**実数式**を評価(モデルの手動再実装はしない)。
2. **書き出し**:`openpyxl`(data_only)で読み、`data/balance/*.json` を生成。
3. **検証**:天体50・資源33、速度単調増加、強化50Lv、精錬6レシピ、
   進行シミュレーションの充足率(拘束条件=最小値)が100%前後、
   既知値(エロス≒2,070万NOVA)の突き合わせを assert。

## 出力(`data/balance/`)
| ファイル | 内容 |
|---|---|
| `params.json` | 基準パラメータ(v0/r/α/s/E/USD=VND=26,300/基準日) |
| `resources.json` | 資源33種(`id`・連動区分 daily/weekly/fixed・USD/NOVA単価・市況指標・取得ソース目安) |
| `bodies.json` | 天体50体(距離・収入レート・アンロック価格・開放時点の片道分) |
| `speed_curve.json` | 全船統一速度カーブ10バンド |
| `upgrade_curve.json` | 強化コスト曲線(C0・成長率・50Lv) |
| `refining.json` | 精錬パラメータ+加工6レシピ(成分市況連動の販売単価) |
| `progression.json` | 進行シミュレーション(充足率) |
| `manifest.json` | 上記の索引 |

## 注意
- xlsx の数式セルは(openpyxl で書き込んだ経緯上)キャッシュ値を持たないため、
  必ず本スクリプトの再計算ステップを通すこと。`data/balance/*.json` は生成物。
- `resources.json` の `id` は市況サーバの `instruments.id` と JOIN するキー。
