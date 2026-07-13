-- 市況連動サーバ SQLite スキーマ(market_price_server_design.md §2 準拠・拡張)
-- 生値(prices)とゲーム配信値(game_prices)の二本立て。
-- 価格モデルは「基準日比スケール」: usd_per_kg = base_usd_per_kg × (raw / base_raw)。

CREATE TABLE IF NOT EXISTS instruments (
  id             TEXT PRIMARY KEY,     -- 'iron_ore','copper',... (resources.json の id と一致)
  name_ja        TEXT,
  source         TEXT,                 -- 'fred:DHHNGSP' / 'manual' / 'derived:methane' / 'fixed'
  fetch_type     TEXT,                 -- 'auto' | 'manual' | 'derived' | 'fixed'
  link_type      TEXT,                 -- 'daily' | 'weekly' | 'fixed'
  unit_note      TEXT,                 -- '$/MMBtu','$/bbl','$/t','$/toz','$/kg'
  base_raw       REAL,                 -- 指標の基準日値(raw単位)= 比率モデルの分母
  base_usd_per_kg  REAL,               -- 基準日の USD/kg(= 資源マスタ)
  base_nova_per_kg REAL,               -- 基準日の NOVA/kg(固定直接指定=He3・希少物質の保持用)
  derive_from    TEXT,                 -- derived の親 instrument_id
  indicator      TEXT                  -- 連動する市況指標(表示用)
);

-- 生値(社内用・無加工)。手動上書き・保留フラグもここに持つ。
CREATE TABLE IF NOT EXISTS prices (
  instrument_id  TEXT,
  date           TEXT,                 -- 'YYYY-MM-DD'
  raw_value      REAL,
  usd_per_kg     REAL,
  source_note    TEXT,
  pending        INTEGER DEFAULT 0,    -- 1=妥当性チェック(±閾値)で保留中(未承認)
  created_at     TEXT,
  PRIMARY KEY (instrument_id, date)
);

-- クランプ・NOVA換算適用後(ゲーム配信用)。
CREATE TABLE IF NOT EXISTS game_prices (
  date           TEXT,
  instrument_id  TEXT,
  nova_per_kg    REAL,
  change_1d      REAL,
  clamped        INTEGER DEFAULT 0,
  stale_days     INTEGER DEFAULT 0,
  PRIMARY KEY (date, instrument_id)
);

CREATE TABLE IF NOT EXISTS config (key TEXT PRIMARY KEY, value TEXT);

CREATE INDEX IF NOT EXISTS idx_prices_date ON prices(date);
CREATE INDEX IF NOT EXISTS idx_game_prices_date ON game_prices(date);
