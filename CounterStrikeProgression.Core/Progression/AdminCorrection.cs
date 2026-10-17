// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// What an admin correction does.
/// </summary>
internal enum CorrectionKind
{
	/// <summary>Adds rank XP, or takes it away when the amount is negative.</summary>
	Grant = 0,

	/// <summary>Clears every rank, weapon, and ladder row.</summary>
	Reset,
}

/// <summary>
/// A player's standing before and after a correction.
/// </summary>
/// <param name="XpBefore">The rank XP before.</param>
/// <param name="XpAfter">The rank XP after.</param>
/// <param name="RankBefore">The rank index before.</param>
/// <param name="RankAfter">The rank index after.</param>
/// <param name="WeaponsCleared">How many weapon rows a reset removed.</param>
internal sealed record CorrectionOutcome(long XpBefore, long XpAfter, int RankBefore, int RankAfter, int WeaponsCleared);

/// <summary>
/// A fix an admin makes by hand, such as repairing a bad save or removing exploited XP.
/// </summary>
/// <remarks>
/// Ranks are otherwise up-only, but a correction is the one way down: taking XP away puts the rank back to
/// what the remaining XP earns, so removing exploited XP removes the ranks it bought.
/// </remarks>
/// <param name="Kind">What the correction does.</param>
/// <param name="Xp">The rank XP to add, or to take away when negative. Ignored by a reset.</param>
internal sealed record AdminCorrection(CorrectionKind Kind, long Xp = 0)
{
	/// <summary>
	/// Applies the correction and flags the player row for saving.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="ranks">The rank ladder.</param>
	/// <returns>The player's standing before and after.</returns>
	public CorrectionOutcome Apply(PlayerProgress player, RankLadder ranks)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(ranks);

		long xpBefore = player.RankXp;
		int rankBefore = Math.Max(player.Rank, ranks.RankFor(player.RankXp));
		int weaponsCleared = 0;

		if (Kind == CorrectionKind.Reset)
		{
			weaponsCleared = player.Weapons.Count;
			player.Weapons.Clear();
			player.RankXp = 0;
			player.Rank = 0;
			player.PendingXp = 0;
		}
		else
		{
			player.RankXp = Math.Max(0, xpBefore + Xp);
			int earned = ranks.RankFor(player.RankXp);
			player.Rank = Xp >= 0 ? Math.Max(rankBefore, earned) : earned;
		}

		// Feats are worked out from the rest of the progress, so they're recounted quietly on the next result
		// rather than announced again or kept after the progress behind them is gone.
		player.EarnedFeats = null;
		player.IsDirty = true;
		return new(xpBefore, player.RankXp, rankBefore, player.Rank, weaponsCleared);
	}
}
