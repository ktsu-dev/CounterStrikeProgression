// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Weapons;

using System.Collections.Frozen;

/// <summary>
/// Every weapon the plugin tracks, and the rules for turning the names CS2 reports into a <see cref="WeaponKey"/>.
/// </summary>
internal static class WeaponCatalog
{
	private const string WeaponPrefix = "weapon_";

	/// <summary>
	/// Gets every tracked weapon, in the order the progress panel lists them.
	/// </summary>
	public static IReadOnlyList<WeaponInfo> All { get; } =
	[
		Weapon("glock", "Glock-18", WeaponClass.Pistol),
		Weapon("hkp2000", "P2000", WeaponClass.Pistol),
		Weapon("usp_silencer", "USP-S", WeaponClass.Pistol),
		Weapon("p250", "P250", WeaponClass.Pistol),
		Weapon("cz75a", "CZ75-Auto", WeaponClass.Pistol),
		Weapon("fiveseven", "Five-SeveN", WeaponClass.Pistol),
		Weapon("tec9", "Tec-9", WeaponClass.Pistol),
		Weapon("deagle", "Desert Eagle", WeaponClass.Pistol),
		Weapon("revolver", "R8 Revolver", WeaponClass.Pistol),
		Weapon("elite", "Dual Berettas", WeaponClass.Pistol),
		Weapon("mac10", "MAC-10", WeaponClass.Smg),
		Weapon("mp9", "MP9", WeaponClass.Smg),
		Weapon("mp7", "MP7", WeaponClass.Smg),
		Weapon("mp5sd", "MP5-SD", WeaponClass.Smg),
		Weapon("ump45", "UMP-45", WeaponClass.Smg),
		Weapon("p90", "P90", WeaponClass.Smg),
		Weapon("bizon", "PP-Bizon", WeaponClass.Smg),
		Weapon("famas", "FAMAS", WeaponClass.Rifle),
		Weapon("galilar", "Galil AR", WeaponClass.Rifle),
		Weapon("ak47", "AK-47", WeaponClass.Rifle),
		Weapon("m4a1", "M4A4", WeaponClass.Rifle),
		Weapon("m4a1_silencer", "M4A1-S", WeaponClass.Rifle),
		Weapon("aug", "AUG", WeaponClass.Rifle, isScoped: true),
		Weapon("sg556", "SG 553", WeaponClass.Rifle, isScoped: true),
		Weapon("ssg08", "SSG 08", WeaponClass.Sniper, isScoped: true),
		Weapon("awp", "AWP", WeaponClass.Sniper, isScoped: true),
		Weapon("g3sg1", "G3SG1", WeaponClass.Sniper, isScoped: true),
		Weapon("scar20", "SCAR-20", WeaponClass.Sniper, isScoped: true),
		Weapon("nova", "Nova", WeaponClass.Shotgun),
		Weapon("xm1014", "XM1014", WeaponClass.Shotgun),
		Weapon("sawedoff", "Sawed-Off", WeaponClass.Shotgun),
		Weapon("mag7", "MAG-7", WeaponClass.Shotgun),
		Weapon("m249", "M249", WeaponClass.MachineGun),
		Weapon("negev", "Negev", WeaponClass.MachineGun),
		Weapon("knife", "Knife", WeaponClass.Knife),
		Weapon("taser", "Zeus x27", WeaponClass.Taser),
		Weapon("hegrenade", "HE Grenade", WeaponClass.Grenade),
		Weapon("inferno", "Molotov", WeaponClass.Grenade),
		Weapon("flashbang", "Flashbang", WeaponClass.Grenade),
		Weapon("decoy", "Decoy", WeaponClass.Grenade),
		Weapon("smokegrenade", "Smoke Grenade", WeaponClass.Grenade),
	];

	private static FrozenDictionary<string, WeaponInfo> ByName { get; } = All.ToFrozenDictionary(w => w.Key.WeakString);

	// Alternate weapons share a designer name with the weapon they replace, so the item definition
	// index is the only way to tell them apart.
	private static FrozenDictionary<int, string> ByDefinitionIndex { get; } = new Dictionary<int, string>
	{
		[1] = "deagle",
		[16] = "m4a1",
		[23] = "mp5sd",
		[32] = "hkp2000",
		[36] = "p250",
		[60] = "m4a1_silencer",
		[61] = "usp_silencer",
		[63] = "cz75a",
		[64] = "revolver",
	}.ToFrozenDictionary();

	// Names CS2 reports that aren't the catalogue key for the same weapon.
	private static FrozenDictionary<string, string> Aliases { get; } = new Dictionary<string, string>
	{
		["knife_t"] = "knife",
		["bayonet"] = "knife",
		["molotov"] = "inferno",
		["incgrenade"] = "inferno",
		["incendiarygrenade"] = "inferno",
		["molotov_projectile"] = "inferno",
		["hegrenade_projectile"] = "hegrenade",
		["flashbang_projectile"] = "flashbang",
		["decoy_projectile"] = "decoy",
		["smokegrenade_projectile"] = "smokegrenade",
		["zeus"] = "taser",
		["usp"] = "usp_silencer",
		["m4a1s"] = "m4a1_silencer",
		["m4a4"] = "m4a1",
	}.ToFrozenDictionary();

	/// <summary>
	/// Looks up a weapon by its catalogue key.
	/// </summary>
	/// <param name="key">The weapon key.</param>
	/// <returns>The weapon, or <see langword="null"/> when it isn't tracked.</returns>
	public static WeaponInfo? Find(WeaponKey key) => key is null ? null : ByName.GetValueOrDefault(key.WeakString);

	/// <summary>
	/// Turns a weapon name from a CS2 event or entity into a tracked weapon.
	/// </summary>
	/// <param name="reportedName">The name CS2 reported, with or without the <c>weapon_</c> prefix.</param>
	/// <param name="definitionIndex">The item definition index, when known, which separates alternates such as the USP-S from the P2000.</param>
	/// <returns>The weapon, or <see langword="null"/> for damage that isn't from a tracked weapon, such as falls or the bomb.</returns>
	public static WeaponInfo? Resolve(string? reportedName, int? definitionIndex = null)
	{
		if (definitionIndex is int index && ByDefinitionIndex.TryGetValue(index, out string? byIndex))
		{
			return ByName[byIndex];
		}

		if (string.IsNullOrWhiteSpace(reportedName))
		{
			return null;
		}

		string name = reportedName.Trim().ToLowerInvariant();
		if (name.StartsWith(WeaponPrefix, StringComparison.Ordinal))
		{
			name = name[WeaponPrefix.Length..];
		}

		if (Aliases.TryGetValue(name, out string? alias))
		{
			name = alias;
		}
		else if (name.StartsWith("knife", StringComparison.Ordinal))
		{
			// Every knife skin reports its own name (knife_karambit, knife_butterfly, and so on).
			name = "knife";
		}

		return ByName.GetValueOrDefault(name);
	}

	private static WeaponInfo Weapon(string key, string displayName, WeaponClass weaponClass, bool isScoped = false) =>
		new(WeaponKey.Create(key), displayName, weaponClass, isScoped);
}
