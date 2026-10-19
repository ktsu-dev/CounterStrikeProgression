// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Discord;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

[TestClass]
public sealed class WeeklyDigestTests
{
	// Monday 19 October 2026 starts the new week, so the digest covers 12 to 18 October.
	private static readonly DateOnly Monday = new(2026, 10, 19);
	private static readonly DateOnly LastMonday = new(2026, 10, 12);
	private static readonly ProgressionSettings Progression = new();
	private static readonly RankLadder Ranks = new(Progression.FirstRankXp, Progression.RankGrowth);
	private const int GoldNova = 3 * RankLadder.StagesPerRank;

	public TestContext TestContext { get; set; } = null!;

	private static DateTime At(DateOnly day, int hour = 12) => day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

	private static ProgressSnapshot Save(ulong steamId, string name, long rankXp, DateOnly firstSeen, DateOnly lastSeen) =>
		new(new PlayerRow(steamId, name, rankXp, 0, At(firstSeen), At(lastSeen)), [], []);

	private async Task<InMemoryProgressionStore> StoreAsync(params ProgressSnapshot[] saves)
	{
		InMemoryProgressionStore store = new();
		foreach (ProgressSnapshot save in saves)
		{
			await store.SaveAsync(save, TestContext.CancellationToken).ConfigureAwait(false);
		}

		return store;
	}

