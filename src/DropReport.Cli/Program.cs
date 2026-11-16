// Drop Report command line: analyze replays without the app (debugging, batch runs).
//   dropreport analyze <replay file or folder> [--out <dir>] [--names <itemnames.json>] [--max N]
using System.Diagnostics;
using System.Text.Json;
using DropReport;
using DropReport.Analysis;

var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropReport");
if (args.Length >= 1 && args[0] == "gamedata")
{
    // builds the real-name item table from the installed game (downloads the key/mappings/Oodle if not cached)
    var game = DropReport.GameData.GameInstall.Find();
    if (game == null) { Console.WriteLine("Fortnite install not found"); return 1; }
    Console.WriteLine($"game: {game.Folder} {game.Build}");
    var b = new DropReport.GameData.GameDataBuilder(Path.Combine(dataRoot, "gamedata"));
    var n = await b.BuildAsync(game, new Progress<string>(Console.WriteLine));
    Console.WriteLine($"{n} items -> {b.ItemTablePath}");
    return 0;
}
if (args.Length >= 2 && args[0] == "mapinfo")
{
    var rep = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ReadReplay(args[1]);
    var md = rep.MapData;
    Console.WriteLine($"map info: {rep.GameData?.MapInfo}  playlist {rep.GameData?.CurrentPlaylist}");
    Console.WriteLine($"levels: {string.Join(", ", rep.GameData?.AdditionalPlaylistLevels ?? Array.Empty<string>())}");
    Console.WriteLine($"grid start {md?.WorldGridStart?.X},{md?.WorldGridStart?.Y}  end {md?.WorldGridEnd?.X},{md?.WorldGridEnd?.Y}  spacing {md?.WorldGridSpacing?.X},{md?.WorldGridSpacing?.Y}  total {md?.WorldGridTotalSize?.X},{md?.WorldGridTotalSize?.Y}");
    return 0;
}
if (args.Length >= 2 && args[0] == "players")
{
    // name fields per player (debugging names, bots, anonymous/streamer mode)
    var rep = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ReadReplay(args[1]);
    foreach (var p in rep.PlayerData.OrderBy(p => p.IsBot).ThenBy(p => p.TeamIndex))
        Console.WriteLine($"bot={p.IsBot,-5} team={p.TeamIndex,-3} name='{p.PlayerName}' anon={p.IsUsingAnonymousMode} streamer={p.IsUsingStreamerMode} " +
                          $"streamerName='{p.StreamerModeName}' override='{p.PlayerNameCustomOverride}' platform={p.Platform} botId={(p.BotId != null ? "yes" : "")} owner={p.IsReplayOwner}");
    return 0;
}
if (args.Length >= 3 && args[0] == "chest-looks")
{
    var rs = Directory.GetFiles(args[1], "*.replay").Where(f => File.GetLastWriteTime(f) >= DateTime.Parse(args[2])).OrderBy(f => f).ToList();
    var dist = new Dictionary<string, Dictionary<string, int>>();
    var flagged = new List<string>();
    var tierRows = new List<string>(); int tierBefore = 0, tierAfter = 0, tierNever = 0, ownTier = 0; var ownRows = new List<string>();
    int chests = 0;
    foreach (var f in rs)
    {
        var r = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { CollectPaths = true };
        try { r.ReadReplay(f); } catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: {e.Message}"); continue; }
        foreach (var (g, look) in r.ChestLooks)
        {
            chests++;
            foreach (var k in new[] { "mesh", "searchedMesh", "altMesh", "lootTier", "upgrade" })
            {
                var key = $"{look["class"]}.{k}";
                var v = look.GetValueOrDefault(k) ?? "(never sent)";
                v = v.Contains('/') ? v[(v.LastIndexOf('/') + 1)..] : v;
                (dist.TryGetValue(key, out var dd) ? dd : dist[key] = new())[v] = dd?.GetValueOrDefault(v) + 1 ?? 1;
            }
        }
        foreach (var (g, look) in r.ChestLooks.Where(x => x.Value.GetValueOrDefault("lootTier") == "2" && x.Value["class"] is "TrackedChest" or "AlwaysSpawnNormalChest" or "BuriedChest"))
        {
            var at = float.Parse(look["lootTierAt"]);
            var first = r.Chests.Events.Where(e => e.Guid == g && e.Kind != "carried").OrderBy(e => e.T).FirstOrDefault();
            var mineP = r.PawnGuidToState.Where(kv => kv.Value == r.RecorderState).Select(kv => kv.Key).ToHashSet();
            r.StateTeam.TryGetValue(r.RecorderState ?? 0, out var myT);
            string WhoP(uint pw) => mineP.Contains(pw) ? "me" : r.PawnGuidToState.TryGetValue(pw, out var st) && st is uint s2 && r.StateTeam.TryGetValue(s2, out var tm) && tm == myT ? "teammate" : "other";
            if (first != null && WhoP(first.Pawn) != "other")
            {
                ownTier++;
                var revealNear = r.PrizeUpdates.Any(x => Math.Abs(x.T - first.T) <= 3);
                ownRows.Add($"{Path.GetFileNameWithoutExtension(f)[14..]} chest {g}: {WhoP(first.Pawn)} {first.Kind} at {first.T:0.0}s (tier known {at:0.0}s) -> reveal: {(revealNear ? "YES" : "no")}");
            }
            if (first == null) tierNever++; else if (at < first.T - 0.5f) tierBefore++; else tierAfter++;
            tierRows.Add($"{Path.GetFileNameWithoutExtension(f)[14..]} chest {g} seen {look["firstSeen"]}s tier@{at:0.0}s first search {(first == null ? "none" : $"{first.T:0.0}s ({first.Kind})")}");
        }
        // the chest searched/broken closest to each treasure reveal
        foreach (var rv in r.PrizeUpdates.GroupBy(x => x.Actor))
        {
            var t0 = rv.Min(x => x.T);
            var near = r.Chests.Events.Where(e => e.Guid != null && Math.Abs(e.T - t0) <= 2).OrderBy(e => Math.Abs(e.T - t0)).FirstOrDefault();
            if (near?.Guid is uint ng && r.ChestLooks.TryGetValue(ng, out var lk))
                flagged.Add($"{Path.GetFileNameWithoutExtension(f)} reveal {t0:0.0}s <- chest {ng} {near.Kind}: " + string.Join("  ", lk.Select(kv => $"{kv.Key}={(kv.Value.Contains('/') ? kv.Value[(kv.Value.LastIndexOf('/') + 1)..] : kv.Value)}")));
        }
        Console.Error.Write(".");
    }
    Console.WriteLine($"\n{rs.Count} replays, {chests} chests seen");
    foreach (var kv in dist.OrderBy(k => k.Key))
    {
        Console.WriteLine($"{kv.Key}:");
        foreach (var v in kv.Value.OrderByDescending(x => x.Value).Take(12)) Console.WriteLine($"   {v.Value,6}  {v.Key}");
    }
    Console.WriteLine("--- loot tier 2 on regular chests: when it arrived vs the first search");
    foreach (var s in tierRows.Take(40)) Console.WriteLine("  " + s);
    Console.WriteLine($"  summary: {tierRows.Count} chests; tier known before any search: {tierBefore}; at/after a search: {tierAfter}; never searched: {tierNever}");
    Console.WriteLine($"--- tier-2 chests opened or broken by you or your team: {ownTier}");
    foreach (var s in ownRows) Console.WriteLine("  " + s);
    Console.WriteLine("--- the chest at each treasure reveal:");
    foreach (var s in flagged) Console.WriteLine("  " + s);
    return 0;
}
if (args.Length >= 1 && args[0] == "base-container")
{
    foreach (var pr in typeof(FortniteReplayReader.Models.NetFieldExports.Items.BaseContainer).GetProperties())
    {
        var at = pr.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().Name == "NetFieldExportAttribute");
        Console.WriteLine($"{pr.Name,-32} {pr.PropertyType.Name,-20} {(at == null ? "" : string.Join(" ", at.GetType().GetProperties().Select(x => $"{x.Name}={x.GetValue(at)}")))}");
    }
    return 0;
}
if (args.Length >= 4 && args[0] == "fields")
{
    // research: the replicated fields of every networked class matching a regex: fields <folder> <since> <regex>
    var rs = Directory.GetFiles(args[1], "*.replay").Where(f => File.GetLastWriteTime(f) >= DateTime.Parse(args[2])).OrderBy(f => f).Take(5).ToList();
    var all = new Dictionary<string, HashSet<string>>();
    foreach (var f in rs)
    {
        var r = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            { CollectPaths = true, GroupFilter = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase) };
        try { r.ReadReplay(f); } catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: {e.Message}"); continue; }
        foreach (var g in r.GroupFields) { var s = all.TryGetValue(g.Key, out var x) ? x : all[g.Key] = new(); s.UnionWith(g.Value); }
    }
    foreach (var kv in all.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Key}\n    {string.Join(", ", kv.Value.OrderBy(x => x))}");
    return 0;
}
if (args.Length >= 3 && args[0] == "gizmos")
{
    // research: every gizmo the replay owner held (granted gizmos never show as pickups), where it came from,
    // and the owner's chest searches just before: gizmos <folder> <since>
    var gz = new System.Text.RegularExpressions.Regex(@"(?i)gizmo|SmugglerExtractionKey|RelicExtractor|RelicFinder|LuckyLocator|FlameCeleb|ExtractionSiteBooster");
    var rs = Directory.GetFiles(args[1], "*.replay").Where(f => File.GetLastWriteTime(f) >= DateTime.Parse(args[2])).OrderBy(f => f).ToList();
    int found = 0;
    foreach (var f in rs)
    {
        var r = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { CollectPaths = true };
        try { r.ReadReplay(f); } catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: {e.Message}"); continue; }
        var mine = r.PawnGuidToState.Where(kv => kv.Value == r.RecorderState).Select(kv => kv.Key).ToHashSet();
        r.StateTeam.TryGetValue(r.RecorderState ?? 0, out var myTeam);
        string Team(uint pawn) => mine.Contains(pawn) ? "me" : r.PawnGuidToState.TryGetValue(pawn, out var st) && st is uint s && r.StateTeam.TryGetValue(s, out var tm) && tm == myTeam ? "teammate" : null;
        var held = r.PawnWeapons.Where(kv => mine.Contains(kv.Key)).SelectMany(kv => kv.Value)
            .Select(w => (w.t, w.weapon, cls: r.ActorClass.GetValueOrDefault(w.weapon) ?? r.AllPaths.GetValueOrDefault(w.weapon) ?? "?"))
            .Where(w => gz.IsMatch(w.cls)).GroupBy(w => w.weapon).Select(g => g.OrderBy(x => x.t).First()).OrderBy(w => w.t).ToList();
        var picked = r.AllPickups.Where(pk => pk.PickupTarget is uint pt && mine.Contains(pt) && pk.Item != null && gz.IsMatch(pk.Item)).ToList();
        if (held.Count == 0 && picked.Count == 0) continue;
        found++;
        Console.WriteLine($"\n## {Path.GetFileNameWithoutExtension(f)}");
        foreach (var pk in picked) Console.WriteLine($"   picked up {pk.Item} at {pk.PickedAt:0.0}s{(pk.Tossed ? " (tossed)" : "")}");
        foreach (var h in held)
        {
            var fromPickup = picked.Any(pk => pk.PickedAt <= h.t + 1);
            Console.WriteLine($"   held {h.cls.Split('/').Last()} first at {h.t:0.0}s  -> {(fromPickup ? "after a pickup" : "NOT picked up: granted")}");
            foreach (var e in r.Chests.Events.Where(e => Team(e.Pawn) != null && e.Kind != "carried" && e.T <= h.t && e.T >= h.t - 240).OrderByDescending(e => e.T).Take(8))
                Console.WriteLine($"        {Team(e.Pawn),-8} {DropReport.Rules.ChestLabel(e.Chest),-22} {e.Kind,-7} at {e.T,6:0.0}s ({h.t - e.T,5:0.0}s earlier)");
        }
        var rev = r.PrizeUpdates.Select(x => x.Actor).Distinct().Count();
        if (rev > 0) Console.WriteLine($"   treasure reveals in this match: {rev}");
    }
    Console.WriteLine($"\n{found} of {rs.Count} matches with a gizmo");
    return 0;
}
if (args.Length >= 4 && args[0] == "scan-paths")
{
    // research: which replays reference asset paths matching a regex (e.g. chest classes): scan-paths <folder> <since> <regex>
    var rxp = new System.Text.RegularExpressions.Regex(args[3], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var hitsByPath = new Dictionary<string, int>();
    var rs = Directory.GetFiles(args[1], "*.replay").Where(f => File.GetLastWriteTime(f) >= DateTime.Parse(args[2])).OrderBy(f => f).ToList();
    foreach (var f in rs)
    {
        var reader = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { CollectPaths = true };
        try { reader.ReadReplay(f); } catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}: {e.Message}"); continue; }
        foreach (var path in reader.AllPaths.Values.Where(v => v.StartsWith("/") && rxp.IsMatch(v)).Distinct()) hitsByPath[path] = hitsByPath.GetValueOrDefault(path) + 1;
        Console.Error.Write(".");
    }
    Console.WriteLine($"\n{rs.Count} replays");
    foreach (var kv in hitsByPath.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Value,4} of {rs.Count}  {kv.Key}");
    return 0;
}
if (args.Length >= 2 && args[0] == "scan-rewards")
{
    // research: does anything reward-like (Gizmo items, token, cosmetics, treasure) ever appear in replays?
    var rx = new System.Text.RegularExpressions.Regex(@"(?i)gizmo|pirate|plunder|shopaccess|treasure|reward|bounty|cosmetic|collectib|lootdrop|bonus|jackpot|token|loadingscreen|jamtrack|musicpack");
    var groupFields = new Dictionary<string, HashSet<string>>();
    var paths = new Dictionary<string, int>(); var picks = new Dictionary<string, int>(); var seenIn = new Dictionary<string, HashSet<string>>();
    var since = args.Length >= 3 ? DateTime.Parse(args[2]) : DateTime.MinValue;
    var replays = Directory.GetFiles(args[1], "*.replay").Where(f => File.GetLastWriteTime(f) >= since).OrderBy(f => f).ToList();
    var gtable = ItemTable.Load(Path.Combine(dataRoot, "gamedata", "itemnames.json"));
    foreach (var f in replays)
    {
        try
        {
            var reader = new DropReport.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { CollectPaths = true, GroupFilter = new System.Text.RegularExpressions.Regex(@"(?i)treasurehunt|prize|treasurekey|playtimereward|pirateshop|booty|reward|award|grant|unlock|celebrat|toast|notif|jackpot|gizmo") };
            reader.ReadReplay(f);
            foreach (var p in reader.AllPaths.Values.Distinct().Where(p => rx.IsMatch(p))) { paths[p] = paths.GetValueOrDefault(p) + 1; }
            if (reader.PrizeUpdates.Count > 0)
            {
                var mine = reader.PawnGuidToState.Where(kv => kv.Value == reader.RecorderState).Select(kv => kv.Key).ToHashSet();
                string D(Unreal.Core.Models.DebuggingObject o) => o == null ? "-" :
                    $"bits={o.TotalBits} fstr=\"{Safe(() => o.FString)}\" name=\"{Safe(() => o.StaticName)}\" int={Safe(() => o.IntValue)} packed={Safe(() => o.IntPacked)} byte={Safe(() => o.ByteValue)} hex={Convert.ToHexString(o.Bytes ?? Array.Empty<byte>())}";
                static string Safe(Func<object> f) { try { return f()?.ToString(); } catch { return "!"; } }
                Console.WriteLine($"\n## {Path.GetFileNameWithoutExtension(f)}: {reader.PrizeUpdates.Count(x => x.Update is DropReport.Replay.TreasureHuntPrizeChest)} prize-chest updates on {reader.PrizeUpdates.Where(x => x.Update is DropReport.Replay.TreasureHuntPrizeChest).Select(x => x.Actor).Distinct().Count()} chests");
                foreach (var (t, actor, upd) in reader.PrizeUpdates.Where(x => x.Update is DropReport.Replay.TreasureHuntPrizeChest).Take(60))
                {
                    if (upd is DropReport.Replay.TreasureHuntPrizeChest c)
                        Console.WriteLine($"  {t,7:0.0} chest#{actor} opener={(c.OpeningPawn is uint op ? (mine.Contains(op) ? "ME" : op.ToString()) : "-")} source={c.SourceContainer?.ToString() ?? "-"} success={c.bSuccess?.ToString() ?? "-"} destroyed={c.bDestroyed?.ToString() ?? "-"}" +
                                          $"\n          prize: {D(c.PrizeTemplateId)}\n          tier: {D(c.Tier)}  tierData: {D(c.TierData)}  owner: {D(c.OwnerPersistentID)}");
                }
            }
            foreach (var g in reader.GroupFields) { var s = groupFields.TryGetValue(g.Key, out var x) ? x : groupFields[g.Key] = new(); s.UnionWith(g.Value); }
            foreach (var pk in reader.AllPickups.Where(p => p.Item != null))
            {
                var tags = gtable.ById.TryGetValue(pk.Item, out var def) ? string.Join(",", def.Tags ?? new()) : "";
                if (!rx.IsMatch(pk.Item + " " + tags)) continue;
                var key = $"{pk.Item}  [{tags}]";
                picks[key] = picks.GetValueOrDefault(key) + 1;
                (seenIn.TryGetValue(key, out var s) ? s : seenIn[key] = new()).Add(Path.GetFileNameWithoutExtension(f));
            }
            Console.Error.Write(".");
        }
        catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)} failed: {e.Message}"); }
    }
    Console.WriteLine($"\n{replays.Count} replays");
    Console.WriteLine("--- reward-like pickups (items on the ground: chests, floor loot, drops):");
    foreach (var kv in picks.OrderByDescending(k => k.Value)) Console.WriteLine($"{kv.Value,5}  {kv.Key}  in {seenIn[kv.Key].Count} matches");
    Console.WriteLine("--- reward-like asset paths (not cosmetics):");
    foreach (var kv in paths.Where(k => !k.Key.Contains("Cosmetic", StringComparison.OrdinalIgnoreCase) && !k.Key.Contains("/BRCosmetics/")).OrderByDescending(k => k.Value)) Console.WriteLine($"{kv.Value,5}  {kv.Key}");
    Console.WriteLine("--- networked classes and the fields they replicate:");
    foreach (var kv in groupFields.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Key}\n      {string.Join(", ", kv.Value.OrderBy(x => x))}");
    return 0;
}
if (args.Length >= 2 && args[0] == "treasure-json")
{
    // the odds file the app downloads (data/treasure.json): start it from the built-in values, then edit dates/odds
    File.WriteAllText(args[1], JsonSerializer.Serialize(Treasure.BuiltIn(), new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
    Console.WriteLine($"wrote {args[1]}");
    return 0;
}
if (args.Length >= 1 && args[0] == "insights")
{
    // what the Treasure Hunt / Weapons / Drop spots pages will say, from the app's saved match summaries
    var season = args.Length >= 2 ? int.Parse(args[1]) : 43;
    var ms = Directory.GetFiles(Path.Combine(dataRoot, "matches"), "*.summary.json")
        .Select(f => JsonSerializer.Deserialize<MatchSummary>(File.ReadAllText(f), Json.Options)).Where(m => m?.Season == season).ToList();
    var t = Treasure.Build(ms, CountRule.Default, DateTime.UtcNow);
    Console.WriteLine($"season {season}: {ms.Count} matches");
    Console.WriteLine($"token: {t.Chests} rolling chests ({t.BoostedChests} boosted), chance {t.TokenChance:P3}; pace {t.ChestsPerDay:0.#}/day, {t.DaysLeft} days left -> {t.ProjectedChests:0} chests, {t.ProjectedChance:P3}");
    Console.WriteLine($"  refills {t.Refills}, broken {t.Broken}, team chests {t.TeamChests}");
    foreach (var o in t.Others) Console.WriteLine($"  {o.Reward.Name,-22} {o.Chance,8:P1}  over {o.Rolls:0} chests{(o.Reward.Weekly ? " this week" : " this season")}");
    var ws = Insights.Weapons(ms);
    foreach (var p in Insights.BestWeapons(ws)) Console.WriteLine($"weapon  {p.Title,-22} {p.Name,-28} {p.Value,6}  {p.Detail}");
    foreach (var c in Insights.Classes(ms)) Console.WriteLine($"class   {c.Class,-10} elims {c.Elims,3} knocks {c.Knocks,3} share {c.Share:P0} median {c.MedianDist:0} m");
    foreach (var s in Insights.Spots(ms)) Console.WriteLine($"spot    {s.Spot,-20} n={s.Matches,2} place {s.AvgPlace,5:0.0} wins {s.Wins} top10 {s.Top10Rate:P0} elims {s.ElimsPerMatch:0.0} chests {s.ChestsPerMatch:0.0} early {s.EarlyDeaths}");
    foreach (var p in Insights.BestSpots(Insights.Spots(ms))) Console.WriteLine($"drop    {p.Title,-22} {p.Name,-20} {p.Value,6}  {p.Detail}");
    foreach (var n in Insights.Notes(ms)) Console.WriteLine($"note    [{n.Kind}] {n.Headline}: {n.Detail}");
    return 0;
}
if (args.Length < 2 || args[0] != "analyze")
{
    Console.WriteLine("usage: dropreport analyze <replay file or folder> [--out <dir>] [--names <itemnames.json>] [--max N]\n       dropreport gamedata");
    return 1;
}
string Opt(string name, string def) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var target = args[1];
var outDir = Opt("--out", Path.Combine(Environment.CurrentDirectory, "out"));
var names = ItemTable.Load(Opt("--names", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropReport", "gamedata", "itemnames.json")));
var max = int.Parse(Opt("--max", "100000"));
var geo = MapGeo.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Opt("--names", Path.Combine(dataRoot, "gamedata", "itemnames.json")))), "mapinfo.json"));
Directory.CreateDirectory(outDir);
Console.WriteLine($"item names: {names.ById.Count} ({names.Build ?? "none: items keep code names"})");

