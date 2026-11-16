using System.Text.Json;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace BootyBoard.GameData;

/// <summary>The installed game, found through the Epic Games Launcher's install manifests.</summary>
public sealed record GameInstall(string Folder, string Version)
{
    public string PaksDir => Path.Combine(Folder, "FortniteGame", "Content", "Paks");
    /// <summary>"++Fortnite+Release-43.10-CL-60118918-Windows" -> "++Fortnite+Release-43.10-CL-60118918".</summary>
    public string Build => Version?.Replace("-Windows", "");

    public static GameInstall Find(string manifestsDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests")
    {
        if (!Directory.Exists(manifestsDir)) return null;
        foreach (var f in Directory.GetFiles(manifestsDir, "*.item"))
        {
            try
            {
                var j = JsonDocument.Parse(File.ReadAllText(f)).RootElement;
                if (j.TryGetProperty("AppName", out var app) && app.GetString() == "Fortnite" ||
                    j.TryGetProperty("DisplayName", out var dn) && dn.GetString() == "Fortnite")
                {
                    var folder = j.GetProperty("InstallLocation").GetString();
                    if (Directory.Exists(Path.Combine(folder, "FortniteGame", "Content", "Paks")))
                        return new GameInstall(folder, j.TryGetProperty("AppVersionString", out var v) ? v.GetString() : null);
                }
            }
            catch { }
        }
        return null;
    }
}

/// <summary>A file the builder needs from the internet; shown to the user before anything is fetched.</summary>
public sealed record Download(string What, string Source, string ApproxSize);

/// <summary>
/// Builds the real-name item table from the game's own item definitions. Reads the game's files on disk, read-only;
/// never touches the running game. Needs three downloads (listed by <see cref="Downloads"/>), fetched only after the
/// user agrees.
/// </summary>
public sealed class GameDataBuilder
{
    private readonly string _dir, _deps;
    private static readonly HttpClient Http = CreateHttp();

    public GameDataBuilder(string gameDataDir)
    {
        _dir = gameDataDir; _deps = Path.Combine(gameDataDir, "deps");
    }

    public string ItemTablePath => Path.Combine(_dir, "itemnames.json");

    public static IReadOnlyList<Download> Downloads { get; } = new[]
    {
        new Download("Decryption key for your game version (public)", "fortnite-api.com/v2/aes", "~6 KB"),
        new Download("Field layout (mappings) for your game version", "uedb.dev (data.uedb.dev)", "~3 MB"),
        new Download("Oodle decompression library (community build used by FModel)", "github.com/WorkingRobot/OodleUE", "~1 MB"),
    };

    /// <summary>Build of the current table, or null if none.</summary>
    public string CurrentBuild()
    {
        try { return File.Exists(ItemTablePath) ? JsonDocument.Parse(File.ReadAllText(ItemTablePath)).RootElement.GetProperty("build").GetString() : null; }
        catch { return null; }
    }

    public bool IsUpToDate(GameInstall game) => game?.Build != null && CurrentBuild() is string b && game.Build.StartsWith(b);

    public async Task<int> BuildAsync(GameInstall game, IProgress<string> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_deps);
        // 1) AES key; must be for exactly this game version or nothing decrypts
        var cachedAes = Path.Combine(_deps, "aes.json");
        string aesJson = null;
        if (File.Exists(cachedAes))
        {
            var cb = JsonDocument.Parse(File.ReadAllText(cachedAes)).RootElement.GetProperty("data").GetProperty("build").GetString();
            if (game.Build != null && game.Build.StartsWith(cb)) aesJson = File.ReadAllText(cachedAes);   // same game version: reuse
        }
        if (aesJson == null)
        {
            progress?.Report("Fetching the decryption key...");
            aesJson = await Http.GetStringAsync("https://fortnite-api.com/v2/aes", ct);
        }
        var aes = JsonDocument.Parse(aesJson).RootElement.GetProperty("data");
        var keyBuild = aes.GetProperty("build").GetString();
        if (game.Build != null && !game.Build.StartsWith(keyBuild))
            throw new InvalidOperationException($"The public key is for {keyBuild}, but your game is {game.Build}. Try again after the key sites update (usually within a few hours of a patch).");
        File.WriteAllText(Path.Combine(_deps, "aes.json"), aesJson);

