// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using System.Runtime.CompilerServices;
using Dapper;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Weapons;
using MySqlConnector;

/// <summary>
/// Keeps progress in MariaDB or MySQL, in tables prefixed <c>csp_</c>.
/// </summary>
/// <param name="settings">Where the database is.</param>
internal sealed class MySqlProgressionStore(DatabaseSettings settings) : IProgressionStore
{
	/// <summary>
	/// The schema version these statements create and expect. Version 3 counts ranks in stages of the 12-rank
	/// ladder, so an older database has its rank indexes cleared once, and each player's rank comes back from their XP.
	/// </summary>
	public const int SchemaVersion = 3;

	private const string CreateTables = """
		CREATE TABLE IF NOT EXISTS csp_schema (
			id TINYINT NOT NULL PRIMARY KEY,
			version INT NOT NULL
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_players (
			steam_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
			name VARCHAR(128) NOT NULL DEFAULT '',
			rank_xp BIGINT NOT NULL DEFAULT 0,
			rank_index INT NOT NULL DEFAULT 0,
			first_seen DATETIME NOT NULL,
			last_seen DATETIME NOT NULL,
			KEY idx_csp_players_rank_xp (rank_xp)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_weapons (
			steam_id BIGINT UNSIGNED NOT NULL,
			weapon VARCHAR(32) NOT NULL,
			xp BIGINT NOT NULL DEFAULT 0,
			level INT NOT NULL DEFAULT 1,
			kills BIGINT NOT NULL DEFAULT 0,
			headshots BIGINT NOT NULL DEFAULT 0,
			hits BIGINT NOT NULL DEFAULT 0,
			shots BIGINT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, weapon)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_challenges (
			steam_id BIGINT UNSIGNED NOT NULL,
			weapon VARCHAR(32) NOT NULL,
			challenge VARCHAR(48) NOT NULL,
			count BIGINT NOT NULL DEFAULT 0,
			tier INT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, weapon, challenge)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_contracts (
			steam_id BIGINT UNSIGNED NOT NULL,
			period VARCHAR(16) NOT NULL,
			contract VARCHAR(48) NOT NULL,
			count BIGINT NOT NULL DEFAULT 0,
			completed TINYINT(1) NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, period, contract)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		INSERT IGNORE INTO csp_schema (id, version) VALUES (1, @version);
		""";

	private const string MigrateToStagedRanks = """
		UPDATE csp_players SET rank_index = 0 WHERE (SELECT version FROM csp_schema WHERE id = 1) < 3;
		UPDATE csp_schema SET version = 3 WHERE id = 1 AND version < 3;
		""";

	private const string UpsertPlayer = """
		INSERT INTO csp_players (steam_id, name, rank_xp, rank_index, first_seen, last_seen)
		VALUES (@SteamId, @Name, @RankXp, @Rank, @FirstSeen, @LastSeen)
		ON DUPLICATE KEY UPDATE name = VALUES(name), rank_xp = VALUES(rank_xp), rank_index = VALUES(rank_index), last_seen = VALUES(last_seen);
		""";

	private const string UpsertWeapon = """
		INSERT INTO csp_weapons (steam_id, weapon, xp, level, kills, headshots, hits, shots)
		VALUES (@SteamId, @Weapon, @Xp, @Level, @Kills, @Headshots, @Hits, @Shots)
		ON DUPLICATE KEY UPDATE xp = VALUES(xp), level = VALUES(level), kills = VALUES(kills), headshots = VALUES(headshots), hits = VALUES(hits), shots = VALUES(shots);
		""";

	private const string UpsertChallenge = """
		INSERT INTO csp_challenges (steam_id, weapon, challenge, count, tier)
		VALUES (@SteamId, @Weapon, @Challenge, @Count, @Tier)
		ON DUPLICATE KEY UPDATE count = VALUES(count), tier = VALUES(tier);
		""";

	private const string UpsertContract = """
		INSERT INTO csp_contracts (steam_id, period, contract, count, completed)
		VALUES (@SteamId, @Period, @Contract, @Count, @Completed)
		ON DUPLICATE KEY UPDATE count = VALUES(count), completed = VALUES(completed);
		""";

	private const string DeletePlayerRows = """
		DELETE FROM csp_challenges WHERE steam_id = @SteamId;
		DELETE FROM csp_weapons WHERE steam_id = @SteamId;
		DELETE FROM csp_contracts WHERE steam_id = @SteamId;
		""";

	private readonly string connectionString = ConnectionStringFor(settings);

