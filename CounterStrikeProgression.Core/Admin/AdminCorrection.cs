// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Admin;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

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
	/// <summary>Gets a short description for logs and replies, such as "grant +500 XP" or "reset".</summary>
	public string Description => Kind == CorrectionKind.Reset
		? "reset"
		: string.Format(CultureInfo.InvariantCulture, "grant {0:+#;-#} XP", Xp);

	/// <summary>
	/// Reads the XP argument of a grant.
	/// </summary>
	/// <param name="text">The argument.</param>
	/// <param name="correction">The grant, when the text is a whole number other than 0.</param>
	/// <returns>Whether the text was a usable amount.</returns>
	public static bool TryParseGrant(string? text, out AdminCorrection? correction)
	{
		correction = null;
		if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long xp) || (xp == 0))
		{
			return false;
		}

		correction = new(CorrectionKind.Grant, xp);
		return true;
	}

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
			player.Contracts.Clear();
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

	/// <summary>
	/// Loads a player who isn't in memory and applies the correction to what's stored.
	/// </summary>
	/// <param name="store">Where progress is kept.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="ranks">The rank ladder.</param>
	/// <param name="cancellationToken">Cancels the load.</param>
	/// <returns>The outcome and the rows to write, or <see langword="null"/> for a player with nothing stored.</returns>
	public async Task<(CorrectionOutcome Outcome, ProgressSnapshot Rows)?> ApplyToStoredAsync(IProgressionStore store, ulong steamId, RankLadder ranks, CancellationToken cancellationToken)
	{
		Ensure.NotNull(store);
		if (await store.LoadAsync(steamId, cancellationToken).ConfigureAwait(false) is not PlayerProgress stored)
		{
			return null;
		}

		CorrectionOutcome outcome = Apply(stored, ranks);
		return (outcome, ProgressSnapshot.Capture(stored));
	}

	/// <summary>
	/// Writes the rows a correction changed. A reset also deletes the player's weapon and ladder rows.
	/// </summary>
	/// <param name="store">Where progress is kept.</param>
	/// <param name="rows">The rows captured right after <see cref="Apply"/>.</param>
	/// <param name="cancellationToken">Cancels the write.</param>
	/// <returns>A task that completes when the rows are written.</returns>
	public Task WriteAsync(IProgressionStore store, ProgressSnapshot rows, CancellationToken cancellationToken)
	{
		Ensure.NotNull(store);
		Ensure.NotNull(rows);
		return Kind == CorrectionKind.Reset
			? store.ResetAsync(rows.Player ?? throw new ArgumentException("A reset's rows must hold the player row.", nameof(rows)), cancellationToken)
			: store.SaveAsync(rows, cancellationToken);
	}

	/// <summary>
	/// Gets the reply for the admin once the correction is written.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="outcome">The outcome.</param>
	/// <returns>The reply.</returns>
	public string DoneMessage(string name, ulong steamId, CorrectionOutcome outcome)
	{
		Ensure.NotNull(outcome);
		return string.Format(
			CultureInfo.InvariantCulture,
			"Done: {0} for {1} ({2}). Rank XP {3:N0} -> {4:N0}, rank {5} -> {6}.",
			Description,
			name,
			steamId,
			outcome.XpBefore,
			outcome.XpAfter,
			RankName(outcome.RankBefore),
			RankName(outcome.RankAfter));
	}

	/// <summary>
	/// Gets the reply for the admin when the correction couldn't be written.
	/// </summary>
	/// <param name="name">The player's name.</param>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <returns>The reply.</returns>
	public string FailedMessage(string name, ulong steamId) =>
		$"Couldn't {Description} for {name} ({steamId}): they have no stored progress, or the database write failed. Nothing was changed in the database.";

	/// <summary>
	/// Gets the chat line that tells the corrected player what happened.
	/// </summary>
	/// <param name="outcome">The outcome.</param>
	/// <returns>The line.</returns>
	public string TargetMessage(CorrectionOutcome outcome)
	{
		Ensure.NotNull(outcome);
		return Kind == CorrectionKind.Reset
			? "An admin reset your progress."
			: string.Format(CultureInfo.InvariantCulture, "An admin adjusted your rank XP by {0:+#,0;-#,0}. You're now {1}.", outcome.XpAfter - outcome.XpBefore, RankName(outcome.RankAfter));
	}

	private static string RankName(int rank) => RankLadder.Names[Math.Clamp(rank, 0, RankLadder.TopRank)];
}
