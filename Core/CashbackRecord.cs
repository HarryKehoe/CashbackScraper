using System;

namespace CashbackScraper.Core;

/// <summary>
/// Persisted row. This is your existing TopCashback.Core.CashbackRecord with one
/// addition: a "Site" field, since records now come from multiple cashback sites,
/// not just TopCashback. If your real DatabaseContext/schema doesn't have a Site
/// column yet, add it (e.g. "ALTER TABLE CashbackRecords ADD COLUMN Site TEXT").
/// </summary>
public class CashbackRecord
{
    public string Site { get; set; } = string.Empty;          // "TopCashback", "Rakuten", "Quidco", ...
    public string Merchant { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string RateText { get; set; } = string.Empty;
    public bool IsExclusive { get; set; }
    public string Countdown { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
}

/// <summary>
/// What a per-site scraper hands back for a single merchant page, before it's
/// turned into a CashbackRecord (site/merchant/timestamp get stamped on by the
/// orchestrator so individual scrapers don't need to know about them).
/// </summary>
public class RawOfferResult
{
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public bool IsExclusive { get; set; }
    public string Countdown { get; set; } = string.Empty;
    public string RateText { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}
