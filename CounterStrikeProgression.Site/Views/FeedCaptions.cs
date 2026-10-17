// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>
/// Adds a caption to feed entries that says how the moment was earned.
/// </summary>
/// <remarks>
/// Feed entries carry only their title, so the caption is found from it: a skill shot's title is its name, and
/// a tier's title is the challenge name followed by the tier numeral. Entries saved before a skill shot was
/// renamed or removed get no caption.
/// </remarks>
internal static class FeedCaptions
{
	private const int MaxTier = 10;

	private static readonly Dictionary<string, SkillShotDefinition> SkillShotsByName =
		SkillShotCatalog.All.ToDictionary(s => s.Name, StringComparer.Ordinal);

	/// <summary>
	/// Captions a list of entries.
	/// </summary>
	/// <param name="entries">The entries, in the order to keep.</param>
	/// <param name="settings">The plugin's settings, for the tier targets.</param>
	/// <returns>The captioned entries.</returns>
	public static IReadOnlyList<FeedItem> For(IEnumerable<FeedEntry> entries, ProgressionSettings settings)
	{
		Ensure.NotNull(entries);
		return [.. entries.Select(e => For(e, settings))];
	}

	/// <summary>
	/// Captions one entry.
	/// </summary>
	/// <param name="entry">The entry.</param>
	/// <param name="settings">The plugin's settings, for the tier targets.</param>
	/// <returns>The entry with its caption, which is <see langword="null"/> for rank ups, weapon level ups, and titles that match nothing.</returns>
	public static FeedItem For(FeedEntry entry, ProgressionSettings settings)
	{
		Ensure.NotNull(entry);
		Ensure.NotNull(settings);

		string? caption = entry.Kind switch
		{
			FeedKind.SkillShot => SkillShotsByName.GetValueOrDefault(entry.Title)?.Description,
			FeedKind.TierCompleted => TierCaption(entry, settings),
			FeedKind.FeatEarned => FeatCatalog.FindByName(entry.Title)?.Description,
			_ => null,
		};

		string? challengeId = entry.Kind switch
		{
			FeedKind.SkillShot => SkillShotsByName.GetValueOrDefault(entry.Title)?.Id.WeakString,
			FeedKind.TierCompleted => TierChallengeId(entry.Title),
			_ => null,
		};

		return new(entry.At, entry.SteamId, entry.PlayerName, entry.Kind, entry.Title, entry.Weapon, entry.Xp, caption, challengeId);
	}

	private static string? TierChallengeId(string title)
	{
		int space = title.LastIndexOf(' ');
		string name = space > 0 ? title[..space] : title;
		return SkillShotsByName.TryGetValue(name, out SkillShotDefinition? skillShot)
			? skillShot.Id.WeakString
			: name switch
			{
				"Kills" => ChallengeDefinition.KillsId,
				"Headshots" => ChallengeDefinition.HeadshotsId,
				"Hits" => ChallengeDefinition.HitsId,
				"Chickens" => ChallengeDefinition.ChickensId,
				_ => null,
			};
	}

	private static string? TierCaption(FeedEntry entry, ProgressionSettings settings)
	{
		int space = entry.Title.LastIndexOf(' ');
		if (space <= 0)
		{
			return null;
		}

		string name = entry.Title[..space];
		string numeral = entry.Title[(space + 1)..];
		int tier = Enumerable.Range(1, MaxTier).FirstOrDefault(t => string.Equals(PanelBuilder.Numeral(t), numeral, StringComparison.Ordinal));
		if (tier == 0)
		{
			return null;
		}

		if (SkillShotsByName.TryGetValue(name, out SkillShotDefinition? skillShot))
		{
			int? times = Target(skillShot.Rarity switch
			{
				Rarity.Epic => settings.EpicSkillTiers,
				Rarity.Rare => settings.RareSkillTiers,
				_ => settings.CommonSkillTiers,
			}, tier);
			return times is { } count ? $"{skillShot.Description}, {Times(count)}" : skillShot.Description;
		}

		(IReadOnlyList<int>? tiers, string? noun) = name switch
		{
			"Kills" => (settings.KillTiers, "kills"),
			"Headshots" => (settings.HeadshotTiers, "headshot kills"),
			"Hits" => (settings.HitTiers, "hits"),
			"Chickens" => (settings.ChickenTiers, "chickens"),
			_ => (null, null),
		};

		if (tiers is null || Target(tiers, tier) is not { } target)
		{
			return null;
		}

		string total = $"{target.ToString("N0", CultureInfo.InvariantCulture)} {noun}";
		return entry.Weapon is null ? total : $"{total} with the {entry.Weapon}";
	}

	private static int? Target(IReadOnlyList<int> tiers, int tier) => tier <= tiers.Count ? tiers[tier - 1] : null;

	private static string Times(int count) => count == 1 ? "once" : $"{count.ToString(CultureInfo.InvariantCulture)} times";
}
