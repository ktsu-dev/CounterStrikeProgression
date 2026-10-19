// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Tracking;

using System.Globalization;

/// <summary>
/// What happened to the bounty.
/// </summary>
internal enum BountyChangeKind
{
	/// <summary>A player became the leader and now carries the bounty.</summary>
	Placed = 0,

	/// <summary>The holder's streak reached another multiple of the starting kills, so the bounty grew.</summary>
	Raised,

	/// <summary>Another player ended the holder's streak and collects the bounty.</summary>
	Collected,

	/// <summary>The holder's streak ended with no one to pay, such as a suicide, a bot, or leaving.</summary>
	Lost,
}

/// <summary>
/// One change to the bounty.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="HolderSlot">The slot of the player who carries, or carried, the bounty.</param>
/// <param name="Kills">The holder's life streak.</param>
/// <param name="Xp">What the bounty is worth.</param>
/// <param name="CollectorSlot">The slot of the player who collects it, for <see cref="BountyChangeKind.Collected"/>.</param>
internal sealed record BountyChange(BountyChangeKind Kind, int HolderSlot, int Kills, long Xp, int? CollectorSlot = null);

/// <summary>
/// Puts a bounty on the human player with the longest current life streak, once it reaches
/// <see cref="ProgressionSettings.BountyStartKills"/>, and pays it to whoever ends that streak.
/// </summary>
/// <remarks>
/// The bounty is worth <see cref="ProgressionSettings.BountyXpPerKill"/> for every kill in the streak, so it grows
/// with each kill. When another player's streak passes the holder's, the bounty moves to them. When the holder's
/// streak ends, the next longest streak still over the threshold takes it. Bots never carry a bounty. Ties keep the
/// current holder, and otherwise go to the lowest slot.
/// </remarks>
/// <param name="settings">The bounty settings.</param>
internal sealed class BountyBoard(ProgressionSettings settings)
{
	private readonly Dictionary<int, int> streaks = [];

	/// <summary>Gets the slot of the player carrying the bounty, or <see langword="null"/> when no one is.</summary>
	public int? HolderSlot { get; private set; }

	/// <summary>Gets a value indicating whether bounties run.</summary>
	public bool Enabled => (settings.BountyStartKills > 0) && (settings.BountyXpPerKill > 0);

	/// <summary>
	/// Gets a player's current life streak.
	/// </summary>
	/// <param name="slot">The player's slot.</param>
	/// <returns>Their kills since they last died, or 0 for a bot.</returns>
	public int StreakOf(int slot) => streaks.GetValueOrDefault(slot);

	/// <summary>
	/// Gets what a bounty on a streak is worth.
	/// </summary>
	/// <param name="kills">The streak.</param>
	/// <returns>The rank XP.</returns>
	public long Value(int kills) => (long)kills * settings.BountyXpPerKill;

	/// <summary>
	/// Records a kill, which ends the victim's streak and adds to the killer's.
	/// </summary>
	/// <param name="killerSlot">The killer's slot.</param>
	/// <param name="killerScores">Whether the kill adds to the killer's streak, which a bot's or a teamkill doesn't.</param>
	/// <param name="victimSlot">The victim's slot.</param>
	/// <param name="killerCollects">Whether the killer is paid for ending the victim's bounty, which a bot, a teammate, or a player whose progress hasn't loaded isn't.</param>
	/// <returns>What happened to the bounty, in order.</returns>
	public IReadOnlyList<BountyChange> Kill(int killerSlot, bool killerScores, int victimSlot, bool killerCollects)
	{
		List<BountyChange> changes = [];
		if (!Enabled)
		{
			return changes;
		}

		EndStreak(victimSlot, killerCollects ? killerSlot : null, changes);
		if (killerScores && (killerSlot != victimSlot))
		{
			streaks[killerSlot] = StreakOf(killerSlot) + 1;
		}

		Elect(killerSlot, changes);
		return changes;
	}

