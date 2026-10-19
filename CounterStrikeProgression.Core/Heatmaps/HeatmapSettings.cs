// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// Where kills happen, kept per map for the site's heatmaps. Only kills with a human on either end are kept,
/// and each map keeps only its latest <see cref="MaxPointsPerMap"/>, so the table stays a fixed size.
/// </summary>
public sealed class HeatmapSettings
{
	/// <summary>Gets or sets a value indicating whether kill positions are recorded.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Gets or sets how many kills make one sample: 1 keeps every kill, 3 keeps every third. Values under 1 count as 1.</summary>
	public int SampleEvery { get; set; } = 1;

	/// <summary>Gets or sets how many kills each map keeps. Past this, the oldest are deleted. Values under 100 count as 100.</summary>
	public int MaxPointsPerMap { get; set; } = 30_000;

	/// <summary>Gets or sets how many kills wait to be written before the oldest are dropped, for when the database is down. Values under 100 count as 100.</summary>
	public int QueueLimit { get; set; } = 5_000;
}
