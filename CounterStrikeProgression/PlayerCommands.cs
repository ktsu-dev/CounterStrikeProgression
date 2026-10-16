// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// The chat and console commands: <c>!rank</c> (also <c>!xp</c> and <c>!progress</c>), <c>!top</c>, and the
/// admin-only <c>css_csp_status</c>.
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
		plugin.AddCommand("css_top", "Show the players with the most rank XP", OnTop);
		plugin.AddCommand("css_csp_status", "Show progression storage and session diagnostics", OnStatus);
	}

	private void OnRank(CCSPlayerController? player, CommandInfo command)
	{
		if ((player is not null) && (services.Sessions.Get(player) is PlayerSession session))
		{
			services.Panel.Toggle(player, session);
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

		session.NextLeaderboardAllowed = now + LeaderboardCooldownSeconds;
		int slot = player.Slot;
		ulong steamId = player.SteamID;
		_ = Task.Run(() => ShowTopAsync(slot, steamId));
	}

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
				string rank = RankLadder.Names[Math.Clamp(entry.Rank, 0, RankLadder.TopRank)];
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
