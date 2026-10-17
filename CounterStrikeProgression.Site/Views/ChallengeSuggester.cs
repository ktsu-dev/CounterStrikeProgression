// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Picks the challenges a player is closest to finishing a tier of, plus one they haven't started.
/// </summary>
internal static class ChallengeSuggester
{
	/// <summary>The most started challenges to suggest.</summary>
	public const int StartedLimit = 4;

	/// <summary>The most started challenges to suggest for one weapon, so one weapon can't fill the list.</summary>
	public const int PerWeaponLimit = 2;

	/// <summary>
	/// Suggests the player's next challenges.
	/// </summary>
	/// <param name="player">The player's merged progress.</param>
	/// <param name="settings">The plugin's settings, for the XP a tier pays.</param>
	/// <param name="catalog">The challenge ladders built from <paramref name="settings"/>.</param>
	/// <returns>
	/// Up to <see cref="StartedLimit"/> started challenges, closest to their next tier first, then one the
	/// player hasn't started when there is one.
	/// </returns>
	/// <remarks>
	/// Closeness is the share of the gap between the last tier and the next one already done, scaled by how
	/// much the player uses the weapon, so a weapon they barely touch counts half as much as their main one.
	/// </remarks>
	public static IReadOnlyList<SuggestionView> Suggest(PlayerProgress player, ProgressionSettings settings, ChallengeCatalog catalog)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(settings);
		Ensure.NotNull(catalog);

		List<(WeaponInfo Info, WeaponProgress Progress)> weapons = [];
		foreach (WeaponProgress progress in player.Weapons.Values)
		{
			if (WeaponCatalog.Find(progress.Weapon) is { } info)
			{
				weapons.Add((info, progress));
			}
		}

		weapons.Sort((a, b) => b.Progress.Xp != a.Progress.Xp
			? b.Progress.Xp.CompareTo(a.Progress.Xp)
			: string.CompareOrdinal(a.Info.Key.WeakString, b.Info.Key.WeakString));

		double topXp = Math.Max(1, weapons.Count > 0 ? weapons[0].Progress.Xp : 0);
		List<(SuggestionView View, double Score)> started = [];
		foreach ((WeaponInfo info, WeaponProgress progress) in weapons)
		{
			double usage = 0.5 + (0.5 * Math.Clamp(progress.Xp / topXp, 0, 1));
			foreach (ChallengeDefinition definition in catalog.For(info))
			{
				if (Build(info, definition, progress, settings, isNew: false) is { Count: > 0 } view)
				{
					started.Add((view, view.Progress * usage));
				}
			}
		}

		List<SuggestionView> suggestions = [];
		Dictionary<string, int> perWeapon = new(StringComparer.Ordinal);
		IEnumerable<SuggestionView> closest = started
			.OrderByDescending(s => s.Score)
			.ThenBy(s => s.View.Target - s.View.Count)
			.ThenBy(s => s.View.WeaponKey, StringComparer.Ordinal)
			.ThenBy(s => s.View.ChallengeId, StringComparer.Ordinal)
			.Select(s => s.View);
		foreach (SuggestionView view in closest)
		{
			int count = perWeapon.GetValueOrDefault(view.WeaponKey);
			if (count < PerWeaponLimit)
			{
				perWeapon[view.WeaponKey] = count + 1;
				suggestions.Add(view);
				if (suggestions.Count == StartedLimit)
				{
					break;
				}
			}
		}

		if (NotStarted(weapons, settings, catalog) is { } fresh)
		{
			suggestions.Add(fresh);
		}

		return suggestions;
	}

	/// <summary>
	/// Finds a challenge the player hasn't started: the most common unstarted skill challenge on the weapon they
	/// use most that has one, or else the first Kills tier with the first weapon they haven't used.
	/// </summary>
	private static SuggestionView? NotStarted(List<(WeaponInfo Info, WeaponProgress Progress)> weapons, ProgressionSettings settings, ChallengeCatalog catalog)
	{
		foreach ((WeaponInfo info, WeaponProgress progress) in weapons)
		{
			SuggestionView? skill = catalog.For(info)
				.Where(d => d.Kind == ChallengeKind.Skill)
				.OrderBy(d => d.Rarity)
				.Select(d => Build(info, d, progress, settings, isNew: true))
				.FirstOrDefault(v => v is { Count: 0 });
			if (skill is not null)
			{
				return skill;
			}
		}

		HashSet<string> used = new(weapons.Select(w => w.Info.Key.WeakString), StringComparer.Ordinal);
		WeaponInfo? unused = WeaponCatalog.All.FirstOrDefault(w => !used.Contains(w.Key.WeakString));
		ChallengeDefinition? kills = unused is null ? null : catalog.Find(unused, ChallengeDefinition.KillsId);
		return unused is null || kills is null
			? null
			: Build(unused, kills, new WeaponProgress(unused.Key), settings, isNew: true);
	}

	private static SuggestionView? Build(WeaponInfo info, ChallengeDefinition definition, WeaponProgress progress, ProgressionSettings settings, bool isNew)
	{
		progress.Challenges.TryGetValue(definition.Id, out ChallengeProgress? challenge);
		long count = challenge?.Count ?? 0;
		int tier = Math.Min(definition.Tiers.Count, Math.Max(definition.TierFor(count), challenge?.TierReached ?? 0));
		if (definition.NextTarget(tier) is not { } target)
		{
			return null;
		}

		int previous = tier > 0 ? definition.Tiers[tier - 1] : 0;
		double fraction = target > previous ? Math.Clamp((count - previous) / (double)(target - previous), 0, 1) : 0;
		bool isSkill = definition.Kind == ChallengeKind.Skill;
		string? description = isSkill
			? SkillShotCatalog.All.FirstOrDefault(s => string.Equals(s.Id.WeakString, definition.Id, StringComparison.Ordinal))?.Description
			: null;
		int xp = isSkill ? settings.SkillTierXp : settings.VolumeTierXp;

		return new(info.Key.WeakString, info.DisplayName, definition.Id, definition.Name, description, tier + 1, count, target, fraction, xp, isNew);
	}
}
