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
  by its own `CloverRewardGrant` granter); Gold Outfit (Supreme in the files, **Major** in the live config); Pickaxe, Back Bling,
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
- No blueprint touches pre-roll: a search of every treasure-hunt asset's bytecode for `PreRoll`, `ForcedPrize`,
  `ForcedReward`, `Inventory` and `RolledPrize` finds only the prize tables. It is all compiled server code.
- "Clover" is the code name of the whole treasure-hunt system (`DelilahTreasureHuntCloverController_Server`, `_Frontend`),
  not only the token's granter. The prize granters are: none (the 25 cosmetic rows: account quests), `InGameItemGranter`
  (the 5 Gizmo rows) and `CloverRewardGrant` (Pirate Shop Access only).
- **Prize reservations (game config `DefaultEngine.ini`, also re-sent in the live config on 16 Nov):** Epic runs a
  backend service named `clover` with exactly two endpoints:
  `.../clover/api/v1/{level}/deployments/{deploymentId}/domains/{domain}/sessions/{sessionId}/prizes/{prizeId}/prize-reservation`
  and the same path ending `/prize-reservation/claim`. Both use the dedicated-server path (`ds`), so the match server
  calls them, not the player's PC. A reservation is made for one **match session** and one **prize id**, then claimed.
  Pirate Shop Access is the only prize whose id is a GUID (the other 30 are `DelilahPrize:dpid_itemN`). The files don't
  say which prizes go through it.
- A roll result (`DelilahTreasureHuntPrizeRollResult`) carries a pool id, a **list** of prize ids and a backend error
  code; pools carry start/end times and per-prize `StartingInventory` / `RemainingInventory`. So prizes come from
  limited stock held by Epic's backend, not from odds decided inside the match.
- Every cosmetic and token quest is completed by `CustomVerb.DelilahTreasureHunt.ManualReward` ("Reserved for
  manual-only prize objectives; gameplay must not broadcast this verb."): the server grants them directly, nothing in
  gameplay does. The server also tracks `bHasFinishedRewardDispatch`.
- Ranked: the reward config lists `Athena.Playlist.Habanero.BR` (Ranked Battle Royale) as a ranked playlist and has
  lists for prizes suppressed or disabled in ranked. Both lists are empty in the game files and the live config doesn't
  fill them, so as shipped, ranked doesn't switch off any prize. (Epic's backend could still treat ranked differently.)
- Nothing on the PC sees a reservation: the player's Fortnite log has no reservation, roll or pre-roll lines (14 logs),
  replays carry no fields, and the token's prize row is not changed by the live config.
- **What pre-roll most likely means (inference, not a finding):** an attempt to reserve the prize from Epic's limited
  stock for this match. `AttemptPreRoll` = use a reservation if one is granted, otherwise roll normally; `PreRollOnly` =
  the token can only be awarded through a successful reservation, never by a normal chest roll. The reservation URL is
  scoped to the match session (fits "decided once per match"), but the prize request that carries `bPreRoll` also
  carries the chest and the player (fits "attempted at each chest search"). The files can't settle which. Either way,
  `PerMatchMaxAmount = 1` caps it at one token per match for the whole lobby.
- Not known: when the reservation is attempted (match start or each search), how a reserved token is matched to a chest
  or player (`ForcedPrizeIds` is keyed by an object; which kind isn't recorded), and whether broken or refilled chests can
  receive it. Datamine agrees on the setting (Llamalaxy, reported by TheGamer 7 Nov 2026) and says it doesn't prove a
  token is assigned to a chest at match start. Epic declined to explain the odds ("everyone has an equal opportunity",
  per TheGamer).
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
Drop Report uses this from summary version 8 (`MatchReader.PrizeHandlers`, `Treasure.Attribute`); the log is now a backup.

What the handler is (game files): `GA_Prizes_EventListener` hears the server's prize event and, for each prize, grants the
handler named by the prize's `EventHandler` tag (`DT_PrizeHandlers`) to the player who earned it. Every one of the 31
prizes (the token, cosmetics and gizmos) uses the same tag, `DelilahTreasureHunt.PrizeHandler.Tier.3`, so the same handler,
`GA_TreasureHandler_Generic`; the only other handler is for treasure keys. The handler sends the prize id and the chest to
the player's game (`Client_ReplicateContext(PrizeId, Container)`), but that message isn't recorded in replays (the class
replicates no fields, and no group for the message exists). So the handler says when and to whom, never which prize.

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

**Seen in the owner's log: counting more than one treasure (16 Nov, 51 matches).** Fortnite logs
`... Current active widget [WBP_DelilahTreasureHunt_RewardWon_C_N] does not want to show content` when its in-match "reward
won" popup is asked to show. Most matches log it once, about a minute in, whether or not anything was won (some log it
not at all). Matches with a Treasures Found screen log it once more per treasure: 12 matches once more, 2 matches twice
more (14 Nov 11:07 AM and 12:36 PM, both squads: two treasures each), 1 match not at all. The 36 matches without the screen
never logged an extra one. Tonight's three extra lines each came shortly after the replay's prize moment. So the log can
count a second treasure that the replay can't. This is a reading of matching patterns, not of the game's code, and it only
covers the days Fortnite keeps its log. Drop Report counts popups between one match's treasure-UI set-up line
(`Adding scene: UIScene_DelilahTreasureHunt_RewardWon`) and the next match's, so a following match's popup isn't counted.

