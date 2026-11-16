using System.Net.Http.Headers;
using System.Text.Json;
using DropReport.Analysis;
using DropReport.App.ViewModels;

namespace DropReport.App.Services;

/// <summary>
/// The only things Drop Report fetches on its own (each behind a Settings switch): the latest release tag, and the
/// odds/Treasure Hours file. Both are plain GETs from GitHub with a generic user agent; nothing about you is sent.
/// </summary>
public static class Online
{
    private const string Repo = "JoshuaR06/drop-report";
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DropReport", AppInfo.Version));
        return h;
    }

    private static string OddsCache => Path.Combine(AppData.GameData, "treasure.json");

    /// <summary>At startup: use the last downloaded odds file, if any.</summary>
    public static void LoadCachedOdds()
    {
        try { if (File.Exists(OddsCache)) Treasure.Apply(JsonSerializer.Deserialize<Treasure.Data>(File.ReadAllText(OddsCache), Json.Options)); }
        catch (Exception e) { AppData.Log("odds cache: " + e.Message); }
    }

    /// <summary>Fetch data/treasure.json from the repo; apply and cache it if it is valid. True if the odds changed.</summary>
    public static async Task<bool> RefreshOddsAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"https://raw.githubusercontent.com/{Repo}/main/data/treasure.json");
            var d = JsonSerializer.Deserialize<Treasure.Data>(json, Json.Options);
            var before = Treasure.Source;
            if (!Treasure.Apply(d)) return false;
            File.WriteAllText(OddsCache, json);
            return Treasure.Source != before;
        }
        catch (Exception e) { AppData.Log("odds refresh: " + e.Message); return false; }   // offline, or the repo is still private
    }

    /// <summary>The newest release if it is newer than this build: (version, page url), else null.</summary>
    public static async Task<(string Version, string Url)?> CheckForUpdateAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            var url = doc.RootElement.GetProperty("html_url").GetString();
            return Version.TryParse(tag, out var latest) && Version.TryParse(AppInfo.Version, out var mine) && latest > mine ? (tag, url) : null;
        }
        catch (Exception e) { AppData.Log("update check: " + e.Message); return null; }
    }
}
