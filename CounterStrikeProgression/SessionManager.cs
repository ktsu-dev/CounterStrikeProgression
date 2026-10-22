// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Keeps a session for every connected human, and for every bot while a human is on, loads their progress when
/// they join, and saves what changed.
/// </summary>
/// <remarks>
/// <para>
/// Database work runs on the thread pool, and its results come back to the game thread through
/// <see cref="Server.NextFrameAsync(Action)"/>. Every other member is called on the game thread only.
/// </para>
/// <para>
/// Each player has one chain of saves, so a save never starts until the one before it has finished. Without
/// that, two saves could commit out of order and put an older value back. A load also waits for the player's
/// chain, so a quick reconnect can't read a row that's about to be overwritten.
/// </para>
/// <para>
/// The server records have a chain of their own. They load once at startup, and until they have, none are
/// checked, so an empty book can't hand out records below the stored ones.
/// </para>
/// </remarks>
/// <param name="store">Where progress is kept.</param>
/// <param name="engine">The progression engine, which grants the welcome-back bonus on load.</param>
/// <param name="logger">Where failures are reported.</param>
internal sealed class SessionManager(IProgressionStore store, ProgressionEngine engine, ILogger logger)
{
	// No player has Steam ID 0, so the records' saves can share the per-player chains without meeting one.
	private const ulong RecordsChain = 0;

	private const int SaveAttempts = 5;
	private const int InitializeAttempts = 6;

	// Every tracked player saves at round end, bot personas included, so without a cap a full server opens a database
	// connection per player at once, and under disk load the handshakes time out together. Writes past the cap queue.
	private const int MaxConcurrentWrites = 2;

	private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan FirstSaveRetryDelay = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan FirstInitializeRetryDelay = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan RecentlyLeftKeepFor = TimeSpan.FromMinutes(30);

	// Static, because writes from every session share the one database, and a plugin reload keeps no write running.
	private static readonly SemaphoreSlim WriteSlots = new(MaxConcurrentWrites, MaxConcurrentWrites);

	private readonly Dictionary<int, PlayerSession> sessions = [];
	private readonly Dictionary<ulong, Task> saveChains = [];
	private readonly Dictionary<ulong, PlayerProgress> recentlyLeft = [];
	private readonly RecordKeeper records = new(store, engine.Records, Server.NextFrameAsync, IsStorageFailure, exception => Log.RecordsLoadFailed(logger, exception));
	private Task initialized = Task.CompletedTask;
	private long saveFailures;
	private volatile bool stopped;

	/// <summary>Gets every current session.</summary>
	public IEnumerable<PlayerSession> All => sessions.Values;

	/// <summary>Gets a value indicating whether any human has a session, which bots need before they're tracked.</summary>
	public bool HumansOnline => sessions.Values.Any(s => !s.IsBot);

	/// <summary>Gets the sessions of the bot personas being tracked.</summary>
	public IReadOnlyList<PlayerSession> BotSessions => [.. sessions.Values.Where(s => s.IsBot)];

	/// <summary>Gets how many saves failed every retry since the plugin loaded.</summary>
	public long SaveFailures => Interlocked.Read(ref saveFailures);

	/// <summary>Gets the store, for the leaderboard.</summary>
	public IProgressionStore Store { get; } = store;

	/// <summary>
	/// Creates the tables in the background. Loads and saves wait for it.
	/// </summary>
	/// <remarks>
	/// The first connection after a server start can be refused while the pod's network comes up, so it
	/// retries. After the last attempt loads and saves go ahead anyway, because on a database that already
	/// has its tables they work, and their own retries cover a database that's still down.
	/// </remarks>
	public void Initialize()
	{
		initialized = Task.Run(async () =>
		{
			TimeSpan delay = FirstInitializeRetryDelay;
			for (int attempt = 1; attempt <= InitializeAttempts; attempt++)
			{
				try
				{
					await Store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
					return;
				}
				catch (Exception exception) when (IsStorageFailure(exception))
				{
					Log.InitializeFailed(logger, attempt, InitializeAttempts, exception);
				}

				await Task.Delay(delay).ConfigureAwait(false);
				delay *= 2;
			}
		});
		_ = Task.Run(() => records.LoadAsync(initialized, () => stopped, LoadRetryDelay));
	}

