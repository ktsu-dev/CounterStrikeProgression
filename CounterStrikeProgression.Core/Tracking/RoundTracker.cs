// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Tracking;

using ktsu.Extensions;

/// <summary>
/// What the round adds to a kill.
/// </summary>
/// <param name="IsOpeningKill">Whether it's the round's first kill.</param>
/// <param name="ChainedKills">The attacker's kills in the current chain, counting this one.</param>
/// <param name="KillsWithThisBullet">The attacker's kills on this tick, counting this one.</param>
/// <param name="IsTrade">Whether the victim had just killed one of the attacker's teammates.</param>
internal readonly record struct KillContext(bool IsOpeningKill, int ChainedKills, int KillsWithThisBullet, bool IsTrade);

/// <summary>
/// Tracks the kills in one round, for opening kills, multi-kills, collaterals, trades, and clutches.
/// </summary>
/// <remarks>
/// Players are identified by any id that's stable for the round, such as their slot. Teams are whatever the
/// plugin considers a side: when teammates are enemies, as in free-for-all deathmatch, every player is their
/// own team, so trades and clutches never apply.
/// </remarks>
/// <param name="settings">The multi-kill and trade windows.</param>
internal sealed class RoundTracker(ProgressionSettings settings)
{
	private readonly Dictionary<int, KillChain> chains = [];
	private readonly Dictionary<int, List<KillRecord>> recentKillsBy = [];
	private readonly Dictionary<int, LastAlive> lastAliveByTeam = [];
	private bool anyKill;

	/// <summary>
	/// Clears everything at the start of a round.
	/// </summary>
	public void StartRound()
	{
		chains.Clear();
		recentKillsBy.Clear();
		lastAliveByTeam.Clear();
		anyKill = false;
	}

	/// <summary>
	/// Records a kill and works out what the round adds to it.
	/// </summary>
	/// <param name="attacker">The attacker's id.</param>
	/// <param name="attackerTeam">The attacker's side.</param>
	/// <param name="victim">The victim's id.</param>
	/// <param name="victimTeam">The victim's side.</param>
	/// <param name="time">The game time in seconds.</param>
	/// <param name="tick">The server tick, which identifies kills made with one bullet.</param>
	/// <returns>The round context for the kill.</returns>
	public KillContext RegisterKill(int attacker, int attackerTeam, int victim, int victimTeam, double time, int tick)
	{
		bool opening = !anyKill;
		anyKill = true;

		if (!chains.TryGetValue(attacker, out KillChain? chain) || ((time - chain.LastKillTime) > settings.MultiKillSeconds))
		{
			chain = new KillChain();
			chains[attacker] = chain;
		}

		chain.Count++;
		chain.LastKillTime = time;
		chain.KillsThisTick = chain.LastTick == tick ? chain.KillsThisTick + 1 : 1;
		chain.LastTick = tick;

		bool trade = false;
		if (recentKillsBy.TryGetValue(victim, out List<KillRecord>? victimsKills))
		{
			trade = victimsKills.Any(k => (k.VictimTeam == attackerTeam) && (k.Victim != attacker) && ((time - k.Time) <= settings.TradeSeconds));
		}

		List<KillRecord> attackersKills = recentKillsBy.GetOrCreate(attacker);
		attackersKills.RemoveAll(k => (time - k.Time) > settings.TradeSeconds);
		attackersKills.Add(new KillRecord(victim, victimTeam, time));

		return new KillContext(opening, chain.Count, chain.KillsThisTick, trade);
	}

	/// <summary>
	/// Notes that a player has become the last one alive on their side. Only the first time per side per round counts.
	/// </summary>
	/// <param name="player">The player's id.</param>
	/// <param name="team">Their side.</param>
	/// <param name="enemiesAlive">Enemies still alive at that moment.</param>
	public void NoteLastAlive(int player, int team, int enemiesAlive) =>
		lastAliveByTeam.TryAdd(team, new LastAlive(player, enemiesAlive));

	/// <summary>
	/// Gets the clutch, if any, when a round ends.
	/// </summary>
	/// <param name="winningTeam">The side that won.</param>
	/// <param name="isAlive">Whether a player is still alive.</param>
	/// <returns>The clutcher and how many enemies they faced, or <see langword="null"/> when there was no clutch.</returns>
	public (int Player, int Enemies)? Clutch(int winningTeam, Func<int, bool> isAlive)
	{
		Ensure.NotNull(isAlive);
		return lastAliveByTeam.TryGetValue(winningTeam, out LastAlive? last) && (last.Enemies > 0) && isAlive(last.Player)
			? (last.Player, last.Enemies)
			: null;
	}

	private sealed class KillChain
	{
		public int Count { get; set; }

		public double LastKillTime { get; set; }

		public int LastTick { get; set; } = -1;

		public int KillsThisTick { get; set; }
	}

	private sealed record KillRecord(int Victim, int VictimTeam, double Time);

	private sealed record LastAlive(int Player, int Enemies);
}
