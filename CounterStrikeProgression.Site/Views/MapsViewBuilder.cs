// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>One map in the board's picker.</summary>
/// <param name="Map">The map's name.</param>
/// <param name="Players">How many players earned XP on it.</param>
/// <param name="Xp">The rank XP everyone earned on it.</param>
/// <param name="Seconds">The seconds everyone played on it.</param>
internal sealed record MapChoiceView(string Map, long Players, long Xp, long Seconds);

/// <summary>One row of a map's board.</summary>
/// <param name="Position">The place on the map, from 1.</param>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The player's rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="Xp">The rank XP earned on the map.</param>
/// <param name="Kills">Kills on the map.</param>
/// <param name="Deaths">Deaths on the map.</param>
/// <param name="Seconds">Seconds played on the map.</param>
/// <param name="XpPerHour">The rank XP earned per hour on the map.</param>
/// <param name="Online">Whether the player is on the server now.</param>
internal sealed record MapRowView(int Position, string SteamId, string Name, int Rank, string RankName, long Xp, long Kills, long Deaths, long Seconds, long XpPerHour, bool Online);

/// <summary>The map board.</summary>
/// <param name="Map">The map shown, or <see langword="null"/> before anyone has earned XP on any map.</param>
/// <param name="Maps">Every map with XP earned on it, most played first.</param>
/// <param name="Rows">The shown map's board, most XP first.</param>
internal sealed record MapBoardView(string? Map, IReadOnlyList<MapChoiceView> Maps, IReadOnlyList<MapRowView> Rows);

/// <summary>A player's totals on one map.</summary>
/// <param name="Map">The map's name.</param>
/// <param name="Xp">The rank XP earned on it.</param>
/// <param name="Kills">Kills on it.</param>
/// <param name="Deaths">Deaths on it.</param>
/// <param name="Seconds">Seconds played on it.</param>
/// <param name="Sessions">Sessions on it that earned anything.</param>
/// <param name="XpPerHour">The rank XP earned per hour on it.</param>
internal sealed record PlayerMapView(string Map, long Xp, long Kills, long Deaths, long Seconds, long Sessions, long XpPerHour);

/// <summary>A player's maps.</summary>
/// <param name="Best">The player's best map, or <see langword="null"/> before any map has had enough play.</param>
/// <param name="MinimumMinutes">The minutes a map needs before it can be the best.</param>
/// <param name="Maps">The player's maps, most XP first.</param>
internal sealed record PlayerMapsView(string? Best, int MinimumMinutes, IReadOnlyList<PlayerMapView> Maps);

/// <summary>
/// Turns stored map totals into the map board and the player page's maps tab.
/// </summary>
internal static class MapsViewBuilder
{
	/// <summary>
	/// Picks the map the board shows: the one asked for when anyone has played it, else the most played.
	/// </summary>
	/// <param name="requested">The map in the URL, if any.</param>
	/// <param name="maps">Every map with XP earned on it, most played first.</param>
	/// <returns>The map's stored name, or <see langword="null"/> when no map has any XP.</returns>
	public static string? Pick(string? requested, IReadOnlyList<MapSummary> maps)
	{
		Ensure.NotNull(maps);
		string wanted = MapStats.Normalize(requested);
		return maps.FirstOrDefault(m => string.Equals(m.Map, wanted, StringComparison.Ordinal))?.Map ?? (maps.Count > 0 ? maps[0].Map : null);
	}

	/// <summary>
	/// Builds the map board.
	/// </summary>
	/// <param name="map">The map shown.</param>
	/// <param name="maps">Every map with XP earned on it, most played first.</param>
	/// <param name="standings">The shown map's board, read from the database.</param>
	/// <param name="online">Tells whether a player is on the server now.</param>
	/// <returns>The board.</returns>
	public static MapBoardView Build(string? map, IReadOnlyList<MapSummary> maps, IReadOnlyList<MapStanding> standings, Func<ulong, bool> online)
	{
		Ensure.NotNull(maps);
		Ensure.NotNull(standings);
		Ensure.NotNull(online);

		List<MapRowView> rows = [];
		foreach (MapStanding entry in standings)
		{
			int rank = Math.Clamp(entry.Rank, 0, RankLadder.TopRank);
			rows.Add(new(
				rows.Count + 1,
				entry.SteamId.ToString(CultureInfo.InvariantCulture),
				entry.Name,
				rank,
				RankLadder.Names[rank],
				entry.Xp,
				entry.Kills,
				entry.Deaths,
				entry.Seconds,
				MapStats.XpPerHour(entry.Xp, entry.Seconds),
				online(entry.SteamId)));
		}

		return new(map, [.. maps.Select(m => new MapChoiceView(m.Map, m.Players, m.Xp, m.Seconds))], rows);
	}

	/// <summary>
	/// Builds a player's maps.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The player's maps.</returns>
	public static PlayerMapsView BuildPlayer(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		List<PlayerMapView> maps =
		[
			.. progress.Maps
				.Where(m => m.Value.Sessions > 0)
				.OrderByDescending(m => m.Value.Xp)
				.ThenBy(m => m.Key, StringComparer.Ordinal)
				.Select(m => new PlayerMapView(m.Key, m.Value.Xp, m.Value.Kills, m.Value.Deaths, m.Value.Seconds, m.Value.Sessions, MapStats.XpPerHour(m.Value.Xp, m.Value.Seconds))),
		];

		return new(MapStats.Best(progress.Maps), (int)MapStats.BestMapMinimum.TotalMinutes, maps);
	}
}
