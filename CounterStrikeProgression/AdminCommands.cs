// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Panel;

/// <summary>
/// The root-only corrections: <c>css_csp_grant &lt;player&gt; &lt;xp&gt;</c> adds or takes away rank XP, and
/// <c>css_csp_reset &lt;player&gt;</c> clears everything a player has earned. Both are logged.
/// </summary>
/// <remarks>
/// A player is named by <c>#userid</c>, by a SteamID64 (which works for players who aren't connected), or by
/// part of their name when that matches one player only. A reset can't be undone, so it runs only when the
/// same admin repeats it for the same player within <see cref="ResetConfirmSeconds"/>.
/// </remarks>
/// <param name="services">The shared plugin pieces.</param>
internal sealed class AdminCommands(ProgressionServices services)
{
	private const string RootPermission = "@css/root";
	private const double ResetConfirmSeconds = 15;
	private const ulong MinSteamId = 76561197960265729;
	private const ulong MaxSteamId = 76561202255233023;

	// The admin's Steam ID (0 for the console), the target, and when the confirmation runs out.
	private (ulong Admin, ulong Target, double Until) pendingReset;

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

		if ((command.ArgCount < 3) || !long.TryParse(command.GetArg(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long xp) || (xp == 0))
		{
			command.ReplyToCommand("Usage: css_csp_grant <#userid|steamid64|name> <xp>, where a negative amount takes XP away.");
			return;
		}

		if (FindTarget(command.GetArg(1), command) is (ulong steamId, string name))
		{
			Run(admin, command, new AdminCorrection(CorrectionKind.Grant, xp), steamId, name);
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
			command.ReplyToCommand("Usage: css_csp_reset <#userid|steamid64|name>");
			return;
		}

		if (FindTarget(command.GetArg(1), command) is not (ulong steamId, string name))
		{
			return;
		}

		ulong adminId = admin?.SteamID ?? 0;
		double now = Server.CurrentTime;
		if ((pendingReset.Admin != adminId) || (pendingReset.Target != steamId) || (now > pendingReset.Until))
		{
			pendingReset = (adminId, steamId, now + ResetConfirmSeconds);
			command.ReplyToCommand(string.Format(
				CultureInfo.InvariantCulture,
				"This deletes every rank, weapon, and ladder row {0} ({1}) has, and can't be undone. Run the same command again within {2:0} seconds to confirm.",
				name,
				steamId,
				ResetConfirmSeconds));
			return;
		}

		pendingReset = default;
		Run(admin, command, new AdminCorrection(CorrectionKind.Reset), steamId, name);
	}

	private void Run(CCSPlayerController? admin, CommandInfo command, AdminCorrection correction, ulong steamId, string name)
	{
		string adminName = admin?.PlayerName ?? "Console";
		ulong adminId = admin?.SteamID ?? 0;
		int? adminSlot = admin?.Slot;
		string description = correction.Kind == CorrectionKind.Reset
			? "reset"
			: string.Format(CultureInfo.InvariantCulture, "grant {0:+#;-#} XP", correction.Xp);

		bool started = services.Sessions.Correct(steamId, correction, services.Engine.Ranks, outcome =>
		{
			if (outcome is null)
			{
				Log.CorrectionFailed(services.Logger, adminName, adminId, description, steamId);
				ReplyLater(adminSlot, adminId, $"Couldn't {description} for {name} ({steamId}): they have no stored progress, or the database write failed. Nothing was changed in the database.");
				return;
			}

			Log.CorrectionApplied(services.Logger, adminName, adminId, description, steamId, outcome.XpBefore, outcome.XpAfter, outcome.RankBefore, outcome.RankAfter, outcome.WeaponsCleared);
			services.Site?.Corrected(steamId);
			ReplyLater(adminSlot, adminId, string.Format(
				CultureInfo.InvariantCulture,
				"Done: {0} for {1} ({2}). Rank XP {3:N0} -> {4:N0}, rank {5} -> {6}.",
				description,
				name,
				steamId,
				outcome.XpBefore,
				outcome.XpAfter,
				RankName(outcome.RankBefore),
				RankName(outcome.RankAfter)));
			TellTarget(steamId, correction, outcome);
		});

		command.ReplyToCommand(started
			? $"Applying {description} for {name} ({steamId})..."
			: $"{name}'s progress is still loading, so try again in a moment.");
	}

	private void TellTarget(ulong steamId, AdminCorrection correction, CorrectionOutcome outcome)
	{
		foreach (CCSPlayerController player in Utilities.GetPlayers())
		{
			if ((player.SteamID != steamId) || (services.Sessions.Get(player) is not PlayerSession session))
			{
				continue;
			}

			PanelPresenter.Invalidate(session);
			string text = correction.Kind == CorrectionKind.Reset
				? "An admin reset your progress."
				: string.Format(CultureInfo.InvariantCulture, "An admin adjusted your rank XP by {0:+#,0;-#,0}. You're now {1}.", outcome.XpAfter - outcome.XpBefore, RankName(outcome.RankAfter));
			player.PrintToChat($" {ChatColors.Gold}{services.Config.ChatPrefix}{ChatColors.Default} {text}");
		}
	}

	private static string RankName(int rank) => RankLadder.Names[Math.Clamp(rank, 0, RankLadder.TopRank)];

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

	private static (ulong SteamId, string Name)? FindTarget(string text, CommandInfo command)
	{
		text = text.Trim();
		if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId) && (steamId >= MinSteamId) && (steamId <= MaxSteamId))
		{
			CCSPlayerController? online = Utilities.GetPlayers().FirstOrDefault(p => GameState.IsHuman(p) && (p.SteamID == steamId));
			return (steamId, online?.PlayerName ?? steamId.ToString(CultureInfo.InvariantCulture));
		}

		List<CCSPlayerController> humans = [.. Utilities.GetPlayers().Where(GameState.IsHuman)];
		List<CCSPlayerController> matches;
		if (text.StartsWith('#') && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int userId))
		{
			matches = [.. humans.Where(p => p.UserId == userId)];
		}
		else
		{
			List<CCSPlayerController> exact = [.. humans.Where(p => string.Equals(p.PlayerName, text, StringComparison.OrdinalIgnoreCase))];
			matches = exact.Count > 0 ? exact : [.. humans.Where(p => (p.PlayerName ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase))];
		}

		switch (matches.Count)
		{
			case 1:
				return (matches[0].SteamID, matches[0].PlayerName ?? string.Empty);

			case 0:
				command.ReplyToCommand($"No connected player matches \"{text}\". Use #userid, a SteamID64, or part of a name.");
				return null;

			default:
				command.ReplyToCommand($"\"{text}\" matches {matches.Count} players: {string.Join(", ", matches.Select(p => $"{p.PlayerName} (#{p.UserId})"))}. Use #userid or a SteamID64.");
				return null;
		}
	}
}
