// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;

/// <summary>Where one player is on the radar right now.</summary>
/// <param name="Slot">The player's slot.</param>
/// <param name="SteamId">The 64-bit Steam ID or bot persona id as a decimal string, or <see langword="null"/> for a bot with no id.</param>
/// <param name="Name">The player's name.</param>
/// <param name="IsBot">Whether the player is a bot.</param>
/// <param name="Alive">Whether the player is alive.</param>
/// <param name="X">The radar pixel column, from 0 at the left to <see cref="MapOverview.Size"/>.</param>
/// <param name="Y">The radar pixel row, from 0 at the top to <see cref="MapOverview.Size"/>.</param>
/// <param name="Facing">The way the player faces on the radar, in degrees clockwise from screen up (0 is up, 90 is right).</param>
internal sealed record PlayerPosition(int Slot, string? SteamId, string Name, bool IsBot, bool Alive, double X, double Y, double Facing);

/// <summary>Everyone's position on the current map's radar.</summary>
/// <param name="Map">The current map, or <see langword="null"/> before the plugin has sent a snapshot.</param>
/// <param name="Calibrated">Whether the map's radar lines up with the world, so positions can be drawn on it.</param>
/// <param name="Radar">The radar image's URL, or <see langword="null"/> when there is none to show.</param>
/// <param name="Size">The radar image's width and height in pixels.</param>
/// <param name="Players">The players with a position on the radar.</param>
internal sealed record PositionsView(string? Map, bool Calibrated, string? Radar, int Size, IReadOnlyList<PlayerPosition> Players);

/// <summary>
/// Turns the players' world positions in a snapshot into positions on the map's radar.
/// </summary>
internal static class LivePositions
{
	/// <summary>
	/// Gets everyone's radar position from a snapshot. Only a calibrated map's radar lines up with the world, so
	/// any other map gives no players, and players with no position or a position off the radar are left out.
	/// </summary>
	/// <param name="snapshot">The latest snapshot, or <see langword="null"/> when there is none.</param>
	/// <param name="radar">The radar image's URL, or <see langword="null"/>.</param>
	/// <returns>The positions.</returns>
	public static PositionsView From(ServerSnapshot? snapshot, string? radar)
	{
		if (snapshot is null)
		{
			return new PositionsView(null, false, null, MapOverview.Size, []);
		}

		if (MapOverview.For(snapshot.Map) is not { IsCalibrated: true } overview)
		{
			return new PositionsView(snapshot.Map, false, null, MapOverview.Size, []);
		}

		List<PlayerPosition> players = [];
		foreach (LivePlayer player in snapshot.Players)
		{
			if (PositionOf(player, overview) is PlayerPosition position)
			{
				players.Add(position);
			}
		}

		return new PositionsView(snapshot.Map, true, radar, MapOverview.Size, players);
	}

	/// <summary>
	/// Turns a world yaw into a radar facing. Yaw turns counterclockwise from world +X, and the radar's Y runs
	/// opposite to the world's, so the radar angle clockwise from up is 90 minus the yaw.
	/// </summary>
	/// <param name="yaw">The yaw in degrees.</param>
	/// <returns>The facing in degrees, from 0 up to but not including 360.</returns>
	private static double FacingOf(double yaw)
	{
		double facing = (90 - yaw) % 360;
		return facing < 0 ? facing + 360 : facing;
	}

	private static PlayerPosition? PositionOf(LivePlayer player, MapOverview overview)
	{
		if ((player.X is not double worldX) || (player.Y is not double worldY))
		{
			return null;
		}

		(double x, double y) = overview.ToPixel(worldX, worldY);
		if ((x < 0) || (x > MapOverview.Size) || (y < 0) || (y > MapOverview.Size))
		{
			return null;
		}

		string? steamId = player.SteamId == 0 ? null : player.SteamId.ToString(CultureInfo.InvariantCulture);
		return new PlayerPosition(player.Slot, steamId, player.Name, player.IsBot, player.Alive ?? true, x, y, FacingOf(player.Yaw ?? 0));
	}
}
