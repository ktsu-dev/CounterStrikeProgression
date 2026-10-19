// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Records;

/// <summary>
/// The words for a record set or broken, shared by chat and Discord, which mark them up differently.
/// </summary>
internal static class RecordWording
{
	/// <summary>
	/// Describes a record set or broken, to follow the new holder's name.
	/// </summary>
	/// <param name="record">The record set or broken.</param>
	/// <param name="recordName">Marks up the record's name.</param>
	/// <param name="value">Marks up the new value.</param>
	/// <param name="previousHolder">Marks up the previous holder's name.</param>
	/// <returns>Text such as <c>broke Sam's server record for Longest rifle kill: 61.0 m (was 58.2 m)</c>.</returns>
	public static string Describe(RecordBreak record, Func<string, string> recordName, Func<string, string> value, Func<string, string> previousHolder)
	{
		Ensure.NotNull(record);
		Ensure.NotNull(recordName);
		Ensure.NotNull(value);
		Ensure.NotNull(previousHolder);

		RecordRow holder = record.Holder;
		string name = recordName(record.Definition.Name);
		string set = value(record.Definition.Format(holder.Value));
		if (holder.PreviousValue is not { } previous)
		{
			return $"set the server record for {name}: {set}";
		}

		string was = record.Definition.Format(previous);
		return record.BeatOwn
			? $"beat their own server record for {name}: {set} (was {was})"
			: $"broke {previousHolder(holder.PreviousName ?? string.Empty)}'s server record for {name}: {set} (was {was})";
	}
}
