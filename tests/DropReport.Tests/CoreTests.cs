using DropReport;
using DropReport.Analysis;
using DropReport.Replay;
using Unreal.Core.Models;

namespace DropReport.Tests;

public class ChestTrackerTests
{
    [Fact]
    public void Chest_already_searched_when_first_seen_is_carried_not_counted()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "TrackedChest", pawnNow: 7, animCount: 1, destroyed: false, alreadySearched: true, t: 10);
        Assert.Single(t.Events);
        Assert.Equal("carried", t.Events[0].Kind);
    }

    [Fact]
    public void New_searcher_after_first_sight_is_opened()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "TrackedChest", 0, 0, false, false, 10);          // seen, untouched
        t.Update(1, 100, "TrackedChest", 7, 1, false, true, 12);           // searched by pawn 7
        Assert.Equal("opened", Assert.Single(t.Events).Kind);
        Assert.Equal(7u, t.Events[0].Pawn);
    }

    [Fact]
    public void Searcher_and_destroyed_without_search_is_broken()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "TrackedChest", 0, 0, false, false, 10);
        t.Update(1, 100, "TrackedChest", 7, 0, destroyed: true, alreadySearched: false, t: 11);
        Assert.Equal("broken", Assert.Single(t.Events).Kind);
    }

    [Fact]
    public void Destroyed_within_a_second_of_opened_becomes_broken()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "TrackedChest", 0, 0, false, false, 10);
        t.Update(1, 100, "TrackedChest", 7, 0, false, false, 11);
        t.Update(1, 100, "TrackedChest", 7, 0, true, false, 11.5f);
        Assert.Equal("broken", Assert.Single(t.Events).Kind);
    }

    [Fact]
    public void Respawned_chest_counts_each_search()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "RelicChest", 0, 0, false, false, 10);
        t.Update(1, 100, "RelicChest", 7, 1, false, true, 11);
        t.Update(1, 100, "RelicChest", 7, 2, false, true, 200);            // same pawn, animation counter up again
        Assert.Equal(2, t.Events.Count(e => e.Kind == "opened"));
    }

    [Fact]
    public void Channel_reuse_resets_state()
    {
        var t = new ChestTracker();
        t.Update(1, 100, "TrackedChest", 0, 0, false, false, 10);
        t.Reset(1);
        t.Update(1, 200, "TrackedChest", 9, 1, false, true, 20);           // new actor on the channel: first sight
        Assert.Equal("carried", Assert.Single(t.Events).Kind);
    }
}

public class RulesTests
{
    [Theory]
    [InlineData("VeryRare", "Epic")]
    [InlineData("SuperRare", "Legendary")]
    [InlineData("Transcendent", "Exotic")]
    [InlineData("UltraRare", "Mythic")]
    [InlineData("Uncommon", "Uncommon")]
    public void Rarity_tags_map_to_in_game_words(string tag, string word) => Assert.Equal(word, Rules.RarityFromTag(tag));

    [Theory]
    [InlineData("WID_Assault_Heavy_Recoil_Athena_VR", "Epic")]
    [InlineData("WID_Pistol_Scavenger_Athena_SR_Ore_T03", "Legendary")]
    [InlineData("WID_Athena_GrandmaVibes", null)]
    public void Suffix_rarity_fallback(string id, string rarity) => Assert.Equal(rarity, Rules.SuffixRarity(id));

    [Theory]
    [InlineData("TrackedChest", "Chest")]
    [InlineData("AlwaysSpawnNormalChest", "Chest")]
    [InlineData("AlwaysSpawnRareChest", "Rare Chest")]
    [InlineData("RelicChest", "Sprite Chest")]
    public void Chest_labels(string cls, string label) => Assert.Equal(label, Rules.ChestLabel(cls));

    [Theory]
    [InlineData("WID_TreasureKey", "treasure key")]
    [InlineData("AGID_Athena_Booty", "treasure map")]
    [InlineData("WID_RelicFinder_Consumable", "sprite locator")]
    [InlineData("WID_Delilah_Keycard_Gameplay_Key", "vault key")]
    [InlineData("WID_Paprika_ShieldPot", null)]
    public void Season_items(string id, string kind) => Assert.Equal(kind, Rules.SeasonItem(id));

