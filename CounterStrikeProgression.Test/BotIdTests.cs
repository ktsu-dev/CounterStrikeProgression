// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Players;

[TestClass]
public sealed class BotIdTests
{
	private const ulong Human = 76561197960265729;

	[TestMethod]
	public void For_IsStableForAName()
	{
		Assert.AreEqual(10000000062568863UL, BotIds.For("Muhlik"));
		Assert.AreEqual(10000000063859252UL, BotIds.For("Rock"));
	}

	[TestMethod]
	public void For_TrimsButKeepsCase()
	{
		Assert.AreEqual(10000000062568863UL, BotIds.For(" Muhlik "));
		Assert.AreEqual(10000000075765951UL, BotIds.For("muhlik"));
	}

	[TestMethod]
	public void For_RejectsABlankName() =>
		Assert.ThrowsExactly<ArgumentException>(() => BotIds.For("  "));

	[TestMethod]
	public void IsBot_MatchesTheRangeOnly()
	{
		Assert.IsTrue(BotIds.IsBot(10000000000000000UL));
		Assert.IsTrue(BotIds.IsBot(10000000099999999UL));
		Assert.IsFalse(BotIds.IsBot(9999999999999999UL));
		Assert.IsFalse(BotIds.IsBot(10000000100000000UL));
		Assert.IsFalse(BotIds.IsBot(Human));
		Assert.IsFalse(BotIds.IsBot(0));
	}

	[TestMethod]
	[DataRow("Muhlik")]
	[DataRow("Rock")]
	[DataRow("Zed")]
	public void IsBot_IsTrueForEveryNamedId(string name)
	{
		ulong id = BotIds.For(name);

		Assert.IsTrue(BotIds.IsBot(id));
		Assert.HasCount(17, id.ToString(CultureInfo.InvariantCulture));
	}

	[TestMethod]
	public void PlayerIds_KeysAHumanBySteamIdWhateverTheirName() =>
		Assert.AreEqual(Human, PlayerIds.Of(false, Human, "Muhlik"));

	[TestMethod]
	public void PlayerIds_KeysABotByName()
	{
		Assert.AreEqual(10000000062568863UL, PlayerIds.Of(true, 0, "Muhlik"));
		Assert.AreEqual(0UL, PlayerIds.Of(true, 0, ""));
	}

	[TestMethod]
	public void BotTracking_StopsWithoutAHuman()
	{
		Assert.IsFalse(BotTracking.MayStart(BotIds.For("Muhlik"), humansOnline: false, []));
		Assert.IsTrue(BotTracking.MayStart(BotIds.For("Muhlik"), humansOnline: true, []));
	}

	[TestMethod]
	public void BotTracking_RefusesASecondSessionForAnIdInUse() =>
		Assert.IsFalse(BotTracking.MayStart(BotIds.For("Muhlik"), true, [BotIds.For("Muhlik")]));

	[TestMethod]
	public void BotTracking_StartsAHumanWithNoOneElseOn()
	{
		Assert.IsTrue(BotTracking.MayStart(Human, false, []));
		Assert.IsFalse(BotTracking.MayStart(0, true, []));
	}
}
