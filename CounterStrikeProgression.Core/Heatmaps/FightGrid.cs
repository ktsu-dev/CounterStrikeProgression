// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>One cell of a player's fight map: its column and row on the radar grid, and the fights won and lost in it.</summary>
/// <param name="X">The column, from 0 at the left.</param>
/// <param name="Y">The row, from 0 at the top.</param>
/// <param name="Won">The fights the player won here, meaning kills made from this cell.</param>
/// <param name="Lost">The fights the player lost here, meaning deaths in this cell.</param>
internal readonly record struct FightCell(int X, int Y, int Won, int Lost);

/// <summary>
/// Counts one player's won and lost fights into square cells over the radar image. A kill counts as won
/// where the player stood, and a death counts as lost where the player fell.
/// </summary>
internal static class FightGrid
{
	/// <summary>The cell size in radar pixels.</summary>
	public const int CellPixels = 32;

	/// <summary>The cells along each side of the grid.</summary>
	public const int Cells = MapOverview.Size / CellPixels;

	/// <summary>
	/// Counts a player's fights into cells. Fights the player isn't in, suicides, and positions off the radar image are left out.
	/// </summary>
	/// <param name="kills">The kills to count.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="overview">How the radar lines up with the world.</param>
	/// <returns>The cells holding any fights, in row then column order.</returns>
	public static IReadOnlyList<FightCell> Build(IEnumerable<KillPoint> kills, ulong steamId, MapOverview overview)
	{
		Ensure.NotNull(kills);
		Ensure.NotNull(overview);

		Dictionary<(int X, int Y), (int Won, int Lost)> counts = [];
		foreach (KillPoint kill in kills)
		{
			if (CellOf(kill, steamId, overview) is not { } fight)
			{
				continue;
			}

			(int won, int lost) = counts.GetValueOrDefault((fight.X, fight.Y));
			counts[(fight.X, fight.Y)] = (won + fight.Won, lost + fight.Lost);
		}

		return [.. counts.Select(c => new FightCell(c.Key.X, c.Key.Y, c.Value.Won, c.Value.Lost)).OrderBy(c => c.Y).ThenBy(c => c.X)];
	}

	/// <summary>
	/// Finds the cell one fight counts toward for a player.
	/// </summary>
	/// <param name="kill">The kill.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="overview">How the radar lines up with the world.</param>
	/// <returns>The cell with <see cref="FightCell.Won"/> or <see cref="FightCell.Lost"/> set to 1, or null when the player isn't in the fight, it was a suicide, or it falls off the radar.</returns>
	public static FightCell? CellOf(KillPoint kill, ulong steamId, MapOverview overview)
	{
		Ensure.NotNull(kill);
		Ensure.NotNull(overview);

		if (kill.KillerId == kill.VictimId)
		{
			return null;
		}

		bool won = kill.KillerId == steamId;
		if (!won && kill.VictimId != steamId)
		{
			return null;
		}

		HeatPoint point = won ? new HeatPoint(kill.KillerX, kill.KillerY) : new HeatPoint(kill.VictimX, kill.VictimY);
		(double x, double y) = overview.ToPixel(point);
		if (x is < 0 or >= MapOverview.Size || y is < 0 or >= MapOverview.Size)
		{
			return null;
		}

		return new FightCell((int)(x / CellPixels), (int)(y / CellPixels), won ? 1 : 0, won ? 0 : 1);
	}
}
