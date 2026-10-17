// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Panel;

using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Draws "+XP" text that floats up when a player earns XP. In <see cref="FloatieMode.Hud"/> mode it appears
/// just above the crosshair and stays at that spot in the world as the player moves, and gains close together
/// add up in it while the crosshair stays near where it appeared. In <see cref="FloatieMode.World"/> mode it
/// appears above the target for hits and kills, and just in front of the player for XP with no target, and
/// gains on the same target close together add up in one floatie. Only the player who earned it can see it.
/// Each floatie rises and fades. Floaties are rescaled
/// every tick by their distance from the player, so they keep the same size on screen. With a
/// <see cref="FloatieSettings.DrawDistance"/> set, they're drawn that close on the player's line of sight, so
/// walls don't clip them.
/// </summary>
/// <remarks>
/// World text has no outline, so the stroke is four copies of the text in the stroke color, nudged
/// diagonally by <see cref="FloatieSettings.StrokeWidthPixels"/> across the player's view and set just
/// behind the main text. Their offsets are worked out every tick from where the player is looking from, so
/// the stroke stays even as they move.
/// </remarks>
/// <param name="settings">How floaties look and how long they last.</param>
internal sealed class XpFloaties(FloatieSettings settings)
{
	// A floatie above a player sits roughly at head height.
	private const float HeadHeight = 72;

	// Rescaling sends the new size to the client, so it's skipped until the size has drifted this far.
	private const double RescaleThreshold = 0.03;

	// Keeps a floatie at the player's own position from shrinking to nothing.
	private const double MinWorldUnitsPerPixel = 0.001;

	// How far behind the main text, in pixels at its scale, the stroke copies sit so they draw underneath.
	private const double StrokeDepthPixels = 1.5;

	// Fully opaque for this share of a floatie's life, then fading out.
	private const double FadeStart = 0.6;

	private static readonly (int Right, int Up)[] StrokeDirections = [(-1, -1), (-1, 1), (1, -1), (1, 1)];

	private readonly List<Floatie> active = [];
	private readonly Dictionary<int, FloatieMerger> mergers = [];
	private (int Slot, uint Index)[] shown = [];

	// HUD floaties each get their own merge key, counting down below the self key so they never match a slot.
	private int nextHudKey = FloatieMerger.SelfKey;

	/// <summary>
	/// Gets each floatie entity's owner and entity index, stroke copies included, for the transmit filter.
	/// </summary>
	public IReadOnlyList<(int Slot, uint Index)> Shown => shown;

	/// <summary>
	/// Shows what a result earned.
	/// </summary>
	/// <param name="owner">The player who earned it.</param>
	/// <param name="result">What they earned.</param>
	/// <param name="key">The target's slot, or <see cref="FloatieMerger.SelfKey"/> for XP with no target.</param>
	/// <param name="at">Where the target's feet are, or <see langword="null"/> to float in front of the player.</param>
	public void Show(CCSPlayerController owner, ProgressionResult result, int key, Vector? at)
	{
		Ensure.NotNull(owner);
		Ensure.NotNull(result);
		if (!settings.Enabled || (GameState.PawnOf(owner) is not CCSPlayerPawn pawn))
		{
			return;
		}

		double now = Server.CurrentTime;
		if (!mergers.TryGetValue(owner.Slot, out FloatieMerger? merger))
		{
			merger = new FloatieMerger(settings.MergeSeconds);
			mergers[owner.Slot] = merger;
		}

		// On the HUD a gain adds to the floatie the crosshair is still near, and otherwise starts a new one.
		if (settings.Mode == FloatieMode.Hud)
		{
			key = NearHudFloatie(owner.Slot, pawn, now)?.Key ?? --nextHudKey;
		}

		if (merger.Add(key, now, result) is not FloatieUpdate update)
		{
			return;
		}

		Floatie? existing = update.IsNew ? null : active.Find(f => (f.Owner == owner.Slot) && (f.Key == key) && f.Text.IsValid);
		if (existing is not null)
		{
			// It carries on rising from where it is, rather than dropping back to where it started.
			existing.Anchor = Risen(existing, ProgressAt(existing, now));
			existing.Highlight = update.Highlight;
			existing.Born = now;
			foreach (CPointWorldText layer in existing.Layers)
			{
				layer.MessageText = update.Text;
				Utilities.SetStateChanged(layer, "CPointWorldText", "m_messageText");
			}

			SetAlpha(existing, 255);
			return;
		}

		RetireOldestIfFull(owner.Slot);
		Vector anchor = settings.Mode == FloatieMode.Hud
			? AtCrosshair(pawn, settings.HudStartOffset)
			: at is null ? InFrontOf(pawn) : new Vector(at.X, at.Y, at.Z + HeadHeight);
		Floatie? floatie = Create(update, owner.Slot, key, pawn, anchor);
		if (floatie is null)
		{
			return;
		}

		floatie.Born = now;
		floatie.AimPoint = AtCrosshair(pawn, 0);
		active.Add(floatie);
		Place(floatie, pawn, anchor);
		RefreshShown();
	}