	/// <summary>
	/// Ends the streak of a player who left, or who died with no one to pay, such as from a fall or a suicide.
	/// </summary>
	/// <param name="slot">The player's slot.</param>
	/// <returns>What happened to the bounty, in order.</returns>
	public IReadOnlyList<BountyChange> Drop(int slot)
	{
		List<BountyChange> changes = [];
		if (!Enabled)
		{
			return changes;
		}

		EndStreak(slot, collector: null, changes);
		Elect(killerSlot: null, changes);
		return changes;
	}

	/// <summary>
	/// Pays each collected bounty to its collector, and tells everyone about every other change.
	/// </summary>
	/// <param name="changes">What the board returned.</param>
	/// <param name="nameOf">Gets a player's name from their slot.</param>
	/// <param name="pay">Pays a collected bounty, given the holder's name, and returns whether it found the collector to pay.</param>
	/// <param name="announce">Tells the whole server a line.</param>
	public static void Settle(IReadOnlyList<BountyChange> changes, Func<int, string> nameOf, Func<BountyChange, string, bool> pay, Action<string> announce)
	{
		Ensure.NotNull(changes);
		Ensure.NotNull(nameOf);
		Ensure.NotNull(pay);
		Ensure.NotNull(announce);

		foreach (BountyChange change in changes)
		{
			string holder = nameOf(change.HolderSlot);
			if ((change.Kind != BountyChangeKind.Collected) || !pay(change, holder))
			{
				announce(Describe(change, holder));
			}
		}
	}

	/// <summary>
	/// Gets the chat line for a bounty that was placed, grew, or went unclaimed. A collected bounty is announced with
	/// the collector's result instead, so one with no collector to pay reads as unclaimed.
	/// </summary>
	/// <param name="change">What happened.</param>
	/// <param name="holderName">The name of the player who carries, or carried, the bounty.</param>
	/// <returns>The line.</returns>
	public static string Describe(BountyChange change, string holderName)
	{
		Ensure.NotNull(change);
		Ensure.NotNull(holderName);

		string xp = change.Xp.ToString("N0", CultureInfo.InvariantCulture);
		return change.Kind switch
		{
			BountyChangeKind.Placed => $"{holderName} leads with a {change.Kills}-kill streak. End it for a {xp} XP bounty!",
			BountyChangeKind.Raised => $"{holderName} is on {change.Kills} kills. The bounty is up to {xp} XP!",
			_ => $"{holderName}'s {change.Kills}-kill streak ended and the {xp} XP bounty went unclaimed.",
		};
	}

	/// <summary>
	/// Forgets every streak, such as when a new map starts.
	/// </summary>
	public void Clear()
	{
		streaks.Clear();
		HolderSlot = null;
	}

	private void EndStreak(int slot, int? collector, List<BountyChange> changes)
	{
		int kills = StreakOf(slot);
		streaks.Remove(slot);
		if (HolderSlot != slot)
		{
			return;
		}

		HolderSlot = null;
		BountyChangeKind kind = (collector is int c) && (c != slot) ? BountyChangeKind.Collected : BountyChangeKind.Lost;
		changes.Add(new(kind, slot, kills, Value(kills), kind == BountyChangeKind.Collected ? collector : null));
	}

	private void Elect(int? killerSlot, List<BountyChange> changes)
	{
		int holderKills = HolderSlot is int holder ? StreakOf(holder) : 0;
		(int Slot, int Kills)? leader = streaks
			.Where(s => (s.Value >= settings.BountyStartKills) && (s.Value > holderKills))
			.OrderByDescending(s => s.Value)
			.ThenBy(s => s.Key)
			.Select(s => ((int, int)?)(s.Key, s.Value))
			.FirstOrDefault();

		if (leader is (int slot, int kills))
		{
			HolderSlot = slot;
			changes.Add(new(BountyChangeKind.Placed, slot, kills, Value(kills)));
			return;
		}

		// The holder's own kill keeps the bounty growing, and every so often the server hears about it.
		if ((HolderSlot is int current) && (killerSlot == current) && ((holderKills % settings.BountyStartKills) == 0))
		{
			changes.Add(new(BountyChangeKind.Raised, current, holderKills, Value(holderKills)));
		}
	}
}
