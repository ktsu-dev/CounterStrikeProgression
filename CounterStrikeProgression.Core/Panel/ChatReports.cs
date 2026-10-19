// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Panel;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The plain text behind the chat commands: the welcome on join, <c>!next</c>, <c>!feats</c>, <c>!stats</c>,
/// and <c>!top &lt;weapon&gt;</c>. The plugin adds the prefix and colors.
/// </summary>
/// <param name="engine">The engine whose ranks, levels, ladders, and feats to report.</param>
internal sealed class ChatReports(ProgressionEngine engine)
{
	/// <summary>How many tiers <c>!next</c> lists.</summary>
	public const int NextTiersShown = 3;

	private readonly PanelBuilder panel = new(engine);

	/// <summary>
	/// Gets a player's rank name, which never drops below the stored rank.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The rank name, such as <c>Gold Nova · II</c>.</returns>
	public string RankName(PlayerProgress player)
	{
		Ensure.NotNull(player);
		return RankName(player.Rank, player.RankXp);
	}

	/// <summary>
	/// Gets a rank name from a stored rank and XP, which never drops below the stored rank.
	/// </summary>
	/// <param name="rank">The stored rank index.</param>
	/// <param name="rankXp">The rank XP.</param>
	/// <returns>The rank name.</returns>
	public string RankName(int rank, long rankXp) =>
		RankLadder.Names[Math.Clamp(Math.Max(rank, engine.Ranks.RankFor(rankXp)), 0, RankLadder.TopRank)];

	/// <summary>
	/// Builds the welcome line for a player joining.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="player">The player's progress.</param>
	/// <returns>The line.</returns>
	public string Welcome(string name, PlayerProgress player)
	{
		Ensure.NotNull(player);
		return player.RankXp <= 0
			? $"Welcome, {name}! Every weapon earns XP toward your rank, its own levels, and its challenge ladder."
			: $"Welcome back, {name}! You're {RankName(player)} with {Number(player.RankXp)} XP.";
	}

	/// <summary>
	/// Builds the lines a player gets on joining: the welcome, a link to their page when the site has a public
	/// address, and the commands.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="player">The player's progress.</param>
	/// <param name="siteUrl">The site's public address, or empty for none.</param>
	/// <returns>The lines.</returns>
	public IReadOnlyList<string> WelcomeLines(string name, PlayerProgress player, string? siteUrl)
	{
		Ensure.NotNull(player);
		List<string> lines = [Welcome(name, player)];
		if (player.WelcomeBackXp > 0)
		{
			lines.Add($"Welcome-back bonus: your next {Number((long)Math.Ceiling(player.WelcomeBackXp))} rank XP counts double.");
		}

		if (PlayerPage(siteUrl, player.SteamId) is string page)
		{
			lines.Add($"Your progress page: {page}");
		}

		lines.Add("Type !rank for your panel. More: !next !top !feats !stats !site");
		return lines;
	}

