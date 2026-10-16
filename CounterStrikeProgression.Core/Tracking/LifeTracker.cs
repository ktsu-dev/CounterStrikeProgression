// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Tracking;

using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Counts one player's shots and hits per weapon over a life, for the accuracy skill shots.
/// </summary>
/// <remarks>
/// A shotgun blast hits with several pellets on the same tick, and so does a bullet that passes through two
/// players. Hits are counted once per weapon per tick, so accuracy can't go over 100%.
/// </remarks>
internal sealed class LifeTracker
{
	private readonly Dictionary<WeaponKey, Counts> byWeapon = [];

	/// <summary>
	/// Records a shot.
	/// </summary>
	/// <param name="weapon">The weapon fired.</param>
	public void Shot(WeaponInfo weapon)
	{
		Ensure.NotNull(weapon);
		CountsFor(weapon).Shots++;
	}

	/// <summary>
	/// Records a damaging hit.
	/// </summary>
	/// <param name="weapon">The weapon.</param>
	/// <param name="headshot">Whether it hit the head.</param>
	/// <param name="vsBot">Whether the target was a bot.</param>
	/// <param name="tick">The server tick, which merges pellets and wallbang pass-throughs into one hit.</param>
	public void Hit(WeaponInfo weapon, bool headshot, bool vsBot, int tick)
	{
		Ensure.NotNull(weapon);
		Counts counts = CountsFor(weapon);
		if (counts.LastHitTick == tick)
		{
			return;
		}

		counts.LastHitTick = tick;
		counts.Hits++;
		if (headshot)
		{
			counts.HeadshotHits++;
		}

		if (vsBot)
		{
			counts.BotHits++;
		}
	}

	/// <summary>
	/// Ends the life and returns each weapon's totals.
	/// </summary>
	/// <returns>One entry per weapon fired or used that life.</returns>
	public IReadOnlyList<WeaponLifeStats> EndLife()
	{
		List<WeaponLifeStats> lives = [.. byWeapon.Values.Select(c => new WeaponLifeStats(c.Weapon, c.Shots, c.Hits, c.HeadshotHits, (c.BotHits * 2) > c.Hits))];
		byWeapon.Clear();
		return lives;
	}

	private Counts CountsFor(WeaponInfo weapon)
	{
		if (!byWeapon.TryGetValue(weapon.Key, out Counts? counts))
		{
			counts = new Counts(weapon);
			byWeapon[weapon.Key] = counts;
		}

		return counts;
	}

	private sealed class Counts(WeaponInfo weapon)
	{
		public WeaponInfo Weapon { get; } = weapon;

		public int Shots { get; set; }

		public int Hits { get; set; }

		public int HeadshotHits { get; set; }

		public int BotHits { get; set; }

		public int LastHitTick { get; set; } = -1;
	}
}
