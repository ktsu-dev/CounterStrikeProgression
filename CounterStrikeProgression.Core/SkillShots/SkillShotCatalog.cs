// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.SkillShots;

using System.Collections.Frozen;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Every skill shot the plugin recognizes. The ids are stored in the database as challenge ids, so an id must
/// never be renamed once released. Adding new skill shots is always safe.
/// </summary>
internal static class SkillShotCatalog
{
	/// <summary>Gets the id of the efficient reload skill shot, which the engine checks on reloads.</summary>
	public static SkillShotId EfficientReload { get; } = SkillShotId.Create("efficient_reload");

	/// <summary>Gets the id of the 50% accuracy skill shot, which the engine checks when a life ends.</summary>
	public static SkillShotId Marksman { get; } = SkillShotId.Create("marksman");

	/// <summary>Gets the id of the 70% accuracy skill shot, which the engine checks when a life ends.</summary>
	public static SkillShotId Sharpshooter { get; } = SkillShotId.Create("sharpshooter");

	/// <summary>Gets the id of the headshot ratio skill shot, which the engine checks when a life ends.</summary>
	public static SkillShotId Headhunter { get; } = SkillShotId.Create("headhunter");

	/// <summary>Gets the id of the flash assist skill shot, which the engine checks on assists.</summary>
	public static SkillShotId FlashAssist { get; } = SkillShotId.Create("flash_assist");

	/// <summary>Gets the id of the long flash skill shot, which the engine checks when an enemy is blinded.</summary>
	public static SkillShotId LongFlash { get; } = SkillShotId.Create("long_flash");

	/// <summary>Gets the id of the clutch skill shot, which the engine checks at round end.</summary>
	public static SkillShotId Clutch { get; } = SkillShotId.Create("clutch");

