// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Reads each query's columns, in the query's order, through Dapper into its record, the way a MariaDB reader
/// would hand them over. Dapper fills a positional record's constructor from the columns in order, so a column
/// out of place fails here instead of on the server.
/// </summary>
[TestClass]
public sealed partial class QueryMappingTests
{
	[TestMethod]
	public void TopColumnsMapIntoTheLeaderboardEntry()
	{
		LeaderboardEntry entry = ReadOne<LeaderboardEntry>(MySqlProgressionStore.SelectTop, new Dictionary<string, object>
		{
			["SteamId"] = 10000000062568863UL,
			["Name"] = "Muhlik",
			["RankXp"] = 5L,
			["Rank"] = 1,
		});

		Assert.AreEqual(new LeaderboardEntry("Muhlik", 5, 1, 10000000062568863UL), entry);
	}

	[TestMethod]
	public void TopByWeaponColumnsMapIntoTheWeaponLeaderboardEntry()
	{
		WeaponLeaderboardEntry entry = ReadOne<WeaponLeaderboardEntry>(MySqlProgressionStore.SelectTopByWeapon, new Dictionary<string, object>
		{
			["SteamId"] = 76561198000000001UL,
			["Name"] = "Ace",
			["Xp"] = 1200L,
			["Level"] = 4,
		});

		Assert.AreEqual(new WeaponLeaderboardEntry("Ace", 1200, 4, 76561198000000001UL), entry);
	}

	/// <summary>
	/// Builds a one-row reader whose columns are the query's aliases in the query's order, each holding its value,
	/// and reads it through Dapper.
	/// </summary>
	private static T ReadOne<T>(string sql, Dictionary<string, object> values)
	{
		string selectList = sql[..sql.IndexOf(" FROM ", StringComparison.Ordinal)];
		List<string> aliases = [.. AliasPattern().Matches(selectList).Select(m => m.Groups["alias"].Value)];
		CollectionAssert.AreEquivalent(values.Keys.ToList(), aliases, "The test's values must name every column the query selects.");

		using DataTable table = new();
		foreach (string alias in aliases)
		{
			table.Columns.Add(alias, values[alias].GetType());
		}

		table.Rows.Add([.. aliases.Select(a => values[a])]);
		using DataTableReader reader = table.CreateDataReader();
		return reader.Parse<T>().Single();
	}

	[GeneratedRegex(@"\bAS\s+`?(?<alias>\w+)`?", RegexOptions.IgnoreCase)]
	private static partial Regex AliasPattern();
}
