// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class CompareTests
{
	private const ulong OtherId = TestHelpers.SteamId + 1;

	private static readonly PlayerSeasonsView NoSeason = new("Oct–Dec 2026", 0, null, 0, []);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-compare-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState NewState() =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

	private static PlayerProgress Shooter()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Kills = 10;
		ak.Headshots = 5;
		ak.Hits = 30;
		ak.Shots = 60;
		ak.Challenge("moving_target").Count = 3;
		return player;
	}

	private static PlayerProgress Sniper()
	{
		PlayerProgress player = new(OtherId) { Name = "Other" };
		WeaponProgress awp = player.Weapon(TestHelpers.Weapon("awp").Key);
		awp.Kills = 30;
		awp.Headshots = 3;
		awp.Hits = 30;
		awp.Shots = 40;
		awp.Challenge("moving_target").Count = 1;
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Kills = 2;
		return player;
	}

	[TestMethod]
	public void Build_TotalsEachPlayerAcrossWeapons()
	{
		CompareView view = CompareViewBuilder.Build(Shooter(), true, NoSeason, Sniper(), false, NoSeason, new ProgressionSettings());

		Assert.AreEqual("Tester", view.A.Name);
		Assert.IsTrue(view.A.Online);
		Assert.AreEqual(10, view.A.Kills);
		Assert.AreEqual(50, view.A.HeadshotPercent);
		Assert.AreEqual(50, view.A.Accuracy);
		Assert.AreEqual(32, view.B.Kills);
		Assert.AreEqual(9.4, view.B.HeadshotPercent);
		Assert.AreEqual(75, view.B.Accuracy);
	}

	[TestMethod]
	public void Build_ListsEveryWeaponEitherPlayerUsed()
	{
		CompareView view = CompareViewBuilder.Build(Shooter(), false, NoSeason, Sniper(), false, NoSeason, new ProgressionSettings());

		Assert.HasCount(2, view.Weapons);
		CompareWeaponRow awp = view.Weapons.Single(w => w.Key == TestHelpers.Weapon("awp").Key.WeakString);
		Assert.IsNull(awp.A);
		Assert.AreEqual(30, awp.B!.Kills);
		CompareWeaponRow ak = view.Weapons.Single(w => w.Key == TestHelpers.Weapon("ak47").Key.WeakString);
		Assert.AreEqual(10, ak.A!.Kills);
		Assert.AreEqual(2, ak.B!.Kills);
	}

	[TestMethod]
	public void Build_AddsSkillShotsAcrossWeapons()
	{
		PlayerProgress shooter = Shooter();
		shooter.Weapon(TestHelpers.Weapon("m4a1").Key).Challenge("moving_target").Count = 2;

		CompareView view = CompareViewBuilder.Build(shooter, false, NoSeason, Sniper(), false, NoSeason, new ProgressionSettings());

		CompareSkillShotRow row = view.SkillShots.Single(s => s.Id == "moving_target");
		Assert.AreEqual(5, row.A);
		Assert.AreEqual(1, row.B);
		Assert.AreEqual(5, view.A.SkillShots);
		Assert.HasCount(1, view.SkillShots);
	}

	[TestMethod]
	public void Build_CarriesSeasonPlace()
	{
		PlayerSeasonsView second = new("Oct–Dec 2026", 400, 2, 5, []);

		CompareView view = CompareViewBuilder.Build(Shooter(), false, second, Sniper(), false, NoSeason, new ProgressionSettings());

		Assert.AreEqual(2, view.A.SeasonPlace);
		Assert.AreEqual(400, view.A.SeasonXp);
		Assert.IsNull(view.B.SeasonPlace);
	}

	[TestMethod]
	public async Task Endpoint_TwoStoredPlayers_ReturnsComparison()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(ProgressSnapshot.CaptureAll(Shooter()), CancellationToken.None).ConfigureAwait(false);
		await store.SaveAsync(ProgressSnapshot.CaptureAll(Sniper()), CancellationToken.None).ConfigureAwait(false);
		using ProgressReader reader = new(store);

		IResult result = await CompareEndpoints.CompareAsync(Id(TestHelpers.SteamId), Id(OtherId), reader, NewState(), new ManualTime(), CancellationToken.None).ConfigureAwait(false);

		Ok<CompareView> ok = Assert.IsInstanceOfType<Ok<CompareView>>(result);
		Assert.AreEqual("Tester", ok.Value!.A.Name);
		Assert.AreEqual("Other", ok.Value.B.Name);
	}

	[TestMethod]
	public async Task Endpoint_UnknownPlayer_Returns404()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(ProgressSnapshot.CaptureAll(Shooter()), CancellationToken.None).ConfigureAwait(false);
		using ProgressReader reader = new(store);

		IResult result = await CompareEndpoints.CompareAsync(Id(TestHelpers.SteamId), Id(OtherId), reader, NewState(), new ManualTime(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task Endpoint_BadId_Returns404()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await CompareEndpoints.CompareAsync(Id(TestHelpers.SteamId), "not-a-steam-id", reader, NewState(), new ManualTime(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task Endpoint_SamePlayerTwice_Returns400()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await CompareEndpoints.CompareAsync(Id(TestHelpers.SteamId), Id(TestHelpers.SteamId), reader, NewState(), new ManualTime(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsInstanceOfType<BadRequest>(result);
	}

	private static string Id(ulong steamId) => steamId.ToString(CultureInfo.InvariantCulture);
}
