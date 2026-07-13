# 市況連動サーバ Phase 1

`market_price_server_design.md` の実装。ゲームとは独立に稼働し、価格データの蓄積と
管理者用モニターを提供する。ゲーム配信用の署名付き JSON もここで生成する。

## 構成
| ファイル | 役割 |
|---|---|
| `schema.sql` | SQLite スキーマ(instruments / prices=生値 / game_prices=配信値 / config) |
| `seed.py` | instruments マスタ(33種)と基準日価格を投入。真実源は `data/balance/resources.json` |
| `fetchers/fred.py` | FRED fetcher(天然ガス/WTI/鉄鉱石/石炭)。15秒×3リトライ・指数バックオフ |
| `convert.py` | NOVA換算+クランプ(日次±10%・基準日比±50%) |
| `sign.py` | Ed25519 鍵生成・署名・検証 |
| `notify.py` | 管理用メール通知(取得失敗・保留発生)。未設定時はコンソール出力 |
| `batch.py` | 日次バッチ(取得→妥当性±30%保留→クランプ→NOVA→署名JSON→通知) |
| `app.py` | Flask:管理コンソール+手動入力+承認+ゲーム配信API |

## 価格モデル(基準日比スケール)
```
usd_per_kg(当日) = base_usd_per_kg × (raw当日 / raw基準日)
nova_per_kg      = clamp( usd_per_kg × 26,300 )      ← ゲーム配信値
```
単位($/MMBtu, $/bbl, $/t, $/toz…)の差を比率で吸収するため、
**基準日の NOVA 価格はゲームの `data/balance/resources.json` と厳密に一致する**
(検証済み:33銘柄すべて一致)。固定直接指定(He3・希少物質)は市場なし=常に基準値。

## セットアップ
```bash
pip3 install -r server/requirements.txt
cp server/config.example.json server/config.json   # fred_api_key / smtp を記入
python3 tools/export_balance.py                     # data/balance/*.json を先に生成
python3 server/seed.py                              # instruments + 基準日価格
python3 server/batch.py 2026-07-11                  # 基準日の配信JSONを生成・署名
python3 server/app.py                               # http://127.0.0.1:5057
```

## 日次運用
- cron で `python3 server/batch.py`(design §3:06:00 ICT)。
- FRED キー未設定/取得失敗時は前日値を継続(stale_days加算)し、管理者へ通知。
- 前日比 ±30% 超は**保留**(`pending=1`)。承認するまでゲーム値は前日値を使用。
  管理コンソールの「承認」ボタンで `pending=0` にすると次バッチで反映。
- 手動系(LME非鉄・貴金属・週次 Cr/Mo/V/W/U)は管理コンソールの手動入力フォームで upsert。

## ゲーム配信API
| エンドポイント | 内容 |
|---|---|
| `GET /v1/prices/latest` | 署名付き最新価格(`dist/latest.json`)。`Cache-Control: max-age=3600` |
| `GET /v1/prices/history?id=<id>&days=90` | 図鑑チャート用 |

- 署名は Ed25519。公開鍵(base64)は `keys/public_key.txt` → アプリに同梱し、
  クライアントは本文を検証。改ざん時は基準値フォールバック(検証済み:改ざんJSONを棄却)。
- Phase 2 ではこの `dist/*.json` を CDN(Cloudflare R2/Pages)へ publish する。

## FRED シリーズ(要検証・叩き台)
| 資源 | series | 単位 | 基準日raw |
|---|---|---|---|
| methane(天然ガス) | DHHNGSP | $/MMBtu | 2.95 |
| hydrocarbon(WTI) | DCOILWTICO | $/bbl | 71.97 |
| iron_ore(鉄鉱石) | PIORECRUSDM | $/t | 105.14 |
| coal(石炭) | PCOALAUUSDM | $/t | 129.75 |

series ID と基準日値は運用前に FRED 実データで要確認(`seed.py` の `FRED_SERIES`)。

## ライセンス注意(design §7)
- FRED は公的(再配布可・出典表記)。LME/SMM/Fastmarkets 等の購読系は
  **ゲーム配信=別途データ再配布契約が必要**。Phase 1 は手動入力で運用開始できる。
- ゲーム内表記:「価格は実市況を参考にしたゲーム内換算値(NOVA)」+出典クレジット。
