using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashbackScraper.Core;
using Microsoft.Playwright;

namespace CashbackScraper.Scraping;

public enum RunEventLevel { Info, Warning, Error }

public class RunEventArgs : EventArgs
{
    public RunEventLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
}

public class MerchantScrapedEventArgs : EventArgs
{
    public string Site { get; init; } = string.Empty;
    public string Merchant { get; init; } = string.Empty;
    public int OfferCount { get; init; }
}

/// <summary>
/// Minimal contract for whatever persistence layer you already have
/// (e.g. your existing TopCashback.Core.DatabaseContext). Wrap it to
/// satisfy this interface, or just implement it directly.
/// </summary>
public interface ICashbackStore
{
    void SaveBatch(List<CashbackRecord> records);
}

/// <summary>
/// Drives the whole run: for each enabled site, opens a persistent browser
/// profile, waits for manual login/captcha, then scrapes every enabled
/// merchant in every enabled category and saves the results.
/// </summary>
public class ScraperOrchestrator
{
    private readonly RootConfig _config;
    private readonly ICashbackStore _store;
    private readonly Dictionary<string, ISiteScraper> _scrapers;

    public event EventHandler<RunEventArgs>? Log;
    public event EventHandler<MerchantScrapedEventArgs>? MerchantScraped;

    public ScraperOrchestrator(RootConfig config, ICashbackStore store, IEnumerable<ISiteScraper> scrapers)
    {
        _config = config;
        _store = store;
        _scrapers = new Dictionary<string, ISiteScraper>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in scrapers)
            _scrapers[s.SiteName] = s;
    }

    /// <summary>
    /// Runs every enabled site sequentially (one visible browser window at a
    /// time keeps login/captcha handling manageable). Call this from a console
    /// Main, or from a GUI's "Start" button handler on a background task.
    /// </summary>
    public async Task RunAsync(Func<string, Task>? waitForUserLogin = null, CancellationToken ct = default)
    {
        using var playwright = await Playwright.CreateAsync();

        foreach (var site in _config.Sites)
        {
            ct.ThrowIfCancellationRequested();

            if (!site.Enabled)
            {
                Emit(RunEventLevel.Info, $"Skipping {site.Name} (disabled in config).");
                continue;
            }

            if (!_scrapers.TryGetValue(site.Name, out var scraper))
            {
                Emit(RunEventLevel.Warning, $"No ISiteScraper registered for '{site.Name}' — skipping. " +
                                             "Add an implementation and register it in Program.cs.");
                continue;
            }

            var merchants = new List<(ScrapeCategory Category, MerchantTarget Merchant)>(site.EnabledMerchants());
            if (merchants.Count == 0)
            {
                Emit(RunEventLevel.Info, $"{site.Name}: nothing enabled, skipping.");
                continue;
            }

            Emit(RunEventLevel.Info, $"=== {site.Name}: {merchants.Count} merchant(s) to scrape ===");

            var profileDir = string.IsNullOrWhiteSpace(site.ProfileDir)
                ? $"./playwright_profiles/{site.Name}"
                : site.ProfileDir;

            await using var browser = await playwright.Chromium.LaunchPersistentContextAsync(profileDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Args = new[] { "--disable-blink-features=AutomationControlled" }
                });

            var page = browser.Pages.Count > 0 ? browser.Pages[0] : await browser.NewPageAsync();

            try
            {
                await scraper.LoginAsync(page, site.BaseUrl, ct);

                if (waitForUserLogin != null)
                {
                    // Lets a console app do Console.ReadLine(), or a GUI show a
                    // "Continue" button, without this class knowing which.
                    await waitForUserLogin(site.Name);
                }

                var batch = new List<CashbackRecord>();

                foreach (var (category, merchant) in merchants)
                {
                    ct.ThrowIfCancellationRequested();
                    Emit(RunEventLevel.Info, $"[{site.Name}] Scraping {merchant.Name}...");

                    try
                    {
                        var offers = await scraper.ScrapeMerchantAsync(page, merchant, ct);
                        foreach (var raw in offers)
                        {
                            batch.Add(new CashbackRecord
                            {
                                Site = site.Name,
                                Merchant = merchant.Name,
                                Category = string.IsNullOrEmpty(raw.Category) ? category.Name : raw.Category,
                                SubCategory = raw.SubCategory,
                                Rate = raw.Rate,
                                RateText = raw.RateText,
                                IsExclusive = raw.IsExclusive,
                                Countdown = raw.Countdown,
                                LoggedAt = DateTime.UtcNow
                            });
                        }

                        MerchantScraped?.Invoke(this, new MerchantScrapedEventArgs
                        {
                            Site = site.Name,
                            Merchant = merchant.Name,
                            OfferCount = offers.Count
                        });
                    }
                    catch (Exception ex)
                    {
                        Emit(RunEventLevel.Error, $"[{site.Name}] Failed to scrape {merchant.Name}: {ex.Message}");
                    }
                }

                _store.SaveBatch(batch);
                Emit(RunEventLevel.Info, $"{site.Name}: saved {batch.Count} record(s).");
            }
            finally
            {
                await browser.CloseAsync();
            }
        }
    }

    private void Emit(RunEventLevel level, string message)
        => Log?.Invoke(this, new RunEventArgs { Level = level, Message = message });
}
