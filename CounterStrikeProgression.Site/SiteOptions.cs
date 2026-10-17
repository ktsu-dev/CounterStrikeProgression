// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// The site's settings, bound from the <c>Site</c> configuration section.
/// </summary>
internal sealed class SiteOptions
{
	/// <summary>Gets or sets where the database is. Use a user that can only read the <c>csp_</c> tables. An empty host keeps the stored progress in memory, which starts empty.</summary>
	public DatabaseSettings Database { get; set; } = new();

	/// <summary>Gets or sets the shared secret the plugin sends with every batch. Empty rejects all batches.</summary>
	public string Token { get; set; } = string.Empty;

	/// <summary>Gets or sets the folder where the feed history is kept.</summary>
	public string DataPath { get; set; } = "/data";

	/// <summary>Gets or sets how many seconds without a batch before the server counts as offline.</summary>
	public double OfflineAfterSeconds { get; set; } = 15;

	/// <summary>Gets or sets how many feed entries to keep and show.</summary>
	public int FeedLength { get; set; } = 50;
}
