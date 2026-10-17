// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Rotation;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>The weapon class featured on one day, as players see it.</summary>
/// <param name="Day">The UTC day, as <c>yyyy-MM-dd</c>.</param>
/// <param name="Name">The class's plural name, capitalized, such as <c>SMGs</c>.</param>
/// <param name="Multiplier">How much more weapon XP it earns.</param>
/// <param name="Weapons">The display names of the weapons in the class.</param>
internal sealed record FeaturedDay(string Day, string Name, double Multiplier, IReadOnlyList<string> Weapons);

/// <summary>
/// The day's featured weapon class, which earns <see cref="ProgressionSettings.FeaturedWeaponMultiplier"/> times the
/// weapon XP. The plugin and the site both work it out from the date and the settings.
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
	/// Describes the class featured on a day.
	/// </summary>
	/// <param name="settings">The settings, which can turn the featured weapon off.</param>
	/// <param name="day">The UTC day.</param>
	/// <returns>The featured class, or <see langword="null"/> when the featured weapon is off.</returns>
	public static FeaturedDay? Describe(ProgressionSettings settings, DateOnly day)
	{
		if (On(settings, day) is not { } featured)
		{
			return null;
		}

		return new(
			DailyRotation.DayKey(day),
			Title(featured),
			settings.FeaturedWeaponMultiplier,
			[.. Weapons(featured).Select(w => w.DisplayName)]);
	}

	/// <summary>
	/// Gets a class's plural name with a capital letter, such as <c>Pistols</c>.
	/// </summary>
	/// <param name="weaponClass">The class.</param>
	/// <returns>The name.</returns>
	public static string Title(WeaponClass weaponClass)
	{
		string name = DailyRotation.ClassName(weaponClass);
		return char.ToUpperInvariant(name[0]) + name[1..];
	}

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
