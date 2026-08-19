using System.Globalization;
using System.Text.RegularExpressions;

namespace CashbackScraper.Scraping;

public static class RateTextParser
{
    /// <summary>
    /// Pulls the first "N" or "N.N" out of a string like "5% Cashback",
    /// "Up to 15.75% Cashback", "10%" etc. Returns 0 if nothing found.
    /// </summary>
    public static decimal ParsePercentage(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0m;
        var match = Regex.Match(input, @"(\d+(\.\d+)?)\s*%");
        if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate))
            return rate;
        return 0m;
    }
}