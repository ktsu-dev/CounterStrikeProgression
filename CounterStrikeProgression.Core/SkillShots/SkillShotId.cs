// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.SkillShots;

using ktsu.Semantics.Strings;

/// <summary>
/// The stable id of a skill shot, such as <c>moving_target</c>. It doubles as the id of the matching
/// challenge on every weapon's ladder, so it must never change once players have progress against it.
/// </summary>
[RegexMatch("^[a-z0-9_]+$")]
public sealed record SkillShotId : SemanticString<SkillShotId>;
