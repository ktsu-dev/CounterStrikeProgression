// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Where the plugin keeps progress. The tables are prefixed <c>csp_</c>, so they can share a database with
/// other plugins.
/// </summary>
public sealed class DatabaseSettings
{
	/// <summary>Gets or sets the MariaDB or MySQL host.</summary>
	public string Host { get; set; } = "127.0.0.1";

	/// <summary>Gets or sets the port.</summary>
	public int Port { get; set; } = 3306;

	/// <summary>Gets or sets the database name.</summary>
	public string Database { get; set; } = "cs2";

	/// <summary>Gets or sets the user name.</summary>
	public string User { get; set; } = "cs2";

	/// <summary>Gets or sets the password.</summary>
	public string Password { get; set; } = string.Empty;

	/// <summary>Gets or sets a value indicating whether the server connection must be encrypted.</summary>
	public bool RequireTls { get; set; }
}
