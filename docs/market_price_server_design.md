# 市況連動サーバ 技術設計書 v1
(宇宙採掘ゲーム価格配信 + 管理者用の相場モニター)

## 0. 方針
- **Phase 1(先行稼働)**:常時稼働マシン(Raspberry Pi等)+ cron + Python + SQLite + Flask。価格データの蓄積と管理者用モニター(自分用のウォッチリスト・手動入力)を稼働させる。ゲームより先にW・Cu・Cr・Mo等の相場監視・データ蓄積が始められる
- **Phase 2(ゲームリリース時)**:同じPiをオリジンにしたまま、生成した価格JSONをCDN(Cloudflare R2/Pages)へpublishして配信。ゲームクライアントは静的JSONを取得するだけなので、Piが一時停止してもCDNキャッシュで配信が継続する。規模が出たらVPS/クラウドへ移行(コードはそのまま)

```
[データソース群]                [Raspberry Pi]                      [配信先]
 FRED API ──┐              ┌────────────────────┐       ┌ CDN(R2/Pages) ─▶ ゲームアプリ
 貴金属API ─┼─ 日次cron ─▶│ fetcher → 検証 → クランプ │──▶│
 手動入力 ──┘   06:00 ICT  │ → SQLite → JSON生成+署名  │       └ Flask(Tunnel) ─▶ 社内ダッシュボード
 (Zalo/Web)                └──────────┬─────────┘                └ Zalo OA ─▶ 社内通知
                                       └ 失敗時アラート(Zalo)
```

## 1. データソース設計(段階導入)
| 区分 | 指標 | ソース | 自動/手動 | 備考 |
|---|---|---|---|---|
| 無料・公的 | 天然ガスHenry Hub(日次)、WTI(日次)、鉄鉱石(月次)、石炭(月次) | FRED API | 自動 | APIキー無料。パブリックドメインで再配布問題なし |
| 無料API | 金・銀・白金・パラジウム スポット | 無料系貴金属API(要選定・規約確認) | 自動 | 落ちたら手動フォールバック |
| 購読系 | LME非鉄7種(Cu/Al/Zn/Pb/Sn/Ni/Co) | LME公式/商用API(metals-api等) | Phase1手動→Phase2契約 | **再配布=ライセンス契約必須** |
| 購読系 | W・Cr・Mo・V・ウラン | SMM/Fastmarkets/AsianMetal | 手動(週1) | 社内利用契約とゲーム配信契約は別条件 |
| 手動 | 上記すべての緊急上書き | Webフォーム | 手動 | ダッシュボード上の入力欄から当日値をupsert |

**Phase 1の現実解**:自動化できる指標(FRED+貴金属)から始め、購読系は週1の手動入力で運用開始。手動入力はダッシュボードのWebフォームから行う。

## 2. DBスキーマ(SQLite)
```sql
CREATE TABLE instruments (
  id TEXT PRIMARY KEY,            -- 'tungsten', 'copper', ...
  name_ja TEXT, name_en TEXT,
  source TEXT,                    -- 'fred:DHHNGSP', 'manual', 'metalsapi:XPT'
  fetch_type TEXT,                -- 'auto' | 'manual'
  link_type TEXT,                 -- 'daily' | 'weekly' | 'fixed'
  unit_note TEXT,                 -- '$/t', '$/toz', '$/MMBtu'
  usd_per_kg_factor REAL          -- 原単位→USD/kg 換算係数(例: $/t→0.001, $/toz→1/0.0311035)
);
CREATE TABLE prices (
  instrument_id TEXT, date TEXT,
  raw_value REAL, usd_per_kg REAL,
  source_note TEXT, created_at TEXT,
  PRIMARY KEY (instrument_id, date)
);
CREATE TABLE game_prices (        -- クランプ・NOVA換算適用後(ゲーム配信用)
  date TEXT, instrument_id TEXT,
  nova_per_kg REAL, change_1d REAL,
  clamped INTEGER, stale_days INTEGER,
  PRIMARY KEY (date, instrument_id)
);
CREATE TABLE config (key TEXT PRIMARY KEY, value TEXT);
-- base_usd_vnd=26300 / base_iron_usd_kg=0.105 / clamp_daily=0.10 / clamp_total=0.50 / base_date=2026-07-11
```

