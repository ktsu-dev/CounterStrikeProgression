// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

[TestClass]
public sealed class ProgressionEngineTests
{
	[TestMethod]
	public void AKillPaysKillXpAndCountsTowardKills()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(10, result.XpGained);
		Assert.AreEqual(10, player.RankXp);
		WeaponProgress weapon = player.Weapons[WeaponKey.Create("ak47")];
		Assert.AreEqual(1, weapon.Kills);
		Assert.AreEqual(10, weapon.Xp);
		Assert.AreEqual(1, weapon.Challenges[ChallengeDefinition.KillsId].Count);
	}

	[TestMethod]
	public void AHeadshotKillPaysTheBonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { IsHeadshot = true });

		Assert.AreEqual(15, player.RankXp);
		Assert.AreEqual(1, player.Weapons[WeaponKey.Create("ak47")].Headshots);
	}

	[TestMethod]
	public void AHitPaysHitXp()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Hit("ak47"));

		Assert.AreEqual(1, player.RankXp);
		Assert.AreEqual(1, player.Weapons[WeaponKey.Create("ak47")].Hits);
	}

	[TestMethod]
	public void GrenadeDamageOnlyPaysOnTheKill()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		Assert.AreEqual(0, engine.ApplyCombat(player, TestHelpers.Hit("inferno")).XpGained);
		Assert.AreEqual(0, engine.ApplyCombat(player, TestHelpers.Hit("hegrenade")).XpGained);
		Assert.IsGreaterThan(0, engine.ApplyCombat(player, TestHelpers.Kill("inferno")).XpGained);
	}

	[TestMethod]
	public void TeamDamageNeverPays()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { IsTeammate = true });

		Assert.AreEqual(0, result.XpGained);
		Assert.IsEmpty(player.Weapons);
	}

	[TestMethod]
	public void BotsPayReducedRankXpButFullWeaponProgress()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		for (int i = 0; i < 4; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { VictimIsBot = true });
		}

		WeaponProgress weapon = player.Weapons[WeaponKey.Create("ak47")];
		Assert.AreEqual(10, player.RankXp);
		Assert.AreEqual(40, weapon.Xp);
		Assert.AreEqual(4, weapon.Challenges[ChallengeDefinition.KillsId].Count);
	}

	[TestMethod]
	public void FractionsOfAnXpPointCarryOver()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { VictimIsBot = true });
		engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { VictimIsBot = true });
		engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { VictimIsBot = true });
		Assert.AreEqual(0, player.RankXp);

		engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { VictimIsBot = true });
		Assert.AreEqual(1, player.RankXp);
	}

	[TestMethod]
	public void FinishingATierPaysItsBonusAndAnnouncesIt()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.KillTiers = [2, 4, 6, 8, 10]);
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressionNotice tier = result.Notices.Single(n => n.Kind == NoticeKind.TierCompleted);
		Assert.AreEqual("Kills", tier.ChallengeName);
		Assert.AreEqual(1, tier.Value);
		Assert.AreEqual(100, tier.Xp);
		Assert.IsTrue(tier.Broadcast);
		Assert.AreEqual(10 + 100, result.XpGained);
		Assert.AreEqual(1, player.Weapons[WeaponKey.Create("ak47")].Challenges[ChallengeDefinition.KillsId].TierReached);
	}

	[TestMethod]
	public void SkillTiersPayTheSkillRate()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.RareSkillTiers = [1, 2, 3, 4, 5]);
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { Attacker = new PlayerState { OnGround = false } });

		ProgressionNotice tier = result.Notices.Single(n => n.Kind == NoticeKind.TierCompleted);
		Assert.AreEqual("Jumpshot", tier.ChallengeName);
		Assert.AreEqual(250, tier.Xp);
	}

	[TestMethod]
	public void LevellingAWeaponPaysAndAnnouncesEveryFifthLevel()
	{
		ProgressionEngine engine = TestHelpers.Engine(s =>
		{
			s.WeaponLevelBaseXp = 10;
			s.WeaponLevelStepXp = 0;
			s.KillTiers = [1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000];
		});
		PlayerProgress player = TestHelpers.Player();

		List<ProgressionNotice> levelUps = [];
		for (int i = 0; i < 4; i++)
		{
			levelUps.AddRange(engine.ApplyCombat(player, TestHelpers.Kill("glock")).Notices.Where(n => n.Kind == NoticeKind.WeaponLevelUp));
		}

		Assert.HasCount(4, levelUps);
		Assert.AreEqual(2, levelUps[0].Value);
		Assert.AreEqual(50 + 20, levelUps[0].Xp);
		Assert.IsFalse(levelUps[0].Broadcast);
		Assert.AreEqual(5, levelUps[3].Value);
		Assert.IsTrue(levelUps[3].Broadcast);
	}

	[TestMethod]
	public void RankingUpIsAnnounced()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.FirstRankXp = 10);
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressionNotice rankUp = result.Notices.Single(n => n.Kind == NoticeKind.RankUp);
		Assert.AreEqual(1, rankUp.Value);
		Assert.AreEqual(1, player.Rank);
	}

	[TestMethod]
	public void AChickenKill_PaysALittleXpAndAdvancesTheWeaponsChickens()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		WeaponInfo ak = TestHelpers.Weapon("ak47");

		ProgressionResult result = engine.ApplyChickenKill(player, new CombatEvent { Weapon = ak, IsKill = true });

		WeaponProgress weapon = player.Weapons[ak.Key];
		Assert.AreEqual(1, weapon.Challenges[ChallengeDefinition.ChickensId].Count);
		Assert.AreEqual(engine.Settings.ChickenXp, weapon.Xp);
		Assert.AreEqual(0, weapon.Kills);
		ProgressionNotice tier = result.Notices.Single(n => n.Kind == NoticeKind.TierCompleted);
		Assert.AreEqual("Chickens", tier.ChallengeName);
		Assert.AreEqual(engine.Settings.ChickenXp + engine.Settings.VolumeTierXp, player.RankXp);
	}

	[TestMethod]
	public void ChickenSkillShots_AreEarnedOnlyOnChickens()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		CombatEvent headshot = TestHelpers.Kill("ak47") with { IsHeadshot = true, Penetrations = 1 };

		ProgressionResult onChicken = engine.ApplyChickenKill(player, headshot);
		ProgressionResult onPlayer = engine.ApplyCombat(player, headshot);

		string[] expected = ["chicken_fowl_play", "chicken_nugget"];
		CollectionAssert.AreEquivalent(expected, TestHelpers.SkillShots(onChicken).ToArray());
		Assert.IsFalse(TestHelpers.SkillShots(onPlayer).Any(id => id.StartsWith("chicken_", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void ChickenSkillShots_FollowTheirWeapons()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult knife = engine.ApplyChickenKill(player, new CombatEvent { Weapon = TestHelpers.Weapon("knife"), IsKill = true, IsHeadshot = true });
		ProgressionResult zeus = engine.ApplyChickenKill(player, new CombatEvent { Weapon = TestHelpers.Weapon("taser"), IsKill = true });

		Assert.AreEqual("chicken_hand_plucked", Assert.ContainsSingle(TestHelpers.SkillShots(knife)));
		Assert.AreEqual("chicken_rotisserie", Assert.ContainsSingle(TestHelpers.SkillShots(zeus)));
	}

	[TestMethod]
	public void AStoredRankBehindTheXp_CatchesUpWithoutAnnouncing()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = engine.Ranks.ThresholdFor(5);
		player.Rank = 0;

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(5, player.Rank);
		Assert.IsFalse(result.Notices.Any(n => n.Kind == NoticeKind.RankUp));
	}

	[TestMethod]
	public void RanksNeverGoDown()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.Rank = 5;

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(5, player.Rank);
	}

	[TestMethod]
	public void EfficientReloadNeedsANearlyEmptyMagazine()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		WeaponInfo ak = TestHelpers.Weapon("ak47");

		Assert.Contains("efficient_reload", TestHelpers.SkillShots(engine.ApplyReload(player, new ReloadEvent(ak, 9, 30, LoadsShellByShell: false))));
		Assert.Contains("efficient_reload", TestHelpers.SkillShots(engine.ApplyReload(player, new ReloadEvent(ak, 0, 30, LoadsShellByShell: false))));
		Assert.IsEmpty(TestHelpers.SkillShots(engine.ApplyReload(player, new ReloadEvent(ak, 10, 30, LoadsShellByShell: false))));
		Assert.IsEmpty(TestHelpers.SkillShots(engine.ApplyReload(player, new ReloadEvent(TestHelpers.Weapon("nova"), 0, 8, LoadsShellByShell: true))));
	}

	[TestMethod]
	public void AccurateLivesEarnMarksmanAndSharpshooter()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		WeaponInfo ak = TestHelpers.Weapon("ak47");

		List<string> good = [.. TestHelpers.SkillShots(engine.ApplyLifeEnd(player, new WeaponLifeStats(ak, 10, 6, 0, MostlyBots: false)))];
		Assert.Contains("marksman", good);
		Assert.DoesNotContain("sharpshooter", good);

		List<string> great = [.. TestHelpers.SkillShots(engine.ApplyLifeEnd(player, new WeaponLifeStats(ak, 10, 8, 5, MostlyBots: false)))];
		Assert.Contains("marksman", great);
		Assert.Contains("sharpshooter", great);
		Assert.Contains("headhunter", great);

		Assert.IsEmpty(TestHelpers.SkillShots(engine.ApplyLifeEnd(player, new WeaponLifeStats(ak, 9, 9, 0, MostlyBots: false))));
	}

	[TestMethod]
	public void FlashesEarnFlashSkillShots()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		WeaponInfo flash = TestHelpers.Weapon("flashbang");

		ProgressionResult assist = engine.ApplyAssist(player, new AssistEvent(flash, ByFlash: true, VictimIsBot: false));
		Assert.Contains("flash_assist", TestHelpers.SkillShots(assist));
		Assert.AreEqual(3 + 10, assist.XpGained);

		Assert.Contains("long_flash", TestHelpers.SkillShots(engine.ApplyFlash(player, new FlashEvent(flash, 3.5, VictimIsBot: false))));
		Assert.IsEmpty(TestHelpers.SkillShots(engine.ApplyFlash(player, new FlashEvent(flash, 2.0, VictimIsBot: false))));
	}

	[TestMethod]
	public void APlainAssistPaysAssistXp()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyAssist(player, new AssistEvent(TestHelpers.Weapon("ak47"), ByFlash: false, VictimIsBot: false));

		Assert.AreEqual(3, result.XpGained);
		Assert.IsEmpty(TestHelpers.SkillShots(result));
	}

	[TestMethod]
	public void AClutchPaysEvenWithoutAWeapon()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult withWeapon = engine.ApplyClutch(player, new ClutchEvent(2, TestHelpers.Weapon("ak47"), VsBots: false));
		Assert.Contains("clutch", TestHelpers.SkillShots(withWeapon));
		Assert.AreEqual(1, player.Weapons[WeaponKey.Create("ak47")].Challenges["clutch"].Count);

		ProgressionResult withoutWeapon = engine.ApplyClutch(player, new ClutchEvent(1, null, VsBots: false));
		Assert.AreEqual(60, withoutWeapon.XpGained);

		Assert.AreEqual(0, engine.ApplyClutch(player, new ClutchEvent(0, null, VsBots: false)).XpGained);
	}

	[TestMethod]
	public void AClutchAgainstOnlyBotsPaysTheBotRate()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		long botRate = (long)Math.Round(engine.SkillShotXp(Rarity.Epic) * engine.Settings.BotXpMultiplier);

		// The weapon XP and its level-up bonuses are the same either way; only the clutch's own XP is scaled.
		PlayerProgress human = TestHelpers.Player();
		ProgressionResult vsHumans = engine.ApplyClutch(human, new ClutchEvent(2, TestHelpers.Weapon("ak47"), VsBots: false));
		ProgressionResult withWeapon = engine.ApplyClutch(player, new ClutchEvent(2, TestHelpers.Weapon("ak47"), VsBots: true));
		Assert.AreEqual(engine.SkillShotXp(Rarity.Epic) - botRate, vsHumans.XpGained - withWeapon.XpGained);
		Assert.AreEqual(botRate, withWeapon.Notices.Single(n => n.Kind == NoticeKind.SkillShot).Xp);
		Assert.AreEqual(1, player.Weapons[WeaponKey.Create("ak47")].Challenges["clutch"].Count);
		Assert.AreEqual(human.Weapons[WeaponKey.Create("ak47")].Xp, player.Weapons[WeaponKey.Create("ak47")].Xp);

		ProgressionResult withoutWeapon = engine.ApplyClutch(player, new ClutchEvent(2, null, VsBots: true));
		Assert.AreEqual(botRate, withoutWeapon.XpGained);
		ProgressionNotice notice = withoutWeapon.Notices.Single(n => n.Kind == NoticeKind.SkillShot);
		Assert.AreEqual(botRate, notice.Xp);
		Assert.IsTrue(notice.VsBot);

		Assert.AreEqual(engine.SkillShotXp(Rarity.Epic), engine.ApplyClutch(player, new ClutchEvent(2, null, VsBots: false)).XpGained);
	}

	[TestMethod]
	public void ChangesAreFlaggedForSaving()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.IsTrue(player.IsDirty);
		WeaponProgress weapon = player.Weapons[WeaponKey.Create("ak47")];
		Assert.IsTrue(weapon.IsDirty);
		Assert.IsTrue(weapon.Challenges[ChallengeDefinition.KillsId].IsDirty);
	}
}
