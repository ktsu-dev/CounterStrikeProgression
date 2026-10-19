// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Records;

/// <summary>One server record on the Records board.</summary>
/// <param name="Id">The record id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Description">What it takes.</param>
/// <param name="SteamId">The holder's 64-bit Steam ID as a decimal string, or <see langword="null"/> while nobody holds it.</param>
/// <param name="Holder">The holder's name when they set it, or <see langword="null"/> while nobody holds it.</param>
/// <param name="Value">The value, written out, such as <c>52.1 m</c>, or <see langword="null"/> while nobody holds it.</param>
/// <param name="SetAt">When it was set, in UTC, or <see langword="null"/> while nobody holds it.</param>
/// <param name="PreviousHolder">The name of the holder before, or <see langword="null"/> when there was none.</param>
/// <param name="PreviousValue">The value they held, written out, or <see langword="null"/> when there was none.</param>
internal sealed record RecordItem(string Id, string Name, string Description, string? SteamId, string? Holder, string? Value, DateTime? SetAt, string? PreviousHolder, string? PreviousValue);

/// <summary>
/// Lays the stored records out in catalog order, with the ones nobody holds yet left empty.
/// </summary>
internal static class RecordsViewBuilder
{
	/// <summary>
	/// Builds the Records board.
	/// </summary>
	/// <param name="rows">The stored records.</param>
	/// <returns>Every record the plugin keeps, in catalog order.</returns>
	public static IReadOnlyList<RecordItem> Build(IReadOnlyList<RecordRow> rows)
	{
		Ensure.NotNull(rows);
		Dictionary<string, RecordRow> byId = rows.GroupBy(r => r.Record, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.MaxBy(r => r.Value)!, StringComparer.Ordinal);
		return [.. RecordCatalog.All.Select(definition => byId.TryGetValue(definition.Id, out RecordRow? row)
			? new RecordItem(
				definition.Id,
				definition.Name,
				definition.Description,
				row.SteamId.ToString(CultureInfo.InvariantCulture),
				row.Name,
				definition.Format(row.Value),
				row.SetAt,
				row.PreviousSteamId == row.SteamId ? null : row.PreviousName,
				row.PreviousValue is { } previous ? definition.Format(previous) : null)
			: new RecordItem(definition.Id, definition.Name, definition.Description, null, null, null, null, null, null))];
	}
}
