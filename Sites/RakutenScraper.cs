using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CashbackScraper.Core;
using CashbackScraper.Scraping;
using Microsoft.Playwright;

namespace CashbackScraper.Sites;

/// <summary>
/// Rakuten merchant pages render a clean tier list:
///
///   <section data-testid="merchant-tier">
///     <ul>
///       <li>
///         <div><div><span>5% Cashback</span></div></div>   <!-- rate -->
///         <div><span>Hotel Bookings</span></div>            <!-- tier/category name -->
///       </li>
///       ...
///     </ul>
///   </section>
///
/// data-testid attributes are stable across builds (Chakra/emotion's
/// generated "css-xxxxx" classes are NOT — those are regenerated on every
/// deploy, which is why this scraper never selects on them).
///
/// Not every merchant has a tier breakdown; single-rate merchants only show
/// the headline "Up to X% Cashback" via [data-testid="online-cash-back"],
/// so that's used as a fallback.
/// </summary>
public class RakutenScraper : ISiteScraper
{
    public string SiteName => "Rakuten";

    public async Task LoginAsync(IPage page, string baseUrl, CancellationToken ct)
    {
        await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    public async Task<List<RawOfferResult>> ScrapeMerchantAsync(IPage page, MerchantTarget merchant, CancellationToken ct)
    {
        await page.GotoAsync(merchant.Url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        try
        {
            await page.WaitForSelectorAsync(
                "[data-testid=\"merchant-tier\"], [data-testid=\"online-cash-back\"]",
                new PageWaitForSelectorOptions { Timeout = 10000 });
        }
        catch (TimeoutException) { /* merchant page may have no cashback content at all */ }

        string jsonResult = await page.EvaluateAsync<string>(@"() => {
            try {
                const results = [];

                // Tiered category breakdown, e.g. Hotel Bookings / Car Rentals / etc.
                const tierSection = document.querySelector('[data-testid=""merchant-tier""]');
                if (tierSection) {
                    const items = tierSection.querySelectorAll('ul > li');
                    items.forEach(li => {
                        const divs = Array.from(li.children).filter(c => c.tagName === 'DIV');
                        if (divs.length >= 2) {
                            const rateText = (divs[0].innerText || '').replace(/\s+/g, ' ').trim();
                            const subCategory = (divs[1].innerText || '').replace(/\s+/g, ' ').trim();
                            if (rateText) {
                                results.push({ subCategory: subCategory || 'All Purchases', rateText: rateText });
                            }
                        }
                    });
                }

                // Fallback: single headline rate, only used when no tier breakdown exists.
                if (results.length === 0) {
                    const headline = document.querySelector('[data-testid=""online-cash-back""]');
                    if (headline) {
                        const rateText = (headline.innerText || '').replace(/\s+/g, ' ').trim();
                        if (rateText) {
                            results.push({ subCategory: 'All Purchases', rateText: rateText });
                        }
                    }
                }

                return JSON.stringify({ success: true, data: results });
            } catch (err) {
                return JSON.stringify({ success: false, error: err.toString() });
            }
        }");

        var offers = new List<RawOfferResult>();
        if (string.IsNullOrEmpty(jsonResult)) return offers;

        using var doc = JsonDocument.Parse(jsonResult);
        var root = doc.RootElement;
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            return offers;

        var dataJson = root.GetProperty("data").GetRawText();
        var rawOffers = JsonSerializer.Deserialize<List<RawTier>>(dataJson) ?? new List<RawTier>();

        foreach (var raw in rawOffers)
        {
            offers.Add(new RawOfferResult
            {
                // Rakuten doesn't split into named categories the way TopCashback does -
                // the tier name (e.g. "Hotel Bookings") IS the sub-category. Category
                // itself is stamped on by the orchestrator from sites.json.
                SubCategory = raw.SubCategory,
                RateText = raw.RateText,
                Rate = RateTextParser.ParsePercentage(raw.RateText),
                // Rakuten pages in the samples seen so far don't expose an explicit
                // "exclusive" flag the way Quidco does via its GA4 item_variant string.
                // Leaving these false is the honest default until a page with an
                // exclusive-offer badge is captured and its markup inspected.
                IsExclusive = false,
                Countdown = string.Empty
            });
        }
        return offers;
    }

    private class RawTier
    {
        [JsonPropertyName("subCategory")] public string SubCategory { get; set; } = string.Empty;
        [JsonPropertyName("rateText")] public string RateText { get; set; } = string.Empty;
    }
}