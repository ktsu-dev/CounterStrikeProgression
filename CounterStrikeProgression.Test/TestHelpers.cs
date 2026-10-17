// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

internal static class TestHelpers
{
	public const ulong SteamId = 76561198000000001;

	public static WeaponInfo Weapon(string name) =>
		WeaponCatalog.Resolve(name) ?? throw new InvalidOperationException($"No weapon named {name}.");

	// The featured weapon changes with the date, so it's off unless a test asks for it.
	public static ProgressionEngine Engine(Action<ProgressionSettings>? configure = null, TimeProvider? time = null)
	{
		ProgressionSettings settings = new() { FeaturedWeaponMultiplier = 1 };
		configure?.Invoke(settings);
		return new ProgressionEngine(settings, time);
	}

	public static PlayerProgress Player() => new(SteamId) { Name = "Tester" };

	// A mid-range distance, so neither Point blank nor Long shot applies unless a test asks for it.
	public const double MidRange = 600;

	public static CombatEvent Hit(string weapon) => new() { Weapon = Weapon(weapon), Damage = 27, Distance = MidRange };

	public static CombatEvent Kill(string weapon) => new() { Weapon = Weapon(weapon), IsKill = true, Damage = 100, Distance = MidRange };

	public static IReadOnlyList<string> SkillShots(ProgressionResult result) =>
		[.. result.Notices.Where(n => n.Kind == NoticeKind.SkillShot).Select(n => n.SkillShot!.Id.WeakString)];
}

/// <summary>A clock that moves only when a test advances it.</summary>
internal sealed class ManualTime : TimeProvider
{
	private DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

	public override DateTimeOffset GetUtcNow() => now;

	public void Advance(TimeSpan by) => now += by;
}
