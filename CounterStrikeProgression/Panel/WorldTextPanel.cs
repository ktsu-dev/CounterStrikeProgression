// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Panel;

using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

/// <summary>
/// Draws the side panel as a <c>point_worldtext</c> entity floating to the right of the player's view.
/// </summary>
/// <remarks>
/// CS2 no longer has a client-predicted viewmodel to attach text to, so the entity is moved in front of the
/// player's eyes every tick. Fast turns make it trail slightly behind. Other players never receive it,
/// because <see cref="Shown"/> drives the transmit filter.
/// </remarks>
/// <param name="settings">Where the panel sits and how it looks.</param>
internal sealed class WorldTextPanel(PanelSettings settings)
{
	/// <summary>The most UTF-8 bytes CS2 stores in a world text message, keeping one for the terminator.</summary>
	public const int MaxTextBytes = 511;

	private readonly Dictionary<int, CPointWorldText> entities = [];
	private (int Slot, uint Index)[] shown = [];

	/// <summary>
	/// Shows or updates a player's panel.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="pawn">The player's pawn.</param>
	/// <param name="text">The panel text.</param>
	/// <param name="textChanged">Whether the text differs from what's drawn.</param>
	/// <exception cref="InvalidOperationException">The entity couldn't be created.</exception>
	public void Show(CCSPlayerController player, CCSPlayerPawn pawn, string text, bool textChanged)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(pawn);

		if (!entities.TryGetValue(player.Slot, out CPointWorldText? entity) || !entity.IsValid)
		{
			entity = Create(text);
			entities[player.Slot] = entity;
			RefreshShown();
		}
		else if (textChanged)
		{
			entity.MessageText = text;
			Utilities.SetStateChanged(entity, "CPointWorldText", "m_messageText");
		}

		(Vector position, QAngle angle) = Placement(pawn);
		entity.Teleport(position, angle, new Vector(0, 0, 0));
	}

	/// <summary>
	/// Removes a player's panel.
	/// </summary>
	/// <param name="slot">The player's slot.</param>
	public void Hide(int slot)
	{
		if (!entities.Remove(slot, out CPointWorldText? entity))
		{
			return;
		}

		if (entity.IsValid)
		{
			entity.Remove();
		}

		RefreshShown();
	}

	/// <summary>
	/// Removes every panel, for map end and plugin unload.
	/// </summary>
	public void HideAll()
	{
		foreach (int slot in entities.Keys.ToList())
		{
			Hide(slot);
		}
	}

	/// <summary>
	/// Gets each showing panel's owner and entity index. The transmit filter reads it every tick, so it's
	/// rebuilt only when a panel appears or goes away.
	/// </summary>
	public IReadOnlyList<(int Slot, uint Index)> Shown => shown;

	/// <summary>
	/// Gets a value indicating whether any panel is showing.
	/// </summary>
	public bool Any => shown.Length > 0;

	private void RefreshShown() => shown = [.. entities.Where(e => e.Value.IsValid).Select(e => (e.Key, e.Value.Index))];

	private CPointWorldText Create(string text)
	{
		CPointWorldText entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext")
			?? throw new InvalidOperationException("CS2 refused to create a point_worldtext entity.");

		try
		{
			Configure(entity, text);
			entity.DispatchSpawn();
			return entity;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
		{
			// The entity exists already, so a failed setup would otherwise leak it.
			entity.Remove();
			throw;
		}
	}

	private void Configure(CPointWorldText entity, string text)
	{
		entity.MessageText = text;
		entity.Enabled = true;
		entity.Fullbright = true;
		entity.FontSize = settings.FontSize;
		entity.FontName = "Arial Bold";
		entity.WorldUnitsPerPx = settings.WorldUnitsPerPixel;
		entity.Color = ParseColor(settings.Color);
		entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
		entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP;
		entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
		entity.DrawBackground = settings.DrawBackground;
		entity.BackgroundBorderWidth = 0.3f;
		entity.BackgroundBorderHeight = 0.3f;
		entity.DepthOffset = 0;
	}

	private (Vector Position, QAngle Angle) Placement(CCSPlayerPawn pawn)
	{
		Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
		QAngle eyes = pawn.EyeAngles;
		double pitch = eyes.X * Math.PI / 180;
		double yaw = eyes.Y * Math.PI / 180;

		(double forwardX, double forwardY, double forwardZ) = (Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), -Math.Sin(pitch));
		(double rightX, double rightY) = (Math.Sin(yaw), -Math.Cos(yaw));
		(double upX, double upY, double upZ) = (Math.Sin(pitch) * Math.Cos(yaw), Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch));

		double eyeZ = origin.Z + pawn.ViewOffset.Z;
		Vector position = new(
			(float)(origin.X + (forwardX * settings.Distance) + (rightX * settings.OffsetRight) + (upX * settings.OffsetUp)),
			(float)(origin.Y + (forwardY * settings.Distance) + (rightY * settings.OffsetRight) + (upY * settings.OffsetUp)),
			(float)(eyeZ + (forwardZ * settings.Distance) + (upZ * settings.OffsetUp)));

		// World text faces along its local +X once rolled upright, so turn it to face back at the eyes.
		QAngle angle = new(0, eyes.Y + 270, 90 - eyes.X);
		return (position, angle);
	}

	private static Color ParseColor(string hex)
	{
		string digits = hex.TrimStart('#');
		return (digits.Length == 6) && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
			? System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
			: System.Drawing.Color.White;
	}
}
