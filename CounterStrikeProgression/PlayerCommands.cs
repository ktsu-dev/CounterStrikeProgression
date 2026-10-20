// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The chat and console commands: <c>!rank</c> (also <c>!xp</c> and <c>!progress</c>), <c>!contracts</c>, <c>!top</c> and
/// <c>!top &lt;weapon&gt;</c>, <c>!next</c>, <c>!feats</c>, <c>!stats</c>, <c>!site</c>, and the admin-only
/// <c>css_csp_status</c>.
/// </summary>
/// <param name="services">The shared plugin pieces.</param>
/// <param name="moduleName">The plugin's name, for the status line.</param>
/// <param name="moduleVersion">The plugin's version, for the status line.</param>
internal sealed class PlayerCommands(ProgressionServices services, string moduleName, string moduleVersion)
{
	private const int LeaderboardSize = 10;

	// Each !top is a database query, so a player can ask at most this often.
	private const double LeaderboardCooldownSeconds = 10;

	/// <summary>
	/// Adds the commands.
	/// </summary>
	/// <param name="plugin">The plugin to register with.</param>
	public void Register(BasePlugin plugin)
	{
		Ensure.NotNull(plugin);
		plugin.AddCommand("css_rank", "Open or close your progress panel", OnRank);
		plugin.AddCommand("css_xp", "Open or close your progress panel", OnRank);
		plugin.AddCommand("css_progress", "Open or close your progress panel", OnRank);
		plugin.AddCommand("css_contracts", "Open your progress panel on today's contracts", OnContracts);
		plugin.AddCommand("css_top", "Show the players with the most rank XP, or with a weapon: !top <weapon>", OnTop);
		plugin.AddCommand("css_next", "Show your three challenges closest to their next tier", OnNext);
		plugin.AddCommand("css_feats", "Show the feats and how close you are to each", OnFeats);
		plugin.AddCommand("css_stats", "Show your stats, or a connected player's: !stats <name>", OnStats);
		plugin.AddCommand("css_site", "Show the progression site's address", OnSite);
		plugin.AddCommand("css_csp_status", "Show progression storage and session diagnostics", OnStatus);
	}

	private void OnRank(CCSPlayerController? player, CommandInfo command)
	{
		if ((player is not null) && (services.Sessions.Get(player) is PlayerSession session))
		{
			services.Panel.Toggle(player, session);
		}
	}

	private void OnContracts(CCSPlayerController? player, CommandInfo command)
	{
		if ((player is not null) && (services.Sessions.Get(player) is PlayerSession session))
		{
			services.Panel.Toggle(player, session, PanelBuilder.ContractsPage);
		}
	}

	private void OnTop(CCSPlayerController? player, CommandInfo command)
	{
		if ((player is null) || !player.IsValid || (services.Sessions.Get(player) is not PlayerSession session))
		{
			return;
		}

		double now = Server.CurrentTime;
		if (now < session.NextLeaderboardAllowed)
		{
			return;
		}

		string query = command.ArgString.Trim();
		WeaponInfo? weapon = null;
		if (query.Length > 0)
		{
			weapon = WeaponCatalog.Search(query);
			if (weapon is null)
			{
				Reply(player, $"No single weapon matches \"{query}\". Try a name like ak47, awp, or deagle.");
				return;
			}
		}

		session.NextLeaderboardAllowed = now + LeaderboardCooldownSeconds;
		int slot = player.Slot;
		ulong steamId = player.SteamID;
		_ = weapon is null
			? Task.Run(() => ShowTopAsync(slot, steamId))
			: Task.Run(() => ShowWeaponTopAsync(slot, steamId, weapon));
	}

