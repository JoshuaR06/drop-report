# How Drop Report decodes a replay

Everything here was verified against real replays and the game's own files (game version 43.10). Code references are
to `src/DropReport.ReplayReader`.

## Reading
- Parser: FortniteReplayReader 3.2 in `ParseMode.Debug` (chests and some stats only parse in Debug). Custom export
  classes load only from assemblies whose name contains "ReplayReader" (`Exports.cs`).
- Debug mode throws on event types newer than the library (e.g. "Timecode"): skipped (`MatchReader.ReadEvent`).
- The owner: `GameState.RecorderPlayerState` = my player state; my pawns = pawns whose `PlayerState` is that state.
  `PlayerState` is 0 on early pawn updates: ignored.
- Actor classes: the parser keeps its guid -> path cache private; it is read by reflection. In practice the cache only
  holds the bare class name (`B_Assault_Heavy_Recoil_Athena_C`).
- Clocks: most data uses the replay clock. Kill feed and storm use server world time; offset = `ReplicatedWorldTimeSeconds`
  minus replay time. The kill feed's float `ReplicatedWorldTimeSeconds` is one constant per match: use the Double field.
- Warmup island: everything before my first `bIsSkydivingFromBus` is pre-game and is ignored for stats.

## Chests (`ChestTracker.cs`)
- Our `TrackedChest` export reads `SearchingPawn` (the library's class doesn't). Chest kinds: Tiered_Chest_Athena
  ("Chest"), AlwaysSpawn_NormalChest, AlwaysSpawn_RareChest ("Rare Chest"), B_TieredChest_Relics ("Sprite Chest"),
  buried chest, extraction crate. Names: DisplayName in the game files where one exists.
- Chests respawn, so count search EVENTS: a new searcher, or the search-animation counter going up.
- Already searched when first seen = "carried" (searched before it came into view), not counted.
- Broken = searcher stamped + destroyed at the same moment without `bAlreadySearched`, or destroyed < 1 s after an
  "opened" event.
- Chests have no world location (static actors); positions come from the searcher's pawn.
- Team chests: the same events for teammates' pawns. Only what my client could see: a minimum.

## Loot per chest (`LootMatcher.cs`)
- A chest's items are `FortPickupAthena` with `bTossedFromContainer`, all in one packet within ~0.05 s of the search.
- Cluster tossed pickups by first-seen time (gap < 0.03 s) in -0.4..+0.8 s around the search; keep clusters with an item
  within 7 m of the searcher; a cluster with no positions only within 0.15 s.
- A fresh toss's first position decodes at 1/10 scale: distance is tried at x1 and x10.
- `PickupTarget` = the pawn who took it (me / teammate / enemy / left). `ItemDefinition.Count` is always 0.

## Damage (`MatchAnalyzer.cs`)
- `NetMulticast_Athena_BatchedDamageCues` arrives on the SHOOTER's pawn channel; `SourceWeapon` = the weapon actor
  that fired (matched the held weapon 70/70). Our export adds `HitBoneName`, `bFiredWhileTargeting`, `HitComponent`.
- `HitActor` can be a chest, a building or a vehicle: only pawns count. Bots = class contains `Phoebe`.
- `bIsCritical` = headshot. Shotguns: one cue per shot (pellets summed).
- Slingshot hits arrive as blank cues (no target, damage or weapon): counted for the held weapon, no damage. Melee items
  send no cues at all.

## Knocks, eliminations, deaths (`FightAnalyzer.cs`)
- Source: `replay.Eliminations` (complete). Ids = `PlayerData.PlayerId`. The kill feed is not complete (later changes
  overwrite an entry) but adds damage tags and distance (uu / 100 = m).
- Victim -> pawns: `FortPlayerState.UniqueId` (or `BotUniqueId`) == PlayerId -> state -> pawns. `PlayerData.Id` is not
  a usable channel.
- Weapon credit: my last detailed hit on the victim (<= 5 s before), then for a bleed-out the knocking weapon, then the
  last non-consumable item held.
- GunType: 2 pistol (includes the slingshot), 3 shotgun, 4 rifle, 5 SMG, 8 melee, 50 = team wiped (downed players
  eliminated when their last teammate dies, credited to the knocker).
- The game's own elimination count = ours minus finishes of another team's knock and minus team-wipe credits.

## Players, bots, NPCs and names (`Rules.Kind`, `Rules.DisplayName`)
- Every participant is in the replay's player list with an `IsBot` flag. Regular bots also have a `BotId` and use the
  AI character class `BP_PlayerPawn_Athena_Phoebe_C`. Story NPCs and bosses (e.g. Buggy, Monkey D. Luffy) are bots
  WITHOUT a BotId.