	/// <summary>
	/// Writes the server records that changed, in the background.
	/// </summary>
	public void SaveRecords()
	{
		if (records.TakeSave(write => SaveWithRetriesAsync(RecordsChain, write)) is { } save)
		{
			Chain(RecordsChain, save);
		}
	}

	/// <summary>
	/// Gets a player's session.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <returns>The session, or <see langword="null"/> for untracked bots and players not yet connected.</returns>
	public PlayerSession? Get(CCSPlayerController? player) =>
		GameState.IsTracked(player) && sessions.TryGetValue(player!.Slot, out PlayerSession? session) && (session.SteamId == GameState.PlayerIdOf(player)) ? session : null;

	/// <summary>
	/// Starts a session for a joining human, or for a bot while a human is on, and loads their progress.
	/// </summary>
	/// <remarks>
	/// A bot's id comes from its name, so a second bot with a name already in use gets no session, and a bot
	/// kicked and added again picks its progress back up from the players who recently left.
	/// </remarks>
	/// <param name="player">The player.</param>
	public void Connect(CCSPlayerController player)
	{
		Ensure.NotNull(player);
		if (!GameState.IsTracked(player) || (Get(player) is not null))
		{
			return;
		}

		ulong id = GameState.PlayerIdOf(player);
		EndStaleSessions(id);
		if (!BotTracking.MayStart(id, HumansOnline, sessions.Values.Select(s => s.SteamId)))
		{
			return;
		}

		// A disconnect that never arrived leaves someone else's session in the slot, so end it properly first.
		DisconnectSlot(player.Slot);

		PlayerSession session = new(player.Slot, id);

		// A bot that stays on while untracked keeps its kills on the scoreboard, so the recap counts from here.
		session.Recap.StartScoreboard(GameState.ScoreboardOf(player));
		sessions[player.Slot] = session;
		string name = player.PlayerName;

		if (recentlyLeft.Remove(session.SteamId, out PlayerProgress? kept))
		{
			Attach(session, kept, name);
			return;
		}

		Task pendingSaves = saveChains.GetValueOrDefault(session.SteamId) ?? Task.CompletedTask;
		_ = Task.Run(() => LoadAsync(session, name, pendingSaves));
	}

	/// <summary>
	/// Ends every session holding an id whose slot no longer holds a controller with it, which a disconnect that
	/// never arrived leaves behind. Without this, such a session would keep a bot persona's name from being
	/// tracked in any other slot. The progress is kept with the players who recently left.
	/// </summary>
	/// <param name="id">The id.</param>
	private void EndStaleSessions(ulong id)
	{
		foreach (PlayerSession stale in sessions.Values.Where(s => (s.SteamId == id) && (GameState.PlayerIdOf(SlotController(s.Slot)) != id)).ToList())
		{
			DisconnectSlot(stale.Slot);
		}
	}

	private static CCSPlayerController? SlotController(int slot) =>
		Utilities.GetPlayerFromSlot(slot) is { } controller && GameState.IsTracked(controller) ? controller : null;

	/// <summary>
	/// Saves and ends the session in a slot, if there is one.
	/// </summary>
	/// <param name="slot">The slot.</param>
	public void DisconnectSlot(int slot)
	{
		if (!sessions.Remove(slot, out PlayerSession? session))
		{
			return;
		}

		session.IsGone = true;
		if (session.Progress is PlayerProgress progress)
		{
			progress.LastSeen = DateTime.UtcNow;
			progress.IsDirty = true;
			recentlyLeft[session.SteamId] = progress;
			Save(progress);
		}
	}

	/// <summary>
	/// Saves and ends every bot persona's session, for when the last human leaves. Their progress is kept with
	/// the players who recently left, so bots that stay on pick it back up when a human returns.
	/// </summary>
	public void EndBotSessions()
	{
		foreach (PlayerSession session in BotSessions)
		{
			DisconnectSlot(session.Slot);
		}
	}

