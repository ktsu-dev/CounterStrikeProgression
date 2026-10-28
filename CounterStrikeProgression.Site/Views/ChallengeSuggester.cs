// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Picks the challenges a player is closest to finishing a tier of, plus one they haven't started.
/// </summary>
internal static class ChallengeSuggester
{
	/// <summary>The most started challenges to suggest.</summary>
	public const int StartedLimit = 9;

	/// <summary>
	/// The most started challenges to take from one weapon before other weapons have had their turn, so one weapon
	/// can't crowd out the rest. Slots left over after that are filled from any weapon.
	/// </summary>
	public const int PerWeaponLimit = 2;

	/// <summary>The most challenges to suggest for the weapon a player is holding.</summary>
	public const int EquippedLimit = 3;

	/// <summary>
	/// Suggests the player's next challenges.
	/// </summary>
	/// <param name="player">The player's merged progress.</param>
	/// <param name="settings">The plugin's settings, for the XP a tier pays.</param>
	/// <param name="catalog">The challenge ladders built from <paramref name="settings"/>.</param>
	/// <param name="reroll">How many times the player has rerolled the unstarted pick. Each reroll moves to the next one.</param>
	/// <returns>
	/// Up to <see cref="StartedLimit"/> started challenges, closest to their next tier first, then one the
	/// player hasn't started when there is one.
	/// </returns>
	/// <remarks>
	/// Closeness is the share of the gap between the last tier and the next one already done, scaled by how
	/// much the player uses the weapon, so a weapon they barely touch counts half as much as their main one.
	/// </remarks>
	public static IReadOnlyList<SuggestionView> Suggest(PlayerProgress player, ProgressionSettings settings, ChallengeCatalog catalog, int reroll = 0)
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
		List<SuggestionView> skipped = [];
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
			else
			{
				skipped.Add(view);
			}
		}

		suggestions.AddRange(skipped.Take(StartedLimit - suggestions.Count));

		if (NotStarted(weapons, settings, catalog, reroll) is { } fresh)
		{
			suggestions.Add(fresh);
		}

		return suggestions;
	}

	/// <summary>
	/// Suggests challenges for one weapon, the one a player is holding.
	/// </summary>
	/// <param name="player">The player's merged progress.</param>
	/// <param name="weapon">The weapon.</param>
	/// <param name="settings">The plugin's settings, for the XP a tier pays.</param>
	/// <param name="catalog">The challenge ladders built from <paramref name="settings"/>.</param>
	/// <returns>
	/// Up to <see cref="EquippedLimit"/> of the weapon's challenges: started ones closest to their next tier first,
	/// then ones not started, Kills first and then skill challenges commonest first.
	/// </returns>
	public static IReadOnlyList<SuggestionView> ForWeapon(PlayerProgress player, WeaponInfo weapon, ProgressionSettings settings, ChallengeCatalog catalog)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(weapon);
		Ensure.NotNull(settings);
		Ensure.NotNull(catalog);

		WeaponProgress progress = player.Weapons.GetValueOrDefault(weapon.Key) ?? new(weapon.Key);
		List<ChallengeDefinition> ladders = [.. catalog.For(weapon)];
		IEnumerable<SuggestionView> started = ladders
			.Select(d => Build(weapon, d, progress, settings, isNew: false))
			.OfType<SuggestionView>()
			.Where(v => v.Count > 0)
			.OrderByDescending(v => v.Progress)
			.ThenBy(v => v.Target - v.Count)
			.ThenBy(v => v.ChallengeId, StringComparer.Ordinal);
		IEnumerable<SuggestionView> fresh = ladders.Where(d => d.Id == ChallengeDefinition.KillsId)
			.Concat(ladders.Where(d => d.Kind == ChallengeKind.Skill).OrderBy(d => d.Rarity))
			.Select(d => Build(weapon, d, progress, settings, isNew: true))
			.OfType<SuggestionView>()
			.Where(v => v.Count == 0);
		return [.. started.Concat(fresh).Take(EquippedLimit)];
	}

	/// <summary>
	/// Finds a challenge the player hasn't started. With no rerolls it's the most common unstarted skill challenge
	/// on the weapon they use most that has one, or else the first Kills tier with the first weapon they haven't used.
	/// </summary>
	/// <remarks>
	/// Rerolls walk the unstarted challenges a weapon at a time: the commonest unstarted one on each weapon they
	/// use, most used first, then the Kills tier on each weapon they haven't used, then the next commonest on each,
	/// and so on, wrapping round at the end. So a reroll changes the weapon as well as the challenge.
	/// </remarks>
	private static SuggestionView? NotStarted(List<(WeaponInfo Info, WeaponProgress Progress)> weapons, ProgressionSettings settings, ChallengeCatalog catalog, int reroll)
	{
		List<List<SuggestionView>> perWeapon = [];
		foreach ((WeaponInfo info, WeaponProgress progress) in weapons)
		{
			List<SuggestionView> skills =
			[
				.. catalog.For(info)
					.Where(d => d.Kind == ChallengeKind.Skill)
					.OrderBy(d => d.Rarity)
					.Select(d => Build(info, d, progress, settings, isNew: true))
					.OfType<SuggestionView>()
					.Where(v => v.Count == 0),
			];
			if (skills.Count > 0)
			{
				perWeapon.Add(skills);
			}
		}

		HashSet<string> used = new(weapons.Select(w => w.Info.Key.WeakString), StringComparer.Ordinal);
		foreach (WeaponInfo unused in WeaponCatalog.All.Where(w => !used.Contains(w.Key.WeakString)))
		{
			WeaponProgress fresh = new(unused.Key);
			List<ChallengeDefinition> ladders = [.. catalog.For(unused)];
			List<SuggestionView> picks =
			[
				.. ladders.Where(d => d.Id == ChallengeDefinition.KillsId)
					.Concat(ladders.Where(d => d.Kind == ChallengeKind.Skill).OrderBy(d => d.Rarity))
					.Select(d => Build(unused, d, fresh, settings, isNew: true))
					.OfType<SuggestionView>(),
			];
			if (picks.Count > 0)
			{
				perWeapon.Add(picks);
			}
		}

		List<SuggestionView> order = [];
		int longest = perWeapon.Count > 0 ? perWeapon.Max(p => p.Count) : 0;
		for (int round = 0; round < longest; round++)
		{
			order.AddRange(perWeapon.Where(p => round < p.Count).Select(p => p[round]));
		}

		return order.Count == 0 ? null : order[Math.Max(0, reroll) % order.Count];
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

		double fraction = PlayerViewBuilder.TierProgress(definition, tier, count);
		bool isSkill = definition.Kind == ChallengeKind.Skill;
		string? description = PlayerViewBuilder.Describe(definition);
		int xp = (isSkill ? settings.SkillTierXp : settings.VolumeTierXp) * (tier + 1);

		return new(info.Key.WeakString, info.DisplayName, definition.Id, definition.Name, description, tier + 1, count, target, fraction, xp, isNew);
	}
}
