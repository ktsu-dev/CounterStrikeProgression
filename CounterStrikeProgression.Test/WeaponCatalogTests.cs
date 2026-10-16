// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Weapons;

[TestClass]
public sealed class WeaponCatalogTests
{
	private static readonly string[] ScopedKeys = ["aug", "sg556", "ssg08", "awp", "g3sg1", "scar20"];

	[TestMethod]
	[DataRow("ak47", "ak47")]
	[DataRow("weapon_ak47", "ak47")]
	[DataRow("WEAPON_AWP", "awp")]
	[DataRow("knife_karambit", "knife")]
	[DataRow("knife_t", "knife")]
	[DataRow("bayonet", "knife")]
	[DataRow("molotov", "inferno")]
	[DataRow("incgrenade", "inferno")]
	[DataRow("inferno", "inferno")]
	[DataRow("hegrenade_projectile", "hegrenade")]
	[DataRow("taser", "taser")]
	public void ResolveNormalizesReportedNames(string reported, string expected) =>
		Assert.AreEqual(expected, WeaponCatalog.Resolve(reported)?.Key.WeakString);

	[TestMethod]
	[DataRow("hkp2000", 61, "usp_silencer")]
	[DataRow("m4a1", 60, "m4a1_silencer")]
	[DataRow("p250", 63, "cz75a")]
	[DataRow("deagle", 64, "revolver")]
	[DataRow("hkp2000", 32, "hkp2000")]
	[DataRow("mp7", 23, "mp5sd")]
	public void ResolveUsesDefinitionIndexForAlternates(string reported, int definitionIndex, string expected) =>
		Assert.AreEqual(expected, WeaponCatalog.Resolve(reported, definitionIndex)?.Key.WeakString);

	[TestMethod]
	[DataRow("world")]
	[DataRow("planted_c4")]
	[DataRow("")]
	[DataRow(null)]
	public void ResolveReturnsNullForUntrackedDamage(string? reported) =>
		Assert.IsNull(WeaponCatalog.Resolve(reported));

	[TestMethod]
	public void KeysAreUnique()
	{
		List<string> keys = [.. WeaponCatalog.All.Select(w => w.Key.WeakString)];
		Assert.HasCount(keys.Count, keys.Distinct());
	}

	[TestMethod]
	public void ScopedWeaponsAreTheSnipersAndTheScopedRifles()
	{
		List<string> scoped = [.. WeaponCatalog.All.Where(w => w.IsScoped).Select(w => w.Key.WeakString)];
		CollectionAssert.AreEquivalent(ScopedKeys, scoped);
	}
}
