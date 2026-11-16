using FortniteReplayReader.Models.NetFieldExports.Items;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace DropReport.Replay;

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
    // research: the chest's model, which a treasure pre-roll might swap (the library skips these fields)
    [NetFieldExport("StaticMesh", RepLayoutCmdType.PropertyObject)] public uint? MeshRef { get; set; }
    [NetFieldExport("SearchedMesh", RepLayoutCmdType.PropertyObject)] public uint? SearchedMeshRef { get; set; }
    [NetFieldExport("AltMeshIdx", RepLayoutCmdType.Property)] public DebuggingObject AltMesh { get; set; }
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

/// <summary>Season 43: the chest you dig up with a Sprite Treasure Map (same searched/searcher fields as a chest).</summary>
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest.B_SpritePosters_Booty_Chest_C", minimalParseMode: ParseMode.Debug)]
public class SpriteTreasureChest : TrackedChest { }
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest_Variant1.B_SpritePosters_Booty_Chest_Variant1_C", minimalParseMode: ParseMode.Debug)]
public class SpriteTreasureChestV1 : SpriteTreasureChest { }
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest_Variant2.B_SpritePosters_Booty_Chest_Variant2_C", minimalParseMode: ParseMode.Debug)]
public class SpriteTreasureChestV2 : SpriteTreasureChest { }
[NetFieldExportGroup("/SpritePosters/Blueprints/Items/Booty/B_SpritePosters_Booty_Chest_Variant3.B_SpritePosters_Booty_Chest_Variant3_C", minimalParseMode: ParseMode.Debug)]
public class SpriteTreasureChestV3 : SpriteTreasureChest { }

// The other Sprite Chest looks (season 43). All of these are on the game's treasure-eligible list
// (DT_DelilahTreasureHunt_ChestReplaceClasses).
[NetFieldExportGroup("/SpritePosters/Gameplay/RelicChests/B_Delilah_TieredChest_Relics_Variant1.B_Delilah_TieredChest_Relics_Variant1_C", minimalParseMode: ParseMode.Debug)]
public class RelicChestV1 : RelicChest { }
[NetFieldExportGroup("/SpritePosters/Gameplay/RelicChests/B_Delilah_TieredChest_Relics_Variant2.B_Delilah_TieredChest_Relics_Variant2_C", minimalParseMode: ParseMode.Debug)]
public class RelicChestV2 : RelicChest { }
[NetFieldExportGroup("/SpritePosters/Gameplay/RelicChests/B_Delilah_TieredChest_Relics_Variant3.B_Delilah_TieredChest_Relics_Variant3_C", minimalParseMode: ParseMode.Debug)]
public class RelicChestV3 : RelicChest { }

/// <summary>The buried chest a Sprite Locator (Relic Finder) leads you to.</summary>
[NetFieldExportGroup("/ExtractionRelicFinder/Gameplay/Chests/B_Athena_BuriedChest_RelicFinder.B_Athena_BuriedChest_RelicFinder_C", minimalParseMode: ParseMode.Debug)]
public class RelicFinderChest : TrackedChest { }

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

/// <summary>
/// Season 43 treasure reveal: when a chest holding a treasure is opened or broken, the game swaps in this animated
/// chest (one class per chest kind, from DT_DelilahTreasureHunt_ChestReplaceClasses). Fields the replay may carry: the
/// prize, who opened it, the source chest, the tier, success. Prize/tier types are not confirmed yet, so they are raw.
/// </summary>
[NetFieldExportGroup("/DelilahTreasureHuntGameplay/Gameplay/Actors/BGA_DelilahTreasureHuntAnimatedChest.BGA_DelilahTreasureHuntAnimatedChest_C", minimalParseMode: ParseMode.Debug)]
public class TreasureHuntPrizeChest : INetFieldExportGroup
{
    [NetFieldExport("PrizeTemplateId", RepLayoutCmdType.Property)] public DebuggingObject PrizeTemplateId { get; set; }
    [NetFieldExport("OpeningPawn", RepLayoutCmdType.PropertyObject)] public uint? OpeningPawn { get; set; }
    [NetFieldExport("SourceContainer", RepLayoutCmdType.PropertyObject)] public uint? SourceContainer { get; set; }
    [NetFieldExport("Tier", RepLayoutCmdType.Property)] public DebuggingObject Tier { get; set; }
    [NetFieldExport("TierData", RepLayoutCmdType.Property)] public DebuggingObject TierData { get; set; }
    [NetFieldExport("bSuccess", RepLayoutCmdType.PropertyBool)] public bool? bSuccess { get; set; }
    [NetFieldExport("bDestroyed", RepLayoutCmdType.PropertyBool)] public bool? bDestroyed { get; set; }
    [NetFieldExport("bFromDestruction", RepLayoutCmdType.PropertyBool)] public bool? bFromDestruction { get; set; }
    [NetFieldExport("bInstantDeath", RepLayoutCmdType.PropertyBool)] public bool? bInstantDeath { get; set; }
    [NetFieldExport("OwnerPersistentID", RepLayoutCmdType.Property)] public DebuggingObject OwnerPersistentID { get; set; }
}

[NetFieldExportGroup("/DelilahTreasureHuntGameplay/Gameplay/Actors/BGA_DelilahTreasureHuntAnimatedChest_Rare.BGA_DelilahTreasureHuntAnimatedChest_Rare_C", minimalParseMode: ParseMode.Debug)]
public class TreasureHuntPrizeChestRare : TreasureHuntPrizeChest { }

[NetFieldExportGroup("/DelilahTreasureHuntGameplay/Gameplay/Actors/BGA_DelilahTreasureHuntAnimatedChest_Sprite.BGA_DelilahTreasureHuntAnimatedChest_Sprite_C", minimalParseMode: ParseMode.Debug)]
public class TreasureHuntPrizeChestSprite : TreasureHuntPrizeChest { }