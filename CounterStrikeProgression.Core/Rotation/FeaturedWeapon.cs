// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Rotation;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The day's featured weapon class, which earns <see cref="ProgressionSettings.FeaturedWeaponMultiplier"/> times the
/// weapon XP and ladder progress. The plugin and the site both work it out from the date and the settings.
/// </summary>
internal static class FeaturedWeapon
{
	/// <summary>
	/// Gets the class featured on a day.
	/// </summary>
	/// <param name="settings">The settings, which can turn the featured weapon off.</param>
	/// <param name="day">The UTC day.</param>
	/// <returns>The class, or <see langword="null"/> when the featured weapon is off.</returns>
	public static WeaponClass? On(ProgressionSettings settings, DateOnly day)
	{
		Ensure.NotNull(settings);
		return settings.FeaturedWeaponMultiplier > 1 ? DailyRotation.FeaturedClass(day) : null;
	}

	/// <summary>
	/// Gets how many steps one event moves a featured weapon's ladders.
	/// </summary>
	/// <param name="multiplier">The featured multiplier.</param>
	/// <returns>The step, at least 1.</returns>
	public static int LadderStep(double multiplier) => Math.Max(1, (int)Math.Round(multiplier, MidpointRounding.AwayFromZero));

	/// <summary>
	/// Writes a multiplier for players, such as <c>×2</c> or <c>×1.5</c>.
	/// </summary>
	/// <param name="multiplier">The multiplier.</param>
	/// <returns>The text.</returns>
	public static string Times(double multiplier) => "×" + multiplier.ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>
	/// Gets the weapons in a class, in catalogue order.
	/// </summary>
	/// <param name="weaponClass">The class.</param>
	/// <returns>The weapons.</returns>
	public static IReadOnlyList<WeaponInfo> Weapons(WeaponClass weaponClass) => [.. WeaponCatalog.All.Where(w => w.Class == weaponClass)];
}
