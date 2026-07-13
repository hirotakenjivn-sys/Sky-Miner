// 当日市況(市況サーバの latest.json スナップショット)。資源ごとの当日 NOVA/kg と前日比。
// 企画書の独自要素「実市況連動」の取込口。署名検証(Ed25519)と自動更新は Phase 2/3。
// ここでは StreamingAssets に同梱した latest.json を読むだけ(=キャッシュ/フォールバック経路)。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable] public class MarketEntry
    {
        public string id;
        public double nova_per_kg;
        public double change_1d;   // 前日比(比率。0.012 = +1.2%)
        public int stale_days;
        public bool clamped;
    }

    [Serializable] class MarketDto
    {
        public string date;
        public MarketEntry[] prices;
    }

    public class Market
    {
        public string Date;
        readonly Dictionary<string, MarketEntry> _byId = new Dictionary<string, MarketEntry>();

        public IReadOnlyCollection<MarketEntry> Entries => _byId.Values;

        // latest.json が無ければ null(呼び出し側は基準価格へフォールバック)
        public static Market Load(string fileName = "latest.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path)) return null;
            var dto = JsonUtility.FromJson<MarketDto>(File.ReadAllText(path));
            if (dto == null || dto.prices == null) return null;

            var m = new Market { Date = dto.date };
            foreach (var e in dto.prices)
                if (!string.IsNullOrEmpty(e.id)) m._byId[e.id] = e;
            return m;
        }

        public MarketEntry Get(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var e) ? e : null;

        // 前日比の表示テキストと色(▲緑/▼赤/―灰)。言語非依存(記号+数字)。
        public static string ChangeText(double c)
        {
            if (Mathf.Abs((float)c) < 0.00005f) return "―";
            return (c > 0 ? "▲" : "▼") + (Mathf.Abs((float)c) * 100f).ToString("0.0") + "%";
        }
        public static Color ChangeColor(double c)
        {
            if (Mathf.Abs((float)c) < 0.00005f) return new Color(0.55f, 0.58f, 0.71f);
            return c > 0 ? new Color(0.44f, 0.85f, 0.56f) : new Color(0.94f, 0.45f, 0.35f);
        }
    }
}
