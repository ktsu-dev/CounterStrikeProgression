// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Cards;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

[TestClass]
public sealed class CardTests
{
	private static readonly DateTime Seen = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-cards-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState NewState(TimeProvider time) =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), time, NullLogger<LiveState>.Instance);

	private static PlayerCard Card(string name = "Tester", long? place = 3, params CardWeapon[] weapons) =>
		new("76561198000000001", name, 17, "Gold Nova · III", 12345, 0.4, weapons, "Oct–Dec 2026", place, 40);

	private async Task<InMemoryProgressionStore> StoreWithPlayerAsync()
	{
		InMemoryProgressionStore store = new();
		ulong id = TestHelpers.SteamId;
		ProgressSnapshot rows = new(
			new PlayerRow(id, "Tester", 12345, 0, Seen, Seen),
			[
				new WeaponRow(id, "ak47", 9000, 0, 300, 120, 900, 2000),
				new WeaponRow(id, "awp", 7000, 0, 150, 20, 160, 300),
				new WeaponRow(id, "deagle", 3000, 0, 80, 40, 200, 500),
				new WeaponRow(id, "knife", 100, 0, 3, 0, 3, 3),
			],
			[]);
		await store.SaveAsync(rows, TestContext.CancellationToken).ConfigureAwait(false);
		return store;
	}

	[TestMethod]
	public void Ordinal_UsesEnglishSuffixes()
	{
		Assert.AreEqual("1st", PlayerCard.Ordinal(1));
		Assert.AreEqual("2nd", PlayerCard.Ordinal(2));
		Assert.AreEqual("3rd", PlayerCard.Ordinal(3));
		Assert.AreEqual("4th", PlayerCard.Ordinal(4));
		Assert.AreEqual("11th", PlayerCard.Ordinal(11));
		Assert.AreEqual("12th", PlayerCard.Ordinal(12));
		Assert.AreEqual("13th", PlayerCard.Ordinal(13));
		Assert.AreEqual("21st", PlayerCard.Ordinal(21));
		Assert.AreEqual("112th", PlayerCard.Ordinal(112));
		Assert.AreEqual("1,023rd", PlayerCard.Ordinal(1023));
	}

	[TestMethod]
	public void Description_NamesXpSeasonAndWeapons()
	{
		PlayerCard card = Card(weapons: [new CardWeapon("AK-47", 12, 40, 300), new CardWeapon("AWP", 9, 40, 150)]);

		Assert.AreEqual("Tester · Gold Nova · III", card.Title);
		Assert.AreEqual("12,345 XP · 3rd of 40 in Oct–Dec 2026 · Best weapons: AK-47 12, AWP 9", card.Description);
		Assert.AreEqual("12,345 XP", Card(place: null).Description);
	}

	[TestMethod]
	public void From_KeepsTheTopThreeWeaponsAndTheSeasonPlace()
	{
		PlayerView player = PlayerViewBuilder.Build(TestHelpers.Player(), new ProgressionSettings(), online: false);
		PlayerView withWeapons = player with
		{
			Weapons =
			[
				.. Enumerable.Range(1, 5).Select(i => new WeaponView($"w{i}", $"Weapon {i}", 10 - i, 40, 0, 0, 0, i * 10, 0, 0, [])),
			],
		};

		PlayerCard card = PlayerCard.From(withWeapons, new PlayerSeasonsView("Oct–Dec 2026", 500, 2, 9, []));

		Assert.HasCount(PlayerCard.TopWeapons, card.Weapons);
		Assert.AreEqual(new CardWeapon("Weapon 1", 9, 40, 10), card.Weapons[0]);
		Assert.AreEqual(2L, card.SeasonPlace);
		Assert.AreEqual(9L, card.SeasonPlayers);
		Assert.AreEqual(PlayerCard.UnnamedPlayer, PlayerCard.From(withWeapons with { Name = "  " }, new PlayerSeasonsView("Oct–Dec 2026", 0, null, 0, [])).Name);
	}

	[TestMethod]
	public void WithCard_AddsEncodedOpenGraphTagsAndTheName()
	{
		const string page = "<html><head>\n\t<title>Player · ktsu.dev CS2</title>\n</head><body></body></html>";
		PlayerCard card = Card(name: "<b>\"Bad\"</b>");

		string html = CardEndpoints.WithCard(page, card, "https://cs2.example.com");

		Assert.Contains("<title>&lt;b&gt;&quot;Bad&quot;&lt;/b&gt; · ktsu.dev CS2</title>", html);
		Assert.Contains("<meta property=\"og:image\" content=\"https://cs2.example.com/card/76561198000000001.png?v=12345\">", html);
		Assert.Contains("<meta property=\"og:url\" content=\"https://cs2.example.com/players/76561198000000001\">", html);
		Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\">", html);
		Assert.Contains("<meta name=\"theme-color\" content=\"#f2c94c\">", html);
		Assert.DoesNotContain("<b>", html);
		Assert.IsLessThan(html.IndexOf("</head>", StringComparison.Ordinal), html.IndexOf("og:title", StringComparison.Ordinal));
	}

	[TestMethod]
	public void PublicBase_PrefersTheSettingThenTheForwardedScheme()
	{
		DefaultHttpContext context = new();
		context.Request.Scheme = "http";
		context.Request.Host = new HostString("cs2.example.com");

		Assert.AreEqual("http://cs2.example.com", CardEndpoints.PublicBase(context.Request, new SiteOptions()));

		context.Request.Headers["X-Forwarded-Proto"] = "https";
		Assert.AreEqual("https://cs2.example.com", CardEndpoints.PublicBase(context.Request, new SiteOptions()));

		context.Request.Headers["X-Forwarded-Proto"] = "javascript";
		Assert.AreEqual("http://cs2.example.com", CardEndpoints.PublicBase(context.Request, new SiteOptions()));

		Assert.AreEqual("https://stats.example.com/cs2", CardEndpoints.PublicBase(context.Request, new SiteOptions { PublicUrl = " https://stats.example.com/cs2/ " }));
		Assert.AreEqual("http://cs2.example.com", CardEndpoints.PublicBase(context.Request, new SiteOptions { PublicUrl = "not a url" }));
	}

	[TestMethod]
	public void Printable_DropsWhatTheFontCannotDraw()
	{
		Assert.AreEqual("Tester", CardRenderer.Printable("Tester"));
		Assert.AreEqual("Тестер", CardRenderer.Printable("Тестер"));
		Assert.AreEqual("Ace", CardRenderer.Printable("Ace 🔥"));
		Assert.AreEqual(PlayerCard.UnnamedPlayer, CardRenderer.Printable("玩家"));
		Assert.AreEqual("a b", CardRenderer.Printable("a\u0000  b"));
	}

	[TestMethod]
	public void Render_DrawsAnOpenGraphSizedPng()
	{
		string[] names = ["Tester", "A name far too long to fit on one line of the share card at all, honestly"];
		for (int rank = 0; rank < RankLadder.Names.Count; rank += 7)
		{
			byte[] png = CardRenderer.Render(Card(name: names[rank % 2], weapons: rank % 2 == 0 ? [] : [new CardWeapon("AK-47", 12, 40, 300)]) with { Rank = rank });

			using SKBitmap bitmap = SKBitmap.Decode(png);
			Assert.AreEqual(CardRenderer.Width, bitmap.Width);
			Assert.AreEqual(CardRenderer.Height, bitmap.Height);
		}
	}

	[TestMethod]
	public async Task CardEndpoint_ServesAKnownPlayerAndRejectsOthers()
	{
		ManualTime time = new();
		using ProgressReader reader = new(await StoreWithPlayerAsync().ConfigureAwait(false), time);
		LiveState live = NewState(time);
		CardCache cache = new(time);
		DefaultHttpContext context = new();

		IResult result = await CardEndpoints.CardAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), context, reader, live, time, cache).ConfigureAwait(false);

		FileContentHttpResult file = Assert.IsInstanceOfType<FileContentHttpResult>(result);
		Assert.AreEqual("image/png", file.ContentType);
		Assert.AreEqual("public, max-age=300", context.Response.Headers.CacheControl.ToString());
		Assert.AreEqual(1, cache.Count);

		Assert.IsInstanceOfType<NotFound>(await CardEndpoints.CardAsync("nope", new DefaultHttpContext(), reader, live, time, cache).ConfigureAwait(false));
		Assert.IsInstanceOfType<NotFound>(await CardEndpoints.CardAsync("76561198999999999", new DefaultHttpContext(), reader, live, time, cache).ConfigureAwait(false));
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public async Task CardCache_KeepsACardForItsLifetime()
	{
		ManualTime time = new();
		CardCache cache = new(time);
		int built = 0;
		Task<PlayerCard?> Build()
		{
			built++;
			return Task.FromResult<PlayerCard?>(Card());
		}

		CachedCard? first = await cache.GetAsync(1, Build).ConfigureAwait(false);
		CachedCard? again = await cache.GetAsync(1, Build).ConfigureAwait(false);
		Assert.AreSame(first, again);
		Assert.AreEqual(1, built);

		time.Advance(CardCache.Lifetime);
		Assert.AreNotSame(first, await cache.GetAsync(1, Build).ConfigureAwait(false));
		Assert.AreEqual(2, built);
	}

	[TestMethod]
	public async Task CardCache_StaysWithinItsCapacity()
	{
		ManualTime time = new();
		CardCache cache = new(time);
		for (ulong id = 1; id <= CardCache.Capacity + 10; id++)
		{
			await cache.GetAsync(id, () => Task.FromResult<PlayerCard?>(Card())).ConfigureAwait(false);
			time.Advance(TimeSpan.FromMilliseconds(1));
		}

		Assert.AreEqual(CardCache.Capacity, cache.Count);
	}
}
