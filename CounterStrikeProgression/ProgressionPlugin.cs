// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Panel;

/// <summary>
/// The CounterStrikeProgression plugin: matchmaking-style ranks that only go up, a level and a challenge
/// ladder for every weapon, and XP for skill shots, shown in an in-game side panel.
/// </summary>
public sealed class ProgressionPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
	private ProgressionServices? services;

	/// <inheritdoc/>
	public override string ModuleName => "CounterStrikeProgression";

	/// <inheritdoc/>
	public override string ModuleVersion { get; } =
		typeof(ProgressionPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

	/// <inheritdoc/>
	public override string ModuleAuthor => "ktsu.dev";

	/// <inheritdoc/>
	public override string ModuleDescription => "Ranks, weapon levels, and skill shot challenges";

	/// <summary>Gets or sets the parsed config.</summary>
	public PluginConfig Config { get; set; } = new();

	/// <inheritdoc/>
	public void OnConfigParsed(PluginConfig config)
	{
		Ensure.NotNull(config);
		Config = config;
	}

	/// <inheritdoc/>
	public override void Load(bool hotReload)
	{
		ProgressionEngine engine = new(Config.Progression);
		SessionManager sessions = new(CreateStore(), Logger);
		PanelPresenter panel = new(Config.Panel, new PanelBuilder(engine), Logger, Config.ChatPrefix);
		services = new ProgressionServices(Config, engine, sessions, panel, new Announcer(Config), Logger);

		sessions.Initialize();
		new LifecycleHandlers(services).Register(this);
		new CombatHandlers(services).Register(this);
		new PlayerCommands(services, ModuleName, ModuleVersion).Register(this);
		AddTimer(Math.Max(10, Config.SaveIntervalSeconds), sessions.SaveAll, TimerFlags.REPEAT);

		if (hotReload)
		{
			foreach (CCSPlayerController player in Utilities.GetPlayers())
			{
				sessions.Connect(player);
			}
		}
	}

	/// <inheritdoc/>
	public override void Unload(bool hotReload)
	{
		services?.Panel.WorldText.HideAll();
		services?.Floaties.RemoveAll();
		services?.Sessions.Shutdown(TimeSpan.FromSeconds(5));
	}

	private IProgressionStore CreateStore()
	{
		IProgressionStore store;
		if (string.IsNullOrWhiteSpace(Config.Database.Host))
		{
			Log.InMemoryWarning(Logger);
			store = new InMemoryProgressionStore();
		}
		else
		{
			store = new MySqlProgressionStore(Config.Database);
		}

		Log.StoreSelected(Logger, store.GetType().Name);
		return store;
	}
}