	/// <summary>
	/// Saves every session's changes.
	/// </summary>
	public void SaveAll()
	{
		foreach (PlayerSession session in sessions.Values)
		{
			if (session.Progress is not null)
			{
				Save(session.Progress);
			}
		}

		SaveRecords();

		// Players who left are kept so a quick reconnect reuses their progress, and so rows whose save ran out
		// of retries get another try. Once they're saved and have been gone a while, they're dropped.
		DateTime cutoff = DateTime.UtcNow - RecentlyLeftKeepFor;
		foreach ((ulong steamId, PlayerProgress progress) in recentlyLeft.ToList())
		{
			Save(progress);
			if ((progress.LastSeen < cutoff) && !saveChains.ContainsKey(steamId))
			{
				recentlyLeft.Remove(steamId);
			}
		}
	}

	/// <summary>
	/// Saves everything and waits a short while for the writes, for plugin unload, when nothing would
	/// otherwise keep them alive.
	/// </summary>
	/// <param name="timeout">How long to wait.</param>
	public void Shutdown(TimeSpan timeout)
	{
		stopped = true;
		foreach (int slot in sessions.Keys.ToList())
		{
			DisconnectSlot(slot);
		}

		SaveRecords();

		Task[] pending = [.. saveChains.Values];
		if (pending.Length > 0)
		{
			// Unload has no async path, and returning before the writes finish would drop them.
			_ = Task.WaitAll(pending, timeout);
		}
	}

	/// <summary>
	/// Applies an admin correction to a player, whether or not they're connected, and writes it at once.
	/// </summary>
	/// <remarks>
	/// The write joins the player's chain of saves, so it lands after any save already queued and before any
	/// load that follows, and a reconnect can't read the old rows back.
	/// </remarks>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="correction">The correction.</param>
	/// <param name="ranks">The rank ladder.</param>
	/// <param name="done">Called on the game thread once the write finishes, with the outcome, or <see langword="null"/> when it failed or the player has no stored progress.</param>
	/// <returns><see langword="false"/> when the player is connected but their progress hasn't loaded, so nothing was done.</returns>
	public bool Correct(ulong steamId, AdminCorrection correction, RankLadder ranks, Action<CorrectionOutcome?> done)
	{
		Ensure.NotNull(correction);
		Ensure.NotNull(ranks);
		Ensure.NotNull(done);

		PlayerSession? session = sessions.Values.FirstOrDefault(s => s.SteamId == steamId);
		PlayerProgress? progress = session?.Progress ?? recentlyLeft.GetValueOrDefault(steamId);
		if ((session is not null) && (session.Progress is null))
		{
			return false;
		}

		if (progress is not null)
		{
			CorrectionOutcome outcome = correction.Apply(progress, ranks);
			ProgressSnapshot rows = ProgressSnapshot.Capture(progress);
			Chain(steamId, async () =>
			{
				bool written = await SaveWithRetriesAsync(steamId, () => correction.WriteAsync(Store, rows, CancellationToken.None)).ConfigureAwait(false);
				if (!written)
				{
					await Server.NextFrameAsync(() => rows.Restore(progress)).ConfigureAwait(false);
				}

				await Server.NextFrameAsync(() => done(written ? outcome : null)).ConfigureAwait(false);
			});
			return true;
		}

		Chain(steamId, async () =>
		{
			CorrectionOutcome? outcome = null;
			try
			{
				if (await correction.ApplyToStoredAsync(Store, steamId, ranks, CancellationToken.None).ConfigureAwait(false) is (CorrectionOutcome applied, ProgressSnapshot rows)
					&& await SaveWithRetriesAsync(steamId, () => correction.WriteAsync(Store, rows, CancellationToken.None)).ConfigureAwait(false))
				{
					outcome = applied;
				}
			}
			catch (Exception exception) when (IsStorageFailure(exception))
			{
				Log.LoadFailed(logger, steamId, exception);
			}

			await Server.NextFrameAsync(() => done(outcome)).ConfigureAwait(false);
		});
		return true;
	}

	/// <summary>
	/// Gets a value indicating whether an exception is a storage problem the plugin should survive.
	/// </summary>
	/// <param name="exception">The exception.</param>
	/// <returns>Whether to catch it.</returns>
	public static bool IsStorageFailure(Exception exception) =>
		exception is DbException or InvalidOperationException or TimeoutException or IOException or SocketException or OperationCanceledException;

