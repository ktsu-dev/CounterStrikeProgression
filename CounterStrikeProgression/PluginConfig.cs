// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// How the progress panel is drawn.
/// </summary>
public enum PanelMode
{
	/// <summary>The side panel, falling back to the center card and then chat when a way of drawing fails.</summary>
	Auto = 0,

	/// <summary>Only the side panel, falling back to chat.</summary>
	WorldText,

	/// <summary>Only the center-screen card, falling back to chat.</summary>
	CenterHtml,

	/// <summary>Only chat.</summary>
	Chat,
}

/// <summary>
/// Where and how the progress panel appears.
/// </summary>
public sealed class PanelSettings
{
	/// <summary>Gets or sets how the panel is drawn.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter<PanelMode>))]
	public PanelMode Mode { get; set; } = PanelMode.Auto;

	/// <summary>Gets or sets how many body lines the side panel shows at once.</summary>
	public int VisibleLines { get; set; } = 12;

	/// <summary>Gets or sets how many body lines the center card shows at once.</summary>
	public int CenterVisibleLines { get; set; } = 5;

	/// <summary>Gets or sets the side panel's font size.</summary>
	public float FontSize { get; set; } = 26;

	/// <summary>Gets or sets the side panel's world units per pixel, which sets how big it looks.</summary>
	public float WorldUnitsPerPixel { get; set; } = 0.0085f;

	/// <summary>Gets or sets how far in front of the eyes the side panel floats, in units.</summary>
	public float Distance { get; set; } = 7;

	/// <summary>Gets or sets how far right of center the side panel sits, in units at that distance.</summary>
	public float OffsetRight { get; set; } = 3.4f;

	/// <summary>Gets or sets how far above center the side panel sits, in units at that distance.</summary>
	public float OffsetUp { get; set; } = 2.6f;

	/// <summary>Gets or sets the side panel's text color, as #RRGGBB.</summary>
	public string Color { get; set; } = "#F2E6C9";

	/// <summary>Gets or sets a value indicating whether the side panel draws a dark backing behind the text.</summary>
	public bool DrawBackground { get; set; } = true;
}

/// <summary>
/// The plugin's config file, <c>configs/plugins/CounterStrikeProgression/CounterStrikeProgression.json</c>.
/// </summary>
public sealed class PluginConfig : BasePluginConfig
{
	/// <summary>Gets or sets the database. With an empty host, progress is kept in memory until the server restarts.</summary>
	public DatabaseSettings Database { get; set; } = new();

	/// <summary>Gets or sets every XP value and detection threshold.</summary>
	public ProgressionSettings Progression { get; set; } = new();

	/// <summary>Gets or sets the progress panel.</summary>
	public PanelSettings Panel { get; set; } = new();

	/// <summary>Gets or sets how often, in seconds, changed progress is saved.</summary>
	public float SaveIntervalSeconds { get; set; } = 60;

	/// <summary>Gets or sets the chat prefix on every message.</summary>
	public string ChatPrefix { get; set; } = "[Progression]";

	/// <summary>Gets or sets a value indicating whether common skill shots are announced to the player who earned them.</summary>
	public bool AnnounceCommonSkillShots { get; set; }
}
