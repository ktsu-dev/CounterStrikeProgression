// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Shows each player's rank, such as <c>[GN III]</c>, as their scoreboard clan tag and before their chat messages.
/// </summary>
/// <remarks>
/// CS2-SimpleAdmin never sets a clan tag, and its own chat hook only blocks or narrows gagged players' messages,
/// so neither fights it. The chat prefix is added to the name in the chat message itself rather than by
/// swallowing <c>say</c> and printing a copy, so gags, team chat, and dead chat all behave as before.
/// </remarks>
/// <param name="settings">Which tags are shown.</param>
/// <param name="sessions">The connected players.</param>
internal sealed class RankTags(RankTagSettings settings, SessionManager sessions)
{
	// CS_UM_SayText2, the message that carries a player's chat line to everyone who should see it.
	private const int SayText2 = 118;

	/// <summary>
	/// Hooks chat, when the chat prefix is on.
	/// </summary>
	/// <param name="plugin">The plugin to register with.</param>
	public void Register(BasePlugin plugin)
	{
		Ensure.NotNull(plugin);
		if (settings.ChatPrefix)
		{
			plugin.HookUserMessage(SayText2, OnSayText2, HookMode.Pre);
		}
	}

	/// <summary>
	/// Sets a player's clan tag when their rank has changed since it was last set, or it was asked to be set again.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	public void Tick(CCSPlayerController player, PlayerSession session)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(session);
		if (!settings.ClanTag || (session.Progress is not PlayerProgress progress) || (session.ClanTagRank == progress.Rank))
		{
			return;
		}

		player.Clan = RankLadder.TagFor(progress.Rank);
		Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
		session.ClanTagRank = progress.Rank;
	}

	/// <summary>
	/// Sets a player's clan tag again on the next tick, after the game may have put their own back, such as on a
	/// spawn or a team change.
	/// </summary>
	/// <param name="session">The player's session.</param>
	public static void Refresh(PlayerSession session)
	{
		Ensure.NotNull(session);
		session.ClanTagRank = -1;
	}

	private HookResult OnSayText2(UserMessage message)
	{
		CCSPlayerController? author = Utilities.GetPlayerFromIndex(message.ReadInt("entityindex"));
		if (sessions.Get(author) is { Progress: PlayerProgress progress })
		{
			message.SetString("param1", $"{RankLadder.TagFor(progress.Rank)} {message.ReadString("param1")}");
		}

		return HookResult.Continue;
	}
}
