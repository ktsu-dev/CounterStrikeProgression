// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Cards;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

/// <summary>
/// The route handlers for the share cards, and the player page that names its card in its Open Graph tags,
/// so a pasted link unfurls as the card in Discord and Steam chat.
/// </summary>
internal static class CardEndpoints
{
	/// <summary>The card route. The id is a SteamID64.</summary>
	public const string CardRoute = "/card/{id}.png";

	private const string ServerName = "ktsu.dev CS2";

	/// <summary>
	/// Gets a player's share card.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="context">The request, whose response gets the cache header.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the current season.</param>
	/// <param name="cache">The kept cards.</param>
	/// <returns>The PNG, 404 for a bad or unknown id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> CardAsync(string id, HttpContext context, ProgressReader reader, LiveState live, TimeProvider time, CardCache cache)
	{
		Ensure.NotNull(context);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		CachedCard? card;
		try
		{
			card = await CardFor(steamId, reader, live, time, cache, context.RequestAborted).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		if (card is null)
		{
			return TypedResults.NotFound();
		}

		context.Response.Headers.CacheControl = "public, max-age=" + ((int)CardCache.Lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture);
		return TypedResults.File(card.Png, "image/png");
	}

	/// <summary>
	/// Serves the player page for a well-formed SteamID64, with the player's Open Graph tags when the player is known.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="context">The request, for the site's address.</param>
	/// <param name="environment">The host environment.</param>
	/// <param name="options">The site settings, for the public address.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the current season.</param>
	/// <param name="cache">The kept cards.</param>
	/// <returns>The page, or 404 when the id isn't a SteamID64 or the file is missing.</returns>
	public static async Task<IResult> PlayerPageAsync(string id, HttpContext context, IWebHostEnvironment environment, SiteOptions options, ProgressReader reader, LiveState live, TimeProvider time, CardCache cache)
	{
		Ensure.NotNull(context);
		Ensure.NotNull(environment);
		Ensure.NotNull(options);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		IFileInfo file = environment.WebRootFileProvider.GetFileInfo("player.html");
		if (file is not { Exists: true, PhysicalPath: { } path })
		{
			return TypedResults.NotFound();
		}

		string html = await File.ReadAllTextAsync(path, context.RequestAborted).ConfigureAwait(false);
		CachedCard? card = null;
		try
		{
			card = await CardFor(steamId, reader, live, time, cache, context.RequestAborted).ConfigureAwait(false);
		}
		catch (DbException)
		{
			// The page loads its data itself and says what failed, so it goes out without the tags.
		}

		if (card is not null)
		{
			html = WithCard(html, card.Card, PublicBase(context.Request, options));
		}

		return TypedResults.Text(html, "text/html; charset=utf-8");
	}

	/// <summary>
	/// Adds a player's Open Graph tags and name to the player page.
	/// </summary>
	/// <param name="html">The player page.</param>
	/// <param name="card">The player's card.</param>
	/// <param name="baseUrl">The site's address, with no trailing slash.</param>
	/// <returns>The page with the tags.</returns>
	internal static string WithCard(string html, PlayerCard card, string baseUrl)
	{
		Ensure.NotNull(html);
		Ensure.NotNull(card);

		string title = Encode(card.Title);
		string description = Encode(card.Description);

		// The XP is in the image link so a chat that keeps previews by link shows the card again once it changes.
		string image = Encode($"{baseUrl}/card/{card.SteamId}.png?v={card.RankXp.ToString(CultureInfo.InvariantCulture)}");
		StringBuilder tags = new();
		tags.Append("\t<meta name=\"description\" content=\"").Append(description).Append("\">\n");
		tags.Append("\t<meta name=\"theme-color\" content=\"").Append(CardRenderer.RankColorHex(card.Rank)).Append("\">\n");
		tags.Append("\t<meta property=\"og:type\" content=\"profile\">\n");
		tags.Append("\t<meta property=\"og:site_name\" content=\"").Append(ServerName).Append("\">\n");
		tags.Append("\t<meta property=\"og:title\" content=\"").Append(title).Append("\">\n");
		tags.Append("\t<meta property=\"og:description\" content=\"").Append(description).Append("\">\n");
		tags.Append("\t<meta property=\"og:url\" content=\"").Append(Encode($"{baseUrl}/players/{card.SteamId}")).Append("\">\n");
		tags.Append("\t<meta property=\"og:image\" content=\"").Append(image).Append("\">\n");
		tags.Append("\t<meta property=\"og:image:type\" content=\"image/png\">\n");
		tags.Append("\t<meta property=\"og:image:width\" content=\"").Append(CardRenderer.Width.ToString(CultureInfo.InvariantCulture)).Append("\">\n");
		tags.Append("\t<meta property=\"og:image:height\" content=\"").Append(CardRenderer.Height.ToString(CultureInfo.InvariantCulture)).Append("\">\n");
		tags.Append("\t<meta property=\"og:image:alt\" content=\"").Append(title).Append("\">\n");
		tags.Append("\t<meta name=\"twitter:card\" content=\"summary_large_image\">\n");

		string named = html.Replace($"<title>Player · {ServerName}</title>", $"<title>{Encode(card.Name)} · {ServerName}</title>", StringComparison.Ordinal);
		int head = named.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
		return head < 0 ? named : named.Insert(head, tags.ToString());
	}

	/// <summary>
	/// Works out the site's public address: the configured one, or else the address the request came to,
	/// with the scheme the proxy in front of the site reports.
	/// </summary>
	/// <param name="request">The request.</param>
	/// <param name="options">The site settings.</param>
	/// <returns>The address, with no trailing slash.</returns>
	internal static string PublicBase(HttpRequest request, SiteOptions options)
	{
		Ensure.NotNull(request);
		Ensure.NotNull(options);

		if (Uri.TryCreate(options.PublicUrl?.Trim(), UriKind.Absolute, out Uri? configured) && (configured.Scheme == Uri.UriSchemeHttps || configured.Scheme == Uri.UriSchemeHttp))
		{
			return configured.GetLeftPart(UriPartial.Path).TrimEnd('/');
		}

		string forwarded = request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim();
		string scheme = forwarded is "https" or "http" ? forwarded : request.Scheme;
		return $"{scheme}://{request.Host.ToUriComponent()}";
	}

	private static async Task<CachedCard?> CardFor(ulong steamId, ProgressReader reader, LiveState live, TimeProvider time, CardCache cache, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);
		Ensure.NotNull(cache);

		return await cache.GetAsync(steamId, async () =>
		{
			PlayerProgress? stored = await reader.PlayerAsync(steamId, cancellationToken).ConfigureAwait(false);
			ProgressSnapshot? overlay = live.Overlay(steamId);
			if (stored is null && ProgressMerge.SteamIdOf(overlay) is null)
			{
				return null;
			}

			PlayerProgress merged = ProgressMerge.Merge(stored, overlay);
			ProgressionSettings settings = live.Settings;
			PlayerView player = PlayerViewBuilder.Build(merged, settings, live.IsPlaying(steamId), now: time.GetUtcNow().UtcDateTime);
			PlayerSeasonsView seasons = await SeasonEndpoints.PlayerSeasonsViewAsync(steamId, reader, live, time, cancellationToken).ConfigureAwait(false);
			return PlayerCard.From(player, seasons);
		}).ConfigureAwait(false);
	}

	private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