	/// <summary>
	/// Builds <c>!stats &lt;name&gt;</c>: finds a player by exact name, then by part of a name, ignoring case.
	/// </summary>
	/// <param name="players">The players to search, with their names.</param>
	/// <param name="query">What was typed.</param>
	/// <param name="siteUrl">The site's public address, or empty for none.</param>
	/// <returns>The stats line and a link to their page, or a line saying why no single player matched.</returns>
	public IReadOnlyList<string> StatsFor(IEnumerable<(string Name, PlayerProgress Progress)> players, string query, string? siteUrl)
	{
		Ensure.NotNull(players);
		Ensure.NotNull(query);
		List<(string Name, PlayerProgress Progress)> all = [.. players];
		List<(string Name, PlayerProgress Progress)> matches = [.. all.Where(p => string.Equals(p.Name, query, StringComparison.OrdinalIgnoreCase))];
		if (matches.Count == 0)
		{
			matches = [.. all.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
		}

		switch (matches.Count)
		{
			case 0:
				return [$"No connected player's name contains \"{query}\"."];

			case 1:
				List<string> lines = [Stats(matches[0].Name, matches[0].Progress)];
				if (PlayerPage(siteUrl, matches[0].Progress.SteamId) is string page)
				{
					lines.Add(page);
				}

				return lines;

			default:
				return [$"Several players match: {string.Join(", ", matches.Take(5).Select(m => m.Name))}. Type more of the name."];
		}
	}

	/// <summary>
	/// Builds <c>!next</c>: the challenges closest to their next tier.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>One line per tier, or one line saying there are none yet.</returns>
	public IReadOnlyList<string> Next(PlayerProgress player)
	{
		Ensure.NotNull(player);
		List<string> lines = [.. panel.ClosestTiers(player, NextTiersShown).Select(l => $"{l.Label}  {l.Detail}")];
		return lines.Count > 0 ? lines : ["Nothing in progress yet. A kill with any weapon starts its ladder."];
	}

	/// <summary>
	/// Builds <c>!feats</c>: a count, then each feat, earned or with the player's progress toward it.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The lines.</returns>
	public IReadOnlyList<string> Feats(PlayerProgress player)
	{
		Ensure.NotNull(player);
		IReadOnlyList<(FeatDefinition Feat, FeatProgress Progress)> measured = engine.Feats.Measure(player);
		List<string> lines = [$"Feats earned: {measured.Count(m => m.Progress.IsEarned)} of {measured.Count}"];
		lines.AddRange(measured.Select(m => m.Progress.IsEarned
			? $"+ {m.Feat.Name}: {m.Feat.Description}"
			: $"o {m.Feat.Name}: {m.Feat.Description} ({Number(Math.Min(m.Progress.Current, m.Progress.Target))}/{Number(m.Progress.Target)})"));
		return lines;
	}

	/// <summary>
	/// Builds <c>!stats</c>: rank, XP, kills, headshot share, best weapon, feats, and the player's nemesis and favourite victim.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="player">The player's progress.</param>
	/// <returns>The line.</returns>
	public string Stats(string name, PlayerProgress player)
	{
		Ensure.NotNull(player);
		long kills = player.Weapons.Values.Sum(w => w.Kills);
		long headshots = player.Weapons.Values.Sum(w => w.Headshots);
		string headshotShare = kills > 0 ? $", {Math.Round(100.0 * headshots / kills).ToString(CultureInfo.InvariantCulture)}% headshots" : string.Empty;

		string best = string.Empty;
		WeaponProgress? top = player.Weapons.Values.Where(w => w.Xp > 0).OrderByDescending(w => w.Xp).FirstOrDefault();
		if ((top is not null) && (WeaponCatalog.Find(top.Weapon) is { } weapon))
		{
			best = $". Best weapon {weapon.DisplayName}, level {Math.Max(top.Level, engine.Levels.LevelFor(top.Xp))}";
		}

		int feats = engine.Feats.Earned(player).Count();
		return $"{name}: {RankName(player)}, {Number(player.RankXp)} XP. {Number(kills)} kills{headshotShare}{best}. Feats {feats} of {FeatCatalog.All.Count}.{Rivals(player)}";
	}

	/// <summary>
	/// Builds the rivals part of <c>!stats</c>, with each head-to-head written as the player's kills first.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The text, starting with a space, or empty when the player has neither a nemesis nor a victim.</returns>
	public string Rivals(PlayerProgress player)
	{
		Ensure.NotNull(player);
		IReadOnlyList<HeadToHead> rows = Rivalry.Of(player);
		int minimum = engine.Settings.NemesisMinimumKills;
		string text = string.Empty;
		if (Rivalry.Nemesis(rows, minimum) is { } nemesis)
		{
			text += $" Nemesis {NameOf(nemesis)} ({Number(nemesis.Kills)}-{Number(nemesis.Deaths)}).";
		}

		if (Rivalry.Victim(rows, minimum) is { } victim)
		{
			text += $" Favourite victim {NameOf(victim)} ({Number(victim.Kills)}-{Number(victim.Deaths)}).";
		}

		return text;
	}

	/// <summary>
	/// Builds one line of <c>!top &lt;weapon&gt;</c>.
	/// </summary>
	/// <param name="place">The place, starting at 1.</param>
	/// <param name="entry">The row.</param>
	/// <returns>The line.</returns>
	public string WeaponLeader(int place, WeaponLeaderboardEntry entry)
	{
		Ensure.NotNull(entry);
		return $"{place}. {entry.Name} level {Math.Max(entry.Level, engine.Levels.LevelFor(entry.Xp))} ({Number(entry.Xp)} XP)";
	}

	/// <summary>
	/// Builds the link to a player's page on the progression site.
	/// </summary>
	/// <param name="siteUrl">The site's public address, such as <c>https://cs2.example.com</c>.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <returns>The link, or <see langword="null"/> when no site address is set or it isn't an http or https URL.</returns>
	public static string? PlayerPage(string? siteUrl, ulong steamId)
	{
		if (string.IsNullOrWhiteSpace(siteUrl)
			|| !Uri.TryCreate(siteUrl.Trim(), UriKind.Absolute, out Uri? site)
			|| ((site.Scheme != Uri.UriSchemeHttp) && (site.Scheme != Uri.UriSchemeHttps)))
		{
			return null;
		}

		return $"{site.GetLeftPart(UriPartial.Path).TrimEnd('/')}/players/{steamId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

	private static string NameOf(HeadToHead rival) => string.IsNullOrWhiteSpace(rival.Name) ? "an unknown player" : rival.Name;
}
