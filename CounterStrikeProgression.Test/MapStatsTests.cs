// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class MapStatsTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-maps-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	[TestMethod]
	[DataRow("de_dust2", "de_dust2")]
	[DataRow("  DE_Inferno ", "de_inferno")]
	[DataRow("workshop/3070284539/aim_map", "aim_map")]
	[DataRow("workshop\\123\\Aim_Redline", "aim_redline")]
	[DataRow("", "")]
	[DataRow(null, "")]
	public void NormalizeLowersAndDropsTheWorkshopPath(string? map, string expected) =>
		Assert.AreEqual(expected, MapStats.Normalize(map));

	[TestMethod]
	public void NormalizeCutsLongNamesToTheColumn() =>
		Assert.HasCount(MapStats.MaxNameLength, MapStats.Normalize(new string('a', 100)));

	[TestMethod]
	public void RecapsAddUpPerMap()
	{
		PlayerProgress player = TestHelpers.Player();

		Assert.IsTrue(MapStats.Add(player, Recap("de_dust2", xp: 300, kills: 10, deaths: 4, minutes: 20)));
		Assert.IsTrue(MapStats.Add(player, Recap("DE_DUST2", xp: 200, kills: 5, deaths: 6, minutes: 10)));
		Assert.IsTrue(MapStats.Add(player, Recap("de_mirage", xp: 50, kills: 1, deaths: 2, minutes: 5)));

		Assert.HasCount(2, player.Maps);
		MapProgress dust = player.Maps["de_dust2"];
		Assert.AreEqual(500, dust.Xp);
		Assert.AreEqual(15, dust.Kills);
		Assert.AreEqual(10, dust.Deaths);
		Assert.AreEqual(30 * 60, dust.Seconds);
		Assert.AreEqual(2, dust.Sessions);
		Assert.IsTrue(dust.IsDirty);
	}

	[TestMethod]
	public void RecapsWithoutAMapAreSkipped()
	{
		PlayerProgress player = TestHelpers.Player();
		Assert.IsFalse(MapStats.Add(player, Recap(" ", xp: 300, kills: 1, deaths: 1, minutes: 20)));
		Assert.IsEmpty(player.Maps);
	}

	[TestMethod]
	public void BestMapIsTheMostXpPerHourWithEnoughPlay()
	{
		PlayerProgress player = TestHelpers.Player();
		MapStats.Add(player, Recap("de_dust2", xp: 600, kills: 0, deaths: 0, minutes: 60));
		MapStats.Add(player, Recap("de_mirage", xp: 400, kills: 0, deaths: 0, minutes: 20));

		// Far more XP per hour, but too short to count.
		MapStats.Add(player, Recap("aim_map", xp: 300, kills: 0, deaths: 0, minutes: 5));

		Assert.AreEqual("de_mirage", MapStats.Best(player.Maps));
		Assert.AreEqual(1200, MapStats.XpPerHour(400, 20 * 60));
	}

	[TestMethod]
	public void NoBestMapBeforeEnoughPlay()
	{
		PlayerProgress player = TestHelpers.Player();
		MapStats.Add(player, Recap("de_dust2", xp: 600, kills: 0, deaths: 0, minutes: 14));
		Assert.IsNull(MapStats.Best(player.Maps));
		Assert.AreEqual(0, MapStats.XpPerHour(100, 0));
	}

	[TestMethod]
	public void SnapshotsCaptureRestoreAndRebuildMaps()
	{
		PlayerProgress player = TestHelpers.Player();
		MapStats.Add(player, Recap("de_dust2", xp: 300, kills: 10, deaths: 4, minutes: 20));

		ProgressSnapshot first = ProgressSnapshot.Capture(player);
		Assert.IsFalse(first.IsEmpty);
		Assert.HasCount(1, first.Maps);
		Assert.AreEqual(new MapRow(TestHelpers.SteamId, "de_dust2", 300, 10, 4, 1200, 1), first.Maps[0]);
		Assert.IsEmpty(ProgressSnapshot.Capture(player).Maps);

		first.Restore(player);
		Assert.IsTrue(player.Maps["de_dust2"].IsDirty);
		Assert.HasCount(1, ProgressSnapshot.CaptureAll(player).Maps);

		PlayerProgress rebuilt = ProgressSnapshot.Rebuild(ProgressSnapshot.ToRow(player), [], [], maps: first.Maps);
		Assert.AreEqual(300, rebuilt.Maps["de_dust2"].Xp);
		Assert.AreEqual(1, rebuilt.Maps["de_dust2"].Sessions);
		Assert.IsFalse(rebuilt.Maps["de_dust2"].IsDirty);
	}

	[TestMethod]
	public async Task StoreKeepsMapsAndRanksThem()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", ("de_dust2", 300, 60), ("de_mirage", 100, 10)).ConfigureAwait(false);
		await SaveAsync(store, 2, "Two", ("de_dust2", 900, 30)).ConfigureAwait(false);
		await SaveAsync(store, 3, "Three", ("de_dust2", 0, 5)).ConfigureAwait(false);

		PlayerProgress loaded = (await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false))!;
		Assert.HasCount(2, loaded.Maps);
		Assert.AreEqual(300, loaded.Maps["de_dust2"].Xp);

		IReadOnlyList<MapStanding> dust = await store.MapStandingsAsync("de_dust2", 10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual("Two,One", string.Join(',', dust.Select(s => s.Name)));

		IReadOnlyList<MapSummary> summaries = await store.MapSummariesAsync(TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(2, summaries);
		Assert.AreEqual(new MapSummary("de_dust2", 2, 1200, 90 * 60), summaries[0]);
		Assert.AreEqual("de_mirage", summaries[1].Map);
	}

	[TestMethod]
	public async Task ResetClearsTheMaps()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", ("de_dust2", 300, 60)).ConfigureAwait(false);
		PlayerProgress player = (await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false))!;

		new AdminCorrection(CorrectionKind.Reset).Apply(player, new ProgressionEngine(new ProgressionSettings(), new ManualTime()).Ranks);
		await store.ResetAsync(ProgressSnapshot.ToRow(player), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(player.Maps);
		Assert.IsEmpty(await store.MapSummariesAsync(TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsEmpty((await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false))!.Maps);
	}

	[TestMethod]
	public async Task MapsEndpoint_ShowsTheMostPlayedMapUnlessOneIsAsked()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", ("de_dust2", 300, 60), ("de_mirage", 500, 10)).ConfigureAwait(false);
		await SaveAsync(store, 2, "Two", ("de_mirage", 100, 10)).ConfigureAwait(false);
		using ProgressReader reader = new(store);
		LiveState live = NewState();

		MapBoardView most = Assert.IsInstanceOfType<Ok<MapBoardView>>(await MapEndpoints.MapsAsync(null, reader, live, CancellationToken.None).ConfigureAwait(false)).Value!;
		Assert.AreEqual("de_dust2", most.Map);
		Assert.HasCount(2, most.Maps);
		Assert.HasCount(1, most.Rows);
		Assert.AreEqual(300, most.Rows[0].XpPerHour);

		MapBoardView mirage = Assert.IsInstanceOfType<Ok<MapBoardView>>(await MapEndpoints.MapsAsync("DE_Mirage", reader, live, CancellationToken.None).ConfigureAwait(false)).Value!;
		Assert.AreEqual("de_mirage", mirage.Map);
		Assert.AreEqual("One,Two", string.Join(',', mirage.Rows.Select(r => r.Name)));
		Assert.AreEqual(2, mirage.Rows[1].Position);

		MapBoardView unknown = Assert.IsInstanceOfType<Ok<MapBoardView>>(await MapEndpoints.MapsAsync("de_nowhere", reader, live, CancellationToken.None).ConfigureAwait(false)).Value!;
		Assert.AreEqual("de_dust2", unknown.Map);
	}

	[TestMethod]
	public async Task MapsEndpoint_IsEmptyBeforeAnyMapEnds()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());
		MapBoardView view = Assert.IsInstanceOfType<Ok<MapBoardView>>(await MapEndpoints.MapsAsync(null, reader, NewState(), CancellationToken.None).ConfigureAwait(false)).Value!;
		Assert.IsNull(view.Map);
		Assert.IsEmpty(view.Maps);
		Assert.IsEmpty(view.Rows);
	}

	[TestMethod]
	public async Task PlayerMapsEndpoint_ListsMapsAndTheBest()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, TestHelpers.SteamId, "Tester", ("de_dust2", 300, 60), ("de_mirage", 500, 20), ("aim_map", 900, 5)).ConfigureAwait(false);
		using ProgressReader reader = new(store);

		IResult result = await MapEndpoints.PlayerMapsAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), reader, CancellationToken.None).ConfigureAwait(false);

		PlayerMapsView view = Assert.IsInstanceOfType<Ok<PlayerMapsView>>(result).Value!;
		Assert.AreEqual("de_mirage", view.Best);
		Assert.AreEqual(15, view.MinimumMinutes);
		Assert.AreEqual("aim_map,de_mirage,de_dust2", string.Join(',', view.Maps.Select(m => m.Map)));
		Assert.AreEqual(1500, view.Maps[1].XpPerHour);
	}

	[TestMethod]
	public async Task PlayerMapsEndpoint_RejectsBadAndUnknownIds()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());
		Assert.IsInstanceOfType<NotFound>(await MapEndpoints.PlayerMapsAsync("123", reader, CancellationToken.None).ConfigureAwait(false));
		Assert.IsInstanceOfType<NotFound>(await MapEndpoints.PlayerMapsAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), reader, CancellationToken.None).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MySql_WritesAndReadsMaps()
	{
		// Runs against a real MariaDB only when CSP_TEST_MYSQL names an empty, disposable database as host:port/database:user:password.
		string? target = Environment.GetEnvironmentVariable("CSP_TEST_MYSQL");
		if (string.IsNullOrEmpty(target))
		{
			Assert.Inconclusive("Set CSP_TEST_MYSQL to run against MariaDB.");
			return;
		}

		string[] parts = target.Replace('/', ':').Split(':', StringSplitOptions.None);
		DatabaseSettings settings = new() { Host = parts[0], Port = int.Parse(parts[1], CultureInfo.InvariantCulture), Database = parts[2], User = parts[3], Password = parts[4] };
		MySqlProgressionStore store = new(settings);
		await store.InitializeAsync(TestContext.CancellationToken).ConfigureAwait(false);

		ulong steamId = TestHelpers.SteamId + (ulong)Random.Shared.Next(1_000, 1_000_000);
		string map = "test_" + Guid.NewGuid().ToString("N")[..12];
		PlayerProgress player = new(steamId) { Name = "Tester", FirstSeen = Start, LastSeen = Start, IsDirty = true };
		MapStats.Add(player, Recap(map, xp: 300, kills: 10, deaths: 4, minutes: 20));
		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		MapStats.Add(player, Recap(map, xp: 200, kills: 5, deaths: 1, minutes: 10));
		await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress loaded = (await store.LoadAsync(steamId, TestContext.CancellationToken).ConfigureAwait(false))!;
		Assert.AreEqual(500, loaded.Maps[map].Xp);
		Assert.AreEqual(2, loaded.Maps[map].Sessions);

		IReadOnlyList<MapStanding> board = await store.MapStandingsAsync(map, 10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(new MapStanding(steamId, "Tester", 0, 500, 15, 5, 1800), board.Single());

		IReadOnlyList<MapSummary> summaries = await store.MapSummariesAsync(TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(new MapSummary(map, 1, 500, 1800), summaries.Single(s => s.Map == map));

		await store.ResetAsync(new PlayerRow(steamId, "Tester", 0, 0, Start, Start), TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsEmpty(await store.MapStandingsAsync(map, 10, TestContext.CancellationToken).ConfigureAwait(false));
	}

	private static MapRecap Recap(string map, long xp, int kills, int deaths, int minutes) =>
		new(TestHelpers.SteamId, "Tester", map, Start, Start.AddMinutes(minutes), xp, kills, deaths, 0, 0, 0, 0, 0, null);

	private LiveState NewState() =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

	private async Task SaveAsync(InMemoryProgressionStore store, ulong steamId, string name, params (string Map, long Xp, int Minutes)[] maps)
	{
		ProgressSnapshot rows = new(new PlayerRow(steamId, name, maps.Sum(m => m.Xp), 0, Start, Start), [], [])
		{
			Maps = [.. maps.Select(m => new MapRow(steamId, m.Map, m.Xp, 1, 1, m.Minutes * 60L, 1))],
		};
		await store.SaveAsync(rows, TestContext.CancellationToken).ConfigureAwait(false);
	}
}
