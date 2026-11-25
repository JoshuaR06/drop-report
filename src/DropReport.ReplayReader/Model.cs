using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropReport;

// Everything one match produces. Summary = small (lists, season totals); Map = big (positions for the map view).

public sealed record MatchResult(MatchSummary Summary, MapData Map, List<SearchStart> Searches = null);

/// <summary>A pawn began a search (replicated bStartedInteractSearch). Role 0 = me, 1 = teammate, 2 = other.</summary>
public sealed record SearchStart(double T, int Role, double X, double Y, double? Seconds = null);

/// <summary>Where and when a teammate searched something the replay didn't show (it was out of view).</summary>
public sealed record SearchMark(double T, double X, double Y);

public sealed record MatchSummary(
    int Version, string File, DateTime? Start, string Branch, int Season, string Playlist, string Mode, string Special,
    int? Placement, double Minutes, double BusJump, string NamesBuild,
    EpicStats Epic, ChestStats Chests, List<SeasonPickup> PickedUp, DamageStats Damage, FightStats Fights, DeathInfo Death,
    string Haunt = null,    // the named location you spent the most time near (needs the map data); for headlines
    string Landing = null,  // the named location nearest to where you touched down (null = open ground or no map data)
    List<TreasureReveal> Reveals = null,    // season 43: chests that turned out to hold a treasure
    List<TreasureMoment> Prizes = null,     // season 43 (v8+): when you earned a treasure (first one per match) and what set it off
    List<SearchMark> UnseenTeamSearches = null,   // v9: teammates' searches out of the replay's view (see TeamEstimate)
    List<ChestRoll> Rolls = null)   // v11: when each chest that rolls for treasure was searched (Treasure Hours go by these times)
{
    /// <summary>Of UnseenTeamSearches, those at a known chest spot (ChestSpots): teammates' chest searches the replay
    /// couldn't show, estimated. Set when loaded, because the chest-spot list grows with every match.</summary>
    [JsonIgnore] public int TeamEstimated { get; init; }
    /// <summary>From Fortnite's log, not the replay (TreasureLog.Earned): true = the post-match Treasures Found screen
    /// followed this match, false = the log covered it and it didn't, null = no log for that time. Set when loaded.</summary>
    [JsonIgnore] public bool? TreasureEarned { get; init; }
    /// <summary>Treasures this match: the replay's first one, or more when Fortnite's log shows extra reward popups.</summary>
    [JsonIgnore] public int TreasureCount { get; init; }
    /// <summary>From Fortnite's log: worst ping jitter in the match (ms) and the load screen into it (s); null = no log.</summary>
    [JsonIgnore] public double? JitterMs { get; init; }
    [JsonIgnore] public double? LoadSeconds { get; init; }
}

/// <summary>The moment the replay owner earned a treasure (from the prize handler; see MatchReader.PrizeHandlers) and what
/// set it off: the chest search or break by you or a teammate just before it, "teammate-far" (v10: an out-of-view teammate's
/// search just before, seen by its search flag, see TeamEstimate), or "unseen" (nothing in view: a teammate out of
/// range). Kind = opened or broken. Gold = the game showed its gold treasure chest (a Major or Supreme prize).</summary>
public sealed record TreasureMoment(double T, string Trigger, string Kind, string Chest, bool Gold, bool Refill);

/// <summary>One chest search or break after the bus jump that rolls for treasure (the same chests ChestStats counts), with
/// its time: a Treasure Hours boost applies to the chests opened inside the window, not to the whole match.</summary>
public sealed record ChestRoll(double T, bool Team, bool Broken, bool Refill);

/// <summary>A treasure reveal: a chest holding a treasure was opened or broken and the game swapped in its animated chest.</summary>
/// <param name="Who">me, teammate, other or unknown: the opener, else whoever searched or broke a chest at that moment.</param>
/// <param name="Prize">The prize, when the replay carried it (it often doesn't for chests other players open).</param>
public sealed record TreasureReveal(double T, string Chest, string Who, bool Broken, bool? Success, string Prize, string Tier);

