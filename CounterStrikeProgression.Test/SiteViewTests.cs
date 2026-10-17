// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class SiteViewTests
{
	private static readonly DateTime Seen = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private static readonly string[] ExpectedWeaponOrder = ["awp", "glock", "ak47"];

	private static ProgressionSettings Settings() => new() { FirstRankXp = 1000, RankGrowth = 1.25 };

	[TestMethod]
	public void Player_RankProgressUsesSettingsLadder()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 1625;

		PlayerView view = PlayerViewBuilder.Build(player, settings, online: true);

		Assert.AreEqual(1, view.Rank);
		Assert.AreEqual("Silver II", view.RankName);
		Assert.AreEqual("Silver III", view.NextRankName);
		Assert.AreEqual(0.5, view.RankProgress, 0.01);
		Assert.AreEqual(2250 - 1625, view.XpToNextRank);
		Assert.IsTrue(view.Online);
		Assert.AreEqual(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), view.SteamId);
	}

	[TestMethod]
	public void Player_StoredRankAboveLadder_IsKept()
	{
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 0;
		player.Rank = 5;

		PlayerView view = PlayerViewBuilder.Build(player, Settings(), online: false);

		Assert.AreEqual(5, view.Rank);
		Assert.AreEqual("Silver Elite Master", view.RankName);
	}

	[TestMethod]
	public void Player_TopRank_HasNoNextRank()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = new RankLadder(settings.FirstRankXp, settings.RankGrowth).ThresholdFor(RankLadder.TopRank) + 10;

		PlayerView view = PlayerViewBuilder.Build(player, settings, online: false);

		Assert.AreEqual(RankLadder.TopRank, view.Rank);
		Assert.IsNull(view.NextRankName);
		Assert.AreEqual(1, view.RankProgress);
		Assert.AreEqual(0, view.XpToNextRank);
	}

	[TestMethod]
	public void Player_WeaponStats()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Kills = 50;
		ak.Headshots = 20;
		ak.Hits = 300;
		ak.Shots = 1000;
		WeaponProgress awp = player.Weapon(TestHelpers.Weapon("awp").Key);
		awp.Kills = 0;
		awp.Shots = 0;

		PlayerView view = PlayerViewBuilder.Build(player, Settings(), online: false);

		WeaponView akView = view.Weapons.Single(w => w.Key == "ak47");
		Assert.AreEqual(40, akView.HeadshotPercent);
		Assert.AreEqual(30, akView.Accuracy);
		Assert.AreEqual("AK-47", akView.DisplayName);
		WeaponView awpView = view.Weapons.Single(w => w.Key == "awp");
		Assert.AreEqual(0, awpView.HeadshotPercent);
		Assert.AreEqual(0, awpView.Accuracy);
	}

	[TestMethod]
	public void Player_WeaponLevelUsesSettingsCurve()
	{
		ProgressionSettings settings = Settings();
		settings.WeaponLevelBaseXp = 100;
		settings.WeaponLevelStepXp = 0;
		settings.WeaponMaxLevel = 5;
		PlayerProgress player = TestHelpers.Player();
		player.Weapon(TestHelpers.Weapon("ak47").Key).Xp = 250;

		WeaponView view = PlayerViewBuilder.Build(player, settings, online: false).Weapons.Single();

		Assert.AreEqual(3, view.Level);
		Assert.AreEqual(5, view.MaxLevel);
		Assert.AreEqual(0.5, view.LevelProgress, 0.01);
	}

	[TestMethod]
	public void Player_PercentagesAreCappedAt100()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Kills = 10;
		ak.Headshots = 15;
		ak.Shots = 100;
		ak.Hits = 130;

		WeaponView view = PlayerViewBuilder.Build(player, Settings(), online: false).Weapons.Single();

		Assert.AreEqual(100, view.HeadshotPercent);
		Assert.AreEqual(100, view.Accuracy);
	}

	[TestMethod]
	public void Player_StoredLevelAboveCurve_HasZeroProgress()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 10;
		ak.Level = 4;

		WeaponView view = PlayerViewBuilder.Build(player, Settings(), online: false).Weapons.Single();

		Assert.AreEqual(4, view.Level);
		Assert.AreEqual(0, view.LevelProgress);
	}

	[TestMethod]
	public void Player_WeaponsOrderedByXpDescending()
	{
		PlayerProgress player = TestHelpers.Player();
		player.Weapon(TestHelpers.Weapon("ak47").Key).Xp = 100;
		player.Weapon(TestHelpers.Weapon("awp").Key).Xp = 900;
		player.Weapon(TestHelpers.Weapon("glock").Key).Xp = 400;

		PlayerView view = PlayerViewBuilder.Build(player, Settings(), online: false);

		CollectionAssert.AreEqual(ExpectedWeaponOrder, view.Weapons.Select(w => w.Key).ToArray());
	}

	[TestMethod]
	public void Player_UnknownWeaponKey_UsesKeyAsDisplayName()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress removed = player.Weapon(WeaponKey.Create("weapon_removed"));
		removed.Kills = 3;
		removed.Challenge("kills").Count = 3;

		WeaponView view = PlayerViewBuilder.Build(player, Settings(), online: false).Weapons.Single();

		Assert.AreEqual("weapon_removed", view.DisplayName);
		Assert.IsEmpty(view.Challenges);
	}

	[TestMethod]
	public void Player_ChallengesListEveryDefinitionForTheWeapon()
	{
		ProgressionSettings settings = Settings();
		WeaponInfo ak = TestHelpers.Weapon("ak47");
		IReadOnlyList<ChallengeDefinition> definitions = new ChallengeCatalog(settings).For(ak);
		ChallengeDefinition kills = definitions.Single(d => d.Id == ChallengeDefinition.KillsId);
		ChallengeDefinition hits = definitions.Single(d => d.Id == ChallengeDefinition.HitsId);
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress weapon = player.Weapon(ak.Key);
		weapon.Kills = 1;
		weapon.Challenge(ChallengeDefinition.KillsId).Count = kills.Tiers[0];
		weapon.Challenge(ChallengeDefinition.HitsId).Count = hits.Tiers[^1] + 5;

		WeaponView view = PlayerViewBuilder.Build(player, settings, online: false).Weapons.Single();

		Assert.HasCount(definitions.Count, view.Challenges);
		ChallengeView killsView = view.Challenges.Single(c => c.Id == ChallengeDefinition.KillsId);
		Assert.AreEqual(1, killsView.Tier);
		Assert.AreEqual(kills.Tiers.Count, killsView.TierCount);
		Assert.AreEqual(kills.Tiers[1], killsView.NextTarget);
		ChallengeView hitsView = view.Challenges.Single(c => c.Id == ChallengeDefinition.HitsId);
		Assert.AreEqual(hits.Tiers.Count, hitsView.Tier);
		Assert.IsNull(hitsView.NextTarget);
		ChallengeView headshots = view.Challenges.Single(c => c.Id == ChallengeDefinition.HeadshotsId);
		Assert.AreEqual(0, headshots.Count);
		Assert.AreEqual(0, headshots.Tier);
		Assert.AreEqual(definitions.Single(d => d.Id == ChallengeDefinition.HeadshotsId).Tiers[0], headshots.NextTarget);
	}

	[TestMethod]
	public void Ranks_OverlayRaisesXpAndReorders()
	{
		const ulong a = 76561198000000010;
		const ulong b = 76561198000000011;
		List<RankingEntry> stored =
		[
			new(b, "Bee", 1200, 1, Seen),
			new(a, "Ay", 900, 0, Seen),
		];
		ProgressSnapshot live = new(new PlayerRow(a, "Ay", 1500, 1, Seen, Seen), [], []);

		IReadOnlyList<RankRow> rows = RanksViewBuilder.Build(stored, id => id == a ? live : null, id => id == a, Settings());

		Assert.HasCount(2, rows);
		Assert.AreEqual(1, rows[0].Position);
		Assert.AreEqual(a.ToString(CultureInfo.InvariantCulture), rows[0].SteamId);
		Assert.AreEqual(1500, rows[0].RankXp);
		Assert.IsTrue(rows[0].Online);
		Assert.AreEqual(2, rows[1].Position);
		Assert.AreEqual(1200, rows[1].RankXp);
		Assert.IsFalse(rows[1].Online);
	}

	[TestMethod]
	public void Ranks_OverlayOnlyPlayerIsAdded()
	{
		const ulong fresh = 76561198000000020;
		ProgressSnapshot live = new(new PlayerRow(fresh, "Fresh", 50, 0, Seen, Seen), [], []);

		IReadOnlyList<RankRow> rows = RanksViewBuilder.Build([], id => id == fresh ? live : null, _ => true, Settings(), [fresh]);

		Assert.HasCount(1, rows);
		Assert.AreEqual("Fresh", rows[0].Name);
		Assert.AreEqual(1, rows[0].Position);
	}

	[TestMethod]
	public void Ranks_TiesBreakBySteamIdAscending()
	{
		List<RankingEntry> stored =
		[
			new(76561198000000032, "Hi", 100, 0, Seen),
			new(76561198000000031, "Lo", 100, 0, Seen),
		];

		IReadOnlyList<RankRow> rows = RanksViewBuilder.Build(stored, _ => null, _ => false, Settings());

		Assert.AreEqual("Lo", rows[0].Name);
		Assert.AreEqual("Hi", rows[1].Name);
	}

	[TestMethod]
	public void Player_StoredDates_SerializeAsUtc()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.FirstSeen = Seen.AddDays(-3);
		stored.LastSeen = Seen;

		PlayerView view = PlayerViewBuilder.Build(ProgressMerge.Merge(stored, null), Settings(), online: false);
		string json = JsonSerializer.Serialize(view, SiteJson.Options);

		Assert.AreEqual(DateTimeKind.Utc, view.LastSeen.Kind);
		Assert.AreEqual(DateTimeKind.Utc, view.FirstSeen.Kind);
		Assert.Contains("\"lastSeen\":\"2026-10-17T12:00:00Z\"", json);
		Assert.Contains("\"firstSeen\":\"2026-10-14T12:00:00Z\"", json);
	}
}
