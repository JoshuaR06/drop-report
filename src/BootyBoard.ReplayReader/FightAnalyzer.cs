namespace BootyBoard.Analysis;

/// <summary>One of Epic's elimination events (replay.Eliminations): complete, unlike the kill feed.</summary>
public sealed record Elim(float T, string Eliminator, string Eliminated, bool Knocked, int GunType);

/// <summary>What the kill feed adds for the same victim + state: damage tags, distance (m), bleed-out.</summary>
public sealed record FeedInfo(List<string> Tags, double? Dist, bool BledOut);

public sealed record WeaponRef(string Name, string Rarity);

/// <summary>
/// Knocks vs elims. Weapon credit, strongest evidence first: my last detailed hit on the victim (&lt;= 5 s before), then
/// for a bleed-out the weapon that knocked them, then the last non-consumable item I held, else Epic's category.
/// Epic's own elim count = ours minus finishes of ANOTHER team's knock and minus team-wipe credits (GunType 50).
/// </summary>
public static class FightAnalyzer
{
    public static List<FightEvent> Build(IReadOnlyList<Elim> elims, Func<string, string> who, Func<string, string> name,
                                         Func<Elim, FeedInfo> feed, Func<Elim, WeaponRef> lastHit, Func<Elim, WeaponRef> held)
    {
        var ordered = elims.OrderBy(e => e.T).ToList();
        var knockWeapon = new Dictionary<string, (WeaponRef w, string from)>();
        var res = new List<FightEvent>();
        foreach (var e in ordered)
        {
            var fk = feed(e) ?? new FeedInfo(new(), null, false);
            var by = who(e.Eliminator); var victim = who(e.Eliminated);
            var knockedBy = e.Knocked ? null : ordered.Where(y => y.Knocked && y.Eliminated == e.Eliminated && y.T <= e.T && y.T > e.T - 90)
                                                      .Select(y => who(y.Eliminator)).LastOrDefault();
            var next = e.Knocked ? ordered.FirstOrDefault(y => y.T > e.T && y.Eliminated == e.Eliminated) : null;
            string outcome = !e.Knocked ? null : next == null ? "no elim seen (revived or out of view)"
                           : next.Knocked ? "revived, knocked again" : $"finished by {who(next.Eliminator)}";
            WeaponRef w = null; string from = null;
            if (by == "me")
            {
                var hit = lastHit(e);
                if (hit != null) { w = hit; from = "last hit"; }
                else if (fk.BledOut && knockWeapon.TryGetValue(e.Eliminated ?? "", out var kw)) { w = kw.w; from = "knock"; }
                else if (held(e) is WeaponRef h) { w = h; from = "held"; }
                if (e.Knocked && w != null) knockWeapon[e.Eliminated ?? ""] = (w, from);
            }
            res.Add(new FightEvent(Math.Round(e.T, 1), e.Knocked ? "knock" : "elim", by, name(e.Eliminator), victim, name(e.Eliminated),
                                   Rules.Cause(e.GunType), e.GunType, w?.Name, w?.Rarity, from, fk.Tags, fk.Dist, knockedBy, outcome));
        }
        return res;
    }

    public static FightStats Summarize(List<FightEvent> fights, int? epicElims)
    {
        var mine = fights.Where(f => f.By == "me" && f.Victim is not ("me" or "teammate")).ToList();
        var deaths = fights.Where(f => f.Victim == "me").ToList();
        int C(Func<FightEvent, bool> p) => mine.Count(p);
        return new FightStats(
            C(f => f.Kind == "knock"), C(f => f.Kind == "elim"),
            C(f => f.Kind == "knock" && f.Victim == "player"), C(f => f.Kind == "elim" && f.Victim == "player"),
            C(f => f.Kind == "knock" && f.Victim is "bot" or "npc"), C(f => f.Kind == "elim" && f.Victim is "bot" or "npc"),   // AI: bots + NPC characters
            C(f => f.Kind == "knock" && f.Outcome == "finished by me"), C(f => f.Kind == "knock" && f.Outcome == "finished by teammate"),
            C(f => f.Kind == "knock" && f.Outcome is not ("finished by me" or "finished by teammate")),
            C(f => f.Kind == "elim" && f.KnockedBy == "teammate"), C(f => f.Kind == "elim" && f.KnockedBy == null),
            C(f => f.Kind == "elim" && f.KnockedBy is "player" or "bot" or "npc"), C(f => f.Kind == "elim" && f.GunType == 50),
            epicElims,
            mine.GroupBy(f => (f.Weapon ?? f.Cause, f.WeaponRarity)).Select(g => new WeaponFights(g.Key.Item1, g.Key.WeaponRarity, g.Count(f => f.Kind == "knock"), g.Count(f => f.Kind == "elim")))
                .OrderByDescending(g => g.Knocks + g.Elims).ToList(),
            mine, deaths);
    }
}