- Names: `PlayerName` (bots too; NPCs also carry it in `PlayerNameCustomOverride`). Players in anonymous mode still
  have their account name in the replay but no anonymous tag (0 of 23 anonymous players over 677 players had one), so
  Drop Report shows them as "Anonymous player" to respect their choice. A streamer-mode name is used when present.

## Map overlay (`MapGeo.cs`, `GameDataBuilder.MapCamera`)
- The replay's own map fields (`MapData.WorldGrid*`, `GameData.MapInfo`) decode as garbage/empty with this parser.
- The game's map UI data says where the map image sits: `Content/UI/IngameMap/<Map>/<Map>_MapUIData`
  (`FortMapUIData`) -> `CaptureCameraFrontendData` -> `OrthoWidth` (square width in world units) + `WorldOffset`
  (centre). v43.10: `Delilah_Terrain`, centre (8500, 508), width 299,926. World X runs left to right on the image and
  world Y top to bottom (as on the in-game map).
- The main map's image is streamed by the game into an on-demand cache that can't be read, so the image comes from
  fortnite-api.com/images/map.png (same capture, 2048 px), downloaded at setup with the user's consent and never
  redistributed. Location names + world positions: fortnite-api.com/v1/map.
- Verified: of 450 placements tried around the camera numbers, the camera's own were the best fit (93% of 10k player
  positions on land vs 66-88% for the other orientations).
- Other map variants in `BlastBerryMapUI` (BlastBerry, DashBerry, JumpBear...) have their own images in the game files
  (`Capture_Iteration_Discovered_<Map>`) and camera data; not used yet.

## Item names (`src/DropReport.GameData`)
- Read with CUE4Parse from the installed game (IoStore). Needs the version's AES key, a mappings (.usmap) file and Oodle.
- The name is `ItemName` (not DisplayName). Rarity is a gameplay tag in the `DataList` fragments
  (`Rarity.VeryRare` = Epic, `SuperRare` = Legendary, `Mythic`, `Transcendent` = shown in game as Exotic). The id
  suffix (_VR...) disagrees with the tag on 72 of 1,768 items, so the tag wins.
- `WeaponActorClass` links an item to the weapon class the replay shows. One class covers several items (rarities and
  variants); the exact one is my pickup whose id matches, near the time the weapon appeared.

## Elimination credit follows the knock

Epic's scoreboard credits an elimination to the player who **knocked** the victim, whoever finishes them (a teammate, a bleed-out, the storm, a team wipe). Finishing someone another player knocked earns nothing. Counting the replay's elimination events that way matches Epic's own end-of-match elimination count exactly in 94 of 100 test matches (42 of 45 in season 43); crediting the final blow instead matched only 74. The 6 left are off by one: a knocked player finished out of the replay's view, an elimination missing from the replay, or a special Solo mode with team-wipe events. `FightEvent.Credit` carries the rule; every count (totals, per weapon, players vs bots, records) uses it, while the match timeline still shows who landed the final blow.

## Treasure rewards: what replays show (researched 16 Nov 2026, 45 season-43 matches)

`dropreport scan-rewards <folder> [since]` reads replays with every referenced asset path and every networked class's
field list captured (the parser clears its own caches at the end of a read, so `MatchReader.CollectPaths` copies them
while reading).

- **The Pirate Shop token and the weekly rewards leave no trace.** No item, no event, no networked field. The treasure
  hunt's game-state component (`B_GameState_TreasureHuntController`) and the playtime-rewards player component are in
  every replay but replicate nothing; `GA_Prizes_EventListener` is an ability that only appears as a name. Rewards are
  decided and granted on Epic's servers. Odds are the best a replay tool can offer.
- **Gizmos are not chest drops.** The game has in-match items tagged `Item.Gizmo.*` (Extraction Accelerator, Llama
  Supply Drop, Spicy Taco, Snack-O'-Lantern), but none was seen as a pickup in 45 matches; at 1 in 127 over ~1,400
  rolling chests about 11 would be expected. Epic's "any Gizmo" chest reward is account-side too.
- **Treasure-key prize chests are replicated, barely.** `BGA_DelilahTreasureHuntAnimatedChest` carries
  `PrizeTemplateId`, `OpeningPawn`, `SourceContainer`, `Tier`, `bSuccess`; it appeared once in 45 matches, and only its
  destruction was recorded (no prize). Captured by `TreasureHuntPrizeChest` for research runs.
- **Dug-up treasure chests were not counted before.** The chest you dig up with a Sprite Treasure Map
  (`B_SpritePosters_Booty_Chest`) has the same searched/searcher fields as a chest; now tracked as "Dug-up treasure
  chest" (5 in season 43: 3 by the replay owner, 2 by teammates). They count as rolling chests.