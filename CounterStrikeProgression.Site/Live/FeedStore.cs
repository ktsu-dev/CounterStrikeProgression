// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Globalization;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core.Live;

/// <summary>
/// Keeps the feed history in a JSON file, and each player's feed in a <c>players</c> folder next to it, one
/// file per player, so both survive a restart and a batch rewrites only the players it touched.
/// </summary>
/// <param name="path">The file to read and write.</param>
internal sealed class FeedStore(string path)
{
	/// <summary>Gets the file the history is kept in.</summary>
	public string Path => path;

	/// <summary>Gets the folder each player's feed is kept in.</summary>
	public string PlayersPath { get; } = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? string.Empty, "players");

	/// <summary>
	/// Reads the saved entries.
	/// </summary>
	/// <returns>The entries, or an empty list when the file is missing or unreadable.</returns>
	public IReadOnlyList<FeedEntry> Load() => Read(path);

	/// <summary>
	/// Reads every saved player feed.
	/// </summary>
	/// <returns>Each player's entries keyed by Steam ID, leaving out files that are missing, unreadable, or not named for a Steam ID.</returns>
	public Dictionary<ulong, IReadOnlyList<FeedEntry>> LoadPlayers()
	{
		Dictionary<ulong, IReadOnlyList<FeedEntry>> players = [];
		string[] files;
		try
		{
			files = Directory.Exists(PlayersPath) ? Directory.GetFiles(PlayersPath, "*.json") : [];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return players;
		}

		foreach (string file in files)
		{
			if (ulong.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId))
			{
				List<FeedEntry> entries = Read(file);
				if (entries.Count > 0)
				{
					players[steamId] = entries;
				}
			}
		}

		return players;
	}

	/// <summary>
	/// Replaces the saved entries. The data goes to a temporary file first, so a crash mid-write keeps the old history.
	/// </summary>
	/// <param name="entries">The entries to save.</param>
	public void Save(IReadOnlyList<FeedEntry> entries) => Write(path, entries);

	/// <summary>
	/// Replaces one player's saved feed, the same way <see cref="Save"/> does.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="entries">The entries to save.</param>
	public void SavePlayer(ulong steamId, IReadOnlyList<FeedEntry> entries) => Write(PlayerFile(steamId), entries);

	/// <summary>
	/// Removes one player's saved feed, if there is one.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	public void DeletePlayer(ulong steamId) => File.Delete(PlayerFile(steamId));

	private string PlayerFile(ulong steamId) =>
		System.IO.Path.Combine(PlayersPath, steamId.ToString(CultureInfo.InvariantCulture) + ".json");

	private static List<FeedEntry> Read(string file)
	{
		try
		{
			if (!File.Exists(file))
			{
				return [];
			}

			using FileStream stream = File.OpenRead(file);
			return JsonSerializer.Deserialize<List<FeedEntry>>(stream, LiveJson.Options) ?? [];
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	private static void Write(string file, IReadOnlyList<FeedEntry> entries)
	{
		Ensure.NotNull(entries);

		string? directory = System.IO.Path.GetDirectoryName(file);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string temporary = file + ".tmp";
		using (FileStream stream = File.Create(temporary))
		{
			JsonSerializer.Serialize(stream, entries, LiveJson.Options);
		}

		File.Move(temporary, file, overwrite: true);
	}
}
