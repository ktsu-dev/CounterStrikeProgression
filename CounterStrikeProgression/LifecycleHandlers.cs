// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Panel;
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
		if (services.Site is { } site)
		{
			// A batch straight after the new map loads, so the site's scoreboard doesn't show the old map until the next interval.
			plugin.RegisterListener<Listeners.OnMapStart>(_ => plugin.AddTimer(1, site.Collect));
		}
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
			services.Site?.Forget(session.SteamId);
		}

		services.Floaties.RemoveOwner(playerSlot);
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
		Welcome(player, session);
		return HookResult.Continue;
	}

	// On the first spawn rather than on connect, because chat sent while the player is still loading in is
	// easy to miss, and their progress may not have loaded yet. A load that finishes later waits for the next spawn.
	private void Welcome(CCSPlayerController player, PlayerSession session)
	{
		if (session.Welcomed || !services.Config.WelcomeMessage || (session.Progress is not PlayerProgress progress))
		{
			return;
		}

		session.Welcomed = true;
		string prefix = $" {ChatColors.Gold}{services.Config.ChatPrefix}{ChatColors.Default}";
		player.PrintToChat($"{prefix} {services.Reports.Welcome(player.PlayerName, progress)}");
		if (ChatReports.PlayerPage(services.Config.Site.PublicUrl, session.SteamId) is string page)
		{
			player.PrintToChat($"{prefix} Your progress page: {ChatColors.LightBlue}{page}");
		}

		player.PrintToChat($"{prefix} Type {ChatColors.Yellow}!rank{ChatColors.Default} for your panel. More: {ChatColors.Yellow}!next !top !feats !stats !site");
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

		services.Floaties.Tick();
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
		services.Floaties.RemoveAll();
		foreach (PlayerSession session in Sessions.All)
		{
			session.Panel.IsOpen = false;
			session.MapXp = 0;
		}

		Sessions.SaveAll();
	}

	private void OnCheckTransmit(CCheckTransmitInfoList infoList)
	{
		IReadOnlyList<(int Slot, uint Index)> panels = services.Panel.WorldText.Shown;
		IReadOnlyList<(int Slot, uint Index)> floaties = services.Floaties.Shown;
		if ((panels.Count == 0) && (floaties.Count == 0))
		{
			return;
		}

		foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
		{
			if (viewer is null)
			{
				continue;
			}

			// Every panel and floatie except the viewer's own is withheld from them.
			HideOthers(info, panels, viewer.Slot);
			HideOthers(info, floaties, viewer.Slot);
		}
	}

	private static void HideOthers(CCheckTransmitInfo info, IReadOnlyList<(int Slot, uint Index)> owned, int viewerSlot)
	{
		for (int i = 0; i < owned.Count; i++)
		{
			if (owned[i].Slot != viewerSlot)
			{
				info.TransmitEntities.Remove(owned[i].Index);
			}
		}
	}

	private static bool IsSlotAlive(int slot) => Utilities.GetPlayerFromSlot(slot) is { IsValid: true, PawnIsAlive: true };
}
