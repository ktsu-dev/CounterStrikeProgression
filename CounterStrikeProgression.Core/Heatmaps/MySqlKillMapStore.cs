// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

using System.Runtime.CompilerServices;
using Dapper;
using ktsu.CounterStrikeProgression.Core.Storage;
using MySqlConnector;

/// <summary>
/// Keeps kill positions in MariaDB or MySQL, in <c>csp_kills</c>.
/// </summary>
/// <param name="settings">Where the database is.</param>
internal sealed class MySqlKillMapStore(DatabaseSettings settings) : IKillMapStore
{
	private const string CreateTable = """
		CREATE TABLE IF NOT EXISTS csp_kills (
			id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
			map VARCHAR(64) NOT NULL,
			killer_id BIGINT UNSIGNED NOT NULL,
			victim_id BIGINT UNSIGNED NOT NULL,
			weapon VARCHAR(32) NOT NULL,
			headshot TINYINT(1) NOT NULL DEFAULT 0,
			killer_x SMALLINT NOT NULL,
			killer_y SMALLINT NOT NULL,
			killer_z SMALLINT NOT NULL,
			victim_x SMALLINT NOT NULL,
			victim_y SMALLINT NOT NULL,
			victim_z SMALLINT NOT NULL,
			at DATETIME NOT NULL,
			KEY idx_csp_kills_map (map, id),
			KEY idx_csp_kills_killer (killer_id, map, id),
			KEY idx_csp_kills_victim (victim_id, map, id)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
		""";

	private const string Insert = """
		INSERT INTO csp_kills (map, killer_id, victim_id, weapon, headshot, killer_x, killer_y, killer_z, victim_x, victim_y, victim_z, at)
		VALUES (@Map, @KillerId, @VictimId, @Weapon, @Headshot, @KillerX, @KillerY, @KillerZ, @VictimX, @VictimY, @VictimZ, @At);
		""";

	// The derived table stops MariaDB refusing a delete that reads its own table, and finds the newest id past the cap.
	private const string Prune = """
		DELETE FROM csp_kills WHERE map = @map AND id <= (
			SELECT id FROM (SELECT id FROM csp_kills WHERE map = @map ORDER BY id DESC LIMIT 1 OFFSET @max) cutoff);
		""";

	private const string SelectMaps = "SELECT map AS Map, COUNT(*) AS Points FROM csp_kills GROUP BY map ORDER BY Points DESC, Map ASC";

	private const string SelectKills = "SELECT killer_x AS X, killer_y AS Y FROM csp_kills WHERE map = @map ORDER BY id DESC LIMIT @limit";

	private const string SelectDeaths = "SELECT victim_x AS X, victim_y AS Y FROM csp_kills WHERE map = @map ORDER BY id DESC LIMIT @limit";

	private const string SelectPlayerKills = "SELECT killer_x AS X, killer_y AS Y FROM csp_kills WHERE killer_id = @steamId AND map = @map ORDER BY id DESC LIMIT @limit";

	private const string SelectPlayerDeaths = "SELECT victim_x AS X, victim_y AS Y FROM csp_kills WHERE victim_id = @steamId AND map = @map ORDER BY id DESC LIMIT @limit";

	private readonly string connectionString = MySqlProgressionStore.ConnectionStringFor(settings);

	/// <inheritdoc/>
	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(CreateTable, cancellationToken: cancellationToken)).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task AddAsync(IReadOnlyList<KillPoint> points, int maxPerMap, CancellationToken cancellationToken)
	{
		Ensure.NotNull(points);
		if (points.Count == 0)
		{
			return;
		}

		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable transactionScope = transaction.ConfigureAwait(false);

		await connection.ExecuteAsync(new CommandDefinition(Insert, points, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		foreach (string map in points.Select(p => p.Map).Distinct(StringComparer.Ordinal))
		{
			await connection.ExecuteAsync(new CommandDefinition(Prune, new { map, max = maxPerMap }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<MapPointCount>> MapsAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		try
		{
			IEnumerable<MapPointCount> maps = await connection.QueryAsync<MapPointCount>(new CommandDefinition(SelectMaps, cancellationToken: cancellationToken)).ConfigureAwait(false);
			return [.. maps];
		}
		catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoSuchTable)
		{
			// The site can read before the plugin has written its first kill.
			return [];
		}
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<HeatPoint>> PointsAsync(string map, HeatmapKind kind, ulong? steamId, int limit, CancellationToken cancellationToken)
	{
		string sql = (kind, steamId is null) switch
		{
			(HeatmapKind.Kills, true) => SelectKills,
			(HeatmapKind.Kills, false) => SelectPlayerKills,
			(_, true) => SelectDeaths,
			_ => SelectPlayerDeaths,
		};

		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		try
		{
			IEnumerable<PointRow> rows = await connection.QueryAsync<PointRow>(new CommandDefinition(
				sql,
				new { map, steamId = steamId ?? 0, limit },
				cancellationToken: cancellationToken)).ConfigureAwait(false);
			return [.. rows.Select(r => new HeatPoint(r.X, r.Y))];
		}
		catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoSuchTable)
		{
			return [];
		}
	}

	private async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = new(connectionString);
		try
		{
			await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			return connection;
		}
		catch
		{
			await connection.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>A position as read, with the column types the driver returns.</summary>
	/// <param name="X">The X position.</param>
	/// <param name="Y">The Y position.</param>
	private sealed record PointRow(short X, short Y);
}
