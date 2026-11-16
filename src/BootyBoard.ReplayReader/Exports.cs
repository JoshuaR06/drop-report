using FortniteReplayReader.Models.NetFieldExports.Items;
using Unreal.Core.Attributes;
using Unreal.Core.Models.Enums;

namespace BootyBoard.Replay;

// Custom export groups. The parser picks these over its own classes for the same path, because they live in an
// assembly whose name contains "ReplayReader".

/// <summary>Regular chest. The library's Chest misses SearchingPawn (who searched it).</summary>
[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/Tiered_Chest_Athena.Tiered_Chest_Athena_C", minimalParseMode: ParseMode.Debug)]
public class TrackedChest : BaseContainer
{
    [NetFieldExport("SearchingPawn", RepLayoutCmdType.PropertyObject)]
    public uint? SearchingPawn { get; set; }
    [NetFieldExport("bDestroyOnPlayerBuildingPlacement", RepLayoutCmdType.PropertyBool)]
    public bool bDestroyOnPlayerBuildingPlacement { get; set; }
    [NetFieldExport("ResourceType", RepLayoutCmdType.Enum)]
    public int ResourceType { get; set; }
}

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/AlwaysSpawn_NormalChest.AlwaysSpawn_NormalChest_C", minimalParseMode: ParseMode.Debug)]
public class AlwaysSpawnNormalChest : TrackedChest { }

[NetFieldExportGroup("/Game/Building/ActorBlueprints/Containers/AlwaysSpawn_RareChest.AlwaysSpawn_RareChest_C", minimalParseMode: ParseMode.Debug)]
public class AlwaysSpawnRareChest : TrackedChest { }

/// <summary>Sprite Chest (DisplayName in the game files).</summary>
[NetFieldExportGroup("/RelicChest/Gameplay/B_TieredChest_Relics.B_TieredChest_Relics_C", minimalParseMode: ParseMode.Debug)]
public class RelicChest : TrackedChest { }

[NetFieldExportGroup("/Game/Athena/Items/ForagedItems/BuriedChest/B_Athena_BuriedChest_Chest.B_Athena_BuriedChest_Chest_C", minimalParseMode: ParseMode.Debug)]
public class BuriedChest : TrackedChest { }

[NetFieldExportGroup("/ExtractionPointGameplay/Gameplay/BGA_ExtractionPointGameplay_Chest.BGA_ExtractionPointGameplay_Chest_C", minimalParseMode: ParseMode.Debug)]
public class ExtractionChest : TrackedChest { }

/// <summary>Damage cues with the fields the library skips (hit bone, ADS, hit component).</summary>
[NetFieldExportGroup("/Script/FortniteGame.FortPawn:NetMulticast_Athena_BatchedDamageCues", minimalParseMode: ParseMode.Minimal)]
public class TrackedDamageCues : FortniteReplayReader.Models.NetFieldExports.RPC.BatchedDamageCues
{
    [NetFieldExport("HitBoneName", RepLayoutCmdType.PropertyName)]
    public string HitBoneName { get; set; }
    [NetFieldExport("bFiredWhileTargeting", RepLayoutCmdType.PropertyBool)]
    public bool? bFiredWhileTargeting { get; set; }
    [NetFieldExport("bOffhandWasFired", RepLayoutCmdType.PropertyBool)]
    public bool? bOffhandWasFired { get; set; }
    [NetFieldExport("bIsBeam", RepLayoutCmdType.PropertyBool)]
    public bool? bIsBeam { get; set; }
    [NetFieldExport("HitComponent", RepLayoutCmdType.PropertyObject)]
    public uint? HitComponent { get; set; }
}
