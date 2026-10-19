// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Records;

using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Loads the server records into a <see cref="RecordBook"/> and writes the ones that change, off the game thread.
/// </summary>
/// <param name="store">Where the records are kept.</param>
/// <param name="book">The records, which are only touched on the game thread.</param>
/// <param name="onGameThread">Runs an action on the game thread.</param>
/// <param name="isStorageFailure">Whether an exception is a storage failure worth retrying.</param>
/// <param name="loadFailed">Reports a failed load.</param>
internal sealed class RecordKeeper(IProgressionStore store, RecordBook book, Func<Action, Task> onGameThread, Func<Exception, bool> isStorageFailure, Action<Exception> loadFailed)
{
	/// <summary>
	/// Loads the stored records, retrying until it works or it's told to stop.
	/// </summary>
	/// <param name="ready">Finishes when the store's tables exist.</param>
	/// <param name="stopped">Whether to give up, checked before each attempt.</param>
	/// <param name="retryDelay">How long to wait after a failure.</param>
	/// <returns>Whether the records loaded.</returns>
	public async Task<bool> LoadAsync(Task ready, Func<bool> stopped, TimeSpan retryDelay)
	{
		Ensure.NotNull(stopped);
		await ready.ConfigureAwait(false);
		while (!stopped())
		{
			try
			{
				IReadOnlyList<RecordRow> rows = await store.RecordsAsync(CancellationToken.None).ConfigureAwait(false);
				await onGameThread(() => book.Load(rows)).ConfigureAwait(false);
				return true;
			}
			catch (Exception exception) when (isStorageFailure(exception))
			{
				loadFailed(exception);
			}

			await Task.Delay(retryDelay).ConfigureAwait(false);
		}

		return false;
	}

	/// <summary>
	/// Takes the records that changed, to be written. Call it on the game thread.
	/// </summary>
	/// <param name="writeWithRetries">Runs a write, retrying it, and says whether it worked.</param>
	/// <returns>The write, which flags the rows again if it fails, or <see langword="null"/> when nothing changed.</returns>
	public Func<Task>? TakeSave(Func<Func<Task>, Task<bool>> writeWithRetries)
	{
		Ensure.NotNull(writeWithRetries);
		IReadOnlyList<RecordRow> rows = book.TakeDirty();
		if (rows.Count == 0)
		{
			return null;
		}

		return async () =>
		{
			if (!await writeWithRetries(() => store.SaveRecordsAsync(rows, CancellationToken.None)).ConfigureAwait(false))
			{
				await onGameThread(() => book.Restore(rows)).ConfigureAwait(false);
			}
		};
	}
}
