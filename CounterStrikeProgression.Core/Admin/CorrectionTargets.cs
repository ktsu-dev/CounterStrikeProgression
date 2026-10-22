// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Admin;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Players;

/// <summary>A connected human or bot an admin command can name.</summary>
/// <param name="SteamId">The 64-bit Steam ID, or the bot id for a bot.</param>
/// <param name="UserId">The user id shown by <c>status</c>.</param>
/// <param name="Name">The player's name.</param>
internal sealed record TargetCandidate(ulong SteamId, int UserId, string Name);

/// <summary>Who an admin command names, or why it names nobody.</summary>
/// <param name="SteamId">The player's 64-bit Steam ID or bot id, or 0 when there's no single match.</param>
/// <param name="Name">The player's name, or the Steam ID as text for a human who isn't connected.</param>
/// <param name="Error">The reply for the admin when there's no single match.</param>
internal sealed record TargetResolution(ulong SteamId, string Name, string? Error)
{
	/// <summary>Gets a value indicating whether the text named exactly one player.</summary>
	public bool Found => Error is null;
}

/// <summary>
/// Works out which player an admin command names: <c>#userid</c>, a SteamID64 or bot id (which work for players who
/// aren't connected), <c>bot:&lt;name&gt;</c> (a bot persona, connected or not), an exact name, or part of a name that matches one player only.
/// </summary>
internal static class CorrectionTargets
{
	/// <summary>The prefix that names a bot persona by its name.</summary>
	public const string BotPrefix = "bot:";

	/// <summary>The lowest individual-account SteamID64.</summary>
	public const ulong MinSteamId = 76561197960265729;

	/// <summary>The highest individual-account SteamID64.</summary>
	public const ulong MaxSteamId = 76561202255233023;

	/// <summary>
	/// Resolves a command argument against the connected players.
	/// </summary>
	/// <param name="text">The argument.</param>
	/// <param name="players">The connected humans and tracked bots.</param>
	/// <returns>The player, or the reason there isn't one.</returns>
	public static TargetResolution Resolve(string? text, IReadOnlyList<TargetCandidate> players)
	{
		Ensure.NotNull(players);
		text = text?.Trim() ?? string.Empty;

		if (text.StartsWith(BotPrefix, StringComparison.OrdinalIgnoreCase))
		{
			string botName = text[BotPrefix.Length..].Trim();
			return botName.Length == 0
				? new(0, text, $"Name a bot as {BotPrefix}<name>.")
				: new(BotIds.For(botName), botName, null);
		}

		if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId) && (steamId is >= MinSteamId and <= MaxSteamId || BotIds.IsBot(steamId)))
		{
			TargetCandidate? online = players.FirstOrDefault(h => h.SteamId == steamId);
			return new(steamId, online?.Name ?? text, null);
		}

		List<TargetCandidate> matches;
		if (text.StartsWith('#') && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int userId))
		{
			matches = [.. players.Where(h => h.UserId == userId)];
		}
		else
		{
			List<TargetCandidate> exact = [.. players.Where(h => string.Equals(h.Name, text, StringComparison.OrdinalIgnoreCase))];
			matches = (exact.Count > 0) || (text.Length == 0) ? exact : [.. players.Where(h => h.Name.Contains(text, StringComparison.OrdinalIgnoreCase))];
		}

		return matches.Count switch
		{
			1 => new(matches[0].SteamId, matches[0].Name, null),
			0 => new(0, text, $"No connected player matches \"{text}\". Use #userid, a SteamID64, bot:<name>, or part of a name."),
			_ => new(0, text, $"\"{text}\" matches {matches.Count} players: {string.Join(", ", matches.Select(h => $"{h.Name} (#{h.UserId})"))}. Use #userid, a SteamID64, or bot:<name>."),
		};
	}
}

/// <summary>
/// Holds a reset until the same admin repeats it for the same player in time, because a reset can't be undone.
/// </summary>
/// <param name="window">How long, in seconds, the admin has to repeat the command.</param>
internal sealed class ResetConfirmation(double window)
{
	private (ulong Admin, ulong Target, double Until)? pending;

	/// <summary>Gets how long, in seconds, the admin has to repeat the command.</summary>
	public double Window { get; } = window;

	/// <summary>
	/// Records a reset request.
	/// </summary>
	/// <param name="admin">The admin's Steam ID, or 0 for the console.</param>
	/// <param name="target">The player's Steam ID.</param>
	/// <param name="now">The current time, in seconds.</param>
	/// <returns><see langword="true"/> when this repeats a pending request in time, so the reset should run.</returns>
	public bool Confirm(ulong admin, ulong target, double now)
	{
		if ((pending is var (pendingAdmin, pendingTarget, until)) && (pendingAdmin == admin) && (pendingTarget == target) && (now <= until))
		{
			pending = null;
			return true;
		}

		pending = (admin, target, now + Window);
		return false;
	}

	/// <summary>
	/// Gets the warning that asks the admin to repeat the command.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <returns>The warning.</returns>
	public string Warning(string name, ulong steamId) => string.Format(
		CultureInfo.InvariantCulture,
		"This deletes every rank, weapon, and ladder row {0} ({1}) has, and can't be undone. Run the same command again within {2:0} seconds to confirm.",
		name,
		steamId,
		Window);
}