	/// <summary>
	/// Builds the connection string for a database. Dates are read back as UTC, because the plugin writes
	/// <see cref="DateTime.UtcNow"/>, and a reader that sees an unspecified kind would treat them as local time.
	/// </summary>
	/// <param name="settings">Where the database is.</param>
	/// <returns>The connection string.</returns>
	public static string ConnectionStringFor(DatabaseSettings settings)
	{
		Ensure.NotNull(settings);
		return new MySqlConnectionStringBuilder
		{
			Server = settings.Host,
			Port = (uint)settings.Port,
			Database = settings.Database,
			UserID = settings.User,
			Password = settings.Password,
			SslMode = settings.RequireTls ? MySqlSslMode.Required : MySqlSslMode.Preferred,
			AllowUserVariables = false,
			ConnectionTimeout = 15,
			DateTimeKind = MySqlDateTimeKind.Utc,
		}.ConnectionString;
	}

	/// <inheritdoc/>
	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(CreateTables, new { version = SchemaVersion }, cancellationToken: cancellationToken)).ConfigureAwait(false);

		MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable transactionScope = transaction.ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(MigrateToStagedRanks, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		object parameters = new { steamId };

		PlayerRow? player = await connection.QuerySingleOrDefaultAsync<PlayerRow>(new CommandDefinition(
			"SELECT steam_id AS SteamId, name AS Name, rank_xp AS RankXp, rank_index AS `Rank`, first_seen AS FirstSeen, last_seen AS LastSeen FROM csp_players WHERE steam_id = @steamId",
			parameters,
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		if (player is null)
		{
			return null;
		}

		IEnumerable<WeaponRow> weapons = await connection.QueryAsync<WeaponRow>(new CommandDefinition(
			"SELECT steam_id AS SteamId, weapon AS Weapon, xp AS Xp, level AS Level, kills AS Kills, headshots AS Headshots, hits AS Hits, shots AS Shots FROM csp_weapons WHERE steam_id = @steamId",
			parameters,
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		IEnumerable<ChallengeRow> challenges = await connection.QueryAsync<ChallengeRow>(new CommandDefinition(
			"SELECT steam_id AS SteamId, weapon AS Weapon, challenge AS Challenge, count AS `Count`, tier AS Tier FROM csp_challenges WHERE steam_id = @steamId",
			parameters,
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		// Only the running day and week matter, so older contract rows stay in the table but aren't read.
		DateOnly today = DailyRotation.Day(DateTime.UtcNow);
		IEnumerable<ContractRow> contracts = await connection.QueryAsync<ContractRow>(new CommandDefinition(
			"SELECT steam_id AS SteamId, period AS Period, contract AS Contract, count AS `Count`, completed AS Completed FROM csp_contracts WHERE steam_id = @steamId AND period IN (@day, @week)",
			new { steamId, day = DailyRotation.DayKey(today), week = DailyRotation.WeekKey(today) },
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		return ProgressSnapshot.Rebuild(player, weapons, challenges, contracts);
	}

	/// <inheritdoc/>
	public async Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken)
	{
		Ensure.NotNull(snapshot);
		if (snapshot.IsEmpty)
		{
			return;
		}

		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable transactionScope = transaction.ConfigureAwait(false);

		if (snapshot.Player is not null)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertPlayer, snapshot.Player, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		if (snapshot.Weapons.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertWeapon, snapshot.Weapons, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		if (snapshot.Challenges.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertChallenge, snapshot.Challenges, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		if (snapshot.Contracts.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertContract, snapshot.Contracts, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task ResetAsync(PlayerRow player, CancellationToken cancellationToken)
	{
		Ensure.NotNull(player);

		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable transactionScope = transaction.ConfigureAwait(false);

		await connection.ExecuteAsync(new CommandDefinition(DeletePlayerRows, new { player.SteamId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(UpsertPlayer, player, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<LeaderboardEntry> top = await connection.QueryAsync<LeaderboardEntry>(new CommandDefinition(
			"SELECT name AS Name, rank_xp AS RankXp, rank_index AS `Rank` FROM csp_players ORDER BY rank_xp DESC LIMIT @count",
			new { count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. top];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<WeaponLeaderboardEntry>> TopByWeaponAsync(WeaponKey weapon, int count, CancellationToken cancellationToken)
	{
		Ensure.NotNull(weapon);
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<WeaponLeaderboardEntry> top = await connection.QueryAsync<WeaponLeaderboardEntry>(new CommandDefinition(
			"SELECT p.name AS Name, w.xp AS Xp, w.level AS Level FROM csp_weapons w JOIN csp_players p ON p.steam_id = w.steam_id WHERE w.weapon = @weapon AND w.xp > 0 ORDER BY w.xp DESC, w.steam_id ASC LIMIT @count",
			new { weapon = weapon.WeakString, count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. top];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<RankingEntry> rankings = await connection.QueryAsync<RankingEntry>(new CommandDefinition(
			"SELECT steam_id AS SteamId, name AS Name, rank_xp AS RankXp, rank_index AS `Rank`, last_seen AS LastSeen FROM csp_players ORDER BY rank_xp DESC, steam_id ASC LIMIT @count",
			new { count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. rankings];
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
}
