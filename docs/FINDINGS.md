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

## Treasure rewards: what is certain (researched 16 Nov 2026)

Sources: **game files** (installed v43.10, read offline with CUE4Parse, including blueprint bytecode); **live config**
(Epic's hotfix the game caches in `Saved\PersistentDownloadDir\EMS`, seen 15 Nov; overrides the files); **replays** (45
season-43 matches); **Epic** (Fortnite Status 7 Nov 2026, the matching Epic help page "What are the drop rates of treasure
in chests...", patch notes v43.00 / v43.10); **datamine** (Llamalaxy on X, 7 and 12 Nov 2026, reading the 43.00 files);
**reported** (in-game event text posted by @FNBRunderground, 1 Nov 2026; guides).

**Certain: the game's logic** (`B_BuildingContainerOverride_TreasureHunt`, a component on every eligible chest)
- Opening: when a player starts to search the chest (`BlueprintServerOnAttemptInteract`), the server calls
  `RequestPrizeRoll(player, false)` and sets `RollRequested`. The chest's own search only goes ahead once the roll comes
  back (`OnPrizeRollComplete` -> `OwnerServerAttemptInteract`), so every prize is rolled before the chest opens.
- Breaking: when the chest is destroyed and it is not searched, not already rolled, and `IsDamageSourceEligible(cause)`
  (native code; what counts as eligible isn't in the blueprint), the server calls `RequestPrizeRoll(instigating player,
  true)`. So breaking rolls too; what the server does with the `true` (destruction) flag isn't visible. Datamine agrees
  ("or if destroyed before opening").
- Refills: `BP_HandleContainerUnSearched` sets `RollRequested = false`, so a refilled chest can request a roll again.
- No chest is marked in advance in anything the client receives: every regular chest has the same model and alternate
  model in replays (14,888 chests checked). The "loot tier 2" some chests carry from the start is not treasure (24 of
  them opened by the owner or team, no treasure followed).
- The gold treasure chest (`BGA_DelilahTreasureHuntAnimatedChest`, `_Rare`, `_Sprite`) is only spawned for Major or
  Supreme prizes: `B_TreasureHuntChestRoll.BP_BuildAnimatedChestSpawnParams` clears the chest class when the prize tier is
  Standard (Gizmos). Datamine agrees ("Gold chest animation is skipped for Standard tier prizes").
- Prizes reach one player controller per broadcast (`B_TreasureHuntChestRoll.BroadcastPrizes(InTargetController, ...)`),
  recorded in `B_PlayerController_TreasureHuntMatchDataComponent.EarnedPrizes`: the source of the post-match screen.

**Certain: game files and live config**
- Prizes (`DT_DelilahTreasureHunt_Prizes`): Pirate Shop Access (Supreme, `PreRollOnly`, `PerMatchMaxAmount = 1`, granted
  by a dedicated "Clover" granter); Gold Outfit (Supreme in the files, **Major** in the live config); Pickaxe, Back Bling,
  Jam Track, Loading Screen, Gold Car Decal (Major; rows 14-33 look like later weeks' rotations, by numbering only);
  Sprite Gizmos (Standard). Cosmetics and the token are account quests (`Quest_DPID_ItemN`). The weekly prize list in
  the frontend table is placeholder text; the real weekly pools are on Epic's server.
- Pre-roll mode: every prize row has `PreRollMode`, a setting with exactly two values (`EDelilahTreasureHuntPreRollMode`:
  `PreRollOnly` = 0, `AttemptPreRoll` = 1). Pirate Shop Access is the only `PreRollOnly` prize; the other 30 rows are
  `AttemptPreRoll`, and the live config doesn't change any of them (it only changes tiers and icons). Pirate Shop Access
  is also the only prize with a per-match cap (1), its own granter (`DelilahTreasureHuntCloverRewardGrant`) and a GUID id.
- Pre-roll on the server (native type definitions, from the mappings file): a prize request carries a `bPreRoll` flag
  (`DelilahTreasureHuntLocalPrizeRequest`); the server keeps `ForcedPrizeIds`, `RolledPrizeDefinitions` and
  `CurrentMatchRolledAmounts`; every treasure chest component has a `ForcedRewardId`; prize pools fetched from Epic's
  backend list each prize's `StartingInventory` and `RemainingInventory`. What the server does with these is compiled
  code, not in the readable game data.
- None of it reaches players: in replays the treasure controller on the game state carries no fields, and the chest's
  treasure component isn't replicated, so no replay can show a pre-rolled or forced prize.
- Not known: what "pre-roll" means in practice (when it happens, whether it pins a prize to a chest, whether refilled or
  broken chests can receive a pre-rolled prize). It can't be the per-chest roll itself, since every prize is rolled
  before its chest opens. The names fit a prize reserved ahead of time from Epic's limited stock (`AttemptPreRoll` = use
  a reserved one if there is one, otherwise roll at the chest; `PreRollOnly` = reserved only), but that is a reading of
  names, not a finding. Datamine agrees on the setting (Llamalaxy, reported by TheGamer 7 Nov 2026) and says it doesn't
  prove a token is assigned to a chest at match start. Epic declined to explain the odds ("everyone has an equal
  opportunity", per TheGamer).
- Gizmo prizes map to gizmo-wheel ("MorningBell") account entitlements (`DelilahTreasureHuntSharedGame.ini`: prizes 9-13
  -> `/MorningBell/Delilah/Item00-04`). In-game names: Extraction Accelerator, Portable Extractor, Golden Firework,
  Sprite Locator, Lucky Locator. Gizmos also come from Sprite Mastery and quests (Epic, v43.00). The loot tables still
  list gizmo vending rows, but Epic's v43.00 notes say vending machines were removed this season.
- Treasure-eligible chests (`DT_DelilahTreasureHunt_ChestReplaceClasses`): regular, Rare, Sprite x4, buried, dug-up
  treasure x4, Sprite Locator (Relic Finder) chests. Not extraction crates.
- Weeks run Sunday 14:00 UTC to Sunday 14:00 UTC; the hunt ends 5 Dec 2026 14:00 UTC (live config).
- The reward config names Ranked BR (`RankedPlaylistTags = Athena.Playlist.Habanero.BR`); the effect isn't in the data.

**Published by Epic:** the per-chest odds ("every time you open a chest in a normal match") and the 7 Nov Treasure Hours
boosts; "other than Gizmos, you can't get the same treasure twice".

**Reported, not confirmed by Epic:** squad sharing (event text: "there's a chance that every player on your squad will win
a free reward. Most of the time, this will be a Gizmo..."); Ranked (conflicting).

**Seen in replays:** one gold treasure chest in 45 season-43 matches, 0.1 s after the replay owner broke a fresh regular
chest (14 Nov, Squad). By the logic above, that roll was a Major or Supreme prize. The replay carried neither the prize nor
whether it was granted.

**Measured in replays: how fast chests refill.** No refill timer is in the client files or the live config (the chest only
has an `OnLootReset` event), so it is set on Epic's server. Measured as "search finished (`bAlreadySearched`) -> searcher
cleared" over 45 season-43 matches (`dropreport refills`): regular chests median 105 s (half between 62 s and 173 s,
n=700), always-spawn chests median 90 s (n=341), Rare chests median 92 s (n=113). The spread is wide, so it is not one
fixed timer. The client only hears about chests near the player, so a reset can arrive late: these are upper bounds.
A chest can only roll again once it has refilled; an already-searched chest doesn't roll when opened or broken.

**Certain: when a match earned something.** The post-match "Treasures Found" screen is only added when the player's
prizes for the match aren't empty (`BP_DelilahTreasureHunt_UIDirectorComponent.HasFoundAnyTreasure` ->
`B_PlayerController_TreasureHuntMatchDataComponent.GetAllPrizesEarnedThisMatch`, filled by `PrizeEarned` events). Fortnite's
log writes `Adding scene: UIScene_DelilahTreasureHunt_TreasuresFound_PostMatch` when it does. So that line means at least
one prize was earned that match; it doesn't say which. Its absence proves nothing if the post-match tabs were skipped.
Seen in the owner's log (12-15 Nov, 46 season-43 matches, each line within about a minute of a replay's end): 12 matches
earned something, 10 of 26 squad matches, 1 of 10 solo, 1 of 9 duo, including the match with the gold treasure chest.
The squad-heavy split fits the reported squad sharing, but doesn't prove it.

**Seen and confirmed by the owner (16 Nov evening, 6 matches):** the Treasures Found line appeared after 3 team matches,
one of them with a gold treasure chest the owner opened; the owner's rewards that session were a Loading Screen and a
Back Bling (both Major tier). That fits both rules above: the gold chest goes with a Major prize, and the screen line
goes with a prize earned. The owner thinks the Back Bling came with the gold chest (from memory, not certain). Then the
Loading Screen came from a match whose replay shows no gold chest: a teammate's chest out of view, or the reported squad
sharing. The replays can't tell these apart.

**Seen in replays: the moment a treasure is earned (16 Nov, 51 season-43 matches).** When the replay owner earns a prize,
the replay starts referencing the prize-handler ability
`/DelilahTreasureHuntGameplay/Gameplay/Abilities/PrizeHandlers/GA_TreasureHandler_Generic`. It appeared in 15 of 51
replays, and those are exactly the 15 matches whose Treasures Found screen is in the owner's Fortnite log (no misses, none
extra). It appears about 2 s after a gold treasure chest (the chest animation), and otherwise within about half a second of a
chest search or break. So replays alone tell which matches earned a treasure and when, back to the first replay, without the
log. Limit: only the first prize in a match shows (the class is already referenced after that). Tool: `dropreport prize-moments`.

What set each one off (the chest search or break just before; `scan-paths ... --list` gives the times):
- **The owner's own chest, 10 of 15:** opened 6 times, **broken 4 times** (13 Nov solo, 14 Nov ×3, including the gold-chest
  match). In one of the opens a teammate opened a chest 0.2 s earlier, so that one could be either. Breaking a chest does
  pay out, which matches the game's logic (Certain, above).
- **A teammate's chest, 4 of 15, with the owner touching no chest at that moment:** 14 Nov ×3 (one break, two opens) and
  16 Nov (a teammate broke a chest; the owner's last open was 7.6 s earlier). Plus 1 match with no chest in view at all (a
  teammate out of the replay's range). **So a teammate's chest can earn the owner a treasure:** the squad sharing that was
  only reported before is seen here. It's still a timing match, not Epic's word, but 4 of 4 land within 0.6 s.
- None of the 15 chests was a refill, so this says nothing yet about refilled chests (they're about 4% of chests).
- About one match in three earned something (15 of 51). By mode: squad 10 of 28, duo 2 of 10, solo 1 of 11, trio 2 of 2.

**Before a match: nothing.** Nothing the client receives says whether a match holds the token or any prize (see the
pre-roll section), and no chest looks different.

**Not knowable from the PC:** what a roll granted, whether a broken treasure chest still pays out, what "eligible damage"
is, and where a given Gizmo came from. Apart from the screen line above, Fortnite's local log doesn't record grants or
treasure-quest progress. The record is
the game's UI: the post-match "Treasures found" screen, the locker and gizmo wheel, the treasure-hunt quests.

Research tools: `dropreport scan-rewards`, `scan-paths`, `fields`, `chest-looks`, `gizmos` (CLI); the game-file reader in
the author's workbench (`--find`, `--dump` incl. data/curve tables, `--cat` for config files, `--json` with blueprint
bytecode).