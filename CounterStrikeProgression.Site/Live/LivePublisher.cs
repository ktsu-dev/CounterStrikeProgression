// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Views;

/// <summary>One fight a player was in, as the live event stream sends it.</summary>
/// <param name="Map">The map.</param>
/// <param name="SteamId">The player's 64-bit Steam ID, as a decimal string.</param>
/// <param name="X">The radar grid column, from 0 at the left.</param>
/// <param name="Y">The radar grid row, from 0 at the top.</param>
/// <param name="Won">Whether the player made the kill, rather than died.</param>
internal sealed record FightEvent(string Map, string SteamId, int X, int Y, bool Won);

/// <summary>
/// Tells the browsers what an applied batch changed.
/// </summary>
internal static class LivePublisher
{
	/// <summary>
	/// Publishes <c>snapshot</c> and <c>positions</c> events when the snapshot changed, a <c>feed</c> event per new global feed
	/// entry (humans' only), oldest first, a <c>progress</c> event naming the players whose progress changed, a <c>fights</c> event for
	/// the new kills, and a <c>kills</c> event for the batch's kills on the radar, bots' included.
	/// </summary>
	/// <param name="broadcaster">Where the events go.</param>
	/// <param name="batch">The batch.</param>
	/// <param name="changes">What the batch changed.</param>
	/// <param name="fights">The kills the current match kept from the batch.</param>
	public static void Publish(Broadcaster broadcaster, LiveBatch batch, LiveChanges changes, IReadOnlyList<KillPoint> fights)
	{
		Ensure.NotNull(broadcaster);
		Ensure.NotNull(batch);
		Ensure.NotNull(changes);
		Ensure.NotNull(fights);

		if (changes.SnapshotChanged)
		{
			broadcaster.Publish("snapshot", batch.Snapshot);

			// The page already has the radar from /api/positions, so the event carries none.
			broadcaster.Publish("positions", LivePositions.From(batch.Snapshot, null));
		}

		foreach (FeedEntry entry in changes.NewFeed.OrderBy(e => e.At))
		{
			broadcaster.Publish("feed", FeedCaptions.For(entry, batch.Settings));
		}

		if (changes.ChangedPlayers.Count > 0)
		{
			string[] steamIds = [.. changes.ChangedPlayers.Select(id => id.ToString(CultureInfo.InvariantCulture))];
			broadcaster.Publish("progress", steamIds);
		}

		FightEvent[] events = [.. fights.SelectMany(EventsOf)];
		if (events.Length > 0)
		{
			broadcaster.Publish("fights", events);
		}

		if (batch.Kills is { Count: > 0 } batchKills && LiveKills.From(batchKills) is { Count: > 0 } kills)
		{
			broadcaster.Publish("kills", kills);
		}
	}

	/// <summary>
	/// Gets one kill's fight for each player in it with an id, human or bot persona, whose cell is on the radar. A map without a calibrated
	/// overview has no fixed grid, so its kills give none.
	/// </summary>
	private static IEnumerable<FightEvent> EventsOf(KillPoint kill)
	{
		if (MapOverview.For(kill.Map) is not { IsCalibrated: true } overview)
		{
			yield break;
		}

		foreach (ulong steamId in new[] { kill.KillerId, kill.VictimId }.Where(id => id != 0).Distinct())
		{
			if (FightGrid.CellOf(kill, steamId, overview) is { } cell)
			{
				yield return new FightEvent(kill.Map, steamId.ToString(CultureInfo.InvariantCulture), cell.X, cell.Y, cell.Won > 0);
			}
		}
	}
}
