// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Tracking;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Turns shots, reloads, hits, kills, assists, and flashes into progress.
/// </summary>
/// <param name="services">The shared plugin pieces.</param>
internal sealed class CombatHandlers(ProgressionServices services)
{
	private SessionManager Sessions => services.Sessions;

	private ProgressionEngine Engine => services.Engine;

	/// <summary>
	/// Hooks the combat events.
	/// </summary>
	/// <param name="plugin">The plugin to register with.</param>
	public void Register(BasePlugin plugin)
	{
		Ensure.NotNull(plugin);
		plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
		plugin.RegisterEventHandler<EventWeaponReload>(OnWeaponReload);
		plugin.RegisterEventHandler<EventBulletDamage>(OnBulletDamage);
		plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
		plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		plugin.RegisterEventHandler<EventPlayerBlind>(OnPlayerBlind);
		plugin.RegisterEventHandler<EventOtherDeath>(OnOtherDeath);
	}

	private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
	{
		if ((Sessions.Get(@event.Userid) is not { Progress: PlayerProgress progress } session) || GameState.IsWarmup)
		{
			return HookResult.Continue;
		}

		CCSPlayerPawn? pawn = GameState.PawnOf(@event.Userid);
		WeaponInfo? weapon = GameState.ResolveWeapon(@event.Weapon, pawn);
		if ((weapon is null) || !weapon.IsGun)
		{
			return HookResult.Continue;
		}

		session.Combat.ShotFired(pawn?.ShotsFired ?? 1);
		session.Life.Shot(weapon);
		ProgressionEngine.RecordShot(progress, weapon);
		return HookResult.Continue;
	}

	private HookResult OnWeaponReload(EventWeaponReload @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if ((player is null) || (Sessions.Get(player) is not { Progress: PlayerProgress progress } session) || GameState.IsWarmup)
		{
			return HookResult.Continue;
		}

		session.Combat.ReloadStarted();
		CBasePlayerWeapon? held = GameState.ActiveWeapon(GameState.PawnOf(player));
		WeaponInfo? weapon = held is null ? null : WeaponCatalog.Resolve(held.DesignerName, held.AttributeManager.Item.ItemDefinitionIndex);
		CCSWeaponBaseVData? data = held?.As<CCSWeaponBase>().VData;
		if ((weapon is null) || (held is null) || (data is null))
		{
			return HookResult.Continue;
		}

		ReloadEvent reload = new(weapon, held.Clip1, data.MaxClip1, data.ReloadsSingleShells);
		services.Apply(player, session, Engine.ApplyReload(progress, reload));
		return HookResult.Continue;
	}

	private HookResult OnBulletDamage(EventBulletDamage @event, GameEventInfo info)
	{
		if ((Sessions.Get(@event.Attacker) is PlayerSession session) && (@event.Victim is CCSPlayerController victim))
		{
			session.LastBullet = (Server.TickCount, victim.Slot, @event.NumPenetrations);
		}

		return HookResult.Continue;
	}

