// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Storage;

[TestClass]
public sealed class LiveTests
{
	private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private static ProgressionResult ResultWith(params ProgressionNotice[] notices)
	{
		ProgressionResult result = new();
		foreach (ProgressionNotice notice in notices)
		{
			result.Notices.Add(notice);
		}

		return result;
	}

	private static PlayerProgress PlayerWithWeapon()
	{
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 100;
		ak.Challenge("kills").Count = 5;
		WeaponProgress awp = player.Weapon(TestHelpers.Weapon("awp").Key);
		awp.Xp = 50;
		return player;
	}

	[TestMethod]
	public void FeedBuilder_SkillShotNotice_MakesSkillShotEntry()
	{
		SkillShotDefinition shot = SkillShotCatalog.All[0];
		ProgressionResult result = ResultWith(new ProgressionNotice
		{
			Kind = NoticeKind.SkillShot,
			SkillShot = shot,
			Weapon = TestHelpers.Weapon("awp"),
			Xp = 40,
		});

		IReadOnlyList<FeedEntry> feed = FeedBuilder.From(result, TestHelpers.SteamId, "Tester", Now);

		Assert.HasCount(1, feed);
		Assert.AreEqual(FeedKind.SkillShot, feed[0].Kind);
		Assert.AreEqual(shot.Name, feed[0].Title);
		Assert.AreEqual("AWP", feed[0].Weapon);
		Assert.AreEqual(40, feed[0].Xp);
		Assert.AreEqual(TestHelpers.SteamId, feed[0].SteamId);
		Assert.AreEqual("Tester", feed[0].PlayerName);
		Assert.AreEqual(Now, feed[0].At);
	}

	[TestMethod]
	public void FeedBuilder_RankUp_TitleNamesTheRank()
	{
		ProgressionResult result = ResultWith(new ProgressionNotice { Kind = NoticeKind.RankUp, Value = 5 });

		IReadOnlyList<FeedEntry> feed = FeedBuilder.From(result, TestHelpers.SteamId, "Tester", Now);

		Assert.HasCount(1, feed);
		Assert.AreEqual(FeedKind.RankUp, feed[0].Kind);
		Assert.AreEqual("Rank up: " + RankLadder.Names[5], feed[0].Title);
	}

	[TestMethod]
	public void FeedBuilder_WeaponLevelUpAndTier_AreIncluded()
	{
		ProgressionResult result = ResultWith(
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("awp"), Value = 7 },
			new ProgressionNotice { Kind = NoticeKind.TierCompleted, ChallengeName = "Headhunter", Weapon = TestHelpers.Weapon("awp"), Value = 3 });

		IReadOnlyList<FeedEntry> feed = FeedBuilder.From(result, TestHelpers.SteamId, "Tester", Now);

