// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Cards;

using System.Globalization;
using System.Text;
using SkiaSharp;

/// <summary>
/// Draws a player's share card as a PNG: the name, the rank badge, the season place, and the top weapons.
/// It is sized for an Open Graph image, so Discord and Steam show it whole.
/// </summary>
internal static class CardRenderer
{
	/// <summary>The card's width in pixels.</summary>
	public const int Width = 1200;

	/// <summary>The card's height in pixels.</summary>
	public const int Height = 630;

	private const string ServerName = "ktsu.dev CS2";
	private const float Pad = 56;
	private const float Gap = 24;
	private const float BadgeScale = 6;

	// The site's colors, from site.css.
	private static readonly SKColor Background = SKColor.Parse("#0d0f12");
	private static readonly SKColor Panel = SKColor.Parse("#15181d");
	private static readonly SKColor Track = SKColor.Parse("#262b33");
	private static readonly SKColor Text = SKColor.Parse("#d8dde3");
	private static readonly SKColor Muted = SKColor.Parse("#8a939e");
	private static readonly SKColor Gold = SKColor.Parse("#f2c94c");

	// The badge color of each rank family, as in site.js: silver, gold, blue, purple, orange, and red.
	private static readonly SKColor[] RankColors =
	[
		.. new[] { "#b9c3cd", "#b9c3cd", "#b9c3cd", "#f2c94c", "#f2c94c", "#5fa8ff", "#5fa8ff", "#5fa8ff", "#c77dff", "#c77dff", "#ff8a3d", "#ff4d6d" }.Select(SKColor.Parse),
	];

	private const int StagesPerRank = 5;
	private const string Shield = "M17 3 L31 3 L31 10 Q31 16.5 24 19 Q17 16.5 17 10 Z";
	private const string Wings = "M6 9 Q14 4 21 8 L24 12 L27 8 Q34 4 42 9 Q35 9 31 12 Q34 12 36 14 Q30 14 27 16 L24 19 L21 16 Q18 14 12 14 Q14 12 17 12 Q13 9 6 9 Z";
	private const string Crown = "M15 17 L15 7 L19.5 11 L24 4 L28.5 11 L33 7 L33 17 Z";

	private static readonly SKTypeface Regular = LoadFont("DejaVuSansMono.ttf");
	private static readonly SKTypeface Bold = LoadFont("DejaVuSansMono-Bold.ttf");

	/// <summary>
	/// Draws a card.
	/// </summary>
	/// <param name="card">What the card shows.</param>
	/// <returns>The PNG bytes.</returns>
	public static byte[] Render(PlayerCard card)
	{
		Ensure.NotNull(card);

		using SKSurface surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
		SKCanvas canvas = surface.Canvas;
		canvas.Clear(Background);

		int rank = Math.Clamp(card.Rank, 0, (RankColors.Length * StagesPerRank) - 1);
		SKColor rankColor = RankColors[rank / StagesPerRank];
		using (SKPaint accent = Fill(rankColor))
		{
			canvas.DrawRect(0, 0, Width, 8, accent);
		}

		DrawHeader(canvas, card);
		DrawText(canvas, Printable(card.Name, Bold), Pad, 172, Bold, 64, Text, Width - (2 * Pad));
		DrawRank(canvas, card, rank, rankColor);
		DrawWeapons(canvas, card);

		using SKImage image = surface.Snapshot();
		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
		return data.ToArray();
	}

	/// <summary>
	/// Gets the badge color of a rank, which the player page also gives chats as its theme color.
	/// </summary>
	/// <param name="rank">The rank index.</param>
	/// <returns>The color, such as <c>#f2c94c</c>.</returns>
	public static string RankColorHex(int rank)
	{
		SKColor color = RankColors[Math.Clamp(rank, 0, (RankColors.Length * StagesPerRank) - 1) / StagesPerRank];
		return string.Create(CultureInfo.InvariantCulture, $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}");
	}

	/// <summary>
	/// Keeps only the characters the card's font can draw, so a name never shows empty boxes.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <returns>The text the font can draw, or <see cref="PlayerCard.UnnamedPlayer"/> when that leaves nothing.</returns>
	internal static string Printable(string text) => Printable(text, Bold);

	private static string Printable(string text, SKTypeface typeface)
	{
		using SKFont font = new(typeface);
		StringBuilder kept = new();
		foreach (Rune rune in text.EnumerateRunes())
		{
			if (Rune.IsControl(rune))
			{
				continue;
			}

			if (Rune.IsWhiteSpace(rune) || (font.GetGlyph(rune.Value) != 0))
			{
				kept.Append(rune.ToString());
			}
		}

		string printable = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		return printable.Length == 0 ? PlayerCard.UnnamedPlayer : printable;
	}

