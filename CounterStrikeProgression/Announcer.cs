// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>
/// Turns what a player earned into chat lines. Rank-ups, finished tiers, milestone weapon levels, and weekly
/// contracts go to everyone, other level-ups, daily contracts, and rare skill shots go to the player, and common skill shots stay quiet unless
/// the config turns them on.
/// </summary>
/// <param name="config">The chat prefix and announcement settings.</param>
internal sealed class Announcer(PluginConfig config)
{
	// Skill shots can land several times a second in a firefight, so the player hears about them at most this often.
	private const double SkillShotMessageSeconds = 1.5;

	/// <summary>
	/// Announces a result.
	/// </summary>
	/// <param name="player">The player who earned it.</param>
	/// <param name="session">The player's session.</param>
	/// <param name="result">What they earned.</param>
	public void Announce(CCSPlayerController player, PlayerSession session, ProgressionResult result)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(session);
		Ensure.NotNull(result);

		List<string> skillShots = [];
		long skillShotXp = 0;
		foreach (ProgressionNotice notice in result.Notices)
		{
			switch (notice.Kind)
			{
				case NoticeKind.SkillShot when (notice.SkillShot is not null) && ((notice.SkillShot.Rarity != Rarity.Common) || config.AnnounceCommonSkillShots):
					skillShots.Add(notice.SkillShot.Name);
					skillShotXp += notice.Xp;
					break;

				case NoticeKind.TierCompleted:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} finished {ChatColors.Yellow}{notice.Weapon?.DisplayName} {notice.ChallengeName} {PanelBuilder.Numeral(notice.Value)}{ChatColors.Default} (+{Xp(notice.Xp)} XP)");
					break;

				case NoticeKind.WeaponLevelUp when notice.Broadcast:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default}'s {notice.Weapon?.DisplayName} reached level {ChatColors.Yellow}{notice.Value}{ChatColors.Default}");
					break;

				case NoticeKind.WeaponLevelUp:
					player.PrintToChat($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {notice.Weapon?.DisplayName} reached level {ChatColors.Yellow}{notice.Value}{ChatColors.Default} (+{Xp(notice.Xp)} XP)");
					break;

				case NoticeKind.FeatEarned when notice.Feat is not null:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} earned the feat {ChatColors.Gold}{notice.Feat.Name}{ChatColors.Default}: {notice.Feat.Description}!");
					break;

				case NoticeKind.ContractCompleted when notice.Contract is { Definition.Period: ContractPeriod.Weekly } weekly:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} finished the weekly contract {ChatColors.Gold}{weekly.Definition.Description}{ChatColors.Default} (+{Xp(notice.Xp)} XP)");
					break;

				case NoticeKind.ContractCompleted when notice.Contract is not null:
					player.PrintToChat($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} Daily contract done: {ChatColors.Yellow}{notice.Contract.Definition.Description}{ChatColors.Default} (+{Xp(notice.Xp)} XP)");
					break;

				case NoticeKind.RecordBroken when notice.Record is { } record:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} {RecordText(record)}");
					break;

				case NoticeKind.RankUp:
					Server.PrintToChatAll($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.Green}{player.PlayerName}{ChatColors.Default} ranked up to {ChatColors.Gold}{RankLadder.Names[notice.Value]}{ChatColors.Default}!");
					break;
			}
		}

		double now = Server.CurrentTime;
		if ((skillShots.Count > 0) && ((now - session.LastSkillShotMessage) >= SkillShotMessageSeconds))
		{
			session.LastSkillShotMessage = now;
			player.PrintToChat($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {ChatColors.LightPurple}{string.Join(", ", skillShots)}{ChatColors.Default} +{Xp(skillShotXp)} XP");
		}
	}

	/// <summary>
	/// Tells a player what they earned this map, when the config allows it.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="recap">Their recap.</param>
	public void AnnounceRecap(CCSPlayerController player, MapRecap recap)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(recap);

		if (config.AnnounceMapRecap)
		{
			player.PrintToChat($" {ChatColors.Gold}{config.ChatPrefix}{ChatColors.Default} {PanelFormatter.ToRecapChat(recap)}");
		}
	}

	private static string RecordText(RecordBreak record)
	{
		RecordRow holder = record.Holder;
		string name = $"{ChatColors.Gold}{record.Definition.Name}{ChatColors.Default}";
		string value = $"{ChatColors.Yellow}{record.Definition.Format(holder.Value)}{ChatColors.Default}";
		if (holder.PreviousValue is not { } previous)
		{
			return $"set the server record for {name}: {value}!";
		}

		string was = record.Definition.Format(previous);
		return record.BeatOwn
			? $"beat their own server record for {name}: {value} (was {was})!"
			: $"broke {ChatColors.Green}{holder.PreviousName}{ChatColors.Default}'s server record for {name}: {value} (was {was})!";
	}

	private static string Xp(long xp) => xp.ToString("N0", CultureInfo.InvariantCulture);
}
