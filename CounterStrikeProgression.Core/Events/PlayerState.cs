// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Events;

/// <summary>
/// What a player was doing at the moment of a hit or kill, read from their pawn.
/// </summary>
internal sealed record PlayerState
{
	/// <summary>Gets the horizontal speed in units per second.</summary>
	public double Speed { get; init; }

	/// <summary>Gets a value indicating whether the player is standing on something.</summary>
	public bool OnGround { get; init; } = true;

	/// <summary>Gets a value indicating whether the player is crouched.</summary>
	public bool Ducked { get; init; }

	/// <summary>Gets a value indicating whether the player is on a ladder.</summary>
	public bool OnLadder { get; init; }

	/// <summary>Gets a value indicating whether the player is in water.</summary>
	public bool InWater { get; init; }

	/// <summary>Gets a value indicating whether the player is shift-walking.</summary>
	public bool Walking { get; init; }

	/// <summary>Gets a value indicating whether the player is scoped in.</summary>
	public bool Scoped { get; init; }

	/// <summary>Gets a value indicating whether the player is defusing the bomb.</summary>
	public bool Defusing { get; init; }

	/// <summary>Gets the player's health after the hit.</summary>
	public int Health { get; init; } = 100;
}