    [Theory]
    [InlineData(false, null, "player")]
    [InlineData(true, "5A2B...", "bot")]
    [InlineData(true, null, "npc")]
    public void Bot_player_npc(bool isBot, string botId, string kind) => Assert.Equal(kind, Rules.Kind(isBot, botId));

    [Fact]
    public void Anonymous_players_stay_anonymous()
    {
        Assert.Equal("Anonymous player", Rules.DisplayName("RealName", false, true, null, null, null));
        Assert.Equal("Streamer123", Rules.DisplayName("RealName", false, false, "Streamer123", null, null));
        Assert.Equal("RealName", Rules.DisplayName("RealName", false, false, null, null, null));
        Assert.Equal("Buggy", Rules.DisplayName(null, true, null, null, "Buggy", null));
    }

    [Fact]
    public void Mode_season_and_start_time()
    {
        Assert.Equal("Squad", Rules.ModeOf("Playlist_NoBuildBR_Squad"));
        Assert.Equal(43, Rules.SeasonOf("++Fortnite+Release-43.10"));
        Assert.Equal(new DateTime(2026, 11, 15, 14, 39, 46), Rules.StartFromFileName("UnsavedReplay-2026.11.15-14.39.46.replay"));
        Assert.Equal("team wiped", Rules.Cause(50));
        Assert.Equal("Hammer Assault Rifle", Rules.StripRarity("Hammer Assault Rifle [Epic]"));
    }
}

public class HeadlineTests
{
    private static MatchSummary M(int? place, int elims, int chests, string haunt = null, DeathInfo death = null) => new(
        1, "f", null, "b", 43, "p", "Duo", null, place, 18.6, 60, null,
        new EpicStats(elims, 0, 0.3, 1000, 100, 0, 0),
        new ChestStats(chests, 0, 0, 0, new(), new(), new(), 0), new(), new DamageStats(0, 0, 0, 0, 0, new()),
        new FightStats(2, elims, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, elims, new(), new(), new()), death, haunt);

    [Fact]
    public void Headlines_pick_the_story()
    {
        Assert.Equal("Victory Royale at Loguetown", Headlines.For(M(1, 3, 10, "Loguetown")).Headline);
        Assert.Equal("Twelve down at Plunder Port", Headlines.For(M(3, 12, 10, "Plunder Port")).Headline);
        Assert.Equal("31 chests opened", Headlines.For(M(9, 2, 31)).Headline);
        Assert.Equal("2nd place", Headlines.For(M(2, 1, 5)).Headline);
        Assert.Equal("14th place after 19 minutes", Headlines.For(M(14, 1, 5)).Headline);
        Assert.Equal("Picked off from 162 m", Headlines.For(M(20, 1, 5, death: new DeathInfo(900, "player", "x", "pistol", 162, true, false))).Headline);
    }

    [Fact]
    public void Dek_reads_like_a_sentence()
    {
        var d = Headlines.For(M(3, 7, 17, death: new DeathInfo(900, "player", "x", "shotgun", 5, true, false))).Dek;
        Assert.Equal("Seven eliminations, two knocks, 17 chests for the team. Went down to a shotgun from 5 m.", d);
    }
}

public class MapGeoTests
{
    [Fact]
    public void World_to_image_matches_the_game_camera()
    {
        var g = new MapGeo("b", 43, "Delilah_Terrain", 8500, 508, 299926, new());
        Assert.Equal((0.5, 0.5), g.ToImage(8500, 508));
        var (u, v) = g.ToImage(8500 + 299926 / 2.0, 508 - 299926 / 2.0);   // +X = right edge, -Y = top edge
        Assert.Equal(1.0, u, 6);
        Assert.Equal(0.0, v, 6);
    }
}

public class ItemTableTests
{
    private static ItemTable Table()
    {
        var t = new ItemTable();
        t.Add(new ItemTable.Item("WID_Assault_Heavy_Recoil_Athena_VR", "Hammer Assault Rifle", "Epic", "VeryRare", "FortWeaponRangedItemDefinition",
                                 "/DaisyWeaponGameplay/Items/Weapons/Assault/B_Assault_Heavy_Recoil_Athena.B_Assault_Heavy_Recoil_Athena_C", new() { "Weapon.Ranged.Assault" }));
        t.Add(new ItemTable.Item("WID_Assault_Heavy_Recoil_Athena_R", "Hammer Assault Rifle", "Rare", "Rare", "FortWeaponRangedItemDefinition",
                                 "/DaisyWeaponGameplay/Items/Weapons/Assault/B_Assault_Heavy_Recoil_Athena.B_Assault_Heavy_Recoil_Athena_C", new() { "Weapon.Ranged.Assault" }));
        return t;
    }