	private static void DrawHeader(SKCanvas canvas, PlayerCard card)
	{
		DrawText(canvas, ServerName, Pad, 84, Bold, 28, Muted, 400);
		if (card.SeasonPlace is { } place)
		{
			string text = $"{PlayerCard.Ordinal(place)} of {PlayerCard.FormatNumber(Math.Max(place, card.SeasonPlayers))} · {card.Season}";
			DrawText(canvas, text, Width - Pad, 84, Bold, 28, place <= 3 ? Gold : Text, 640, SKTextAlign.Right);
		}
		else
		{
			DrawText(canvas, $"{card.Season} · no season XP yet", Width - Pad, 84, Regular, 24, Muted, 640, SKTextAlign.Right);
		}
	}

	private static void DrawRank(SKCanvas canvas, PlayerCard card, int rank, SKColor color)
	{
		const float top = 208;
		canvas.Save();
		canvas.Translate(Pad, top);
		canvas.Scale(BadgeScale);
		DrawBadge(canvas, rank, color);
		canvas.Restore();

		float left = Pad + (48 * BadgeScale) + 36;
		float width = Width - Pad - left;
		DrawText(canvas, card.RankName, left, top + 60, Bold, 42, color, width);
		DrawText(canvas, $"{PlayerCard.FormatNumber(card.RankXp)} rank XP", left, top + 112, Regular, 30, Text, width);

		float barTop = top + 140;
		using SKPaint track = Fill(Track);
		using SKPaint fill = Fill(color);
		canvas.DrawRoundRect(left, barTop, width, 14, 7, 7, track);
		float done = (float)Math.Clamp(card.RankProgress, 0, 1) * width;
		if (done > 0)
		{
			canvas.DrawRoundRect(left, barTop, Math.Max(done, 14), 14, 7, 7, fill);
		}
	}

	private static void DrawWeapons(SKCanvas canvas, PlayerCard card)
	{
		const float top = 418;
		const float height = 156;
		float width = (Width - (2 * Pad) - ((PlayerCard.TopWeapons - 1) * Gap)) / PlayerCard.TopWeapons;
		using SKPaint panel = Fill(Panel);
		if (card.Weapons.Count == 0)
		{
			canvas.DrawRoundRect(Pad, top, Width - (2 * Pad), height, 10, 10, panel);
			DrawText(canvas, "No weapon XP yet", Width / 2f, top + 90, Regular, 30, Muted, Width - (2 * Pad), SKTextAlign.Center);
			return;
		}

		using SKPaint track = Fill(Track);
		using SKPaint gold = Fill(Gold);
		for (int i = 0; i < card.Weapons.Count; i++)
		{
			CardWeapon weapon = card.Weapons[i];
			float left = Pad + (i * (width + Gap));
			float inner = width - 48;
			canvas.DrawRoundRect(left, top, width, height, 10, 10, panel);
			DrawText(canvas, weapon.Name, left + 24, top + 50, Bold, 32, Text, inner);
			DrawText(canvas, $"Level {weapon.Level.ToString(CultureInfo.InvariantCulture)} / {weapon.MaxLevel.ToString(CultureInfo.InvariantCulture)}", left + 24, top + 92, Regular, 26, Gold, inner);
			DrawText(canvas, $"{PlayerCard.FormatNumber(weapon.Kills)} kills", left + 24, top + 128, Regular, 24, Muted, inner);

			float levelBar = weapon.MaxLevel > 0 ? Math.Clamp((float)weapon.Level / weapon.MaxLevel, 0, 1) : 0;
			canvas.DrawRect(left + 24, top + height - 14, inner, 4, track);
			canvas.DrawRect(left + 24, top + height - 14, inner * levelBar, 4, gold);
		}
	}

