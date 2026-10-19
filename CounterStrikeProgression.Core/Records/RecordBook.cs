// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Records;

/// <summary>
/// One record's current holder, as kept in <c>csp_records</c>.
/// </summary>
/// <param name="Record">The record id.</param>
/// <param name="SteamId">The holder's 64-bit Steam ID.</param>
/// <param name="Name">The holder's name when they set it.</param>
/// <param name="Value">The value they set.</param>
/// <param name="SetAt">When they set it, in UTC.</param>
/// <param name="PreviousSteamId">The 64-bit Steam ID of the holder before them, or <see langword="null"/> for the first holder.</param>
/// <param name="PreviousName">The name of the holder before them, or <see langword="null"/> for the first holder.</param>
/// <param name="PreviousValue">The value they beat, or <see langword="null"/> for the first holder.</param>
internal sealed record RecordRow(string Record, ulong SteamId, string Name, double Value, DateTime SetAt, ulong? PreviousSteamId, string? PreviousName, double? PreviousValue);

/// <summary>
/// A record a player just set or broke.
/// </summary>
/// <param name="Definition">The record.</param>
/// <param name="Holder">The new holder's row, which names the previous holder when there was one.</param>
internal sealed record RecordBreak(RecordDefinition Definition, RecordRow Holder)
{
	/// <summary>Gets a value indicating whether the player beat a record they already held.</summary>
	public bool BeatOwn => Holder.PreviousSteamId == Holder.SteamId;
}

/// <summary>
/// The server's records, held on the game thread and saved to <c>csp_records</c>.
/// </summary>
/// <remarks>
/// Nothing is checked until the stored records have loaded, because an empty book would hand out records
/// below the ones already stored. A value has to beat the record outright, so the first to reach it keeps it.
/// </remarks>
internal sealed class RecordBook
{
	private readonly Dictionary<string, RecordRow> holders = new(StringComparer.Ordinal);
	private readonly HashSet<string> dirty = new(StringComparer.Ordinal);

	/// <summary>Gets a value indicating whether the stored records have loaded.</summary>
	public bool IsLoaded { get; private set; }

	/// <summary>Gets a value indicating whether a record changed since it was last saved.</summary>
	public bool IsDirty => dirty.Count > 0;

	/// <summary>
	/// Takes the stored records, and starts checking.
	/// </summary>
	/// <param name="rows">The stored rows. Rows for records this plugin doesn't know are kept as they are.</param>
	public void Load(IEnumerable<RecordRow> rows)
	{
		Ensure.NotNull(rows);
		foreach (RecordRow row in rows)
		{
			// A record broken while the load was out is kept, since it beat whatever was stored.
			if (!holders.TryGetValue(row.Record, out RecordRow? held) || (row.Value > held.Value))
			{
				holders[row.Record] = row;
			}
		}

		IsLoaded = true;
	}

	/// <summary>
	/// Gets a record's holder.
	/// </summary>
	/// <param name="id">The record id.</param>
	/// <returns>The holder, or <see langword="null"/> while nobody holds it.</returns>
	public RecordRow? Holder(string id) => holders.GetValueOrDefault(id);

	/// <summary>
	/// Gives a player a record when the value beats it.
	/// </summary>
	/// <param name="definition">The record.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="name">The player's name.</param>
	/// <param name="value">What they did.</param>
	/// <param name="at">When, in UTC.</param>
	/// <returns>The record set or broken, or <see langword="null"/> when the value doesn't beat it or the records haven't loaded.</returns>
	public RecordBreak? TryBreak(RecordDefinition definition, ulong steamId, string name, double value, DateTime at)
	{
		Ensure.NotNull(definition);
		Ensure.NotNull(name);
		if (!IsLoaded || double.IsNaN(value) || double.IsInfinity(value) || (value <= 0))
		{
			return null;
		}

		RecordRow? previous = holders.GetValueOrDefault(definition.Id);
		if ((previous is not null) && (value <= previous.Value))
		{
			return null;
		}

		RecordRow row = new(definition.Id, steamId, name, value, at, previous?.SteamId, previous?.Name, previous?.Value);
		holders[definition.Id] = row;
		dirty.Add(definition.Id);
		return new RecordBreak(definition, row);
	}

	/// <summary>
	/// Copies the records that changed since the last save, and marks them saved.
	/// </summary>
	/// <returns>The rows to write.</returns>
	public IReadOnlyList<RecordRow> TakeDirty()
	{
		List<RecordRow> rows = [.. dirty.Select(id => holders[id])];
		dirty.Clear();
		return rows;
	}

	/// <summary>
	/// Flags rows again after their save failed, so the next save writes them. A record broken again since
	/// is already flagged, and its newer row is the one written.
	/// </summary>
	/// <param name="rows">The rows that weren't written.</param>
	public void Restore(IEnumerable<RecordRow> rows)
	{
		Ensure.NotNull(rows);
		foreach (RecordRow row in rows)
		{
			dirty.Add(row.Record);
		}
	}
}