	/// <summary>
	/// Moves every floatie up, rescales and fades it, and removes the ones that have finished. Called every tick.
	/// </summary>
	public void Tick()
	{
		if (active.Count == 0)
		{
			return;
		}

		double now = Server.CurrentTime;
		bool removed = false;
		for (int i = active.Count - 1; i >= 0; i--)
		{
			Floatie floatie = active[i];
			double age = now - floatie.Born;
			if (!floatie.Text.IsValid || (age >= settings.LifetimeSeconds))
			{
				Remove(floatie);
				active.RemoveAt(i);
				removed = true;
				continue;
			}

			double progress = age / settings.LifetimeSeconds;
			if (GameState.PawnOf(Utilities.GetPlayerFromSlot(floatie.Owner)) is CCSPlayerPawn pawn)
			{
				Place(floatie, pawn, Risen(floatie, progress));
			}

			if (progress > FadeStart)
			{
				int alpha = (int)Math.Round(255 * (1 - ((progress - FadeStart) / (1 - FadeStart))));
				SetAlpha(floatie, Math.Clamp(alpha, 0, 255));
			}
		}

		if (removed)
		{
			RefreshShown();
		}

		foreach (FloatieMerger merger in mergers.Values)
		{
			merger.Expire(now);
		}
	}

	/// <summary>
	/// Removes a player's floaties, for when they leave.
	/// </summary>
	/// <param name="slot">The player's slot.</param>
	public void RemoveOwner(int slot)
	{
		foreach (Floatie floatie in active.Where(f => f.Owner == slot).ToList())
		{
			Remove(floatie);
			active.Remove(floatie);
		}

		mergers.Remove(slot);
		RefreshShown();
	}

	/// <summary>
	/// Removes every floatie, for map end and plugin unload.
	/// </summary>
	public void RemoveAll()
	{
		foreach (Floatie floatie in active)
		{
			Remove(floatie);
		}

		active.Clear();
		mergers.Clear();
		RefreshShown();
	}

	private Floatie? Create(FloatieUpdate update, int owner, int key, CCSPlayerPawn ownerPawn, Vector anchor)
	{
		double scale = ScaleFor(Distance(ownerPawn, anchor));
		List<CPointWorldText> strokes = [];
		if (settings.StrokeWidthPixels > 0)
		{
			foreach ((int _, int _) in StrokeDirections)
			{
				if (CreateLayer(update.Text, StrokeColor(255), scale, ownerPawn, anchor) is CPointWorldText stroke)
				{
					strokes.Add(stroke);
				}
			}
		}

		CPointWorldText? text = CreateLayer(update.Text, ColorFor(update.Highlight, 255), scale, ownerPawn, anchor);
		if (text is null)
		{
			foreach (CPointWorldText stroke in strokes)
			{
				stroke.Remove();
			}

			return null;
		}

		return new Floatie(text, strokes, owner, key, anchor) { Highlight = update.Highlight, Scale = scale };
	}

