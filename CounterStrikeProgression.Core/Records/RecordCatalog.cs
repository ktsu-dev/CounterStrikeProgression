// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Records;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// What a record measures, which decides how its value is written.
/// </summary>
internal enum RecordUnit
{
	/// <summary>A distance in game units, shown in metres.</summary>
	Distance = 0,

	/// <summary>A count of things.</summary>
	Count,

	/// <summary>A share from 0 to 1, shown as a percentage.</summary>
	Share,
}

/// <summary>
/// One server record: a single-moment best that one player holds until someone beats it.
/// </summary>
/// <param name="Id">The id stored in <c>csp_records</c>, so never rename one.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Description">What it takes, in a few words.</param>
/// <param name="Unit">What the value measures.</param>
internal sealed record RecordDefinition(string Id, string Name, string Description, RecordUnit Unit)
{
	/// <summary>
	/// Writes a value of this record, such as <c>52.1 m</c>, <c>14 kills</c>, or <c>87%</c>.
	/// </summary>
	/// <param name="value">The value.</param>
	/// <returns>The text.</returns>
	public string Format(double value) => RecordCatalog.Format(Unit, value);
}

/// <summary>
/// Every server record.
/// </summary>
/// <remarks>
/// Ids are stored, so never rename one. Distances are kept in game units, and a game unit is an inch.
/// </remarks>
internal static class RecordCatalog
{
	/// <summary>Metres in one game unit.</summary>
	public const double MetresPerUnit = 0.0254;

	/// <summary>The id of Most kills in one life.</summary>
	public const string LifeKillsId = "life_kills";

	/// <summary>The id of Best life accuracy.</summary>
	public const string LifeAccuracyId = "life_accuracy";

	/// <summary>The id of Longest no-scope.</summary>
	public const string NoScopeId = "longest_no_scope";

	/// <summary>The id of Most skill shots in one map.</summary>
	public const string MapSkillShotsId = "map_skill_shots";

	/// <summary>The weapon classes with a longest kill record, in the order the site lists them.</summary>
	public static IReadOnlyList<WeaponClass> DistanceClasses { get; } =
		[WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.Rifle, WeaponClass.Sniper, WeaponClass.Shotgun, WeaponClass.MachineGun];

	/// <summary>Gets every record, in the order the site lists them.</summary>
	public static IReadOnlyList<RecordDefinition> All { get; } =
	[
		.. DistanceClasses.Select(c => new RecordDefinition(LongestKillId(c), $"Longest {ClassName(c)} kill", $"The longest kill with {ClassWithArticle(c)}", RecordUnit.Distance)),
		new(NoScopeId, "Longest no-scope", "The longest kill with a scoped weapon, unscoped", RecordUnit.Distance),
		new(LifeKillsId, "Most kills in one life", "The most kills before dying", RecordUnit.Count),
		new(LifeAccuracyId, "Best life accuracy", "The best accuracy over one life", RecordUnit.Share),
		new(MapSkillShotsId, "Most skill shots in one map", "The most skill shots earned in one map", RecordUnit.Count),
	];

	private static readonly Dictionary<string, RecordDefinition> ById = All.ToDictionary(r => r.Id, StringComparer.Ordinal);

	/// <summary>
	/// Gets the id of a weapon class's longest kill record.
	/// </summary>
	/// <param name="weaponClass">The weapon class.</param>
	/// <returns>The id, such as <c>longest_kill_rifle</c>.</returns>
	public static string LongestKillId(WeaponClass weaponClass) =>
		$"longest_kill_{weaponClass.ToString().ToLowerInvariant()}";

	/// <summary>
	/// Finds a record by id.
	/// </summary>
	/// <param name="id">The id.</param>
	/// <returns>The record, or <see langword="null"/> for an id no record has, such as one from a newer plugin.</returns>
	public static RecordDefinition? Find(string id) => ById.GetValueOrDefault(id);

	/// <summary>
	/// Gets a record by id.
	/// </summary>
	/// <param name="id">One of the ids in <see cref="All"/>.</param>
	/// <returns>The record.</returns>
	public static RecordDefinition Get(string id) => ById[id];

	/// <summary>
	/// Writes a value, such as <c>52.1 m</c>, <c>14 kills</c>, or <c>87%</c>.
	/// </summary>
	/// <param name="unit">What the value measures.</param>
	/// <param name="value">The value.</param>
	/// <returns>The text.</returns>
	public static string Format(RecordUnit unit, double value) => unit switch
	{
		RecordUnit.Distance => $"{(value * MetresPerUnit).ToString("0.0", CultureInfo.InvariantCulture)} m",
		RecordUnit.Share => $"{Math.Floor(value * 100).ToString("0", CultureInfo.InvariantCulture)}%",
		_ => value.ToString("N0", CultureInfo.InvariantCulture),
	};

	private static string ClassName(WeaponClass weaponClass) => weaponClass switch
	{
		WeaponClass.Smg => "SMG",
		WeaponClass.MachineGun => "machine gun",
		_ => weaponClass.ToString().ToLowerInvariant(),
	};

	private static string ClassWithArticle(WeaponClass weaponClass) => weaponClass switch
	{
		WeaponClass.Smg => "an SMG",
		WeaponClass.MachineGun => "a machine gun",
		_ => $"a {weaponClass.ToString().ToLowerInvariant()}",
	};
}
