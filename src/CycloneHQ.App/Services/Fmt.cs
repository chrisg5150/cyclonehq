using System.Globalization;
using CycloneHQ.Core.Models;

namespace CycloneHQ.App.Services;

// Small display helpers shared by the pages.
public static class Fmt
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string Pts(double v) => v.ToString("0.0", C);
    public static string Signed(double v, string format = "0.0") => (v > 0 ? "+" : v < 0 ? "−" : "±") + Math.Abs(v).ToString(format, C);

    // Shares can arrive as 0-1 fractions or 0-100 percents.
    public static string Pct(double? v) => v is null ? "—" : ((v <= 1.0 ? v * 100 : v)!.Value).ToString("0", C) + "%";

    public static string Rank(int? r, string pos) => r is null ? "—" : $"{pos}{r}";

    public static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    public static string ShortName(string name)
    {
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Length > 0 ? $"{parts[0][0]}. {parts[1]}" : name;
    }

    public static string Ago(DateTimeOffset when)
    {
        var d = DateTimeOffset.Now - when;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} hr ago";
        return when.ToLocalTime().ToString("MMM d", C);
    }

    public static string Clock(int seconds) => seconds <= 0 ? "Done" : $"{seconds / 60}:{seconds % 60:00} left";

    public static string FlagLabel(string flag) => flag switch
    {
        PlayerFlags.Underperforming => "Underperforming",
        PlayerFlags.Underutilized => "Underused",
        PlayerFlags.BuyLow => "Buy low",
        PlayerFlags.SellHigh => "Sell high",
        PlayerFlags.Breakout => "Breakout",
        PlayerFlags.Injured => "Injured",
        PlayerFlags.OnBye => "Bye",
        _ => flag,
    };

    // Warning flags sort first on the Home screen.
    public static int FlagWeight(string flag) => flag switch
    {
        PlayerFlags.Injured => 0, PlayerFlags.Underperforming => 1, PlayerFlags.Underutilized => 2,
        PlayerFlags.SellHigh => 3, PlayerFlags.OnBye => 4, PlayerFlags.BuyLow => 5, PlayerFlags.Breakout => 6, _ => 9,
    };

    public static string VerdictClass(string verdict) => verdict.ToLowerInvariant() switch
    {
        "start" => "v-start", "buy low" => "v-buy", "hold" => "v-hold", "bench" => "v-bench",
        "sell" => "v-sell", "drop" => "v-drop", _ => "v-hold",
    };
}
