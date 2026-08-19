using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashbackScraper.Scraping;

public class MerchantTarget
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public class ScrapeCategory
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("merchants")] public List<MerchantTarget> Merchants { get; set; } = new();
}

public class SiteConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;      // must match ISiteScraper.SiteName
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = string.Empty;
    [JsonPropertyName("profileDir")] public string ProfileDir { get; set; } = string.Empty; // per-site Playwright profile
    [JsonPropertyName("categories")] public List<ScrapeCategory> Categories { get; set; } = new();

    /// <summary>Flattened list of enabled merchants across enabled categories.</summary>
    public IEnumerable<(ScrapeCategory Category, MerchantTarget Merchant)> EnabledMerchants()
    {
        foreach (var cat in Categories)
        {
            if (!cat.Enabled) continue;
            foreach (var m in cat.Merchants)
            {
                if (m.Enabled) yield return (cat, m);
            }
        }
    }
}

public class RootConfig
{
    [JsonPropertyName("sites")] public List<SiteConfig> Sites { get; set; } = new();

    public static RootConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RootConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new RootConfig();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }
}
