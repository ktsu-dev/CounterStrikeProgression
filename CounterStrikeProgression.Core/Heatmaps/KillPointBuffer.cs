// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// Holds kills on the game thread until the next write. It keeps one in every
/// <see cref="HeatmapSettings.SampleEvery"/> kills per map, and drops the oldest past
/// <see cref="HeatmapSettings.QueueLimit"/>, so a database outage can't grow it without end.
/// </summary>
/// <param name="settings">The heatmap settings.</param>
internal sealed class KillPointBuffer(HeatmapSettings settings)
{
	private readonly LinkedList<KillPoint> pending = new();
	private readonly Dictionary<string, long> seen = new(StringComparer.Ordinal);

	/// <summary>Gets how many kills are waiting.</summary>
	public int Count => pending.Count;

	private int SampleEvery => Math.Max(1, settings.SampleEvery);

	private int QueueLimit => Math.Max(100, settings.QueueLimit);

	/// <summary>
	/// Offers a kill. Kills with no id on either end (two bots with no persona), and kills the sampling skips, aren't kept.
	/// </summary>
	/// <param name="point">The kill.</param>
	/// <returns>Whether it was kept.</returns>
	public bool Add(KillPoint point)
	{
		Ensure.NotNull(point);
		if (!settings.Enabled || ((point.KillerId == 0) && (point.VictimId == 0)) || string.IsNullOrEmpty(point.Map))
		{
			return false;
		}

		long index = seen.GetValueOrDefault(point.Map);
		seen[point.Map] = index + 1;
		if ((index % SampleEvery) != 0)
		{
			return false;
		}

		pending.AddLast(point);
		Trim();
		return true;
	}

	/// <summary>
	/// Takes every waiting kill, oldest first.
	/// </summary>
	/// <returns>The kills, which are no longer held.</returns>
	public IReadOnlyList<KillPoint> Drain()
	{
		List<KillPoint> taken = [.. pending];
		pending.Clear();
		return taken;
	}

	/// <summary>
	/// Puts back kills whose write failed, ahead of any that arrived since, so they're written next time.
	/// </summary>
	/// <param name="points">The kills from <see cref="Drain"/>.</param>
	public void Return(IReadOnlyList<KillPoint> points)
	{
		Ensure.NotNull(points);
		for (int i = points.Count - 1; i >= 0; i--)
		{
			pending.AddFirst(points[i]);
		}

		Trim();
	}

	private void Trim()
	{
		while (pending.Count > QueueLimit)
		{
			pending.RemoveFirst();
		}
	}
}