    [Fact]
    public void Known_id_gets_real_name_unknown_keeps_code_name()
    {
        var t = Table();
        Assert.Equal("Hammer Assault Rifle", t.Lookup("WID_Assault_Heavy_Recoil_Athena_VR", "me").Name);
        Assert.Equal("Assault Rifle", t.Lookup("WID_Assault_Heavy_Recoil_Athena_VR", "me").Kind);
        var u = t.Lookup("WID_DrivingNebula", "me");
        Assert.False(u.Decoded);
        Assert.Contains("code name", u.Name);
    }

    [Fact]
    public void Weapon_class_resolves_to_the_exact_item_I_picked_up()
    {
        var picks = new List<PickupState> { new() { Item = "WID_Assault_Heavy_Recoil_Athena_R", PickedAt = 99.5f, PickupTarget = 1 } };
        var w = WeaponNames.Resolve("B_Assault_Heavy_Recoil_Athena_C", 100f, picks, Table());
        Assert.Equal(("Hammer Assault Rifle", "Rare"), (w.Name, w.Rarity));
    }

    [Fact]
    public void Weapon_without_pickup_shows_name_and_no_guessed_rarity()
    {
        var w = WeaponNames.Resolve("B_Assault_Heavy_Recoil_Athena_C", 100f, new List<PickupState>(), Table());
        Assert.Equal("Hammer Assault Rifle", w.Name);
        Assert.Null(w.Rarity);
    }
}

public class LootMatcherTests
{
    private static PickupState P(float t, float x, float y, string item = "X") =>
        new() { Tossed = true, FirstSeen = t, Item = item, FirstPos = new FVector(x, y, 0) };

    [Fact]
    public void Takes_the_packet_near_me_not_the_teammate_chest_10m_away()
    {
        var pk = new List<PickupState> { P(10.00f, 100, 0, "mine1"), P(10.01f, 120, 0, "mine2"), P(10.10f, 1100, 0, "theirs") };
        var m = LootMatcher.For(10f, 0, 0, pk, new());
        Assert.Equal(new[] { "mine1", "mine2" }, m.Items.Select(i => i.Item));
    }

    [Fact]
    public void Fresh_toss_position_at_one_tenth_scale_still_matches()
    {
        var pk = new List<PickupState> { P(10.0f, 30, 20) };                 // 300,200 uu at 1/10 scale
        var m = LootMatcher.For(10f, 310, 190, pk, new());
        Assert.Single(m.Items);
    }

    [Fact]
    public void Cluster_without_positions_only_counts_right_at_the_search()
    {
        var a = new PickupState { Tossed = true, FirstSeen = 10.1f, Item = "a" };
        var b = new PickupState { Tossed = true, FirstSeen = 10.5f, Item = "b" };
        Assert.Equal("a", Assert.Single(LootMatcher.For(10f, 0, 0, new List<PickupState> { a }, new()).Items).Item);
        Assert.Empty(LootMatcher.For(10f, 0, 0, new List<PickupState> { b }, new()).Items);
    }
}

public class FightAnalyzerTests
{
    private static List<FightEvent> Build(List<Elim> e, Func<Elim, FeedInfo> feed = null, Func<Elim, WeaponRef> hit = null, Func<Elim, WeaponRef> held = null) =>
        FightAnalyzer.Build(e, id => id switch { "ME" => "me", "MATE" => "teammate", var s when s.StartsWith("BOT") => "bot", _ => "player" },
                            id => id, feed ?? (_ => null), hit ?? (_ => null), held ?? (_ => null));

    [Fact]
    public void Knock_then_my_finish()
    {
        var f = Build(new() { new(10, "ME", "P1", true, 3), new(14, "ME", "P1", false, 3) });
        Assert.Equal("finished by me", f[0].Outcome);
        Assert.Equal("me", f[1].KnockedBy);
        var s = FightAnalyzer.Summarize(f, 1);
        Assert.Equal((1, 1, 1), (s.Knocks, s.Elims, s.MyKnocksIFinished));
    }

