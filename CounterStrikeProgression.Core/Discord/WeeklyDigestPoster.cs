// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Discord;

using System.Diagnostics.CodeAnalysis;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Runs <see cref="WeeklyDigest.PostAsync"/> from the thread pool at each map start until this week's digest is
/// handled, one check at a time. A check that fails is reported and tried again at the next map start.
/// </summary>
/// <param name="store">Where last week's XP is read and the posted week is recorded.</param>
/// <param name="ranks">The rank ladder.</param>
/// <param name="progression">The progression settings.</param>
/// <param name="settings">The Discord settings.</param>
/// <param name="send">Queues the post, returning whether it was queued.</param>
/// <param name="onFailure">Called with a check's failure.</param>
internal sealed class WeeklyDigestPoster(IProgressionStore store, RankLadder ranks, ProgressionSettings progression, DiscordSettings settings, Func<string, bool> send, Action<Exception> onFailure)
{
	// The Monday whose digest is handled, as a day number, and whether a check is running.
	private int handledWeek = -1;
	private int busy;

	/// <summary>
	/// Starts a check on the thread pool, unless this week's digest is handled or a check is already running.
	/// </summary>
	/// <param name="utcNow">The time now, in UTC.</param>
	/// <returns>The check, or <see cref="Task.CompletedTask"/> when none was started.</returns>
	public Task Check(DateTime utcNow)
	{
		int week = DailyRotation.WeekStart(DailyRotation.Day(utcNow)).DayNumber;
		if ((Volatile.Read(ref handledWeek) == week) || (Interlocked.Exchange(ref busy, 1) == 1))
		{
			return Task.CompletedTask;
		}

		return Task.Run(() => RunAsync(week, utcNow));
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The check runs unobserved on the thread pool, so anything it throws must be reported here or it vanishes.")]
	private async Task RunAsync(int week, DateTime utcNow)
	{
		try
		{
			await WeeklyDigest.PostAsync(store, ranks, progression, settings, send, utcNow, CancellationToken.None).ConfigureAwait(false);
			Volatile.Write(ref handledWeek, week);
		}
		catch (Exception exception)
		{
			onFailure(exception);
		}
		finally
		{
			Volatile.Write(ref busy, 0);
		}
	}
}
