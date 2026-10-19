// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using Microsoft.Extensions.Logging;

/// <summary>
/// The plugin's log messages, generated so logging allocates nothing when the level is off.
/// </summary>
internal static partial class Log
{
	[LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Storing progress in {Store}")]
	public static partial void StoreSelected(ILogger logger, string store);

	[LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "No database host is configured, so progress lasts only until the server restarts")]
	public static partial void InMemoryWarning(ILogger logger);

	[LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Creating the progression tables failed (attempt {Attempt} of {Attempts})")]
	public static partial void InitializeFailed(ILogger logger, int attempt, int attempts, Exception exception);

	[LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Loading progress for {SteamId} failed")]
	public static partial void LoadFailed(ILogger logger, ulong steamId, Exception exception);

	[LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Saving progress for {SteamId} failed, retrying on the next save")]
	public static partial void SaveFailed(ILogger logger, ulong steamId, Exception exception);

	[LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The side panel failed for {Player}, switching them to {Fallback}")]
	public static partial void PanelFallback(ILogger logger, string player, string fallback, Exception? exception);

	[LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Reading the leaderboard failed")]
	public static partial void LeaderboardFailed(ILogger logger, Exception exception);

	[LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Handling {Event} failed")]
	public static partial void EventFailed(ILogger logger, string @event, Exception exception);

	[LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Sending live data to the progression site failed")]
	public static partial void SiteFailed(ILogger logger, Exception exception);

	[LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "The progression site URL {Url} isn't an absolute http or https URL, so nothing is sent to the site")]
	public static partial void SiteUrlInvalid(ILogger logger, string url);

	[LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Admin {Admin} ({AdminSteamId}) applied {Correction} to {SteamId}: rank XP {XpBefore} -> {XpAfter}, rank {RankBefore} -> {RankAfter}, {WeaponsCleared} weapon rows cleared")]
	public static partial void CorrectionApplied(ILogger logger, string admin, ulong adminSteamId, string correction, ulong steamId, long xpBefore, long xpAfter, int rankBefore, int rankAfter, int weaponsCleared);

	[LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "Admin {Admin} ({AdminSteamId}) tried {Correction} on {SteamId}, but it wasn't written")]
	public static partial void CorrectionFailed(ILogger logger, string admin, ulong adminSteamId, string correction, ulong steamId);

	[LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "Posting to the Discord webhook failed")]
	public static partial void DiscordFailed(ILogger logger, Exception exception);

	[LoggerMessage(EventId = 14, Level = LogLevel.Error, Message = "The Discord webhook URL isn't an absolute https URL, so nothing is posted to Discord")]
	public static partial void DiscordUrlInvalid(ILogger logger);

	[LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Writing {Count} kill positions for the heatmaps failed, retrying on the next write")]
	public static partial void HeatmapWriteFailed(ILogger logger, int count, Exception exception);

	[LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Creating the heatmap table failed")]
	public static partial void HeatmapInitializeFailed(ILogger logger, Exception exception);
}