    [Fact]
    public void Elim_credit_follows_the_knock()
    {
        var f = Build(new()
        {
            new(10, "P9", "P1", true, 4), new(12, "ME", "P1", false, 4),     // I finished someone else's knock: theirs
            new(20, "ME", "P2", true, 3), new(30, "ME", "P2", false, 50),    // my knock, gone when their team was wiped: mine
            new(40, "ME", "BOT1", false, 2),                                  // no knock: the finisher's, mine
            new(50, "ME", "P3", true, 3), new(55, "MATE", "P3", false, 3),   // my knock, my teammate finished: still mine
        });
        var s = FightAnalyzer.Summarize(f, 3);
        Assert.Equal(3, s.Elims);
        Assert.Equal(new[] { false, true, true, true }, s.Mine.Where(x => x.T is 12 or 30 or 40 or 50).OrderBy(x => x.T).Select(x => x.Credit));
        Assert.Equal(1, s.FinishedOtherTeamsKnocks);
        Assert.Equal(1, s.TeamWipeCredits);
    }

    [Fact]
    public void Weapon_credit_order_last_hit_then_knock_weapon_for_bleed_out_then_held()
    {
        var gun = new WeaponRef("Gilded Dragoon Shotgun", "Epic");
        var melee = new WeaponRef("Leekcapper", "Epic");
        var f = Build(new() { new(10, "ME", "P1", true, 3), new(20, "ME", "P1", false, 3), new(30, "ME", "P2", false, 8) },
                      feed: e => e.T == 20 ? new FeedInfo(new(), null, BledOut: true) : null,
                      hit: e => e.T == 10 ? gun : null,
                      held: _ => melee);
        Assert.Equal(("Gilded Dragoon Shotgun", "last hit"), (f[0].Weapon, f[0].WeaponFrom));
        Assert.Equal(("Gilded Dragoon Shotgun", "knock"), (f[1].Weapon, f[1].WeaponFrom));
        Assert.Equal(("Leekcapper", "held"), (f[2].Weapon, f[2].WeaponFrom));
    }

    [Fact]
    public void My_death_is_listed_separately()
    {
        var f = Build(new() { new(50, "P5", "ME", true, 2), new(55, "P5", "ME", false, 2) });
        var s = FightAnalyzer.Summarize(f, 0);
        Assert.Equal(2, s.Deaths.Count);
        Assert.Equal(0, s.Knocks + s.Elims);
    }
}

public class TreasureTests
{
    private static MatchSummary M(DateTime? start, List<ChestTypeCount> mine, List<ChestTypeCount> team, int? place = 5, string landing = null,
                                  DeathInfo death = null, double busJump = 60) => new(
        2, "f", start, "b", 43, "p", "Duo", null, place, 18.6, busJump, null,
        new EpicStats(3, 0, 0.3, 1000, 100, 0, 0),
        new ChestStats(mine.Sum(c => c.Opened), mine.Sum(c => c.Broken), 0, 0, new(), mine, team, 1), new(), new DamageStats(0, 0, 0, 0, 0, new()),
        new FightStats(2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, new(), new(), new()), death, null, landing);

    [Fact]
    public void Counting_rules_change_what_rolls()
    {
        var m = M(null, new() { new("Chest", 10, 2, OpenedRefill: 1, BrokenRefill: 1), new("Extraction crate", 4, 0) }, new() { new("Rare Chest", 5, 0, 1, 0) });
        Assert.Equal(10 + 2 + 5, Treasure.Eligible(m, CountRule.Default));                           // refills, broken and team all count; crates never
        Assert.Equal(10 - 1 + 2 - 1 + 5 - 1, Treasure.Eligible(m, new CountRule(Refills: false, Broken: true, Team: true)));
        Assert.Equal(12 + 5, Treasure.Eligible(m, new CountRule(Refills: true, Broken: true, Team: true)));
        Assert.Equal(10 - 1, Treasure.Eligible(m, new CountRule(Refills: false, Broken: false, Team: false)));
    }