	private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
	{
		CCSPlayerController? victim = @event.Userid;
		CCSPlayerController? attacker = @event.Attacker;

		if (Sessions.Get(victim) is PlayerSession victimSession)
		{
			victimSession.Combat.Hurt(Server.CurrentTime);
			services.Panel.Close(victim, victimSession);
		}

		if ((victim is null) || !victim.IsValid || (attacker is null) || (attacker == victim) || GameState.IsWarmup
			|| (Sessions.Get(attacker) is not { Progress: PlayerProgress progress } session))
		{
			return HookResult.Continue;
		}

		CCSPlayerPawn? attackerPawn = GameState.PawnOf(attacker);
		WeaponInfo? weapon = GameState.ResolveWeapon(@event.Weapon, attackerPawn);
		if (weapon is null)
		{
			return HookResult.Continue;
		}

		int tick = Server.TickCount;
		bool headshot = @event.Hitgroup == (int)HitGroup_t.HITGROUP_HEAD;
		bool isTeammate = ProgressionServices.IsTeammate(attacker, victim);
		if (weapon.IsGun)
		{
			session.Life.Hit(weapon, headshot, victim.IsBot, isTeammate, tick);
		}

		// A shotgun blast arrives as one hurt event per pellet, but it's one hit.
		if (session.LastHit == (tick, victim.Slot))
		{
			return HookResult.Continue;
		}

		session.LastHit = (tick, victim.Slot);
		CCSPlayerPawn? victimPawn = GameState.PawnOf(victim);
		PlayerState victimState = GameState.StateOf(victimPawn) with { Health = @event.Health };
		double sideways = GameState.SidewaysSpeed(attackerPawn, victimPawn);
		session.LastHitVictim = (tick, victim.Slot, victimState, sideways);

		CombatEvent hit = new()
		{
			Weapon = weapon,
			IsHeadshot = headshot,
			Damage = @event.DmgHealth,
			VictimIsBot = victim.IsBot,
			IsTeammate = isTeammate,
			Attacker = GameState.StateOf(attackerPawn),
			Victim = victimState,
			VictimSidewaysSpeed = sideways,
			Distance = GameState.Distance(attackerPawn, victimPawn),
		};

		// bullet_damage carries the wallbang count. If it hasn't arrived for this bullet yet, the hit waits one
		// frame for it rather than guessing the order CS2 sends the two in.
		int victimSlot = victim.Slot;
		Vector? victimAt = Copy(victimPawn?.AbsOrigin);
		if (BulletPenetrations(session, tick, victimSlot) is int known)
		{
			services.Apply(attacker, session, Engine.ApplyCombat(progress, hit with { Penetrations = known }), victimSlot, victimAt);
		}
		else
		{
			Server.NextFrame(() => ApplyLateHit(session, progress, hit, tick, victimSlot, victimAt));
		}

		return HookResult.Continue;
	}

	private void ApplyLateHit(PlayerSession session, PlayerProgress progress, CombatEvent hit, int tick, int victimSlot, Vector? victimAt)
	{
		CCSPlayerController? attacker = Utilities.GetPlayerFromSlot(session.Slot);
		if ((Sessions.Get(attacker) != session) || !ReferenceEquals(session.Progress, progress))
		{
			return;
		}

		int penetrations = BulletPenetrations(session, tick, victimSlot) ?? 0;
		services.Apply(attacker!, session, Engine.ApplyCombat(progress, hit with { Penetrations = penetrations }), victimSlot, victimAt);
	}

	// Positions are read from live entity memory, so a value kept for later is copied first.
	private static Vector? Copy(Vector? position) => position is null ? null : new Vector(position.X, position.Y, position.Z);

	private static int? BulletPenetrations(PlayerSession session, int tick, int victimSlot)
	{
		(int bulletTick, int bulletVictim, int penetrations) = session.LastBullet;
		return (bulletTick == tick) && (bulletVictim == victimSlot) ? penetrations : null;
	}

	private HookResult OnOtherDeath(EventOtherDeath @event, GameEventInfo info)
	{
		if (GameState.IsWarmup || !string.Equals(@event.Othertype, "chicken", StringComparison.OrdinalIgnoreCase))
		{
			return HookResult.Continue;
		}

		CCSPlayerController? attacker = Utilities.GetPlayerFromUserid(@event.Attacker);
		if ((attacker is null) || (Sessions.Get(attacker) is not { Progress: PlayerProgress progress } session))
		{
			return HookResult.Continue;
		}

		CCSPlayerPawn? pawn = GameState.PawnOf(attacker);
		WeaponInfo? weapon = GameState.ResolveWeapon(@event.Weapon, pawn);
		if (weapon is null)
		{
			return HookResult.Continue;
		}

		CBaseEntity? chicken = Utilities.GetEntityFromIndex<CBaseEntity>(@event.Otherid);
		CombatEvent kill = new()
		{
			Weapon = weapon,
			IsKill = true,
			IsHeadshot = @event.Headshot,
			Attacker = GameState.StateOf(pawn),
			Distance = GameState.Distance(pawn?.AbsOrigin, chicken?.AbsOrigin),
			Penetrations = @event.Penetrated,
			ThroughSmoke = @event.Thrusmoke,
			AttackerBlind = @event.Attackerblind,
			NoScope = @event.Noscope,
		};
		services.Apply(attacker, session, Engine.ApplyChickenKill(progress, kill));

		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		CCSPlayerController? victim = @event.Userid;
		CCSPlayerController? attacker = @event.Attacker;
		bool warmup = GameState.IsWarmup;

		EndLife(victim, warmup);
		if (warmup || (victim is null) || !victim.IsValid)
		{
			return HookResult.Continue;
		}

		bool teammatesAreEnemies = GameState.TeammatesAreEnemies;
		NoteLastAlive(teammatesAreEnemies, victim.Slot);
		if ((attacker is null) || !attacker.IsValid || (attacker == victim))
		{
			services.ApplyBounty(services.Bounties.Drop(victim.Slot));
			return HookResult.Continue;
		}

		// The kill is credited before the bounty, so its skill shots are announced first.
		IReadOnlyList<BountyChange> bounty = services.Bounties.Kill(
			attacker.Slot, victim.Slot, attacker.IsBot, ProgressionServices.IsTeammate(attacker, victim), Sessions.Get(attacker)?.Progress is not null);
		CreditKill(@event, attacker, victim, teammatesAreEnemies);
		services.ApplyBounty(bounty);
		return HookResult.Continue;
	}