        // 2) mappings for this build
        var usmap = Path.Combine(_deps, Sanitize(keyBuild) + "_br.usmap");
        if (!File.Exists(usmap))
        {
            progress?.Report("Fetching the mappings file...");
            var listing = JsonDocument.Parse(await Http.GetStringAsync("https://uedb.dev/svc/api/v1/fortnite/mappings", ct)).RootElement;
            var mapBuild = listing.GetProperty("version").GetString();
            if (!keyBuild.StartsWith(mapBuild) && !mapBuild.StartsWith(keyBuild))
                throw new InvalidOperationException($"The mappings are for {mapBuild}, the key for {keyBuild}. Try again later.");
            var url = listing.GetProperty("mappings").GetProperty("Brotli").GetString();
            await using var s = await Http.GetStreamAsync(url, ct);
            await using var fs = File.Create(usmap + ".part");
            await s.CopyToAsync(fs, ct);
            fs.Close(); File.Move(usmap + ".part", usmap, true);
        }

        // 3) Oodle
        var oodle = Path.Combine(_deps, OodleHelper.OodleFileName);
        if (!File.Exists(oodle)) { progress?.Report("Fetching the Oodle library..."); var op = oodle; OodleHelper.DownloadOodleDll(ref op); oodle = op; }
        OodleHelper.Initialize(oodle);

        // 4) read the item definitions
        progress?.Report("Reading your game files (about a minute)...");
        var count = await Task.Run(() => Extract(game.PaksDir, aes, usmap, keyBuild, progress, ct), ct);
        progress?.Report($"Done: {count:N0} items named.");
        return count;
    }

    private int Extract(string paks, JsonElement aes, string usmap, string build, IProgress<string> progress, CancellationToken ct)
    {
        using var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE5_LATEST), StringComparer.OrdinalIgnoreCase);
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(usmap);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey(aes.GetProperty("mainKey").GetString()));
        foreach (var k in aes.GetProperty("dynamicKeys").EnumerateArray())
            try { provider.SubmitKey(new FGuid(k.GetProperty("pakGuid").GetString()), new FAesKey(k.GetProperty("key").GetString())); } catch { }
        provider.PostMount();
        provider.LoadLocalization(ELanguage.English);

        // item definitions: WID_ (weapons + most items), AGID_ (gadgets), Athena_* consumables, ammo, resources
        static bool Wanted(string n) => n.StartsWith("WID_", StringComparison.OrdinalIgnoreCase) || n.StartsWith("AGID_", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Athena_", StringComparison.OrdinalIgnoreCase) || n.Contains("AmmoData", StringComparison.OrdinalIgnoreCase) || n.EndsWith("ItemData", StringComparison.OrdinalIgnoreCase);
        var cands = provider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) && Wanted(Path.GetFileNameWithoutExtension(k))).ToList();
        var items = new List<object>();
        for (var i = 0; i < cands.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i % 500 == 0) progress?.Report($"Reading your game files... {i:N0} / {cands.Count:N0}");
            var path = cands[i]; var id = Path.GetFileNameWithoutExtension(path);
            try
            {
                var pkg = provider.LoadPackage(path);
                var obj = pkg.GetExports().FirstOrDefault(e => e.Name.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? pkg.GetExports().FirstOrDefault();
                if (obj == null) continue;
                var cls = obj.Class?.Name.Text ?? obj.ExportType;
                if (!cls.Contains("ItemDefinition", StringComparison.OrdinalIgnoreCase) && !cls.Contains("ItemData", StringComparison.OrdinalIgnoreCase) && !cls.Contains("Ammo", StringComparison.OrdinalIgnoreCase)) continue;
                // the name is ItemName (not DisplayName); rarity + kind are gameplay tags in the DataList fragments
                string name = obj.TryGetValue(out FText dn, "ItemName") ? dn.Text : obj.TryGetValue(out FText dn2, "DisplayName") ? dn2.Text : null;
                var tags = new List<string>();
                if (obj.TryGetValue(out FInstancedStruct[] dl, "DataList"))
                    foreach (var s in dl)
                        if (s.NonConstStruct is FStructFallback fb)
                            foreach (var q in fb.Properties.Where(q => q.Name.Text == "Tags"))
                                tags.AddRange((q.Tag?.GenericValue?.ToString() ?? "").Replace(" (FGameplayTagContainer)", "").Split(", ", StringSplitOptions.RemoveEmptyEntries));
                var rTag = tags.FirstOrDefault(t => t.StartsWith("Rarity."))?[7..];
                string desc = obj.TryGetValue(out FText ds, "ItemDescription") ? ds.Text : null;
                string actor = obj.TryGetValue(out FSoftObjectPath wac, "WeaponActorClass") ? wac.AssetPathName.Text : null;
                items.Add(new { id, name, rarityTag = rTag, type = cls, actor, description = desc, tags = tags.Where(t => !t.StartsWith("Rarity.")).Distinct().ToList() });
            }
            catch { }
        }
        var tmp = ItemTablePath + ".part";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new { build, generated = DateTime.Now, items }));
        File.Move(tmp, ItemTablePath, true);
        return items.Count;
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '+' ? '_' : c));

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("BootyBoard/1.0");   // generic client name, nothing about the user
        return h;
    }
}