	private async Task ShowWeaponTopAsync(int slot, ulong steamId, WeaponInfo weapon)
	{
		IReadOnlyList<WeaponLeaderboardEntry> top;
		try
		{
			top = await services.Sessions.Store.TopByWeaponAsync(weapon.Key, LeaderboardSize, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (SessionManager.IsStorageFailure(exception))
		{
			Log.LeaderboardFailed(services.Logger, exception);
			return;
		}

		await Server.NextFrameAsync(() =>
		{
			CCSPlayerController? viewer = Utilities.GetPlayerFromSlot(slot);
			if ((viewer is null) || !viewer.IsValid || (viewer.SteamID != steamId))
			{
				return;
			}

			if (top.Count == 0)
			{
				Reply(viewer, $"Nobody has XP with the {weapon.DisplayName} yet.");
				return;
			}

			Reply(viewer, $"Top {weapon.DisplayName} players");
			for (int i = 0; i < top.Count; i++)
			{
				viewer.PrintToChat($" {ChatColors.Yellow}{services.Reports.WeaponLeader(i + 1, top[i])}");
			}
		}).ConfigureAwait(false);
	}

	private void OnNext(CCSPlayerController? player, CommandInfo command)
	{
		if (LoadedProgress(player) is not PlayerProgress progress)
		{
			return;
		}

		Reply(player!, "Closest tiers");
		foreach (string line in services.Reports.Next(progress))
		{
			player!.PrintToChat($" {ChatColors.Yellow}{line}");
		}
	}

	private void OnFeats(CCSPlayerController? player, CommandInfo command)
	{
		if (LoadedProgress(player) is not PlayerProgress progress)
		{
			return;
		}

		IReadOnlyList<string> lines = services.Reports.Feats(progress);
		Reply(player!, lines[0]);
		foreach (string line in lines.Skip(1))
		{
			char color = line.StartsWith('+') ? ChatColors.Green : ChatColors.Grey;
			player!.PrintToChat($" {color}{line}");
		}
	}

	private void OnStats(CCSPlayerController? player, CommandInfo command)
	{
		if (LoadedProgress(player) is not PlayerProgress own)
		{
			return;
		}

		string query = command.ArgString.Trim();
		if (query.Length == 0)
		{
			Reply(player!, services.Reports.Stats(player!.PlayerName, own));
			return;
		}

		// Only connected players, whose progress is already in memory, so a lookup never waits on the database.
		IEnumerable<(string Name, PlayerProgress Progress)> online = Utilities.GetPlayers()
			.Select(other => (other.PlayerName, services.Sessions.Get(other)?.Progress))
			.Where(o => o.Progress is not null)
			.Select(o => (o.PlayerName, o.Progress!));

		foreach (string line in services.Reports.StatsFor(online, query, services.Config.Site.PublicUrl))
		{
			Reply(player!, line);
		}
	}

	private void OnSite(CCSPlayerController? player, CommandInfo command)
	{
		if (player is null)
		{
			return;
		}

		Reply(player, ChatReports.SiteLine(services.Config.Site.PublicUrl, site => $"{ChatColors.LightBlue}{site}{ChatColors.Default}")
			?? "This server has no progression site set up.");
	}

	private PlayerProgress? LoadedProgress(CCSPlayerController? player)
	{
		if ((player is null) || !player.IsValid || (services.Sessions.Get(player) is not PlayerSession session))
		{
			return null;
		}

		if (session.Progress is null)
		{
			Reply(player, "Your progress is still loading.");
		}

		return session.Progress;
	}

	private void Reply(CCSPlayerController player, string text) =>
		player.PrintToChat($" {ChatColors.Gold}{services.Config.ChatPrefix}{ChatColors.Default} {text}");

	private async Task ShowTopAsync(int slot, ulong steamId)
	{
		IReadOnlyList<LeaderboardEntry> top;
		try
		{
			top = await services.Sessions.Store.TopAsync(LeaderboardSize, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (SessionManager.IsStorageFailure(exception))
		{
			Log.LeaderboardFailed(services.Logger, exception);
			return;
		}

		await Server.NextFrameAsync(() =>
		{
			CCSPlayerController? viewer = Utilities.GetPlayerFromSlot(slot);
			if ((viewer is null) || !viewer.IsValid || (viewer.SteamID != steamId))
			{
				return;
			}

			viewer.PrintToChat($" {ChatColors.Gold}{services.Config.ChatPrefix}{ChatColors.Default} Top players");
			for (int i = 0; i < top.Count; i++)
			{
				LeaderboardEntry entry = top[i];
				string rank = services.Reports.RankName(entry.Rank, entry.RankXp);
				viewer.PrintToChat(string.Format(
					CultureInfo.InvariantCulture,
					" {0}{1}.{2} {3} {4}{5}{2} ({6:N0} XP)",
					ChatColors.Yellow,
					i + 1,
					ChatColors.Default,
					entry.Name,
					ChatColors.Gold,
					rank,
					entry.RankXp));
			}
		}).ConfigureAwait(false);
	}

	private void OnStatus(CCSPlayerController? player, CommandInfo command)
	{
		if ((player is not null) && !AdminManager.PlayerHasPermissions(player, "@css/root"))
		{
			command.ReplyToCommand("You need @css/root to use this.");
			return;
		}

		List<PlayerSession> all = [.. services.Sessions.All];
		command.ReplyToCommand(string.Format(
			CultureInfo.InvariantCulture,
			"[{0}] v{1} store={2} sessions={3} loaded={4} panelsOpen={5} warmup={6} ffa={7}",
			moduleName,
			moduleVersion,
			services.Sessions.Store.GetType().Name,
			all.Count,
			all.Count(s => s.Progress is not null),
			all.Count(s => s.Panel.IsOpen),
			GameState.IsWarmup,
			GameState.TeammatesAreEnemies));
	}
}