	/// <summary>
	/// Gets every skill shot, in the order the progress panel lists them.
	/// </summary>
	public static IReadOnlyList<SkillShotDefinition> All { get; } =
	[
		// Movement
		Hit("moving_target", "Moving target", "Hit a target moving fast across your view", Rarity.Common, Gun, (e, s) => e.VictimSidewaysSpeed >= s.MovingTargetSpeed),
		Hit("airborne_target", "Airborne target", "Hit a target in the air", Rarity.Rare, Gun, (e, _) => !e.Victim.OnGround && !e.Victim.OnLadder && !e.Victim.InWater),
		Hit("jumpshot", "Jumpshot", "Hit while you're in the air", Rarity.Rare, Gun, (e, _) => IsAirborne(e.Attacker)),
		Kill("air_to_air", "Air to air", "Kill an airborne target while airborne", Rarity.Epic, Gun, (e, _) => IsAirborne(e.Attacker) && IsAirborne(e.Victim)),
		Kill("run_and_gun", "Run and gun", "Kill while moving fast", Rarity.Common, RifleSniperOrMachineGun, (e, s) => e.Attacker.OnGround && (e.Attacker.Speed >= s.RunAndGunSpeed)),
		Kill("crouch_kill", "Crouch kill", "Kill while crouched", Rarity.Common, NotGrenade, (e, _) => e.Attacker.Ducked),
		Kill("ladder_kill", "Ladder kill", "Kill while on a ladder", Rarity.Epic, NotGrenade, (e, _) => e.Attacker.OnLadder),
		Kill("silent_kill", "Silent kill", "Kill while shift-walking", Rarity.Common, GunOrKnife, (e, _) => e.Attacker.Walking && e.Attacker.OnGround),
		Kill("landing_shot", "Landing shot", "Kill the moment you land", Rarity.Rare, Gun, (e, s) => e.Attacker.OnGround && (e.SecondsSinceAttackerLanded <= s.LandingShotSeconds)),
		Kill("water_kill", "Water kill", "Kill while standing in water", Rarity.Rare, NotGrenade, (e, _) => e.Attacker.InWater),

		// Distance
		Kill("long_shot", "Long shot", "Kill from long range", Rarity.Rare, Gun, (e, s) => e.Distance >= LongShotDistance(e.Weapon.Class, s)),
		Kill("point_blank", "Point blank", "Kill up close with a gun", Rarity.Common, GunNotShotgun, (e, s) => e.Distance <= s.PointBlankDistance),
		Kill("pistol_sniper", "Pistol sniper", "Headshot kill with a pistol from very far away", Rarity.Epic, Pistol, (e, s) => e.IsHeadshot && (e.Distance >= s.PistolSniperDistance)),

		// Penetration and visibility
		Hit("wallbang_hit", "Wallbang hit", "Hit someone through a wall", Rarity.Common, Gun, (e, _) => e.Penetrations > 0),
		Kill("wallbang", "Wallbang", "Kill through a wall", Rarity.Rare, Gun, (e, _) => e.Penetrations > 0),
		Kill("double_wallbang", "Double wallbang", "Kill through two or more objects", Rarity.Epic, Gun, (e, _) => e.Penetrations >= 2),
		Kill("through_smoke", "Through the smoke", "Kill through a smoke", Rarity.Rare, Gun, (e, _) => e.ThroughSmoke),
		Kill("blind_kill", "Blind kill", "Kill while flashed", Rarity.Epic, NotGrenade, (e, _) => e.AttackerBlind),

		// Scope
		Kill("no_scope", "No-scope", "Kill with a scoped weapon without scoping", Rarity.Rare, Scoped, (e, _) => e.NoScope),
		Kill("quickscope", "Quickscope", "Kill moments after scoping in", Rarity.Rare, Scoped, (e, s) => !e.NoScope && (e.SecondsSinceScoped <= s.QuickscopeSeconds)),
		Kill("flying_no_scope", "Flying no-scope", "No-scope kill while airborne", Rarity.Epic, Scoped, (e, _) => e.NoScope && IsAirborne(e.Attacker)),
		Kill("scoped_on_the_move", "Scoped on the move", "Scoped kill while moving", Rarity.Rare, Scoped, (e, s) => !e.NoScope && e.Attacker.OnGround && (e.Attacker.Speed >= s.ScopedMovingSpeed)),

		// Utility and melee
		Kill("grenade_kill", "Grenade kill", "Kill with an HE grenade", Rarity.Rare, Named("hegrenade"), (_, _) => true),
		Kill("fire_kill", "Fire kill", "Kill with a molotov or incendiary", Rarity.Rare, Named("inferno"), (_, _) => true),
		Kill("grenade_impact", "Grenade impact", "Kill by throwing a flashbang, decoy, or smoke at someone", Rarity.Epic, Named("flashbang", "decoy", "smokegrenade"), (_, _) => true),
		Kill("knife_kill", "Knife kill", "Kill with the knife", Rarity.Rare, Knife, (_, _) => true),
		Kill("backstab", "Backstab", "Kill with a heavy knife hit", Rarity.Rare, Knife, (e, s) => e.Damage >= s.BackstabDamage),
		Kill("zeus_kill", "Zeus kill", "Kill with the Zeus", Rarity.Rare, Taser, (_, _) => true),
		Stat(FlashAssist, "Flash assist", "Get an assist by blinding the victim", Rarity.Common, SkillTrigger.Assist, Named("flashbang")),
		Stat(LongFlash, "Long flash", "Blind an enemy for a long time", Rarity.Common, SkillTrigger.Flash, Named("flashbang")),

		// Timing and aim
		Kill("quick_switch", "Quick switch", "Kill right after switching weapons", Rarity.Rare, GunOrKnife, (e, s) => e.SecondsSinceWeaponSwitch <= s.QuickSwitchSeconds),
		Kill("fresh_mag", "Fresh mag", "Kill with the first shot after reloading", Rarity.Common, Gun, (e, _) => e.FirstShotAfterReload),
		Kill("last_bullet", "Last bullet", "Kill with the last round in the magazine", Rarity.Rare, Gun, (e, _) => e.ClipAfterShot == 0),
		Kill("one_tap", "One tap", "Headshot kill with the first shot of a spray", Rarity.Rare, GunNotShotgun, (e, _) => e.IsHeadshot && (e.ShotsInSpray == 1)),
		Kill("flick", "Flick", "Kill right after a fast turn", Rarity.Rare, Gun, (e, s) => e.RecentTurnDegrees >= s.FlickDegrees),
		Kill("spray_transfer", "Spray transfer", "Kill two players in one spray", Rarity.Rare, Automatic, (e, _) => (e.EarlierKillsInSpray >= 1) && (e.KillsWithThisBullet < 2)),
		Stat(EfficientReload, "Efficient reload", "Reload with the magazine nearly empty", Rarity.Common, SkillTrigger.Reload, Gun),
		Stat(Marksman, "Marksman", "Finish a life with high accuracy", Rarity.Rare, SkillTrigger.LifeEnd, Gun),
		Stat(Sharpshooter, "Sharpshooter", "Finish a life with very high accuracy", Rarity.Epic, SkillTrigger.LifeEnd, Gun),
		Stat(Headhunter, "Headhunter", "Finish a life with half your hits on the head", Rarity.Rare, SkillTrigger.LifeEnd, Gun),

		// Multi-kill and situation
		Kill("double_kill", "Double kill", "Two kills in quick succession", Rarity.Common, Any, (e, _) => e.ChainedKills == 2),
		Kill("triple_kill", "Triple kill", "Three kills in quick succession", Rarity.Rare, Any, (e, _) => e.ChainedKills == 3),
		Kill("rampage", "Rampage", "Four or more kills in quick succession", Rarity.Epic, Any, (e, _) => e.ChainedKills >= 4),
		Kill("collateral", "Collateral", "Kill two players with one bullet", Rarity.Epic, Gun, (e, _) => e.KillsWithThisBullet >= 2),
		Kill("opening_kill", "Opening kill", "Get the first kill of the round", Rarity.Common, Any, (e, _) => e.IsOpeningKill),
		Kill("trade", "Trade", "Kill a teammate's killer right away", Rarity.Common, Any, (e, _) => e.IsTrade),
		Kill("revenge", "Revenge", "Kill a player who was dominating you", Rarity.Common, Any, (e, _) => e.IsRevenge),
		Kill("last_stand", "Last stand", "Kill while almost dead", Rarity.Rare, Any, (e, s) => (e.Attacker.Health > 0) && (e.Attacker.Health <= s.LastStandHealth)),
		Kill("bounce_back", "Bounce back", "Kill right after taking damage", Rarity.Common, Any, (e, s) => e.SecondsSinceAttackerHurt <= s.BounceBackSeconds),
		Kill("defuse_denial", "Defuse denial", "Kill a player who is defusing", Rarity.Rare, Any, (e, _) => e.Victim.Defusing),
		Stat(Clutch, "Clutch", "Win the round as your team's last player alive", Rarity.Epic, SkillTrigger.Clutch, Any),

		// Chickens
		Chicken("chicken_nugget", "Nugget", "Headshot a chicken", Rarity.Common, Gun, (e, _) => e.IsHeadshot),
		Chicken("chicken_cluck_and_cover", "Cluck and cover", "Kill a chicken while crouched", Rarity.Common, NotGrenade, (e, _) => e.Attacker.Ducked),
		Chicken("chicken_hand_plucked", "Hand-plucked", "Knife a chicken", Rarity.Common, Knife, (_, _) => true),
		Chicken("chicken_fowl_play", "Fowl play", "Kill a chicken through a wall", Rarity.Rare, Gun, (e, _) => e.Penetrations > 0),
		Chicken("chicken_smoked", "Smoked chicken", "Kill a chicken through a smoke", Rarity.Rare, Gun, (e, _) => e.ThroughSmoke),
		Chicken("chicken_fried", "Fried chicken", "Cook a chicken with a molotov or incendiary", Rarity.Rare, Named("inferno"), (_, _) => true),
		Chicken("chicken_scrambled", "Scrambled", "Blow up a chicken with an HE grenade", Rarity.Rare, Named("hegrenade"), (_, _) => true),
		Chicken("chicken_rotisserie", "Rotisserie", "Zap a chicken with the Zeus", Rarity.Rare, Taser, (_, _) => true),
		Chicken("chicken_wing_it", "Wing it", "No-scope a chicken", Rarity.Rare, Scoped, (e, _) => e.NoScope),
		Chicken("chicken_poultry_in_motion", "Poultry in motion", "Kill a chicken while you're in the air", Rarity.Rare, NotGrenade, (e, _) => IsAirborne(e.Attacker)),
		Chicken("chicken_blind_clucking_luck", "Blind clucking luck", "Kill a chicken while flashed", Rarity.Epic, NotGrenade, (e, _) => e.AttackerBlind),
		Chicken("chicken_crossed_the_road", "Crossed the road", "Kill a chicken from long range", Rarity.Epic, Gun, (e, s) => e.Distance >= LongShotDistance(e.Weapon.Class, s)),
	];

