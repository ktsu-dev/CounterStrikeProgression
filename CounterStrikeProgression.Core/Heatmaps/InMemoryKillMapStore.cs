// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

using ktsu.Extensions;

/// <summary>
/// Keeps kill positions in memory. Used by the tests, and when no database is configured.
/// </summary>
internal sealed class InMemoryKillMapStore : IKillMapStore
{
	private readonly Lock gate = new();
	private readonly Dictionary<string, List<KillPoint>> maps = new(StringComparer.Ordinal);
	private readonly Dictionary<ulong, string> lastMaps = [];

	/// <inheritdoc/>
	public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <inheritdoc/>
	public Task AddAsync(IReadOnlyList<KillPoint> points, int maxPerMap, CancellationToken cancellationToken)
	{
		Ensure.NotNull(points);
		lock (gate)
		{
			foreach (KillPoint point in points)
			{
				List<KillPoint> kept = maps.GetOrCreate(point.Map);
				kept.Add(point);
				lastMaps[point.KillerId] = point.Map;
				lastMaps[point.VictimId] = point.Map;
			}

			foreach (string map in points.Select(p => p.Map).Distinct())
			{
				List<KillPoint> kept = maps[map];
				if (kept.Count > maxPerMap)
				{
					kept.RemoveRange(0, kept.Count - maxPerMap);
				}
			}
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<MapPointCount>> MapsAsync(CancellationToken cancellationToken)
	{
		lock (gate)
		{
			IReadOnlyList<MapPointCount> counts = [.. maps
				.Where(m => m.Value.Count > 0)
				.Select(m => new MapPointCount(m.Key, m.Value.Count))
				.OrderByDescending(m => m.Points)
				.ThenBy(m => m.Map, StringComparer.Ordinal)];
			return Task.FromResult(counts);
		}
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<HeatPoint>> PointsAsync(string map, HeatmapKind kind, ulong? steamId, int limit, CancellationToken cancellationToken)
	{
		lock (gate)
		{
			if (!maps.TryGetValue(map ?? string.Empty, out List<KillPoint>? kept))
			{
				return Task.FromResult<IReadOnlyList<HeatPoint>>([]);
			}

			IEnumerable<KillPoint> newest = Enumerable.Reverse(kept);
			IReadOnlyList<HeatPoint> points = kind == HeatmapKind.Kills
				? [.. newest.Where(p => (steamId is null) || (p.KillerId == steamId)).Take(limit).Select(p => new HeatPoint(p.KillerX, p.KillerY))]
				: [.. newest.Where(p => (steamId is null) || (p.VictimId == steamId)).Take(limit).Select(p => new HeatPoint(p.VictimX, p.VictimY))];
			return Task.FromResult(points);
		}
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<KillPoint>> FightsAsync(string map, ulong? steamId, DateTime? since, int limit, CancellationToken cancellationToken)
	{
		lock (gate)
		{
			if (!maps.TryGetValue(map ?? string.Empty, out List<KillPoint>? kept))
			{
				return Task.FromResult<IReadOnlyList<KillPoint>>([]);
			}

			IReadOnlyList<KillPoint> fights = [.. Enumerable.Reverse(kept)
				.Where(p => (steamId is null) || (p.KillerId == steamId) || (p.VictimId == steamId))
				.Where(p => (since is null) || (p.At >= since))
				.Take(limit)];
			return Task.FromResult(fights);
		}
	}

	/// <inheritdoc/>
	public Task<string?> LastMapAsync(ulong steamId, CancellationToken cancellationToken)
	{
		lock (gate)
		{
			string? map = lastMaps.GetValueOrDefault(steamId);
			return Task.FromResult(map);
		}
	}
}
