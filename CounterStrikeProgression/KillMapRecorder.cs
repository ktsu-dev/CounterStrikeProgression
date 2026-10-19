// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Weapons;
using Microsoft.Extensions.Logging;

/// <summary>
/// Notes where kills happen for the site's heatmaps, and writes them in batches on the thread pool. Every
/// member is called on the game thread.
/// </summary>
/// <param name="settings">The heatmap settings.</param>
/// <param name="store">Where kills are kept.</param>
/// <param name="logger">Where failures are reported.</param>
internal sealed class KillMapRecorder(HeatmapSettings settings, IKillMapStore store, ILogger logger)
{
	private const int InitializeAttempts = 6;

	private static readonly TimeSpan FirstInitializeRetryDelay = TimeSpan.FromSeconds(2);

	private readonly KillPointBuffer buffer = new(settings);
	private volatile bool initialized;
	private bool writing;

	/// <summary>
	/// Creates the recorder the config asks for.
	/// </summary>
	/// <param name="config">The plugin config.</param>
	/// <param name="logger">Where failures are reported.</param>
	/// <returns>The recorder, or <see langword="null"/> when heatmaps are off.</returns>
	public static KillMapRecorder? Create(PluginConfig config, ILogger logger)
	{
		Ensure.NotNull(config);
		HeatmapSettings settings = config.Heatmaps ?? new();
		if (!settings.Enabled)
		{
			return null;
		}

		IKillMapStore store = string.IsNullOrWhiteSpace(config.Database.Host) ? new InMemoryKillMapStore() : new MySqlKillMapStore(config.Database);
		return new KillMapRecorder(settings, store, logger);
	}

	/// <summary>
	/// Creates the table in the background, so the site's read-only user can be granted it before the first kill.
	/// A write that finds it still missing creates it then.
	/// </summary>
	public void Initialize() => _ = Task.Run(async () =>
	{
		TimeSpan delay = FirstInitializeRetryDelay;
		for (int attempt = 1; attempt <= InitializeAttempts && !initialized; attempt++)
		{
			if (await TryInitializeAsync().ConfigureAwait(false))
			{
				return;
			}

			await Task.Delay(delay).ConfigureAwait(false);
			delay *= 2;
		}
	});

	/// <summary>
	/// Notes a kill, if a human is on either end of it.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="killer">The killer.</param>
	/// <param name="victim">The victim.</param>
	/// <param name="weapon">The weapon.</param>
	/// <param name="headshot">Whether it was a headshot.</param>
	public void Record(string map, CCSPlayerController killer, CCSPlayerController victim, WeaponInfo weapon, bool headshot)
	{
		Ensure.NotNull(killer);
		Ensure.NotNull(victim);
		Ensure.NotNull(weapon);

		if ((GameState.PawnOf(killer)?.AbsOrigin is not { } from) || (GameState.PawnOf(victim)?.AbsOrigin is not { } to))
		{
			return;
		}

		buffer.Add(new KillPoint(
			map,
			GameState.IsHuman(killer) ? killer.SteamID : 0,
			GameState.IsHuman(victim) ? victim.SteamID : 0,
			weapon.Key.WeakString,
			headshot,
			KillPoint.Round(from.X),
			KillPoint.Round(from.Y),
			KillPoint.Round(from.Z),
			KillPoint.Round(to.X),
			KillPoint.Round(to.Y),
			KillPoint.Round(to.Z),
			DateTime.UtcNow));
	}

	/// <summary>
	/// Writes the waiting kills in the background, unless a write is still running. A failed write puts them back.
	/// </summary>
	public void Flush()
	{
		if (writing || (buffer.Count == 0))
		{
			return;
		}

		IReadOnlyList<KillPoint> points = buffer.Drain();
		int max = Math.Max(100, settings.MaxPointsPerMap);
		writing = true;
		_ = Task.Run(async () =>
		{
			bool written = await WriteAsync(points, max).ConfigureAwait(false);
			await Server.NextFrameAsync(() =>
			{
				writing = false;
				if (!written)
				{
					buffer.Return(points);
				}
			}).ConfigureAwait(false);
		});
	}

	/// <summary>
	/// Writes the waiting kills and waits a short while for them, for plugin unload.
	/// </summary>
	/// <param name="timeout">How long to wait.</param>
	public void Shutdown(TimeSpan timeout)
	{
		if (writing || (buffer.Count == 0))
		{
			return;
		}

		IReadOnlyList<KillPoint> points = buffer.Drain();
		_ = Task.Run(() => WriteAsync(points, Math.Max(100, settings.MaxPointsPerMap))).Wait(timeout);
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "This runs unobserved on the thread pool, so anything it throws must be logged here or it vanishes.")]
	private async Task<bool> TryInitializeAsync()
	{
		try
		{
			await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
			initialized = true;
			return true;
		}
		catch (Exception exception)
		{
			Log.HeatmapInitializeFailed(logger, exception);
			return false;
		}
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The write runs unobserved on the thread pool, so anything it throws must be logged here or it vanishes.")]
	private async Task<bool> WriteAsync(IReadOnlyList<KillPoint> points, int max)
	{
		try
		{
			if (!initialized)
			{
				await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
				initialized = true;
			}

			await store.AddAsync(points, max, CancellationToken.None).ConfigureAwait(false);
			return true;
		}
		catch (Exception exception)
		{
			Log.HeatmapWriteFailed(logger, points.Count, exception);
			return false;
		}
	}
}
