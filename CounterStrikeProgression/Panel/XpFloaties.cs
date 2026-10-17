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
/// Draws "+XP" text in the world where a player earned it: above the target for hits and kills, and just in
/// front of the player for XP with no target. Only the player who earned it can see it. Each floatie rises
/// and fades, and gains on the same target close together add up in one floatie. Floaties are rescaled
/// every tick by their distance from the player, so they keep the same size on screen.
/// </summary>
/// <param name="settings">How floaties look and how long they last.</param>
internal sealed class XpFloaties(FloatieSettings settings)
{
	// A floatie above a player sits roughly at head height.
	private const float HeadHeight = 72;

	// Rescaling sends the new size to the client, so it's skipped until the size has drifted this far.
	private const double RescaleThreshold = 0.03;

	// Keeps a floatie at the player's own position from shrinking to nothing.
	private const double MinWorldUnitsPerPixel = 0.001;

	private readonly List<Floatie> active = [];
	private readonly Dictionary<int, FloatieMerger> mergers = [];
	private (int Slot, uint Index)[] shown = [];

	/// <summary>
	/// Gets each floatie's owner and entity index, for the transmit filter.
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

		if (merger.Add(key, now, result) is not FloatieUpdate update)
		{
			return;
		}

		Floatie? existing = update.IsNew ? null : active.Find(f => (f.Owner == owner.Slot) && (f.Key == key) && f.Entity.IsValid);
		if (existing is not null)
		{
			existing.Entity.MessageText = update.Text;
			existing.Entity.Color = ColorFor(update.Highlight, 255);
			existing.Highlight = update.Highlight;
			existing.Born = now;
			Utilities.SetStateChanged(existing.Entity, "CPointWorldText", "m_messageText");
			Utilities.SetStateChanged(existing.Entity, "CPointWorldText", "m_Color");
			return;
		}

		RetireOldestIfFull(owner.Slot);
		Vector anchor = at is null ? InFrontOf(pawn) : new Vector(at.X, at.Y, at.Z + HeadHeight);
		CPointWorldText? entity = Create(update, pawn, anchor);
		if (entity is null)
		{
			return;
		}

		active.Add(new Floatie(entity, owner.Slot, key, anchor) { Born = now, Highlight = update.Highlight, Scale = entity.WorldUnitsPerPx });
		RefreshShown();
	}

	/// <summary>
	/// Moves every floatie up, fades it, and removes the ones that have finished. Called every tick.
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
			if (!floatie.Entity.IsValid || (age >= settings.LifetimeSeconds))
			{
				Remove(floatie);
				active.RemoveAt(i);
				removed = true;
				continue;
			}

			double progress = age / settings.LifetimeSeconds;
			Vector position = new(floatie.Anchor.X, floatie.Anchor.Y, (float)(floatie.Anchor.Z + (settings.RiseUnits * progress)));
			floatie.Entity.Teleport(position, null, null);
			Rescale(floatie, position);

			// Fully opaque for the first part of its life, then fading out.
			const double FadeStart = 0.6;
			if (progress > FadeStart)
			{
				int alpha = (int)Math.Round(255 * (1 - ((progress - FadeStart) / (1 - FadeStart))));
				floatie.Entity.Color = ColorFor(floatie.Highlight, Math.Clamp(alpha, 0, 255));
				Utilities.SetStateChanged(floatie.Entity, "CPointWorldText", "m_Color");
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

	private CPointWorldText? Create(FloatieUpdate update, CCSPlayerPawn ownerPawn, Vector anchor)
	{
		CPointWorldText? entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
		if (entity is null)
		{
			return null;
		}

		try
		{
			entity.MessageText = update.Text;
			entity.Enabled = true;
			entity.Fullbright = true;
			entity.FontSize = settings.FontSize;
			entity.FontName = "Arial Bold";
			entity.WorldUnitsPerPx = (float)ScaleFor(Distance(ownerPawn, anchor));
			entity.Color = ColorFor(update.Highlight, 255);
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

	private Vector InFrontOf(CCSPlayerPawn pawn)
	{
		Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
		double yaw = pawn.EyeAngles.Y * Math.PI / 180;
		return new Vector(
			(float)(origin.X + (Math.Cos(yaw) * settings.SelfDistance)),
			(float)(origin.Y + (Math.Sin(yaw) * settings.SelfDistance)),
			origin.Z + pawn.ViewOffset.Z);
	}

	private void Rescale(Floatie floatie, Vector position)
	{
		if (GameState.PawnOf(Utilities.GetPlayerFromSlot(floatie.Owner)) is not CCSPlayerPawn pawn)
		{
			return;
		}

		double scale = ScaleFor(Distance(pawn, position));
		if (Math.Abs(scale - floatie.Scale) <= (floatie.Scale * RescaleThreshold))
		{
			return;
		}

		floatie.Scale = scale;
		floatie.Entity.WorldUnitsPerPx = (float)scale;
		Utilities.SetStateChanged(floatie.Entity, "CPointWorldText", "m_flWorldUnitsPerPx");
	}

	// World units per pixel proportional to distance is what holds the on-screen size constant.
	private double ScaleFor(double distance) => Math.Max(MinWorldUnitsPerPixel, distance * settings.ScreenSize);

	private static double Distance(CCSPlayerPawn pawn, Vector point)
	{
		Vector origin = pawn.AbsOrigin ?? point;
		double dx = origin.X - point.X;
		double dy = origin.Y - point.Y;
		double dz = origin.Z + pawn.ViewOffset.Z - point.Z;
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
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

	private Color ColorFor(bool highlight, int alpha)
	{
		Color baseColor = ParseColor(highlight ? settings.HighlightColor : settings.Color);
		return System.Drawing.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
	}

	private void RefreshShown() => shown = [.. active.Where(f => f.Entity.IsValid).Select(f => (f.Owner, f.Entity.Index))];

	private static void Remove(Floatie floatie)
	{
		if (floatie.Entity.IsValid)
		{
			floatie.Entity.Remove();
		}
	}

	private static Color ParseColor(string hex)
	{
		string digits = hex.TrimStart('#');
		return (digits.Length == 6) && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
			? System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
			: System.Drawing.Color.White;
	}

	private sealed class Floatie(CPointWorldText entity, int owner, int key, Vector anchor)
	{
		public CPointWorldText Entity { get; } = entity;

		public int Owner { get; } = owner;

		public int Key { get; } = key;

		public Vector Anchor { get; } = anchor;

		public double Born { get; set; }

		public bool Highlight { get; set; }

		public double Scale { get; set; }
	}
}
