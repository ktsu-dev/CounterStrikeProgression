// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using System.Runtime.CompilerServices;
using Dapper;
using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Weapons;
using MySqlConnector;

/// <summary>A <c>csp_daily</c> row as read, with the day as the <see cref="DateTime"/> the driver returns.</summary>
/// <param name="Day">The day, at midnight.</param>
/// <param name="RankXp">The rank XP at the end of the day.</param>
internal sealed record DailyRow(DateTime Day, long RankXp);

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
			welcome_back_xp BIGINT NOT NULL DEFAULT 0,
			KEY idx_csp_players_rank_xp (rank_xp)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		ALTER TABLE csp_players ADD COLUMN IF NOT EXISTS welcome_back_xp BIGINT NOT NULL DEFAULT 0;

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

		CREATE TABLE IF NOT EXISTS csp_daily (
			steam_id BIGINT UNSIGNED NOT NULL,
			day DATE NOT NULL,
			rank_xp BIGINT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, day)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_seasons (
			steam_id BIGINT UNSIGNED NOT NULL,
			season_start DATE NOT NULL,
			xp BIGINT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, season_start),
			KEY idx_csp_seasons_xp (season_start, xp)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_maps (
			steam_id BIGINT UNSIGNED NOT NULL,
			map VARCHAR(64) NOT NULL,
			xp BIGINT NOT NULL DEFAULT 0,
			kills BIGINT NOT NULL DEFAULT 0,
			deaths BIGINT NOT NULL DEFAULT 0,
			seconds BIGINT NOT NULL DEFAULT 0,
			sessions BIGINT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, map),
			KEY idx_csp_maps_xp (map, xp)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_records (
			record VARCHAR(48) NOT NULL PRIMARY KEY,
			steam_id BIGINT UNSIGNED NOT NULL,
			name VARCHAR(128) NOT NULL DEFAULT '',
			value DOUBLE NOT NULL,
			set_at DATETIME NOT NULL,
			previous_steam_id BIGINT UNSIGNED NULL,
			previous_name VARCHAR(128) NULL,
			previous_value DOUBLE NULL
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_digests (
			week DATE NOT NULL PRIMARY KEY,
			posted_at DATETIME NOT NULL
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		CREATE TABLE IF NOT EXISTS csp_rivals (
			steam_id BIGINT UNSIGNED NOT NULL,
			victim_id BIGINT UNSIGNED NOT NULL,
			kills BIGINT NOT NULL DEFAULT 0,
			PRIMARY KEY (steam_id, victim_id),
			KEY idx_csp_rivals_victim (victim_id)
		) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

		INSERT IGNORE INTO csp_schema (id, version) VALUES (1, @version);
		""";

	/// <summary>
	/// Gives each player who has no daily rows yet, such as everyone who played before the table existed, one
	/// starting row with their current total, dated the day before they were last seen. Without it their whole
	/// total would count as earned on the next day they play. The row sits before any day a later save writes,
	/// so it's never overwritten.
	/// </summary>
	private const string SeedDaily = """
		INSERT IGNORE INTO csp_daily (steam_id, day, rank_xp)
		SELECT p.steam_id, DATE(p.last_seen) - INTERVAL 1 DAY, p.rank_xp
		FROM csp_players p
		WHERE NOT EXISTS (SELECT 1 FROM csp_daily d WHERE d.steam_id = p.steam_id);
		""";

	private const string MigrateToStagedRanks = """
		UPDATE csp_players SET rank_index = 0 WHERE (SELECT version FROM csp_schema WHERE id = 1) < 3;
		UPDATE csp_schema SET version = 3 WHERE id = 1 AND version < 3;
		""";

	private const string UpsertPlayer = """
		INSERT INTO csp_players (steam_id, name, rank_xp, rank_index, first_seen, last_seen, welcome_back_xp)
		VALUES (@SteamId, @Name, @RankXp, @Rank, @FirstSeen, @LastSeen, @WelcomeBackXp)
		ON DUPLICATE KEY UPDATE name = VALUES(name), rank_xp = VALUES(rank_xp), rank_index = VALUES(rank_index), last_seen = VALUES(last_seen), welcome_back_xp = VALUES(welcome_back_xp);
		""";

	private const string UpsertDaily = """
		INSERT INTO csp_daily (steam_id, day, rank_xp)
		VALUES (@SteamId, DATE(@LastSeen), @RankXp)
		ON DUPLICATE KEY UPDATE rank_xp = VALUES(rank_xp);
		""";

	/// <summary>
	/// Every daily row of a player, which the play streak is worked out from. A player has one row per day they
	/// played, so reading them all stays small.
	/// </summary>
	private const string SelectDaily = "SELECT day AS Day, rank_xp AS RankXp FROM csp_daily WHERE steam_id = @steamId ORDER BY day";

	private const string SelectHistory = """
		(SELECT day AS Day, rank_xp AS RankXp FROM csp_daily WHERE steam_id = @steamId AND day < @from ORDER BY day DESC LIMIT 1)
		UNION
		(SELECT day AS Day, rank_xp AS RankXp FROM csp_daily WHERE steam_id = @steamId ORDER BY day ASC LIMIT 1)
		UNION
		(SELECT day AS Day, rank_xp AS RankXp FROM csp_daily WHERE steam_id = @steamId AND day >= @from)
		ORDER BY Day
		""";

	/// <summary>
	/// The same rule as <see cref="XpHistory.Baseline"/>: the latest row before the start, else 0 for a player first
	/// seen since, else their earliest row, else their current total.
	/// </summary>
	private const string SelectGains = """
		SELECT SteamId, Name, RankXp, `Rank`, Gained FROM (
			SELECT p.steam_id AS SteamId, p.name AS Name, p.rank_xp AS RankXp, p.rank_index AS `Rank`,
				p.rank_xp - COALESCE(
					(SELECT d.rank_xp FROM csp_daily d WHERE d.steam_id = p.steam_id AND d.day < @start ORDER BY d.day DESC LIMIT 1),
					IF(p.first_seen >= @start, 0, NULL),
					(SELECT d.rank_xp FROM csp_daily d WHERE d.steam_id = p.steam_id ORDER BY d.day ASC LIMIT 1),
					p.rank_xp) AS Gained
			FROM csp_players p
			WHERE p.last_seen >= @start
		) g
		WHERE Gained > 0
		ORDER BY Gained DESC, SteamId ASC
		LIMIT @count
		""";

	/// <summary>
	/// Each player's rank XP before a span, by the same rule as <see cref="SelectGains"/>, and at its end, which is
	/// their latest row before the day after it.
	/// </summary>
	private const string SelectGainsBetween = """
		SELECT SteamId, Name, StartXp, EndXp FROM (
			SELECT p.steam_id AS SteamId, p.name AS Name,
				COALESCE(
					(SELECT d.rank_xp FROM csp_daily d WHERE d.steam_id = p.steam_id AND d.day < @start ORDER BY d.day DESC LIMIT 1),
					IF(p.first_seen >= @start, 0, NULL),
					(SELECT d.rank_xp FROM csp_daily d WHERE d.steam_id = p.steam_id ORDER BY d.day ASC LIMIT 1)) AS StartXp,
				(SELECT d.rank_xp FROM csp_daily d WHERE d.steam_id = p.steam_id AND d.day < @end ORDER BY d.day DESC LIMIT 1) AS EndXp
			FROM csp_players p
			WHERE p.last_seen >= @start AND p.first_seen < @end
		) g
		WHERE EndXp > StartXp
		ORDER BY EndXp - StartXp DESC, SteamId ASC
		""";

	private const string ClaimDigest = "INSERT IGNORE INTO csp_digests (week, posted_at) VALUES (@week, @at)";

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

	private const string UpsertSeason = """
		INSERT INTO csp_seasons (steam_id, season_start, xp)
		VALUES (@SteamId, @Start, @Xp)
		ON DUPLICATE KEY UPDATE xp = VALUES(xp);
		""";

	private const string UpsertMap = """
		INSERT INTO csp_maps (steam_id, map, xp, kills, deaths, seconds, sessions)
		VALUES (@SteamId, @Map, @Xp, @Kills, @Deaths, @Seconds, @Sessions)
		ON DUPLICATE KEY UPDATE xp = VALUES(xp), kills = VALUES(kills), deaths = VALUES(deaths), seconds = VALUES(seconds), sessions = VALUES(sessions);
		""";

	private const string SelectMaps =
		"SELECT steam_id AS SteamId, map AS Map, xp AS Xp, kills AS Kills, deaths AS Deaths, seconds AS Seconds, sessions AS Sessions FROM csp_maps WHERE steam_id = @steamId";

	private const string SelectMapStandings = """
		SELECT m.steam_id AS SteamId, p.name AS Name, p.rank_index AS `Rank`, m.xp AS Xp, m.kills AS Kills, m.deaths AS Deaths, m.seconds AS Seconds
		FROM csp_maps m JOIN csp_players p ON p.steam_id = m.steam_id
		WHERE m.map = @map AND m.xp > 0
		ORDER BY m.xp DESC, m.steam_id ASC LIMIT @count
		""";

	private const string SelectMapSummaries = """
		SELECT map AS Map, COUNT(*) AS Players, CAST(SUM(xp) AS SIGNED) AS Xp, CAST(SUM(seconds) AS SIGNED) AS Seconds
		FROM csp_maps WHERE xp > 0
		GROUP BY map
		ORDER BY Seconds DESC, Map ASC
		""";

	private const string UpsertRival = """
		INSERT INTO csp_rivals (steam_id, victim_id, kills)
		VALUES (@SteamId, @VictimId, @Kills)
		ON DUPLICATE KEY UPDATE kills = VALUES(kills);
		""";

	/// <summary>
	/// A player's kills of each other player and that player's kills of them, from both sides of <c>csp_rivals</c>.
	/// </summary>
	private const string SelectHeadToHead = """
		SELECT o.other AS SteamId, COALESCE(MAX(p.name), '') AS Name, CAST(SUM(o.kills) AS SIGNED) AS Kills, CAST(SUM(o.deaths) AS SIGNED) AS Deaths
		FROM (
			SELECT victim_id AS other, kills, 0 AS deaths FROM csp_rivals WHERE steam_id = @steamId AND kills > 0
			UNION ALL
			SELECT steam_id AS other, 0 AS kills, kills AS deaths FROM csp_rivals WHERE victim_id = @steamId AND kills > 0
		) o
		LEFT JOIN csp_players p ON p.steam_id = o.other
		GROUP BY o.other
		ORDER BY Kills + Deaths DESC, SteamId ASC
		LIMIT @count
		""";

	private const string SelectLatestSeason =
		"SELECT steam_id AS SteamId, season_start AS Start, xp AS Xp FROM csp_seasons WHERE steam_id = @steamId ORDER BY season_start DESC LIMIT 1";

	private const string SelectSeasonStandings = """
		SELECT s.steam_id AS SteamId, p.name AS Name, p.rank_index AS `Rank`, s.xp AS Xp
		FROM csp_seasons s JOIN csp_players p ON p.steam_id = s.steam_id
		WHERE s.season_start = @start AND s.xp > 0
		ORDER BY s.xp DESC, s.steam_id ASC LIMIT @count
		""";

	private const string SelectSeasonResults = """
		SELECT s.season_start AS Start, s.xp AS Xp,
			(SELECT COUNT(*) FROM csp_seasons o WHERE o.season_start = s.season_start AND o.xp > s.xp) + 1 AS Place,
			(SELECT COUNT(*) FROM csp_seasons o WHERE o.season_start = s.season_start AND o.xp > 0) AS Players
		FROM csp_seasons s
		WHERE s.steam_id = @steamId AND s.xp > 0
		ORDER BY s.season_start DESC
		""";

	/// <summary>
	/// Writes a record only when it beats the stored one. MariaDB applies the assignments in order, so
	/// <c>value</c> goes last, and every assignment before it still compares against the stored value.
	/// </summary>
	private const string UpsertRecord = """
		INSERT INTO csp_records (record, steam_id, name, value, set_at, previous_steam_id, previous_name, previous_value)
		VALUES (@Record, @SteamId, @Name, @Value, @SetAt, @PreviousSteamId, @PreviousName, @PreviousValue)
		ON DUPLICATE KEY UPDATE
			steam_id = IF(VALUES(value) > value, VALUES(steam_id), steam_id),
			name = IF(VALUES(value) > value, VALUES(name), name),
			set_at = IF(VALUES(value) > value, VALUES(set_at), set_at),
			previous_steam_id = IF(VALUES(value) > value, VALUES(previous_steam_id), previous_steam_id),
			previous_name = IF(VALUES(value) > value, VALUES(previous_name), previous_name),
			previous_value = IF(VALUES(value) > value, VALUES(previous_value), previous_value),
			value = GREATEST(value, VALUES(value));
		""";

	private const string SelectRecords = """
		SELECT record AS Record, steam_id AS SteamId, name AS Name, value AS Value, set_at AS SetAt,
			previous_steam_id AS PreviousSteamId, previous_name AS PreviousName, previous_value AS PreviousValue
		FROM csp_records
		""";

	private const string SelectContracts =
		"SELECT steam_id AS SteamId, period AS Period, contract AS Contract, count AS `Count`, completed AS Completed FROM csp_contracts WHERE steam_id = @steamId AND period IN (@day, @week)";

	/// <summary>
	/// Clears a player's rows for a reset. Their daily history starts again from the day before, so the XP they
	/// keep isn't counted as earned.
	/// </summary>
	private const string DeletePlayerRows = """
		DELETE FROM csp_challenges WHERE steam_id = @SteamId;
		DELETE FROM csp_weapons WHERE steam_id = @SteamId;
		DELETE FROM csp_contracts WHERE steam_id = @SteamId;
		DELETE FROM csp_rivals WHERE steam_id = @SteamId;
		DELETE FROM csp_seasons WHERE steam_id = @SteamId;
		DELETE FROM csp_maps WHERE steam_id = @SteamId;
		DELETE FROM csp_daily WHERE steam_id = @SteamId;
		INSERT INTO csp_daily (steam_id, day, rank_xp) VALUES (@SteamId, DATE(@LastSeen) - INTERVAL 1 DAY, @RankXp);
		""";

	/// <summary>
	/// The most rivals a load reads. Players only meet the people on the server with them, so real counts stay far below it.
	/// </summary>
	private const int MaxRivalsLoaded = 5000;

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
		await connection.ExecuteAsync(new CommandDefinition(SeedDaily, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		object parameters = new { steamId };

		PlayerRow? player = await connection.QuerySingleOrDefaultAsync<PlayerRow>(new CommandDefinition(
			"SELECT steam_id AS SteamId, name AS Name, rank_xp AS RankXp, rank_index AS `Rank`, first_seen AS FirstSeen, last_seen AS LastSeen, welcome_back_xp AS WelcomeBackXp FROM csp_players WHERE steam_id = @steamId",
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

		SeasonRow? season = await connection.QueryFirstOrDefaultAsync<SeasonRow>(new CommandDefinition(
			SelectLatestSeason,
			parameters,
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		// Only the running day and week matter, so older contract rows stay in the table but aren't read.
		DateOnly today = DailyRotation.Day(DateTime.UtcNow);
		IEnumerable<ContractRow> contracts = await connection.QueryAsync<ContractRow>(new CommandDefinition(
			SelectContracts,
			new { steamId, day = DailyRotation.DayKey(today), week = DailyRotation.WeekKey(today) },
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		IEnumerable<MapRow> maps = await connection.QueryAsync<MapRow>(new CommandDefinition(
			SelectMaps,
			parameters,
			cancellationToken: cancellationToken)).ConfigureAwait(false);

		IEnumerable<DailyRow> daily = await connection.QueryAsync<DailyRow>(new CommandDefinition(SelectDaily, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

		PlayerProgress progress = ProgressSnapshot.Rebuild(player, weapons, challenges, season, contracts, maps, daily);
		try
		{
			Rivalry.Load(progress, await QueryHeadToHeadAsync(connection, steamId, MaxRivalsLoaded, cancellationToken).ConfigureAwait(false));
		}
		catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.TableAccessDenied)
		{
			// The site's read-only user gets its grant on csp_rivals after the plugin creates the table. Until then
			// its pages still load, without rivals, which they read separately anyway.
		}

		return progress;
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
			await connection.ExecuteAsync(new CommandDefinition(UpsertDaily, snapshot.Player, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
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

		if (snapshot.Seasons.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertSeason, snapshot.Seasons, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		if (snapshot.Maps.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertMap, snapshot.Maps, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		}

		if (snapshot.Rivals.Count > 0)
		{
			await connection.ExecuteAsync(new CommandDefinition(UpsertRival, snapshot.Rivals, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
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

		await connection.ExecuteAsync(new CommandDefinition(DeletePlayerRows, new { player.SteamId, player.LastSeen, player.RankXp }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(UpsertPlayer, player, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<LeaderboardEntry> top = await connection.QueryAsync<LeaderboardEntry>(new CommandDefinition(
			"SELECT steam_id AS SteamId, name AS Name, rank_xp AS RankXp, rank_index AS `Rank` FROM csp_players ORDER BY rank_xp DESC LIMIT @count",
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
			"SELECT w.steam_id AS SteamId, p.name AS Name, w.xp AS Xp, w.level AS Level FROM csp_weapons w JOIN csp_players p ON p.steam_id = w.steam_id WHERE w.weapon = @weapon AND w.xp > 0 ORDER BY w.xp DESC, w.steam_id ASC LIMIT @count",
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

	/// <inheritdoc/>
	public async Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<DailyRow> rows = await connection.QueryAsync<DailyRow>(new CommandDefinition(
			SelectHistory,
			new { steamId, from = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. rows.Select(r => new DailyXp(DateOnly.FromDateTime(r.Day), r.RankXp))];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<XpGainEntry> gains = await connection.QueryAsync<XpGainEntry>(new CommandDefinition(
			SelectGains,
			new { start = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. gains];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<SeasonStanding>> SeasonStandingsAsync(DateOnly start, int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<SeasonStanding> standings = await connection.QueryAsync<SeasonStanding>(new CommandDefinition(
			SelectSeasonStandings,
			new { start = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. standings];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<SeasonResult>> SeasonResultsAsync(ulong steamId, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<SeasonResult> results = await connection.QueryAsync<SeasonResult>(new CommandDefinition(
			SelectSeasonResults,
			new { steamId },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. results];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<MapStanding>> MapStandingsAsync(string map, int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<MapStanding> standings = await connection.QueryAsync<MapStanding>(new CommandDefinition(
			SelectMapStandings,
			new { map, count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. standings];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<MapSummary>> MapSummariesAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<MapSummary> summaries = await connection.QueryAsync<MapSummary>(new CommandDefinition(
			SelectMapSummaries,
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. summaries];
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<RecordRow>> RecordsAsync(CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<RecordRow> rows = await connection.QueryAsync<RecordRow>(new CommandDefinition(SelectRecords, cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. rows];
	}

	/// <inheritdoc/>
	public async Task SaveRecordsAsync(IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken)
	{
		Ensure.NotNull(rows);
		if (rows.Count == 0)
		{
			return;
		}

		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		await connection.ExecuteAsync(new CommandDefinition(UpsertRecord, rows, cancellationToken: cancellationToken)).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<PeriodGain>> GainsBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		IEnumerable<PeriodGain> gains = await connection.QueryAsync<PeriodGain>(new CommandDefinition(
			SelectGainsBetween,
			new { start = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), end = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. gains];
	}

	/// <inheritdoc/>
	public async Task<bool> ClaimDigestAsync(DateOnly week, DateTime at, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		int inserted = await connection.ExecuteAsync(new CommandDefinition(
			ClaimDigest,
			new { week = week.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), at },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return inserted > 0;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, int count, CancellationToken cancellationToken)
	{
		MySqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
		await using ConfiguredAsyncDisposable connectionScope = connection.ConfigureAwait(false);
		return await QueryHeadToHeadAsync(connection, steamId, count, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<IReadOnlyList<HeadToHead>> QueryHeadToHeadAsync(MySqlConnection connection, ulong steamId, int count, CancellationToken cancellationToken)
	{
		IEnumerable<HeadToHead> rows = await connection.QueryAsync<HeadToHead>(new CommandDefinition(
			SelectHeadToHead,
			new { steamId, count },
			cancellationToken: cancellationToken)).ConfigureAwait(false);
		return [.. rows];
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
