// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Text.Json;
using ktsu.CounterStrikeProgression.Core.Live;

/// <summary>
/// Keeps the feed history in a JSON file, so it survives a restart.
/// </summary>
/// <param name="path">The file to read and write.</param>
internal sealed class FeedStore(string path)
{
	/// <summary>
	/// Reads the saved entries.
	/// </summary>
	/// <returns>The entries, or an empty list when the file is missing or unreadable.</returns>
	public IReadOnlyList<FeedEntry> Load()
	{
		try
		{
			if (!File.Exists(path))
			{
				return [];
			}

			using FileStream stream = File.OpenRead(path);
			return JsonSerializer.Deserialize<List<FeedEntry>>(stream, LiveJson.Options) ?? [];
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	/// <summary>
	/// Replaces the saved entries. The data goes to a temporary file first, so a crash mid-write keeps the old history.
	/// </summary>
	/// <param name="entries">The entries to save.</param>
	public void Save(IReadOnlyList<FeedEntry> entries)
	{
		Ensure.NotNull(entries);

		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string temporary = path + ".tmp";
		using (FileStream stream = File.Create(temporary))
		{
			JsonSerializer.Serialize(stream, entries, LiveJson.Options);
		}

		File.Move(temporary, path, overwrite: true);
	}
}