	[TestMethod]
	public async Task GainsBetween_CountsOnlyTheSpan()
	{
		InMemoryProgressionStore store = await StoreAsync(
			Save(1, "Old", 1000, LastMonday.AddDays(-30), LastMonday.AddDays(-1)),
			Save(1, "Old", 1600, LastMonday.AddDays(-30), LastMonday.AddDays(3)),
			Save(1, "Old", 9000, LastMonday.AddDays(-30), Monday),
			Save(2, "New", 400, LastMonday.AddDays(5), LastMonday.AddDays(5)),
			Save(3, "Idle", 7000, LastMonday.AddDays(-30), LastMonday.AddDays(-2)),
			Save(4, "Today", 500, Monday, Monday)).ConfigureAwait(false);

		IReadOnlyList<PeriodGain> gains = await store.GainsBetweenAsync(LastMonday, Monday, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.HasCount(2, gains);
		Assert.AreEqual(new PeriodGain(1, "Old", 1000, 1600), gains[0]);
		Assert.AreEqual(new PeriodGain(2, "New", 0, 400), gains[1]);
	}

	[TestMethod]
	public void Build_ListsClimbersRankUpsAndTheWeeklyContract()
	{
		long goldNova = Ranks.ThresholdFor(GoldNova);
		PeriodGain[] gains =
		[
			new(1, "Top_Dog", goldNova - 10, goldNova + 5000),
			new(2, "Second", 100, 3100),
			new(3, "Third", 0, 2000),
			new(4, "Fourth", 0, 50),
		];
		DiscordSettings settings = new() { WeeklyDigestPlayers = 3 };

		string? post = WeeklyDigest.Build(Monday, gains, Ranks, Progression, settings);

		ActiveContract weekly = ContractCatalog.Active(Monday, Progression).Single(c => c.Definition.Period == ContractPeriod.Weekly);
		string[] lines = post!.Split('\n');
		Assert.AreEqual("📅 **Weekly digest** for 12 to 18 October", lines[0]);
		Assert.AreEqual("**Top climbers**", lines[1]);
		Assert.AreEqual($"🥇 **Top\\_Dog**: +5,010 XP", lines[2]);
		Assert.AreEqual("🥈 **Second**: +3,000 XP", lines[3]);
		Assert.AreEqual("🥉 **Third**: +2,000 XP", lines[4]);
		Assert.AreEqual("**Rank-ups**", lines[5]);
		Assert.AreEqual($"⬆️ **Top\\_Dog** reached **{RankLadder.Names[Ranks.RankFor(goldNova + 5000)]}**", lines[6]);
		Assert.AreEqual("**This week's contract**", lines[7]);
		Assert.AreEqual($"📜 {weekly.Definition.Description} (+2,500 XP)", lines[8]);
		Assert.HasCount(9, lines);
	}

	[TestMethod]
	public void Build_NamesBothMonthsAcrossAMonthEnd()
	{
		string? post = WeeklyDigest.Build(new DateOnly(2026, 11, 2), [new(1, "A", 0, 10)], Ranks, Progression, new DiscordSettings());

		Assert.StartsWith("📅 **Weekly digest** for 26 October to 1 November\n", post!);
	}

	[TestMethod]
	public void Build_LeavesOutTheContractWhenContractsAreOff()
	{
		string? post = WeeklyDigest.Build(Monday, [new(1, "A", 0, 10)], Ranks, new ProgressionSettings { ContractsEnabled = false }, new DiscordSettings());

		Assert.DoesNotContain("contract", post!);
	}

	[TestMethod]
	public void Build_IsNothingForAnEmptyWeek() =>
		Assert.IsNull(WeeklyDigest.Build(Monday, [], Ranks, Progression, new DiscordSettings()));

	[TestMethod]
	public async Task Post_SendsOncePerWeek()
	{
		InMemoryProgressionStore store = await StoreAsync(Save(1, "Player", 800, LastMonday, LastMonday.AddDays(2))).ConfigureAwait(false);
		List<string> sent = [];
		bool Send(string post)
		{
			sent.Add(post);
			return true;
		}

		Assert.IsTrue(await WeeklyDigest.PostAsync(store, Ranks, Progression, new DiscordSettings(), Send, At(Monday, 0), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsFalse(await WeeklyDigest.PostAsync(store, Ranks, Progression, new DiscordSettings(), Send, At(Monday.AddDays(3)), TestContext.CancellationToken).ConfigureAwait(false));

		Assert.HasCount(1, sent);
		Assert.Contains("**Player**: +800 XP", sent[0]);
	}

	[TestMethod]
	public async Task Post_SkipsAWeekNobodyPlayed()
	{
		InMemoryProgressionStore store = await StoreAsync(Save(1, "Player", 800, LastMonday.AddDays(-20), LastMonday.AddDays(-10))).ConfigureAwait(false);

		Assert.IsFalse(await WeeklyDigest.PostAsync(store, Ranks, Progression, new DiscordSettings(), _ => true, At(Monday), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsTrue(await store.ClaimDigestAsync(Monday, At(Monday), TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task Poster_ChecksOncePerWeekAndRetriesAfterAFailure()
	{
		InMemoryProgressionStore store = await StoreAsync(Save(1, "Player", 800, LastMonday, LastMonday.AddDays(2))).ConfigureAwait(false);
		List<string> sent = [];
		List<Exception> failures = [];
		bool fail = true;
		bool Send(string post)
		{
			if (fail)
			{
				fail = false;
				throw new InvalidOperationException("Discord is down");
			}

			sent.Add(post);
			return true;
		}

		WeeklyDigestPoster poster = new(store, Ranks, Progression, new DiscordSettings(), Send, failures.Add);

		await poster.Check(At(Monday))!.ConfigureAwait(false);
		Assert.HasCount(1, failures);

		// The failed send had already claimed the week, so the retry finds it posted and gives up on it.
		await poster.Check(At(Monday, 13))!.ConfigureAwait(false);
		Assert.IsNull(poster.Check(At(Monday, 14)));
		Assert.IsEmpty(sent);
		Assert.HasCount(1, failures);
	}

	[TestMethod]
	public async Task Poster_PostsAgainTheWeekAfter()
	{
		InMemoryProgressionStore store = await StoreAsync(
			Save(1, "Player", 800, LastMonday, LastMonday.AddDays(2)),
			Save(1, "Player", 1500, LastMonday, Monday.AddDays(2))).ConfigureAwait(false);
		List<string> sent = [];
		WeeklyDigestPoster poster = new(store, Ranks, Progression, new DiscordSettings(), p =>
		{
			sent.Add(p);
			return true;
		}, e => Assert.Fail(e.Message));

		await poster.Check(At(Monday))!.ConfigureAwait(false);
		Assert.IsNull(poster.Check(At(Monday.AddDays(6))));
		await poster.Check(At(Monday.AddDays(7)))!.ConfigureAwait(false);

		Assert.HasCount(2, sent);
		Assert.Contains("+800 XP", sent[0]);
		Assert.Contains("+700 XP", sent[1]);
	}

	[TestMethod]
	public async Task MySql_ReadsGainsAndClaimsOnce()
	{
		// Runs against a real MariaDB only when CSP_TEST_MYSQL names an empty, disposable database as host:port/database:user:password.
		string? target = Environment.GetEnvironmentVariable("CSP_TEST_MYSQL");
		if (string.IsNullOrEmpty(target))
		{
			Assert.Inconclusive("Set CSP_TEST_MYSQL to run against MariaDB.");
			return;
		}

		string[] parts = target.Replace('/', ':').Split(':', StringSplitOptions.None);
		DatabaseSettings settings = new() { Host = parts[0], Port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), Database = parts[2], User = parts[3], Password = parts[4] };
		MySqlProgressionStore store = new(settings);
		await store.InitializeAsync(TestContext.CancellationToken).ConfigureAwait(false);

		ulong old = TestHelpers.SteamId + (ulong)Random.Shared.Next(1_000, 1_000_000);
		ulong fresh = old + 1;
		await store.SaveAsync(Save(old, "Old", 1000, LastMonday.AddDays(-30), LastMonday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(old, "Old", 1600, LastMonday.AddDays(-30), LastMonday.AddDays(3)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(old, "Old", 9000, LastMonday.AddDays(-30), Monday), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(fresh, "New", 400, LastMonday.AddDays(5), LastMonday.AddDays(5)), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<PeriodGain> gains = await store.GainsBetweenAsync(LastMonday, Monday, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(new PeriodGain(old, "Old", 1000, 1600), gains.Single(g => g.SteamId == old));
		Assert.AreEqual(new PeriodGain(fresh, "New", 0, 400), gains.Single(g => g.SteamId == fresh));

		DateOnly week = Monday.AddDays(7 * Random.Shared.Next(1, 100_000));
		Assert.IsTrue(await store.ClaimDigestAsync(week, At(week), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsFalse(await store.ClaimDigestAsync(week, At(week, 13), TestContext.CancellationToken).ConfigureAwait(false));
	}
}
