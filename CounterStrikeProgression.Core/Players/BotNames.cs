// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Players;

/// <summary>
/// Picks the name a bot is given in place of the one its profile gave it. A bot's persona is keyed by its name
/// (<see cref="BotIds"/>), so a renamed bot is tracked under its new name.
/// </summary>
internal static class BotNames
{
	/// <summary>
	/// Gets the name to give a bot.
	/// </summary>
	/// <param name="names">Profile names to the names they're replaced with, matched ignoring case.</param>
	/// <param name="current">The bot's name now.</param>
	/// <returns>
	/// The new name, or null to leave the bot alone: the name isn't mapped, maps to itself or to a blank, or is
	/// already a name the map gives. The last keeps a bot from being renamed twice when one rename's result is
	/// another's source.
	/// </returns>
	public static string? RenameFor(IReadOnlyDictionary<string, string> names, string current)
	{
		Ensure.NotNull(names);
		if (string.IsNullOrWhiteSpace(current))
		{
			return null;
		}

		string trimmed = current.Trim();

		// A null value is read from config as readily as a blank one ("Rock": null), and means the same:
		// leave that bot alone. It must not stop every other bot from being renamed.
		if (names.Values.Any(given => string.Equals(given?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}

		string? target = names.FirstOrDefault(pair => string.Equals(pair.Key.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
		return string.IsNullOrEmpty(target) ? null : target;
	}
}
