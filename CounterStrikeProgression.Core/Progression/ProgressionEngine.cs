// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Turns what a player did into rank XP, weapon XP, ladder progress, level-ups, and rank-ups.
/// </summary>
/// <remarks>
/// Rank XP from anything done to a bot is scaled by <see cref="ProgressionSettings.BotXpMultiplier"/>, but
/// weapon XP and ladder counts aren't, so practice against bots still moves a weapon forward. Tier and
/// level-up bonuses are paid in full, because they reward the ladder rather than one event. The day's featured
/// weapon class earns <see cref="ProgressionSettings.FeaturedWeaponMultiplier"/> times the XP for each event, and
/// contracts count what each event did once it's applied.
/// </remarks>
/// <param name="settings">The XP values and thresholds.</param>
/// <param name="time">The clock that decides the day, or the system clock when omitted.</param>
internal sealed class ProgressionEngine(ProgressionSettings settings, TimeProvider? time = null)
{
	/// <summary>Gets the settings in use.</summary>
	public ProgressionSettings Settings { get; } = settings;

	/// <summary>Gets the clock that decides the day.</summary>
	public TimeProvider Time { get; } = time ?? TimeProvider.System;

	/// <summary>Gets the current UTC day.</summary>
	public DateOnly Today => DailyRotation.Day(Time.GetUtcNow().UtcDateTime);

	/// <summary>Gets the weapon class featured today, or <see langword="null"/> when the featured weapon is off.</summary>
	public WeaponClass? FeaturedClass => Settings.FeaturedWeaponMultiplier > 1 ? DailyRotation.FeaturedClass(Today) : null;

	/// <summary>Gets the contracts running today.</summary>
	public IReadOnlyList<ActiveContract> ActiveContracts => ContractCatalog.Active(Today, Settings);

	/// <summary>Gets the rank thresholds.</summary>
	public RankLadder Ranks { get; } = new(settings.FirstRankXp, settings.RankGrowth);

	/// <summary>Gets the weapon level thresholds.</summary>
	public WeaponLevelCurve Levels { get; } = new(settings.WeaponLevelBaseXp, settings.WeaponLevelStepXp, settings.WeaponMaxLevel);

	/// <summary>Gets every weapon's ladder.</summary>
	public ChallengeCatalog Challenges { get; } = new(settings);

	/// <summary>Gets the feats.</summary>
	public FeatCatalog Feats { get; } = new(settings);

	/// <summary>
	/// Adds a notice for each feat a result just earned. Call it after every result.
	/// </summary>
	/// <param name="player">The player's progress, with the result already applied.</param>
	/// <param name="result">The result to add the notices to.</param>
	/// <remarks>
	/// The first call for a player records the feats they already have without announcing them. Feats move only
	/// when something else is earned, so later calls check them only for results with notices. A feat earned by
	/// the very first result of a session is recorded quietly.
	/// </remarks>
	public void CheckFeats(PlayerProgress player, ProgressionResult result)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(result);

		if (player.EarnedFeats is null)
		{
			player.EarnedFeats = new(Feats.Earned(player).Select(f => f.Id), StringComparer.Ordinal);
			return;
		}

		if (result.Notices.Count == 0)
		{
			return;
		}

