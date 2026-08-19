# CashbackScraper scaffold

## Layout
```
Core/CashbackRecord.cs        - persisted row model (+ RawOfferResult DTO)
Scraping/Config.cs            - sites.json models (MerchantTarget, ScrapeCategory, SiteConfig, RootConfig)
Scraping/ISiteScraper.cs      - the interface every site plugs into
Scraping/ScraperOrchestrator.cs - drives browser sessions, login pause, scraping, saving
Sites/TopCashbackScraper.cs   - real implementation, ported from your original Program.cs
Sites/RakutenScraper.cs       - stub, needs real selectors
Sites/QuidcoScraper.cs        - stub, needs real selectors
config/sites.json             - merchant/category list per site, toggle "enabled" freely
App/Program.cs                - console entry point wiring it all together
```

## Adding a new site
1. Implement `ISiteScraper` in `Sites/YourSiteScraper.cs` (copy TopCashbackScraper's
   shape — `LoginAsync` just navigates to the base URL, `ScrapeMerchantAsync` does
   the `page.EvaluateAsync` DOM scraping and returns `List<RawOfferResult>`).
2. Register it in `App/Program.cs`'s `scrapers` list.
3. Add a `{ "name": "YourSite", ... }` block to `config/sites.json` with its
   merchants/categories.

To get the selectors right for Rakuten/Quidco, send me the HTML around a
merchant's rate/category listing (view-source, or right-click → Inspect →
copy the outer HTML of the offers block) the same way the original
TopCashback markup implied `.merch-rate-card` / `.merch-cat__*` classes.

## Adding/removing categories or merchants
All in `config/sites.json` — no recompiling needed:
- Toggle a whole category off: `"enabled": false` on the category.
- Toggle one merchant off: `"enabled": false` on that merchant.
- "Scrape everything": leave everything `enabled: true`.
- Add a merchant: append `{ "name": ..., "url": ..., "enabled": true }` to a
  category's `merchants` array. Add a whole new category the same way.

## Database
`Core/DatabaseContext.cs` is a from-scratch SQLite wrapper (using
`Microsoft.Data.Sqlite`, no native SQLite install needed). It creates the
`CashbackRecords` table on first run at `cashback_history.db` in the project
root, and **upserts** on `(Site, Merchant, Category, SubCategory)` — so
re-running the scraper updates existing rows instead of piling up duplicates
every time. `LoggedAt` always reflects the last time that row was seen.

If you'd rather keep full history (e.g. to chart how a rate changes over
time) instead of "latest known rate per merchant", see the note at the
bottom of `DatabaseContext.cs` — it's a small schema change.

## Running
```
dotnet restore
dotnet build
pwsh bin/Debug/net8.0/playwright.ps1 install   # first time only, installs Chromium
dotnet run
```
A visible Chromium window opens per enabled site; log in / solve captcha,
press Enter in the console, and it scrapes every enabled merchant for that
site before moving to the next site. `cashback_history.db` and
`playwright_profiles/` are created in the project root on first run.

## GUI later
The orchestrator only talks to the UI through:
- `Log` event (info/warning/error messages)
- `MerchantScraped` event (progress per merchant)
- `waitForUserLogin` callback (you decide how "ready to continue" is signalled)

None of `Scraping/*` or `Sites/*` reference `Console` directly, so a WPF/Avalonia
GUI can subscribe to those events and call `RunAsync` from a button handler
without touching the scraping logic at all.

## Known gaps / things I made assumptions about
- Rakuten/Quidco scrapers are stubs pending their HTML.
- The DB upserts by default (latest rate per merchant/category, not full
  history) — say the word if you want full history instead.
