// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Discord;

using System.Globalization;
using System.Text;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// The Monday post: last week's top climbers and rank-ups, and the weekly contract that starts now. It posts once a
/// week, guarded by a row in <c>csp_digests</c>, so a restart or a second map start in the week posts nothing.
/// </summary>
internal static class WeeklyDigest
{
	/// <summary>The most rank-ups listed by name, so a busy week still fits in one message.</summary>
	public const int MaxRankUps = 10;

	private const int MaxPlayers = 10;

	private static readonly string[] Medals = ["🥇", "🥈", "🥉"];

	/// <summary>
	/// Writes the digest for the week before a Monday.
	/// </summary>
	/// <param name="week">The Monday that starts the new week.</param>
	/// <param name="gains">Every player who earned rank XP last week, most first.</param>
	/// <param name="ranks">The rank ladder, which turns XP into ranks.</param>
	/// <param name="progression">The settings that pick the weekly contract and what it pays.</param>
	/// <param name="settings">How many climbers to list, and the lowest rank-up to name.</param>
	/// <returns>The post, or <see langword="null"/> when nobody earned anything last week.</returns>
	public static string? Build(DateOnly week, IReadOnlyList<PeriodGain> gains, RankLadder ranks, ProgressionSettings progression, DiscordSettings settings)
	{
		Ensure.NotNull(gains);
		Ensure.NotNull(ranks);
		Ensure.NotNull(progression);
		Ensure.NotNull(settings);

		if (gains.Count == 0)
		{
			return null;
		}

		StringBuilder post = new();
		post.Append("📅 **Weekly digest** for ").Append(Span(week.AddDays(-7), week.AddDays(-1)));

		post.Append("\n**Top climbers**");
		int players = Math.Clamp(settings.WeeklyDigestPlayers, 1, MaxPlayers);
		for (int i = 0; i < Math.Min(players, gains.Count); i++)
		{
			string place = i < Medals.Length ? Medals[i] : $"{i + 1}.";
			post.Append('\n').Append(place).Append(" **").Append(DiscordMessages.Escape(gains[i].Name)).Append("**: +")
				.Append(gains[i].Gained.ToString("N0", CultureInfo.InvariantCulture)).Append(" XP");
		}

		int minimumRank = DiscordMessages.MinimumRankIndex(settings.MinimumRank);
		List<(string Name, int Rank)> rankUps = [.. gains
			.Select(g => (g.Name, Before: ranks.RankFor(g.StartXp), After: ranks.RankFor(g.EndXp)))
			.Where(g => (g.After > g.Before) && (g.After >= minimumRank))
			.OrderByDescending(g => g.After)
			.Select(g => (g.Name, g.After))];
		if (rankUps.Count > 0)
		{
			post.Append("\n**Rank-ups**");
			foreach ((string name, int rank) in rankUps.Take(MaxRankUps))
			{
				post.Append("\n⬆️ **").Append(DiscordMessages.Escape(name)).Append("** reached **").Append(RankLadder.Names[rank]).Append("**");
			}

			if (rankUps.Count > MaxRankUps)
			{
				post.Append("\n…and ").Append((rankUps.Count - MaxRankUps).ToString(CultureInfo.InvariantCulture)).Append(" more");
			}
		}

		if (ContractCatalog.Active(week, progression).FirstOrDefault(c => c.Definition.Period == ContractPeriod.Weekly) is { } contract)
		{
			post.Append("\n**This week's contract**\n📜 ").Append(contract.Definition.Description)
				.Append(" (+").Append(contract.Xp.ToString("N0", CultureInfo.InvariantCulture)).Append(" XP)");
		}

		return post.ToString();
	}

	/// <summary>
	/// Posts this week's digest unless it was already posted. Run off the game thread.
	/// </summary>
	/// <param name="store">Where last week's XP is read and the posted week is recorded.</param>
	/// <param name="ranks">The rank ladder.</param>
	/// <param name="progression">The progression settings.</param>
	/// <param name="settings">The Discord settings.</param>
	/// <param name="send">Queues the post, returning whether it was queued.</param>
	/// <param name="utcNow">The time now, in UTC.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>True if this call posted the digest.</returns>
	public static async Task<bool> PostAsync(IProgressionStore store, RankLadder ranks, ProgressionSettings progression, DiscordSettings settings, Func<string, bool> send, DateTime utcNow, CancellationToken cancellationToken)
	{
		Ensure.NotNull(store);
		Ensure.NotNull(send);

		DateOnly week = DailyRotation.WeekStart(DailyRotation.Day(utcNow));
		IReadOnlyList<PeriodGain> gains = await store.GainsBetweenAsync(week.AddDays(-7), week, cancellationToken).ConfigureAwait(false);
		if (Build(week, gains, ranks, progression, settings) is not { } post)
		{
			return false;
		}

		// Claimed before it's queued, so a failure between the two loses one post rather than doubling it.
		return await store.ClaimDigestAsync(week, utcNow, cancellationToken).ConfigureAwait(false) && send(post);
	}

	private static string Span(DateOnly first, DateOnly last)
	{
		CultureInfo culture = CultureInfo.InvariantCulture;
		string end = last.ToString("d MMMM", culture);
		return first.Month == last.Month
			? $"{first.ToString("%d", culture)} to {end}"
			: $"{first.ToString("d MMMM", culture)} to {end}";
	}
}
