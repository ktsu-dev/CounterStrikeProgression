// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Text.Json;
using System.Threading.Channels;

/// <summary>
/// Fans server-sent events out to every connected browser. Each subscriber has its own bounded queue that
/// drops its oldest message when full, so a slow browser can't hold up the rest.
/// </summary>
internal sealed class Broadcaster
{
	/// <summary>How many messages a subscriber can fall behind before the oldest are dropped.</summary>
	public const int QueueLength = 100;

	private readonly Lock gate = new();
	private readonly HashSet<Channel<string>> subscribers = [];

	/// <summary>Gets the number of connected subscribers.</summary>
	public int SubscriberCount
	{
		get
		{
			lock (gate)
			{
				return subscribers.Count;
			}
		}
	}

	/// <summary>
	/// Adds a subscriber that stays until the token is cancelled.
	/// </summary>
	/// <param name="cancellationToken">Removes the subscriber and completes its reader when cancelled.</param>
	/// <returns>The subscriber's messages, each a complete event-stream frame.</returns>
	public ChannelReader<string> Subscribe(CancellationToken cancellationToken)
	{
		Channel<string> channel = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueLength)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		});

		lock (gate)
		{
			subscribers.Add(channel);
		}

		cancellationToken.Register(() => Remove(channel));
		return channel.Reader;
	}

	/// <summary>
	/// Sends an event to every subscriber.
	/// </summary>
	/// <param name="eventName">The event name.</param>
	/// <param name="payload">The data, written as JSON.</param>
	public void Publish(string eventName, object payload)
	{
		string frame = Frame(eventName, payload);
		lock (gate)
		{
			foreach (Channel<string> channel in subscribers)
			{
				channel.Writer.TryWrite(frame);
			}
		}
	}

	/// <summary>
	/// Formats one event-stream frame.
	/// </summary>
	/// <param name="eventName">The event name.</param>
	/// <param name="payload">The data, written as JSON on one line.</param>
	/// <returns>The frame, ending with the blank line that dispatches it.</returns>
	public static string Frame(string eventName, object payload)
	{
		Ensure.NotNull(eventName);
		Ensure.NotNull(payload);
		return $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, payload.GetType(), SiteJson.Options)}\n\n";
	}

	private void Remove(Channel<string> channel)
	{
		lock (gate)
		{
			subscribers.Remove(channel);
		}

		channel.Writer.TryComplete();
	}
}
