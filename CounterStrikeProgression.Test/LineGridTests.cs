// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Heatmaps;

[TestClass]
public sealed class LineGridTests
{
	private const ulong Player = 7;
	private const ulong Rival = 8;
	private const ulong Bystander = 9;

	private static readonly DateTime At = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

	private static readonly int[] BusiestThree = [5, 4, 3];

	private static MapOverview Overview => MapOverview.For("de_dust2")!;

	// On de_dust2 these world positions are just past radar pixels (104, 204) and (304, 404), away from any cell edge.
	private static KillPoint Kill(ulong killer, ulong victim, short killerX = -2018, short killerY = 2341, short victimX = -1138, short victimY = 1461) =>
		new("de_dust2", killer, victim, "ak47", false, killerX, killerY, 0, victimX, victimY, 0, At);

	// The world position at the centre of a de_dust2 cell's column or row.
	private static short WorldX(int column) => (short)Math.Round(-2476 + (((column * HeatmapGrid.CellPixels) + 4) * 4.4));

	private static short WorldY(int row) => (short)Math.Round(3239 - (((row * HeatmapGrid.CellPixels) + 4) * 4.4));

	[TestMethod]
	public void Build_SnapsBothEndsToCells()
	{
		IReadOnlyList<HeatLine> lines = LineGrid.Build([Kill(Rival, Bystander)], null, Overview);

		Assert.AreEqual(new HeatLine(104 / HeatmapGrid.CellPixels, 204 / HeatmapGrid.CellPixels, 304 / HeatmapGrid.CellPixels, 404 / HeatmapGrid.CellPixels, false, 1), Assert.ContainsSingle(lines));
	}

	[TestMethod]
	public void Build_MergesIdenticalSegments()
	{
		// Three units apart is under one cell, so both kills land in the same pair of cells.
		IReadOnlyList<HeatLine> lines = LineGrid.Build([Kill(Rival, Bystander), Kill(Bystander, Rival, killerX: -2015, victimY: 1458)], null, Overview);

		Assert.AreEqual(2, Assert.ContainsSingle(lines).Count);
	}

	[TestMethod]
	public void Build_KeepsDifferentSegmentsApart()
	{
		IReadOnlyList<HeatLine> lines = LineGrid.Build([Kill(Rival, Bystander), Kill(Rival, Bystander, victimX: -500)], null, Overview);

		Assert.HasCount(2, lines);
		Assert.IsTrue(lines.All(l => l.Count == 1));
	}

	[TestMethod]
	public void Build_MarksAPlayersDeaths()
	{
		IReadOnlyList<HeatLine> lines = LineGrid.Build([Kill(Player, Rival), Kill(Rival, Player, victimX: -500), Kill(Rival, Bystander, victimX: 0)], Player, Overview);

		Assert.HasCount(2, lines);
		Assert.IsFalse(lines.Single(l => l.X2 == 304 / HeatmapGrid.CellPixels).Death);
		Assert.IsTrue(lines.Single(l => l.X2 != 304 / HeatmapGrid.CellPixels).Death);
	}

	[TestMethod]
	public void Build_KeepsAKillAndADeathOnTheSameCellsApart()
	{
		IReadOnlyList<HeatLine> lines = LineGrid.Build([Kill(Player, Rival), Kill(Rival, Player)], Player, Overview);

		Assert.HasCount(2, lines);
		Assert.AreEqual(1, lines.Count(l => l.Death));
	}

	[TestMethod]
	public void Build_ServerWideNeverMarksADeath() =>
		Assert.IsFalse(LineGrid.Build([Kill(Player, Rival), Kill(Rival, Player)], null, Overview).Any(l => l.Death));

	[TestMethod]
	public void Build_LeavesOutSuicidesAndOffRadarEnds()
	{
		KillPoint[] kills =
		[
			Kill(Player, Player),
			Kill(Rival, Rival),
			Kill(Player, Rival, killerX: -30000, killerY: 30000),
			Kill(Rival, Player, victimX: 30000),
		];

		Assert.IsEmpty(LineGrid.Build(kills, null, Overview));
		Assert.IsEmpty(LineGrid.Build(kills, Player, Overview));
	}

	[TestMethod]
	public void Build_KeepsOnlyTheBusiestLinesPastTheCap()
	{
		// Five distinct segments, each 50 world units (over one cell) further along, used 1 to 5 times.
		List<KillPoint> kills = [];
		for (int segment = 0; segment < 5; segment++)
		{
			for (int use = 0; use <= segment; use++)
			{
				kills.Add(Kill(Rival, Bystander, victimX: (short)(-1138 + (segment * 50))));
			}
		}

		IReadOnlyList<HeatLine> lines = LineGrid.Build(kills, null, Overview, maxLines: 3);

		Assert.HasCount(3, lines);
		CollectionAssert.AreEqual(BusiestThree, lines.Select(l => l.Count).ToArray());
	}

	[TestMethod]
	public void Build_CapsAFullMapAtMaxLines()
	{
		// One distinct segment per kill: every victim cell on the grid from one killer cell, then from the next.
		List<KillPoint> kills = [];
		for (int i = 0; i < LineGrid.MaxLines + 100; i++)
		{
			int victim = i % (HeatmapGrid.Cells * HeatmapGrid.Cells);
			int killer = i / (HeatmapGrid.Cells * HeatmapGrid.Cells);
			kills.Add(Kill(
				Rival,
				Bystander,
				killerX: WorldX(killer),
				killerY: WorldY(0),
				victimX: WorldX(victim % HeatmapGrid.Cells),
				victimY: WorldY(victim / HeatmapGrid.Cells)));
		}

		Assert.HasCount(LineGrid.MaxLines, LineGrid.Build(kills, null, Overview));
	}
}