	private CPointWorldText? CreateLayer(string text, Color color, double scale, CCSPlayerPawn ownerPawn, Vector anchor)
	{
		CPointWorldText? entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
		if (entity is null)
		{
			return null;
		}

		try
		{
			entity.MessageText = text;
			entity.Enabled = true;
			entity.Fullbright = true;
			entity.FontSize = settings.FontSize;
			entity.FontName = "Arial Bold";
			entity.WorldUnitsPerPx = (float)scale;
			entity.Color = color;
			entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
			entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
			entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
			entity.DrawBackground = false;
			entity.DepthOffset = 0;
			entity.DispatchSpawn();
			entity.Teleport(anchor, new QAngle(0, ownerPawn.EyeAngles.Y + 270, 90), new Vector(0, 0, 0));
			return entity;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
		{
			// A floatie is decoration, so one that fails to set up is dropped rather than shown broken.
			entity.Remove();
			return null;
		}
	}

	// Puts the text and its stroke copies where the owner sees the floatie's world position. Text kept at a
	// constant screen size looks identical anywhere along the line of sight, so it's drawn just in front of
	// the owner's eyes on that line, which keeps walls between them and the target from clipping it.
	private void Place(Floatie floatie, CCSPlayerPawn ownerPawn, Vector position)
	{
		Vector eye = Eye(ownerPawn);
		double dx = position.X - eye.X;
		double dy = position.Y - eye.Y;
		double dz = position.Z - eye.Z;
		double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
		double drawDistance = settings.DrawDistance > 0 ? Math.Min(distance, settings.DrawDistance) : distance;
		double pull = distance > 0.001 ? drawDistance / distance : 1;
		Vector drawn = new((float)(eye.X + (dx * pull)), (float)(eye.Y + (dy * pull)), (float)(eye.Z + (dz * pull)));
		Rescale(floatie, drawDistance);

		floatie.Text.Teleport(drawn, null, null);
		if (floatie.Strokes.Count == 0)
		{
			return;
		}

		// The text turns to face the player around the vertical axis, so its right is horizontal and
		// perpendicular to the line of sight, and its up is world up.
		double horizontal = Math.Sqrt((dx * dx) + (dy * dy));
		(double rightX, double rightY) = horizontal > 0.001 ? (dy / horizontal, -dx / horizontal) : (1.0, 0.0);
		(double awayX, double awayY, double awayZ) = distance > 0.001 ? (dx / distance, dy / distance, dz / distance) : (0.0, 0.0, 0.0);
		double offset = settings.StrokeWidthPixels * floatie.Scale;
		double depth = StrokeDepthPixels * floatie.Scale;

		for (int i = 0; i < floatie.Strokes.Count; i++)
		{
			(int right, int up) = StrokeDirections[i % StrokeDirections.Length];
			floatie.Strokes[i].Teleport(
				new Vector(
					(float)(drawn.X + (rightX * offset * right) + (awayX * depth)),
					(float)(drawn.Y + (rightY * offset * right) + (awayY * depth)),
					(float)(drawn.Z + (offset * up) + (awayZ * depth))),
				null,
				null);
		}
	}

	// The point on the crosshair HudDistance in front of the eyes, raised along the view's own up.
	private Vector AtCrosshair(CCSPlayerPawn pawn, double above)
	{
		Vector eye = Eye(pawn);
		(double forwardX, double forwardY, double forwardZ) = Forward(pawn);
		double pitch = pawn.EyeAngles.X * Math.PI / 180;
		double yaw = pawn.EyeAngles.Y * Math.PI / 180;
		(double upX, double upY, double upZ) = (Math.Sin(pitch) * Math.Cos(yaw), Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch));
		return new Vector(
			(float)(eye.X + (forwardX * settings.HudDistance) + (upX * above)),
			(float)(eye.Y + (forwardY * settings.HudDistance) + (upY * above)),
			(float)(eye.Z + (forwardZ * settings.HudDistance) + (upZ * above)));
	}

	// The owner's newest HUD floatie that can still grow and that the crosshair is still pointing near.
	private Floatie? NearHudFloatie(int owner, CCSPlayerPawn pawn, double now)
	{
		Vector eye = Eye(pawn);
		(double forwardX, double forwardY, double forwardZ) = Forward(pawn);
		double minCosine = Math.Cos(settings.HudReuseDegrees * Math.PI / 180);
		return active
			.Where(f => (f.Owner == owner) && (f.AimPoint is not null) && f.Text.IsValid && ((now - f.Born) <= settings.MergeSeconds))
			.OrderByDescending(f => f.Born)
			.FirstOrDefault(f =>
			{
				double dx = f.AimPoint!.X - eye.X;
				double dy = f.AimPoint.Y - eye.Y;
				double dz = f.AimPoint.Z - eye.Z;
				double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
				return (length > 0.001) && ((((dx * forwardX) + (dy * forwardY) + (dz * forwardZ)) / length) >= minCosine);
			});
	}

	private double ProgressAt(Floatie floatie, double now) => Math.Clamp((now - floatie.Born) / settings.LifetimeSeconds, 0, 1);

	private Vector Risen(Floatie floatie, double progress)
	{
		double rise = settings.Mode == FloatieMode.Hud ? settings.HudRise : settings.RiseUnits;
		return new Vector(floatie.Anchor.X, floatie.Anchor.Y, (float)(floatie.Anchor.Z + (rise * progress)));
	}

