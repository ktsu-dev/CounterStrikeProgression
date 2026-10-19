// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>One player's totals on the compare page.</summary>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="RankXp">The player's rank XP.</param>
/// <param name="Online">Whether the player is on the server now.</param>
/// <param name="LastSeen">When the player was last seen, in UTC.</param>
/// <param name="SeasonXp">The rank XP the player has earned this season.</param>
/// <param name="SeasonPlace">The player's place this season, or <see langword="null"/> before they've earned any.</param>
/// <param name="Kills">Kills with every weapon.</param>
/// <param name="HeadshotPercent">Headshot kills as a percentage of kills, from 0 to 100.</param>
/// <param name="Accuracy">Hits as a percentage of shots, from 0 to 100.</param>
/// <param name="WeaponLevels">Every weapon's level added up.</param>
/// <param name="Tiers">The ladder tiers finished on every weapon.</param>
/// <param name="SkillShots">Skill shots landed, every kind on every weapon.</param>
/// <param name="Feats">The feats earned.</param>
internal sealed record CompareSide(
	string SteamId,
	string Name,
	int Rank,
	string RankName,
	long RankXp,
	bool Online,
	DateTime LastSeen,
	long SeasonXp,
	long? SeasonPlace,
	long Kills,
	double HeadshotPercent,
	double Accuracy,
	int WeaponLevels,
	int Tiers,
	long SkillShots,
	int Feats);

/// <summary>One player's numbers for one weapon on the compare page.</summary>
/// <param name="Level">The weapon level.</param>
/// <param name="LevelProgress">How far the weapon is to the next level, from 0 to 1.</param>
/// <param name="Kills">Kills with the weapon.</param>
/// <param name="HeadshotPercent">Headshot kills as a percentage of kills, from 0 to 100.</param>
/// <param name="Accuracy">Hits as a percentage of shots, from 0 to 100.</param>
internal sealed record CompareWeaponSide(int Level, double LevelProgress, long Kills, double HeadshotPercent, double Accuracy);

/// <summary>One weapon on the compare page, with each player's numbers.</summary>
/// <param name="Key">The weapon key.</param>
/// <param name="DisplayName">The name players see.</param>
/// <param name="MaxLevel">The highest level a weapon can reach.</param>
/// <param name="A">The first player's numbers, or <see langword="null"/> when they haven't used it.</param>
/// <param name="B">The second player's numbers, or <see langword="null"/> when they haven't used it.</param>
internal sealed record CompareWeaponRow(string Key, string DisplayName, int MaxLevel, CompareWeaponSide? A, CompareWeaponSide? B);

/// <summary>One skill shot on the compare page, with how many times each player has landed it.</summary>
/// <param name="Id">The skill shot id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Rarity">How hard it is.</param>
/// <param name="A">The first player's count across every weapon.</param>
/// <param name="B">The second player's count across every weapon.</param>
internal sealed record CompareSkillShotRow(string Id, string Name, Rarity Rarity, long A, long B);

/// <summary>Two players side by side.</summary>
/// <param name="Season">The current season's name.</param>
/// <param name="A">The first player.</param>
/// <param name="B">The second player.</param>
/// <param name="Weapons">Every weapon either player has used, highest level first.</param>
/// <param name="SkillShots">Every skill shot either player has landed, rarest first, then in catalog order.</param>
internal sealed record CompareView(
	string Season,
	CompareSide A,
	CompareSide B,
	IReadOnlyList<CompareWeaponRow> Weapons,
	IReadOnlyList<CompareSkillShotRow> SkillShots);

