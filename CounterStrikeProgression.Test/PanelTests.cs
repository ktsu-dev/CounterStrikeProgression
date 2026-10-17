// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

[TestClass]
public sealed class PanelTests
{
	[TestMethod]
	public void TheOverviewShowsTheRankAndXp()
	{
		ProgressionEngine engine = TestHelpers.Engine(s =>
		{
			s.FirstRankXp = 1000;
			s.RankGrowth = 1.25;
		});
		PlayerProgress player = TestHelpers.Player();
		player.RankXp = 1500;
		player.Rank = 1;

		PanelPage page = new PanelBuilder(engine).Overview(player);

		Assert.AreEqual("Silver · II", page.Title);
		Assert.AreEqual("1,500 / 2,250 XP", page.ProgressLabel);
		Assert.AreEqual(500.0 / 1250, page.Progress, 1e-9);
	}

	[TestMethod]
	public void TheOverviewListsTheTiersClosestToDone()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		for (int i = 0; i < 20; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		}

		engine.ApplyCombat(player, TestHelpers.Kill("deagle"));

		PanelPage page = new PanelBuilder(engine).Overview(player);
		int heading = page.Lines.ToList().FindIndex(l => l.State == LineState.Heading);
		Assert.AreEqual("AK-47 Kills I", page.Lines[heading + 1].Label);
		Assert.AreEqual("20/25", page.Lines[heading + 1].Detail);
	}

	[TestMethod]
	public void WeaponPagesComeInOrderOfUse()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		engine.ApplyCombat(player, TestHelpers.Kill("awp"));
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		IReadOnlyList<WeaponInfo> order = PanelBuilder.WeaponOrder(player);

		Assert.AreEqual("awp", order[0].Key.WeakString);
		Assert.AreEqual("ak47", order[1].Key.WeakString);
		Assert.AreEqual("glock", order[2].Key.WeakString);
		Assert.HasCount(WeaponCatalog.All.Count, order);
		Assert.AreEqual(WeaponCatalog.All.Count + 1, PanelBuilder.PageCount);
	}

	[TestMethod]
	public void AWeaponPageShowsLevelAndChallenges()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("deagle"));

		PanelPage page = new PanelBuilder(engine).Weapon(player, TestHelpers.Weapon("deagle"));

		Assert.AreEqual("Desert Eagle", page.Title);
		Assert.AreEqual("Level 1", page.Subtitle);
		Assert.AreEqual("10 / 300 XP to level 2", page.ProgressLabel);
		PanelLine kills = page.Lines.Single(l => l.Label.StartsWith("Kills", StringComparison.Ordinal));
		Assert.AreEqual("Kills I", kills.Label);
		Assert.AreEqual("1/25  +100", kills.Detail);
		Assert.AreEqual(LineState.InProgress, kills.State);
		Assert.AreEqual(LineState.NotStarted, page.Lines.Single(l => l.Label.StartsWith("Jumpshot", StringComparison.Ordinal)).State);
	}

	[TestMethod]
	public void FinishedChallengesShowAsDone()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.KillTiers = [1, 2, 3, 4, 5]);
		PlayerProgress player = TestHelpers.Player();
		for (int i = 0; i < 5; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("deagle"));
		}

		PanelLine kills = new PanelBuilder(engine).Weapon(player, TestHelpers.Weapon("deagle")).Lines.Single(l => l.Label.StartsWith("Kills", StringComparison.Ordinal));
		Assert.AreEqual("Kills V", kills.Label);
		Assert.AreEqual(LineState.Done, kills.State);
	}

	[TestMethod]
	[DataRow(0.0, "[--------------------]")]
	[DataRow(0.5, "[##########----------]")]
	[DataRow(1.0, "[####################]")]
	[DataRow(2.0, "[####################]")]
	[DataRow(double.NaN, "[--------------------]")]
	public void BarsFillInProportion(double fraction, string expected) => Assert.AreEqual(expected, PanelFormatter.Bar(fraction));

	[TestMethod]
	public void ScrollingStaysInsideThePage()
	{
		PanelPage page = new("T", "S", 0, string.Empty, [.. Enumerable.Range(0, 10).Select(i => new PanelLine($"L{i}", string.Empty, LineState.InProgress))]);

		Assert.AreEqual(0, PanelFormatter.ClampScroll(page, -3, 4));
		Assert.AreEqual(6, PanelFormatter.ClampScroll(page, 50, 4));
		Assert.AreEqual("L6", PanelFormatter.Visible(page, 50, 4).First().Label);
		Assert.AreEqual(0, PanelFormatter.ClampScroll(page, 5, 20));
	}

	[TestMethod]
	public void PlainTextIncludesTitleBarAndControls()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PanelPage page = new PanelBuilder(engine).Overview(TestHelpers.Player());

		string text = PanelFormatter.ToPlainText(page, 1, PanelBuilder.PageCount, 0, 8);

		StringAssert.StartsWith(text, "SILVER · I");
		StringAssert.Contains(text, "[--------------------]");
		StringAssert.Contains(text, "A/D page");
	}

	[TestMethod]
	public void PlainTextStaysUnderTheWorldTextLimit()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress player = TestHelpers.Player();
		PanelBuilder builder = new(engine);

		for (int page = 0; page < PanelBuilder.PageCount; page++)
		{
			string text = PanelFormatter.ToPlainText(builder.Page(player, page), page + 1, PanelBuilder.PageCount, 0, 40, 511);
			Assert.IsLessThanOrEqualTo(511, System.Text.Encoding.UTF8.GetByteCount(text));
			StringAssert.Contains(text, "A/D page");
		}
	}

	[TestMethod]
	public void CenterHtmlEncodesPlayerText()
	{
		PanelPage page = new("<b>Rank</b>", "x", 0, "1 & 2", []);

		string html = PanelFormatter.ToCenterHtml(page, 1, 2, 0, 4);

		StringAssert.Contains(html, "&lt;B&gt;RANK&lt;/B&gt;");
		StringAssert.Contains(html, "1 &amp; 2");
	}

	[TestMethod]
	public void ChatLinesSkipHeadings()
	{
		PanelPage page = new("T", "S", 0, "x", [PanelLine.Heading("Volume"), new PanelLine("Kills I", "1/25", LineState.InProgress)]);

		IReadOnlyList<string> lines = PanelFormatter.ToChatLines(page);

		Assert.HasCount(2, lines);
		StringAssert.Contains(lines[1], "Kills I");
	}

	[TestMethod]
	[DataRow(1, "I")]
	[DataRow(4, "IV")]
	[DataRow(5, "V")]
	[DataRow(11, "11")]
	public void TiersUseRomanNumerals(int tier, string expected) => Assert.AreEqual(expected, PanelBuilder.Numeral(tier));
}
