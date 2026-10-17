// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Admin;
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

	[TestMethod]
	public void GrantParsesSignedWholeNumbersOtherThanZero()
	{
		Assert.IsTrue(AdminCorrection.TryParseGrant("500", out AdminCorrection? add));
		Assert.AreEqual(new AdminCorrection(CorrectionKind.Grant, 500), add);
		Assert.IsTrue(AdminCorrection.TryParseGrant("-250", out AdminCorrection? take));
		Assert.AreEqual(-250, take?.Xp);
		Assert.IsFalse(AdminCorrection.TryParseGrant("0", out _));
		Assert.IsFalse(AdminCorrection.TryParseGrant("1.5", out _));
		Assert.IsFalse(AdminCorrection.TryParseGrant("lots", out _));
		Assert.IsFalse(AdminCorrection.TryParseGrant(null, out _));
	}

	[TestMethod]
	public void MessagesNameTheCorrectionAndTheRanks()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		AdminCorrection grant = new(CorrectionKind.Grant, engine.Ranks.ThresholdFor(1));
		CorrectionOutcome outcome = grant.Apply(player, engine.Ranks);

		Assert.AreEqual($"grant +{engine.Ranks.ThresholdFor(1)} XP", grant.Description);
		Assert.AreEqual("grant -5 XP", new AdminCorrection(CorrectionKind.Grant, -5).Description);
		Assert.AreEqual("reset", new AdminCorrection(CorrectionKind.Reset).Description);
		StringAssert.Contains(grant.DoneMessage("Tester", TestHelpers.SteamId, outcome), $"{RankLadder.Names[0]} -> {RankLadder.Names[1]}");
		StringAssert.Contains(grant.TargetMessage(outcome), RankLadder.Names[1]);
		StringAssert.Contains(grant.FailedMessage("Tester", TestHelpers.SteamId), "Nothing was changed");
		Assert.AreEqual("An admin reset your progress.", new AdminCorrection(CorrectionKind.Reset).TargetMessage(outcome));
	}

	[TestMethod]
	public async Task StoredGrantLoadsAppliesAndWritesOnlyThePlayerRow()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		AdminCorrection grant = new(CorrectionKind.Grant, 1_000);

		(CorrectionOutcome Outcome, ProgressSnapshot Rows)? applied = await grant.ApplyToStoredAsync(store, player.SteamId, engine.Ranks, TestContext.CancellationToken).ConfigureAwait(false);
		if (applied is not (_, ProgressSnapshot rows))
		{
			Assert.Fail("The stored player wasn't found.");
			return;
		}

		Assert.IsNotNull(rows.Player);
		Assert.IsEmpty(rows.Weapons);
		await grant.WriteAsync(store, rows, TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress? reloaded = await store.LoadAsync(player.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(player.RankXp + 1_000, reloaded?.RankXp);
		Assert.HasCount(1, reloaded!.Weapons);
		Assert.IsNull(await grant.ApplyToStoredAsync(store, player.SteamId + 1, engine.Ranks, TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task StoredResetDeletesTheWeaponRows()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		AdminCorrection reset = new(CorrectionKind.Reset);

		(CorrectionOutcome Outcome, ProgressSnapshot Rows)? applied = await reset.ApplyToStoredAsync(store, player.SteamId, engine.Ranks, TestContext.CancellationToken).ConfigureAwait(false);
		if (applied is not (CorrectionOutcome outcome, ProgressSnapshot rows))
		{
			Assert.Fail("The stored player wasn't found.");
			return;
		}

		Assert.AreEqual(1, outcome.WeaponsCleared);
		await reset.WriteAsync(store, rows, TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress? reloaded = await store.LoadAsync(player.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(0, reloaded?.RankXp);
		Assert.IsEmpty(reloaded!.Weapons);
		Assert.ThrowsExactly<ArgumentException>(() => reset.WriteAsync(store, new ProgressSnapshot(null, [], []), TestContext.CancellationToken));
	}

	[TestMethod]
	public void TargetsResolveBySteamIdUserIdAndName()
	{
		TargetCandidate[] humans = [new(76561198000000001, 3, "Alice"), new(76561198000000002, 7, "Bob"), new(76561198000000003, 9, "Bobby")];

		Assert.AreEqual(76561198000000002UL, CorrectionTargets.Resolve("#7", humans).SteamId);
		Assert.AreEqual("Alice", CorrectionTargets.Resolve("76561198000000001", humans).Name);
		TargetResolution offline = CorrectionTargets.Resolve("76561198999999999", humans);
		Assert.IsTrue(offline.Found);
		Assert.AreEqual("76561198999999999", offline.Name);
		Assert.AreEqual(76561198000000002UL, CorrectionTargets.Resolve("bob", humans).SteamId);
		Assert.AreEqual(76561198000000001UL, CorrectionTargets.Resolve(" lic ", humans).SteamId);
	}

	[TestMethod]
	public void TargetsExplainMissingAndAmbiguousMatches()
	{
		TargetCandidate[] humans = [new(76561198000000002, 7, "Bob"), new(76561198000000003, 9, "Bobby"), new(76561198000000004, 11, "Rob")];

		TargetResolution ambiguous = CorrectionTargets.Resolve("ob", humans);
		Assert.IsFalse(ambiguous.Found);
		StringAssert.Contains(ambiguous.Error, "matches 3 players");
		StringAssert.Contains(ambiguous.Error, "Bobby (#9)");
		Assert.IsFalse(CorrectionTargets.Resolve("#99", humans).Found);
		Assert.IsFalse(CorrectionTargets.Resolve("Zed", humans).Found);
		Assert.IsFalse(CorrectionTargets.Resolve("", humans).Found);
		Assert.IsFalse(CorrectionTargets.Resolve("12345", humans).Found);
	}

	[TestMethod]
	public void ResetRunsOnlyWhenTheSameAdminRepeatsItInTime()
	{
		ResetConfirmation confirmation = new(15);

		Assert.IsFalse(confirmation.Confirm(1, 100, 0));
		Assert.IsTrue(confirmation.Confirm(1, 100, 10));
		Assert.IsFalse(confirmation.Confirm(1, 100, 11), "A confirmed reset starts over.");

		Assert.IsFalse(confirmation.Confirm(2, 100, 12), "Another admin doesn't confirm it.");
		Assert.IsFalse(confirmation.Confirm(2, 200, 13), "Another player doesn't confirm it.");
		Assert.IsFalse(confirmation.Confirm(2, 200, 40), "Too late.");
		Assert.IsTrue(confirmation.Confirm(2, 200, 41));
		StringAssert.Contains(confirmation.Warning("Tester", 100), "within 15 seconds");
	}
}