/// <summary>
/// Builds the compare page from two players' merged progress. Ranks, levels, and tiers come from
/// <see cref="PlayerViewBuilder"/>, so the page agrees with each player's own page.
/// </summary>
internal static class CompareViewBuilder
{
	/// <summary>
	/// Builds the compare page model.
	/// </summary>
	/// <param name="a">The first player's stored and live progress, merged.</param>
	/// <param name="aOnline">Whether the first player is on the server now.</param>
	/// <param name="aSeason">The first player's place this season.</param>
	/// <param name="b">The second player's stored and live progress, merged.</param>
	/// <param name="bOnline">Whether the second player is on the server now.</param>
	/// <param name="bSeason">The second player's place this season.</param>
	/// <param name="settings">The plugin's settings.</param>
	/// <returns>The page model.</returns>
	public static CompareView Build(PlayerProgress a, bool aOnline, PlayerSeasonsView aSeason, PlayerProgress b, bool bOnline, PlayerSeasonsView bSeason, ProgressionSettings settings)
	{
		Ensure.NotNull(a);
		Ensure.NotNull(aSeason);
		Ensure.NotNull(b);
		Ensure.NotNull(bSeason);
		Ensure.NotNull(settings);

		PlayerView aView = PlayerViewBuilder.Build(a, settings, aOnline);
		PlayerView bView = PlayerViewBuilder.Build(b, settings, bOnline);
		Dictionary<string, long> aShots = SkillShotCounts(aView);
		Dictionary<string, long> bShots = SkillShotCounts(bView);

		return new(
			aSeason.Season,
			Side(a, aView, aSeason, aShots),
			Side(b, bView, bSeason, bShots),
			Weapons(aView, bView),
			[
				.. SkillShotCatalog.All
					.Select(s => new CompareSkillShotRow(s.Id.WeakString, s.Name, s.Rarity, aShots.GetValueOrDefault(s.Id.WeakString), bShots.GetValueOrDefault(s.Id.WeakString)))
					.Where(r => (r.A > 0) || (r.B > 0))
					.OrderByDescending(r => r.Rarity),
			]);
	}

	private static CompareSide Side(PlayerProgress progress, PlayerView view, PlayerSeasonsView season, Dictionary<string, long> skillShots)
	{
		long kills = progress.Weapons.Values.Sum(w => w.Kills);
		long headshots = progress.Weapons.Values.Sum(w => w.Headshots);
		long hits = progress.Weapons.Values.Sum(w => w.Hits);
		long shots = progress.Weapons.Values.Sum(w => w.Shots);
		return new(
			view.SteamId,
			view.Name,
			view.Rank,
			view.RankName,
			view.RankXp,
			view.Online,
			view.LastSeen,
			season.Xp,
			season.Place,
			kills,
			Percent(headshots, kills),
			Percent(hits, shots),
			view.Weapons.Sum(w => w.Level),
			view.Weapons.Sum(w => w.Challenges.Sum(c => c.Tier)),
			skillShots.Values.Sum(),
			view.Feats.Count(f => f.Earned));
	}

	private static List<CompareWeaponRow> Weapons(PlayerView a, PlayerView b)
	{
		Dictionary<string, WeaponView> aWeapons = a.Weapons.ToDictionary(w => w.Key, StringComparer.Ordinal);
		Dictionary<string, WeaponView> bWeapons = b.Weapons.ToDictionary(w => w.Key, StringComparer.Ordinal);
		return
		[
			.. aWeapons.Keys.Union(bWeapons.Keys, StringComparer.Ordinal)
				.Select(key =>
				{
					WeaponView? aWeapon = aWeapons.GetValueOrDefault(key);
					WeaponView? bWeapon = bWeapons.GetValueOrDefault(key);
					WeaponView either = aWeapon ?? bWeapons[key];
					return new CompareWeaponRow(key, either.DisplayName, either.MaxLevel, WeaponSide(aWeapon), WeaponSide(bWeapon));
				})
				.OrderByDescending(r => Math.Max(r.A?.Level ?? 0, r.B?.Level ?? 0))
				.ThenByDescending(r => (r.A?.Kills ?? 0) + (r.B?.Kills ?? 0))
				.ThenBy(r => r.Key, StringComparer.Ordinal),
		];
	}

	private static CompareWeaponSide? WeaponSide(WeaponView? weapon) =>
		weapon is null ? null : new(weapon.Level, weapon.LevelProgress, weapon.Kills, weapon.HeadshotPercent, weapon.Accuracy);

	private static Dictionary<string, long> SkillShotCounts(PlayerView view)
	{
		Dictionary<string, long> counts = new(StringComparer.Ordinal);
		foreach (ChallengeView challenge in view.Weapons.SelectMany(w => w.Challenges))
		{
			if ((challenge.Kind == ChallengeKind.Skill) && (challenge.Count > 0))
			{
				counts[challenge.Id] = counts.GetValueOrDefault(challenge.Id) + challenge.Count;
			}
		}

		return counts;
	}

	private static double Percent(long part, long whole) =>
		whole <= 0 ? 0 : Math.Clamp(Math.Round(part * 100.0 / whole, 1, MidpointRounding.AwayFromZero), 0, 100);
}
