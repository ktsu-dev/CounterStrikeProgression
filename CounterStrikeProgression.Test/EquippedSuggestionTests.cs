// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class EquippedSuggestionTests
{
	private static ProgressionSettings Settings() => new() { FirstRankXp = 1000, RankGrowth = 1.25 };

	[TestMethod]
	public void Equipped_SuggestsOnlyTheHeldWeapon_ClosestFirst()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 100;
		ak.Challenge(ChallengeDefinition.KillsId).Count = settings.KillTiers[0] - 5;
		ak.Challenge(ChallengeDefinition.HeadshotsId).Count = settings.HeadshotTiers[0] / 2;
		ak.Challenge(ChallengeDefinition.HitsId).Count = 1;
		WeaponProgress awp = player.Weapon(TestHelpers.Weapon("awp").Key);
		awp.Xp = 500;
		awp.Challenge(ChallengeDefinition.KillsId).Count = settings.KillTiers[0] - 1;

		EquippedView? equipped = PlayerViewBuilder.Build(player, settings, online: true, equippedWeapon: "ak47").Equipped;

		Assert.IsNotNull(equipped);
		Assert.AreEqual("ak47", equipped.WeaponKey);
		Assert.AreEqual(TestHelpers.Weapon("ak47").DisplayName, equipped.WeaponName);
		Assert.HasCount(ChallengeSuggester.EquippedLimit, equipped.Suggestions);
		Assert.IsTrue(equipped.Suggestions.All(s => s.WeaponKey == "ak47"));
		Assert.AreEqual(ChallengeDefinition.KillsId, equipped.Suggestions[0].ChallengeId);
		Assert.AreEqual(ChallengeDefinition.HeadshotsId, equipped.Suggestions[1].ChallengeId);
		Assert.IsFalse(equipped.Suggestions.Any(s => s.IsNew));
	}

	[TestMethod]
	public void Equipped_FillsWithUnstartedChallenges_WhenFewAreStarted()
	{
		ProgressionSettings settings = Settings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 100;
		ak.Challenge(ChallengeDefinition.KillsId).Count = 5;

		EquippedView equipped = PlayerViewBuilder.Build(player, settings, online: true, equippedWeapon: "ak47").Equipped!;

		Assert.HasCount(ChallengeSuggester.EquippedLimit, equipped.Suggestions);
		Assert.AreEqual(ChallengeDefinition.KillsId, equipped.Suggestions[0].ChallengeId);
		Assert.IsFalse(equipped.Suggestions[0].IsNew);
		Assert.IsTrue(equipped.Suggestions.Skip(1).All(s => s.IsNew && (s.Count == 0) && (s.WeaponKey == "ak47")));
		Assert.AreEqual(equipped.Suggestions.Count, equipped.Suggestions.Select(s => s.ChallengeId).Distinct().Count());
	}

	[TestMethod]
	public void Equipped_AWeaponNeverUsed_StartsWithKills()
	{
		EquippedView equipped = PlayerViewBuilder.Build(TestHelpers.Player(), Settings(), online: true, equippedWeapon: "deagle").Equipped!;

		Assert.HasCount(ChallengeSuggester.EquippedLimit, equipped.Suggestions);
		Assert.AreEqual(ChallengeDefinition.KillsId, equipped.Suggestions[0].ChallengeId);
		Assert.IsTrue(equipped.Suggestions.All(s => s.IsNew));
	}

	[TestMethod]
	public void Equipped_IsNull_WithNothingTrackedInHand()
	{
		PlayerProgress player = TestHelpers.Player();

		Assert.IsNull(PlayerViewBuilder.Build(player, Settings(), online: true).Equipped);
		Assert.IsNull(PlayerViewBuilder.Build(player, Settings(), online: true, equippedWeapon: "c4").Equipped);
		Assert.IsNull(PlayerViewBuilder.Build(player, Settings(), online: true, equippedWeapon: " ").Equipped);
	}

	[TestMethod]
	public void Equipped_IsNull_WhileOffline() =>
		Assert.IsNull(PlayerViewBuilder.Build(TestHelpers.Player(), Settings(), online: false, equippedWeapon: "ak47").Equipped);
}