		Assert.HasCount(2, feed);
		Assert.AreEqual(FeedKind.WeaponLevelUp, feed[0].Kind);
		Assert.AreEqual("AWP level 7", feed[0].Title);
		Assert.AreEqual(FeedKind.TierCompleted, feed[1].Kind);
		Assert.AreEqual("Headhunter " + PanelBuilder.Numeral(3), feed[1].Title);
	}

	[TestMethod]
	public void FeedBuilder_NoNotices_GivesNothing()
	{
		IReadOnlyList<FeedEntry> feed = FeedBuilder.From(new ProgressionResult(), TestHelpers.SteamId, "Tester", Now);

		Assert.IsEmpty(feed);
	}

	[TestMethod]
	public void GameModes_KnownPairs()
	{
		Assert.AreEqual("Deathmatch", GameModes.Name(1, 2));
		Assert.AreEqual("Competitive", GameModes.Name(0, 1));
		Assert.AreEqual("Casual", GameModes.Name(0, 0));
		Assert.AreEqual("Wingman", GameModes.Name(0, 2));
		Assert.AreEqual("Arms Race", GameModes.Name(1, 0));
		Assert.AreEqual("Custom", GameModes.Name(9, 9));
	}

	[TestMethod]
	public void ProgressChangeTracker_FirstCall_ReturnsEverything()
	{
		ProgressChangeTracker tracker = new();
		PlayerProgress player = PlayerWithWeapon();

		ProgressSnapshot? changes = tracker.Changes(player);

		Assert.IsNotNull(changes);
		Assert.IsNotNull(changes.Player);
		Assert.HasCount(2, changes.Weapons);
		Assert.HasCount(1, changes.Challenges);
	}

	[TestMethod]
	public void ProgressChangeTracker_NoChange_ReturnsNull()
	{
		ProgressChangeTracker tracker = new();
		PlayerProgress player = PlayerWithWeapon();
		tracker.Changes(player);

		player.LastSeen = player.LastSeen.AddMinutes(5);

		Assert.IsNull(tracker.Changes(player));
	}

	[TestMethod]
	public void ProgressChangeTracker_OneWeaponChanged_ReturnsOnlyThatRow()
	{
		ProgressChangeTracker tracker = new();
		PlayerProgress player = PlayerWithWeapon();
		tracker.Changes(player);

		player.Weapon(TestHelpers.Weapon("awp").Key).Xp = 75;
		ProgressSnapshot? changes = tracker.Changes(player);

		Assert.IsNotNull(changes);
		Assert.IsNull(changes.Player);
		Assert.HasCount(1, changes.Weapons);
		Assert.AreEqual(TestHelpers.Weapon("awp").Key.WeakString, changes.Weapons[0].Weapon);
		Assert.AreEqual(75, changes.Weapons[0].Xp);
		Assert.IsEmpty(changes.Challenges);
	}

	[TestMethod]
	public void ProgressChangeTracker_Reset_ReturnsEverythingAgain()
	{
		ProgressChangeTracker tracker = new();
		PlayerProgress player = PlayerWithWeapon();
		tracker.Changes(player);

		tracker.Reset();
		ProgressSnapshot? changes = tracker.Changes(player);

		Assert.IsNotNull(changes);
		Assert.IsNotNull(changes.Player);
		Assert.HasCount(2, changes.Weapons);
	}

	[TestMethod]
	public void ProgressChangeTracker_Forget_ReturnsEverythingForThatPlayer()
	{
		ProgressChangeTracker tracker = new();
		PlayerProgress player = PlayerWithWeapon();
		PlayerProgress other = new(TestHelpers.SteamId + 1) { Name = "Other" };
		tracker.Changes(player);
		tracker.Changes(other);

		tracker.Forget(player.SteamId);

		Assert.IsNotNull(tracker.Changes(player));
		Assert.IsNull(tracker.Changes(other));
	}

	[TestMethod]
	public void ProgressSnapshot_CaptureAll_LeavesDirtyFlagsAlone()
	{
		PlayerProgress player = PlayerWithWeapon();
		player.IsDirty = true;
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.IsDirty = true;

		ProgressSnapshot snapshot = ProgressSnapshot.CaptureAll(player);

		Assert.IsTrue(player.IsDirty);
		Assert.IsTrue(ak.IsDirty);
		Assert.IsNotNull(snapshot.Player);
		Assert.HasCount(2, snapshot.Weapons);
		Assert.HasCount(1, snapshot.Challenges);
	}

	[TestMethod]
	public void LiveJson_RoundTripsABatch()
	{
		LiveBatch batch = new(
			new ServerSnapshot(Now, "de_dust2", "Deathmatch", 16, [new LivePlayer(3, TestHelpers.SteamId, "Tester", false, 4, 10, 2, 123.5)]),
			[new FeedEntry(Now, TestHelpers.SteamId, "Tester", FeedKind.SkillShot, "Marksman", "AWP", 40)],
			[ProgressSnapshot.CaptureAll(PlayerWithWeapon())],
			new ProgressionSettings());

		string json = JsonSerializer.Serialize(batch, LiveJson.Options);
		LiveBatch? back = JsonSerializer.Deserialize<LiveBatch>(json, LiveJson.Options);

		Assert.Contains("\"SkillShot\"", json);
		Assert.IsNotNull(back);
		Assert.AreEqual("de_dust2", back.Snapshot.Map);
		Assert.AreEqual(16, back.Snapshot.MaxPlayers);
		Assert.HasCount(1, back.Snapshot.Players);
		Assert.AreEqual(123.5, back.Snapshot.Players[0].MapXp);
		Assert.HasCount(1, back.Feed);
		Assert.AreEqual(FeedKind.SkillShot, back.Feed[0].Kind);
		Assert.AreEqual("AWP", back.Feed[0].Weapon);
		Assert.AreEqual(Now, back.Feed[0].At);
		Assert.HasCount(1, back.Progress);
		Assert.HasCount(2, back.Progress[0].Weapons);
		Assert.AreEqual(batch.Settings.KillXp, back.Settings.KillXp);
	}
}