	private void CreditKill(EventPlayerDeath @event, CCSPlayerController attacker, CCSPlayerController victim, bool teammatesAreEnemies)
	{
		KillContext context = services.Round.RegisterKill(
			attacker.Slot,
			GameState.SideOf(attacker, teammatesAreEnemies),
			victim.Slot,
			GameState.SideOf(victim, teammatesAreEnemies),
			Server.CurrentTime,
			Server.TickCount);

		CreditAssist(@event, victim);
		if (!ProgressionServices.IsTeammate(attacker, victim) && (Sessions.Get(attacker) is { Progress: not null } lifeSession))
		{
			lifeSession.Life.Kill(victim.IsBot);
		}

		CCSPlayerPawn? attackerPawn = GameState.PawnOf(attacker);
		WeaponInfo? weapon = GameState.ResolveWeapon(@event.Weapon, attackerPawn);
		if ((weapon is not null) && !ProgressionServices.IsTeammate(attacker, victim))
		{
			services.KillMaps?.Record(Server.MapName ?? string.Empty, attacker, victim, weapon, @event.Headshot);
		}

		if ((weapon is null) || (Sessions.Get(attacker) is not { Progress: PlayerProgress progress } session))
		{
			return;
		}

		CombatEvent kill = BuildKill(@event, victim, attackerPawn, weapon, session.Combat.Read(Server.CurrentTime), context, session.LastHitVictim);
		session.Combat.KillMade();
		session.LastKillWeapon = weapon;
		Vector? at = Copy(GameState.PawnOf(victim)?.AbsOrigin);
		services.Apply(attacker, session, Engine.ApplyCombat(progress, kill), victim.Slot, at);

		// Only humans have sessions, so a bot victim never counts toward a head-to-head.
		if (!kill.IsTeammate && (Sessions.Get(victim) is { Progress: PlayerProgress victimProgress }))
		{
			services.Apply(attacker, session, Engine.ApplyRivalKill(progress, victimProgress), victim.Slot, at);
		}
	}

	private HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
	{
		CCSPlayerController? victim = @event.Userid;
		CCSPlayerController? attacker = @event.Attacker;
		if ((victim is null) || !victim.IsValid || (attacker is null) || (attacker == victim) || GameState.IsWarmup
			|| (Sessions.Get(attacker) is not { Progress: PlayerProgress progress } session)
			|| ProgressionServices.IsTeammate(attacker, victim)
			|| (WeaponCatalog.Resolve("flashbang") is not WeaponInfo flashbang))
		{
			return HookResult.Continue;
		}

		services.Apply(attacker, session, Engine.ApplyFlash(progress, new FlashEvent(flashbang, @event.BlindDuration, victim.IsBot)), victim.Slot, Copy(GameState.PawnOf(victim)?.AbsOrigin));
		return HookResult.Continue;
	}