**Other numbers in Fortnite's log (16 Nov survey).** Mostly cosmetics loading, voice chat and asset streaming. Kept by Drop
Report, numbers only: the profiler's `worstperiodofpingvariancems` (climbs during a match; its peak is shown per match as
"ping variation", without a verdict, since what's normal for it isn't documented; the owner's matches split into about
3-8 ms and 45-53 ms) and `maploadscreenduration`. Not used: ranked lines (`LogFortControllerComponentHabanero`, several
players' progress, can't be tied to the owner), `averagereplicationblockedseconds`/`maxreplicationblockedseconds` (unclear
whether it's the network or the replay recorder), and every line type that carries names or account ids (profile, quest,
cosmetics, party, voice).

What doesn't work: the owner's granted-ability list (`FortAbilitySystemComponentAthena`, fields ability/Handle/Level/
SourceObject) is in the replay's layout, but the parser doesn't hand over its entries, so it can't count handler grants.

**Other players' treasures.** The only thing other players can see is the gold treasure chest (Major or Supreme prizes),
for chests near the replay owner. Its tier and prize aren't replicated (`Tier`, `TierData`, `PrizeTemplateId` never arrive),
and the same two files load for every gold chest (`BGA_DelilahTreasureHuntAnimatedChest`, `MR_Athena_Chest_Parent`). The
treasure-hunt blueprints never announce a prize to other players (no multicast; the prize quests have
`bDisplayDynamicAnnouncementUpdate = false`). So nobody can tell from a replay whether another player got Pirate Shop Access.
By Epic's published odds, a gold chest is the token about once in 1,100 (1/137,500 against all Major and Supreme odds
together). In the owner's 51 matches, the only gold chests in view were his own two.

**Before a match: nothing.** Nothing the client receives says whether a match holds the token or any prize (see the
pre-roll section), and no chest looks different.

**Not knowable from the PC:** what a roll granted, whether a broken treasure chest still pays out, what "eligible damage"
is, and where a given Gizmo came from. Apart from the screen line above, Fortnite's local log doesn't record grants or
treasure-quest progress. The record is
the game's UI: the post-match "Treasures found" screen, the locker and gizmo wheel, the treasure-hunt quests.

**Statistics from the owner's matches (17 Nov, 59 season-43 matches, 18 with treasure; replay-detected):**
- Treasure goes with chest searches (the owner's plus teammates' in view): matches with treasure averaged 45.5 searches,
  others 29.8 (permutation test, one-sided p = 0.0014). By searches: 0-14 -> 0 of 9 matches, 15-29 -> 2 of 15, 30-44 ->
  7 of 17, 45-59 -> 5 of 10, 60+ -> 4 of 8. Solo: 1 of 11.
- A "same chance on every search" model fits far better than "same chance every match" (likelihood ratio about 160) and
  gives about 1 prize per 89 searches (any prize, Gizmos included; team counts are a minimum and only the first prize
  per match is counted). Dataminers' estimate is about 1 in 100.
- Limits: chest searches and time survived rise together, and among matches of 15+ minutes the treasure and no-treasure
  groups searched about the same (45.2 vs 43.9). Both "every search rolls" and "a prize reserved for the match lands on
  one of its searches" predict more treasure with more searches, so this does NOT settle what pre-roll does. The
  practical result holds either way: more team chest searches, more treasure.
- Treasure Hours (Saturdays 2-4 and 9-11 PM ET): **correction 18 Nov:** Epic extended 14 Nov's afternoon window to
  6 PM ET (Fortnite Status on X, 14 Nov, after a disconnect bug). So 2 matches did start inside one: 5:28 PM (win,
  treasure at 5:41 PM) and 5:46 PM (treasure at 5:54 PM). Both earned treasure, but the type isn't known (the log only
  records that a prize screen appeared), and Epic boosts only the token, Gold Outfit and Vehicle & Decal, so 2 matches
  say nothing about the boost. (The owner keeps a separate, non-public tracker of real vs rumored sessions.)
- **Which modes can drop treasure: the log says so per match.** When a mode is excluded, Fortnite's log writes
  `LogDelilahTreasureHuntRewards: Playlist has tag Plugin.Playlist.Exclude.Delilah.LootTables, skipping prize pool request`
  right after the match loads, and no prize pool is requested. Seen 19 Nov 2026 in Respawn Rumble
  (`playlist_respawn_alt_minimobile`), the day posts claimed it was "farmable again"; Battle Royale matches never log
  it. Respawn Rumble also saves no replay.
  Earlier note (before the extension was found): two matches ran into a window
  (replay clock + chest open times): 14 Nov 1:43 PM match had only its last ~60 s after 2 PM (0 chests; its treasure
  came at 1:45 PM); 14 Nov 8:53 PM match ran to 9:10 PM with 20 team searches after 9 PM (3 Rare, 1 Sprite), no
  treasure. At ~1 in 89 per search, 20 searches give treasure ~20% of the time (~37% if the odds doubled), so one
  match says nothing. Not known whether the boost follows match start or chest-open time.

Research tools: `dropreport scan-rewards`, `scan-paths`, `fields`, `chest-looks`, `gizmos` (CLI); the game-file reader in
the author's workbench (`--find`, `--dump` incl. data/curve tables, `--cat` for config files, `--json` with blueprint
bytecode).