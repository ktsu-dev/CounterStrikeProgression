// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Panel;

/// <summary>
/// The root-only corrections: <c>css_csp_grant &lt;player&gt; &lt;xp&gt;</c> adds or takes away rank XP, and
/// <c>css_csp_reset &lt;player&gt;</c> clears everything a player has earned. Both are logged, and both can name a
/// bot persona as <c>bot:&lt;name&gt;</c>.
/// </summary>
/// <remarks>
/// <see cref="CorrectionTargets"/> decides who a command names. A reset can't be undone, so it runs only when
/// the same admin repeats it for the same player within <see cref="ResetConfirmSeconds"/>.
/// </remarks>
/// <param name="services">The shared plugin pieces.</param>
internal sealed class AdminCommands(ProgressionServices services)
{
	private const string RootPermission = "@css/root";
	private const double ResetConfirmSeconds = 15;

	private readonly ResetConfirmation resetConfirmation = new(ResetConfirmSeconds);

	/// <summary>
	/// Adds the commands.
	/// </summary>
	/// <param name="plugin">The plugin to register with.</param>
	public void Register(BasePlugin plugin)
	{
		Ensure.NotNull(plugin);
		plugin.AddCommand("css_csp_grant", "Add rank XP to a player, or take it away with a negative amount: css_csp_grant <player> <xp>", OnGrant);
		plugin.AddCommand("css_csp_reset", "Clear all of a player's progress: css_csp_reset <player>", OnReset);
	}

	private void OnGrant(CCSPlayerController? admin, CommandInfo command)
	{
		if (!IsRoot(admin, command))
		{
			return;
		}

		if ((command.ArgCount < 3) || !AdminCorrection.TryParseGrant(command.GetArg(2), out AdminCorrection? grant) || (grant is null))
		{
			command.ReplyToCommand("Usage: css_csp_grant <#userid|steamid64|bot:<name>|name> <xp>, where a negative amount takes XP away.");
			return;
		}

		if (FindTarget(command) is TargetResolution target)
		{
			Run(admin, command, grant, target);
		}
	}

	private void OnReset(CCSPlayerController? admin, CommandInfo command)
	{
		if (!IsRoot(admin, command))
		{
			return;
		}

		if (command.ArgCount < 2)
		{
			command.ReplyToCommand("Usage: css_csp_reset <#userid|steamid64|bot:<name>|name>");
			return;
		}

		if (FindTarget(command) is not TargetResolution target)
		{
			return;
		}

		if (!resetConfirmation.Confirm(admin?.SteamID ?? 0, target.SteamId, Server.CurrentTime))
		{
			command.ReplyToCommand(resetConfirmation.Warning(target.Name, target.SteamId));
			return;
		}

		Run(admin, command, new AdminCorrection(CorrectionKind.Reset), target);
	}

	private void Run(CCSPlayerController? admin, CommandInfo command, AdminCorrection correction, TargetResolution target)
	{
		string adminName = admin?.PlayerName ?? "Console";
		ulong adminId = admin?.SteamID ?? 0;
		int? adminSlot = admin?.Slot;
		(ulong steamId, string name) = (target.SteamId, target.Name);

		bool started = services.Sessions.Correct(steamId, correction, services.Engine.Ranks, outcome =>
		{
			if (outcome is null)
			{
				Log.CorrectionFailed(services.Logger, adminName, adminId, correction.Description, steamId);
				ReplyLater(adminSlot, adminId, correction.FailedMessage(name, steamId));
				return;
			}

			Log.CorrectionApplied(services.Logger, adminName, adminId, correction.Description, steamId, outcome.XpBefore, outcome.XpAfter, outcome.RankBefore, outcome.RankAfter, outcome.WeaponsCleared);
			services.Site?.Corrected(steamId);
			ReplyLater(adminSlot, adminId, correction.DoneMessage(name, steamId, outcome));
			TellTarget(steamId, correction.TargetMessage(outcome));
		});

		command.ReplyToCommand(started
			? $"Applying {correction.Description} for {name} ({steamId})..."
			: $"{name}'s progress is still loading, so try again in a moment.");
	}

	// A bot persona is never told, because nothing is sent to a bot.
	private void TellTarget(ulong steamId, string text)
	{
		if (BotIds.IsBot(steamId))
		{
			return;
		}

		foreach (CCSPlayerController player in Utilities.GetPlayers().Where(p => GameState.IsHuman(p) && (p.SteamID == steamId)))
		{
			if (services.Sessions.Get(player) is PlayerSession session)
			{
				PanelPresenter.Invalidate(session);
				player.PrintToChat($" {ChatColors.Gold}{services.Config.ChatPrefix}{ChatColors.Default} {text}");
			}
		}
	}

	private static void ReplyLater(int? adminSlot, ulong adminId, string text)
	{
		if (adminSlot is not int slot)
		{
			Server.PrintToConsole(text);
			return;
		}

		CCSPlayerController? admin = Utilities.GetPlayerFromSlot(slot);
		if ((admin is not null) && admin.IsValid && (admin.SteamID == adminId))
		{
			admin.PrintToChat(text);
			admin.PrintToConsole(text);
		}
	}

	private static bool IsRoot(CCSPlayerController? admin, CommandInfo command)
	{
		if ((admin is not null) && !AdminManager.PlayerHasPermissions(admin, RootPermission))
		{
			command.ReplyToCommand($"You need {RootPermission} to use this.");
			return false;
		}

		return true;
	}

	// Every connected human is a candidate, and a bot only while it's tracked, under its persona's id.
	private TargetResolution? FindTarget(CommandInfo command)
	{
		TargetCandidate[] players = [.. Utilities.GetPlayers()
			.Where(GameState.IsTracked)
			.Select(p => new TargetCandidate(p.IsBot ? services.Sessions.Get(p)?.SteamId ?? 0 : p.SteamID, p.UserId ?? -1, p.PlayerName ?? string.Empty))
			.Where(c => c.SteamId != 0)];
		TargetResolution target = CorrectionTargets.Resolve(command.GetArg(1), players);
		if (!target.Found)
		{
			command.ReplyToCommand(target.Error!);
			return null;
		}

		return target;
	}
}
