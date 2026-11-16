using BootyBoard;
using BootyBoard.Analysis;
using BootyBoard.Replay;
using Unreal.Core.Models;

namespace BootyBoard.Tests;

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
    public void Epic_count_excludes_other_teams_knocks_and_team_wipes()
    {
        var f = Build(new()
        {
            new(10, "P9", "P1", true, 4), new(12, "ME", "P1", false, 4),     // finished someone else's knock
            new(20, "ME", "P2", true, 3), new(30, "ME", "P2", false, 50),    // team-wipe credit
            new(40, "ME", "BOT1", false, 2),
        });
        var s = FightAnalyzer.Summarize(f, 1);
        Assert.Equal(3, s.Elims);
        Assert.Equal(1, s.FinishedOtherTeamsKnocks);
        Assert.Equal(1, s.TeamWipeCredits);
        Assert.Equal(s.EpicElims, s.Elims - s.FinishedOtherTeamsKnocks - s.TeamWipeCredits);
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
