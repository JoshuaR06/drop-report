using System.Text.RegularExpressions;

namespace BootyBoard;

/// <summary>Small decoding rules, each verified against the game files or real replays (see docs/FINDINGS.md).</summary>
public static class Rules
{
    /// <summary>Game rarity tag (Rarity.X, also in kill-feed damage tags) -> the rarity word shown in game.</summary>
    public static string RarityFromTag(string tag) => tag switch
    {
        "Common" => "Common", "Uncommon" => "Uncommon", "Rare" => "Rare", "VeryRare" => "Epic", "SuperRare" => "Legendary",
        "Mythic" or "UltraRare" => "Mythic", "Transcendent" => "Exotic", null => null, _ => tag,
    };

    public static readonly string[] RarityOrder = { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic", "Exotic" };

    private static readonly (string token, string name)[] Suffixes = { ("UR", "Mythic"), ("SR", "Legendary"), ("VR", "Epic"), ("R", "Rare"), ("UC", "Uncommon"), ("C", "Common") };

    /// <summary>Rarity from an item id suffix (_VR...). Fallback only: wrong for 72 of 1,768 items vs the rarity tag.</summary>
    public static string SuffixRarity(string id) =>
        (id ?? "").Split('_').Reverse().Select(t => Suffixes.FirstOrDefault(r => r.token == t).name).FirstOrDefault(n => n != null);

    /// <summary>Chest class -> in-game name. "Chest"/"Sprite Chest" are DisplayNames in the game files; the rare chest's
    /// visuals component is "Rare Chest Visuals"; buried/extraction have no display name.</summary>
    public static string ChestLabel(string cls) => cls switch
    {
        "TrackedChest" or "AlwaysSpawnNormalChest" => "Chest",
        "AlwaysSpawnRareChest" => "Rare Chest",
        "RelicChest" => "Sprite Chest",
        "BuriedChest" => "Buried chest",
        "ExtractionChest" => "Extraction crate",
        _ => cls,
    };

    /// <summary>Treasure-season (v43) items by id, from the game's item table.</summary>
    public static string SeasonItem(string id) => id switch
    {
        null => null,
        "WID_TreasureKey" => "treasure key",
        _ when id.EndsWith("_Booty") => "treasure map",
        _ when id.StartsWith("WID_RelicFinder") => "sprite locator",
        _ when id.StartsWith("WID_RelicExtractor") || id.StartsWith("WID_ExtractionSiteBooster") || id == "WID_SmugglerExtractionKey_Gizmo" => "extraction",
        _ when id.Contains("VaultKey") || id.Contains("Keycard") || id.Contains("RelicVault") => "vault key",
        _ => null,
    };

    /// <summary>Epic's elimination GunType -> category. 2 also covers the slingshot; 50 = downed players eliminated when
    /// their last teammate dies, credited to whoever knocked them.</summary>
    public static string Cause(int gunType) => gunType switch
    {
        1 => "fall", 2 => "pistol", 3 => "shotgun", 4 => "rifle", 5 => "SMG", 6 => "sniper", 8 => "melee", 10 => "grenade",
        13 => "rocket", 15 => "bow", 17 => "bled out", 50 => "team wiped", _ => $"cause {gunType}",
    };

    public static string ModeOf(string playlist)
    {
        playlist ??= "";
        return playlist.Contains("Solo") ? "Solo" : playlist.Contains("Duo") ? "Duo" : playlist.Contains("Trio") ? "Trio"
             : playlist.Contains("Squad") ? "Squad" : "Other";
    }

    /// <summary>"++Fortnite+Release-43.10" -> 43.</summary>
    public static int SeasonOf(string branch)
    {
        var m = Regex.Match(branch ?? "", @"Release-(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>Replay file names carry the local start time: UnsavedReplay-2026.11.15-14.39.46.replay.</summary>
    public static DateTime? StartFromFileName(string file)
    {
        var m = Regex.Match(Path.GetFileName(file ?? ""), @"(\d{4})\.(\d\d)\.(\d\d)-(\d\d)\.(\d\d)\.(\d\d)");
        if (!m.Success) return null;
        int G(int i) => int.Parse(m.Groups[i].Value);
        try { return new DateTime(G(1), G(2), G(3), G(4), G(5), G(6), DateTimeKind.Local); } catch { return null; }
    }

    /// <summary>Who someone is: bots carry a BotId; NPC characters (bosses like Buggy) are bots without one.</summary>
    public static string Kind(bool isBot, string botId) => !isBot ? "player" : string.IsNullOrEmpty(botId) ? "npc" : "bot";

    /// <summary>
    /// The name to show. Players in anonymous mode chose to hide their name, and the replay carries no anonymous tag for
    /// them (checked over 677 players), so they show as "Anonymous player" rather than the account name the replay leaks.
    /// </summary>
    public static string DisplayName(string name, bool isBot, bool? anonymous, string streamerName, string nameOverride, string botId)
    {
        if (!isBot && anonymous == true) return string.IsNullOrWhiteSpace(streamerName) ? "Anonymous player" : streamerName;
        if (!string.IsNullOrWhiteSpace(streamerName)) return streamerName;
        return !string.IsNullOrWhiteSpace(name) ? name : !string.IsNullOrWhiteSpace(nameOverride) ? nameOverride : botId ?? "?";
    }

    public static string StripRarity(string label) => Regex.Replace(label ?? "", @"\s*\[\w+\]$", "");
}
