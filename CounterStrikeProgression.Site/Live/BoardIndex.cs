// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Collections.Concurrent;
using System.Data.Common;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Views;

/// <summary>
/// Each ranked player's weapon levels, skill shot counts, and feats, for the leaderboards and the skill shot
/// guide. Like <see cref="FeatIndex"/>, it needs every player's whole progress, so <see cref="FeatRefresher"/>
/// fills it in the background, and the players on the server now are brought up to date on each request.
/// </summary>
/// <param name="time">The clock, or the system clock when omitted.</param>
internal sealed class BoardIndex(TimeProvider? time = null)
{
	private readonly TimeProvider clock = time ?? TimeProvider.System;
	private readonly ConcurrentDictionary<ulong, PlayerTally> tallies = new();

	/// <summary>Gets when the whole index was last rebuilt, or <see langword="null"/> before the first rebuild.</summary>
	public DateTimeOffset? Updated { get; private set; }

	/// <summary>Gets every player's tally.</summary>
	public IReadOnlyList<PlayerTally> All => [.. tallies.Values];

	/// <summary>
	/// Replaces a player's tally with one worked out from their whole progress.
	/// </summary>
	/// <param name="player">The player's merged progress.</param>
	/// <param name="settings">The plugin's settings.</param>
	public void Set(PlayerProgress player, ProgressionSettings settings)
	{
		Ensure.NotNull(player);
		tallies[player.SteamId] = PlayerTally.From(player, settings);
	}

	/// <summary>Records that every ranked player has just been tallied.</summary>
	public void MarkUpdated() => Updated = clock.GetUtcNow();

	/// <summary>
	/// Brings the players on the server now up to date, so their latest skill shots and levels show without
	/// waiting for the next rebuild. A database that can't be read leaves their last tallies in place.
	/// </summary>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>A task that completes when the players on the server are tallied.</returns>
	public async Task RefreshPlayingAsync(ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

		foreach (ulong steamId in live.OverlayIds.Where(live.IsPlaying))
		{
			PlayerProgress? stored;
			try
			{
				stored = await reader.PlayerAsync(steamId, cancellationToken).ConfigureAwait(false);
			}
			catch (DbException)
			{
				return;
			}

			ProgressSnapshot? overlay = live.Overlay(steamId);
			if ((stored is not null) || (ProgressMerge.SteamIdOf(overlay) is not null))
			{
				Set(ProgressMerge.Merge(stored, overlay), live.Settings);
			}
		}
	}
}
