// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Tracking;
using ktsu.CounterStrikeProgression.Panel;
using Microsoft.Extensions.Logging;

/// <summary>
/// The pieces the event handlers and commands share.
/// </summary>
/// <param name="config">The plugin config.</param>
/// <param name="engine">Turns events into progress.</param>
/// <param name="sessions">The connected players.</param>
/// <param name="panel">The progress panels.</param>
/// <param name="announcer">The chat announcements.</param>
/// <param name="logger">Where failures are reported.</param>
internal sealed class ProgressionServices(PluginConfig config, ProgressionEngine engine, SessionManager sessions, PanelPresenter panel, Announcer announcer, ILogger logger)
{
	/// <summary>Gets the plugin config.</summary>
	public PluginConfig Config { get; } = config;

	/// <summary>Gets the engine that turns events into progress.</summary>
	public ProgressionEngine Engine { get; } = engine;

	/// <summary>Gets the connected players.</summary>
	public SessionManager Sessions { get; } = sessions;

	/// <summary>Gets the progress panels.</summary>
	public PanelPresenter Panel { get; } = panel;

	/// <summary>Gets the round's kills.</summary>
	public RoundTracker Round { get; } = new(config.Progression);

	/// <summary>Gets where failures are reported.</summary>
	public ILogger Logger { get; } = logger;

	/// <summary>
	/// Refreshes a player's panel and announces what they earned.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	/// <param name="result">What they earned.</param>
	public void Apply(CCSPlayerController player, PlayerSession session, ProgressionResult result)
	{
		Ensure.NotNull(result);
		if ((result.XpGained == 0) && (result.Notices.Count == 0))
		{
			return;
		}

		PanelPresenter.Invalidate(session);
		announcer.Announce(player, session, result);
	}

	/// <summary>
	/// Gets a value indicating whether two players are on the same side, which means damage between them never pays.
	/// </summary>
	/// <param name="attacker">The attacker.</param>
	/// <param name="victim">The victim.</param>
	/// <returns>Whether they're teammates.</returns>
	public static bool IsTeammate(CCSPlayerController attacker, CCSPlayerController victim)
	{
		Ensure.NotNull(attacker);
		Ensure.NotNull(victim);
		return !GameState.TeammatesAreEnemies && (attacker.TeamNum == victim.TeamNum);
	}
}
