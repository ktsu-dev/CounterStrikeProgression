// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Tracking;

[TestClass]
public sealed class BountyTests
{
	private const int Leader = 1;
	private const int Hunter = 2;
	private const int Victim = 9;

	private static BountyBoard Board(int startKills = 5, int xpPerKill = 50) =>
		new(new ProgressionSettings { BountyStartKills = startKills, BountyXpPerKill = xpPerKill });

	private static List<BountyChange> Streak(BountyBoard board, int slot, int kills)
	{
		List<BountyChange> changes = [];
		for (int i = 0; i < kills; i++)
		{
			changes.AddRange(board.Kill(slot, killerScores: true, Victim, killerCollects: true));
		}

		return changes;
	}

	[TestMethod]
	public void TheBountyIsPlacedOnceAStreakReachesTheStart()
	{
		BountyBoard board = Board();

		Assert.IsEmpty(Streak(board, Leader, 4));
		Assert.IsNull(board.HolderSlot);

		List<BountyChange> changes = Streak(board, Leader, 1);
		Assert.HasCount(1, changes);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Placed, Leader, 5, 250), changes[0]);
		Assert.AreEqual(Leader, board.HolderSlot);
	}

	[TestMethod]
	public void TheBountyGrowsWithEachKillAndIsAnnouncedEveryStartKills()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 5);

		Assert.IsEmpty(Streak(board, Leader, 4));
		Assert.AreEqual(450, board.Value(board.StreakOf(Leader)));

		List<BountyChange> changes = Streak(board, Leader, 1);
		Assert.HasCount(1, changes);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Raised, Leader, 10, 500), changes[0]);
	}

	[TestMethod]
	public void EndingTheStreakPaysTheKiller()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 7);

		IReadOnlyList<BountyChange> changes = board.Kill(Hunter, killerScores: true, Leader, killerCollects: true);

		Assert.HasCount(1, changes);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Collected, Leader, 7, 350, Hunter), changes[0]);
		Assert.IsNull(board.HolderSlot);
		Assert.AreEqual(0, board.StreakOf(Leader));
		Assert.AreEqual(1, board.StreakOf(Hunter));
	}

	[TestMethod]
	public void AKillerWhoCantCollectLeavesTheBountyUnclaimed()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 5);

		IReadOnlyList<BountyChange> changes = board.Kill(Hunter, killerScores: false, Leader, killerCollects: false);

		Assert.HasCount(1, changes);
		Assert.AreEqual(BountyChangeKind.Lost, changes[0].Kind);
		Assert.AreEqual(0, board.StreakOf(Hunter));
	}

	[TestMethod]
	public void ASuicideOrLeavingLosesTheBounty()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 6);

		IReadOnlyList<BountyChange> changes = board.Drop(Leader);

		Assert.HasCount(1, changes);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Lost, Leader, 6, 300), changes[0]);
		Assert.IsNull(board.HolderSlot);
	}

	[TestMethod]
	public void ALongerStreakTakesTheBounty()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 5);
		Streak(board, Hunter, 5);
		Assert.AreEqual(Leader, board.HolderSlot);

		List<BountyChange> changes = Streak(board, Hunter, 1);

		Assert.HasCount(1, changes);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Placed, Hunter, 6, 300), changes[0]);
		Assert.AreEqual(Hunter, board.HolderSlot);
	}

	[TestMethod]
	public void TheNextLongestStreakTakesTheBountyWhenTheHolderFalls()
	{
		BountyBoard board = Board();
		const int third = 3;
		Streak(board, Leader, 8);
		Streak(board, Hunter, 6);

		IReadOnlyList<BountyChange> changes = board.Kill(third, killerScores: true, Leader, killerCollects: true);

		Assert.HasCount(2, changes);
		Assert.AreEqual(BountyChangeKind.Collected, changes[0].Kind);
		Assert.AreEqual(third, changes[0].CollectorSlot);
		Assert.AreEqual(new BountyChange(BountyChangeKind.Placed, Hunter, 6, 300), changes[1]);
	}

	[TestMethod]
	public void ABotNeverCarriesABounty()
	{
		BountyBoard board = Board();

		for (int i = 0; i < 10; i++)
		{
			Assert.IsEmpty(board.Kill(Leader, killerScores: false, Victim, killerCollects: false));
		}

		Assert.IsNull(board.HolderSlot);
	}

	[TestMethod]
	public void ClearingForgetsEveryStreak()
	{
		BountyBoard board = Board();
		Streak(board, Leader, 5);

		board.Clear();

		Assert.IsNull(board.HolderSlot);
		Assert.AreEqual(0, board.StreakOf(Leader));
	}

	[TestMethod]
	[DataRow(0, 50)]
	[DataRow(5, 0)]
	public void ZeroTurnsBountiesOff(int startKills, int xpPerKill)
	{
		BountyBoard board = Board(startKills, xpPerKill);

		Assert.IsFalse(board.Enabled);
		Assert.IsEmpty(Streak(board, Leader, 20));
		Assert.IsNull(board.HolderSlot);
	}

	[TestMethod]
	public void CollectingABountyPaysRankXpInFull()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.BotXpMultiplier = 0.25);
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyBounty(player, "Leader", 7, 350);

		Assert.AreEqual(350, result.XpGained);
		Assert.AreEqual(350, player.RankXp);
		ProgressionNotice notice = result.Notices.Single(n => n.Kind == NoticeKind.BountyCollected);
		Assert.AreEqual("Leader", notice.Target);
		Assert.AreEqual(7, notice.Value);
		Assert.AreEqual(350, notice.Xp);
		Assert.IsTrue(notice.Broadcast);
	}

	[TestMethod]
	public void ABountyShowsOnTheFloatieAndTheRecap()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		ProgressionResult result = engine.ApplyBounty(TestHelpers.Player(), "Leader", 5, 250);

		Assert.Contains("Bounty", FloatieMerger.Labels(result));
		Assert.AreEqual("Leader's bounty", RecapTracker.Describe(result.Notices.Single(n => n.Kind == NoticeKind.BountyCollected)));
	}
}
