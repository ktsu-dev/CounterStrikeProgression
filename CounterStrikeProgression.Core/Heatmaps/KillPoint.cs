// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// Where one kill happened: both players' positions in world units, rounded to whole units.
/// </summary>
/// <param name="Map">The map, such as <c>de_dust2</c>.</param>
/// <param name="KillerId">The killer's 64-bit Steam ID, or 0 for a bot.</param>
/// <param name="VictimId">The victim's 64-bit Steam ID, or 0 for a bot.</param>
/// <param name="Weapon">The weapon key.</param>
/// <param name="Headshot">Whether it was a headshot.</param>
/// <param name="KillerX">The killer's X position.</param>
/// <param name="KillerY">The killer's Y position.</param>
/// <param name="KillerZ">The killer's height.</param>
/// <param name="VictimX">The victim's X position.</param>
/// <param name="VictimY">The victim's Y position.</param>
/// <param name="VictimZ">The victim's height.</param>
/// <param name="At">When it happened, in UTC.</param>
internal sealed record KillPoint(
	string Map,
	ulong KillerId,
	ulong VictimId,
	string Weapon,
	bool Headshot,
	short KillerX,
	short KillerY,
	short KillerZ,
	short VictimX,
	short VictimY,
	short VictimZ,
	DateTime At)
{
	/// <summary>
	/// Rounds a world coordinate to a stored one. CS2 maps fit in ±16384 units, so anything past a short's range is clamped.
	/// </summary>
	/// <param name="value">The coordinate.</param>
	/// <returns>The rounded coordinate.</returns>
	public static short Round(double value) =>
		double.IsFinite(value) ? (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue) : (short)0;
}

/// <summary>Which end of a kill a heatmap shows.</summary>
internal enum HeatmapKind
{
	/// <summary>Where the killers stood.</summary>
	Kills,

	/// <summary>Where the victims fell.</summary>
	Deaths,
}

/// <summary>One position on a heatmap, in world units.</summary>
/// <param name="X">The X position.</param>
/// <param name="Y">The Y position.</param>
internal readonly record struct HeatPoint(int X, int Y);

/// <summary>How many kills a map has kept.</summary>
/// <param name="Map">The map.</param>
/// <param name="Points">The kills kept.</param>
internal sealed record MapPointCount(string Map, long Points);
