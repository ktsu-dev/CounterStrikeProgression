// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>One kill as the live map draws it, with both ends in radar pixels.</summary>
/// <param name="Map">The map.</param>
/// <param name="KillerName">The killer's name, or <see langword="null"/> when the plugin sent none.</param>
/// <param name="VictimName">The victim's name, or <see langword="null"/> when the plugin sent none.</param>
/// <param name="KillerIsBot">Whether the killer is a bot: a bot persona, or a bot with no id.</param>
/// <param name="VictimIsBot">Whether the victim is a bot: a bot persona, or a bot with no id.</param>
/// <param name="KillerX">The killer's radar pixel column, or <see langword="null"/> when the killer stood off the radar.</param>
/// <param name="KillerY">The killer's radar pixel row, or <see langword="null"/> when the killer stood off the radar.</param>
/// <param name="VictimX">The victim's radar pixel column, from 0 at the left to <see cref="MapOverview.Size"/>.</param>
/// <param name="VictimY">The victim's radar pixel row, from 0 at the top to <see cref="MapOverview.Size"/>.</param>
/// <param name="Weapon">The weapon's display name, or its key when the catalog doesn't know it.</param>
/// <param name="Headshot">Whether it was a headshot.</param>
/// <param name="At">When it happened, in UTC.</param>
internal sealed record LiveKill(
	string Map,
	string? KillerName,
	string? VictimName,
	bool KillerIsBot,
	bool VictimIsBot,
	double? KillerX,
	double? KillerY,
	double VictimX,
	double VictimY,
	string Weapon,
	bool Headshot,
	DateTime At);

/// <summary>
/// Turns a batch's kills into kills on the map's radar, for the live map.
/// </summary>
internal static class LiveKills
{
	/// <summary>
	/// Gets the kills that can be drawn on their map's radar. Only a calibrated map's radar lines up with the
	/// world, and a kill is drawn where the victim fell, so kills on any other map or with the victim off the
	/// radar are left out. A killer off the radar keeps the kill, without the killer's position.
	/// </summary>
	/// <param name="kills">The kills.</param>
	/// <returns>The kills on the radar, in the order given.</returns>
	public static IReadOnlyList<LiveKill> From(IEnumerable<KillPoint> kills)
	{
		Ensure.NotNull(kills);

		List<LiveKill> drawn = [];
		foreach (KillPoint kill in kills)
		{
			if (MapOverview.For(kill.Map) is not { IsCalibrated: true } overview)
			{
				continue;
			}

			(double victimX, double victimY) = overview.ToPixel(kill.VictimX, kill.VictimY);
			if (!IsOnRadar(victimX, victimY))
			{
				continue;
			}

			(double killerX, double killerY) = overview.ToPixel(kill.KillerX, kill.KillerY);
			bool killerOnRadar = IsOnRadar(killerX, killerY);
			string weapon = WeaponCatalog.Find(WeaponKey.Create(kill.Weapon))?.DisplayName ?? kill.Weapon;
			drawn.Add(new LiveKill(
				kill.Map,
				kill.KillerName,
				kill.VictimName,
				KillerIsBot: IsBot(kill.KillerId),
				VictimIsBot: IsBot(kill.VictimId),
				killerOnRadar ? killerX : null,
				killerOnRadar ? killerY : null,
				victimX,
				victimY,
				weapon,
				kill.Headshot,
				kill.At));
		}

		return drawn;
	}

	private static bool IsOnRadar(double x, double y) =>
		(x >= 0) && (x <= MapOverview.Size) && (y >= 0) && (y <= MapOverview.Size);

	private static bool IsBot(ulong id) => (id == 0) || BotIds.IsBot(id);
}