	private static CombatEvent BuildKill(
		EventPlayerDeath @event,
		CCSPlayerController victim,
		CCSPlayerPawn? attackerPawn,
		WeaponInfo weapon,
		CombatTiming timing,
		KillContext context,
		(int Tick, int Victim, PlayerState State, double SidewaysSpeed) lastHit)
	{
		CCSPlayerPawn? victimPawn = GameState.PawnOf(victim);
		CBasePlayerWeapon? held = GameState.ActiveWeapon(attackerPawn);

		// The killing blow's hurt event ran moments ago on this tick, while the victim was still alive.
		bool fromHit = (lastHit.Tick == Server.TickCount) && (lastHit.Victim == victim.Slot);
		PlayerState victimState = fromHit ? lastHit.State : GameState.StateOf(victimPawn);
		return new CombatEvent
		{
			Weapon = weapon,
			IsKill = true,
			IsHeadshot = @event.Headshot,
			Damage = @event.DmgHealth,
			VictimIsBot = victim.IsBot,
			IsTeammate = (@event.Attacker is CCSPlayerController attacker) && ProgressionServices.IsTeammate(attacker, victim),
			Attacker = GameState.StateOf(attackerPawn),
			Victim = victimState with { Health = 0 },
			VictimSidewaysSpeed = fromHit ? lastHit.SidewaysSpeed : GameState.SidewaysSpeed(attackerPawn, victimPawn),
			Distance = GameState.Distance(attackerPawn, victimPawn),
			Penetrations = @event.Penetrated,
			ThroughSmoke = @event.Thrusmoke,
			AttackerBlind = @event.Attackerblind,
			NoScope = @event.Noscope,
			SecondsSinceAttackerLanded = timing.SecondsSinceLanded,
			SecondsSinceScoped = timing.SecondsSinceScoped,
			SecondsSinceWeaponSwitch = timing.SecondsSinceWeaponSwitch,
			SecondsSinceAttackerHurt = timing.SecondsSinceHurt,
			FirstShotAfterReload = timing.FirstShotAfterReload,
			ClipAfterShot = weapon.IsGun && (held is not null) ? held.Clip1 : -1,
			ShotsInSpray = attackerPawn?.ShotsFired ?? 0,
			EarlierKillsInSpray = timing.EarlierKillsInSpray,
			RecentTurnDegrees = timing.RecentTurnDegrees,
			ChainedKills = context.ChainedKills,
			LifeKills = timing.EarlierLifeKills + 1,
			KillsUnhurt = timing.EarlierKillsUnhurt + 1,
			KillsWithThisBullet = context.KillsWithThisBullet,
			IsOpeningKill = context.IsOpeningKill,
			IsTrade = context.IsTrade,
			IsRevenge = @event.Revenge > 0,
		};
	}

	private void EndLife(CCSPlayerController? victim, bool warmup)
	{
		if ((victim is null) || (Sessions.Get(victim) is not PlayerSession session))
		{
			return;
		}

		services.Panel.Close(victim, session);
		services.EndLife(victim, session, warmup);
		session.Combat.Reset();
	}

	private void CreditAssist(EventPlayerDeath @event, CCSPlayerController victim)
	{
		CCSPlayerController? assister = @event.Assister;
		if ((assister is null) || !assister.IsValid || (assister == victim) || ProgressionServices.IsTeammate(assister, victim)
			|| (Sessions.Get(assister) is not { Progress: PlayerProgress progress } session))
		{
			return;
		}

		CCSPlayerPawn? pawn = GameState.PawnOf(assister);
		WeaponInfo? weapon = @event.Assistedflash
			? WeaponCatalog.Resolve("flashbang")
			: GameState.ResolveWeapon(GameState.ActiveWeapon(pawn)?.DesignerName, pawn);
		if (weapon is not null)
		{
			services.Apply(assister, session, Engine.ApplyAssist(progress, new AssistEvent(weapon, @event.Assistedflash, victim.IsBot)), victim.Slot, Copy(GameState.PawnOf(victim)?.AbsOrigin));
		}
	}

	private void NoteLastAlive(bool teammatesAreEnemies, int deadSlot)
	{
		if (teammatesAreEnemies)
		{
			return;
		}

		// The victim's controller may not show them as dead yet while their death event runs.
		List<CCSPlayerController> alive = [.. Utilities.GetPlayers().Where(p => p.PawnIsAlive && (p.Slot != deadSlot) && (p.TeamNum is 2 or 3))];
		foreach (IGrouping<byte, CCSPlayerController> team in alive.GroupBy(p => p.TeamNum))
		{
			int enemies = alive.Count(p => p.TeamNum != team.Key);
			if ((team.Count() == 1) && (enemies > 0))
			{
				services.Round.NoteLastAlive(team.First().Slot, team.Key, enemies);
			}
		}
	}
}
