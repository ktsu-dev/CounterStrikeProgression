// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Panel;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Builds the pages of the progress panel from a player's progress. Page 0 is the overview, and every later
/// page is one weapon, with the weapons the player uses most first.
/// </summary>
/// <param name="engine">The engine whose ranks, levels, and ladders to show.</param>
internal sealed class PanelBuilder(ProgressionEngine engine)
{
	private const int ClosestTiersShown = 5;

	private static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

	/// <summary>
	/// Gets the weapons in page order: those the player has used, most XP first, then the rest in catalogue order.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The weapons, one per page after the overview.</returns>
	public static IReadOnlyList<WeaponInfo> WeaponOrder(PlayerProgress player)
	{
		Ensure.NotNull(player);
		return
		[
			.. WeaponCatalog.All
				.Select((weapon, index) => (weapon, index, xp: player.Weapons.TryGetValue(weapon.Key, out WeaponProgress? p) ? p.Xp : 0))
				.OrderByDescending(w => w.xp)
				.ThenBy(w => w.index)
				.Select(w => w.weapon),
		];
	}

	/// <summary>
	/// Gets the number of pages: the overview plus one per weapon.
	/// </summary>
	public static int PageCount => WeaponCatalog.All.Count + 1;

	/// <summary>
	/// Builds one page.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="pageIndex">The page, where 0 is the overview.</param>
	/// <returns>The page.</returns>
	public PanelPage Page(PlayerProgress player, int pageIndex)
	{
		Ensure.NotNull(player);
		if (pageIndex <= 0)
		{
			return Overview(player);
		}

		IReadOnlyList<WeaponInfo> order = WeaponOrder(player);
		return Weapon(player, order[Math.Min(pageIndex - 1, order.Count - 1)]);
	}

	/// <summary>
	/// Builds the overview page: rank, XP, totals, and the challenges closest to their next tier.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The page.</returns>
	public PanelPage Overview(PlayerProgress player)
	{
		Ensure.NotNull(player);

		int rank = engine.Ranks.RankFor(player.RankXp);
		string progressLabel = rank >= RankLadder.TopRank
			? $"{Number(player.RankXp)} XP"
			: $"{Number(player.RankXp)} / {Number(engine.Ranks.ThresholdFor(rank + 1))} XP";

		long kills = player.Weapons.Values.Sum(w => w.Kills);
		long headshots = player.Weapons.Values.Sum(w => w.Headshots);
		int levelled = player.Weapons.Values.Count(w => w.Level > 1);
		int tiers = player.Weapons.Values.Sum(w => w.Challenges.Values.Sum(c => c.TierReached));

		List<PanelLine> lines =
		[
			new("Kills", Number(kills), LineState.InProgress),
			new("Headshots", Number(headshots), LineState.InProgress),
			new("Weapons levelled", $"{levelled} of {WeaponCatalog.All.Count}", LineState.InProgress),
			new("Tiers finished", Number(tiers), LineState.InProgress),
			PanelLine.Heading("Closest tiers"),
		];

		lines.AddRange(ClosestTiers(player, ClosestTiersShown));
		return new PanelPage(RankLadder.Names[rank], "Overview", engine.Ranks.ProgressToNext(player.RankXp), progressLabel, lines);
	}

	/// <summary>
	/// Builds a weapon page: its level, then its volume challenges, then its skill challenges.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="weapon">The weapon.</param>
	/// <returns>The page.</returns>
	public PanelPage Weapon(PlayerProgress player, WeaponInfo weapon)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(weapon);

		WeaponProgress? progress = player.Weapons.GetValueOrDefault(weapon.Key);
		long xp = progress?.Xp ?? 0;
		int level = engine.Levels.LevelFor(xp);
		string progressLabel = level >= engine.Levels.MaxLevel
			? "Max level"
			: $"{Number(xp - engine.Levels.ThresholdFor(level))} / {Number(engine.Levels.CostOf(level))} XP to level {level + 1}";

		IReadOnlyList<ChallengeDefinition> ladder = engine.Challenges.For(weapon);
		List<PanelLine> lines = [PanelLine.Heading("Volume")];
		lines.AddRange(ladder.Where(c => c.Kind == ChallengeKind.Volume).Select(c => ChallengeLine(c, progress)));
		lines.Add(PanelLine.Heading("Skill"));
		lines.AddRange(ladder.Where(c => c.Kind == ChallengeKind.Skill).Select(c => ChallengeLine(c, progress)));

		return new PanelPage(weapon.DisplayName, $"Level {level}", engine.Levels.ProgressToNext(xp), progressLabel, lines);
	}

	/// <summary>
	/// Writes a tier number as a Roman numeral.
	/// </summary>
	/// <param name="tier">The tier, starting at 1.</param>
	/// <returns>The numeral.</returns>
	public static string Numeral(int tier) => (tier >= 1) && (tier <= Numerals.Length) ? Numerals[tier - 1] : tier.ToString(CultureInfo.InvariantCulture);

	private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

	private PanelLine ChallengeLine(ChallengeDefinition challenge, WeaponProgress? progress)
	{
		ChallengeProgress? state = progress?.Challenges.GetValueOrDefault(challenge.Id);
		long count = state?.Count ?? 0;
		int reached = state?.TierReached ?? 0;
		int? target = challenge.NextTarget(reached);

		if (target is null)
		{
			return new($"{challenge.Name} {Numeral(reached)}", $"{Number(count)} done", LineState.Done);
		}

		int perTier = challenge.Kind == ChallengeKind.Skill ? engine.Settings.SkillTierXp : engine.Settings.VolumeTierXp;
		long reward = (long)perTier * (reached + 1);
		string label = $"{challenge.Name} {Numeral(reached + 1)}";
		string detail = $"{Number(count)}/{Number(target.Value)}  +{Number(reward)}";
		return new(label, detail, count > 0 ? LineState.InProgress : LineState.NotStarted);
	}

	/// <summary>
	/// Gets the challenges closest to their next tier, as a share of the way from the last tier to the next.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="count">How many to return.</param>
	/// <returns>The lines, closest first.</returns>
	public IEnumerable<PanelLine> ClosestTiers(PlayerProgress player, int count)
	{
		Ensure.NotNull(player);
		List<(double Fraction, PanelLine Line)> candidates = [];
		foreach (WeaponProgress progress in player.Weapons.Values)
		{
			WeaponInfo? weapon = WeaponCatalog.Find(progress.Weapon);
			if (weapon is null)
			{
				continue;
			}

			foreach ((string id, ChallengeProgress state) in progress.Challenges)
			{
				ChallengeDefinition? challenge = engine.Challenges.Find(weapon, id);
				int? target = challenge?.NextTarget(state.TierReached);
				if ((challenge is null) || (target is null) || (state.Count <= 0))
				{
					continue;
				}

				int floor = state.TierReached > 0 ? challenge.Tiers[state.TierReached - 1] : 0;
				double fraction = (double)(state.Count - floor) / Math.Max(1, target.Value - floor);
				string label = $"{weapon.DisplayName} {challenge.Name} {Numeral(state.TierReached + 1)}";
				candidates.Add((fraction, new PanelLine(label, $"{Number(state.Count)}/{Number(target.Value)}", LineState.InProgress)));
			}
		}

		return candidates.OrderByDescending(c => c.Fraction).Take(count).Select(c => c.Line);
	}
}
