// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class ContractTests
{
	private static readonly DateOnly Saturday = new(2026, 10, 17);

	public TestContext TestContext { get; set; } = null!;

	// Every daily group runs, so the class kills contract is always there to test against.
	private static ProgressionEngine Engine(ManualTime time) =>
		TestHelpers.Engine(s =>
		{
			s.ContractsEnabled = true;
			s.DailyContracts = ContractCatalog.DailyGroups.Count;
		}, time);

	private static ActiveContract ClassKills(ProgressionEngine engine) =>
		engine.ActiveContracts.Single(c => c.Definition.Id.StartsWith("kills_", StringComparison.Ordinal));

	// A gun of the contract's class, picked so the kills don't also count toward the knife and grenade contract.
	private static string WeaponFor(ActiveContract contract) =>
		WeaponCatalog.All.First(w => w.IsGun && contract.Definition.Counts(new ContractSignal(SignalKind.Kill, w))).Key.WeakString;

	private static void Kill(ProgressionEngine engine, PlayerProgress player, string weapon, int times, List<ProgressionNotice>? notices = null)
	{
		for (int i = 0; i < times; i++)
		{
			ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill(weapon));
			notices?.AddRange(result.Notices);
		}
	}

	[TestMethod]
	public void ADayHasThreeDailyContractsAndOneWeekly()
	{
		IReadOnlyList<ActiveContract> active = ContractCatalog.Active(Saturday, new ProgressionSettings());

		Assert.HasCount(4, active);
		Assert.HasCount(3, active.Where(c => c.Definition.Period == ContractPeriod.Daily));
		Assert.AreEqual(ContractPeriod.Weekly, active[^1].Definition.Period);
		Assert.AreEqual("2026-10-17", active[0].PeriodKey);
		Assert.AreEqual("2026-W42", active[^1].PeriodKey);
		Assert.AreEqual(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), active[0].EndsAt);
		Assert.AreEqual(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), active[^1].EndsAt);
		Assert.AreEqual(500, active[0].Xp);
		Assert.AreEqual(2500, active[^1].Xp);
	}

	[TestMethod]
	public void ThePickIsTheSameEveryTimeAndChangesDayToDay()
	{
		ProgressionSettings settings = new();
		string[] first = [.. ContractCatalog.Active(Saturday, settings).Select(c => c.Definition.Id)];
		string[] again = [.. ContractCatalog.Active(Saturday, settings).Select(c => c.Definition.Id)];
		CollectionAssert.AreEqual(first, again);

		HashSet<string> days = [.. Enumerable.Range(0, 14).Select(d => string.Join(",", ContractCatalog.Active(Saturday.AddDays(d), settings).Take(3).Select(c => c.Definition.Id)))];
		Assert.IsGreaterThan(1, days.Count);
	}

	[TestMethod]
	public void ADayNeverDrawsTwoContractsFromOneGroup()
	{
		ProgressionSettings settings = new();
		for (int d = 0; d < 60; d++)
		{
			IReadOnlyList<ActiveContract> daily = [.. ContractCatalog.Active(Saturday.AddDays(d), settings).Where(c => c.Definition.Period == ContractPeriod.Daily)];
			int groups = daily.Select(c => ContractCatalog.DailyGroups.ToList().FindIndex(g => g.Any(v => v.Id == c.Definition.Id))).Distinct().Count();
			Assert.AreEqual(daily.Count, groups);
		}
	}

	[TestMethod]
	public void ContractIdsAreUnique()
	{
		string[] ids = [.. ContractCatalog.DailyGroups.SelectMany(g => g).Concat(ContractCatalog.Weekly).Select(c => c.Id)];
		Assert.HasCount(ids.Length, ids.Distinct(StringComparer.Ordinal));
		Assert.IsTrue(ids.All(id => ContractCatalog.Find(id) is not null));
	}

	[TestMethod]
	public void TurnedOffThereAreNoContracts()
	{
		Assert.IsEmpty(ContractCatalog.Active(Saturday, new ProgressionSettings { ContractsEnabled = false }));
	}

	[TestMethod]
	public void FinishingAContractPaysItsXpOnce()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract contract = ClassKills(engine);
		string weapon = WeaponFor(contract);

		List<ProgressionNotice> notices = [];
		Kill(engine, player, weapon, contract.Definition.Target - 1, notices);
		Assert.IsFalse(notices.Any(n => n.Kind == NoticeKind.ContractCompleted && n.Contract == contract));
		Assert.AreEqual(contract.Definition.Target - 1, player.Contracts[(contract.PeriodKey, contract.Definition.Id)].Count);

		long before = player.RankXp;
		ProgressionResult finishing = engine.ApplyCombat(player, TestHelpers.Kill(weapon));
		ProgressionNotice done = finishing.Notices.Single(n => n.Kind == NoticeKind.ContractCompleted && n.Contract == contract);
		Assert.AreEqual(contract.Xp, done.Xp);
		Assert.IsGreaterThanOrEqualTo(before + contract.Xp, player.RankXp);
		Assert.IsTrue(player.Contracts[(contract.PeriodKey, contract.Definition.Id)].Completed);

		notices.Clear();
		Kill(engine, player, weapon, 5, notices);
		Assert.IsFalse(notices.Any(n => n.Kind == NoticeKind.ContractCompleted && n.Contract == contract));
	}

	[TestMethod]
	public void KillsWithOtherClassesDoNotCount()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract contract = ClassKills(engine);
		string other = WeaponCatalog.All.First(w => w.IsGun && !contract.Definition.Counts(new ContractSignal(SignalKind.Kill, w))).Key.WeakString;

		Kill(engine, player, other, 3);

		Assert.IsFalse(player.Contracts.ContainsKey((contract.PeriodKey, contract.Definition.Id)));
	}

	[TestMethod]
	public void ANewDayStartsFreshAndDropsYesterdayOnceSaved()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract yesterday = ClassKills(engine);
		Kill(engine, player, WeaponFor(yesterday), 2);
		_ = ProgressSnapshot.Capture(player);

		time.Advance(TimeSpan.FromDays(1));
		ActiveContract today = ClassKills(engine);
		Kill(engine, player, WeaponFor(today), 1);

		Assert.IsFalse(player.Contracts.ContainsKey((yesterday.PeriodKey, yesterday.Definition.Id)));
		Assert.AreEqual(1, player.Contracts[(today.PeriodKey, today.Definition.Id)].Count);
	}

	[TestMethod]
	public void AnUnsavedRowFromYesterdayIsKeptUntilItSaves()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract yesterday = ClassKills(engine);
		Kill(engine, player, WeaponFor(yesterday), 2);

		time.Advance(TimeSpan.FromDays(1));
		Kill(engine, player, WeaponFor(ClassKills(engine)), 1);

		ProgressSnapshot snapshot = ProgressSnapshot.Capture(player);
		Assert.IsTrue(snapshot.Contracts.Any(c => c.Period == yesterday.PeriodKey && c.Contract == yesterday.Definition.Id && c.Count == 2));
	}

	[TestMethod]
	public async Task ContractRowsSaveAndLoad()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract contract = ClassKills(engine);
		Kill(engine, player, WeaponFor(contract), contract.Definition.Target);

		InMemoryProgressionStore store = new();
		ProgressSnapshot snapshot = ProgressSnapshot.Capture(player);
		Assert.IsNotEmpty(snapshot.Contracts);
		await store.SaveAsync(snapshot, TestContext.CancellationToken).ConfigureAwait(false);

		PlayerProgress? loaded = await store.LoadAsync(TestHelpers.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsNotNull(loaded);
		ContractProgress row = loaded.Contracts[(contract.PeriodKey, contract.Definition.Id)];
		Assert.AreEqual(contract.Definition.Target, row.Count);
		Assert.IsTrue(row.Completed);
		Assert.IsFalse(row.IsDirty);
	}

	[TestMethod]
	public void RestoreFlagsContractRowsAgain()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		Kill(engine, player, WeaponFor(ClassKills(engine)), 1);

		ProgressSnapshot failed = ProgressSnapshot.Capture(player);
		failed.Restore(player);

		CollectionAssert.AreEquivalent(failed.Contracts.ToList(), ProgressSnapshot.Capture(player).Contracts.ToList());
	}

	[TestMethod]
	public void TheChangeTrackerSendsOnlyChangedContracts()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		string weapon = WeaponFor(ClassKills(engine));
		Kill(engine, player, weapon, 1);

		ProgressChangeTracker tracker = new();
		Assert.IsNotEmpty(tracker.Changes(player)!.Contracts);
		Assert.IsNull(tracker.Changes(player));

		Kill(engine, player, weapon, 1);
		Assert.IsNotEmpty(tracker.Changes(player)!.Contracts);
	}

	[TestMethod]
	public void TheFeedNamesTheContract()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract contract = ClassKills(engine);
		Kill(engine, player, WeaponFor(contract), contract.Definition.Target - 1);

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill(WeaponFor(contract)));
		IReadOnlyList<FeedEntry> feed = FeedBuilder.From(result, player.SteamId, player.Name, DateTime.UtcNow);

		FeedEntry entry = feed.Single(e => e.Kind == FeedKind.ContractCompleted && e.Title.EndsWith(contract.Definition.Description, StringComparison.Ordinal));
		Assert.AreEqual($"Daily contract: {contract.Definition.Description}", entry.Title);
		Assert.AreEqual(contract.Xp, entry.Xp);
	}

	[TestMethod]
	public void ThePanelListsTodayThenTheWeek()
	{
		ManualTime time = new();
		ProgressionEngine engine = TestHelpers.Engine(s => s.ContractsEnabled = true, time);
		PlayerProgress player = TestHelpers.Player();

		PanelPage page = new PanelBuilder(engine).Page(player, PanelBuilder.ContractsPage);

		Assert.AreEqual("Contracts", page.Title);
		Assert.AreEqual("0 of 4 done", page.ProgressLabel);
		Assert.AreEqual("Today, 12h 0m left", page.Lines[0].Label);
		Assert.AreEqual(LineState.Heading, page.Lines[4].State);
		Assert.AreEqual("This week, 1d 12h left", page.Lines[4].Label);
		Assert.AreEqual(LineState.NotStarted, page.Lines[1].State);
		StringAssert.EndsWith(page.Lines[1].Detail, "+500");
		StringAssert.EndsWith(page.Lines[5].Detail, "+2,500");
	}

	[TestMethod]
	public void ThePanelMarksFinishedContractsDone()
	{
		ManualTime time = new();
		ProgressionEngine engine = Engine(time);
		PlayerProgress player = TestHelpers.Player();
		ActiveContract contract = ClassKills(engine);
		Kill(engine, player, WeaponFor(contract), contract.Definition.Target);

		PanelPage page = new PanelBuilder(engine).Contracts(player);

		PanelLine line = page.Lines.Single(l => l.Label == contract.Definition.Description);
		Assert.AreEqual(LineState.Done, line.State);
		Assert.AreEqual("Done", line.Detail);
		StringAssert.Contains(new PanelBuilder(engine).Overview(player).Lines.Single(l => l.Label == "Contracts").Detail, "done");
	}

	[TestMethod]
	public void ContractsPageFitsTheWorldTextLimit()
	{
		ManualTime time = new();
		ProgressionEngine engine = TestHelpers.Engine(s => s.ContractsEnabled = true, time);
		for (int d = 0; d < 28; d++)
		{
			string text = PanelFormatter.ToPlainText(new PanelBuilder(engine).Contracts(TestHelpers.Player()), 2, PanelBuilder.PageCount, 0, 40, 511);
			Assert.IsLessThanOrEqualTo(511, System.Text.Encoding.UTF8.GetByteCount(text));
			StringAssert.Contains(text, "A/D page");
			time.Advance(TimeSpan.FromDays(1));
		}
	}

	[TestMethod]
	[DataRow(0, 0, 5, "0h 5m")]
	[DataRow(0, 13, 0, "13h 0m")]
	[DataRow(3, 4, 59, "3d 4h")]
	[DataRow(-1, 0, 0, "0h 0m")]
	public void RemainingTimeIsShort(int days, int hours, int minutes, string expected) =>
		Assert.AreEqual(expected, PanelBuilder.Remaining(new TimeSpan(days, hours, minutes, 0)));

	[TestMethod]
	public void ThePlayerPageShowsTheRunningContracts()
	{
		ProgressionSettings settings = new();
		DateTime now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
		ActiveContract first = ContractCatalog.Active(Saturday, settings)[0];
		PlayerProgress player = TestHelpers.Player();
		player.Contract(first.PeriodKey, first.Definition.Id).Count = first.Definition.Target + 3;
		player.Contract(first.PeriodKey, first.Definition.Id).Completed = true;
		player.Contract("2026-10-16", first.Definition.Id).Count = 1;

		PlayerView view = PlayerViewBuilder.Build(player, settings, online: false, now: now);

		Assert.HasCount(4, view.Contracts);
		ContractView shown = view.Contracts[0];
		Assert.AreEqual(first.Definition.Id, shown.Id);
		Assert.IsTrue(shown.Completed);
		Assert.AreEqual(first.Definition.Target, shown.Count);
		Assert.AreEqual(ContractPeriod.Weekly, view.Contracts[^1].Period);
		Assert.IsTrue(view.Contracts.Skip(1).All(c => c.Count == 0 && !c.Completed));
	}

	[TestMethod]
	public void MergingKeepsTheLargerContractCount()
	{
		ProgressSnapshot older = new(null, [], []) { Contracts = [new ContractRow(TestHelpers.SteamId, "2026-10-17", "headshots", 4, false)] };
		ProgressSnapshot newer = new(null, [], []) { Contracts = [new ContractRow(TestHelpers.SteamId, "2026-10-17", "headshots", 2, true)] };

		ContractRow combined = ProgressMerge.Combine(older, newer).Contracts.Single();
		Assert.AreEqual(4, combined.Count);
		Assert.IsTrue(combined.Completed);
		Assert.AreEqual(TestHelpers.SteamId, ProgressMerge.SteamIdOf(newer));

		PlayerProgress stored = TestHelpers.Player();
		stored.Contract("2026-10-17", "headshots").Count = 9;
		PlayerProgress merged = ProgressMerge.Merge(stored, newer);
		Assert.AreEqual(9, merged.Contracts[("2026-10-17", "headshots")].Count);
		Assert.IsTrue(merged.Contracts[("2026-10-17", "headshots")].Completed);
	}
}