var files = Directory.Exists(target)
    ? Directory.GetFiles(target, "*.replay").OrderByDescending(File.GetLastWriteTime).Take(max).ToArray()
    : new[] { target };
int ok = 0, failed = 0;
foreach (var f in files)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var res = MatchAnalyzer.Analyze(f, names, geo: geo);
        var b = Path.GetFileNameWithoutExtension(f);
        File.WriteAllText(Path.Combine(outDir, b + ".summary.json"), JsonSerializer.Serialize(res.Summary, Json.Options));
        File.WriteAllText(Path.Combine(outDir, b + ".map.json"), JsonSerializer.Serialize(res.Map, Json.Options));
        var s = res.Summary;
        Console.WriteLine($"{b}  v{s.Season} {s.Mode,-5} #{s.Placement,-3} chests me {s.Chests.Opened}+{s.Chests.Broken} team {s.Chests.Team.Sum(t => t.Opened)}" +
                          $"  loot {s.Chests.Items}  knocks {s.Fights.Knocks} elims {s.Fights.Elims} (Epic {s.Fights.EpicElims})  dmg {s.Damage.Recorded} hits {s.Damage.Hits} HS {s.Damage.Headshots}  {sw.Elapsed.TotalSeconds:F1}s  | {Headlines.For(s).Headline}");
        ok++;
    }
    catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}  FAILED {e.GetType().Name}: {e.Message}"); failed++; }
}
Console.WriteLine($"done: {ok} ok, {failed} failed -> {outDir}");
return failed > 0 && ok == 0 ? 2 : 0;