	// The badge from site.js: a plate in the rank's color with the rank's emblem, and five pips that fill with the stage.
	// It draws in the same 48 by 28 units, so the canvas is scaled first.
	private static void DrawBadge(SKCanvas canvas, int rank, SKColor color)
	{
		int family = rank / StagesPerRank;
		int stage = (rank % StagesPerRank) + 1;
		using SKPaint plate = Fill(Panel);
		using SKPaint edge = Stroke(color, 1);
		using SKPaint paint = Fill(color);
		using SKPaint dark = Fill(Background);
		using SKPaint off = Fill(Track);
		canvas.DrawRoundRect(0.5f, 0.5f, 47, 27, 4, 4, plate);
		canvas.DrawRoundRect(0.5f, 0.5f, 47, 27, 4, 4, edge);

		switch (family)
		{
			case 0:
				Chevron(canvas, 7, paint);
				break;
			case 1:
				Chevron(canvas, 4, paint);
				Chevron(canvas, 10, paint);
				break;
			case 2:
				Chevron(canvas, 2, paint);
				Chevron(canvas, 7, paint);
				Chevron(canvas, 12, paint);
				break;
			case 3:
				Star(canvas, 24, 11, 8, 3.4f, paint);
				break;
			case 4:
				using (SKPaint ring = Stroke(color, 1.3f))
				{
					canvas.DrawCircle(24, 11, 8.5f, ring);
				}

				Star(canvas, 24, 11, 6, 2.6f, paint);
				break;
			case 5:
				Path(canvas, Shield, paint);
				break;
			case 6:
				Path(canvas, Shield, paint);
				Star(canvas, 24, 10, 4, 1.7f, dark);
				break;
			case 7:
				Path(canvas, Shield, paint);
				Star(canvas, 24, 10, 4, 1.7f, dark);
				Star(canvas, 11, 11, 3.5f, 1.5f, paint);
				Star(canvas, 37, 11, 3.5f, 1.5f, paint);
				break;
			case 8:
				Path(canvas, Wings, paint);
				break;
			case 9:
				Path(canvas, Wings, paint);
				Star(canvas, 24, 4.5f, 3.5f, 1.5f, paint);
				break;
			case 10:
				Path(canvas, Crown, paint);
				Star(canvas, 9, 12, 3.5f, 1.5f, paint);
				Star(canvas, 39, 12, 3.5f, 1.5f, paint);
				break;
			default:
				canvas.DrawCircle(24, 11, 8, paint);
				using (SKPaint line = Stroke(Background, 1.3f))
				{
					canvas.DrawOval(24, 11, 3.5f, 8, line);
					canvas.DrawLine(16, 11, 32, 11, line);
				}

				Star(canvas, 9, 11, 3.5f, 1.5f, paint);
				Star(canvas, 39, 11, 3.5f, 1.5f, paint);
				break;
		}

		for (int i = 0; i < StagesPerRank; i++)
		{
			canvas.DrawRoundRect(4 + (i * 8.2f), 22, 7, 3, 1, 1, i < stage ? paint : off);
		}
	}

	private static void Chevron(SKCanvas canvas, float y, SKPaint paint) =>
		Path(canvas, string.Create(CultureInfo.InvariantCulture, $"M15 {y + 6} L24 {y} L33 {y + 6} L33 {y + 9} L24 {y + 3} L15 {y + 9} Z"), paint);

	private static void Star(SKCanvas canvas, float cx, float cy, float outer, float inner, SKPaint paint)
	{
		using SKPathBuilder builder = new();
		for (int i = 0; i < 10; i++)
		{
			float radius = i % 2 == 0 ? outer : inner;
			double angle = (Math.PI / 5 * i) - (Math.PI / 2);
			SKPoint point = new(cx + (radius * (float)Math.Cos(angle)), cy + (radius * (float)Math.Sin(angle)));
			if (i == 0)
			{
				builder.MoveTo(point);
			}
			else
			{
				builder.LineTo(point);
			}
		}

		builder.Close();
		using SKPath path = builder.Detach();
		canvas.DrawPath(path, paint);
	}

	private static void Path(SKCanvas canvas, string data, SKPaint paint)
	{
		using SKPath path = SKPath.ParseSvgPathData(data);
		canvas.DrawPath(path, paint);
	}

	// Draws one line, shrinking it to fit the width down to two thirds of its size, then cutting it with an ellipsis.
	private static void DrawText(SKCanvas canvas, string text, float x, float y, SKTypeface typeface, float size, SKColor color, float maxWidth, SKTextAlign align = SKTextAlign.Left)
	{
		using SKFont font = new(typeface, size) { Subpixel = true, Edging = SKFontEdging.Antialias };
		float smallest = size * 2 / 3;
		while ((font.MeasureText(text) > maxWidth) && (font.Size > smallest))
		{
			font.Size -= 1;
		}

		string shown = text;
		if (font.MeasureText(shown) > maxWidth)
		{
			StringInfo letters = new(text);
			int count = letters.LengthInTextElements;
			while ((count > 1) && (font.MeasureText(shown) > maxWidth))
			{
				count--;
				shown = letters.SubstringByTextElements(0, count).TrimEnd() + "…";
			}
		}

		using SKPaint paint = Fill(color);
		canvas.DrawText(shown, x, y, align, font, paint);
	}

	private static SKPaint Fill(SKColor color) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

	private static SKPaint Stroke(SKColor color, float width) => new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width };

	private static SKTypeface LoadFont(string file)
	{
		using Stream stream = typeof(CardRenderer).Assembly.GetManifestResourceStream("Fonts." + file)
			?? throw new InvalidOperationException($"The font {file} isn't embedded in the site.");
		using MemoryStream bytes = new();
		stream.CopyTo(bytes);

		// The typeface keeps reading the font data, so it gets its own copy rather than the resource stream.
		using SKData data = SKData.CreateCopy(bytes.ToArray());
		return SKTypeface.FromData(data) ?? throw new InvalidOperationException($"The font {file} can't be read.");
	}
}