	private static FrozenDictionary<string, SkillShotDefinition> ById { get; } = All.ToFrozenDictionary(s => s.Id.WeakString);

	/// <summary>
	/// Gets the skill shots checked on a trigger.
	/// </summary>
	/// <param name="trigger">The trigger.</param>
	/// <returns>The skill shots with that trigger.</returns>
	public static IEnumerable<SkillShotDefinition> For(SkillTrigger trigger) => All.Where(s => s.Trigger == trigger);

	/// <summary>
	/// Looks up a skill shot by id.
	/// </summary>
	/// <param name="id">The skill shot id.</param>
	/// <returns>The skill shot, or <see langword="null"/> when there's no such id.</returns>
	public static SkillShotDefinition? Find(SkillShotId id) => id is null ? null : ById.GetValueOrDefault(id.WeakString);

	/// <summary>
	/// Looks up a skill shot by id, for ids defined in this catalogue.
	/// </summary>
	/// <param name="id">The skill shot id.</param>
	/// <returns>The skill shot.</returns>
	public static SkillShotDefinition Get(SkillShotId id) => Find(id) ?? throw new KeyNotFoundException($"No skill shot with id '{id}'.");

	private static bool IsAirborne(PlayerState state) => !state.OnGround && !state.OnLadder && !state.InWater;

