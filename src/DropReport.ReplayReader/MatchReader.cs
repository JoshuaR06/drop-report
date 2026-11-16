using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Items;
using Microsoft.Extensions.Logging;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace DropReport.Replay;

public sealed class PickupState
{
    public uint Channel; public uint? Guid; public float FirstSeen; public string Item; public int Count; public int Level; public bool Tossed;
    public FVector InitPos, FinalPos, FirstPos, LastPos, RestPos; public uint? PickupTarget; public float? PickedAt; public uint? DroppedBy; public float? ClosedAt;
}

public sealed class DamageHit
{
    public float T; public uint? ChannelActor; public uint? HitActor, NonPlayerHitActor, SourceWeapon;
    public string Bone; public bool? Ads; public float? Magnitude;
    public bool Crit, Shield, ShieldDestroyed, Fatal; public FVector Loc;
}

/// <summary>
/// Reads one .replay file (read-only; never touches the game) and collects everything the analyzers need:
/// chest search events, pickups, damage cues, pawn tracks/weapons, player states and teams.
/// </summary>
public sealed class MatchReader : FortniteReplayReader.ReplayReader
{
    public MatchReader(ILogger logger) : base(logger, ParseMode.Debug, new HashSet<Type> { typeof(Chest) }) { }

    public ChestTracker Chests { get; } = new();
    public Dictionary<uint, uint> ChannelToGuid { get; } = new();          // channel -> actor guid
    public Dictionary<uint, uint?> PawnGuidToState { get; } = new();       // pawn guid -> player state guid
    public uint? RecorderState { get; private set; }                         // replay owner's player state guid
    public Dictionary<uint, int> StateTeam { get; } = new();               // player state guid -> team index
    public Dictionary<uint, string> StateUniqueId { get; } = new();        // player state guid -> unique id (= PlayerData.PlayerId)
    public Dictionary<uint, List<(float t, double x, double y)>> PawnTracks { get; } = new();   // ~2 Hz, replay clock
    public Dictionary<uint, List<(float t, uint weapon)>> PawnWeapons { get; } = new();          // CurrentWeapon changes
    public Dictionary<uint, float> BusJumpAt { get; } = new();             // pawn -> first bIsSkydivingFromBus (end of warmup)
    public Dictionary<uint, string> ActorClass { get; } = new();           // actor guid -> archetype (bare class name in practice)
    public Dictionary<uint, float> ActorOpenedAt { get; } = new();
    public List<DamageHit> Hits { get; } = new();
    public double? WorldMinusDemo { get; private set; }                      // server world clock minus replay clock
    public float LastTime { get; private set; }
    public IEnumerable<PickupState> AllPickups => _pickups.Values.Concat(_closedPickups);
    /// <summary>Research only: set before reading to keep every asset path the replay referenced (item definitions,
    /// actor classes, ...). The parser clears its own cache at the end, so it is copied while reading.</summary>
    public bool CollectPaths { get; set; }
    public Dictionary<uint, string> AllPaths { get; } = new();
    /// <summary>Research: networked class (export group) -> its replicated field names, for groups matching GroupFilter.</summary>
    public Dictionary<string, HashSet<string>> GroupFields { get; } = new();
    /// <summary>Every update seen on a treasure reveal chest (time, actor, object); merged per actor by the analyzer.</summary>
    public List<(float T, uint? Actor, object Update)> PrizeUpdates { get; } = new();
    public System.Text.RegularExpressions.Regex GroupFilter { get; set; }
    private int _sinceCopy;

    /// <summary>Research (CollectPaths): each chest's look the first time each field arrives (model, alt model index, loot tier, upgrade).</summary>
    public Dictionary<uint, Dictionary<string, string>> ChestLooks { get; } = new();
    private void NoteLook(uint? guid, TrackedChest c)
    {
        if (guid is not uint g) return;
        var d = ChestLooks.TryGetValue(g, out var x) ? x : ChestLooks[g] = new() { ["class"] = c.GetType().Name, ["firstSeen"] = CurrentTimeSeconds.ToString("0.0") };
        void Set(string k, string v) { if (v != null && !d.ContainsKey(k)) d[k] = v; }
        Set("mesh", c.MeshRef is uint m ? (GuidPaths.GetValueOrDefault(m) ?? AllPaths.GetValueOrDefault(m) ?? $"#{m}") : null);
        Set("searchedMesh", c.SearchedMeshRef is uint s ? (GuidPaths.GetValueOrDefault(s) ?? AllPaths.GetValueOrDefault(s) ?? $"#{s}") : null);
        Set("altMesh", c.AltMesh != null ? $"{c.AltMesh.TotalBits}b:{Convert.ToHexString(c.AltMesh.Bytes ?? Array.Empty<byte>())}" : null);
        Set("lootTier", c.ReplicatedLootTier != 0 ? c.ReplicatedLootTier.ToString() : null);
        if (c.ReplicatedLootTier != 0) Set("lootTierAt", CurrentTimeSeconds.ToString("0.0"));
        Set("upgrade", c.ChosenRandomUpgrade?.ToString());
    }

