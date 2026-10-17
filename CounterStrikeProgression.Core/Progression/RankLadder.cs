// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// The matchmaking-style ranks, each split into stages, and the XP each stage needs. Ranks only ever go up.
/// </summary>
internal sealed class RankLadder
{
	/// <summary>The number of stages in each rank.</summary>
	public const int StagesPerRank = 5;

	/// <summary>
	/// The matchmaking rank names, lowest first, with the numbered ones such as Silver I to IV folded into one
	/// rank, because the stages number them instead. Each has <see cref="StagesPerRank"/> stages in <see cref="Names"/>.
	/// </summary>
	public static IReadOnlyList<string> RankNames { get; } =
	[
		"Silver",
		"Silver Elite",
		"Silver Elite Master",
		"Gold Nova",
		"Gold Nova Master",
		"Master Guardian",
		"Master Guardian Elite",
		"Distinguished Master Guardian",
		"Legendary Eagle",
		"Legendary Eagle Master",
		"Supreme Master First Class",
		"Global Elite",
	];

	/// <summary>
	/// The short form of each of <see cref="RankNames"/>, in the same order, such as <c>GN</c> for Gold Nova.
	/// </summary>
	public static IReadOnlyList<string> RankAbbreviations { get; } =
	[
		"S",
		"SE",
		"SEM",
		"GN",
		"GNM",
		"MG",
		"MGE",
		"DMG",
		"LE",
		"LEM",
		"SMFC",
		"GE",
	];

	// Declared before Names and Tags, because static initializers run in order.
	private static readonly string[] StageNumerals = ["I", "II", "III", "IV", "V"];

	/// <summary>
	/// The name of every stage, lowest first, such as <c>Gold Nova · III</c>. A player's rank is an index into this list.
	/// </summary>
	public static IReadOnlyList<string> Names { get; } =
		[.. RankNames.SelectMany(name => StageNumerals.Select(stage => $"{name} · {stage}"))];

	/// <summary>
	/// The short tag for every stage, lowest first, such as <c>[GN III]</c>, for the scoreboard clan tag and
	/// before chat messages. Indexed the same as <see cref="Names"/>.
	/// </summary>
	public static IReadOnlyList<string> Tags { get; } =
		[.. RankAbbreviations.SelectMany(name => StageNumerals.Select(stage => $"[{name} {stage}]"))];

	private readonly long[] thresholds;

	/// <summary>
	/// Initializes a new instance of the <see cref="RankLadder"/> class.
	/// </summary>
	/// <param name="firstRankXp">XP needed to go from the lowest rank to the next.</param>
	/// <param name="growth">How much more each rank costs than the one before, as a multiplier.</param>
	public RankLadder(long firstRankXp, double growth)
	{
		thresholds = new long[Names.Count];
		double step = firstRankXp;
		double total = 0;
		for (int i = 1; i < thresholds.Length; i++)
		{
			total += step;
			thresholds[i] = (long)Math.Round(total);
			step *= growth;
		}
	}

	/// <summary>
	/// Gets the index of the highest rank.
	/// </summary>
	public static int TopRank => Names.Count - 1;

	/// <summary>
	/// Gets the short tag for a rank, such as <c>[GN III]</c>.
	/// </summary>
	/// <param name="rank">The rank index, clamped to the ladder.</param>
	/// <returns>The tag.</returns>
	public static string TagFor(int rank) => Tags[Math.Clamp(rank, 0, TopRank)];

	/// <summary>
	/// Gets the total XP needed to reach a rank.
	/// </summary>
	/// <param name="rank">The rank index, where 0 is Silver I.</param>
	/// <returns>The cumulative XP for that rank.</returns>
	public long ThresholdFor(int rank) => thresholds[Math.Clamp(rank, 0, TopRank)];

	/// <summary>
	/// Gets the rank a total XP earns.
	/// </summary>
	/// <param name="xp">The player's total rank XP.</param>
	/// <returns>The rank index.</returns>
	public int RankFor(long xp)
	{
		int rank = 0;
		while (rank < TopRank && xp >= thresholds[rank + 1])
		{
			rank++;
		}

		return rank;
	}

	/// <summary>
	/// Gets how far a player is from their current rank to the next, from 0 to 1.
	/// </summary>
	/// <param name="xp">The player's total rank XP.</param>
	/// <returns>The fraction of the way to the next rank, or 1 at the top rank.</returns>
	public double ProgressToNext(long xp)
	{
		int rank = RankFor(xp);
		if (rank >= TopRank)
		{
			return 1;
		}

		long floor = thresholds[rank];
		long ceiling = thresholds[rank + 1];
		return (double)(xp - floor) / (ceiling - floor);
	}
}