	private async Task LoadAsync(PlayerSession session, string name, Task pendingSaves)
	{
		await initialized.ConfigureAwait(false);
		await WaitQuietly(pendingSaves).ConfigureAwait(false);

		// A player whose load fails gets no progress until it succeeds. Starting them empty would let the
		// next save overwrite their stored totals with the few points earned since.
		while (!session.IsGone)
		{
			try
			{
				PlayerProgress? loaded = await Store.LoadAsync(session.SteamId, CancellationToken.None).ConfigureAwait(false);
				await Server.NextFrameAsync(() => Attach(session, loaded, name)).ConfigureAwait(false);
				return;
			}
			catch (Exception exception) when (IsStorageFailure(exception))
			{
				Log.LoadFailed(logger, session.SteamId, exception);
			}

			await Task.Delay(LoadRetryDelay).ConfigureAwait(false);
		}
	}

	private void Attach(PlayerSession session, PlayerProgress? loaded, string name)
	{
		if (!sessions.TryGetValue(session.Slot, out PlayerSession? current) || !ReferenceEquals(current, session))
		{
			return;
		}

		PlayerProgress progress = loaded ?? new PlayerProgress(session.SteamId);
		DateTime now = DateTime.UtcNow;
		_ = engine.GrantWelcomeBack(progress, now);
		progress.Name = name;
		progress.LastSeen = now;
		progress.IsDirty = true;
		Rivalry.Join(progress, sessions.Values.Select(s => s.Progress).OfType<PlayerProgress>());
		session.Progress = progress;
	}

	private void Save(PlayerProgress progress)
	{
		ProgressSnapshot snapshot = ProgressSnapshot.Capture(progress);
		if (snapshot.IsEmpty)
		{
			return;
		}

		Chain(progress.SteamId, async () =>
		{
			if (!await SaveWithRetriesAsync(progress.SteamId, () => Store.SaveAsync(snapshot, CancellationToken.None)).ConfigureAwait(false))
			{
				// Out of retries: flag the rows again, so whichever save comes next for this player writes them.
				await Server.NextFrameAsync(() => snapshot.Restore(progress)).ConfigureAwait(false);
			}
		});
	}

	/// <summary>
	/// Runs a player's database work after everything already in their chain, once the tables exist.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="work">The work.</param>
	private void Chain(ulong steamId, Func<Task> work)
	{
		Task previous = saveChains.GetValueOrDefault(steamId) ?? Task.CompletedTask;
		Task next = Task.Run(async () =>
		{
			await WaitQuietly(previous).ConfigureAwait(false);
			await initialized.ConfigureAwait(false);
			await work().ConfigureAwait(false);
		});
		saveChains[steamId] = next;
		_ = next.ContinueWith(
			_ => Server.NextFrame(() => ForgetChain(steamId, next)),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A save runs unobserved on the thread pool, so anything it throws must be logged here or it vanishes.")]
	private async Task<bool> SaveWithRetriesAsync(ulong steamId, Func<Task> write)
	{
		TimeSpan delay = FirstSaveRetryDelay;
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				await WriteAsync(write).ConfigureAwait(false);
				return true;
			}
			catch (Exception exception)
			{
				if (steamId == RecordsChain)
				{
					Log.RecordsSaveFailed(logger, exception);
				}
				else
				{
					Log.SaveFailed(logger, steamId, exception);
				}
				if (attempt >= SaveAttempts)
				{
					Interlocked.Increment(ref saveFailures);
					return false;
				}
			}

			await Task.Delay(delay).ConfigureAwait(false);
			delay *= 2;
		}
	}

	// Holds one of the write slots for a single attempt, so a retry's backoff never keeps a slot from another player.
	private static async Task WriteAsync(Func<Task> write)
	{
		await WriteSlots.WaitAsync().ConfigureAwait(false);
		try
		{
			await write().ConfigureAwait(false);
		}
		finally
		{
			WriteSlots.Release();
		}
	}

	private void ForgetChain(ulong steamId, Task finished)
	{
		if (saveChains.TryGetValue(steamId, out Task? latest) && ReferenceEquals(latest, finished))
		{
			saveChains.Remove(steamId);
		}
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Waiting on an earlier save only orders the work, and that save already logged its own failure.")]
	private static async Task WaitQuietly(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// The failed task reported itself when it ran.
		}
	}
}
