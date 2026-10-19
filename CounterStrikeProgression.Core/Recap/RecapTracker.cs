// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Recap;

using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>
/// Adds up what a player earns over one map, for the recap at map end or when they leave.
/// </summary>
/// <param name="startedAt">When the session started, in UTC.</param>
internal sealed class RecapTracker(DateTime startedAt)
{
	private double xp;
	private int levels;
	private int tiers;
	private int rankUps;
	private int feats;
	private ProgressionNotice? best;

	/// <summary>Gets when the session started, in UTC.</summary>
	public DateTime StartedAt { get; private set; } = startedAt;

	/// <summary>Gets the skill shots earned since the session started.</summary>
	public int SkillShots { get; private set; }

	/// <summary>Gets how many of <see cref="SkillShots"/> were against bots.</summary>
	public int BotSkillShots { get; private set; }

	/// <summary>Gets a value indicating whether nothing has been earned since the session started.</summary>
	public bool IsEmpty => (xp <= 0) && (best is null);

	/// <summary>
	/// Adds what one event earned.
	/// </summary>
	/// <param name="result">What the event earned.</param>
	public void Add(ProgressionResult result)
	{
		Ensure.NotNull(result);

		xp += result.XpEarned;
		foreach (ProgressionNotice notice in result.Notices.Where(n => n.Kind != NoticeKind.StreakDay))
		{
			switch (notice.Kind)
			{
				case NoticeKind.SkillShot:
					SkillShots++;
					if (notice.VsBot)
					{
						BotSkillShots++;
					}

					break;
				case NoticeKind.WeaponLevelUp:
					levels++;
					break;
				case NoticeKind.TierCompleted:
					tiers++;
					break;
				case NoticeKind.RankUp:
					rankUps++;
					break;
				case NoticeKind.FeatEarned:
					feats++;
					break;
			}

			if ((best is null) || (Weight(notice).CompareTo(Weight(best)) > 0))
			{
				best = notice;
			}
		}
	}

	/// <summary>
	/// Ends the session, and starts the next one empty.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="playerName">The player's name.</param>
	/// <param name="map">The map played.</param>
	/// <param name="kills">Kills on the scoreboard.</param>
	/// <param name="deaths">Deaths on the scoreboard.</param>
	/// <param name="at">When the session ended, in UTC, which is also when the next one starts.</param>
	/// <returns>The recap.</returns>
	public MapRecap Finish(ulong steamId, string playerName, string map, int kills, int deaths, DateTime at)
	{
		Ensure.NotNull(playerName);
		Ensure.NotNull(map);

		MapRecap recap = new(
			steamId,
			playerName,
			map,
			StartedAt,
			at,
			(long)Math.Floor(Math.Max(0, xp)),
			kills,
			deaths,
			SkillShots,
			levels,
			tiers,
			rankUps,
			feats,
			best is null ? null : Describe(best));

		Restart(at);
		return recap;
	}

	/// <summary>
	/// Ends the session when anything was earned, and starts the next one empty either way.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="playerName">The player's name.</param>
	/// <param name="map">The map played.</param>
	/// <param name="kills">Kills on the scoreboard.</param>
	/// <param name="deaths">Deaths on the scoreboard.</param>
	/// <param name="at">When the session ended, in UTC, which is also when the next one starts.</param>
	/// <returns>The recap, or <see langword="null"/> when nothing was earned.</returns>
	public MapRecap? TryFinish(ulong steamId, string playerName, string map, int kills, int deaths, DateTime at)
	{
		if (IsEmpty)
		{
			Restart(at);
			return null;
		}

		return Finish(steamId, playerName, map, kills, deaths, at);
	}

	/// <summary>
	/// Forgets what was earned and starts a new session.
	/// </summary>
	/// <param name="at">When the new session starts, in UTC.</param>
	public void Restart(DateTime at)
	{
		StartedAt = at;
		xp = 0;
		SkillShots = 0;
		BotSkillShots = 0;
		levels = 0;
		tiers = 0;
		rankUps = 0;
		feats = 0;
		best = null;
	}

	/// <summary>
	/// Describes a moment for the recap.
	/// </summary>
	/// <param name="notice">The moment.</param>
	/// <returns>The text, such as "Wallbang with the AK-47" or "AWP level 10".</returns>
	internal static string Describe(ProgressionNotice notice)
	{
		Ensure.NotNull(notice);

		string? weapon = notice.Weapon?.DisplayName;
		return notice.Kind switch
		{
			NoticeKind.FeatEarned => $"the feat {notice.Feat?.Name}",
			NoticeKind.RecordBroken => $"the server record for {notice.Record?.Definition.Name}",
			NoticeKind.RankUp => $"ranked up to {RankLadder.Names[notice.Value]}",
			NoticeKind.TierCompleted => $"{weapon} {notice.ChallengeName} {PanelBuilder.Numeral(notice.Value)}".TrimStart(),
			NoticeKind.WeaponLevelUp => $"{weapon} level {notice.Value}".TrimStart(),
			NoticeKind.MasteryStar => WeaponLevelCurve.StarTitle(notice.Weapon, notice.Value),
			NoticeKind.BountyCollected => $"{notice.PlayerName}'s bounty",
			_ => weapon is null ? $"{notice.SkillShot?.Name}" : $"{notice.SkillShot?.Name} with the {weapon}",
		};
	}

	/// <summary>
	/// Ranks moments: server records, then feats, then mastery stars, rank-ups, then epic skill shots, finished tiers and bounties, rare skill shots, weapon
	/// levels, and common skill shots. Within a kind, the one that paid more wins, then the higher level, tier,
	/// or rank, and the first wins a tie.
	/// </summary>
	private static (int Kind, long Xp, int Value) Weight(ProgressionNotice notice)
	{
		int kind = notice.Kind switch
		{
			NoticeKind.RecordBroken => 8,
			NoticeKind.FeatEarned => 7,
			NoticeKind.MasteryStar => 6,
			NoticeKind.RankUp => 5,
			NoticeKind.SkillShot when notice.SkillShot?.Rarity == Rarity.Epic => 4,
			NoticeKind.TierCompleted or NoticeKind.BountyCollected => 3,
			NoticeKind.SkillShot when notice.SkillShot?.Rarity == Rarity.Rare => 2,
			NoticeKind.WeaponLevelUp => 1,
			_ => 0,
		};

		return (kind, notice.Xp, notice.Value);
	}
}
