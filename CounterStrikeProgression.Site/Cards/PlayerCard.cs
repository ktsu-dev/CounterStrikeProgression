// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Cards;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Site.Views;

/// <summary>One weapon on a share card.</summary>
/// <param name="Name">The weapon's display name.</param>
/// <param name="Level">The weapon level.</param>
/// <param name="MaxLevel">The top weapon level.</param>
/// <param name="Kills">The player's kills with it.</param>
internal sealed record CardWeapon(string Name, int Level, int MaxLevel, long Kills);

/// <summary>What a player's share card shows.</summary>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="RankXp">The player's rank XP.</param>
/// <param name="RankProgress">How far the player is to the next rank, from 0 to 1.</param>
/// <param name="Weapons">The player's top weapons by XP, at most <see cref="PlayerCard.TopWeapons"/>.</param>
/// <param name="Season">The current season's name.</param>
/// <param name="SeasonPlace">The player's place this season, or <see langword="null"/> with no season XP.</param>
/// <param name="SeasonPlayers">How many players have season XP.</param>
internal sealed record PlayerCard(
	string SteamId,
	string Name,
	int Rank,
	string RankName,
	long RankXp,
	double RankProgress,
	IReadOnlyList<CardWeapon> Weapons,
	string Season,
	long? SeasonPlace,
	long SeasonPlayers)
{
	/// <summary>How many weapons a card shows.</summary>
	public const int TopWeapons = 3;

	/// <summary>The name a card shows when the player has none.</summary>
	public const string UnnamedPlayer = "Unnamed player";

	/// <summary>
	/// Builds a card from the player page and the season badges.
	/// </summary>
	/// <param name="player">The player page.</param>
	/// <param name="seasons">The player's season badges.</param>
	/// <returns>The card.</returns>
	public static PlayerCard From(PlayerView player, PlayerSeasonsView seasons)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(seasons);

		string name = string.IsNullOrWhiteSpace(player.Name) ? UnnamedPlayer : player.Name.Trim();
		double progress = player.NextRankName is null ? 1 : player.RankProgress;
		return new(
			player.SteamId,
			name,
			player.Rank,
			player.RankName,
			player.RankXp,
			progress,
			[.. player.Weapons.Take(TopWeapons).Select(w => new CardWeapon(w.DisplayName, w.Level, w.MaxLevel, w.Kills))],
			seasons.Season,
			seasons.Place,
			seasons.Players);
	}

	/// <summary>The suffix a bot persona's name gets where only text fits, such as a page title.</summary>
	public const string BotSuffix = " [BOT]";

	/// <summary>Gets whether the card is a bot persona's, from its id.</summary>
	public bool IsBot => ulong.TryParse(SteamId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && BotIds.IsBot(id);

	/// <summary>Gets the name as text shows it: with <see cref="BotSuffix"/> for a bot persona.</summary>
	public string Label => IsBot ? Name + BotSuffix : Name;

	/// <summary>Gets the link preview's title.</summary>
	public string Title => $"{Label} · {RankName}";

	/// <summary>Gets the link preview's description, such as <c>12,345 XP · 3rd of 40 in Oct–Dec 2026 · Best weapons: AK-47 12, AWP 9</c>.</summary>
	public string Description
	{
		get
		{
			List<string> parts = [$"{FormatNumber(RankXp)} XP"];
			if (SeasonText() is { } season)
			{
				parts.Add(season);
			}

			if (Weapons.Count > 0)
			{
				parts.Add("Best weapons: " + string.Join(", ", Weapons.Select(w => $"{w.Name} {w.Level.ToString(CultureInfo.InvariantCulture)}")));
			}

			return string.Join(" · ", parts);
		}
	}

	/// <summary>
	/// Describes the player's season place, such as <c>3rd of 40 in Oct–Dec 2026</c>.
	/// </summary>
	/// <returns>The text, or <see langword="null"/> with no season XP.</returns>
	public string? SeasonText() =>
		SeasonPlace is { } place ? $"{Ordinal(place)} of {FormatNumber(Math.Max(place, SeasonPlayers))} in {Season}" : null;

	/// <summary>
	/// Writes a number with thousands separators, as the site does.
	/// </summary>
	/// <param name="value">The number.</param>
	/// <returns>The text, such as <c>12,345</c>.</returns>
	public static string FormatNumber(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

	/// <summary>
	/// Writes a place as an English ordinal.
	/// </summary>
	/// <param name="place">The place, from 1.</param>
	/// <returns>The text, such as <c>1st</c>, <c>12th</c> or <c>23rd</c>.</returns>
	public static string Ordinal(long place)
	{
		string suffix = (place % 100) is 11 or 12 or 13 ? "th" : (place % 10) switch
		{
			1 => "st",
			2 => "nd",
			3 => "rd",
			_ => "th",
		};
		return FormatNumber(place) + suffix;
	}
}
