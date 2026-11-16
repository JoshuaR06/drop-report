// Booty Board command line: analyze replays without the app (debugging, batch runs).
//   bootyboard analyze <replay file or folder> [--out <dir>] [--names <itemnames.json>] [--max N]
using System.Diagnostics;
using System.Text.Json;
using BootyBoard;
using BootyBoard.Analysis;

var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootyBoard");
if (args.Length >= 1 && args[0] == "gamedata")
{
    // builds the real-name item table from the installed game (downloads the key/mappings/Oodle if not cached)
    var game = BootyBoard.GameData.GameInstall.Find();
    if (game == null) { Console.WriteLine("Fortnite install not found"); return 1; }
    Console.WriteLine($"game: {game.Folder} {game.Build}");
    var b = new BootyBoard.GameData.GameDataBuilder(Path.Combine(dataRoot, "gamedata"));
    var n = await b.BuildAsync(game, new Progress<string>(Console.WriteLine));
    Console.WriteLine($"{n} items -> {b.ItemTablePath}");
    return 0;
}
if (args.Length >= 2 && args[0] == "mapinfo")
{
    var rep = new BootyBoard.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ReadReplay(args[1]);
    var md = rep.MapData;
    Console.WriteLine($"map info: {rep.GameData?.MapInfo}  playlist {rep.GameData?.CurrentPlaylist}");
    Console.WriteLine($"levels: {string.Join(", ", rep.GameData?.AdditionalPlaylistLevels ?? Array.Empty<string>())}");
    Console.WriteLine($"grid start {md?.WorldGridStart?.X},{md?.WorldGridStart?.Y}  end {md?.WorldGridEnd?.X},{md?.WorldGridEnd?.Y}  spacing {md?.WorldGridSpacing?.X},{md?.WorldGridSpacing?.Y}  total {md?.WorldGridTotalSize?.X},{md?.WorldGridTotalSize?.Y}");
    return 0;
}
if (args.Length >= 2 && args[0] == "players")
{
    // name fields per player (debugging names, bots, anonymous/streamer mode)
    var rep = new BootyBoard.Replay.MatchReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ReadReplay(args[1]);
    foreach (var p in rep.PlayerData.OrderBy(p => p.IsBot).ThenBy(p => p.TeamIndex))
        Console.WriteLine($"bot={p.IsBot,-5} team={p.TeamIndex,-3} name='{p.PlayerName}' anon={p.IsUsingAnonymousMode} streamer={p.IsUsingStreamerMode} " +
                          $"streamerName='{p.StreamerModeName}' override='{p.PlayerNameCustomOverride}' platform={p.Platform} botId={(p.BotId != null ? "yes" : "")} owner={p.IsReplayOwner}");
    return 0;
}
if (args.Length < 2 || args[0] != "analyze")
{
    Console.WriteLine("usage: bootyboard analyze <replay file or folder> [--out <dir>] [--names <itemnames.json>] [--max N]\n       bootyboard gamedata");
    return 1;
}
string Opt(string name, string def) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
var target = args[1];
var outDir = Opt("--out", Path.Combine(Environment.CurrentDirectory, "out"));
var names = ItemTable.Load(Opt("--names", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BootyBoard", "gamedata", "itemnames.json")));
var max = int.Parse(Opt("--max", "100000"));
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
        var res = MatchAnalyzer.Analyze(f, names);
        var b = Path.GetFileNameWithoutExtension(f);
        File.WriteAllText(Path.Combine(outDir, b + ".summary.json"), JsonSerializer.Serialize(res.Summary, Json.Options));
        File.WriteAllText(Path.Combine(outDir, b + ".map.json"), JsonSerializer.Serialize(res.Map, Json.Options));
        var s = res.Summary;
        Console.WriteLine($"{b}  v{s.Season} {s.Mode,-5} #{s.Placement,-3} chests me {s.Chests.Opened}+{s.Chests.Broken} team {s.Chests.Team.Sum(t => t.Opened)}" +
                          $"  loot {s.Chests.Items}  knocks {s.Fights.Knocks} elims {s.Fights.Elims} (Epic {s.Fights.EpicElims})  dmg {s.Damage.Recorded} hits {s.Damage.Hits} HS {s.Damage.Headshots}  {sw.Elapsed.TotalSeconds:F1}s");
        ok++;
    }
    catch (Exception e) { Console.WriteLine($"{Path.GetFileName(f)}  FAILED {e.GetType().Name}: {e.Message}"); failed++; }
}
Console.WriteLine($"done: {ok} ok, {failed} failed -> {outDir}");
return failed > 0 && ok == 0 ? 2 : 0;