/// <summary>Epic's own end-of-match numbers (from the replay's stats events).</summary>
public sealed record EpicStats(int? Elims, int? Assists, double? Accuracy, int? DamageToPlayers, int? DamageTaken, int? DamageToStructures, double? TravelledM);

/// <param name="OpenedRefill">Of Opened, searches of a chest that had already been searched this match (chests refill over time).</param>
public sealed record ChestTypeCount(string Type, int Opened, int Broken, int OpenedRefill = 0, int BrokenRefill = 0);
public sealed record TakenItem(string Name, string Rarity);

/// <param name="Team">Teammates' chests (team modes share chest rewards). Only what my client saw: a minimum.</param>
public sealed record ChestStats(int Opened, int Broken, int Items, int ItemsTaken, List<TakenItem> Taken, List<ChestTypeCount> ByType,
                                List<ChestTypeCount> Team, int Teammates);

public sealed record SeasonPickup(double T, string Name, string Rarity, string Season);

public sealed record WeaponDamage(string Weapon, string Rarity, int Hits, int Headshots, double HeadshotPct, double Damage, double ToPlayers,
                                  double ToBots, double ToShield, int Fatal, double BiggestHit, int BlankHits);

/// <summary>Only hits the replay recorded with detail. Slingshot hits are blank, melee items send none.</summary>
public sealed record DamageStats(double Recorded, double RecordedToPlayers, int Hits, int Headshots, int BlankHits, List<WeaponDamage> PerWeapon);

/// <param name="Credit">This event earns its "By" an elimination on Epic's scoreboard. Fortnite credits the player who
/// KNOCKED someone, whoever finishes them: so an elim of someone another player knocked is not credited, and a knock
/// that someone else finishes is. (Checked against Epic's own count: 94 of 100 matches exact, the rest off by one.)</param>
public sealed record FightEvent(double T, string Kind, string By, string ByName, string Victim, string VictimName, string Cause, int GunType,
                                string Weapon, string WeaponRarity, string WeaponFrom, List<string> Tags, double? Dist, string KnockedBy, string Outcome,
                                bool Credit = false);

public sealed record WeaponFights(string Weapon, string Rarity, int Knocks, int Elims);

public sealed record FightStats(int Knocks, int Elims, int KnocksPlayers, int ElimsPlayers, int KnocksBots, int ElimsBots,
                                int MyKnocksIFinished, int MyKnocksMateFinished, int MyKnocksLost, int FinishedMateKnocks, int DirectElims,
                                int FinishedOtherTeamsKnocks, int TeamWipeCredits, int? EpicElims, List<WeaponFights> ByWeapon,
                                List<FightEvent> Mine, List<FightEvent> Deaths);

public sealed record DeathInfo(double T, string By, string ByName, string Cause, double? Dist, bool KnockedFirst, bool BledOut);

// ---- map view
public sealed record MapData(double End, List<Track> Tracks, List<ChestEvent> Chests, List<KillMark> Kills, List<Zone> Zones);

/// <param name="Role">0 me, 1 teammate, 2 others (only while near me).</param>
public sealed record Track(uint Id, int Role, float[] T, float[] X, float[] Y);

/// <param name="Who">me or teammate.</param>
/// <param name="Refill">This chest had already been searched earlier in the match (by anyone the replay saw).</param>
public sealed record ChestEvent(double T, string Kind, string Chest, string Who, double? X, double? Y, double? Cx, double? Cy, List<LootItem> Loot, bool Refill = false);

public sealed record KillMark(double T, double X, double Y, bool Down, bool ByMe, bool MeDied, string Victim);

public sealed record Zone(double Start, double End, double X, double Y, double R, double? Lx, double? Ly, double Lr);

/// <param name="TakenBy">me, teammate, other or nobody.</param>
public sealed record LootItem(string Id, string Name, string Kind, string Rarity, string TakenBy, bool Decoded);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
}
