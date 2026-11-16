using DropReport.Replay;
using Unreal.Core.Models;

namespace DropReport.Analysis;

/// <summary>
/// Loot per chest. A chest's items are pickups with bTossedFromContainer, all arriving in the same packet within ~0.05 s
/// of the search. Cluster tossed pickups by first-seen time (gap &lt; 0.03 s) in -0.4..+0.8 s around the search and keep
/// clusters with any item within 7 m of the searcher; if no item in a cluster has a position, accept it only within
/// 0.15 s. A freshly tossed pickup's first position decodes at 1/10 scale, so distance is tried at x1 and x10.
/// </summary>
public static class LootMatcher
{
    public sealed record Match(List<PickupState> Items, double? Cx, double? Cy);

    public static Match For(float t, double x, double y, IReadOnlyList<PickupState> pickups, HashSet<PickupState> used)
    {
        var cand = pickups.Where(p => p.Tossed && !used.Contains(p) && p.FirstSeen >= t - 0.4 && p.FirstSeen <= t + 0.8).OrderBy(p => p.FirstSeen).ToList();
        var clusters = new List<List<PickupState>>();
        foreach (var p in cand)
        {
            if (clusters.Count > 0 && p.FirstSeen - clusters[^1][^1].FirstSeen < 0.03) clusters[^1].Add(p);
            else clusters.Add(new() { p });
        }
        var nearHits = new List<(PickupState p, FVector pos)>();
        var timeHits = new List<(PickupState p, FVector pos)>();
        foreach (var cl in clusters)
        {
            var withPos = cl.Select(p => (p, all: Positions(p).ToList())).ToList();
            var near = withPos.Select(q => (q.p, pos: q.all.Select(v => Near(v, x, y)).FirstOrDefault(v => v != null))).ToList();
            if (near.Any(q => q.pos != null)) nearHits.AddRange(near);     // the whole packet, incl. items without a position
            else if (withPos.All(q => q.all.Count == 0) && Math.Abs(cl[0].FirstSeen - t) <= 0.15) timeHits.AddRange(cl.Select(p => (p, (FVector)null)));
        }
        var hits = nearHits.Count > 0 ? nearHits : timeHits;
        foreach (var h in hits) used.Add(h.p);
        var hp = hits.Where(h => h.pos != null).ToList();
        return new Match(hits.OrderBy(h => h.p.FirstSeen).Select(h => h.p).ToList(),
                         hp.Count > 0 ? Math.Round(hp.Average(h => h.pos.X)) : null,
                         hp.Count > 0 ? Math.Round(hp.Average(h => h.pos.Y)) : null);
    }

    private static IEnumerable<FVector> Positions(PickupState p) =>
        new[] { p.InitPos, p.FirstPos, p.RestPos, p.FinalPos, p.LastPos }.Where(v => v != null && !(v.X == 0 && v.Y == 0));

    private static FVector Near(FVector v, double x, double y)
    {
        if (v == null) return null;
        foreach (var k in new[] { 1f, 10f })
            if (Math.Sqrt(Math.Pow(v.X * k - x, 2) + Math.Pow(v.Y * k - y, 2)) < 700) return new FVector(v.X * k, v.Y * k, v.Z * k);
        return null;
    }
}

/// <summary>
/// Weapon actor -> real name. The replay names the weapon actor's class (B_..._C); each item definition says which class
/// it spawns, so the class narrows it to a few items (rarities + variants like "Enhanced"). The exact item is my pickup
/// whose id is one of those items, within 3 s of the weapon actor appearing, else my latest earlier pickup of one.
/// </summary>
public static class WeaponNames
{
    public sealed record Info(string Class, string Name, string Rarity, string How);

    public static Info Resolve(string actorClass, float opened, IReadOnlyList<PickupState> myPickups, ItemTable names)
    {
        var cls = actorClass?.Split('.').Last() ?? "?";
        if (actorClass == null) return new(cls, "Unknown weapon", null, "no class");
        if (actorClass.Contains("Pickaxe", StringComparison.OrdinalIgnoreCase) || actorClass.Contains("/FORT_Melee/")) return new(cls, "Pickaxe", null, "melee");
        var cands = names.ByActorClass.GetValueOrDefault(cls) ?? new();
        var ids = cands.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pick = myPickups.Where(p => ids.Contains(p.Item) && !float.IsNaN(opened) && p.PickedAt >= opened - 3 && p.PickedAt <= opened + 1)
                            .OrderBy(p => Math.Abs(p.PickedAt.Value - opened)).FirstOrDefault()
                ?? myPickups.Where(p => ids.Contains(p.Item) && !float.IsNaN(opened) && p.PickedAt <= opened + 1).OrderByDescending(p => p.PickedAt).FirstOrDefault();
        if (pick != null)
        {
            var li = names.Lookup(pick.Item, null);
            return new(cls, li.Name, li.Rarity, $"pickup {pick.Item}");
        }
        var byName = cands.Where(c => !string.IsNullOrWhiteSpace(c.Name)).GroupBy(c => c.Name).OrderByDescending(g => g.Count()).ToList();
        if (byName.Count == 1) return new(cls, byName[0].Key, cands.Select(c => c.Rarity).Distinct().Count() == 1 ? cands[0].Rarity : null, "only item for this class");
        if (byName.Count > 1) return new(cls, byName[0].Key + " (or variant)", null, "no pickup; class shared by " + string.Join(" / ", byName.Select(g => g.Key)));
        return new(cls, System.Text.RegularExpressions.Regex.Replace(cls, "^B_|_C$", "") + " (code name)", null, "class not in the item table");
    }
}
