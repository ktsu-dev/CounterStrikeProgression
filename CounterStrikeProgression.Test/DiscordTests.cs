// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Net;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core.Discord;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

[TestClass]
public sealed class DiscordTests
{
	private static readonly Uri Webhook = new("https://discord.test/api/webhooks/1/token");
	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
	private const int GoldNova = 3 * RankLadder.StagesPerRank;

	private sealed class StubHandler(Func<string, int, HttpResponseMessage> respond) : HttpMessageHandler
	{
		private int calls;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return respond(body, Interlocked.Increment(ref calls));
		}
	}

	private static ProgressionResult ResultWith(params ProgressionNotice[] notices)
	{
		ProgressionResult result = new();
		foreach (ProgressionNotice notice in notices)
		{
			result.Notices.Add(notice);
		}

		return result;
	}

	private static SkillShotDefinition Shot(Rarity rarity) => SkillShotCatalog.All.First(s => s.Rarity == rarity);

	private static ProgressionNotice SkillShot(Rarity rarity) =>
		new() { Kind = NoticeKind.SkillShot, SkillShot = Shot(rarity), Weapon = TestHelpers.Weapon("ak47"), Xp = 1500 };

	private static ProgressionNotice RankUp(int rank) => new() { Kind = NoticeKind.RankUp, Value = rank };

	[TestMethod]
	public void Messages_PostFeatsEpicSkillShotsAndHighRankUps()
	{
		FeatDefinition feat = FeatCatalog.All[0];
		ProgressionResult result = ResultWith(
			new ProgressionNotice { Kind = NoticeKind.FeatEarned, Feat = feat },
			SkillShot(Rarity.Epic),
			RankUp(GoldNova));

		IReadOnlyList<string> lines = DiscordMessages.From(result, "Tester", new DiscordSettings());

		Assert.HasCount(3, lines);
		Assert.AreEqual($"🏅 **Tester** earned the feat **{feat.Name}**: {feat.Description}!", lines[0]);
		Assert.AreEqual($"💥 **Tester** landed **{Shot(Rarity.Epic).Name}** with the AK-47 (+1,500 XP)", lines[1]);
		Assert.AreEqual($"⬆️ **Tester** ranked up to **{RankLadder.Names[GoldNova]}**!", lines[2]);
	}

	[TestMethod]
	public void Messages_SkipLesserMoments()
	{
		ProgressionResult result = ResultWith(
			SkillShot(Rarity.Common),
			SkillShot(Rarity.Rare),
			RankUp(GoldNova - 1),
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("ak47"), Value = 40, Broadcast = true },
			new ProgressionNotice { Kind = NoticeKind.TierCompleted, Weapon = TestHelpers.Weapon("ak47"), ChallengeName = "Kills", Value = 5 });

		Assert.IsEmpty(DiscordMessages.From(result, "Tester", new DiscordSettings()));
	}

	[TestMethod]
	public void Messages_RespectTheToggles()
	{
		ProgressionResult result = ResultWith(
			new ProgressionNotice { Kind = NoticeKind.FeatEarned, Feat = FeatCatalog.All[0] },
			SkillShot(Rarity.Epic),
			RankUp(RankLadder.TopRank));
		DiscordSettings settings = new() { Feats = false, EpicSkillShots = false, RankUps = false };

		Assert.IsEmpty(DiscordMessages.From(result, "Tester", settings));
	}

	[TestMethod]
	public void Messages_EscapeMarkdownInNames()
	{
		IReadOnlyList<string> lines = DiscordMessages.From(ResultWith(RankUp(RankLadder.TopRank)), "*bold*_x_\n@everyone", new DiscordSettings());

		Assert.HasCount(1, lines);
		Assert.StartsWith(@"⬆️ **\*bold\*\_x\_ @everyone**", lines[0]);
	}

	[TestMethod]
	public void MinimumRank_TakesARankOrAStage()
	{
		Assert.AreEqual(GoldNova, DiscordMessages.MinimumRankIndex("Gold Nova"));
		Assert.AreEqual(GoldNova + 2, DiscordMessages.MinimumRankIndex("gold nova · iii"));
		Assert.AreEqual(0, DiscordMessages.MinimumRankIndex("Silver"));
		Assert.AreEqual(RankLadder.TopRank - 4, DiscordMessages.MinimumRankIndex(" Global Elite "));
	}

	[TestMethod]
	public void MinimumRank_UnknownNameFallsBackToGoldNova()
	{
		Assert.AreEqual(GoldNova, DiscordMessages.MinimumRankIndex("Diamond"));
		Assert.AreEqual(GoldNova, DiscordMessages.MinimumRankIndex(null));
	}

	[TestMethod]
	public void ParseUrl_AcceptsOnlyHttps()
	{
		Assert.AreEqual(Webhook, DiscordSender.ParseUrl(" https://discord.test/api/webhooks/1/token "));
		Assert.IsNull(DiscordSender.ParseUrl("http://discord.test/api/webhooks/1/token"));
		Assert.IsNull(DiscordSender.ParseUrl("discord.test/api/webhooks"));
		Assert.IsNull(DiscordSender.ParseUrl(""));
	}

	[TestMethod]
	public async Task Sender_PostsContentWithNoMentions()
	{
		TaskCompletionSource<string> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new((body, _) =>
		{
			seen.TrySetResult(body);
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		});
		using DiscordSender sender = new(Webhook, "CS2", 8, TimeSpan.FromSeconds(5), TimeSpan.Zero, _ => { }, handler);

		Assert.IsTrue(sender.TryEnqueue("hello"));

		using JsonDocument body = JsonDocument.Parse(await seen.Task.WaitAsync(Generous).ConfigureAwait(false));
		Assert.AreEqual("hello", body.RootElement.GetProperty("content").GetString());
		Assert.AreEqual("CS2", body.RootElement.GetProperty("username").GetString());
		Assert.AreEqual(0, body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
	}

	[TestMethod]
	public async Task Sender_LeavesOutABlankUsername()
	{
		TaskCompletionSource<string> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new((body, _) =>
		{
			seen.TrySetResult(body);
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		});
		using DiscordSender sender = new(Webhook, " ", 8, TimeSpan.FromSeconds(5), TimeSpan.Zero, _ => { }, handler);

		sender.TryEnqueue("hello");

		using JsonDocument body = JsonDocument.Parse(await seen.Task.WaitAsync(Generous).ConfigureAwait(false));
		Assert.IsFalse(body.RootElement.TryGetProperty("username", out _));
	}

	[TestMethod]
	public async Task Sender_JoinsWaitingLinesIntoOnePost()
	{
		TaskCompletionSource firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<string> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using ManualResetEventSlim release = new();
		using StubHandler handler = new((body, call) =>
		{
			if (call == 1)
			{
				firstArrived.TrySetResult();
				release.Wait(Generous);
			}
			else
			{
				second.TrySetResult(body);
			}

			return new HttpResponseMessage(HttpStatusCode.NoContent);
		});
		using DiscordSender sender = new(Webhook, "", 8, TimeSpan.FromSeconds(30), TimeSpan.Zero, _ => { }, handler);

		sender.TryEnqueue("one");
		await firstArrived.Task.WaitAsync(Generous).ConfigureAwait(false);
		sender.TryEnqueue("two");
		sender.TryEnqueue("three");
		release.Set();

		using JsonDocument body = JsonDocument.Parse(await second.Task.WaitAsync(Generous).ConfigureAwait(false));
		Assert.AreEqual("two\nthree", body.RootElement.GetProperty("content").GetString());
	}

	[TestMethod]
	public async Task Sender_RetriesARateLimitedPost()
	{
		TaskCompletionSource<int> succeeded = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int reports = 0;
		using StubHandler handler = new((_, call) =>
		{
			if (call == 1)
			{
				HttpResponseMessage limited = new(HttpStatusCode.TooManyRequests);
				limited.Headers.TryAddWithoutValidation("Retry-After", "0");
				return limited;
			}

			succeeded.TrySetResult(call);
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		});
		using DiscordSender sender = new(Webhook, "", 8, TimeSpan.FromSeconds(5), TimeSpan.Zero, _ => Interlocked.Increment(ref reports), handler);

		sender.TryEnqueue("hello");

		Assert.AreEqual(2, await succeeded.Task.WaitAsync(Generous).ConfigureAwait(false));
		Assert.AreEqual(0, Volatile.Read(ref reports));
	}

	[TestMethod]
	public async Task Sender_ReportsAFailedPost()
	{
		TaskCompletionSource<Exception> failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
		using DiscordSender sender = new(Webhook, "", 8, TimeSpan.FromSeconds(5), TimeSpan.Zero, exception => failure.TrySetResult(exception), handler);

		sender.TryEnqueue("hello");

		Assert.IsInstanceOfType<HttpRequestException>(await failure.Task.WaitAsync(Generous).ConfigureAwait(false));
	}
}
