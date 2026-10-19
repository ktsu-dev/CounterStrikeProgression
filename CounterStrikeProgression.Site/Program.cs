// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Cards;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class Program
{
	public static void Main(string[] args)
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.ListenAnyIP(Endpoints.PublicPort);
			kestrel.ListenAnyIP(Endpoints.IngestPort);
		});
		builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
		AddServices(builder);

		WebApplication app = builder.Build();
		MetricsEndpoints.Map(app);
		MapRoutes(app);
		app.Run();
	}

	private static void AddServices(WebApplicationBuilder builder)
	{
		IServiceCollection services = builder.Services;
		ConfigurationManager configuration = builder.Configuration;
		SiteOptions options = configuration.GetSection("Site").Get<SiteOptions>() ?? new();
		bool hasDatabase = !string.IsNullOrWhiteSpace(configuration["Site:Database:Host"]);

		services.ConfigureHttpJsonOptions(json => SiteJson.Apply(json.SerializerOptions));
		services.AddSingleton(options);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton(new FeedStore(Path.Combine(options.DataPath, "feed.json")));
		services.AddSingleton<LiveState>();
		services.AddSingleton<SiteMetrics>();
		services.AddSingleton<Broadcaster>();
		services.AddSingleton<IProgressionStore>(hasDatabase ? new MySqlProgressionStore(options.Database) : new InMemoryProgressionStore());
		services.AddSingleton(provider => new ProgressReader(provider.GetRequiredService<IProgressionStore>(), provider.GetRequiredService<TimeProvider>()));
		services.AddHostedService<OnlineWatcher>();
		services.AddSingleton<FeatIndex>();
		services.AddSingleton(provider => new BoardIndex(provider.GetRequiredService<TimeProvider>()));
		services.AddHostedService<FeatRefresher>();
		services.AddSingleton(provider => new CardCache(provider.GetRequiredService<TimeProvider>()));
	}

	private static void MapRoutes(WebApplication app)
	{
		app.Use(async (context, next) =>
		{
			if (!Endpoints.IsAllowedOnPort(context.Connection.LocalPort, context.Request.Path))
			{
				context.Response.StatusCode = StatusCodes.Status404NotFound;
				return;
			}

			await next(context).ConfigureAwait(false);
		});
		app.UseStaticFiles();

		app.MapGet("/", (IWebHostEnvironment environment) => Endpoints.Page(environment, "index.html"));
		app.MapGet("/ranks", (IWebHostEnvironment environment) => Endpoints.Page(environment, "ranks.html"));
		app.MapGet("/ladder", (IWebHostEnvironment environment) => Endpoints.Page(environment, "ladder.html"));
		app.MapGet("/leaderboards", (IWebHostEnvironment environment) => Endpoints.Page(environment, "leaderboards.html"));
		app.MapGet("/skillshots", (IWebHostEnvironment environment) => Endpoints.Page(environment, "skillshots.html"));
		app.MapGet("/seasons", (IWebHostEnvironment environment) => Endpoints.Page(environment, "seasons.html"));
		app.MapGet("/healthz", Endpoints.Health);
		MapPlayerApi(app);
		MapApiRoutes(app);
	}

	private static void MapApiRoutes(WebApplication app)
	{
		app.MapGet("/api/live", Endpoints.Live);
		app.MapGet("/api/ranks", Endpoints.RanksAsync);
		app.MapGet("/api/ladder", Endpoints.LadderAsync);
		app.MapGet("/api/leaderboards", Endpoints.LeaderboardsAsync);
		app.MapGet("/api/skillshots", Endpoints.SkillShotsAsync);
		app.MapGet("/api/weekly", HistoryEndpoints.WeeklyAsync);
		app.MapGet("/api/seasons", SeasonEndpoints.SeasonsAsync);
		app.MapGet("/api/maps", MapEndpoints.MapsAsync);
		app.MapGet("/api/players/{id}/maps", MapEndpoints.PlayerMapsAsync);
		app.MapGet("/api/stream", Endpoints.Stream);
		app.MapPost(Endpoints.IngestRoute, Endpoints.IngestAsync);
		MapCardRoutes(app);
	}

	private static void MapCardRoutes(WebApplication app)
	{
		app.MapGet("/players/{id}", CardEndpoints.PlayerPageAsync);
		app.MapGet(CardEndpoints.CardRoute, CardEndpoints.CardAsync);
	}

	// Kept apart from MapRoutes so neither method couples to too many types.
	private static void MapPlayerApi(WebApplication app)
	{
		app.MapGet("/api/players/{id}", Endpoints.PlayerAsync);
		app.MapGet("/api/players/{id}/history", HistoryEndpoints.PlayerHistoryAsync);
		app.MapGet("/api/players/{id}/seasons", SeasonEndpoints.PlayerSeasonsAsync);
		app.MapGet("/api/players/{id}/rivals", RivalEndpoints.PlayerRivalsAsync);
	}
}
