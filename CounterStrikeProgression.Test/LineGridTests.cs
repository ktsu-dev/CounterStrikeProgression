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

	private static MapOverview Overview => MapOverview.For("de_dust2")!;

	// On de_dust2 these world positions are just past radar pixels (104, 204) and (304, 404), away from any cell edge.
	private static KillPoint Kill(ulong killer, ulong victim, short killerX = -2018, short killerY = 2341, short victimX = -1138, short victimY = 1461) =>
		new("de_dust2", killer, victim, "ak47", false, killerX, killerY, 0, victimX, victimY, 0, At);

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
}
