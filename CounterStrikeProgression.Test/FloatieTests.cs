// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Progression;

[TestClass]
public sealed class FloatieTests
{
	private const int Victim = 4;

	[TestMethod]
	public void QuickHitsOnOneTargetAddUp()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		FloatieUpdate? first = merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));
		FloatieUpdate? second = merger.Add(Victim, 1.2, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));
		FloatieUpdate? kill = merger.Add(Victim, 1.4, engine.ApplyCombat(player, TestHelpers.Kill("ak47")));

		Assert.IsTrue(first!.IsNew);
		Assert.AreEqual("+1 XP", first.Text);
		Assert.IsFalse(second!.IsNew);
		Assert.AreEqual("+2 XP", second.Text);
		Assert.AreEqual("+12 XP", kill!.Text);
		Assert.IsFalse(kill.Highlight);
	}

	[TestMethod]
	public void AGapStartsANewFloatie()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));
		FloatieUpdate? later = merger.Add(Victim, 2.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));

		Assert.IsTrue(later!.IsNew);
		Assert.AreEqual("+1 XP", later.Text);
	}

	[TestMethod]
	public void DifferentTargetsGetTheirOwnFloaties()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));
		FloatieUpdate? other = merger.Add(Victim + 1, 1.1, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));

		Assert.IsTrue(other!.IsNew);
		Assert.AreEqual(Victim + 1, other.Key);
	}

	[TestMethod]
	public void SkillShotsAreNamedAndHighlighted()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.RareSkillTiers = [1000, 2000, 3000, 4000, 5000]);
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		FloatieUpdate? update = merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { Attacker = new PlayerState { OnGround = false } }));

		Assert.AreEqual("+26 XP\nJumpshot", update!.Text);
		Assert.IsTrue(update.Highlight);
	}

	[TestMethod]
	public void OnlyTheNewestTwoMomentsAreShownWithoutRepeats()
	{
		ProgressionEngine engine = TestHelpers.Engine(s =>
		{
			s.RareSkillTiers = [1000, 2000, 3000, 4000, 5000];
			s.EpicSkillTiers = [1000, 2000, 3000, 4000, 5000];
			s.CommonSkillTiers = [1000, 2000, 3000, 4000, 5000];
		});
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);
		PlayerState airborne = new() { OnGround = false };

		merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { Attacker = airborne }));
		merger.Add(Victim, 1.1, engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { Attacker = airborne }));
		FloatieUpdate? update = merger.Add(Victim, 1.2, engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { Attacker = airborne, AttackerBlind = true }));

		string[] lines = update!.Text.Split('\n');
		Assert.HasCount(3, lines);
		Assert.AreEqual("Blind kill", lines[1]);
		Assert.AreEqual("Jumpshot", lines[2]);
	}

	[TestMethod]
	public void BotHitsShowFractions()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		FloatieUpdate? update = merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { VictimIsBot = true }));

		Assert.AreEqual("+0.25 XP", update!.Text);
	}

	[TestMethod]
	public void NothingEarnedShowsNothing()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		Assert.IsNull(merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47") with { IsTeammate = true })));
	}

	[TestMethod]
	public void ExpiredFloatiesStartFresh()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		FloatieMerger merger = new(0.75);

		merger.Add(Victim, 1.0, engine.ApplyCombat(player, TestHelpers.Hit("ak47")));
		merger.Expire(5.0);

		Assert.IsTrue(merger.Add(Victim, 1.1, engine.ApplyCombat(player, TestHelpers.Hit("ak47")))!.IsNew);
	}

	[TestMethod]
	[DataRow(0.25, "0.25")]
	[DataRow(2.5, "2.5")]
	[DataRow(3.0, "3")]
	[DataRow(12.75, "13")]
	[DataRow(1250.0, "1,250")]
	public void XpIsFormattedForFloaties(double xp, string expected) => Assert.AreEqual(expected, FloatieMerger.FormatXp(xp));
}
