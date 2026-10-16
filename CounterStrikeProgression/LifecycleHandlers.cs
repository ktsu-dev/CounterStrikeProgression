// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Follows players joining, spawning, and leaving, rounds and maps changing, and every tick.
/// </summary>
/// <param name="services">The shared plugin pieces.</param>
internal sealed class LifecycleHandlers(ProgressionServices services)
{
	private SessionManager Sessions => services.Sessions;

	/// <summary>
	/// Hooks the lifecycle events and listeners.
	/// </summary>
	/// <param name="plugin">The plugin to register with.</param>
	public void Register(BasePlugin plugin)
	{
		Ensure.NotNull(plugin);
		plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
		plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		plugin.RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
		plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
		plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
		plugin.RegisterListener<Listeners.OnTick>(OnTick);
		plugin.RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
		plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
		plugin.RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
	}

	private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		if (@event.Userid is CCSPlayerController player)
		{
			Sessions.Connect(player);
		}

		return HookResult.Continue;
	}

	// Keyed by slot, so a player whose controller is already invalid as they leave still gets saved.
	private void OnClientDisconnect(int playerSlot)
	{
		PlayerSession? session = Sessions.All.FirstOrDefault(s => s.Slot == playerSlot);
		if (session is not null)
		{
			services.Panel.Close(null, session);
		}

		Sessions.DisconnectSlot(playerSlot);
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if ((player is null) || (Sessions.Get(player) is not PlayerSession session))
		{
			return HookResult.Continue;
		}

		// A player who survived the last round never died, so their previous life's accuracy is settled here.
		IReadOnlyList<WeaponLifeStats> lives = session.Life.EndLife();
		if (!GameState.IsWarmup && (session.Progress is PlayerProgress progress))
		{
			foreach (WeaponLifeStats life in lives)
			{
				services.Apply(player, session, services.Engine.ApplyLifeEnd(progress, life));
			}
		}

		session.Combat.Reset();
		session.WasScoped = false;
		session.LastWeaponIndex = 0;
		return HookResult.Continue;
	}

	private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
	{
		if (Sessions.Get(@event.Userid) is PlayerSession session)
		{
			services.Panel.Close(@event.Userid, session);
		}

		return HookResult.Continue;
	}

	private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
	{
		services.Round.StartRound();
		foreach (PlayerSession session in Sessions.All)
		{
			session.LastKillWeapon = null;
		}

		return HookResult.Continue;
	}

	private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
	{
		if (!GameState.IsWarmup && !GameState.TeammatesAreEnemies
			&& (services.Round.Clutch(@event.Winner, IsSlotAlive) is (int slot, int enemies))
			&& (Utilities.GetPlayerFromSlot(slot) is CCSPlayerController player)
			&& (Sessions.Get(player) is { Progress: PlayerProgress progress } session))
		{
			services.Apply(player, session, services.Engine.ApplyClutch(progress, new ClutchEvent(enemies, session.LastKillWeapon)));
		}

		Sessions.SaveAll();
		return HookResult.Continue;
	}

	private void OnTick()
	{
		double now = Server.CurrentTime;
		foreach (PlayerSession session in Sessions.All)
		{
			CCSPlayerController? player = Utilities.GetPlayerFromSlot(session.Slot);
			if ((player is null) || !player.IsValid)
			{
				continue;
			}

			CCSPlayerPawn? pawn = GameState.PawnOf(player);
			if ((pawn is not null) && player.PawnIsAlive)
			{
				TrackPawn(session, pawn, now);
			}

			services.Panel.Tick(player, session);
		}
	}

	private static void TrackPawn(PlayerSession session, CCSPlayerPawn pawn, double now)
	{
		bool onGround = ((PlayerFlags)pawn.Flags).HasFlag(PlayerFlags.FL_ONGROUND);
		session.Combat.Tick(now, pawn.EyeAngles.Y, onGround);

		bool scoped = pawn.IsScoped;
		if (scoped != session.WasScoped)
		{
			session.Combat.ScopeChanged(now, scoped);
			session.WasScoped = scoped;
		}

		uint weaponIndex = GameState.ActiveWeapon(pawn)?.Index ?? 0;
		if ((weaponIndex != session.LastWeaponIndex) && (session.LastWeaponIndex != 0))
		{
			session.Combat.WeaponSwitched(now);
			session.WasScoped = false;
		}

		session.LastWeaponIndex = weaponIndex;
	}

	private void OnMapEnd()
	{
		GameState.ForgetMap();
		services.Panel.WorldText.HideAll();
		foreach (PlayerSession session in Sessions.All)
		{
			session.Panel.IsOpen = false;
		}

		Sessions.SaveAll();
	}

	private void OnCheckTransmit(CCheckTransmitInfoList infoList)
	{
		if (!services.Panel.WorldText.Any)
		{
			return;
		}

		IReadOnlyList<(int Slot, uint Index)> panels = services.Panel.WorldText.Shown;
		foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
		{
			if (viewer is null)
			{
				continue;
			}

			// Every panel except the viewer's own is withheld from them.
			for (int i = 0; i < panels.Count; i++)
			{
				if (panels[i].Slot != viewer.Slot)
				{
					info.TransmitEntities.Remove(panels[i].Index);
				}
			}
		}
	}

	private static bool IsSlotAlive(int slot) => Utilities.GetPlayerFromSlot(slot) is { IsValid: true, PawnIsAlive: true };
}
