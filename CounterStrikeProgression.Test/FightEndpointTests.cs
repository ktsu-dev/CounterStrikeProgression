// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class FightEndpointTests
{
	private const ulong Player = 76561198000000007;
	private const ulong Rival = 76561198000000008;
	private const string Dust = "de_dust2";

	private static readonly DateTime At = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-fights-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private static KillPoint Kill(string map, ulong killer, ulong victim, int seconds = 0) =>
		new(map, killer, victim, "ak47", false, 0, 0, 0, 400, 400, 0, At.AddSeconds(seconds));

	private static ServerSnapshot Server(string map) => new(At, map, "Deathmatch", 16, []);

	private static string Text(ulong id) => id.ToString(CultureInfo.InvariantCulture);

	private async Task<IResult> CallAsync(string id, string? scope, string? map, InMemoryKillMapStore store, MatchFights match)
	{
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), TimeProvider.System, NullLogger<LiveState>.Instance);
		RadarImages radars = new(new SiteOptions { DataPath = directory });
		return await FightEndpoints.FightsAsync(id, scope, map, store, match, live, radars, TestContext.CancellationToken).ConfigureAwait(false);
	}

	private static FightView View(IResult result) => ((Ok<FightView>)result).Value!;

	[TestMethod]
	public async Task Fights_MatchScopeCountsTheCurrentMatch()
	{
		InMemoryKillMapStore store = new();
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);
		match.Apply(Server(Dust), [Kill(Dust, Player, Rival), Kill(Dust, Player, 0, 1), Kill(Dust, Rival, Player, 2)]);

		FightView view = View(await CallAsync(Text(Player), "match", null, store, match).ConfigureAwait(false));

		Assert.AreEqual(Dust, view.Map);
		Assert.AreEqual("match", view.Scope);
		Assert.IsTrue(view.Calibrated);
		Assert.AreEqual(FightGrid.CellPixels, view.CellPixels);
		Assert.AreEqual(2, view.Cells.Sum(c => c.Won));
		Assert.AreEqual(1, view.Cells.Sum(c => c.Lost));
	}

	[TestMethod]
	public async Task Fights_AllScopeReadsTheStore()
	{
		InMemoryKillMapStore store = new();
		await store.AddAsync([Kill(Dust, Player, Rival), Kill(Dust, Player, 0, 1)], 100, TestContext.CancellationToken).ConfigureAwait(false);
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);

		FightView view = View(await CallAsync(Text(Player), "all", Dust, store, match).ConfigureAwait(false));

		Assert.AreEqual("all", view.Scope);
		Assert.AreEqual(Dust, view.Map);
		Assert.AreEqual(2, view.Cells.Sum(c => c.Won));
		Assert.AreEqual(0, view.Cells.Sum(c => c.Lost));
	}

	[TestMethod]
	public async Task Fights_AllScopeFallsBackToTheLastMapPlayed()
	{
		InMemoryKillMapStore store = new();
		await store.AddAsync([Kill(Dust, Player, Rival)], 100, TestContext.CancellationToken).ConfigureAwait(false);
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);

		FightView view = View(await CallAsync(Text(Player), null, null, store, match).ConfigureAwait(false));

		Assert.AreEqual(Dust, view.Map);
		Assert.AreEqual(1, view.Cells.Sum(c => c.Won));
	}

	[TestMethod]
	public async Task Fights_EmptyForAPlayerWithNoFights()
	{
		InMemoryKillMapStore store = new();
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);

		FightView view = View(await CallAsync("76561198000000099", "all", Dust, store, match).ConfigureAwait(false));

		Assert.IsEmpty(view.Cells);
	}

	[TestMethod]
	public async Task Fights_UncalibratedMapHasNoCells()
	{
		InMemoryKillMapStore store = new();
		await store.AddAsync([Kill("de_unknown", Player, Rival)], 100, TestContext.CancellationToken).ConfigureAwait(false);
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);

		FightView view = View(await CallAsync(Text(Player), "all", "de_unknown", store, match).ConfigureAwait(false));

		Assert.IsFalse(view.Calibrated);
		Assert.IsEmpty(view.Cells);
	}

	[TestMethod]
	public async Task Fights_BadIdIs404()
	{
		InMemoryKillMapStore store = new();
		MatchFights match = new(store, NullLogger<MatchFights>.Instance);

		Assert.IsInstanceOfType<NotFound>(await CallAsync("123", "all", null, store, match).ConfigureAwait(false));
	}
}
