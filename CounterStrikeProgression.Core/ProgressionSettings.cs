// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core;

/// <summary>
/// Every XP value, multiplier, and detection threshold the plugin uses. All of them can be changed in
/// the plugin's config file, and the defaults are tuned for a small deathmatch server.
/// </summary>
public sealed class ProgressionSettings
{
	/// <summary>Gets or sets the rank XP for each damaging bullet.</summary>
	public int HitXp { get; set; } = 1;

	/// <summary>Gets or sets the rank XP for a kill.</summary>
	public int KillXp { get; set; } = 10;

	/// <summary>Gets or sets the extra rank XP when a kill is a headshot.</summary>
	public int HeadshotKillBonusXp { get; set; } = 5;

	/// <summary>Gets or sets the rank XP for an assist.</summary>
	public int AssistXp { get; set; } = 3;

	/// <summary>Gets or sets the rank XP for a common skill shot.</summary>
	public int CommonSkillShotXp { get; set; } = 10;

	/// <summary>Gets or sets the rank XP for a rare skill shot.</summary>
	public int RareSkillShotXp { get; set; } = 25;

	/// <summary>Gets or sets the rank XP for an epic skill shot.</summary>
	public int EpicSkillShotXp { get; set; } = 60;

	/// <summary>Gets or sets the flat rank XP paid when a weapon levels up.</summary>
	public int WeaponLevelUpBaseXp { get; set; } = 50;

	/// <summary>Gets or sets the extra rank XP per new weapon level when a weapon levels up.</summary>
	public int WeaponLevelUpPerLevelXp { get; set; } = 10;

	/// <summary>Gets or sets the rank XP per tier number for finishing a volume challenge tier.</summary>
	public int VolumeTierXp { get; set; } = 100;

	/// <summary>Gets or sets the rank XP per tier number for finishing a skill challenge tier.</summary>
	public int SkillTierXp { get; set; } = 250;

	/// <summary>Gets or sets the multiplier applied to rank XP earned against bots.</summary>
	public double BotXpMultiplier { get; set; } = 0.25;

	/// <summary>Gets or sets the XP needed to go from the lowest rank to the next.</summary>
	public long FirstRankXp { get; set; } = 5000;

	/// <summary>Gets or sets how much more each rank costs than the one before, as a multiplier.</summary>
	public double RankGrowth { get; set; } = 1.0765;

	/// <summary>Gets or sets the weapon XP needed to go from level 1 to level 2.</summary>
	public long WeaponLevelBaseXp { get; set; } = 300;

	/// <summary>Gets or sets the extra weapon XP each later level costs.</summary>
	public long WeaponLevelStepXp { get; set; } = 100;

	/// <summary>Gets or sets the highest level a weapon can reach.</summary>
	public int WeaponMaxLevel { get; set; } = 40;

	/// <summary>Gets or sets the kill counts for the five tiers of each weapon's Kills challenge.</summary>
	public IReadOnlyList<int> KillTiers { get; set; } = [25, 100, 250, 500, 1000];

	/// <summary>Gets or sets the headshot kill counts for the five tiers of each weapon's Headshots challenge.</summary>
	public IReadOnlyList<int> HeadshotTiers { get; set; } = [10, 50, 125, 250, 500];

	/// <summary>Gets or sets the hit counts for the five tiers of each weapon's Hits challenge.</summary>
	public IReadOnlyList<int> HitTiers { get; set; } = [100, 400, 1000, 2500, 5000];

	/// <summary>Gets or sets the chicken kill counts for the five tiers of each weapon's Chickens challenge.</summary>
	public IReadOnlyList<int> ChickenTiers { get; set; } = [1, 5, 15, 40, 100];

	/// <summary>Gets or sets the XP a chicken kill pays, to the weapon and to rank.</summary>
	public int ChickenXp { get; set; } = 2;

	/// <summary>Gets or sets the counts for the five tiers of a common skill shot challenge.</summary>
	public IReadOnlyList<int> CommonSkillTiers { get; set; } = [5, 20, 50, 100, 200];

	/// <summary>Gets or sets the counts for the five tiers of a rare skill shot challenge.</summary>
	public IReadOnlyList<int> RareSkillTiers { get; set; } = [2, 8, 20, 50, 100];

	/// <summary>Gets or sets the counts for the five tiers of an epic skill shot challenge.</summary>
	public IReadOnlyList<int> EpicSkillTiers { get; set; } = [1, 3, 8, 20, 50];

	/// <summary>
	/// Gets or sets the target's sideways speed across the shooter's view, in units per second, that counts as moving.
	/// Running straight at or away from the shooter doesn't count, because it barely moves the target on screen.
	/// </summary>
	public double MovingTargetSpeed { get; set; } = 170;

	/// <summary>Gets or sets the shooter speed, in units per second, that counts as running and gunning.</summary>
	public double RunAndGunSpeed { get; set; } = 150;

	/// <summary>Gets or sets the shooter speed, in units per second, that counts as moving while scoped.</summary>
	public double ScopedMovingSpeed { get; set; } = 100;

	/// <summary>Gets or sets how soon after landing, in seconds, a kill counts as a landing shot.</summary>
	public double LandingShotSeconds { get; set; } = 0.15;

	/// <summary>Gets or sets how soon after scoping in, in seconds, a kill counts as a quickscope.</summary>
	public double QuickscopeSeconds { get; set; } = 0.35;

