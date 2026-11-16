using DropReport.Replay;
using FortniteReplayReader.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DropReport.Analysis;

/// <summary>Reads one replay and produces the match summary + map data. Read-only on the replay file.</summary>
public static class MatchAnalyzer
{
    /// <summary>Bump when the summary gains data: the app re-reads older summaries on its next scan.
    /// 2 = chest refills + landing spot. 4 = re-read after a reverted landmark filter (3). 5 = elimination credit follows the knock. 6 = dug-up treasure chests.</summary>
    public const int SummaryVersion = 6;

    public static MatchResult Analyze(string replayPath, ItemTable names, ILogger logger = null, MapGeo geo = null)
    {
        names ??= ItemTable.Empty;
        var reader = new MatchReader(logger ?? NullLogger.Instance);
        var replay = reader.ReadReplay(replayPath);
        var res = Build(replayPath, replay, reader, names);
        if (geo != null && geo.Season == res.Summary.Season && geo.Pois.Count > 0)
            res = res with { Summary = res.Summary with { Haunt = Haunt(res.Map, geo, res.Summary.BusJump), Landing = Landing(res.Map, geo, res.Summary.BusJump) } };
        return res;
    }

    /// <summary>The named location my track spent the most samples within 250 m of (after the bus), or null.</summary>
    public static string Haunt(MapData map, MapGeo geo, double after)
    {
        var counts = new Dictionary<string, int>();
        foreach (var t in map.Tracks.Where(t => t.Role == 0))
            for (var i = 0; i < t.T.Length; i++)
            {
                if (t.T[i] < after + 30) continue;   // skip the bus and the glide
                var best = geo.Pois.Select(p => (p.Name, d: Math.Pow(p.X - t.X[i], 2) + Math.Pow(p.Y - t.Y[i], 2))).OrderBy(p => p.d).First();
                if (best.d < 25000.0 * 25000.0) counts[best.Name] = counts.GetValueOrDefault(best.Name) + 1;
            }
        return counts.Count == 0 ? null : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>When my track first slows to running pace after the jump (gliding is much faster), or null.</summary>
    public static (double t, double x, double y)? TouchDown(MapData map, double busJump)
    {
        var t = map.Tracks.Where(t => t.Role == 0 && t.T.Length > 2).OrderBy(t => t.T[0]).FirstOrDefault(t => t.T[^1] > busJump + 8);
        if (t == null) return null;
        for (var i = 0; i < t.T.Length; i++)
        {
            if (t.T[i] < busJump + 8) continue;
            var j = i; while (j + 1 < t.T.Length && t.T[j] - t.T[i] < 3) j++;   // a 3 s window
            var dt = t.T[j] - t.T[i];
            if (dt < 1.5) continue;
            var speed = Math.Sqrt(Math.Pow(t.X[j] - t.X[i], 2) + Math.Pow(t.Y[j] - t.Y[i], 2)) / dt;   // cm/s
            if (speed < 750) return (t.T[i], t.X[i], t.Y[i]);
        }
        return null;
    }

    /// <summary>The named location within 300 m of where I touched down, or null (open ground / unknown).</summary>
    public static string Landing(MapData map, MapGeo geo, double busJump)
    {
        if (TouchDown(map, busJump) is not { } td) return null;
        var best = geo.Pois.Select(p => (p.Name, d: Math.Pow(p.X - td.x, 2) + Math.Pow(p.Y - td.y, 2))).OrderBy(p => p.d).FirstOrDefault();
        return best.Name != null && best.d < 30000.0 * 30000.0 ? best.Name : null;
    }

    private static MatchResult Build(string file, FortniteReplay replay, MatchReader r, ItemTable names)
    {
        var me = replay.PlayerData?.FirstOrDefault(p => p.IsReplayOwner);
        var s = replay.Stats;
        var myPawns = r.PawnGuidToState.Where(kv => kv.Value == r.RecorderState).Select(kv => kv.Key).ToHashSet();
        r.StateTeam.TryGetValue(r.RecorderState ?? 0, out var myTeam);
        int Role(uint pawn) => myPawns.Contains(pawn) ? 0
            : r.PawnGuidToState.TryGetValue(pawn, out var st) && st != null && r.StateTeam.TryGetValue(st.Value, out var tm) && tm == myTeam ? 1 : 2;
        string Who(uint? pawn) => pawn == null ? "nobody" : Role(pawn.Value) switch { 0 => "me", 1 => "teammate", _ => "other" };
        var busJump = myPawns.Select(p => r.BusJumpAt.TryGetValue(p, out var bt) ? bt : (float?)null).Where(x => x != null).Min() ?? 0f;
        var off = r.WorldMinusDemo ?? 0;
        var allPk = r.AllPickups.ToList();
        var myPickups = allPk.Where(p => p.PickupTarget is uint pt && myPawns.Contains(pt) && p.PickedAt != null && p.Item != null).ToList();

        // ---- weapons: actor guid -> real name (cached)
        var weaponCache = new Dictionary<uint, WeaponNames.Info>();
        WeaponNames.Info Weapon(uint w) => weaponCache.TryGetValue(w, out var i) ? i
            : weaponCache[w] = WeaponNames.Resolve(r.ActorClass.GetValueOrDefault(w), r.ActorOpenedAt.GetValueOrDefault(w, float.NaN), myPickups, names);

        // ---- chests: my search events with loot, teammates' search events (team modes share chest rewards)
        var used = new HashSet<PickupState>();
        var chestEvents = new List<ChestEvent>();
        var searchedBefore = new HashSet<uint>();
        var refill = new HashSet<SearchEvent>(ReferenceEqualityComparer.Instance);
        foreach (var e in r.Chests.Events.OrderBy(e => e.T))
            if (e.Guid is uint g && !searchedBefore.Add(g)) refill.Add(e);
        foreach (var e in r.Chests.Events.Where(e => e.Kind != "carried" && Role(e.Pawn) <= 1))
        {
            var mine = Role(e.Pawn) == 0;
            var pos = r.PositionAt(new[] { e.Pawn }, e.T);
            var loot = new List<LootItem>(); double? cx = null, cy = null;
            if (mine && pos != null)
            {
                var m = LootMatcher.For(e.T, pos.Value.x, pos.Value.y, allPk, used);
                loot = m.Items.Select(p => names.Lookup(p.Item, Who(p.PickupTarget))).ToList();
                cx = m.Cx; cy = m.Cy;
            }
            chestEvents.Add(new ChestEvent(Math.Round(e.T, 1), e.Kind, Rules.ChestLabel(e.Chest), mine ? "me" : "teammate", pos?.x, pos?.y, cx, cy, loot, refill.Contains(e)));
        }
        var myChests = chestEvents.Where(c => c.Who == "me" && c.T >= busJump).ToList();
        var teamChests = chestEvents.Where(c => c.Who == "teammate" && c.T >= busJump).ToList();
        static List<ChestTypeCount> ByType(IEnumerable<ChestEvent> ev) => ev.GroupBy(c => c.Chest)
            .Select(g => new ChestTypeCount(g.Key, g.Count(c => c.Kind == "opened"), g.Count(c => c.Kind == "broken"),
                                            g.Count(c => c.Kind == "opened" && c.Refill), g.Count(c => c.Kind == "broken" && c.Refill))).ToList();
        var taken = myChests.SelectMany(c => c.Loot).Where(l => l.TakenBy == "me").ToList();
        var chests = new ChestStats(myChests.Count(c => c.Kind == "opened"), myChests.Count(c => c.Kind == "broken"), myChests.Sum(c => c.Loot.Count), taken.Count,
            taken.Where(l => l.Kind != "Ammo").Select(l => new TakenItem(l.Name, l.Rarity)).ToList(), ByType(myChests), ByType(teamChests),
            r.Chests.Events.Where(e => Role(e.Pawn) == 1 && e.Kind != "carried" && e.T >= busJump).Select(e => e.Pawn).Distinct().Count());
        var seasonItems = myPickups.Where(p => (p.PickedAt ?? 0) >= busJump && Rules.SeasonItem(p.Item) != null).OrderBy(p => p.PickedAt)
            .Select(p => { var li = names.Lookup(p.Item, "me"); return new SeasonPickup(Math.Round(p.PickedAt ?? 0, 1), li.Name, li.Rarity, Rules.SeasonItem(p.Item)); }).ToList();

        // ---- damage: cues on MY pawn channel; only hits on pawns; after the bus jump (warmup island excluded)
        string TargetKind(uint? g)
        {
            if (g is not uint v || v == 0) return "none";
            if (myPawns.Contains(v)) return "self";
            var cls = r.ActorClass.GetValueOrDefault(v) ?? "";
            if (cls.Contains("Phoebe")) return "bot";                                  // BR bots
            if (cls.Contains("/AI/") || cls.Contains("NPC", StringComparison.OrdinalIgnoreCase)) return "npc";
            if (Role(v) == 1) return "teammate";
            if (cls.Contains("PlayerPawn") || r.PawnGuidToState.ContainsKey(v)) return "player";
            return "object";                                                           // chests, builds, vehicles
        }
        uint HeldAt(uint pawn, float t) => r.PawnWeapons.GetValueOrDefault(pawn)?.LastOrDefault(x => x.t <= t).weapon ?? 0;
        var myHits = r.Hits.Where(h => h.ChannelActor is uint ca && myPawns.Contains(ca)).ToList();
        var blank = myHits.Where(h => h.T >= busJump && h.HitActor is null or 0 && h.SourceWeapon is null or 0 && h.Magnitude is null or 0)
                          .Select(h => HeldAt(h.ChannelActor.Value, h.T)).Where(w => w != 0).Select(Weapon).ToList();
        var dmgHits = myHits.Where(h => h.T >= busJump && h.HitActor is > 0 && h.Magnitude is > 0)
            .Select(h => (h, target: TargetKind(h.HitActor), w: Weapon(h.SourceWeapon ?? 0))).Where(x => x.target is "player" or "bot" or "npc").ToList();
        var perWeapon = dmgHits.GroupBy(x => (x.w.Name, x.w.Rarity)).Select(g => new WeaponDamage(g.Key.Name, g.Key.Rarity, g.Count(), g.Count(x => x.h.Crit),
                Math.Round(100.0 * g.Count(x => x.h.Crit) / g.Count(), 1), Math.Round(g.Sum(x => x.h.Magnitude.Value)),
                Math.Round(g.Where(x => x.target == "player").Sum(x => x.h.Magnitude.Value)), Math.Round(g.Where(x => x.target != "player").Sum(x => x.h.Magnitude.Value)),
                Math.Round(g.Where(x => x.h.Shield).Sum(x => x.h.Magnitude.Value)), g.Count(x => x.h.Fatal), Math.Round(g.Max(x => x.h.Magnitude.Value), 1), 0))
            .Concat(blank.GroupBy(w => (w.Name, w.Rarity)).Where(g => !dmgHits.Any(x => x.w.Name == g.Key.Name && x.w.Rarity == g.Key.Rarity))
                .Select(g => new WeaponDamage(g.Key.Name, g.Key.Rarity, 0, 0, 0, 0, 0, 0, 0, 0, 0, g.Count())))
            .OrderByDescending(w => w.Damage).ThenByDescending(w => w.BlankHits).ToList();
        var damage = new DamageStats(Math.Round(dmgHits.Sum(x => x.h.Magnitude.Value)), Math.Round(dmgHits.Where(x => x.target == "player").Sum(x => x.h.Magnitude.Value)),
                                     dmgHits.Count, dmgHits.Count(x => x.h.Crit), blank.Count, perWeapon);

        // ---- knocks vs elims + deaths
        var pdById = (replay.PlayerData ?? new List<PlayerData>()).Where(p => p.PlayerId != null).GroupBy(p => p.PlayerId).ToDictionary(g => g.Key, g => g.First());
        var mateIds = pdById.Values.Where(p => !p.IsReplayOwner && p.TeamIndex != null && p.TeamIndex == me?.TeamIndex).Select(p => p.PlayerId).ToHashSet();
        string WhoId(string id) => id == null ? "?" : id == me?.PlayerId ? "me" : mateIds.Contains(id) ? "teammate"
            : pdById.TryGetValue(id, out var p) ? Rules.Kind(p.IsBot, p.BotId) : "player";
        string NameId(string id) => id != null && pdById.TryGetValue(id, out var p)
            ? Rules.DisplayName(p.PlayerName, p.IsBot, p.IsUsingAnonymousMode, p.StreamerModeName, p.PlayerNameCustomOverride, p.BotId) : "?";
        HashSet<uint> PawnsOf(string playerId)
        {
            if (playerId == null) return new();
            var states = r.StateUniqueId.Where(kv => string.Equals(kv.Value, playerId, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToHashSet();
            return r.PawnGuidToState.Where(kv => kv.Value is uint sv && states.Contains(sv)).Select(kv => kv.Key).ToHashSet();
        }
        var elims = (replay.Eliminations ?? (IList<FortniteReplayReader.Models.Events.PlayerElimination>)Array.Empty<FortniteReplayReader.Models.Events.PlayerElimination>()).Select(e => new Elim((float)((e.Info?.StartTime ?? 0) / 1000.0), e.Eliminator, e.Eliminated, e.Knocked, e.GunType))
            .Where(x => x.T >= busJump && x.Eliminator != x.Eliminated || x.Eliminated == me?.PlayerId).ToList();
        FeedInfo Feed(Elim e)
        {
            var pd = e.Eliminated != null && pdById.TryGetValue(e.Eliminated, out var p) ? p : null;
            var k = pd == null ? null : replay.KillFeed.Where(k => k.PlayerId == pd.Id && k.IsDowned == e.Knocked && k.ReplicatedWorldTimeSecondsDouble != null)
                .OrderBy(k => Math.Abs(k.ReplicatedWorldTimeSecondsDouble.Value - off - e.T)).FirstOrDefault(k => Math.Abs(k.ReplicatedWorldTimeSecondsDouble.Value - off - e.T) < 3);
            if (k == null) return null;
            var tags = (k.DeathTags ?? Enumerable.Empty<string>()).Where(g => g.StartsWith("Weapon.Ranged.") || g.StartsWith("Weapon.Melee") || g.StartsWith("Rarity.")
                        || g.StartsWith("Gameplay.Damage.") && !g.StartsWith("Gameplay.Damage.Physical") || g.EndsWith(".Damage") && !g.StartsWith("Gameplay.Damage")).Distinct().ToList();
            return new FeedInfo(tags, k.Distance is float d ? Math.Round(d / 100.0) : null, k.DeathTags?.Contains("Gameplay.Damage.DBNOBleedOut") == true);
        }
        WeaponRef LastHit(Elim e)
        {
            var vp = PawnsOf(e.Eliminated);
            var cue = myHits.Where(h => h.HitActor is uint ha && vp.Contains(ha) && h.SourceWeapon is > 0 && h.T <= e.T + 0.3f && h.T >= e.T - 5).OrderBy(h => h.T).LastOrDefault();
            if (cue == null) return null;
            var w = Weapon(cue.SourceWeapon.Value); return new WeaponRef(w.Name, w.Rarity);
        }
        WeaponRef Held(Elim e)
        {
            var w = myPawns.SelectMany(p => r.PawnWeapons.GetValueOrDefault(p) ?? new()).Where(x => x.t <= e.T + 0.2f).OrderByDescending(x => x.t)
                .Select(x => Weapon(x.weapon)).FirstOrDefault(i => !i.Name.Contains("Consumable") && !(names.ByActorClass.GetValueOrDefault(i.Class)?.Any(c => c.Id.Contains("Consumable") || c.Type?.Contains("Consumable") == true) ?? false));
            return w == null ? null : new WeaponRef(w.Name, w.Rarity);
        }
        var fightList = FightAnalyzer.Build(elims, WhoId, NameId, Feed, LastHit, Held);
        var fights = FightAnalyzer.Summarize(fightList, me?.Kills is uint k ? (int)k : null);
        var lastDeath = fights.Deaths.LastOrDefault(d => d.Kind == "elim") ?? fights.Deaths.LastOrDefault();
        var firstKnock = fights.Deaths.FirstOrDefault(d => d.Kind == "knock");
        var death = lastDeath == null ? null : new DeathInfo(lastDeath.T, lastDeath.By, lastDeath.ByName, lastDeath.Cause, firstKnock?.Dist ?? lastDeath.Dist,
                                                             firstKnock != null, lastDeath.Tags.Contains("Gameplay.Damage.DBNOBleedOut"));

        // ---- summary
        var playlist = replay.GameData?.CurrentPlaylist ?? "";
        var summary = new MatchSummary(SummaryVersion, Path.GetFileName(file), Rules.StartFromFileName(file), r.Branch, Rules.SeasonOf(r.Branch), playlist,
            Rules.ModeOf(playlist), playlist.Contains("ForbiddenFruit") ? "Forbidden Fruit" : null,
            me?.Placement, Math.Round(r.LastTime / 60.0, 1), Math.Round(busJump, 1), names.Build,
            new EpicStats(me?.Kills is uint mk ? (int)mk : s?.Eliminations is uint se ? (int)se : null, s?.Assists is uint a ? (int)a : null, s?.Accuracy,
                          s?.DamageToPlayers is uint dp ? (int)dp : null, s?.DamageTaken is uint dt ? (int)dt : null,
                          s?.DamageToStructures is uint ds ? (int)ds : null, s?.TotalTraveled is uint tv ? Math.Round(tv / 100.0) : null),
            chests, seasonItems, damage, fights, death);

        // ---- map
        var tracks = r.PawnTracks.Where(kv => kv.Value.Count > 1).Select(kv => new Track(kv.Key, Role(kv.Key),
            kv.Value.Select(q => (float)Math.Round(q.t, 1)).ToArray(), kv.Value.Select(q => (float)Math.Round(q.x)).ToArray(), kv.Value.Select(q => (float)Math.Round(q.y)).ToArray())).ToList();
        var kills = replay.KillFeed.Where(k => k.DeathLocation != null && (k.ReplicatedWorldTimeSecondsDouble ?? k.ReplicatedWorldTimeSeconds) != null).Select(k => new KillMark(
            Math.Round((k.ReplicatedWorldTimeSecondsDouble ?? k.ReplicatedWorldTimeSeconds).Value - off, 1), Math.Round(k.DeathLocation.X), Math.Round(k.DeathLocation.Y),
            k.IsDowned, me?.Id != null && k.FinisherOrDowner == me.Id, me?.Id != null && k.PlayerId == me.Id, k.PlayerIsBot ? "bot" : "player")).ToList();
        var zones = (replay.MapData?.SafeZones ?? (IList<SafeZone>)Array.Empty<SafeZone>()).Where(z => z.NextCenter != null).Select(z => new Zone(
            Math.Round(z.StartShrinkTime - off, 1), Math.Round(z.FinishShrinkTime - off, 1), Math.Round(z.NextCenter.X), Math.Round(z.NextCenter.Y), Math.Round(z.NextRadius),
            z.LastCenter == null ? null : Math.Round(z.LastCenter.X), z.LastCenter == null ? null : Math.Round(z.LastCenter.Y), Math.Round(z.LastRadius))).ToList();
        return new MatchResult(summary, new MapData(r.LastTime, tracks, chestEvents, kills, zones));
    }
}
