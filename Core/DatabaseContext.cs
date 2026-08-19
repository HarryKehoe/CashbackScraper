using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CashbackScraper.Core;

/// <summary>
/// Thin, dependency-light SQLite wrapper. Creates the schema on first use
/// and saves batches of CashbackRecord in a single transaction.
///
/// Rows are upserted on (Site, Merchant, Category, SubCategory) so re-running
/// the scraper updates rates instead of accumulating duplicate rows forever.
/// See the note at the bottom of this file if you'd rather keep full history.
/// </summary>
public class DatabaseContext
{
    private readonly string _connectionString;

    public DatabaseContext(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS CashbackRecords (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Site         TEXT NOT NULL,
                Merchant     TEXT NOT NULL,
                Category     TEXT NOT NULL,
                SubCategory  TEXT NOT NULL,
                Rate         DECIMAL NOT NULL,
                RateText     TEXT NOT NULL,
                IsExclusive  INTEGER NOT NULL,
                Countdown    TEXT NOT NULL,
                LoggedAt     TEXT NOT NULL,
                UNIQUE(Site, Merchant, Category, SubCategory)
            );

            CREATE INDEX IF NOT EXISTS IX_CashbackRecords_Merchant
                ON CashbackRecords (Site, Merchant);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts new (Site, Merchant, Category, SubCategory) combos, and
    /// updates the rate/countdown/timestamp on ones that already exist.
    /// </summary>
    public void SaveBatch(List<CashbackRecord> records)
    {
        if (records == null || records.Count == 0) return;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO CashbackRecords
                (Site, Merchant, Category, SubCategory, Rate, RateText, IsExclusive, Countdown, LoggedAt)
            VALUES
                ($site, $merchant, $category, $subCategory, $rate, $rateText, $isExclusive, $countdown, $loggedAt)
            ON CONFLICT(Site, Merchant, Category, SubCategory) DO UPDATE SET
                Rate        = excluded.Rate,
                RateText    = excluded.RateText,
                IsExclusive = excluded.IsExclusive,
                Countdown   = excluded.Countdown,
                LoggedAt    = excluded.LoggedAt;
        ";

        var pSite = cmd.CreateParameter(); pSite.ParameterName = "$site"; cmd.Parameters.Add(pSite);
        var pMerchant = cmd.CreateParameter(); pMerchant.ParameterName = "$merchant"; cmd.Parameters.Add(pMerchant);
        var pCategory = cmd.CreateParameter(); pCategory.ParameterName = "$category"; cmd.Parameters.Add(pCategory);
        var pSubCategory = cmd.CreateParameter(); pSubCategory.ParameterName = "$subCategory"; cmd.Parameters.Add(pSubCategory);
        var pRate = cmd.CreateParameter(); pRate.ParameterName = "$rate"; cmd.Parameters.Add(pRate);
        var pRateText = cmd.CreateParameter(); pRateText.ParameterName = "$rateText"; cmd.Parameters.Add(pRateText);
        var pIsExclusive = cmd.CreateParameter(); pIsExclusive.ParameterName = "$isExclusive"; cmd.Parameters.Add(pIsExclusive);
        var pCountdown = cmd.CreateParameter(); pCountdown.ParameterName = "$countdown"; cmd.Parameters.Add(pCountdown);
        var pLoggedAt = cmd.CreateParameter(); pLoggedAt.ParameterName = "$loggedAt"; cmd.Parameters.Add(pLoggedAt);

        foreach (var r in records)
        {
            pSite.Value = r.Site;
            pMerchant.Value = r.Merchant;
            pCategory.Value = r.Category;
            pSubCategory.Value = r.SubCategory;
            pRate.Value = r.Rate;
            pRateText.Value = r.RateText;
            pIsExclusive.Value = r.IsExclusive ? 1 : 0;
            pCountdown.Value = r.Countdown;
            pLoggedAt.Value = r.LoggedAt.ToString("O"); // ISO 8601, sortable as text

            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Fetch everything currently stored, most recently logged first.</summary>
    public List<CashbackRecord> GetAll()
    {
        var results = new List<CashbackRecord>();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Site, Merchant, Category, SubCategory, Rate, RateText, IsExclusive, Countdown, LoggedAt
            FROM CashbackRecords
            ORDER BY LoggedAt DESC;
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CashbackRecord
            {
                Site = reader.GetString(0),
                Merchant = reader.GetString(1),
                Category = reader.GetString(2),
                SubCategory = reader.GetString(3),
                Rate = reader.GetDecimal(4),
                RateText = reader.GetString(5),
                IsExclusive = reader.GetInt32(6) != 0,
                Countdown = reader.GetString(7),
                LoggedAt = DateTime.Parse(reader.GetString(8))
            });
        }

        return results;
    }
}

// NOTE ON HISTORY:
// This schema keeps one row per (Site, Merchant, Category, SubCategory) and
// overwrites it on every run - good for "what's the current rate" queries,
// bad if you want to chart rate changes over time. If you want full history
// instead, drop the UNIQUE constraint (and the ON CONFLICT clause becomes a
// plain INSERT), and read the data back with a "latest per merchant" query
// using MAX(LoggedAt) grouped by (Site, Merchant, Category, SubCategory).
// Say the word and I'll switch it over.
