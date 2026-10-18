// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

[TestClass]
public sealed class WelcomeBackTests
{
	private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	public TestContext TestContext { get; set; } = null!;

	private static PlayerProgress Returning(int daysAway)
	{
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 5000;
		player.LastSeen = Now.AddDays(-daysAway);
		return player;
	}

	[TestMethod]
	public void AWeekAwayGrantsTheBonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(7);

		Assert.IsTrue(engine.GrantWelcomeBack(player, Now));
		Assert.AreEqual(2000, player.WelcomeBackXp);
		Assert.IsTrue(player.IsDirty);
	}

	[TestMethod]
	public void LessThanTheGapGrantsNothing()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(6);

		Assert.IsFalse(engine.GrantWelcomeBack(player, Now));
		Assert.AreEqual(0, player.WelcomeBackXp);
	}

	[TestMethod]
	public void NewPlayersAndADisabledBonusGrantNothing()
	{
		PlayerProgress newcomer = TestHelpers.Player();
		newcomer.LastSeen = Now.AddDays(-30);
		Assert.IsFalse(TestHelpers.Engine().GrantWelcomeBack(newcomer, Now));

		ProgressionEngine off = TestHelpers.Engine(s => s.WelcomeBackXp = 0);
		Assert.IsFalse(off.GrantWelcomeBack(Returning(30), Now));
	}

	[TestMethod]
	public void ALeftoverBonusTopsUpRatherThanStacks()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(10);
		player.WelcomeBackXp = 300;

		engine.GrantWelcomeBack(player, Now);

		Assert.AreEqual(2000, player.WelcomeBackXp);
	}

	[TestMethod]
	public void RankXpCountsDoubleUntilThePoolRunsOut()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		long normal = engine.ApplyCombat(TestHelpers.Player(), TestHelpers.Kill("ak47")).XpGained;
		long half = normal / 2;
		PlayerProgress player = TestHelpers.Player();
		player.WelcomeBackXp = normal + half;

		ProgressionResult first = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		ProgressionResult second = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		ProgressionResult third = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(normal * 2, first.XpGained);
		Assert.AreEqual(normal + half, second.XpGained);
		Assert.AreEqual(normal, third.XpGained);
		Assert.AreEqual(0, player.WelcomeBackXp);
		Assert.AreEqual(player.RankXp, engine.SeasonXp(player));
	}

	[TestMethod]
	public void TheJoinMessageAndPanelMentionTheBonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(8);
		engine.GrantWelcomeBack(player, Now);

		IReadOnlyList<string> lines = new ChatReports(engine).WelcomeLines("Tester", player, null);
		Assert.Contains("Welcome-back bonus: your next 2,000 rank XP counts double.", lines);

		PanelPage page = new PanelBuilder(engine).Overview(player);
		Assert.Contains(new PanelLine("Welcome back: 2x rank XP", "2,000 XP left", LineState.InProgress), page.Lines);
	}

	[TestMethod]
	public void NoMentionWithoutABonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(1);

		IReadOnlyList<string> lines = new ChatReports(engine).WelcomeLines("Tester", player, null);
		Assert.IsFalse(lines.Any(l => l.Contains("Welcome-back", StringComparison.Ordinal)));
		Assert.IsFalse(new PanelBuilder(engine).Overview(player).Lines.Any(l => l.Label.StartsWith("Welcome back", StringComparison.Ordinal)));
	}

	[TestMethod]
	public async Task TheBonusLeftIsSavedRoundedUp()
	{
		InMemoryProgressionStore store = new();
		PlayerProgress player = Returning(8);
		player.WelcomeBackXp = 1234.25;
		player.IsDirty = true;

		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		PlayerProgress? loaded = await store.LoadAsync(player.SteamId, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNotNull(loaded);
		Assert.AreEqual(1235, loaded.WelcomeBackXp);
	}

	[TestMethod]
	public void AResetClearsTheBonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = Returning(8);
		engine.GrantWelcomeBack(player, Now);

		new AdminCorrection(CorrectionKind.Reset).Apply(player, engine.Ranks);

		Assert.AreEqual(0, player.WelcomeBackXp);
	}
}
