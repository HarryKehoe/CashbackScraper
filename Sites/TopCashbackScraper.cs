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

public class TopCashbackScraper : ISiteScraper
{
    public string SiteName => "TopCashback";

    public async Task LoginAsync(IPage page, string baseUrl, CancellationToken ct)
    {
        await page.GotoAsync(baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        // Actual "wait for login" pause is handled by the orchestrator's
        // waitForUserLogin callback, so the caller (console/GUI) controls it.
    }

    public async Task<List<RawOfferResult>> ScrapeMerchantAsync(IPage page, MerchantTarget merchant, CancellationToken ct)
    {
        await page.GotoAsync(merchant.Url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        try
        {
            await page.WaitForSelectorAsync(".merch-rate-card, .merch-offer, .merchant-rate",
                new PageWaitForSelectorOptions { Timeout = 10000 });
        }
        catch (TimeoutException) { /* page may just have no offers */ }

        string jsonResult = await page.EvaluateAsync<string>(@"() => {
            try {
                const results = [];
                const rateCards = document.querySelectorAll('.merch-rate-card');

                rateCards.forEach(card => {
                    if (!card || !card.children) return;
                    let currentCategory = '';
                    const children = Array.from(card.children);

                    for (let i = 0; i < children.length; i++) {
                        const el = children[i];
                        if (!el || !el.classList) continue;

                        if (el.classList.contains('merch-cat__title')) {
                            currentCategory = (el.innerText || '').replace(/\s+/g, ' ').trim();
                        }
                        else if (el.classList.contains('merch-cat__sub-cat')) {
                            const subCat = (el.innerText || '').replace(/\s+/g, ' ').trim();
                            let isExclusive = false;
                            let countdown = '';
                            let rateText = '';

                            let j = i + 1;
                            while (j < children.length) {
                                const sibling = children[j];
                                if (!sibling || !sibling.classList) { j++; continue; }

                                if (sibling.classList.contains('merch-cat__sub-cat') || sibling.classList.contains('merch-cat__title')) break;

                                if (sibling.classList.contains('merch-cat__tag-wrap-outer') || sibling.classList.contains('merch-cat__tag-wrap')) {
                                    const tagSpans = sibling.querySelectorAll('.merch-cat__tag span');
                                    if (tagSpans) {
                                        tagSpans.forEach(t => {
                                            const txt = (t.innerText || '').replace(/\s+/g, ' ').trim();
                                            if (txt.toLowerCase().includes('exclusive')) {
                                                isExclusive = true;
                                            } else if (txt.length > 0) {
                                                countdown = countdown ? `${countdown} | ${txt}` : txt;
                                            }
                                        });
                                    }
                                }

                                if (sibling.classList.contains('merch-cat__rate-wrap')) {
                                    rateText = (sibling.innerText || '').replace(/\s+/g, ' ').trim();
                                }

                                j++;
                            }

                            results.push({
                                category: currentCategory || 'Standard Cashback',
                                subCategory: subCat || 'All Purchases',
                                isExclusive: isExclusive,
                                countdown: countdown || '',
                                rateText: rateText || ''
                            });
                        }
                    }
                });

                if (results.length === 0) {
                    const promoOffers = document.querySelectorAll('.merch-offer, .merchant-offer');
                    promoOffers.forEach(offer => {
                        if (!offer) return;
                        const titleEl = offer.querySelector('.merch-offer__title, .merch-offer__custom-deal-title');
                        const rateEl = offer.querySelector('.merch-offer__rate');

                        if (titleEl || rateEl) {
                            let isExclusive = (offer.innerText || '').toLowerCase().includes('exclusive');
                            let countdown = '';

                            const tags = offer.querySelectorAll('.merch-offer__tag span, .merch-cat__tag span');
                            if (tags) {
                                tags.forEach(t => {
                                    const txt = (t.innerText || '').replace(/\s+/g, ' ').trim();
                                    if (!txt.toLowerCase().includes('exclusive') && txt.length > 0) {
                                        countdown = countdown ? `${countdown} | ${txt}` : txt;
                                    }
                                });
                            }

                            results.push({
                                category: 'Promotions',
                                subCategory: titleEl ? (titleEl.innerText || '').replace(/\s+/g, ' ').trim() : 'Special Offer',
                                isExclusive: isExclusive,
                                countdown: countdown || '',
                                rateText: rateEl ? (rateEl.innerText || '').replace(/\s+/g, ' ').trim() : ''
                            });
                        }
                    });
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
        var rawOffers = JsonSerializer.Deserialize<List<RawOffer>>(dataJson) ?? new List<RawOffer>();

        foreach (var raw in rawOffers)
        {
            offers.Add(new RawOfferResult
            {
                Category = raw.Category,
                SubCategory = raw.SubCategory,
                IsExclusive = raw.IsExclusive,
                Countdown = raw.Countdown,
                RateText = raw.RateText,
                Rate = RateTextParser.ParsePercentage(raw.RateText)
            });
        }
        return offers;
    }

    private class RawOffer
    {
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("subCategory")] public string SubCategory { get; set; } = string.Empty;
        [JsonPropertyName("isExclusive")] public bool IsExclusive { get; set; }
        [JsonPropertyName("countdown")] public string Countdown { get; set; } = string.Empty;
        [JsonPropertyName("rateText")] public string RateText { get; set; } = string.Empty;
    }
}