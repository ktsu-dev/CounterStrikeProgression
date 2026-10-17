// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Collections.Frozen;
using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>One player's standing with one weapon.</summary>
/// <param name="Level">The weapon level.</param>
/// <param name="Xp">The XP earned with the weapon.</param>
/// <param name="Kills">Kills with the weapon.</param>
internal readonly record struct WeaponTally(int Level, long Xp, long Kills);

/// <summary>What the leaderboards need to know about one player.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="RankXp">The player's rank XP, which breaks ties.</param>
/// <param name="Weapons">Each tracked weapon the player has used, keyed by weapon key.</param>
/// <param name="SkillShots">How many times the player has earned each skill shot across every weapon, keyed by skill shot id. Only skill shots earned at least once.</param>
/// <param name="Feats">The ids of the feats the player has earned.</param>
internal sealed record PlayerTally(
	ulong SteamId,
	string Name,
	long RankXp,
	IReadOnlyDictionary<string, WeaponTally> Weapons,
	IReadOnlyDictionary<string, long> SkillShots,
	IReadOnlyList<string> Feats)
{
	private static readonly FrozenSet<string> SkillShotIds = SkillShotCatalog.All.Select(s => s.Id.WeakString).ToFrozenSet(StringComparer.Ordinal);

	/// <summary>
	/// Tallies a player's progress.
	/// </summary>
	/// <param name="player">The player's merged progress.</param>
	/// <param name="settings">The plugin's settings, for weapon levels and feats.</param>
	/// <returns>The tally.</returns>
	public static PlayerTally From(PlayerProgress player, ProgressionSettings settings)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(settings);

		WeaponLevelCurve curve = new(settings.WeaponLevelBaseXp, settings.WeaponLevelStepXp, settings.WeaponMaxLevel);
		Dictionary<string, WeaponTally> weapons = new(StringComparer.Ordinal);
		Dictionary<string, long> skillShots = new(StringComparer.Ordinal);
		foreach (WeaponProgress weapon in player.Weapons.Values)
		{
			if (WeaponCatalog.Find(weapon.Weapon) is null)
			{
				continue;
			}

			int level = Math.Min(curve.MaxLevel, Math.Max(weapon.Level, curve.LevelFor(weapon.Xp)));
			weapons[weapon.Weapon.WeakString] = new(level, weapon.Xp, weapon.Kills);
			foreach ((string id, ChallengeProgress challenge) in weapon.Challenges)
			{
				if ((challenge.Count > 0) && SkillShotIds.Contains(id))
				{
					skillShots[id] = skillShots.GetValueOrDefault(id) + challenge.Count;
				}
			}
		}

		return new(player.SteamId, player.Name, player.RankXp, weapons, skillShots, [.. new FeatCatalog(settings).Earned(player).Select(f => f.Id)]);
	}
}

/// <summary>One row of a leaderboard.</summary>
/// <param name="Position">The place on the board, from 1.</param>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Value">The number the board ranks by: a weapon level, a skill shot count, or a feat count.</param>
/// <param name="Xp">On a weapon board, the XP with the weapon, which breaks ties between levels. Otherwise 0.</param>
/// <param name="Kills">On a weapon board, kills with the weapon. Otherwise 0.</param>
internal sealed record BoardRow(int Position, string SteamId, string Name, long Value, long Xp, long Kills);

/// <summary>One weapon's leaderboard, by level.</summary>
/// <param name="Key">The weapon key.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Class">The weapon family.</param>
/// <param name="Players">How many players have used the weapon.</param>
/// <param name="Rows">The top players, highest level first.</param>
internal sealed record WeaponBoard(string Key, string Name, WeaponClass Class, int Players, IReadOnlyList<BoardRow> Rows);

/// <summary>One skill shot's leaderboard, by how many times each player has earned it.</summary>
/// <param name="Id">The skill shot id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Rarity">How hard it is.</param>
/// <param name="Players">How many players have earned it.</param>
/// <param name="Total">How many times it has been earned on the server.</param>
/// <param name="Rows">The top players, most earned first.</param>
internal sealed record SkillShotBoard(string Id, string Name, Rarity Rarity, int Players, long Total, IReadOnlyList<BoardRow> Rows);

/// <summary>The leaderboards page.</summary>
/// <param name="Updated">When the boards were last worked out for every player, or <see langword="null"/> while the first pass runs.</param>
/// <param name="Feats">The players with the most feats.</param>
/// <param name="Rarest">The skill shots earned by the fewest players, rarest first. Skill shots nobody has earned yet are left out.</param>
/// <param name="Weapons">Every weapon's board, in catalog order.</param>
/// <param name="SkillShots">Every skill shot's board, in catalog order.</param>
internal sealed record LeaderboardsView(
	DateTimeOffset? Updated,
	IReadOnlyList<BoardRow> Feats,
	IReadOnlyList<SkillShotBoard> Rarest,
	IReadOnlyList<WeaponBoard> Weapons,
	IReadOnlyList<SkillShotBoard> SkillShots);