	private static double LongShotDistance(WeaponClass weaponClass, ProgressionSettings settings) => weaponClass switch
	{
		WeaponClass.Pistol => settings.LongShotPistolDistance,
		WeaponClass.Smg => settings.LongShotSmgDistance,
		WeaponClass.Sniper => settings.LongShotSniperDistance,
		WeaponClass.Shotgun => settings.LongShotShotgunDistance,
		_ => settings.LongShotRifleDistance,
	};

	private static bool Any(WeaponInfo _) => true;

	private static bool Gun(WeaponInfo weapon) => weapon.IsGun;

	private static bool GunNotShotgun(WeaponInfo weapon) => weapon.IsGun && (weapon.Class != WeaponClass.Shotgun);

	private static bool GunOrKnife(WeaponInfo weapon) => weapon.IsGun || (weapon.Class == WeaponClass.Knife);

	private static bool NotGrenade(WeaponInfo weapon) => weapon.Class != WeaponClass.Grenade;

	private static bool Pistol(WeaponInfo weapon) => weapon.Class == WeaponClass.Pistol;

	private static bool Scoped(WeaponInfo weapon) => weapon.IsScoped;

	private static bool Knife(WeaponInfo weapon) => weapon.Class == WeaponClass.Knife;

	private static bool Taser(WeaponInfo weapon) => weapon.Class == WeaponClass.Taser;

	private static bool RifleSniperOrMachineGun(WeaponInfo weapon) => weapon.Class is WeaponClass.Rifle or WeaponClass.Sniper or WeaponClass.MachineGun;

	private static bool Automatic(WeaponInfo weapon) => weapon.Class is WeaponClass.Smg or WeaponClass.Rifle or WeaponClass.MachineGun;

	private static Func<WeaponInfo, bool> Named(params string[] keys) => weapon => keys.Contains(weapon.Key.WeakString);

	private static SkillShotDefinition Hit(string id, string name, string description, Rarity rarity, Func<WeaponInfo, bool> appliesTo, Func<CombatEvent, ProgressionSettings, bool> detect) =>
		new(SkillShotId.Create(id), name, description, rarity, SkillTrigger.Hit, appliesTo, detect);

	private static SkillShotDefinition Kill(string id, string name, string description, Rarity rarity, Func<WeaponInfo, bool> appliesTo, Func<CombatEvent, ProgressionSettings, bool> detect) =>
		new(SkillShotId.Create(id), name, description, rarity, SkillTrigger.Kill, appliesTo, detect);

	private static SkillShotDefinition Chicken(string id, string name, string description, Rarity rarity, Func<WeaponInfo, bool> appliesTo, Func<CombatEvent, ProgressionSettings, bool> detect) =>
		new(SkillShotId.Create(id), name, description, rarity, SkillTrigger.Chicken, appliesTo, detect);

	private static SkillShotDefinition Stat(SkillShotId id, string name, string description, Rarity rarity, SkillTrigger trigger, Func<WeaponInfo, bool> appliesTo) =>
		new(id, name, description, rarity, trigger, appliesTo);
}
