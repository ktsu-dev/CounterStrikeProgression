// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
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

	[TestMethod]
	public void Suggest_ClosestToNextTierFirst_AtMostTwoPerWeapon_ThenOneNew()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 100;
		ak.Challenge(ChallengeDefinition.KillsId).Count = settings.KillTiers[0] - 5;
		ak.Challenge(ChallengeDefinition.HeadshotsId).Count = settings.HeadshotTiers[0] / 2;
		ak.Challenge(ChallengeDefinition.HitsId).Count = 1;

		IReadOnlyList<SuggestionView> suggestions = PlayerViewBuilder.Build(player, settings, online: false).Suggestions;

		Assert.HasCount(3, suggestions);
		Assert.AreEqual(ChallengeDefinition.KillsId, suggestions[0].ChallengeId);
		Assert.AreEqual(1, suggestions[0].NextTier);
		Assert.AreEqual(settings.KillTiers[0], suggestions[0].Target);
		Assert.AreEqual(settings.VolumeTierXp, suggestions[0].Xp);
		Assert.IsFalse(suggestions[0].IsNew);
		Assert.AreEqual(ChallengeDefinition.HeadshotsId, suggestions[1].ChallengeId);
		SuggestionView fresh = suggestions[2];
		Assert.IsTrue(fresh.IsNew);
		Assert.AreEqual("ak47", fresh.WeaponKey);
		Assert.AreEqual(0, fresh.Count);
		Assert.AreEqual(settings.SkillTierXp, fresh.Xp);
		Assert.IsNotNull(fresh.Description);
		Assert.AreEqual(Rarity.Common, SkillShotCatalog.All.Single(s => s.Id.WeakString == fresh.ChallengeId).Rarity);
	}

	[TestMethod]
	public void Suggest_ProgressIsMeasuredFromTheLastTier()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Challenge(ChallengeDefinition.KillsId).Count = 50;

		SuggestionView kills = PlayerViewBuilder.Build(player, settings, online: false).Suggestions[0];

		Assert.AreEqual(2, kills.NextTier);
		Assert.AreEqual(settings.KillTiers[1], kills.Target);
		Assert.AreEqual((50.0 - settings.KillTiers[0]) / (settings.KillTiers[1] - settings.KillTiers[0]), kills.Progress, 0.0001);
	}

	[TestMethod]
	public void Suggest_WeaponsUsedMoreRankHigher()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 1000;
		ak.Challenge(ChallengeDefinition.KillsId).Count = 15;
		WeaponProgress awp = player.Weapon(TestHelpers.Weapon("awp").Key);
		awp.Xp = 10;
		awp.Challenge(ChallengeDefinition.KillsId).Count = 20;

		IReadOnlyList<SuggestionView> suggestions = PlayerViewBuilder.Build(player, settings, online: false).Suggestions;

		Assert.AreEqual("ak47", suggestions[0].WeaponKey);
		Assert.AreEqual("awp", suggestions[1].WeaponKey);
	}

	[TestMethod]
	public void Suggest_FinishedChallengesAreLeftOut()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Challenge(ChallengeDefinition.KillsId).Count = settings.KillTiers[^1] + 10;
		ak.Challenge(ChallengeDefinition.HitsId).Count = 1;

		IReadOnlyList<SuggestionView> suggestions = PlayerViewBuilder.Build(player, settings, online: false).Suggestions;

		Assert.IsFalse(suggestions.Any(s => s.ChallengeId == ChallengeDefinition.KillsId));
		Assert.AreEqual(ChallengeDefinition.HitsId, suggestions[0].ChallengeId);
	}

	[TestMethod]
	public void Suggest_NoWeapons_SuggestsKillsWithTheFirstWeapon()
	{
		ProgressionSettings settings = Settings();

		SuggestionView only = Assert.ContainsSingle(PlayerViewBuilder.Build(TestHelpers.Player(), settings, online: false).Suggestions);

		Assert.IsTrue(only.IsNew);
		Assert.AreEqual(WeaponCatalog.All[0].Key.WeakString, only.WeaponKey);
		Assert.AreEqual(ChallengeDefinition.KillsId, only.ChallengeId);
		Assert.AreEqual(settings.KillTiers[0], only.Target);
		Assert.IsNull(only.Description);
	}

	[TestMethod]
	public void Player_FeedIsCaptioned()
	{
		FeedEntry entry = new(Seen, TestHelpers.SteamId, "Tester", FeedKind.SkillShot, "Wallbang", "AK-47", 25);

		PlayerView view = PlayerViewBuilder.Build(TestHelpers.Player(), Settings(), online: false, [entry]);
		string json = JsonSerializer.Serialize(view, SiteJson.Options);

		FeedItem item = Assert.ContainsSingle(view.Feed);
		Assert.AreEqual(entry.Title, item.Title);
		Assert.AreEqual(entry.At, item.At);
		Assert.AreEqual("Kill through a wall", item.Caption);
		Assert.Contains($"\"steamId\":\"{TestHelpers.SteamId}\"", json);
		Assert.Contains("\"kind\":\"SkillShot\"", json);
		Assert.Contains("\"caption\":\"Kill through a wall\"", json);
	}

	[TestMethod]
	public void Caption_SkillTier_DescribesTheShotAndHowManyTimes()
	{
		ProgressionSettings settings = Settings();
		SkillShotDefinition common = SkillShotCatalog.All.First(s => s.Rarity == Rarity.Common);
		SkillShotDefinition epic = SkillShotCatalog.All.First(s => s.Rarity == Rarity.Epic);

		FeedItem second = FeedCaptions.For(Tier($"{common.Name} II", "AK-47"), settings);
		FeedItem first = FeedCaptions.For(Tier($"{epic.Name} I", "AK-47"), settings);

		Assert.AreEqual($"{common.Description}, {settings.CommonSkillTiers[1]} times", second.Caption);
		Assert.AreEqual(settings.EpicSkillTiers[0] == 1 ? $"{epic.Description}, once" : $"{epic.Description}, {settings.EpicSkillTiers[0]} times", first.Caption);
	}

	[TestMethod]
	public void Caption_VolumeTier_GivesTheTotalAndWeapon()
	{
		ProgressionSettings settings = Settings();

		Assert.AreEqual($"{settings.KillTiers[2]} kills with the AK-47", FeedCaptions.For(Tier("Kills III", "AK-47"), settings).Caption);
		Assert.AreEqual($"{settings.HeadshotTiers[0]} headshot kills with the AWP", FeedCaptions.For(Tier("Headshots I", "AWP"), settings).Caption);
		Assert.AreEqual("1,000 hits", FeedCaptions.For(Tier("Hits III", null), new ProgressionSettings { HitTiers = [10, 100, 1000] }).Caption);
	}

	[TestMethod]
	public void Caption_TitlesThatSayItAllOrMatchNothing_HaveNone()
	{
		ProgressionSettings settings = Settings();

		Assert.IsNull(FeedCaptions.For(new FeedEntry(Seen, 1, "P", FeedKind.RankUp, "Rank up: Gold Nova I", null, 0), settings).Caption);
		Assert.IsNull(FeedCaptions.For(new FeedEntry(Seen, 1, "P", FeedKind.WeaponLevelUp, "AK-47 level 3", "AK-47", 60), settings).Caption);
		Assert.IsNull(FeedCaptions.For(new FeedEntry(Seen, 1, "P", FeedKind.SkillShot, "Renamed shot", null, 10), settings).Caption);
		Assert.IsNull(FeedCaptions.For(Tier("Kills", "AK-47"), settings).Caption);
		Assert.IsNull(FeedCaptions.For(Tier("Kills IX", "AK-47"), settings).Caption);
		Assert.IsNull(FeedCaptions.For(Tier("Mystery II", "AK-47"), settings).Caption);
	}

	private static FeedEntry Tier(string title, string? weapon) =>
		new(Seen, TestHelpers.SteamId, "Tester", FeedKind.TierCompleted, title, weapon, 100);
}