    private void SnapshotGroups()
    {
        if (GroupFilter == null) return;
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cache = typeof(Unreal.Core.ReplayReader<FortniteReplay>).GetField("_netGuidCache", all)?.GetValue(this);
        foreach (var holder in new[] { cache, (object)this })
            for (var ty = holder?.GetType(); ty != null; ty = ty.BaseType)
                foreach (var fld in ty.GetFields(all | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (fld.GetValue(holder) is not System.Collections.IDictionary dict) continue;
                    foreach (System.Collections.DictionaryEntry de in dict)
                    {
                        if (de.Key is not string key || !GroupFilter.IsMatch(key) || de.Value == null) continue;
                        var exports = de.Value.GetType().GetProperty("NetFieldExports")?.GetValue(de.Value) as System.Collections.IEnumerable
                                      ?? de.Value.GetType().GetField("NetFieldExports")?.GetValue(de.Value) as System.Collections.IEnumerable;
                        var set = GroupFields.TryGetValue(key, out var s) ? s : GroupFields[key] = new();
                        if (exports == null) continue;
                        foreach (var ex in exports)
                            if (ex?.GetType().GetProperty("Name")?.GetValue(ex) is string n) set.Add(n);
                    }
                }
    }

    private readonly Dictionary<uint, PickupState> _pickups = new();
    private readonly List<PickupState> _closedPickups = new();
    private Dictionary<uint, string> _guidPaths;
    // the base reader keeps its guid -> path cache private; read it by reflection
    private Dictionary<uint, string> GuidPaths => _guidPaths ??= (typeof(Unreal.Core.ReplayReader<FortniteReplay>)
        .GetField("_netGuidCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.GetValue(this) as NetGuidCache)?.NetGuidToPathName ?? new();

    public (double x, double y)? PositionAt(IEnumerable<uint> pawns, float t, double maxDt = 4.0)
    {
        (double x, double y)? best = null; var bestDt = maxDt;
        foreach (var p in pawns)
            if (PawnTracks.TryGetValue(p, out var tr))
                foreach (var q in tr) { var dt = Math.Abs(q.t - t); if (dt < bestDt) { bestDt = dt; best = (q.x, q.y); } }
        return best;
    }

    // Debug mode throws on event types newer than the library (e.g. "Timecode"); skip them, the base reader seeks on.
    public override void ReadEvent(Unreal.Core.FArchive archive)
    {
        try { base.ReadEvent(archive); }
        catch (Exception e) when (e.GetType().Name == "UnknownEventException") { }
    }

    protected override void OnChannelClosed(uint channelIndex, NetworkGUID actor)
    {
        if (_pickups.Remove(channelIndex, out var pk)) { pk.ClosedAt = CurrentTimeSeconds; _closedPickups.Add(pk); }
        base.OnChannelClosed(channelIndex, actor);
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID actor)
    {
        base.OnChannelOpened(channelIndex, actor);
        if (_pickups.Remove(channelIndex, out var oldp)) _closedPickups.Add(oldp);   // channel reuse: a new actor
        Chests.Reset(channelIndex);
        if (actor != null) ChannelToGuid[channelIndex] = actor.Value;
        var arch = Channels[channelIndex]?.Actor?.Archetype?.Value;
        if (actor != null && arch != null && GuidPaths.TryGetValue(arch.Value, out var path))
        {
            ActorClass[actor.Value] = path;
            ActorOpenedAt.TryAdd(actor.Value, CurrentTimeSeconds);
        }
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        if (CurrentTimeSeconds > LastTime) LastTime = CurrentTimeSeconds;
        if (CollectPaths && ++_sinceCopy >= 500 && GuidPaths.Count != AllPaths.Count)
        {
            _sinceCopy = 0;
            foreach (var kv in GuidPaths) AllPaths[kv.Key] = kv.Value;
            SnapshotGroups();
        }
        uint? guid = ChannelToGuid.TryGetValue(channelIndex, out var g) ? g : null;
        if (exportGroup is TreasureHuntPrizeChest) PrizeUpdates.Add((CurrentTimeSeconds, guid, exportGroup));
        switch (exportGroup)
        {
            case BaseContainer c:
                Chests.Update(channelIndex, guid, c.GetType().Name, (c as TrackedChest)?.SearchingPawn ?? 0, c.SearchAnimationCount,
                              c.bDestroyed, c.bAlreadySearched, CurrentTimeSeconds);
                if (CollectPaths && c is TrackedChest tc) NoteLook(guid, tc);
                break;
            case PlayerPawn pawn when guid is uint pg:
                if (pawn.PlayerState is uint ps && ps != 0) PawnGuidToState[pg] = ps;   // 0 on early updates: ignore
                if (pawn.bIsSkydivingFromBus == true) BusJumpAt.TryAdd(pg, CurrentTimeSeconds);
                if (pawn.CurrentWeapon is uint cw && cw != 0)
                {
                    if (!PawnWeapons.TryGetValue(pg, out var wl)) PawnWeapons[pg] = wl = new();
                    if (wl.Count == 0 || wl[^1].weapon != cw) wl.Add((CurrentTimeSeconds, cw));
                }
                var loc = pawn.ReplicatedMovement?.Location;
                if (loc != null && !(loc.X == 0 && loc.Y == 0))
                {
                    if (!PawnTracks.TryGetValue(pg, out var tr)) PawnTracks[pg] = tr = new();
                    if (tr.Count == 0 || CurrentTimeSeconds - tr[^1].t >= 0.5f) tr.Add((CurrentTimeSeconds, loc.X, loc.Y));
                }
                break;
            case GameState gs:
                if (gs.RecorderPlayerState?.Value is uint rec && rec != 0) RecorderState = rec;
                var wt = gs.ReplicatedWorldTimeSecondsDouble ?? gs.ReplicatedWorldTimeSeconds;
                if (wt is double w && w > 0) WorldMinusDemo = w - CurrentTimeSeconds;
                break;
            case FortPlayerState fps when guid is uint sg:
                if (fps.TeamIndex is int team) StateTeam[sg] = team;
                if ((fps.UniqueId ?? fps.UniqueID ?? fps.BotUniqueId) is string uid && uid.Length > 0) StateUniqueId[sg] = uid;
                break;
            case FortniteReplayReader.Models.NetFieldExports.RPC.BatchedDamageCues d:
                // arrives on the SHOOTER's pawn channel; SourceWeapon = the weapon actor that fired
                Hits.Add(new DamageHit
                {
                    T = CurrentTimeSeconds, ChannelActor = guid, HitActor = d.HitActor, NonPlayerHitActor = d.NonPlayerHitActor,
                    Magnitude = d.Magnitude, Crit = d.bIsCritical == true, Shield = d.bIsShield == true,
                    ShieldDestroyed = d.bIsShieldDestroyed == true, Fatal = d.bIsFatal == true, SourceWeapon = d.SourceWeapon, Loc = d.Location,
                    Bone = (d as TrackedDamageCues)?.HitBoneName, Ads = (d as TrackedDamageCues)?.bFiredWhileTargeting
                });
                break;
            case FortPickup p:
                {
                    if (!_pickups.TryGetValue(channelIndex, out var pk))
                        _pickups[channelIndex] = pk = new PickupState { Channel = channelIndex, Guid = guid, FirstSeen = CurrentTimeSeconds };
                    if (p.ItemDefinition?.Name is string nm && nm.Length > 0) pk.Item = nm;
                    if (p.Count is int cnt) pk.Count = cnt;
                    if (p.bTossedFromContainer == true) pk.Tossed = true;
                    if (p.LootInitialPosition != null && pk.InitPos == null) pk.InitPos = p.LootInitialPosition;
                    if (p.LootFinalPosition != null) pk.FinalPos = p.LootFinalPosition;
                    if (p.ReplicatedMovement?.Location is FVector pl) { pk.FirstPos ??= pl; pk.LastPos = pl; }
                    if (p.FinalTossRestLocation != null) pk.RestPos = p.FinalTossRestLocation;
                    if (p.PickupTarget is uint tgt && tgt != 0) { pk.PickupTarget = tgt; pk.PickedAt ??= CurrentTimeSeconds; }
                    if (p.bPickedUp == true) pk.PickedAt ??= CurrentTimeSeconds;
                    if (p.PawnWhoDroppedPickup is uint dp && dp != 0) pk.DroppedBy = dp;
                    if (p.Level is int lv) pk.Level = lv;
                }
                break;
        }
    }
}
