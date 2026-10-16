// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Turns what a player did into rank XP, weapon XP, ladder progress, level-ups, and rank-ups.
/// </summary>
/// <remarks>
/// Rank XP from anything done to a bot is scaled by <see cref="ProgressionSettings.BotXpMultiplier"/>, but
/// weapon XP and ladder counts aren't, so practice against bots still moves a weapon forward. Tier and
/// level-up bonuses are paid in full, because they reward the ladder rather than one event.
/// </remarks>
/// <param name="settings">The XP values and thresholds.</param>
internal sealed class ProgressionEngine(ProgressionSettings settings)
{
	/// <summary>Gets the settings in use.</summary>
	public ProgressionSettings Settings { get; } = settings;

	/// <summary>Gets the rank thresholds.</summary>
	public RankLadder Ranks { get; } = new(settings.FirstRankXp, settings.RankGrowth);

	/// <summary>Gets the weapon level thresholds.</summary>
	public WeaponLevelCurve Levels { get; } = new(settings.WeaponLevelBaseXp, settings.WeaponLevelStepXp, settings.WeaponMaxLevel);

	/// <summary>Gets every weapon's ladder.</summary>
	public ChallengeCatalog Challenges { get; } = new(settings);

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

		Award award = new(combat.Weapon, combat.VictimIsBot);
		WeaponProgress weapon = player.Weapon(combat.Weapon.Key);
		weapon.IsDirty = true;

		if (combat.IsKill)
		{
			award.EventXp += Settings.KillXp;
			weapon.Kills++;
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
				EarnSkillShot(weapon, award, skillShot, result);
			}
		}

		Credit(player, weapon, award, result);
		return result;
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

		return result;
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

		return result;
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
		Award award = new(assist.Weapon, assist.VictimIsBot) { EventXp = Settings.AssistXp };
		WeaponProgress weapon = player.Weapon(assist.Weapon.Key);
		weapon.IsDirty = true;

		SkillShotDefinition flashAssist = SkillShotCatalog.Get(SkillShotCatalog.FlashAssist);
		if (assist.ByFlash && flashAssist.AppliesTo(assist.Weapon))
		{
			EarnSkillShot(weapon, award, flashAssist, result);
		}

		Credit(player, weapon, award, result);
		return result;
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

		return result;
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
			CreditRankXp(player, SkillShotXp(skillShot.Rarity), result);
			return result;
		}

		AwardSkillShot(player, clutch.LastWeapon, SkillShotCatalog.Clutch, vsBot: false, result);
		return result;
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

		Award award = new(weaponInfo, vsBot);
		WeaponProgress weapon = player.Weapon(weaponInfo.Key);
		weapon.IsDirty = true;
		EarnSkillShot(weapon, award, skillShot, result);
		Credit(player, weapon, award, result);
	}

	private void EarnSkillShot(WeaponProgress weapon, Award award, SkillShotDefinition skillShot, ProgressionResult result)
	{
		int xp = SkillShotXp(skillShot.Rarity);
		award.EventXp += xp;
		result.Notices.Add(new() { Kind = NoticeKind.SkillShot, Weapon = award.Weapon, SkillShot = skillShot, Xp = (long)Math.Round(ScaleForBots(xp, award.VsBot)) });
		Advance(weapon, award, skillShot.Id.WeakString, result);
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
		}
	}

	private void Credit(PlayerProgress player, WeaponProgress weapon, Award award, ProgressionResult result)
	{
		int levelBefore = weapon.Level;
		weapon.Xp += award.EventXp;
		weapon.Level = Math.Max(weapon.Level, Levels.LevelFor(weapon.Xp));
		for (int level = levelBefore + 1; level <= weapon.Level; level++)
		{
			long bonus = Settings.WeaponLevelUpBaseXp + ((long)Settings.WeaponLevelUpPerLevelXp * level);
			award.BonusXp += bonus;
			bool broadcast = (Settings.AnnounceWeaponLevelEvery > 0) && ((level % Settings.AnnounceWeaponLevelEvery) == 0);
			result.Notices.Add(new() { Kind = NoticeKind.WeaponLevelUp, Weapon = award.Weapon, Value = level, Xp = bonus, Broadcast = broadcast });
		}

		double rankXp = ScaleForBots(award.EventXp, award.VsBot) + award.BonusXp;
		CreditRankXp(player, rankXp, result);
	}

	private void CreditRankXp(PlayerProgress player, double xp, ProgressionResult result)
	{
		result.XpEarned += Math.Max(0, xp);
		double total = player.PendingXp + xp;
		long whole = (long)Math.Floor(total);
		player.PendingXp = total - whole;
		if (whole <= 0)
		{
			return;
		}

		player.RankXp += whole;
		player.IsDirty = true;
		result.XpGained += whole;

		int rank = Ranks.RankFor(player.RankXp);
		if (rank > player.Rank)
		{
			player.Rank = rank;
			result.Notices.Add(new() { Kind = NoticeKind.RankUp, Value = rank, Broadcast = true });
		}
	}

	private double ScaleForBots(double xp, bool vsBot) => vsBot ? xp * Settings.BotXpMultiplier : xp;

	private sealed class Award(WeaponInfo weapon, bool vsBot)
	{
		public WeaponInfo Weapon { get; } = weapon;

		public bool VsBot { get; } = vsBot;

		public long EventXp { get; set; }

		public long BonusXp { get; set; }
	}
}
