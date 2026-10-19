// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Maps;

using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;

/// <summary>
/// What a player has done on one map, added up over every session they played it.
/// </summary>
internal sealed class MapProgress
{
	/// <summary>Gets or sets the rank XP earned on the map.</summary>
	public long Xp { get; set; }

	/// <summary>Gets or sets kills on the map's scoreboard.</summary>
	public long Kills { get; set; }

	/// <summary>Gets or sets deaths on the map's scoreboard.</summary>
	public long Deaths { get; set; }

	/// <summary>Gets or sets the seconds played on the map.</summary>
	public long Seconds { get; set; }

	/// <summary>Gets or sets how many sessions on the map earned anything.</summary>
	public long Sessions { get; set; }

	/// <summary>Gets or sets a value indicating whether the row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }
}

/// <summary>A map row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Map">The map's name, as <see cref="MapStats.Normalize"/> gives it.</param>
/// <param name="Xp">The rank XP earned on the map.</param>
/// <param name="Kills">Kills on the map.</param>
/// <param name="Deaths">Deaths on the map.</param>
/// <param name="Seconds">Seconds played on the map.</param>
/// <param name="Sessions">Sessions on the map that earned anything.</param>
internal sealed record MapRow(ulong SteamId, string Map, long Xp, long Kills, long Deaths, long Seconds, long Sessions);

/// <summary>One row of a map's board.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The player's rank index.</param>
/// <param name="Xp">The rank XP they earned on the map.</param>
/// <param name="Kills">Their kills on the map.</param>
/// <param name="Deaths">Their deaths on the map.</param>
/// <param name="Seconds">The seconds they played on the map.</param>
internal sealed record MapStanding(ulong SteamId, string Name, int Rank, long Xp, long Kills, long Deaths, long Seconds);

/// <summary>One map the server has seen, with everyone's totals on it.</summary>
/// <param name="Map">The map's name.</param>
/// <param name="Players">How many players earned XP on it.</param>
/// <param name="Xp">The rank XP everyone earned on it.</param>
/// <param name="Seconds">The seconds everyone played on it.</param>
internal sealed record MapSummary(string Map, long Players, long Xp, long Seconds);

/// <summary>
/// Adds map recaps up into per-map totals, and picks a player's best map. Every finished recap lands here, so the
/// totals cover exactly the sessions the recap covers: from joining or the map starting until it ended or they left.
/// </summary>
internal static class MapStats
{
	/// <summary>The longest map name stored, which is the column's width.</summary>
	public const int MaxNameLength = 64;

	/// <summary>How long a player needs on a map before it can be their best, so one lucky session doesn't win.</summary>
	public static readonly TimeSpan BestMapMinimum = TimeSpan.FromMinutes(15);

	/// <summary>
	/// Turns the name the server reports into the one stored: lower case, without a workshop path, and no longer than the column.
	/// </summary>
	/// <param name="map">The name the server reports, such as <c>de_dust2</c> or <c>workshop/123456/aim_map</c>.</param>
	/// <returns>The stored name, or an empty string when there's no name.</returns>
	public static string Normalize(string? map)
	{
		string name = (map ?? string.Empty).Trim().Replace('\\', '/');
		int slash = name.LastIndexOf('/');
		if (slash >= 0)
		{
			name = name[(slash + 1)..];
		}

		name = name.ToLowerInvariant();
		return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
	}

	/// <summary>
	/// Adds a finished recap to the player's totals for its map.
	/// </summary>
	/// <param name="progress">The player's progress, or <see langword="null"/> while it hasn't loaded.</param>
	/// <param name="recap">The recap.</param>
	/// <returns><see langword="true"/> when the player's progress has loaded and the recap named a map, so it was added.</returns>
	public static bool Add(PlayerProgress? progress, MapRecap recap)
	{
		Ensure.NotNull(recap);

		string map = Normalize(recap.Map);
		if ((progress is null) || (map.Length == 0))
		{
			return false;
		}

		MapProgress totals = progress.Map(map);
		totals.Xp += Math.Max(0, recap.Xp);
		totals.Kills += Math.Max(0, recap.Kills);
		totals.Deaths += Math.Max(0, recap.Deaths);
		totals.Seconds += Math.Max(0, (long)(recap.EndedAt - recap.StartedAt).TotalSeconds);
		totals.Sessions++;
		totals.IsDirty = true;
		return true;
	}

	/// <summary>
	/// Gets the rank XP earned per hour on a map.
	/// </summary>
	/// <param name="xp">The rank XP earned.</param>
	/// <param name="seconds">The seconds played.</param>
	/// <returns>The XP per hour, or 0 when no time was played.</returns>
	public static long XpPerHour(long xp, long seconds) => seconds > 0 ? (long)Math.Round(xp * 3600.0 / seconds) : 0;

	/// <summary>
	/// Picks a player's best map: the one they earn the most XP per hour on, out of those they've played for at least
	/// <see cref="BestMapMinimum"/>. Ties go to the map with more XP, then the name.
	/// </summary>
	/// <param name="maps">The player's maps.</param>
	/// <returns>The best map's name, or <see langword="null"/> when no map has had enough play.</returns>
	public static string? Best(IReadOnlyDictionary<string, MapProgress> maps)
	{
		Ensure.NotNull(maps);
		return maps
			.Where(m => m.Value.Seconds >= BestMapMinimum.TotalSeconds && m.Value.Xp > 0)
			.OrderByDescending(m => XpPerHour(m.Value.Xp, m.Value.Seconds))
			.ThenByDescending(m => m.Value.Xp)
			.ThenBy(m => m.Key, StringComparer.Ordinal)
			.Select(m => m.Key)
			.FirstOrDefault();
	}
}
