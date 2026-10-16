// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Weapons;

using ktsu.Semantics.Strings;

/// <summary>
/// The normalized name of a weapon, such as <c>ak47</c> or <c>usp_silencer</c>, as stored in the database
/// and used to key weapon progress. Always lower case with no <c>weapon_</c> prefix.
/// </summary>
[RegexMatch("^[a-z0-9_]+$")]
public sealed record WeaponKey : SemanticString<WeaponKey>;
