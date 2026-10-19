// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Tracking;

/// <summary>
/// The timing and aim details of a shot, read from <see cref="PlayerCombatTracker"/>.
/// </summary>
/// <param name="SecondsSinceLanded">Seconds since the player last landed.</param>
/// <param name="SecondsSinceScoped">Seconds since the player scoped in, or infinity when not scoped.</param>
/// <param name="SecondsSinceWeaponSwitch">Seconds since the player switched weapons.</param>
/// <param name="SecondsSinceHurt">Seconds since the player last took damage.</param>
/// <param name="FirstShotAfterReload">Whether the latest shot was the first after a reload.</param>
/// <param name="RecentTurnDegrees">The largest turn in the last few ticks.</param>
/// <param name="EarlierKillsInSpray">Kills earlier in the current spray.</param>
/// <param name="EarlierLifeKills">Kills earlier this life.</param>
/// <param name="EarlierKillsUnhurt">Kills earlier this life since the player last took damage.</param>
internal readonly record struct CombatTiming(
	double SecondsSinceLanded,
	double SecondsSinceScoped,
	double SecondsSinceWeaponSwitch,
	double SecondsSinceHurt,
	bool FirstShotAfterReload,
	double RecentTurnDegrees,
	int EarlierKillsInSpray,
	int EarlierLifeKills,
	int EarlierKillsUnhurt);

/// <summary>
/// Follows one player's movement, scope, weapon switches, reloads, and aim, so a kill can be checked for the
/// skill shots that depend on what happened just before it. The plugin feeds it every tick and on each event.
/// </summary>
internal sealed class PlayerCombatTracker
{
	/// <summary>The ticks of aim history kept for flick detection.</summary>
	public const int TurnHistoryTicks = 4;

	private readonly double[] yawHistory = new double[TurnHistoryTicks + 1];
	private int yawSamples;
	private int yawNext;
	private bool wasOnGround = true;
	private double landedAt = double.NegativeInfinity;
	private double scopedAt = double.NaN;
	private double switchedAt = double.NegativeInfinity;
	private double hurtAt = double.NegativeInfinity;
	private bool reloadPending;
	private bool lastShotAfterReload;
	private int sprayKills;
	private int lifeKills;
	private int killsUnhurt;

	/// <summary>
	/// Records the player's state on a tick.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	/// <param name="yaw">The player's view yaw in degrees.</param>
	/// <param name="onGround">Whether the player is standing on something.</param>
	public void Tick(double time, double yaw, bool onGround)
	{
		if (onGround && !wasOnGround)
		{
			landedAt = time;
		}

		wasOnGround = onGround;
		yawHistory[yawNext] = yaw;
		yawNext = (yawNext + 1) % yawHistory.Length;
		yawSamples = Math.Min(yawSamples + 1, yawHistory.Length);
	}

	/// <summary>
	/// Records the player scoping in or out.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	/// <param name="scoped">Whether the player is now scoped.</param>
	public void ScopeChanged(double time, bool scoped) => scopedAt = scoped ? time : double.NaN;

	/// <summary>
	/// Records the player switching weapons, which also drops their scope and spray.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	public void WeaponSwitched(double time)
	{
		switchedAt = time;
		scopedAt = double.NaN;
		sprayKills = 0;
		reloadPending = false;
	}

	/// <summary>
	/// Records the player taking damage.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	public void Hurt(double time)
	{
		hurtAt = time;
		killsUnhurt = 0;
	}

	/// <summary>
	/// Records the start of a reload, so the next shot can count as the first after it.
	/// </summary>
	public void ReloadStarted() => reloadPending = true;

	/// <summary>
	/// Records a shot.
	/// </summary>
	/// <param name="shotsInSpray">The shots in the current spray, counting this one, as CS2 reports it.</param>
	public void ShotFired(int shotsInSpray)
	{
		lastShotAfterReload = reloadPending;
		reloadPending = false;
		if (shotsInSpray <= 1)
		{
			sprayKills = 0;
		}
	}

	/// <summary>
	/// Records a kill, which counts toward the current spray, life, and unhurt run once it has been read.
	/// </summary>
	public void KillMade()
	{
		sprayKills++;
		lifeKills++;
		killsUnhurt++;
	}

	/// <summary>
	/// Clears everything when the player dies or respawns.
	/// </summary>
	public void Reset()
	{
		yawSamples = 0;
		yawNext = 0;
		wasOnGround = true;
		landedAt = double.NegativeInfinity;
		scopedAt = double.NaN;
		switchedAt = double.NegativeInfinity;
		hurtAt = double.NegativeInfinity;
		reloadPending = false;
		lastShotAfterReload = false;
		sprayKills = 0;
		lifeKills = 0;
		killsUnhurt = 0;
	}

	/// <summary>
	/// Reads the timing and aim details for a shot happening now.
	/// </summary>
	/// <param name="time">The game time in seconds.</param>
	/// <returns>The details.</returns>
	public CombatTiming Read(double time) => new(
		time - landedAt,
		double.IsNaN(scopedAt) ? double.PositiveInfinity : time - scopedAt,
		time - switchedAt,
		time - hurtAt,
		lastShotAfterReload,
		RecentTurn(),
		sprayKills,
		lifeKills,
		killsUnhurt);

	/// <summary>
	/// Gets the smallest angle between two yaws, in degrees.
	/// </summary>
	/// <param name="a">One yaw.</param>
	/// <param name="b">The other yaw.</param>
	/// <returns>The angle, from 0 to 180.</returns>
	public static double AngleBetween(double a, double b)
	{
		double difference = Math.Abs((a - b) % 360);
		return difference > 180 ? 360 - difference : difference;
	}

	private double RecentTurn()
	{
		if (yawSamples < 2)
		{
			return 0;
		}

		int latestIndex = (yawNext - 1 + yawHistory.Length) % yawHistory.Length;
		double latest = yawHistory[latestIndex];
		double largest = 0;
		for (int i = 1; i < yawSamples; i++)
		{
			double earlier = yawHistory[(latestIndex - i + yawHistory.Length) % yawHistory.Length];
			largest = Math.Max(largest, AngleBetween(latest, earlier));
		}

		return largest;
	}
}
