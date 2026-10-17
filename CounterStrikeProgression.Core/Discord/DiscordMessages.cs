// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Discord;

using System.Globalization;
using System.Text;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>
/// Picks the moments worth posting to Discord and writes each as one line of Discord markdown.
/// </summary>
internal static class DiscordMessages
{
	// Gold Nova · I, the rank a name that matches nothing falls back to.
	private const int DefaultMinimumRank = 3 * RankLadder.StagesPerRank;

	// Discord reads these as markdown, so a player name keeps them only when they're escaped.
	private const string MarkdownCharacters = "\\*_~`|><[]()#-:";

	/// <summary>
	/// Makes a line for each notice in a result that the settings post.
	/// </summary>
	/// <param name="result">What the event earned.</param>
	/// <param name="playerName">The player's name.</param>
	/// <param name="settings">Which moments to post.</param>
	/// <returns>The lines, in the order the moments happened.</returns>
	public static IReadOnlyList<string> From(ProgressionResult result, string playerName, DiscordSettings settings)
	{
		Ensure.NotNull(result);
		Ensure.NotNull(playerName);
		Ensure.NotNull(settings);

		string name = $"**{Escape(playerName)}**";
		int minimumRank = MinimumRankIndex(settings.MinimumRank);
		List<string> lines = [];
		foreach (ProgressionNotice notice in result.Notices)
		{
			string? line = notice switch
			{
				{ Kind: NoticeKind.FeatEarned, Feat: { } feat } when settings.Feats =>
					$"🏅 {name} earned the feat **{feat.Name}**: {feat.Description}!",
				{ Kind: NoticeKind.RankUp, Value: int rank } when settings.RankUps && (rank >= minimumRank) && (rank < RankLadder.Names.Count) =>
					$"⬆️ {name} ranked up to **{RankLadder.Names[rank]}**!",
				{ Kind: NoticeKind.SkillShot, SkillShot: { Rarity: Rarity.Epic } shot } when settings.EpicSkillShots =>
					$"💥 {name} landed **{shot.Name}**{(notice.Weapon is { } weapon ? $" with the {weapon.DisplayName}" : "")} (+{notice.Xp.ToString("N0", CultureInfo.InvariantCulture)} XP)",
				_ => null,
			};

			if (line is not null)
			{
				lines.Add(line);
			}
		}

		return lines;
	}

	/// <summary>
	/// Finds the rank index a <see cref="DiscordSettings.MinimumRank"/> names.
	/// </summary>
	/// <param name="name">A rank such as <c>Gold Nova</c>, or one stage such as <c>Gold Nova · III</c>.</param>
	/// <returns>The index of the lowest stage it covers, or of Gold Nova · I when it names neither.</returns>
	public static int MinimumRankIndex(string? name)
	{
		string trimmed = name?.Trim() ?? string.Empty;
		for (int i = 0; i < RankLadder.Names.Count; i++)
		{
			if (string.Equals(RankLadder.Names[i], trimmed, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}

		for (int i = 0; i < RankLadder.RankNames.Count; i++)
		{
			if (string.Equals(RankLadder.RankNames[i], trimmed, StringComparison.OrdinalIgnoreCase))
			{
				return i * RankLadder.StagesPerRank;
			}
		}

		return DefaultMinimumRank;
	}

	/// <summary>
	/// Makes a player name safe to post: markdown is escaped and line breaks become spaces. Mentions are left to
	/// the sender, which tells Discord to ping no one.
	/// </summary>
	/// <param name="text">The name.</param>
	/// <returns>The escaped name.</returns>
	public static string Escape(string text)
	{
		Ensure.NotNull(text);
		StringBuilder escaped = new(text.Length + 8);
		foreach (char c in text)
		{
			if (char.IsControl(c))
			{
				escaped.Append(' ');
				continue;
			}

			if (MarkdownCharacters.Contains(c, StringComparison.Ordinal))
			{
				escaped.Append('\\');
			}

			escaped.Append(c);
		}

		return escaped.ToString();
	}
}
