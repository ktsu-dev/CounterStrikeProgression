// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Checks once a second whether the server went online or offline, and tells the browsers when it flips.
/// </summary>
/// <param name="live">The live state.</param>
/// <param name="broadcaster">Where the <c>online</c> event goes.</param>
internal sealed class OnlineWatcher(LiveState live, Broadcaster broadcaster) : BackgroundService
{
	private bool? last;

	/// <summary>
	/// Publishes <c>online</c> when the state differs from the last check.
	/// </summary>
	/// <returns><see langword="true"/> when an event went out.</returns>
	public bool Check()
	{
		bool online = live.IsOnline;
		bool flipped = last is { } previous && previous != online;
		last = online;
		if (flipped)
		{
			broadcaster.Publish("online", online);
		}

		return flipped;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
		Check();
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
			{
				Check();
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}
}
