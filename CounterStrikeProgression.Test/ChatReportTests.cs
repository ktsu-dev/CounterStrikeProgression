// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;

[TestClass]
public sealed class ChatReportTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void ThePlayerPageLinkJoinsTheSiteAndSteamId()
	{
		Assert.AreEqual("https://cs2.example.com/players/76561198000000001", ChatReports.PlayerPage("https://cs2.example.com", 76561198000000001));
		Assert.AreEqual("https://cs2.example.com/players/76561198000000001", ChatReports.PlayerPage(" https://cs2.example.com/ ", 76561198000000001));
		Assert.AreEqual("http://host:8080/csp/players/1", ChatReports.PlayerPage("http://host:8080/csp/?x=1", 1));
	}

	[TestMethod]
	public void NoLinkWithoutAWebAddress()
	{
		Assert.IsNull(ChatReports.PlayerPage("", 1));
		Assert.IsNull(ChatReports.PlayerPage(null, 1));
		Assert.IsNull(ChatReports.PlayerPage("cs2.example.com", 1));
		Assert.IsNull(ChatReports.PlayerPage("ftp://cs2.example.com", 1));
	}

	[TestMethod]
	public void TheWelcomeGreetsNewAndReturningPlayers()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		ChatReports reports = new(engine);
		PlayerProgress player = TestHelpers.Player();

		Assert.StartsWith("Welcome, Tester!", reports.Welcome("Tester", player));

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		string returning = reports.Welcome("Tester", player);
		Assert.StartsWith("Welcome back, Tester! You're Silver · I with ", returning);
	}

	[TestMethod]
	public void NextListsTheThreeClosestTiers()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		ChatReports reports = new(engine);
		PlayerProgress player = TestHelpers.Player();
		Assert.HasCount(1, reports.Next(player));
		Assert.StartsWith("Nothing in progress yet", reports.Next(player)[0]);

		for (int i = 0; i < 20; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		}

		engine.ApplyCombat(player, TestHelpers.Kill("deagle"));
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		engine.ApplyCombat(player, TestHelpers.Kill("mp9"));

		IReadOnlyList<string> next = reports.Next(player);
		Assert.HasCount(ChatReports.NextTiersShown, next);
		Assert.AreEqual("AK-47 Kills I  20/25", next[0]);
	}

	[TestMethod]
	public void FeatsCountsAndListsEveryFeat()
	{
		ChatReports reports = new(TestHelpers.Engine());
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 2_000_000;

		IReadOnlyList<string> lines = reports.Feats(player);

		Assert.HasCount(FeatCatalog.All.Count + 1, lines);
		Assert.Contains("+ Millionaire: Earn 1,000,000 XP", lines);
		Assert.Contains("o Collector: Earn every skill shot at least once (0/" + Core.SkillShots.SkillShotCatalog.All.Count + ")", lines);
	}

	[TestMethod]
	public void StatsSumsKillsAndNamesTheBestWeapon()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		ChatReports reports = new(engine);
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { IsHeadshot = true });

		string stats = reports.Stats("Tester", player);

		Assert.Contains("3 kills, 33% headshots", stats);
		Assert.Contains("Best weapon AWP, level 1", stats);
		Assert.EndsWith($"Feats 0 of {FeatCatalog.All.Count}.", stats);
	}

	[TestMethod]
	[DataRow("ak", "ak47")]
	[DataRow("AK-47", "ak47")]
	[DataRow("weapon_deagle", "deagle")]
	[DataRow("desert eagle", "deagle")]
	[DataRow("m4a1-s", "m4a1_silencer")]
	[DataRow("M4A4", "m4a1")]
	[DataRow("usp", "usp_silencer")]
	public void WeaponSearchUnderstandsWhatPlayersType(string query, string expected) =>
		Assert.AreEqual(expected, WeaponCatalog.Search(query)?.Key.WeakString);

	[TestMethod]
	[DataRow("")]
	[DataRow("m")]
	[DataRow("banana")]
	public void WeaponSearchRefusesNoneOrSeveralMatches(string query) =>
		Assert.IsNull(WeaponCatalog.Search(query));

	[TestMethod]
	public async Task TheWeaponLeaderboardOrdersByWeaponXp()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		for (ulong id = 1; id <= 3; id++)
		{
			PlayerProgress player = new(id) { Name = $"P{id}" };
			for (ulong k = 0; k < id; k++)
			{
				engine.ApplyCombat(player, TestHelpers.Kill("awp"));
			}

			engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
			await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		}

		IReadOnlyList<WeaponLeaderboardEntry> top = await store.TopByWeaponAsync(WeaponKey.Create("awp"), 2, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.HasCount(2, top);
		Assert.AreEqual("P3", top[0].Name);
		Assert.AreEqual("P2", top[1].Name);
		Assert.StartsWith("1. P3 level ", new ChatReports(engine).WeaponLeader(1, top[0]));
	}

	[TestMethod]
	public void WelcomeLinesLinkThePageOnlyWithASite()
	{
		ChatReports reports = new(TestHelpers.Engine());
		PlayerProgress player = TestHelpers.Player();

		IReadOnlyList<string> withSite = reports.WelcomeLines("Tester", player, "https://cs2.example.com");
		Assert.HasCount(3, withSite);
		Assert.AreEqual($"Your progress page: https://cs2.example.com/players/{TestHelpers.SteamId}", withSite[1]);
		Assert.StartsWith("Type !rank", withSite[2]);

		Assert.HasCount(2, reports.WelcomeLines("Tester", player, ""));
	}

	[TestMethod]
	public void StatsForFindsOnePlayerByName()
	{
		ChatReports reports = new(TestHelpers.Engine());
		(string, PlayerProgress)[] online =
		[
			("Alice", new PlayerProgress(1)),
			("Alicia", new PlayerProgress(2)),
			("Bob", new PlayerProgress(3)),
		];

		IReadOnlyList<string> exact = reports.StatsFor(online, "alice", "https://cs2.example.com");
		Assert.HasCount(2, exact);
		Assert.StartsWith("Alice: ", exact[0]);
		Assert.AreEqual("https://cs2.example.com/players/1", exact[1]);

		IReadOnlyList<string> partial = reports.StatsFor(online, "ob", "");
		Assert.HasCount(1, partial);
		Assert.StartsWith("Bob: ", partial[0]);

		Assert.AreEqual("Several players match: Alice, Alicia. Type more of the name.", reports.StatsFor(online, "ali", "")[0]);
		Assert.AreEqual("No connected player's name contains \"zed\".", reports.StatsFor(online, "zed", "")[0]);
	}
}