		foreach (FeatDefinition feat in Feats.Earned(player))
		{
			if (player.EarnedFeats.Add(feat.Id))
			{
				result.Notices.Add(new() { Kind = NoticeKind.FeatEarned, Feat = feat, Broadcast = true });
			}
		}
	}

	/// <summary>
	/// Credits a damaging bullet or a kill.
	/// </summary>
	/// <param name="player">The attacker's progress.</param>
	/// <param name="combat">The hit or kill.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyCombat(PlayerProgress player, CombatEvent combat)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(combat);

		ProgressionResult result = new();

		// A molotov or smoke damages on every burn tick, so only the kill counts for grenades.
		if (combat.IsTeammate || (!combat.IsKill && (combat.Weapon.Class == WeaponClass.Grenade)))
		{
			return result;
		}

		Award award = NewAward(combat.Weapon, combat.VictimIsBot);
		WeaponProgress weapon = player.Weapon(combat.Weapon.Key);
		weapon.IsDirty = true;

		if (combat.IsKill)
		{
			award.EventXp += Settings.KillXp;
			weapon.Kills++;
			result.Kills++;
			DayProgress day = player.Day(Today);
			day.Kills++;
			day.IsDirty = true;
			result.Signals.Add(new(SignalKind.Kill, combat.Weapon, combat.IsHeadshot));
			Advance(weapon, award, ChallengeDefinition.KillsId, result);
			if (combat.IsHeadshot)
			{
				award.EventXp += Settings.HeadshotKillBonusXp;
				weapon.Headshots++;
				Advance(weapon, award, ChallengeDefinition.HeadshotsId, result);
			}
		}
		else
		{
			award.EventXp += Settings.HitXp;
			weapon.Hits++;
			Advance(weapon, award, ChallengeDefinition.HitsId, result);
		}

		SkillTrigger trigger = combat.IsKill ? SkillTrigger.Kill : SkillTrigger.Hit;
		foreach (SkillShotDefinition skillShot in SkillShotCatalog.For(trigger))
		{
			if (skillShot.AppliesTo(combat.Weapon) && (skillShot.Detect?.Invoke(combat, Settings) == true))
			{
				EarnSkillShot(player, weapon, award, skillShot, result);
			}
		}

		Credit(player, weapon, award, result);
		return Settle(player, result);
	}

	/// <summary>
	/// Credits a chicken kill: a little weapon and rank XP, a step on the weapon's Chickens challenge, and any
	/// chicken skill shots it earned.
	/// </summary>
	/// <param name="player">The killer's progress.</param>
	/// <param name="chicken">The kill, with the chicken as the victim.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyChickenKill(PlayerProgress player, CombatEvent chicken)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(chicken);

		ProgressionResult result = new();
		Award award = NewAward(chicken.Weapon, vsBot: false);
		WeaponProgress progress = player.Weapon(chicken.Weapon.Key);
		progress.IsDirty = true;
		award.EventXp += Settings.ChickenXp;
		result.Signals.Add(new(SignalKind.Chicken, chicken.Weapon));
		Advance(progress, award, ChallengeDefinition.ChickensId, result);
		foreach (SkillShotDefinition skillShot in SkillShotCatalog.For(SkillTrigger.Chicken))
		{
			if (skillShot.AppliesTo(chicken.Weapon) && (skillShot.Detect?.Invoke(chicken, Settings) == true))
			{
				EarnSkillShot(player, progress, award, skillShot, result);
			}
		}

		Credit(player, progress, award, result);
		return Settle(player, result);
	}

	/// <summary>
	/// Counts a shot fired, for accuracy.
	/// </summary>
	/// <param name="player">The shooter's progress.</param>
	/// <param name="weapon">The weapon fired.</param>
	public static void RecordShot(PlayerProgress player, WeaponInfo weapon)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(weapon);

		WeaponProgress progress = player.Weapon(weapon.Key);
		progress.Shots++;
		progress.IsDirty = true;
	}

	/// <summary>
	/// Checks a reload for Efficient reload.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="reload">The reload as it started.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyReload(PlayerProgress player, ReloadEvent reload)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(reload);

		ProgressionResult result = new();
		bool efficient = reload.Weapon.IsGun
			&& !reload.LoadsShellByShell
			&& (reload.MagazineSize > 0)
			&& (reload.RoundsLeft >= 0)
			&& (((double)reload.RoundsLeft / reload.MagazineSize) <= Settings.EfficientReloadFraction);

		if (efficient)
		{
			AwardSkillShot(player, reload.Weapon, SkillShotCatalog.EfficientReload, vsBot: false, result);
		}

		return Settle(player, result);
	}

	/// <summary>
	/// Checks how a weapon did over a life for the accuracy skill shots.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="life">The weapon's shots and hits that life.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyLifeEnd(PlayerProgress player, WeaponLifeStats life)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(life);

		ProgressionResult result = new();
		if (!life.Weapon.IsGun)
		{
			return result;
		}

		if (life.Shots >= Settings.AccuracyMinimumShots)
		{
			double accuracy = Math.Min(1.0, (double)life.Hits / life.Shots);
			if (accuracy >= Settings.MarksmanAccuracy)
			{
				AwardSkillShot(player, life.Weapon, SkillShotCatalog.Marksman, life.MostlyBots, result);
			}

			if (accuracy >= Settings.SharpshooterAccuracy)
			{
				AwardSkillShot(player, life.Weapon, SkillShotCatalog.Sharpshooter, life.MostlyBots, result);
			}
		}

		if ((life.Hits >= Settings.HeadhunterMinimumHits) && (((double)life.HeadshotHits / life.Hits) >= Settings.HeadhunterRatio))
		{
			AwardSkillShot(player, life.Weapon, SkillShotCatalog.Headhunter, life.MostlyBots, result);
		}

		return Settle(player, result);
	}

	/// <summary>
	/// Credits an assist, and Flash assist when the assist came from a flashbang.
	/// </summary>
	/// <param name="player">The assister's progress.</param>
	/// <param name="assist">The assist.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyAssist(PlayerProgress player, AssistEvent assist)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(assist);

		ProgressionResult result = new();
		Award award = NewAward(assist.Weapon, assist.VictimIsBot);
		award.EventXp = Settings.AssistXp;
		WeaponProgress weapon = player.Weapon(assist.Weapon.Key);
		weapon.IsDirty = true;
		result.Signals.Add(new(SignalKind.Assist, assist.Weapon));

		SkillShotDefinition flashAssist = SkillShotCatalog.Get(SkillShotCatalog.FlashAssist);
		if (assist.ByFlash && flashAssist.AppliesTo(assist.Weapon))
		{
			EarnSkillShot(player, weapon, award, flashAssist, result);
		}

		Credit(player, weapon, award, result);
		return Settle(player, result);
	}

	/// <summary>
	/// Checks a blinded enemy for Long flash.
	/// </summary>
	/// <param name="player">The thrower's progress.</param>
	/// <param name="flash">The blinding.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyFlash(PlayerProgress player, FlashEvent flash)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(flash);

		ProgressionResult result = new();
		if (flash.BlindSeconds >= Settings.LongFlashSeconds)
		{
			AwardSkillShot(player, flash.Weapon, SkillShotCatalog.LongFlash, flash.VictimIsBot, result);
		}

		return Settle(player, result);
	}

	/// <summary>
	/// Credits a clutch. The ladder progress goes to the weapon of the player's last kill that round.
	/// </summary>
	/// <param name="player">The clutcher's progress.</param>
	/// <param name="clutch">The clutch.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult ApplyClutch(PlayerProgress player, ClutchEvent clutch)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(clutch);

		ProgressionResult result = new();
		if (clutch.EnemiesWhenAlone < 1)
		{
			return result;
		}

		SkillShotDefinition skillShot = SkillShotCatalog.Get(SkillShotCatalog.Clutch);
		if (clutch.LastWeapon is null)
		{
			// No weapon to put the ladder progress on, so the clutch pays XP alone.
			ProgressionNotice notice = new() { Kind = NoticeKind.SkillShot, SkillShot = skillShot, Xp = SkillShotXp(skillShot.Rarity) };
			result.Notices.Add(notice);
			result.Signals.Add(new(SignalKind.SkillShot, SkillShot: skillShot));
			CountSkillShotToday(player);
			CreditRankXp(player, SkillShotXp(skillShot.Rarity), result);
			return Settle(player, result);
		}

		AwardSkillShot(player, clutch.LastWeapon, SkillShotCatalog.Clutch, vsBot: false, result);
		return Settle(player, result);
	}

	/// <summary>
	/// Gets the rank XP a skill shot of a rarity pays.
	/// </summary>
	/// <param name="rarity">The rarity.</param>
	/// <returns>The XP.</returns>
	public int SkillShotXp(Rarity rarity) => rarity switch
	{
		Rarity.Epic => Settings.EpicSkillShotXp,
		Rarity.Rare => Settings.RareSkillShotXp,
		_ => Settings.CommonSkillShotXp,
	};

	private void AwardSkillShot(PlayerProgress player, WeaponInfo weaponInfo, SkillShotId id, bool vsBot, ProgressionResult result)
	{
		SkillShotDefinition skillShot = SkillShotCatalog.Get(id);
		if (!skillShot.AppliesTo(weaponInfo))
		{
			return;
		}

		Award award = NewAward(weaponInfo, vsBot);
		WeaponProgress weapon = player.Weapon(weaponInfo.Key);
		weapon.IsDirty = true;
		EarnSkillShot(player, weapon, award, skillShot, result);
		Credit(player, weapon, award, result);
	}

	private void EarnSkillShot(PlayerProgress player, WeaponProgress weapon, Award award, SkillShotDefinition skillShot, ProgressionResult result)
	{
		int xp = SkillShotXp(skillShot.Rarity);
		award.EventXp += xp;
		result.Notices.Add(new() { Kind = NoticeKind.SkillShot, Weapon = award.Weapon, SkillShot = skillShot, Xp = (long)Math.Round(ScaleForBots(xp * award.Multiplier, award.VsBot)) });
		result.Signals.Add(new(SignalKind.SkillShot, award.Weapon, SkillShot: skillShot));
		CountSkillShotToday(player);
		Advance(weapon, award, skillShot.Id.WeakString, result);
	}

	private void CountSkillShotToday(PlayerProgress player)
	{
		DayProgress day = player.Day(Today);
		day.SkillShots++;
		day.IsDirty = true;
	}

	private void Advance(WeaponProgress weapon, Award award, string challengeId, ProgressionResult result)
	{
		ChallengeDefinition? definition = Challenges.Find(award.Weapon, challengeId);
		if (definition is null)
		{
			return;
		}

		ChallengeProgress progress = weapon.Challenge(challengeId);
		progress.Count++;
		progress.IsDirty = true;

		int tier = definition.TierFor(progress.Count);
		while (progress.TierReached < tier)
		{
			progress.TierReached++;
			int perTier = definition.Kind == ChallengeKind.Skill ? Settings.SkillTierXp : Settings.VolumeTierXp;
			long bonus = (long)perTier * progress.TierReached;
			award.BonusXp += bonus;
			result.Notices.Add(new()
			{
				Kind = NoticeKind.TierCompleted,
				Weapon = award.Weapon,
				ChallengeName = definition.Name,
				Value = progress.TierReached,
				Xp = bonus,
				Broadcast = true,
			});
			result.Signals.Add(new(SignalKind.Tier, award.Weapon));
		}
	}

	private void Credit(PlayerProgress player, WeaponProgress weapon, Award award, ProgressionResult result)
	{
		int levelBefore = weapon.Level;
		weapon.Xp += (long)Math.Round(award.EventXp * award.Multiplier);
		weapon.Level = Math.Max(weapon.Level, Levels.LevelFor(weapon.Xp));
		for (int level = levelBefore + 1; level <= weapon.Level; level++)
		{
			long bonus = Settings.WeaponLevelUpBaseXp + ((long)Settings.WeaponLevelUpPerLevelXp * level);
			award.BonusXp += bonus;
			bool broadcast = (Settings.AnnounceWeaponLevelEvery > 0) && ((level % Settings.AnnounceWeaponLevelEvery) == 0);
			result.Notices.Add(new() { Kind = NoticeKind.WeaponLevelUp, Weapon = award.Weapon, Value = level, Xp = bonus, Broadcast = broadcast });
			result.Signals.Add(new(SignalKind.WeaponLevel, award.Weapon));
		}

		double rankXp = ScaleForBots(award.EventXp * award.Multiplier, award.VsBot) + award.BonusXp;
		CreditRankXp(player, rankXp, result);
	}

	/// <summary>
	/// Counts what an event did toward the running contracts, pays for any it finishes, and drops contract and
	/// day rows from earlier periods once they're saved.
	/// </summary>
	private ProgressionResult Settle(PlayerProgress player, ProgressionResult result)
	{
		IReadOnlyList<ActiveContract> active = ActiveContracts;
		DropStale(player, active);
		foreach (ContractSignal signal in result.Signals)
		{
			foreach (ActiveContract contract in active)
			{
				if (!contract.Definition.Counts(signal))
				{
					continue;
				}

				ContractProgress progress = player.Contract(contract.PeriodKey, contract.Definition.Id);
				if (progress.Completed)
				{
					continue;
				}

				progress.Count++;
				progress.IsDirty = true;
				if (progress.Count >= contract.Definition.Target)
				{
					progress.Completed = true;
					result.Notices.Add(new() { Kind = NoticeKind.ContractCompleted, Contract = contract, Xp = contract.Xp });
					CreditRankXp(player, contract.Xp, result);
				}
			}
		}

		result.Signals.Clear();
		return result;
	}

	private void DropStale(PlayerProgress player, IReadOnlyList<ActiveContract> active)
	{
		// A row is stale once its contract stops running. It goes only after it has saved, so the count isn't lost.
		bool IsRunning((string Period, string Contract) key) => active.Any(a => (a.PeriodKey == key.Period) && (a.Definition.Id == key.Contract));
		if (!player.Contracts.Keys.All(IsRunning))
		{
			foreach ((string Period, string Contract) key in player.Contracts.Keys.ToList())
			{
				if (!player.Contracts[key].IsDirty && !IsRunning(key))
				{
					player.Contracts.Remove(key);
				}
			}
		}

		if (player.Days.Count > 1)
		{
			DateOnly today = Today;
			foreach (DateOnly day in player.Days.Keys.ToList())
			{
				if ((day != today) && !player.Days[day].IsDirty)
				{
					player.Days.Remove(day);
				}
			}
		}
	}

	/// <summary>
	/// Adds or removes rank XP by hand, for an admin putting a mistake right. Added XP announces any rank-ups it
	/// reaches, but neither counts toward the day's XP nor seasons. Removing XP is the one way a rank can go down.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <param name="xp">The XP to add, or a negative amount to remove.</param>
	/// <returns>What it earned.</returns>
	public ProgressionResult Grant(PlayerProgress player, long xp)
	{
		Ensure.NotNull(player);

		ProgressionResult result = new();
		if (xp > 0)
		{
			CreditRankXp(player, xp, result, countToday: false);
			return result;
		}

		player.RankXp = Math.Max(0, player.RankXp + xp);
		player.Rank = Ranks.RankFor(player.RankXp);
		player.IsDirty = true;
		DayProgress day = player.Day(Today);
		day.RankXp = player.RankXp;
		day.IsDirty = true;
		return result;
	}

	private Award NewAward(WeaponInfo weapon, bool vsBot) =>
		new(weapon, vsBot, weapon.Class == FeaturedClass ? Settings.FeaturedWeaponMultiplier : 1);

	private void CreditRankXp(PlayerProgress player, double xp, ProgressionResult result, bool countToday = true)
	{
		result.XpEarned += Math.Max(0, xp);
		double total = player.PendingXp + xp;
		long whole = (long)Math.Floor(total);
		player.PendingXp = total - whole;
		if (whole <= 0)
		{
			return;
		}

		// A stored rank can trail the XP already held, such as after the ladder changes. That gap is closed
		// quietly, so only the ranks this XP reaches are announced.
		int before = Math.Max(player.Rank, Ranks.RankFor(player.RankXp));
		player.RankXp += whole;
		player.IsDirty = true;
		result.XpGained += whole;

		DayProgress day = player.Day(Today);
		day.RankXp = player.RankXp;
		if (countToday)
		{
			day.Xp += whole;
		}

		day.IsDirty = true;

		int rank = Ranks.RankFor(player.RankXp);
		player.Rank = Math.Max(before, rank);
		if (rank > before)
		{
			result.Notices.Add(new() { Kind = NoticeKind.RankUp, Value = rank, Broadcast = true });
		}
	}

	private double ScaleForBots(double xp, bool vsBot) => vsBot ? xp * Settings.BotXpMultiplier : xp;

	private sealed class Award(WeaponInfo weapon, bool vsBot, double multiplier)
	{
		public WeaponInfo Weapon { get; } = weapon;

		public bool VsBot { get; } = vsBot;

		public double Multiplier { get; } = multiplier;

		public long EventXp { get; set; }

		public long BonusXp { get; set; }
	}
}
