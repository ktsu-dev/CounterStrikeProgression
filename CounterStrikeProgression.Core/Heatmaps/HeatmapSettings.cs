// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// Where kills happen, kept per map for the site's heatmaps. Kills with a human on either end and kills between two bot
/// personas are kept apart. Each map keeps only its latest <see cref="MaxPointsPerMap"/> of the first and its latest
/// <see cref="MaxBotFightsPerMap"/> of the second, so the table stays a fixed size and bot fights never push out a human's kills.
/// </summary>
public sealed class HeatmapSettings
{
	/// <summary>Gets or sets a value indicating whether kill positions are recorded.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Gets or sets how many kills make one sample: 1 keeps every kill, 3 keeps every third. Values under 1 count as 1.</summary>
	public int SampleEvery { get; set; } = 1;

	/// <summary>Gets or sets how many kills with a human on either end each map keeps. Past this, the oldest are deleted. Values under 100 count as 100.</summary>
	public int MaxPointsPerMap { get; set; } = 30_000;

	/// <summary>Gets or sets how many kills between two bot personas each map keeps, counted apart from <see cref="MaxPointsPerMap"/>. Past this, the oldest are deleted. Values under 100 count as 100.</summary>
	public int MaxBotFightsPerMap { get; set; } = 10_000;

	/// <summary>Gets or sets how many kills wait to be written before the oldest are dropped, for when the database is down. Values under 100 count as 100.</summary>
	public int QueueLimit { get; set; } = 5_000;
}