## 3. 日次バッチ(06:00 ICT、cron)
1. **取得**:auto系ソースを取得(タイムアウト15秒×リトライ3回、指数バックオフ)
2. **換算**:原単位→USD/kg(instruments.usd_per_kg_factor)
3. **妥当性チェック**:前日比±30%超は「保留」としてメールでアラート(手動承認まで前日値を使用)— データ事故と本物の急騰(例:2026年のタングステン)を人間が判別する
4. **クランプ**:ゲーム用に 日次±10%・基準日(2026-07-11)比±50% を適用(`clamped=1`を記録。実勢は`prices`に無加工で残る=社内用は生値、ゲーム用はクランプ値の二本立て)
5. **NOVA換算**:`nova_per_kg = usd_per_kg × 26300`(基準レート固定)
6. **休場・欠測**:前営業日値を継続し `stale_days` を加算。7日超はクライアント側で基準値フォールバック
7. **出力**:`latest.json` と `history_{id}.json` を生成 → Ed25519署名 → CDNへpublish(Phase 2)
8. **管理用通知**:取得失敗・保留発生時に管理者へメール。朝の相場サマリ配信は任意設定
9. **ヘルスチェック**:バッチ完了時に healthchecks.io へping(来なければ外部から検知)

## 4. 配信API仕様(ゲーム向け)
```
GET /v1/prices/latest
{
  "date": "2026-07-11",
  "base": { "usd_vnd": 26300, "iron_usd_kg": 0.105, "base_date": "2026-07-11" },
  "prices": [
    { "id": "iron_ore", "nova_per_kg": 2762, "usd_per_kg": 0.105, "change_1d": 0.012, "stale_days": 0 },
    { "id": "tungsten", "nova_per_kg": 4655100, "usd_per_kg": 177.0, "change_1d": -0.004, "stale_days": 0 }
  ],
  "sig": "base64(ed25519)", "key_id": "k1"
}
GET /v1/prices/history?id=tungsten&days=90   -- 図鑑チャート用
```
- **署名**:払い出しJSON全体にEd25519署名。公開鍵はアプリ同梱(key_idでローテーション対応)。HTTPSに加えアプリ層で検証し、改ざん時は基準値へフォールバック(チート対策:ローカルプロキシで価格を書き換えても署名不一致で無効)
- **キャッシュ**:Cache-Control: max-age=3600。クライアントは起動時+6時間ごとに取得、失敗時は最終キャッシュ(最大7日)→基準値
- 加工品(超硬・ダイス鋼等)の価格はクライアント側で成分×当日価格から計算(レシピ係数はアプリ内マスタ)。サーバは素材価格のみ配信すればよい

## 5. 管理コンソール(Phase 1の主産物・自分用)
- Flask 1ページ:ウォッチリスト、90日チャート、前日比、生値(クランプなし)とゲーム値の並記、手動入力フォーム、保留承認ボタン
- 管理用メール通知:①取得失敗 ②異常値の保留発生、の2つを管理者へ通知(気づかず古い価格を配り続ける事態の防止が目的)。毎朝の相場サマリは任意設定(オフ可)
- 将来:見積システム(Google Sheets)から材料市況を参照する業務連携も同じAPIで可能

## 6. 運用・保守
- バックアップ:SQLite日次ダンプ→ローカル+クラウド(既存の12ヶ月ローテーション方針に準拠)
- ログ:取得成否・保留判定・クランプ発動を記録。月次で「クランプ発動率」を確認(高頻度ならバンド幅を再検討)
- 障害時:バッチ失敗→メールでアラート→Webフォームの手動入力で当日を凌げる設計(自動化に人間のバイパスを常設)

## 7. ライセンス整理(リリース前の必須確認)
| ソース | 社内利用 | ゲーム配信(再配布) |
|---|---|---|
| FRED(公的) | ○ | ○(出典表記) |
| 無料貴金属API | 規約次第 | 規約次第(多くは再配布不可→商用API検討) |
| LME | 購読契約 | **別途データ再配布契約が必要** |
| SMM/Fastmarkets/AsianMetal | 購読契約 | 同上 |
- 代替案:複数ソースから独自算出した「ゲーム内指数」として配信する方式(生値の再配布を避ける)。ただし規約回避になるかは法務確認が必要
- ゲーム内表記:「価格は実市況を参考にしたゲーム内換算値(NOVA)」+出典クレジット

## 8. 実装順(Phase 1 = 週末2〜3日規模)
1. SQLiteスキーマ+instrumentsマスタ投入(33資源)
2. FRED fetcher(天然ガス・WTI・鉄鉱石・石炭)+換算・保存
3. 手動入力(Webフォーム)
4. 日次バッチ+朝サマリのメール通知
5. 管理コンソール(ウォッチリスト+チャート)
6. (Phase 2)クランプ+NOVA換算+JSON署名+CDN publish
