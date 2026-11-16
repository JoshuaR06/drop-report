using System.Text.Json;

namespace BootyBoard;

/// <summary>
/// Real in-game names, from Fortnite's own item definitions (built by BootyBoard.GameData from the installed game files:
/// ItemName + the Rarity.* gameplay tag + WeaponActorClass). Unknown ids keep their code name, marked as such.
/// </summary>
public sealed class ItemTable
{
    public sealed record Item(string Id, string Name, string Rarity, string RarityTag, string Type, string Actor, List<string> Tags);

    public Dictionary<string, Item> ById { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Keyed by bare class name (B_..._C): the replay's guid cache has no package path.</summary>
    public Dictionary<string, List<Item>> ByActorClass { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Build { get; private set; }
    public bool IsEmpty => ById.Count == 0;

    public static ItemTable Empty { get; } = new();

    public static ItemTable Load(string path)
    {
        var t = new ItemTable();
        if (!File.Exists(path)) return t;
        var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        t.Build = doc.TryGetProperty("build", out var b) ? b.GetString() : null;
        foreach (var e in doc.GetProperty("items").EnumerateArray())
        {
            string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var tag = S("rarityTag");
            var tags = e.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array ? tg.EnumerateArray().Select(x => x.GetString()).ToList() : new List<string>();
            t.Add(new Item(S("id"), S("name"), tag != null ? Rules.RarityFromTag(tag) : S("rarity"), tag, S("type"), S("actor"), tags));
        }
        return t;
    }

    public void Add(Item it)
    {
        if (it.Id == null) return;
        ById[it.Id] = it;
        if (it.Actor != null)
        {
            var key = it.Actor.Split('.').Last();
            if (!ByActorClass.TryGetValue(key, out var l)) ByActorClass[key] = l = new();
            l.Add(it);
        }
    }

    public LootItem Lookup(string id, string takenBy)
    {
        id ??= "?";
        if (ById.TryGetValue(id, out var it) && !string.IsNullOrWhiteSpace(it.Name))
            return new LootItem(id, it.Name, Kind(it), it.Rarity ?? Rules.SuffixRarity(id), takenBy, true);
        return new LootItem(id, id + " (code name)", "Item", Rules.SuffixRarity(id), takenBy, false);
    }

    public static string Kind(Item it)
    {
        if (it.Type?.Contains("Ammo") == true) return "Ammo";
        if (it.Type?.Contains("Resource") == true) return "Material";
        var t = it.Tags.FirstOrDefault(x => x.StartsWith("Weapon.Ranged.") || x.StartsWith("Weapon.Melee"));
        if (t == null) return "Item";
        var p = t.Split('.');
        var k = p.Length > 2 ? p[2] : p[1];
        return k switch { "Assault" => "Assault Rifle", "Shotgun" => "Shotgun", "SMG" => "SMG", "Pistol" => "Pistol", "Sniper" => "Sniper", "Melee" => "Melee", _ => k };
    }
}
