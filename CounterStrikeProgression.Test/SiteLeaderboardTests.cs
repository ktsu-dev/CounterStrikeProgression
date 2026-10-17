// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class SiteLeaderboardTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private static readonly string[] ExpectedWeaponOrder = ["Tied", "High", "Low"];

	private static readonly string[] ExpectedRarest = ["no_scope", "wallbang"];

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-boards-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState PlayingState()
	{
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);
		ProgressSnapshot rows = new(
			new PlayerRow(TestHelpers.SteamId, "Fresh", 50, 0, Start, Start),
			[new WeaponRow(TestHelpers.SteamId, "ak47", 300, 2, 4, 1, 10, 20)],
			[new ChallengeRow(TestHelpers.SteamId, "ak47", "wallbang", 1, 0)]);
		live.Apply(new LiveBatch(
			new ServerSnapshot(Start, "de_inferno", "Deathmatch", 16, [new LivePlayer(1, TestHelpers.SteamId, "Fresh", false, 2, 3, 1, 40)]),
			[],
			[rows],
			new ProgressionSettings()));
		return live;
	}

	private static PlayerProgress Player(ulong steamId, string name)
	{
		PlayerProgress player = new(steamId) { Name = name };
		return player;
	}

	private static void Earn(PlayerProgress player, string weapon, string challenge, long count) =>
		player.Weapon(TestHelpers.Weapon(weapon).Key).Challenge(challenge).Count = count;

	private static PlayerTally Tally(PlayerProgress player) => PlayerTally.From(player, new ProgressionSettings());

	[TestMethod]
	public void Tally_SumsSkillShotsAcrossWeapons_AndSkipsVolumeChallenges()
	{
		PlayerProgress player = Player(TestHelpers.SteamId, "Tester");
		Earn(player, "ak47", "wallbang", 3);
		Earn(player, "awp", "wallbang", 2);
		Earn(player, "awp", "no_scope", 0);
		Earn(player, "ak47", ChallengeDefinition.KillsId, 50);

		PlayerTally tally = Tally(player);

		Assert.AreEqual(5, tally.SkillShots["wallbang"]);
		Assert.IsFalse(tally.SkillShots.ContainsKey("no_scope"));
		Assert.IsFalse(tally.SkillShots.ContainsKey(ChallengeDefinition.KillsId));
	}

	[TestMethod]
	public void Tally_LevelComesFromXpWhenTheStoredLevelLags()
	{
		ProgressionSettings settings = new();
		PlayerProgress player = Player(TestHelpers.SteamId, "Tester");
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 1_000_000;
		ak.Level = 1;

		PlayerTally tally = PlayerTally.From(player, settings);

		Assert.AreEqual(settings.WeaponMaxLevel, tally.Weapons["ak47"].Level);
	}

	[TestMethod]
	public void WeaponBoard_RanksByLevelThenXp_AndSkipsPlayersWhoNeverUsedIt()
	{
		PlayerProgress low = Player(TestHelpers.SteamId + 1, "Low");
		low.Weapon(TestHelpers.Weapon("ak47").Key).Xp = 100;
		PlayerProgress high = Player(TestHelpers.SteamId + 2, "High");
		WeaponProgress highAk = high.Weapon(TestHelpers.Weapon("ak47").Key);
		highAk.Xp = 100;
		highAk.Level = 5;
		PlayerProgress tied = Player(TestHelpers.SteamId + 3, "Tied");
		WeaponProgress tiedAk = tied.Weapon(TestHelpers.Weapon("ak47").Key);
		tiedAk.Xp = 200;
		tiedAk.Level = 5;
		PlayerProgress other = Player(TestHelpers.SteamId + 4, "Other");
		other.Weapon(TestHelpers.Weapon("awp").Key).Xp = 10;

		LeaderboardsView view = LeaderboardViewBuilder.Build([Tally(low), Tally(high), Tally(tied), Tally(other)], null);

		WeaponBoard ak47 = view.Weapons.Single(w => w.Key == "ak47");
		Assert.AreEqual(3, ak47.Players);
		string[] order = [.. ak47.Rows.Select(r => r.Name)];
		CollectionAssert.AreEqual(ExpectedWeaponOrder, order);
		Assert.AreEqual(1, ak47.Rows[0].Position);
		Assert.AreEqual(5, ak47.Rows[0].Value);
		Assert.AreEqual(200, ak47.Rows[0].Xp);
		Assert.HasCount(WeaponCatalogCount, view.Weapons);
	}

	[TestMethod]
	public void SkillShotBoards_CountPlayersAndTotals_AndRarestSkipsUnearned()
	{
		PlayerProgress a = Player(TestHelpers.SteamId + 1, "A");
		Earn(a, "ak47", "wallbang", 4);
		Earn(a, "awp", "no_scope", 1);
		PlayerProgress b = Player(TestHelpers.SteamId + 2, "B");
		Earn(b, "m4a1", "wallbang", 6);

		LeaderboardsView view = LeaderboardViewBuilder.Build([Tally(a), Tally(b)], null);

		SkillShotBoard wallbang = view.SkillShots.Single(s => s.Id == "wallbang");
		Assert.AreEqual(2, wallbang.Players);
		Assert.AreEqual(10, wallbang.Total);
		Assert.AreEqual("B", wallbang.Rows[0].Name);
		Assert.AreEqual(Rarity.Rare, wallbang.Rarity);

		string[] rarest = [.. view.Rarest.Select(s => s.Id)];
		CollectionAssert.AreEqual(ExpectedRarest, rarest);
		Assert.IsTrue(view.Rarest.All(s => s.Rows.Count == 0));
		Assert.HasCount(SkillShotCatalog.All.Count, view.SkillShots);
	}

	[TestMethod]
	public void FeatBoard_ListsOnlyPlayersWithFeats()
	{
		PlayerProgress rich = Player(TestHelpers.SteamId + 1, "Rich");
		rich.RankXp = 2_000_000;
		PlayerProgress poor = Player(TestHelpers.SteamId + 2, "Poor");

		LeaderboardsView view = LeaderboardViewBuilder.Build([Tally(rich), Tally(poor)], null);

		Assert.HasCount(1, view.Feats);
		Assert.AreEqual("Rich", view.Feats[0].Name);
		Assert.IsGreaterThan(0, view.Feats[0].Value);
	}

	[TestMethod]
	public void Guide_NamesWeaponsInPlainWords()
	{
		Assert.AreEqual("Any weapon", SkillShotGuideBuilder.Weapons(SkillShot("double_kill")));
		Assert.AreEqual("Any gun", SkillShotGuideBuilder.Weapons(SkillShot("wallbang_hit")));
		Assert.AreEqual("Any gun, Knife", SkillShotGuideBuilder.Weapons(SkillShot("quick_switch")));
		Assert.AreEqual("Pistols", SkillShotGuideBuilder.Weapons(SkillShot("pistol_sniper")));
		Assert.AreEqual("AUG, SG 553, Snipers", SkillShotGuideBuilder.Weapons(SkillShot("no_scope")));
		Assert.AreEqual("Flashbang, Decoy, Smoke Grenade", SkillShotGuideBuilder.Weapons(SkillShot("grenade_impact")));
		Assert.AreEqual("Knife", SkillShotGuideBuilder.Weapons(SkillShot("backstab")));
	}

	[TestMethod]
	public void Guide_ListsEverySkillShotWithSettingsAndCounts()
	{
		ProgressionSettings settings = new() { EpicSkillShotXp = 99 };
		PlayerProgress player = Player(TestHelpers.SteamId, "Tester");
		Earn(player, "ak47", "collateral", 2);

		SkillShotGuideView guide = SkillShotGuideBuilder.Build([PlayerTally.From(player, settings)], settings, null);

		Assert.HasCount(SkillShotCatalog.All.Count, guide.SkillShots);
		SkillShotGuideEntry collateral = guide.SkillShots.Single(s => s.Id == "collateral");
		Assert.AreEqual(99, collateral.Xp);
		Assert.AreEqual(1, collateral.Players);
		Assert.AreEqual(2, collateral.Total);
		Assert.AreEqual("On a kill", collateral.When);
		Assert.AreEqual("Multi-kills and moments", collateral.Group);
		CollectionAssert.AreEqual(settings.EpicSkillTiers.ToArray(), collateral.Tiers.ToArray());
		Assert.AreEqual("Movement", guide.SkillShots[0].Group);
		Assert.AreEqual("Chickens", guide.SkillShots[^1].Group);
		Assert.AreEqual(0, guide.SkillShots.Single(s => s.Id == "wallbang").Players);
	}

	[TestMethod]
	public async Task LeaderboardsEndpoint_TalliesPlayersOnTheServerLive()
	{
		LiveState live = PlayingState();
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await Endpoints.LeaderboardsAsync(new BoardIndex(), reader, live, CancellationToken.None).ConfigureAwait(false);

		Ok<LeaderboardsView> ok = Assert.IsInstanceOfType<Ok<LeaderboardsView>>(result);
		Assert.IsNull(ok.Value!.Updated);
		Assert.AreEqual("Fresh", ok.Value.Weapons.Single(w => w.Key == "ak47").Rows[0].Name);
		Assert.AreEqual(1, ok.Value.SkillShots.Single(s => s.Id == "wallbang").Players);
	}

	[TestMethod]
	public async Task SkillShotsEndpoint_CountsPlayersOnTheServer()
	{
		LiveState live = PlayingState();
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await Endpoints.SkillShotsAsync(new BoardIndex(), reader, live, CancellationToken.None).ConfigureAwait(false);

		Ok<SkillShotGuideView> ok = Assert.IsInstanceOfType<Ok<SkillShotGuideView>>(result);
		Assert.AreEqual(1, ok.Value!.SkillShots.Single(s => s.Id == "wallbang").Players);
	}

	private static int WeaponCatalogCount => Core.Weapons.WeaponCatalog.All.Count;

	private static SkillShotDefinition SkillShot(string id) => SkillShotCatalog.Get(SkillShotId.Create(id));
}