	private static (double X, double Y, double Z) Forward(CCSPlayerPawn pawn)
	{
		double pitch = pawn.EyeAngles.X * Math.PI / 180;
		double yaw = pawn.EyeAngles.Y * Math.PI / 180;
		return (Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), -Math.Sin(pitch));
	}

	private Vector InFrontOf(CCSPlayerPawn pawn)
	{
		Vector eye = Eye(pawn);
		double yaw = pawn.EyeAngles.Y * Math.PI / 180;
		return new Vector(
			(float)(eye.X + (Math.Cos(yaw) * settings.SelfDistance)),
			(float)(eye.Y + (Math.Sin(yaw) * settings.SelfDistance)),
			eye.Z);
	}

	private void Rescale(Floatie floatie, double distance)
	{
		double scale = ScaleFor(distance);
		if (Math.Abs(scale - floatie.Scale) <= (floatie.Scale * RescaleThreshold))
		{
			return;
		}

		floatie.Scale = scale;
		foreach (CPointWorldText layer in floatie.Layers)
		{
			layer.WorldUnitsPerPx = (float)scale;
			Utilities.SetStateChanged(layer, "CPointWorldText", "m_flWorldUnitsPerPx");
		}
	}

	// World units per pixel proportional to distance is what holds the on-screen size constant.
	private double ScaleFor(double distance) => Math.Max(MinWorldUnitsPerPixel, distance * settings.ScreenSize);

	private static Vector Eye(CCSPlayerPawn pawn)
	{
		Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
		return new Vector(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
	}

	private static double Distance(CCSPlayerPawn pawn, Vector point)
	{
		Vector eye = Eye(pawn);
		double dx = eye.X - point.X;
		double dy = eye.Y - point.Y;
		double dz = eye.Z - point.Z;
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}

	private void SetAlpha(Floatie floatie, int alpha)
	{
		floatie.Text.Color = ColorFor(floatie.Highlight, alpha);
		Utilities.SetStateChanged(floatie.Text, "CPointWorldText", "m_Color");

		Color stroke = StrokeColor(alpha);
		foreach (CPointWorldText layer in floatie.Strokes)
		{
			layer.Color = stroke;
			Utilities.SetStateChanged(layer, "CPointWorldText", "m_Color");
		}
	}

	private void RetireOldestIfFull(int owner)
	{
		List<Floatie> mine = [.. active.Where(f => f.Owner == owner).OrderBy(f => f.Born)];
		for (int i = 0; i <= mine.Count - settings.MaxPerPlayer; i++)
		{
			Remove(mine[i]);
			active.Remove(mine[i]);
		}
	}

	// The outline is a share of the text's opacity, so it fades out with the text.
	private Color StrokeColor(int textAlpha)
	{
		Color baseColor = ParseColor(settings.StrokeColor);
		int alpha = (int)Math.Round(textAlpha * Math.Clamp(settings.StrokeOpacity, 0, 1));
		return System.Drawing.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
	}

	private Color ColorFor(bool highlight, int alpha)
	{
		Color baseColor = ParseColor(highlight ? settings.HighlightColor : settings.Color);
		return System.Drawing.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
	}

	private void RefreshShown() => shown =
	[
		.. active.SelectMany(f => f.Layers.Where(layer => layer.IsValid).Select(layer => (f.Owner, layer.Index))),
	];

	private static void Remove(Floatie floatie)
	{
		foreach (CPointWorldText layer in floatie.Layers)
		{
			if (layer.IsValid)
			{
				layer.Remove();
			}
		}
	}

	private static Color ParseColor(string hex)
	{
		string digits = hex.TrimStart('#');
		return (digits.Length == 6) && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
			? System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
			: System.Drawing.Color.White;
	}

	private sealed class Floatie(CPointWorldText text, List<CPointWorldText> strokes, int owner, int key, Vector anchor)
	{
		public CPointWorldText Text { get; } = text;

		public List<CPointWorldText> Strokes { get; } = strokes;

		public IEnumerable<CPointWorldText> Layers => Strokes.Append(Text);

		public int Owner { get; } = owner;

		public int Key { get; } = key;

		public Vector Anchor { get; set; } = anchor;

		// Where the crosshair pointed when a HUD floatie appeared, for deciding whether new XP adds to it.
		public Vector? AimPoint { get; set; }

		public double Born { get; set; }

		public bool Highlight { get; set; }

		public double Scale { get; set; }
	}
}
