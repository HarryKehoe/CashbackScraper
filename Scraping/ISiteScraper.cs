using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashbackScraper.Core;
using Microsoft.Playwright;

namespace CashbackScraper.Scraping;

/// <summary>
/// One implementation per cashback site. Implementations should be stateless
/// (no per-run fields) so a single instance can be reused across the whole run.
/// </summary>
public interface ISiteScraper
{
    /// <summary>Must match the "name" field used for this site in sites.json.</summary>
    string SiteName { get; }

    /// <summary>
    /// Navigate to the site's home/login page and wait however long is needed
    /// for the human to log in / clear a captcha. Called once per site, before
    /// any merchant pages are visited.
    /// </summary>
    Task LoginAsync(IPage page, string baseUrl, CancellationToken ct);

    /// <summary>
    /// Navigate to a single merchant's offer page and extract every rate/offer
    /// row found there. Category/merchant name is stamped on by the caller.
    /// </summary>
    Task<List<RawOfferResult>> ScrapeMerchantAsync(IPage page, MerchantTarget merchant, CancellationToken ct);
}
