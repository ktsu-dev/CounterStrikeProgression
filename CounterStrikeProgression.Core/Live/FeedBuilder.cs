// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Records;

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
				NoticeKind.ContractCompleted => (FeedKind.ContractCompleted, notice.Contract is { } contract ? ContractTitle(contract) : null),
				NoticeKind.RecordBroken => (FeedKind.RecordBroken, notice.Record is { } record ? RecordTitle(record) : null),
				_ => (FeedKind.SkillShot, null),
			};

			if (title is not null)
			{
				string? detail = notice.Record is { } broken ? RecordDetail(broken) : null;
				entries.Add(new FeedEntry(at, steamId, playerName, kind, title, notice.Weapon?.DisplayName, notice.Xp, detail));
			}
		}

		return entries;
	}

	/// <summary>
	/// Gets the feed title of a finished contract, such as <c>Daily contract: Get 20 kills with SMGs</c>.
	/// </summary>
	/// <param name="contract">The contract.</param>
	/// <returns>The title.</returns>
	public static string ContractTitle(ActiveContract contract)
	{
		Ensure.NotNull(contract);
		string period = contract.Definition.Period == ContractPeriod.Weekly ? "Weekly" : "Daily";
		return $"{period} contract: {contract.Definition.Description}";
	}

	/// <summary>
	/// Gets the feed title of a record, such as <c>Record: Longest rifle kill, 52.1 m</c>.
	/// </summary>
	/// <param name="record">The record set or broken.</param>
	/// <returns>The title.</returns>
	public static string RecordTitle(RecordBreak record)
	{
		Ensure.NotNull(record);
		return $"Record: {record.Definition.Name}, {record.Definition.Format(record.Holder.Value)}";
	}

	/// <summary>
	/// Says whose record was beaten, such as <c>Beat Alice's 48.0 m</c>.
	/// </summary>
	/// <param name="record">The record set or broken.</param>
	/// <returns>The text, which says it's the first for a record nobody held.</returns>
	public static string RecordDetail(RecordBreak record)
	{
		Ensure.NotNull(record);
		RecordRow holder = record.Holder;
		if (holder.PreviousValue is not { } previous)
		{
			return "The first to set it";
		}

		string value = record.Definition.Format(previous);
		return record.BeatOwn ? $"Beat their own {value}" : $"Beat {holder.PreviousName}'s {value}";
	}
}
