// 資源単価(xlsx「資源マスタ」→ data/balance/resources.json)。
// 天体パネルの単価チップ(NOVA/kg 常時表示・企画書4章の決定)に使う。
//
// 天体マスタの産出資源(bodies.json の resources)は自由文字列
//(例「鉄・チタン・アルミニウム・He3(推定)・ウラン(KREEP・推定)」)で構造化されていない。
// ここでは中黒区切り + 括弧注記を落として資源マスタ名と突き合わせ、単価の付く資源だけ拾う。
// ※本来は天体→資源IDを構造化してxlsxに持たせるべき(データ負債。将来のexport改善候補)。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceMining.Game
{
    [Serializable]
    public class ResourcePrice
    {
        public string id;
        public string name_ja;
        public string link_type;   // daily | weekly | fixed
        public string source;
        public double usd_per_kg;
        public double nova_per_kg;
        public string indicator;
        public string note;
    }

    [Serializable]
    class ResourcePriceWrapper { public ResourcePrice[] items; }

    public class ResourcePrices
    {
        readonly List<ResourcePrice> _list = new List<ResourcePrice>();
        readonly Dictionary<string, ResourcePrice> _byName = new Dictionary<string, ResourcePrice>();
        readonly Dictionary<string, ResourcePrice> _byId = new Dictionary<string, ResourcePrice>();

        // 自由文字列トークン → 資源マスタ名のエイリアス(表記ゆれ吸収)
        static readonly Dictionary<string, string> Alias = new Dictionary<string, string>
        {
            { "He3", "ヘリウム3" }, { "ヘリウム-3", "ヘリウム3" }, { "ヘリウム３", "ヘリウム3" },
            { "水氷", "水" }, { "氷", "水" },
        };

        public static ResourcePrices Load(string fileName = "resources.json")
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"資源マスタJSONが見つかりません: {path}");
            string json = File.ReadAllText(path).Trim();
            var wrap = JsonUtility.FromJson<ResourcePriceWrapper>("{\"items\":" + json + "}");
            if (wrap == null || wrap.items == null || wrap.items.Length == 0)
                throw new InvalidDataException("資源マスタJSONの解析に失敗しました。");

            var rp = new ResourcePrices();
            foreach (var r in wrap.items)
            {
                rp._list.Add(r);
                if (!string.IsNullOrEmpty(r.name_ja))
                    rp._byName[r.name_ja] = r;
                if (!string.IsNullOrEmpty(r.id))
                    rp._byId[r.id] = r;
            }
            return rp;
        }

        public ResourcePrice ById(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var r) ? r : null;

        public ResourcePrice ByName(string nameJa)
        {
            if (string.IsNullOrEmpty(nameJa)) return null;
            if (Alias.TryGetValue(nameJa, out var canon)) nameJa = canon;
            return _byName.TryGetValue(nameJa, out var r) ? r : null;
        }

        // 天体の産出資源文字列 → 単価の付く資源リスト(出現順・重複除去)。
        public List<ResourcePrice> MatchBodyResources(string resourceText)
        {
            var result = new List<ResourcePrice>();
            if (string.IsNullOrEmpty(resourceText)) return result;

            // 区切り: 中黒・読点・カンマ。括弧以降は注記として捨てる。
            foreach (var raw in resourceText.Split('・', '、', ',', '，'))
            {
                string tok = raw.Trim();
                int paren = tok.IndexOfAny(new[] { '(', '(' });
                if (paren >= 0) tok = tok.Substring(0, paren).Trim();
                if (tok.Length == 0) continue;

                var r = ByName(tok);
                if (r != null && !result.Contains(r))
                    result.Add(r);
            }
            return result;
        }
    }
}
