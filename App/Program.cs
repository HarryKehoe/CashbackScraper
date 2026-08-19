using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CashbackScraper.Core;
using CashbackScraper.Scraping;
using CashbackScraper.Sites;

namespace CashbackScraper.App;

/// <summary>
/// Thin adapter so the orchestrator (which only knows about ICashbackStore)
/// doesn't need to reference DatabaseContext/SQLite directly.
/// </summary>
public class SqliteCashbackStore : ICashbackStore
{
    private readonly DatabaseContext _db;

    public SqliteCashbackStore(string dbPath) => _db = new DatabaseContext(dbPath);

    public void SaveBatch(List<CashbackRecord> records) => _db.SaveBatch(records);
}

public class Program
{
    public static async Task Main()
    {
        // baseDir is bin/Debug/net8.0 while running via `dotnet run`; three levels
        // up gets back to the project root (where the .csproj lives).
        string baseDir = AppContext.BaseDirectory;
        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));

        string configPath = Path.Combine(projectRoot, "config", "sites.json");
        var config = RootConfig.Load(configPath);

        string dbPath = Path.Combine(projectRoot, "cashback_history.db");
        var store = new SqliteCashbackStore(dbPath);

        var scrapers = new List<ISiteScraper>
        {
            new TopCashbackScraper(),
            new RakutenScraper(),
            new QuidcoScraper()
        };

        var orchestrator = new ScraperOrchestrator(config, store, scrapers);

        orchestrator.Log += (_, e) => Console.WriteLine($"[{e.Level}] {e.Message}");
        orchestrator.MerchantScraped += (_, e) =>
            Console.WriteLine($"    -> {e.Site} / {e.Merchant}: {e.OfferCount} offer(s)");

        await orchestrator.RunAsync(waitForUserLogin: async siteName =>
        {
            Console.WriteLine("=================================================");
            Console.WriteLine($"Please complete login/2FA/captcha for {siteName} in the browser window.");
            Console.WriteLine("Press ENTER here when ready to run automated extraction.");
            Console.WriteLine("=================================================");
            Console.ReadLine();
            await Task.CompletedTask;
        });

        Console.WriteLine("\nDone.");
    }
}
