// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Heatmaps;

[TestClass]
public sealed class FightGridTests
{
	private const ulong Player = 7;
	private const ulong Rival = 8;
	private const ulong Bystander = 9;

	private static readonly DateTime At = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

	private static MapOverview Overview => MapOverview.For("de_dust2")!;

	private static KillPoint Kill(ulong killer, ulong victim, short killerX = 0, short killerY = 0, short victimX = 400, short victimY = 400) =>
		new("de_dust2", killer, victim, "ak47", false, killerX, killerY, 0, victimX, victimY, 0, At);

	private static (int X, int Y) CellHolding(short x, short y)
	{
		(double px, double py) = Overview.ToPixel(new HeatPoint(x, y));
		return ((int)(px / FightGrid.CellPixels), (int)(py / FightGrid.CellPixels));
	}

	[TestMethod]
	public void Build_CountsAKillAsWonAtTheKillersSpot()
	{
		KillPoint kill = Kill(Player, Rival);

		IReadOnlyList<FightCell> cells = FightGrid.Build([kill], Player, Overview);

		(int x, int y) = CellHolding(kill.KillerX, kill.KillerY);
		Assert.AreEqual(new FightCell(x, y, 1, 0), Assert.ContainsSingle(cells));
	}

	[TestMethod]
	public void Build_CountsADeathAsLostAtTheVictimsSpot()
	{
		KillPoint kill = Kill(0, Player);

		IReadOnlyList<FightCell> cells = FightGrid.Build([kill], Player, Overview);

		(int x, int y) = CellHolding(kill.VictimX, kill.VictimY);
		Assert.AreEqual(new FightCell(x, y, 0, 1), Assert.ContainsSingle(cells));
	}

	[TestMethod]
	public void Build_IgnoresOtherPlayersFights() =>
		Assert.IsEmpty(FightGrid.Build([Kill(Rival, Bystander)], Player, Overview));

	[TestMethod]
	public void Build_IgnoresSuicides() =>
		Assert.IsEmpty(FightGrid.Build([Kill(Player, Player)], Player, Overview));

	[TestMethod]
	public void Build_LeavesOutPointsOffTheRadar() =>
		Assert.IsEmpty(FightGrid.Build([Kill(Player, Rival, killerX: -30000, killerY: 30000)], Player, Overview));

	[TestMethod]
	public void Build_AddsUpFightsInOneCell()
	{
		IReadOnlyList<FightCell> cells = FightGrid.Build(
			[Kill(Player, Rival, 0, 0), Kill(Player, Bystander, 0, 0), Kill(Rival, Player, victimX: 0, victimY: 0)],
			Player,
			Overview);

		(int x, int y) = CellHolding(0, 0);
		Assert.AreEqual(new FightCell(x, y, 2, 1), Assert.ContainsSingle(cells));
	}

	[TestMethod]
	public void CellOf_MatchesBuildForOneFight()
	{
		KillPoint won = Kill(Player, Rival);
		KillPoint lost = Kill(Rival, Player);

		Assert.AreEqual(FightGrid.Build([won], Player, Overview).Single(), FightGrid.CellOf(won, Player, Overview));
		Assert.AreEqual(FightGrid.Build([lost], Player, Overview).Single(), FightGrid.CellOf(lost, Player, Overview));
	}
}
