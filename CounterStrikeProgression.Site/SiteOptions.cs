// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

/// <summary>
/// The site's settings, bound from the <c>Site</c> configuration section.
/// </summary>
internal sealed class SiteOptions
{
	/// <summary>Gets or sets the read-only MariaDB connection string. Empty turns off the stored progress pages.</summary>
	public string ConnectionString { get; set; } = string.Empty;

	/// <summary>Gets or sets the shared secret the plugin sends with every batch. Empty rejects all batches.</summary>
	public string Token { get; set; } = string.Empty;

	/// <summary>Gets or sets the folder where the feed history is kept.</summary>
	public string DataPath { get; set; } = "/data";

	/// <summary>Gets or sets how many seconds without a batch before the server counts as offline.</summary>
	public double OfflineAfterSeconds { get; set; } = 15;

	/// <summary>Gets or sets how many feed entries to keep and show.</summary>
	public int FeedLength { get; set; } = 50;
}