/// <summary>
/// Builds the leaderboards from every player's tally: each weapon by level, each skill shot by count, the most
/// feats, and the rarest skill shots.
/// </summary>
internal static class LeaderboardViewBuilder
{
	/// <summary>How many players each board lists.</summary>
	public const int BoardSize = 10;

	/// <summary>How many skill shots the rarest board lists.</summary>
	public const int RarestSize = 10;

	/// <summary>
	/// Builds the leaderboards.
	/// </summary>
	/// <param name="tallies">Every player's tally.</param>
	/// <param name="updated">When the tallies were last rebuilt, or <see langword="null"/> before the first rebuild.</param>
	/// <returns>The boards.</returns>
	public static LeaderboardsView Build(IReadOnlyList<PlayerTally> tallies, DateTimeOffset? updated)
	{
		Ensure.NotNull(tallies);

		List<WeaponBoard> weapons = [];
		foreach (WeaponInfo weapon in WeaponCatalog.All)
		{
			string key = weapon.Key.WeakString;
			List<(PlayerTally Player, WeaponTally Weapon)> users = [.. tallies
				.Where(t => t.Weapons.TryGetValue(key, out WeaponTally w) && ((w.Xp > 0) || (w.Kills > 0)))
				.Select(t => (t, t.Weapons[key]))];
			IEnumerable<BoardRow> rows = users
				.OrderByDescending(u => u.Weapon.Level)
				.ThenByDescending(u => u.Weapon.Xp)
				.ThenByDescending(u => u.Weapon.Kills)
				.ThenBy(u => u.Player.SteamId)
				.Take(BoardSize)
				.Select((u, i) => Row(i, u.Player, u.Weapon.Level, u.Weapon.Xp, u.Weapon.Kills));
			weapons.Add(new(key, weapon.DisplayName, weapon.Class, users.Count, [.. rows]));
		}

		List<SkillShotBoard> skillShots = [.. SkillShotCatalog.All.Select(s => SkillShotBoardFor(s, tallies))];

		IReadOnlyList<BoardRow> feats =
		[
			.. tallies
				.Where(t => t.Feats.Count > 0)
				.OrderByDescending(t => t.Feats.Count)
				.ThenByDescending(t => t.RankXp)
				.ThenBy(t => t.SteamId)
				.Take(BoardSize)
				.Select((t, i) => Row(i, t, t.Feats.Count)),
		];

		IReadOnlyList<SkillShotBoard> rarest =
		[
			.. skillShots
				.Select((board, index) => (board, index))
				.Where(s => s.board.Players > 0)
				.OrderBy(s => s.board.Players)
				.ThenBy(s => s.board.Total)
				.ThenBy(s => s.index)
				.Take(RarestSize)
				.Select(s => s.board with { Rows = [] }),
		];

		return new(updated, feats, rarest, weapons, skillShots);
	}

	/// <summary>
	/// Counts how many players have earned each skill shot, and how many times in all.
	/// </summary>
	/// <param name="tallies">Every player's tally.</param>
	/// <returns>The player count and total for each skill shot that has been earned, keyed by skill shot id.</returns>
	public static IReadOnlyDictionary<string, (int Players, long Total)> Earned(IReadOnlyList<PlayerTally> tallies)
	{
		Ensure.NotNull(tallies);

		Dictionary<string, (int Players, long Total)> earned = new(StringComparer.Ordinal);
		foreach (PlayerTally tally in tallies)
		{
			foreach ((string id, long count) in tally.SkillShots)
			{
				(int players, long total) = earned.GetValueOrDefault(id);
				earned[id] = (players + 1, total + count);
			}
		}

		return earned;
	}

	private static SkillShotBoard SkillShotBoardFor(SkillShotDefinition skillShot, IReadOnlyList<PlayerTally> tallies)
	{
		string id = skillShot.Id.WeakString;
		List<(PlayerTally Player, long Count)> earners = [.. tallies
			.Where(t => t.SkillShots.ContainsKey(id))
			.Select(t => (t, t.SkillShots[id]))];
		IEnumerable<BoardRow> rows = earners
			.OrderByDescending(e => e.Count)
			.ThenBy(e => e.Player.SteamId)
			.Take(BoardSize)
			.Select((e, i) => Row(i, e.Player, e.Count));
		return new(id, skillShot.Name, skillShot.Rarity, earners.Count, earners.Sum(e => e.Count), [.. rows]);
	}

	private static BoardRow Row(int index, PlayerTally player, long value, long xp = 0, long kills = 0) =>
		new(index + 1, player.SteamId.ToString(CultureInfo.InvariantCulture), player.Name, value, xp, kills);
}
