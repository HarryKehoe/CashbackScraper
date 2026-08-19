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
/// Quidco merchant page scraper targeting structured cashback rate containers.
/// </summary>
public class QuidcoScraper : ISiteScraper
{
    public string SiteName => "Quidco";

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
                "[data-test-name=\"cashback-rates\"], .cashback-rate-card, [class*=\"rate\"]",
                new PageWaitForSelectorOptions { Timeout = 10000 });
        }
        catch (TimeoutException) { /* Handle merchants without matching selectors */ }

        // JS string properly escaped for C# verbatim string literals (@"")
        string jsonResult = await page.EvaluateAsync<string>(@"() => {
            try {
                const results = [];
                const clean = (str) => (str || '').replace(/\s+/g, ' ').trim();

                const rateCards = document.querySelectorAll('[data-test-name=""cashback-rate-card""], .cashback-rate-card, div[class*=""RateCard""]');
                
                if (rateCards.length > 0) {
                    rateCards.forEach(card => {
                        const rateEl = card.querySelector('[data-test-name=""rate""], [class*=""rate""], span, h3');
                        const catEl = card.querySelector('[data-test-name=""category""], [class*=""category""], [class*=""title""], p');
                        
                        const rateText = rateEl ? clean(rateEl.innerText) : '';
                        const subCategory = catEl ? clean(catEl.innerText) : 'All Purchases';

                        if (rateText && (rateText.includes('%') || rateText.includes('£') || /\d/.test(rateText))) {
                            results.push({ subCategory: subCategory, rateText: rateText });
                        }
                    });
                }

                if (results.length === 0) {
                    const headline = document.querySelector('[data-test-name=""headline-rate""], h1, [class*=""headline""]');
                    if (headline) {
                        const rateText = clean(headline.innerText);
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
                SubCategory = raw.SubCategory,
                RateText = raw.RateText,
                Rate = RateTextParser.ParsePercentage(raw.RateText),
                IsExclusive = raw.RateText.Contains("Exclusive", StringComparison.OrdinalIgnoreCase),
                Countdown = string.Empty
            });
        }

        return offers;
    }

    public class RawTier
    {
        [JsonPropertyName("subCategory")] public string SubCategory { get; set; } = string.Empty;
        [JsonPropertyName("rateText")] public string RateText { get; set; } = string.Empty;
    }
}