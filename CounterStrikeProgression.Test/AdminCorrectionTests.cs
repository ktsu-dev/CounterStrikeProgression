// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

[TestClass]
public sealed class AdminCorrectionTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void GrantAddsXpAndRaisesTheRank()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		long target = engine.Ranks.ThresholdFor(3);

		CorrectionOutcome outcome = new AdminCorrection(CorrectionKind.Grant, target).Apply(player, engine.Ranks);

		Assert.AreEqual(0, outcome.XpBefore);
		Assert.AreEqual(target, outcome.XpAfter);
		Assert.AreEqual(3, outcome.RankAfter);
		Assert.AreEqual(target, player.RankXp);
		Assert.AreEqual(3, player.Rank);
		Assert.IsTrue(player.IsDirty);
	}

	[TestMethod]
	public void NegativeGrantTakesXpAndTheRanksItBoughtAway()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = engine.Ranks.ThresholdFor(5);
		player.Rank = 5;
		long keep = engine.Ranks.ThresholdFor(2);

		CorrectionOutcome outcome = new AdminCorrection(CorrectionKind.Grant, keep - player.RankXp).Apply(player, engine.Ranks);

		Assert.AreEqual(5, outcome.RankBefore);
		Assert.AreEqual(2, outcome.RankAfter);
		Assert.AreEqual(keep, player.RankXp);
		Assert.AreEqual(2, player.Rank);
	}

	[TestMethod]
	public void NegativeGrantStopsAtZero()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 50;

		CorrectionOutcome outcome = new AdminCorrection(CorrectionKind.Grant, -1_000).Apply(player, engine.Ranks);

		Assert.AreEqual(0, outcome.XpAfter);
		Assert.AreEqual(0, player.Rank);
	}

	[TestMethod]
	public void PositiveGrantKeepsAStoredRankAboveTheXp()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		player.Rank = 4;

		new AdminCorrection(CorrectionKind.Grant, 10).Apply(player, engine.Ranks);

		Assert.AreEqual(4, player.Rank);
	}

	[TestMethod]
	public void ResetClearsEverythingAndRecountsFeatsQuietly()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		player.EarnedFeats = ["something"];

		CorrectionOutcome outcome = new AdminCorrection(CorrectionKind.Reset).Apply(player, engine.Ranks);

		Assert.AreEqual(2, outcome.WeaponsCleared);
		Assert.IsGreaterThan(0, outcome.XpBefore);
		Assert.AreEqual(0, player.RankXp);
		Assert.AreEqual(0, player.Rank);
		Assert.IsEmpty(player.Weapons);
		Assert.IsNull(player.EarnedFeats);
		Assert.IsTrue(player.IsDirty);
	}

	[TestMethod]
	public async Task StoreResetDeletesOnlyThatPlayersRows()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		PlayerProgress other = new(TestHelpers.SteamId + 1) { Name = "Other" };
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		engine.ApplyCombat(other, TestHelpers.Kill("ak47"));
		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(ProgressSnapshot.Capture(other), TestContext.CancellationToken).ConfigureAwait(false);

		new AdminCorrection(CorrectionKind.Reset).Apply(player, engine.Ranks);
		await store.ResetAsync(ProgressSnapshot.ToRow(player), TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress? reloaded = await store.LoadAsync(player.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		PlayerProgress? untouched = await store.LoadAsync(other.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsNotNull(reloaded);
		Assert.AreEqual(0, reloaded.RankXp);
		Assert.IsEmpty(reloaded.Weapons);
		Assert.IsNotNull(untouched);
		Assert.HasCount(1, untouched.Weapons);
	}
}
