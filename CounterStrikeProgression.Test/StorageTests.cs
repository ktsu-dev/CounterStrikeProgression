// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using MySqlConnector;

[TestClass]
public sealed class StorageTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void CaptureTakesOnlyChangedRowsAndClearsTheirFlags()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressSnapshot first = ProgressSnapshot.Capture(player);
		Assert.IsNotNull(first.Player);
		Assert.HasCount(1, first.Weapons);
		Assert.IsNotEmpty(first.Challenges);

		ProgressSnapshot second = ProgressSnapshot.Capture(player);
		Assert.IsTrue(second.IsEmpty);
	}

	[TestMethod]
	public void RestoreFlagsTheRowsForTheNextSave()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressSnapshot failed = ProgressSnapshot.Capture(player);
		failed.Restore(player);

		ProgressSnapshot retry = ProgressSnapshot.Capture(player);
		Assert.AreEqual(failed.Player, retry.Player);
		CollectionAssert.AreEquivalent(failed.Weapons.ToList(), retry.Weapons.ToList());
		CollectionAssert.AreEquivalent(failed.Challenges.ToList(), retry.Challenges.ToList());
	}

	[TestMethod]
	public async Task SavingTheSameSnapshotTwiceDoesNotDoubleCount()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressSnapshot snapshot = ProgressSnapshot.Capture(player);
		await store.SaveAsync(snapshot, TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(snapshot, TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress? loaded = await store.LoadAsync(TestHelpers.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsNotNull(loaded);
		Assert.AreEqual(player.RankXp, loaded.RankXp);
		Assert.AreEqual(1, loaded.Weapons[WeaponKey.Create("ak47")].Kills);
	}

	[TestMethod]
	public async Task ProgressRoundTripsThroughTheStore()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		for (int i = 0; i < 30; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { IsHeadshot = (i % 2) == 0 });
		}

		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		PlayerProgress? loaded = await store.LoadAsync(TestHelpers.SteamId, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNotNull(loaded);
		Assert.AreEqual(player.RankXp, loaded.RankXp);
		Assert.AreEqual(player.Rank, loaded.Rank);
		Assert.AreEqual("Tester", loaded.Name);
		WeaponProgress original = player.Weapons[WeaponKey.Create("ak47")];
		WeaponProgress restored = loaded.Weapons[WeaponKey.Create("ak47")];
		Assert.AreEqual(original.Xp, restored.Xp);
		Assert.AreEqual(original.Level, restored.Level);
		Assert.AreEqual(original.Headshots, restored.Headshots);
		Assert.AreEqual(original.Challenges[ChallengeDefinition.KillsId].TierReached, restored.Challenges[ChallengeDefinition.KillsId].TierReached);
		Assert.IsFalse(loaded.IsDirty);
		Assert.IsFalse(restored.IsDirty);
	}

	[TestMethod]
	public async Task UnknownPlayersLoadAsNull()
	{
		InMemoryProgressionStore store = new();
		Assert.IsNull(await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task TheLeaderboardIsOrderedByRankXp()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(1, "Low", 10, 0, DateTime.UtcNow, DateTime.UtcNow), [], []), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(2, "High", 5000, 3, DateTime.UtcNow, DateTime.UtcNow), [], []), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<LeaderboardEntry> top = await store.TopAsync(10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual("High", top[0].Name);
		Assert.AreEqual("Low", top[1].Name);
	}

	[TestMethod]
	public async Task InMemory_Rankings_OrderedByXpThenSteamId()
	{
		InMemoryProgressionStore store = new();
		DateTime seen = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(30, "Tied B", 500, 1, seen, seen), [], []), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(20, "Tied A", 500, 1, seen, seen.AddHours(1)), [], []), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(10, "Top", 9000, 4, seen, seen), [], []), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<RankingEntry> all = await store.RankingsAsync(10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(3, all);
		Assert.AreEqual(10UL, all[0].SteamId);
		Assert.AreEqual(20UL, all[1].SteamId);
		Assert.AreEqual(30UL, all[2].SteamId);
		Assert.AreEqual("Tied A", all[1].Name);
		Assert.AreEqual(seen.AddHours(1), all[1].LastSeen);

		IReadOnlyList<RankingEntry> capped = await store.RankingsAsync(2, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(2, capped);
	}

	[TestMethod]
	public void MySql_ReadsDatesBackAsUtc()
	{
		MySqlConnectionStringBuilder builder = new(MySqlProgressionStore.ConnectionStringFor(new DatabaseSettings()));

		Assert.AreEqual(MySqlDateTimeKind.Utc, builder.DateTimeKind);
	}

	[TestMethod]
	public async Task TopCarriesEachPlayersId()
	{
		InMemoryProgressionStore store = new();
		ulong bot = BotIds.For("Muhlik");
		DateTime now = DateTime.UtcNow;
		WeaponKey ak47 = WeaponKey.Create("ak47");
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(1, "Human", 10, 0, now, now), [new WeaponRow(1, ak47.WeakString, 5, 1, 0, 0, 0, 0)], []), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(new ProgressSnapshot(new PlayerRow(bot, "Muhlik", 5000, 3, now, now), [new WeaponRow(bot, ak47.WeakString, 50, 2, 0, 0, 0, 0)], []), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<LeaderboardEntry> top = await store.TopAsync(10, TestContext.CancellationToken).ConfigureAwait(false);
		IReadOnlyList<WeaponLeaderboardEntry> byWeapon = await store.TopByWeaponAsync(ak47, 10, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(bot, top[0].SteamId);
		Assert.AreEqual(1UL, top[1].SteamId);
		Assert.AreEqual(bot, byWeapon[0].SteamId);
		Assert.AreEqual(1UL, byWeapon[1].SteamId);
	}
}
