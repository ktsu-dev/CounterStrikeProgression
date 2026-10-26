// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Players;

[TestClass]
public sealed class BotNameTests
{
	private static readonly Dictionary<string, string> Names = new()
	{
		["Muhlik"] = "Data",
		["Rock"] = "Vision",
		["Ava"] = "Ava",
		["Data"] = "Lore",
	};

	[TestMethod]
	public void RenameFor_GivesTheMappedName() =>
		Assert.AreEqual("Data", BotNames.RenameFor(Names, "Muhlik"));

	[TestMethod]
	public void RenameFor_IgnoresCase() =>
		Assert.AreEqual("Vision", BotNames.RenameFor(Names, "rock"));

	[TestMethod]
	public void RenameFor_LeavesAnUnmappedNameAlone() =>
		Assert.IsNull(BotNames.RenameFor(Names, "Crusher"));

	[TestMethod]
	public void RenameFor_LeavesAnUnchangedNameAlone() =>
		Assert.IsNull(BotNames.RenameFor(Names, "Ava"));

	[TestMethod]
	public void RenameFor_NeverRenamesANameItGave() =>
		Assert.IsNull(BotNames.RenameFor(Names, "Data"));

	[TestMethod]
	public void RenameFor_LeavesABlankNameAlone() =>
		Assert.IsNull(BotNames.RenameFor(Names, " "));

	[TestMethod]
	public void RenameFor_IgnoresABlankTarget() =>
		Assert.IsNull(BotNames.RenameFor(new Dictionary<string, string> { ["Rock"] = " " }, "Rock"));
}
