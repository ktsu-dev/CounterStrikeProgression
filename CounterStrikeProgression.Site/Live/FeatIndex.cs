// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Collections.Concurrent;
using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using Microsoft.Extensions.Hosting;

/// <summary>
/// The feats each player has earned, for the small icons next to names on the ranks page and the scoreboard.
/// Working feats out needs a player's whole progress, so the index is filled in the background and kept current
/// by feed lines and player page loads rather than worked out on every ranks request.
/// </summary>
internal sealed class FeatIndex
{
	private readonly ConcurrentDictionary<ulong, IReadOnlyList<string>> earned = new();

	/// <summary>
	/// Gets the ids of the feats a player has earned.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns>The feat ids in catalog order, or an empty list when none are known.</returns>
	public IReadOnlyList<string> For(ulong steamId) => earned.GetValueOrDefault(steamId) ?? [];

	/// <summary>
	/// Replaces a player's feats with ones worked out from their whole progress.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="feats">The earned feats.</param>
	public void Set(ulong steamId, IEnumerable<FeatDefinition> feats)
	{
		Ensure.NotNull(feats);
		earned[steamId] = [.. feats.Select(f => f.Id)];
	}

	/// <summary>
	/// Adds the feats named by feed lines, so an icon appears the moment a feat is announced.
	/// </summary>
	/// <param name="entries">The new feed lines.</param>
	public void Add(IEnumerable<FeedEntry> entries)
	{
		Ensure.NotNull(entries);
		foreach (FeedEntry entry in entries.Where(e => e.Kind == FeedKind.FeatEarned))
		{
			if (FeatCatalog.FindByName(entry.Title) is { } feat)
			{
				earned.AddOrUpdate(
					entry.SteamId,
					_ => [feat.Id],
					(_, ids) => ids.Contains(feat.Id) ? ids : [.. FeatCatalog.All.Select(f => f.Id).Where(id => (id == feat.Id) || ids.Contains(id))]);
			}
		}
	}
}

/// <summary>
/// Fills the <see cref="FeatIndex"/> for every ranked player shortly after start, then every few minutes.
/// </summary>
/// <param name="index">The index to fill.</param>
/// <param name="reader">Reads the stored progress.</param>
/// <param name="live">The live state, for progress not yet saved and the plugin's settings.</param>
internal sealed class FeatRefresher(FeatIndex index, ProgressReader reader, LiveState live) : BackgroundService
{
	/// <summary>How often the whole index is rebuilt.</summary>
	public static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

	/// <summary>
	/// Rebuilds the index for every ranked player.
	/// </summary>
	/// <param name="cancellationToken">Stops the rebuild.</param>
	/// <returns>A task that completes when the rebuild is done.</returns>
	public async Task RefreshAsync(CancellationToken cancellationToken)
	{
		FeatCatalog feats = new(live.Settings);
		IReadOnlyList<RankingEntry> rankings = await reader.RankingsAsync(cancellationToken).ConfigureAwait(false);
		foreach (RankingEntry entry in rankings)
		{
			PlayerProgress? stored = await reader.PlayerAsync(entry.SteamId, cancellationToken).ConfigureAwait(false);
			ProgressSnapshot? overlay = live.Overlay(entry.SteamId);
			if ((stored is not null) || (overlay is not null))
			{
				index.Set(entry.SteamId, feats.Earned(ProgressMerge.Merge(stored, overlay)));
			}
		}
	}

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			// The first batch brings the plugin's settings, which the feats are measured against.
			await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
			using PeriodicTimer timer = new(Every);
			do
			{
				try
				{
					await RefreshAsync(stoppingToken).ConfigureAwait(false);
				}
				catch (DbException)
				{
					// The next round tries again, and the icons already known stay up meanwhile.
				}
			}
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}
}