	/// <summary>Gets or sets how soon after switching weapons, in seconds, a kill counts as a quick switch.</summary>
	public double QuickSwitchSeconds { get; set; } = 0.4;

	/// <summary>Gets or sets the turn, in degrees over the last few ticks, that counts as a flick.</summary>
	public double FlickDegrees { get; set; } = 60;

	/// <summary>Gets or sets the window, in seconds, in which kills chain into a multi-kill.</summary>
	public double MultiKillSeconds { get; set; } = 4;

	/// <summary>Gets or sets the window, in seconds, in which killing a teammate's killer counts as a trade.</summary>
	public double TradeSeconds { get; set; } = 3;

	/// <summary>Gets or sets how soon after being hurt, in seconds, a kill counts as bouncing back.</summary>
	public double BounceBackSeconds { get; set; } = 1;

	/// <summary>Gets or sets the health at or below which a kill counts as a last stand.</summary>
	public int LastStandHealth { get; set; } = 10;

	/// <summary>Gets or sets the distance, in units, within which a gun kill counts as point blank.</summary>
	public double PointBlankDistance { get; set; } = 150;

	/// <summary>Gets or sets the distance, in units, beyond which a pistol kill is a long shot.</summary>
	public double LongShotPistolDistance { get; set; } = 1200;

	/// <summary>Gets or sets the distance, in units, beyond which an SMG kill is a long shot.</summary>
	public double LongShotSmgDistance { get; set; } = 1500;

	/// <summary>Gets or sets the distance, in units, beyond which a rifle or machine gun kill is a long shot.</summary>
	public double LongShotRifleDistance { get; set; } = 2000;

	/// <summary>Gets or sets the distance, in units, beyond which a sniper kill is a long shot.</summary>
	public double LongShotSniperDistance { get; set; } = 2500;

	/// <summary>Gets or sets the distance, in units, beyond which a shotgun kill is a long shot.</summary>
	public double LongShotShotgunDistance { get; set; } = 500;

	/// <summary>Gets or sets the distance, in units, beyond which a pistol headshot kill earns Pistol sniper.</summary>
	public double PistolSniperDistance { get; set; } = 1500;

	/// <summary>Gets or sets how long, in seconds, an enemy must be blinded for a long flash.</summary>
	public double LongFlashSeconds { get; set; } = 3;

	/// <summary>Gets or sets the knife damage at or above which a knife kill is a backstab.</summary>
	public int BackstabDamage { get; set; } = 90;

	/// <summary>Gets or sets the share of the magazine, from 0 to 1, at or below which a reload is efficient.</summary>
	public double EfficientReloadFraction { get; set; } = 0.3;

	/// <summary>Gets or sets the shots a weapon must fire in one life before accuracy skill shots can be earned.</summary>
	public int AccuracyMinimumShots { get; set; } = 10;

	/// <summary>Gets or sets the accuracy, from 0 to 1, that earns Marksman.</summary>
	public double MarksmanAccuracy { get; set; } = 0.5;

	/// <summary>Gets or sets the accuracy, from 0 to 1, that earns Sharpshooter.</summary>
	public double SharpshooterAccuracy { get; set; } = 0.7;

	/// <summary>Gets or sets the hits a weapon must land in one life before Headhunter can be earned.</summary>
	public int HeadhunterMinimumHits { get; set; } = 5;

	/// <summary>Gets or sets the share of hits, from 0 to 1, that must be headshots for Headhunter.</summary>
	public double HeadhunterRatio { get; set; } = 0.5;

	/// <summary>Gets or sets the level interval announced to the whole server. Levels in between are told to the player only.</summary>
	public int AnnounceWeaponLevelEvery { get; set; } = 5;

	/// <summary>
	/// Gets or sets how much more weapon XP the day's featured weapon class earns. Challenge ladders still move one
	/// step per event, and rank XP isn't multiplied, though the faster weapon levels pay their bonuses sooner. 1 turns
	/// the featured weapon off.
	/// </summary>
	public double FeaturedWeaponMultiplier { get; set; } = 2;

	/// <summary>Gets or sets a value indicating whether daily and weekly contracts run.</summary>
	public bool ContractsEnabled { get; set; } = true;

	/// <summary>Gets or sets how many contracts each day has, up to the size of the daily pool.</summary>
	public int DailyContracts { get; set; } = 3;

	/// <summary>Gets or sets the rank XP for finishing a daily contract.</summary>
	public int DailyContractXp { get; set; } = 500;

	/// <summary>Gets or sets the rank XP for finishing the weekly contract.</summary>
	public int WeeklyContractXp { get; set; } = 2500;

	/// <summary>Gets or sets how many whole days away earn the welcome-back bonus when a player returns.</summary>
	public int WelcomeBackDays { get; set; } = 7;

	/// <summary>
	/// Gets or sets how much rank XP counts double for a player back from <see cref="WelcomeBackDays"/> or more days
	/// away, which is also the most bonus XP they can get. 0 turns the bonus off.
	/// </summary>
	public int WelcomeBackXp { get; set; } = 2000;

	/// <summary>Gets or sets how long each season lasts, in months. Seasons start on 1 January and every this many months after.</summary>
	public int SeasonMonths { get; set; } = 3;
}