    [Fact]
    public void Odds_match_the_published_math()
    {
        var p = 1 / 137_500.0;
        Assert.Equal(0.0700, Treasure.AtLeastOne(10_000, p), 3);          // 10,000 chests is about 7%
        Assert.Equal(0.632, Treasure.AtLeastOne(137_500, p), 3);          // one "expected" drop is 63%, not 100%
        Assert.InRange(Treasure.RollsFor(0.5, p), 95_250, 95_350);   // 50% at about 95,300
    }

    [Fact]
    public void Treasure_hours_boost_the_token_only()
    {
        var s = Treasure.SessionAt(new DateTime(2026, 11, 8, 2, 30, 0, DateTimeKind.Utc));   // Sat 7 Nov, 9:30 PM ET
        Assert.Equal(2.0, s.Multiplier);
        Assert.True(s.Confirmed);
        Assert.Null(Treasure.SessionAt(new DateTime(2026, 11, 8, 5, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(1.5, Treasure.SessionAt(new DateTime(2026, 11, 14, 19, 30, 0, DateTimeKind.Utc)).Multiplier);
        Assert.False(Treasure.Rewards.Single(r => r.Name == "Any Gizmo").Boosted);
    }

    [Fact]
    public void Report_projects_at_your_pace()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var start = now.AddDays(-4).ToLocalTime();
        var ms = new List<MatchSummary> { M(start, new() { new("Chest", 40, 0) }, new()), M(start.AddDays(2), new() { new("Chest", 40, 0) }, new()) };
        var r = Treasure.Build(ms, CountRule.Default, now);
        Assert.Equal(80, r.Chests);
        Assert.Equal(20, r.ChestsPerDay, 1);                                // 80 chests over 4 days
        Assert.True(r.ProjectedChance > r.TokenChance);
        Assert.Equal(r.Chests + r.ChestsPerDay * r.DaysLeft, r.ProjectedChests, 6);
    }

    [Fact]
    public void Spots_need_enough_drops_to_rank()
    {
        var ms = new List<MatchSummary>
        {
            M(null, new(), new(), place: 1, landing: "A"),
            M(null, new(), new(), place: 30, landing: "B"), M(null, new(), new(), place: 20, landing: "B"), M(null, new(), new(), place: 10, landing: "B"),
        };
        var picks = Insights.BestSpots(Insights.Spots(ms));
        Assert.Equal("B", picks.Single(p => p.Title == "Best for placement").Name);   // A won once but has only 1 drop
        Assert.True(Insights.EarlyDeath(M(null, new(), new(), death: new DeathInfo(200, "player", "x", "shotgun", 5, false, false))));
        Assert.False(Insights.EarlyDeath(M(null, new(), new(), death: new DeathInfo(400, "player", "x", "shotgun", 5, false, false))));
    }
}

public class OddsFileTests
{
    [Fact]
    public void Odds_file_must_be_sane_to_replace_the_built_in_odds()
    {
        var built = Treasure.BuiltIn();
        Assert.False(Treasure.Apply(built with { Season = 42 }));                                      // wrong season
        Assert.False(Treasure.Apply(built with { Rewards = new() { new("Gold outfit", 10, true, false) } }));   // token must lead
        Assert.False(Treasure.Apply(built with { Rewards = new() { new(Treasure.Token, 0.5, false, true) } })); // odds > 1
        Assert.True(Treasure.Apply(built));
        Assert.Equal(137_500, Treasure.Rewards[0].OneIn);
    }
}
public class TreasureWeekTests
{
    [Fact]
    public void Weeks_reset_sunday_14_utc()
    {
        // Mon 16 Nov 2026 -> week 3 started Sun 15 Nov 14:00 UTC
        Assert.Equal(new DateTime(2026, 11, 15, 14, 0, 0, DateTimeKind.Utc), Treasure.WeekStartUtc(new DateTime(2026, 11, 16, 19, 0, 0, DateTimeKind.Utc)));
        // Sun 15 Nov 13:59 UTC is still week 2
        Assert.Equal(new DateTime(2026, 11, 8, 14, 0, 0, DateTimeKind.Utc), Treasure.WeekStartUtc(new DateTime(2026, 11, 15, 13, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 12, 5, 14, 0, 0, DateTimeKind.Utc), Treasure.SeasonEndUtc);
        Assert.Null(Treasure.NextSession(new DateTime(2026, 12, 5, 13, 0, 0, DateTimeKind.Utc)));   // the 5 Dec sessions fall after the end
    }
}