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
/// The "+XP" text that floats in the world when a player earns XP.
/// </summary>
public sealed class FloatieSettings
{
	/// <summary>Gets or sets a value indicating whether floaties are shown.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Gets or sets how long a floatie lasts, in seconds.</summary>
	public double LifetimeSeconds { get; set; } = 1.4;

	/// <summary>Gets or sets how far a floatie rises over its life, in units.</summary>
	public double RiseUnits { get; set; } = 28;

	/// <summary>Gets or sets how soon after one gain the next on the same target adds to the same floatie, in seconds.</summary>
	public double MergeSeconds { get; set; } = 0.75;

	/// <summary>Gets or sets the most floaties one player can have showing at once. The oldest goes first.</summary>
	public int MaxPerPlayer { get; set; } = 6;

	/// <summary>Gets or sets the font size.</summary>
	public float FontSize { get; set; } = 48;

	/// <summary>
	/// Gets or sets how big floaties look on screen. A floatie is scaled by this times its distance from the
	/// player every tick, so it stays the same size on screen however near or far it is. The default draws the
	/// number about 28 pixels tall at 1080p with the default font size.
	/// </summary>
	public double ScreenSize { get; set; } = 0.0008;

	/// <summary>Gets or sets how far in front of the player a floatie with no target appears, in units.</summary>
	public double SelfDistance { get; set; } = 90;

	/// <summary>Gets or sets the color of plain XP, as #RRGGBB.</summary>
	public string Color { get; set; } = "#FFFFFF";

	/// <summary>Gets or sets the color when a floatie names a skill shot, tier, level, or rank-up, as #RRGGBB.</summary>
	public string HighlightColor { get; set; } = "#F2C94C";
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

	/// <summary>Gets or sets the floating "+XP" text.</summary>
	public FloatieSettings Floaties { get; set; } = new();

	/// <summary>Gets or sets how often, in seconds, changed progress is saved.</summary>
	public float SaveIntervalSeconds { get; set; } = 60;

	/// <summary>Gets or sets the chat prefix on every message.</summary>
	public string ChatPrefix { get; set; } = "[Progression]";

	/// <summary>Gets or sets a value indicating whether common skill shots are announced to the player who earned them.</summary>
	public bool AnnounceCommonSkillShots { get; set; }
}
