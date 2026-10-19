// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class FeatTests
{
	private static readonly DateTime At = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private static FeatProgress ProgressOf(PlayerProgress player, string id, ProgressionSettings? settings = null) =>
		new FeatCatalog(settings ?? new ProgressionSettings()).Measure(player).Single(m => m.Feat.Id == id).Progress;

	[TestMethod]
	public void ANewPlayer_HasNoFeats()
	{
		Assert.IsFalse(new FeatCatalog(new ProgressionSettings()).Earned(TestHelpers.Player()).Any());
		Assert.HasCount(10, FeatCatalog.All);
		Assert.IsGreaterThan(1, ProgressOf(TestHelpers.Player(), "full_ladder").Target);
	}

	[TestMethod]
	public void PeakMilestones_AreMeasuredFromRankXpAndLevels()
	{
		ProgressionSettings settings = new();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = FeatCatalog.MillionaireXp;
		player.Weapon(TestHelpers.Weapon("ak47").Key).Level = settings.WeaponMaxLevel;

		Assert.IsTrue(ProgressOf(player, "millionaire").IsEarned);
		Assert.IsTrue(ProgressOf(player, "weapon_master").IsEarned);
		FeatProgress elite = ProgressOf(player, "global_elite");
		Assert.IsFalse(elite.IsEarned);
		Assert.AreEqual(RankLadder.TopRank, elite.Target);
	}

	[TestMethod]
	public void LadderFeats_NeedEveryTier()
	{
		ProgressionSettings settings = new();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		SkillShotDefinition epic = SkillShotCatalog.All.First(s => s.Rarity == Rarity.Epic && s.AppliesTo(TestHelpers.Weapon("ak47")));
		ak.Challenge(epic.Id.WeakString).Count = settings.EpicSkillTiers[^1];
		ak.Challenge(ChallengeDefinition.ChickensId).Count = settings.ChickenTiers[^2];

		Assert.IsTrue(ProgressOf(player, "epic_ladder").IsEarned);
		FeatProgress overlord = ProgressOf(player, "chicken_overlord");
		Assert.IsFalse(overlord.IsEarned);
		Assert.AreEqual(settings.ChickenTiers.Count - 1, overlord.Current);
		FeatProgress full = ProgressOf(player, "full_ladder");
		Assert.AreEqual(1, full.Current);
		Assert.AreEqual(new ChallengeCatalog(settings).For(TestHelpers.Weapon("ak47")).Count, full.Target);
	}

	[TestMethod]
	public void Collectors_CountDistinctSkillShotsAcrossWeapons()
	{
		PlayerProgress player = TestHelpers.Player();
		player.Weapon(TestHelpers.Weapon("ak47").Key).Challenge("chicken_nugget").Count = 1;
		player.Weapon(TestHelpers.Weapon("awp").Key).Challenge("chicken_nugget").Count = 3;
		player.Weapon(TestHelpers.Weapon("knife").Key).Challenge("chicken_hand_plucked").Count = 1;

		Assert.AreEqual(2, ProgressOf(player, "collector").Current);
		Assert.AreEqual(SkillShotCatalog.All.Count, ProgressOf(player, "collector").Target);
		Assert.AreEqual(2, ProgressOf(player, "chicken_connoisseur").Current);
		Assert.AreEqual(SkillShotCatalog.For(SkillTrigger.Chicken).Count(), ProgressOf(player, "chicken_connoisseur").Target);
	}

	[TestMethod]
	public void Arsenal_NeedsEveryGunAtLevelTen_AndIgnoresOtherWeapons()
	{
		PlayerProgress player = TestHelpers.Player();
		player.Weapon(TestHelpers.Weapon("knife").Key).Level = FeatCatalog.ArsenalLevel;
		player.Weapon(TestHelpers.Weapon("ak47").Key).Level = FeatCatalog.ArsenalLevel;
		player.Weapon(TestHelpers.Weapon("awp").Key).Level = FeatCatalog.ArsenalLevel - 1;

		FeatProgress arsenal = ProgressOf(player, "arsenal");
		Assert.AreEqual(1, arsenal.Current);
		Assert.AreEqual(WeaponCatalog.All.Count(w => w.IsGun), arsenal.Target);

		foreach (WeaponInfo gun in WeaponCatalog.All.Where(w => w.IsGun))
		{
			player.Weapon(gun.Key).Level = FeatCatalog.ArsenalLevel;
		}

		Assert.IsTrue(ProgressOf(player, "arsenal").IsEarned);
	}

	[TestMethod]
	public void Polymath_CountsWeaponsWithAFinishedSkillShotTier()
	{
		ProgressionSettings settings = new();
		PlayerProgress player = TestHelpers.Player();
		SkillShotDefinition common = SkillShotCatalog.Get(SkillShotId.Create("crouch_kill"));
		WeaponInfo[] weapons = [.. WeaponCatalog.All.Where(common.AppliesTo).Take(FeatCatalog.PolymathWeapons)];
		Assert.HasCount(FeatCatalog.PolymathWeapons, weapons);
		foreach (WeaponInfo weapon in weapons.Skip(1))
		{
			player.Weapon(weapon.Key).Challenge(common.Id.WeakString).Count = settings.CommonSkillTiers[0];
		}

		player.Weapon(weapons[0].Key).Challenge(common.Id.WeakString).Count = settings.CommonSkillTiers[0] - 1;
		player.Weapon(weapons[0].Key).Challenge(ChallengeDefinition.KillsId).Count = settings.KillTiers[^1];
		Assert.AreEqual(FeatCatalog.PolymathWeapons - 1, ProgressOf(player, "polymath").Current);

		player.Weapon(weapons[0].Key).Challenge(common.Id.WeakString).Count = settings.CommonSkillTiers[0];
		Assert.IsTrue(ProgressOf(player, "polymath").IsEarned);
	}

	[TestMethod]
	public void CheckFeats_FirstCallRecordsQuietly_ThenAnnouncesEachNewFeatOnce()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = FeatCatalog.MillionaireXp;
		engine.CheckFeats(player, new ProgressionResult());
		Assert.Contains("millionaire", player.EarnedFeats!);

		player.Weapon(TestHelpers.Weapon("ak47").Key).Level = engine.Settings.WeaponMaxLevel;
		ProgressionResult quiet = new();
		engine.CheckFeats(player, quiet);
		player.Weapon(TestHelpers.Weapon("ak47").Key).Challenge(ChallengeDefinition.KillsId).Count = engine.Settings.KillTiers[0] - 1;
		ProgressionResult loud = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		engine.CheckFeats(player, loud);
		ProgressionResult again = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		engine.CheckFeats(player, again);

		Assert.IsEmpty(quiet.Notices);
		ProgressionNotice feat = loud.Notices.Single(n => n.Kind == NoticeKind.FeatEarned);
		Assert.AreEqual("weapon_master", feat.Feat?.Id);
		Assert.IsFalse(again.Notices.Any(n => n.Kind == NoticeKind.FeatEarned));
		FeedEntry line = FeedBuilder.From(loud, TestHelpers.SteamId, "Tester", At).Single(e => e.Kind == FeedKind.FeatEarned);
		Assert.AreEqual("Weapon master", line.Title);
		Assert.AreEqual("Take a weapon to its top level", FeedCaptions.For(line, engine.Settings).Caption);
	}

	[TestMethod]
	public void FeatIndex_AddsFeedFeatsInCatalogOrder_AndSetReplaces()
	{
		FeatIndex index = new();
		index.Add([
			new FeedEntry(At, 7, "P", FeedKind.FeatEarned, "Collector", null, 0),
			new FeedEntry(At, 7, "P", FeedKind.FeatEarned, "Global Elite", null, 0),
			new FeedEntry(At, 7, "P", FeedKind.SkillShot, "Nugget", null, 10),
			new FeedEntry(At, 7, "P", FeedKind.FeatEarned, "Renamed feat", null, 0),
		]);

		string[] expected = ["global_elite", "collector"];
		CollectionAssert.AreEqual(expected, index.For(7).ToArray());

		index.Set(7, [FeatCatalog.All[1]]);
		Assert.AreEqual("millionaire", Assert.ContainsSingle(index.For(7)));
		Assert.IsEmpty(index.For(8));
	}
}
