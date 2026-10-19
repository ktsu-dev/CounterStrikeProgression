// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Discord;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Tracking;
using ktsu.CounterStrikeProgression.Panel;
using ktsu.CounterStrikeProgression.Site;
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
	private static readonly TimeSpan DiscordTimeout = TimeSpan.FromSeconds(5);

	// Discord allows a webhook about five posts every two seconds, and lines that arrive meanwhile join the next post.
	private static readonly TimeSpan DiscordPostGap = TimeSpan.FromSeconds(1);

	/// <summary>Gets the plugin config.</summary>
	public PluginConfig Config { get; } = config;

	/// <summary>Gets the engine that turns events into progress.</summary>
	public ProgressionEngine Engine { get; } = engine;

	/// <summary>Gets the connected players.</summary>
	public SessionManager Sessions { get; } = sessions;

	/// <summary>Gets the text behind the welcome line and the chat commands.</summary>
	public ChatReports Reports { get; } = new(engine);

	/// <summary>Gets the progress panels.</summary>
	public PanelPresenter Panel { get; } = panel;

	/// <summary>Gets the floating "+XP" text.</summary>
	public XpFloaties Floaties { get; } = new(config.Floaties);

	/// <summary>Gets the rank clan tags and chat prefixes.</summary>
	public RankTags RankTags { get; } = new(config.RankTags, sessions);

	/// <summary>Gets the round's kills.</summary>
	public RoundTracker Round { get; } = new(config.Progression);

	/// <summary>Gets where failures are reported.</summary>
	public ILogger Logger { get; } = logger;

	/// <summary>Gets the progression site reporter, which is <see langword="null"/> when no site is configured.</summary>
	public SiteReporter? Site { get; } = SiteReporter.Create(config, sessions, logger);

	/// <summary>Gets the Discord poster, which is <see langword="null"/> when no webhook is configured.</summary>
	public DiscordSender? Discord { get; } = CreateDiscord(config, logger);

	/// <summary>
	/// Refreshes a player's panel, shows a floatie, announces what they earned, and adds it to this map's XP,
	/// the site's feed, and Discord.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	/// <param name="result">What they earned.</param>
	/// <param name="floatieKey">The target's slot, so gains on one target share a floatie, or <see cref="FloatieMerger.SelfKey"/>.</param>
	/// <param name="at">Where the target stands, or <see langword="null"/> to float the XP in front of the player.</param>
	public void Apply(CCSPlayerController player, PlayerSession session, ProgressionResult result, int floatieKey = FloatieMerger.SelfKey, Vector? at = null)
	{
		Ensure.NotNull(result);
		if (session.Progress is { } progress)
		{
			Engine.CheckFeats(progress, result);
		}

		if ((result.XpEarned <= 0) && (result.Notices.Count == 0))
		{
			return;
		}

		session.MapXp += result.XpEarned;
		session.Recap.Add(result);
		Site?.Record(session, player.PlayerName ?? string.Empty, result);
		if (Discord is { } discord)
		{
			foreach (string line in DiscordMessages.From(result, player.PlayerName ?? string.Empty, Config.Discord))
			{
				discord.TryEnqueue(line);
			}
		}
		PanelPresenter.Invalidate(session);
		Floaties.Show(player, result, floatieKey, at);
		announcer.Announce(player, session, result);

		// Records are rare and the site reads them from the database, so a broken one is written straight away.
		Sessions.SaveRecords();
	}

	/// <summary>
	/// Ends a player's life: each weapon's accuracy skill shots, then the whole life's records.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	/// <param name="warmup">Whether it's warmup, when the life is forgotten without earning anything.</param>
	public void EndLife(CCSPlayerController player, PlayerSession session, bool warmup)
	{
		Ensure.NotNull(session);
		foreach (ProgressionResult result in Engine.EndLife(session.Progress, session.Life, warmup))
		{
			Apply(player, session, result);
		}
	}

	/// <summary>
	/// Ends a player's map recap, sends it to the site, and tells them about it. A session that earned
	/// nothing just starts over.
	/// </summary>
	/// <param name="session">The player's session.</param>
	/// <param name="player">The player, or <see langword="null"/> when their controller is already gone.</param>
	/// <param name="map">The map played.</param>
	/// <param name="announce">Whether to tell the player in chat.</param>
	public void FinishRecap(PlayerSession session, CCSPlayerController? player, string map, bool announce)
	{
		Ensure.NotNull(session);
		CCSPlayerController? valid = player is { IsValid: true } ? player : null;
		CSMatchStats_t? stats = valid?.ActionTrackingServices?.MatchStats;
		string name = valid?.PlayerName ?? session.Progress?.Name ?? string.Empty;
		if ((valid is not null) && (session.Progress is PlayerProgress progress))
		{
			// Before the recap ends, so a record set here is its best moment.
			Apply(valid, session, Engine.ApplyMapRecords(progress, session.Recap.SkillShots, session.Recap.BotSkillShots));
		}

		if (session.Recap.TryFinish(session.SteamId, name, map, stats?.Kills ?? 0, stats?.Deaths ?? 0, DateTime.UtcNow) is not { } recap)
		{
			return;
		}

		Site?.Record(recap);
		if (announce && (valid is not null))
		{
			announcer.AnnounceRecap(valid, recap);
		}
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

	private static DiscordSender? CreateDiscord(PluginConfig config, ILogger logger)
	{
		DiscordSettings settings = config.Discord ?? new();
		if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
		{
			return null;
		}

		// The URL holds the webhook's secret token, so it is never logged.
		if (DiscordSender.ParseUrl(settings.WebhookUrl) is not { } webhook)
		{
			Log.DiscordUrlInvalid(logger);
			return null;
		}

		return new DiscordSender(webhook, settings.Username ?? string.Empty, settings.QueueLimit, DiscordTimeout, DiscordPostGap, exception => Log.DiscordFailed(logger, exception));
	}
}
