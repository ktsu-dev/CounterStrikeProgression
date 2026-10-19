// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Discord;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// The Discord channel big moments are posted to. Off by default, because it sends player names and progress
/// outside the server.
/// </summary>
public sealed class DiscordSettings
{
	/// <summary>
	/// Gets or sets the channel's webhook URL, such as <c>https://discord.com/api/webhooks/...</c>. With an empty
	/// URL, the default, nothing is posted.
	/// </summary>
	[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The config file holds the URL as text, and an empty value means no webhook. The plugin parses and validates it.")]
	public string WebhookUrl { get; set; } = "";

	/// <summary>Gets or sets the name the posts appear under. Empty keeps the name set on the webhook.</summary>
	public string Username { get; set; } = "";

	/// <summary>Gets or sets a value indicating whether earned feats are posted.</summary>
	public bool Feats { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether server records set or broken are posted.</summary>
	public bool Records { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether epic skill shots are posted.</summary>
	public bool EpicSkillShots { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether weapon mastery stars are posted.</summary>
	public bool MasteryStars { get; set; } = true;

	/// <summary>Gets or sets a value indicating whether rank-ups from <see cref="MinimumRank"/> up are posted.</summary>
	public bool RankUps { get; set; } = true;

	/// <summary>
	/// Gets or sets the lowest rank whose rank-ups are posted: a rank such as <c>Gold Nova</c>, which covers all of its
	/// stages, or one stage such as <c>Gold Nova · III</c>. A name that matches neither counts as Gold Nova.
	/// </summary>
	public string MinimumRank { get; set; } = "Gold Nova";

	/// <summary>Gets or sets how many posts wait to send before the oldest is dropped. Values under 1 count as 1.</summary>
	public int QueueLimit { get; set; } = 50;
}
