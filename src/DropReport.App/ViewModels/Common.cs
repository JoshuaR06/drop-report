using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DropReport.App.ViewModels;

public abstract class ViewModelBase : ObservableObject { }

public sealed record Tile(string Label, string Value, string Note = null);

/// <summary>One column in a chart: one value per series (stacked), plus the hover text.</summary>
public sealed record ColumnItem(string Label, double[] Values, string Tip);

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public static class Fmt
{
    public static string N(double? v) => v == null ? "–" : Math.Round(v.Value).ToString("N0", CultureInfo.CurrentCulture);
    public static string N1(double? v) => v == null ? "–" : v.Value.ToString("0.0", CultureInfo.CurrentCulture);
    public static string Pct(double? v) => v == null ? "–" : Math.Round(v.Value) + "%";
    public static string Pct(int a, int b) => b > 0 ? Math.Round(100.0 * a / b) + "%" : "–";
    public static string When(DateTime? d) => d == null ? "–" : d.Value.ToString("MMM d  h:mm tt", CultureInfo.CurrentCulture);
    public static string Day(DateTime? d) => d == null ? "" : d.Value.ToString("MMM d", CultureInfo.CurrentCulture);
    public static string Clock(double s) { s = Math.Max(0, s); return $"{(int)(s / 60)}:{(int)(s % 60):00}"; }
}

/// <summary>Rarity word -> rarity brush, or the accent when there is no rarity (pick cards, spots).</summary>
public sealed class RarityOrAccentConverter : IValueConverter
{
    public static readonly RarityOrAccentConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string r) return RarityBrushConverter.Brush(r);
        var app = Application.Current;
        return app != null && app.TryGetResource("AccentBrush", app.ActualThemeVariant, out var b) && b is IBrush br ? br : Brushes.Orange;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Rarity word -> the theme's rarity brush.</summary>
public sealed class RarityBrushConverter : IValueConverter
{
    public static readonly RarityBrushConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Brush(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public static IBrush Brush(string rarity)
    {
        var app = Application.Current;
        if (rarity == null) return Brushes.Transparent;
        return app != null && app.TryGetResource("Rarity" + rarity, app.ActualThemeVariant, out var b) && b is IBrush br ? br : Brushes.Gray;
    }
}

public static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    /// <summary>Donation and repository links; the About page hides a link while it is empty.</summary>
    public const string DonationUrl = "https://github.com/sponsors/JoshuaR06";
    public const string RepoUrl = "https://github.com/JoshuaR06/drop-report";
    public const string EpicDisclaimer = "Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic. This material is not official and is not endorsed by Epic.";
}
