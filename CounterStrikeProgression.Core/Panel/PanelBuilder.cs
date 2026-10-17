// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Panel;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Builds the pages of the progress panel from a player's progress. Page 0 is the overview, page 1 is the
/// contracts, and every later page is one weapon, with the weapons the player uses most first.
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
	/// <returns>The weapons, one per page after the overview and the contracts.</returns>
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
	/// Gets the number of pages: the overview, the contracts, and one per weapon.
	/// </summary>
	public static int PageCount => WeaponCatalog.All.Count + FirstWeaponPage;

	/// <summary>Gets the index of the contracts page.</summary>
	public static int ContractsPage => 1;

	private static int FirstWeaponPage => ContractsPage + 1;

	/// <summary>
	/// Builds one page.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="pageIndex">The page, where 0 is the overview and 1 the contracts.</param>
	/// <returns>The page.</returns>
	public PanelPage Page(PlayerProgress player, int pageIndex)
	{
		Ensure.NotNull(player);
		if (pageIndex <= 0)
		{
			return Overview(player);
		}

		if (pageIndex == ContractsPage)
		{
			return Contracts(player);
		}

		IReadOnlyList<WeaponInfo> order = WeaponOrder(player);
		return Weapon(player, order[Math.Min(pageIndex - FirstWeaponPage, order.Count - 1)]);
	}

	/// <summary>
	/// Builds the contracts page: today's contracts, then the week's, each with its count and reward.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The page.</returns>
	public PanelPage Contracts(PlayerProgress player)
	{
		Ensure.NotNull(player);

		IReadOnlyList<ActiveContract> active = engine.ActiveContracts;
		if (active.Count == 0)
		{
			return new PanelPage("Contracts", "Off", 0, "No contracts are running", []);
		}

		DateTime now = engine.Time.GetUtcNow().UtcDateTime;
		List<PanelLine> lines = [];
		int done = 0;
		ContractPeriod? section = null;
		foreach (ActiveContract contract in active)
		{
			if (contract.Definition.Period != section)
			{
				section = contract.Definition.Period;
				string name = section == ContractPeriod.Weekly ? "This week" : "Today";
				lines.Add(PanelLine.Heading($"{name}, {Remaining(contract.EndsAt - now)} left"));
			}

			ContractProgress? progress = player.Contracts.GetValueOrDefault((contract.PeriodKey, contract.Definition.Id));
			long count = Math.Min(progress?.Count ?? 0, contract.Definition.Target);
			if (progress?.Completed ?? false)
			{
				done++;
				lines.Add(new(contract.Definition.Description, "Done", LineState.Done));
				continue;
			}

			string detail = $"{Number(count)}/{Number(contract.Definition.Target)}  +{Number(contract.Xp)}";
			lines.Add(new(contract.Definition.Description, detail, count > 0 ? LineState.InProgress : LineState.NotStarted));
		}

		return new PanelPage("Contracts", "Daily and weekly", (double)done / active.Count, $"{done} of {active.Count} done", lines);
	}

	/// <summary>
	/// Writes how long is left in a contract's period, such as <c>5h 12m</c> or <c>3d 4h</c>.
	/// </summary>
	/// <param name="left">The time left.</param>
	/// <returns>The text.</returns>
	public static string Remaining(TimeSpan left)
	{
		if (left < TimeSpan.Zero)
		{
			left = TimeSpan.Zero;
		}

		return left.TotalDays >= 1
			? string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalDays}d {left.Hours}h")
			: string.Create(CultureInfo.InvariantCulture, $"{left.Hours}h {left.Minutes}m");
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

		List<PanelLine> lines = [];
		if (engine.FeaturedClass is WeaponClass featured)
		{
			lines.Add(new($"Featured: {FeaturedWeapon.Title(featured)}", $"{FeaturedWeapon.Times(engine.Settings.FeaturedWeaponMultiplier)} weapon XP", LineState.InProgress));
		}

		lines.AddRange(
		[
			new("Kills", Number(kills), LineState.InProgress),
			new("Headshots", Number(headshots), LineState.InProgress),
			new("Weapons levelled", $"{levelled} of {WeaponCatalog.All.Count}", LineState.InProgress),
			new("Tiers finished", Number(tiers), LineState.InProgress),
		]);

		IReadOnlyList<ActiveContract> contracts = engine.ActiveContracts;
		if (contracts.Count > 0)
		{
			int done = contracts.Count(c => player.Contracts.GetValueOrDefault((c.PeriodKey, c.Definition.Id))?.Completed ?? false);
			lines.Add(new("Contracts", $"{done} of {contracts.Count} done", done == contracts.Count ? LineState.Done : LineState.InProgress));
		}

		lines.Add(PanelLine.Heading("Closest tiers"));

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

		string subtitle = engine.IsFeatured(weapon)
			? $"Level {level} · {FeaturedWeapon.Times(engine.Settings.FeaturedWeaponMultiplier)} today"
			: $"Level {level}";
		return new PanelPage(weapon.DisplayName, subtitle, engine.Levels.ProgressToNext(xp), progressLabel, lines);
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
