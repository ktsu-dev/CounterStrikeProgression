// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Rotation;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The days, weeks, and seasons that progression rotates on, and the picks made for each. Days run on UTC. Every
/// pick comes from a hash of the day's date, so the plugin and the site always agree without storing anything.
/// </summary>
internal static class DailyRotation
{
	/// <summary>Gets the weapon classes that can be featured, in a fixed order the pick indexes into.</summary>
	public static IReadOnlyList<WeaponClass> FeaturedClasses { get; } =
	[
		WeaponClass.Pistol,
		WeaponClass.Smg,
		WeaponClass.Rifle,
		WeaponClass.Sniper,
		WeaponClass.Shotgun,
		WeaponClass.MachineGun,
	];

	/// <summary>
	/// Gets the UTC day a moment falls on.
	/// </summary>
	/// <param name="utc">The moment, in UTC.</param>
	/// <returns>The day.</returns>
	public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc);

	/// <summary>
	/// Gets the key a day's contracts are stored under, such as <c>2026-10-17</c>.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The key.</returns>
	public static string DayKey(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	/// <summary>
	/// Gets the key a week's contracts are stored under, its ISO week such as <c>2026-W42</c>. Weeks start on Monday.
	/// </summary>
	/// <param name="day">Any day in the week.</param>
	/// <returns>The key.</returns>
	public static string WeekKey(DateOnly day)
	{
		DateTime date = day.ToDateTime(TimeOnly.MinValue);
		return string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(date)}-W{ISOWeek.GetWeekOfYear(date):00}");
	}

	/// <summary>
	/// Gets the Monday that starts a day's week.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The Monday.</returns>
	public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

	/// <summary>
	/// Gets the weapon class featured on a day.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The featured class.</returns>
	public static WeaponClass FeaturedClass(DateOnly day) =>
		FeaturedClasses[(int)(Hash(DayKey(day), "featured") % (ulong)FeaturedClasses.Count)];

	/// <summary>
	/// Gets the plural name players see for a weapon class, such as <c>SMGs</c>.
	/// </summary>
	/// <param name="weaponClass">The class.</param>
	/// <returns>The name.</returns>
	public static string ClassName(WeaponClass weaponClass) => weaponClass switch
	{
		WeaponClass.Pistol => "pistols",
		WeaponClass.Smg => "SMGs",
		WeaponClass.Rifle => "rifles",
		WeaponClass.Sniper => "snipers",
		WeaponClass.Shotgun => "shotguns",
		WeaponClass.MachineGun => "machine guns",
		WeaponClass.Knife => "knives",
		WeaponClass.Grenade => "grenades",
		WeaponClass.Taser => "the Zeus",
		_ => "weapons",
	};

	/// <summary>
	/// Hashes a key and a salt into a number the picks are made from. FNV-1a, so it never changes between runs or
	/// .NET versions the way <see cref="string.GetHashCode()"/> does.
	/// </summary>
	/// <param name="key">The period key.</param>
	/// <param name="salt">What the pick is for, so different picks on one day don't line up.</param>
	/// <returns>The hash.</returns>
	public static ulong Hash(string key, string salt)
	{
		Ensure.NotNull(key);
		Ensure.NotNull(salt);

		const ulong offset = 14695981039346656037;
		const ulong prime = 1099511628211;
		ulong hash = offset;
		foreach (char c in salt + "|" + key)
		{
			hash ^= c;
			hash *= prime;
		}

		// FNV's low bits mix poorly, so fold the high half in before the caller takes a remainder.
		return hash ^ (hash >> 29);
	}
}
