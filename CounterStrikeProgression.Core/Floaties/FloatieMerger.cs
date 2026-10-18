// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Floaties;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// What an XP floatie should show after a gain.
/// </summary>
/// <param name="Key">Which floatie it is: the target's slot, or <see cref="FloatieMerger.SelfKey"/> for gains with no target.</param>
/// <param name="IsNew">Whether a new floatie starts, rather than an existing one growing.</param>
/// <param name="Text">The text to show.</param>
/// <param name="Highlight">Whether it includes a skill shot, tier, level, or rank-up, which draws it in the highlight color.</param>
internal sealed record FloatieUpdate(int Key, bool IsNew, string Text, bool Highlight);

/// <summary>
/// Merges one player's XP gains into floaties. Gains on the same target close together add up in one
/// floatie, so a spray reads "+14 XP" instead of fourteen "+1 XP".
/// </summary>
/// <param name="mergeSeconds">How long after a gain the next one on the same target still adds to it.</param>
internal sealed class FloatieMerger(double mergeSeconds)
{
	/// <summary>The key for gains with no target, such as reloads and accuracy bonuses.</summary>
	public const int SelfKey = -1;

	private const int MaxLabels = 2;

	private readonly Dictionary<int, Entry> entries = [];

	/// <summary>
	/// Adds a gain.
	/// </summary>
	/// <param name="key">The target's slot, or <see cref="SelfKey"/>.</param>
	/// <param name="time">The game time in seconds.</param>
	/// <param name="result">What the event earned.</param>
	/// <returns>What to show, or <see langword="null"/> when the event earned no XP.</returns>
	public FloatieUpdate? Add(int key, double time, ProgressionResult result)
	{
		Ensure.NotNull(result);
		if (result.XpEarned <= 0)
		{
			return null;
		}

		bool isNew = !entries.TryGetValue(key, out Entry? entry) || ((time - entry.LastTime) > mergeSeconds);
		if (isNew || (entry is null))
		{
			entry = new Entry();
			entries[key] = entry;
		}

		entry.LastTime = time;
		entry.Xp += result.XpEarned;
		foreach (string label in Labels(result))
		{
			// The newest moment goes on top, and repeats aren't stacked.
			entry.Labels.Remove(label);
			entry.Labels.Insert(0, label);
		}

		if (entry.Labels.Count > MaxLabels)
		{
			entry.Labels.RemoveRange(MaxLabels, entry.Labels.Count - MaxLabels);
		}

		string text = entry.Labels.Count == 0
			? $"+{FormatXp(entry.Xp)} XP"
			: $"+{FormatXp(entry.Xp)} XP\n{string.Join("\n", entry.Labels)}";
		return new FloatieUpdate(key, isNew, text, entry.Labels.Count > 0);
	}

	/// <summary>
	/// Forgets floaties too old to grow, so the next gain on that target starts a new one.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	public void Expire(double time)
	{
		foreach (int key in entries.Where(e => (time - e.Value.LastTime) > mergeSeconds).Select(e => e.Key).ToList())
		{
			entries.Remove(key);
		}
	}

	/// <summary>
	/// Writes an XP amount for a floatie: whole numbers with separators, and up to two decimals below 10 so a
	/// quarter point against a bot still shows.
	/// </summary>
	/// <param name="xp">The XP.</param>
	/// <returns>The amount as text.</returns>
	public static string FormatXp(double xp)
	{
		double rounded = Math.Round(xp, 2);
		return (rounded < 10) && (rounded != Math.Floor(rounded))
			? rounded.ToString("0.##", CultureInfo.InvariantCulture)
			: Math.Round(xp).ToString("N0", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Gets the moments in a result worth naming on a floatie.
	/// </summary>
	/// <param name="result">What the event earned.</param>
	/// <returns>Short labels, in the order they happened.</returns>
	public static IEnumerable<string> Labels(ProgressionResult result)
	{
		Ensure.NotNull(result);
		foreach (ProgressionNotice notice in result.Notices)
		{
			string? label = notice.Kind switch
			{
				NoticeKind.SkillShot => notice.SkillShot?.Name,
				NoticeKind.TierCompleted => $"{notice.ChallengeName} {PanelBuilder.Numeral(notice.Value)}",
				NoticeKind.WeaponLevelUp => $"{notice.Weapon?.DisplayName} level {notice.Value}",
				NoticeKind.RankUp => $"Rank up: {RankLadder.Names[notice.Value]}",
				NoticeKind.FeatEarned => notice.Feat is null ? null : $"Feat: {notice.Feat.Name}",
				NoticeKind.BountyCollected => "Bounty",
				_ => null,
			};

			if (label is not null)
			{
				yield return label;
			}
		}
	}

	private sealed class Entry
	{
		public double LastTime { get; set; }

		public double Xp { get; set; }

		public List<string> Labels { get; } = [];
	}
}
