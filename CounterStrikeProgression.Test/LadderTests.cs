// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

[TestClass]
public sealed class LadderTests
{
	private static readonly RankLadder Ranks = new(1000, 1.25);

	[TestMethod]
	public void RanksStartAtSilverAndEndAtGlobalElite()
	{
		Assert.HasCount(18, RankLadder.Names);
		Assert.AreEqual("Silver I", RankLadder.Names[0]);
		Assert.AreEqual("Global Elite", RankLadder.Names[RankLadder.TopRank]);
	}

	[TestMethod]
	public void EachRankCostsMoreThanTheLast()
	{
		Assert.AreEqual(0, Ranks.ThresholdFor(0));
		Assert.AreEqual(1000, Ranks.ThresholdFor(1));
		Assert.AreEqual(2250, Ranks.ThresholdFor(2));
		Assert.AreEqual(3812, Ranks.ThresholdFor(3));
	}

	[TestMethod]
	public void GlobalEliteTakesAbout174kXp()
	{
		long total = Ranks.ThresholdFor(RankLadder.TopRank);
		Assert.IsGreaterThan(173_000, total);
		Assert.IsLessThan(174_500, total);
	}

	[TestMethod]
	[DataRow(0, 0)]
	[DataRow(999, 0)]
	[DataRow(1000, 1)]
	[DataRow(2249, 1)]
	[DataRow(2250, 2)]
	[DataRow(10_000_000, 17)]
	public void RankForMatchesThresholds(long xp, int expected) => Assert.AreEqual(expected, Ranks.RankFor(xp));

	[TestMethod]
	public void ProgressToNextIsAFractionOfTheCurrentRank()
	{
		Assert.AreEqual(0.5, Ranks.ProgressToNext(500), 1e-9);
		Assert.AreEqual(0.0, Ranks.ProgressToNext(1000), 1e-9);
		Assert.AreEqual(1.0, Ranks.ProgressToNext(10_000_000), 1e-9);
	}

	[TestMethod]
	public void WeaponLevelsCostMoreAsTheyGo()
	{
		WeaponLevelCurve curve = new(300, 100, 40);
		Assert.AreEqual(300, curve.CostOf(1));
		Assert.AreEqual(400, curve.CostOf(2));
		Assert.AreEqual(1, curve.LevelFor(299));
		Assert.AreEqual(2, curve.LevelFor(300));
		Assert.AreEqual(2, curve.LevelFor(699));
		Assert.AreEqual(3, curve.LevelFor(700));
		Assert.AreEqual(700, curve.ThresholdFor(3));
		Assert.AreEqual(40, curve.LevelFor(long.MaxValue / 2));
		Assert.AreEqual(1.0, curve.ProgressToNext(long.MaxValue / 2), 1e-9);
	}

	[TestMethod]
	public void GunsGetVolumeChallengesThenTheirSkillChallenges()
	{
		ChallengeCatalog catalog = new(new ProgressionSettings());
		IReadOnlyList<ChallengeDefinition> ladder = catalog.For(TestHelpers.Weapon("ak47"));

		Assert.AreEqual(ChallengeDefinition.KillsId, ladder[0].Id);
		Assert.AreEqual(ChallengeDefinition.HeadshotsId, ladder[1].Id);
		Assert.AreEqual(ChallengeDefinition.HitsId, ladder[2].Id);
		List<string> ids = [.. ladder.Select(c => c.Id)];
		Assert.Contains("moving_target", ids);
		Assert.Contains("spray_transfer", ids);
		Assert.DoesNotContain("no_scope", ids);
		Assert.DoesNotContain("knife_kill", ids);
	}

	[TestMethod]
	public void ScopedWeaponsGetScopeChallenges()
	{
		ChallengeCatalog catalog = new(new ProgressionSettings());
		List<string> ids = [.. catalog.For(TestHelpers.Weapon("awp")).Select(c => c.Id)];
		Assert.Contains("no_scope", ids);
		Assert.Contains("quickscope", ids);
		Assert.Contains("flying_no_scope", ids);
	}

	[TestMethod]
	public void TheKnifeHasKillsButNoHitsOrHeadshots()
	{
		ChallengeCatalog catalog = new(new ProgressionSettings());
		List<string> ids = [.. catalog.For(TestHelpers.Weapon("knife")).Select(c => c.Id)];
		Assert.Contains(ChallengeDefinition.KillsId, ids);
		Assert.Contains("knife_kill", ids);
		Assert.Contains("backstab", ids);
		Assert.DoesNotContain(ChallengeDefinition.HitsId, ids);
		Assert.DoesNotContain(ChallengeDefinition.HeadshotsId, ids);
	}

	[TestMethod]
	public void SkillTiersPayMoreThanVolumeTiers()
	{
		ProgressionSettings settings = new();
		Assert.IsGreaterThan(settings.VolumeTierXp, settings.SkillTierXp);
		Assert.IsGreaterThan(settings.CommonSkillShotXp, settings.RareSkillShotXp);
		Assert.IsGreaterThan(settings.RareSkillShotXp, settings.EpicSkillShotXp);
	}

	[TestMethod]
	public void TierForCountsFinishedTiers()
	{
		ChallengeDefinition challenge = new("kills", "Kills", ChallengeKind.Volume, Rarity.Common, [25, 100, 250, 500, 1000]);
		Assert.AreEqual(0, challenge.TierFor(24));
		Assert.AreEqual(1, challenge.TierFor(25));
		Assert.AreEqual(5, challenge.TierFor(5000));
		Assert.AreEqual(100, challenge.NextTarget(1));
		Assert.IsNull(challenge.NextTarget(5));
	}
}
