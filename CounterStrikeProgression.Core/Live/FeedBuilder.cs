// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Turns the notices of one event into live feed entries.
/// </summary>
internal static class FeedBuilder
{
	/// <summary>
	/// Makes a feed entry for each notice in a result.
	/// </summary>
	/// <param name="result">What the event earned.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="playerName">The player's name.</param>
	/// <param name="at">When it happened, in UTC.</param>
	/// <returns>The entries, in the order the moments happened.</returns>
	public static IReadOnlyList<FeedEntry> From(ProgressionResult result, ulong steamId, string playerName, DateTime at)
	{
		Ensure.NotNull(result);
		Ensure.NotNull(playerName);

		List<FeedEntry> entries = [];
		foreach (ProgressionNotice notice in result.Notices)
		{
			(FeedKind kind, string? title) = notice.Kind switch
			{
				NoticeKind.SkillShot => (FeedKind.SkillShot, notice.SkillShot?.Name),
				NoticeKind.RankUp => (FeedKind.RankUp, $"Rank up: {RankLadder.Names[notice.Value]}"),
				NoticeKind.WeaponLevelUp => (FeedKind.WeaponLevelUp, $"{notice.Weapon?.DisplayName} level {notice.Value}"),
				NoticeKind.TierCompleted => (FeedKind.TierCompleted, $"{notice.ChallengeName} {PanelBuilder.Numeral(notice.Value)}"),
				NoticeKind.FeatEarned => (FeedKind.FeatEarned, notice.Feat?.Name),
				_ => (FeedKind.SkillShot, null),
			};

			if (title is not null)
			{
				entries.Add(new FeedEntry(at, steamId, playerName, kind, title, notice.Weapon?.DisplayName, notice.Xp));
			}
		}

		return entries;
	}
}
