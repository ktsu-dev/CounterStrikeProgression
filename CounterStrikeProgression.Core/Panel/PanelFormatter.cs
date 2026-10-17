// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Panel;

using System.Globalization;
using System.Net;
using System.Text;
using ktsu.CounterStrikeProgression.Core.Recap;

/// <summary>
/// Turns a panel page into the text each way of drawing the panel needs.
/// </summary>
internal static class PanelFormatter
{
	/// <summary>The width of the text progress bar, in characters.</summary>
	public const int BarWidth = 20;

	private const string Controls = "W/S scroll   A/D page   R close";

	/// <summary>
	/// Draws a progress bar as text, such as <c>[#######-------------]</c>.
	/// </summary>
	/// <param name="fraction">The progress, from 0 to 1.</param>
	/// <param name="width">The number of characters inside the brackets.</param>
	/// <returns>The bar.</returns>
	public static string Bar(double fraction, int width = BarWidth)
	{
		int filled = (int)Math.Round(Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1) * width);
		return $"[{new string('#', filled)}{new string('-', width - filled)}]";
	}

	/// <summary>
	/// Gets the window of body lines visible at a scroll position.
	/// </summary>
	/// <param name="page">The page.</param>
	/// <param name="scroll">The first visible line.</param>
	/// <param name="visibleLines">How many body lines fit.</param>
	/// <returns>The visible lines.</returns>
	public static IEnumerable<PanelLine> Visible(PanelPage page, int scroll, int visibleLines)
	{
		Ensure.NotNull(page);
		return page.Lines.Skip(ClampScroll(page, scroll, visibleLines)).Take(visibleLines);
	}

	/// <summary>
	/// Keeps a scroll position inside the page.
	/// </summary>
	/// <param name="page">The page.</param>
	/// <param name="scroll">The requested first visible line.</param>
	/// <param name="visibleLines">How many body lines fit.</param>
	/// <returns>The scroll position to use.</returns>
	public static int ClampScroll(PanelPage page, int scroll, int visibleLines)
	{
		Ensure.NotNull(page);
		return Math.Clamp(scroll, 0, Math.Max(0, page.Lines.Count - visibleLines));
	}

	/// <summary>
	/// Formats a page as plain text for the side panel, one line per row.
	/// </summary>
	/// <param name="page">The page.</param>
	/// <param name="pageNumber">The page's position, starting at 1.</param>
	/// <param name="pageCount">The number of pages.</param>
	/// <param name="scroll">The first visible body line.</param>
	/// <param name="visibleLines">How many body lines fit.</param>
	/// <param name="maxBytes">The most UTF-8 bytes the text may take. Body lines that don't fit are left off.</param>
	/// <returns>The panel text.</returns>
	public static string ToPlainText(PanelPage page, int pageNumber, int pageCount, int scroll, int visibleLines, int maxBytes = int.MaxValue)
	{
		Ensure.NotNull(page);

		string header = $"{page.Title.ToUpperInvariant()}   {page.Subtitle}\n{Bar(page.Progress)} {page.ProgressLabel}\n\n";
		string footer = string.Format(CultureInfo.InvariantCulture, "\n{0}/{1}   {2}", pageNumber, pageCount, Controls);
		int budget = maxBytes - Encoding.UTF8.GetByteCount(header) - Encoding.UTF8.GetByteCount(footer);

		StringBuilder text = new(header);
		foreach (PanelLine line in Visible(page, scroll, visibleLines))
		{
			string row = (line.State == LineState.Heading
				? $"-- {line.Label.ToUpperInvariant()} --"
				: $"{Marker(line.State)} {line.Label}   {line.Detail}") + "\n";
			int bytes = Encoding.UTF8.GetByteCount(row);
			if (bytes > budget)
			{
				break;
			}

			budget -= bytes;
			text.Append(row);
		}

		return text.Append(footer).ToString();
	}

	/// <summary>
	/// Formats a page as the HTML CS2 shows in the center of the screen, which only fits a few lines.
	/// </summary>
	/// <param name="page">The page.</param>
	/// <param name="pageNumber">The page's position, starting at 1.</param>
	/// <param name="pageCount">The number of pages.</param>
	/// <param name="scroll">The first visible body line.</param>
	/// <param name="visibleLines">How many body lines fit.</param>
	/// <returns>The HTML.</returns>
	public static string ToCenterHtml(PanelPage page, int pageNumber, int pageCount, int scroll, int visibleLines)
	{
		Ensure.NotNull(page);

		StringBuilder html = new();
		html.Append("<font color='#f2c94c'><b>").Append(Encode(page.Title.ToUpperInvariant())).Append("</b></font> ")
			.Append("<font color='#8a949e'>").Append(Encode(page.Subtitle)).Append("</font><br>");
		html.Append("<font color='#6fcf97'>").Append(Bar(page.Progress, 12)).Append("</font> ")
			.Append("<font color='#8a949e'>").Append(Encode(page.ProgressLabel)).Append("</font><br>");
		foreach (PanelLine line in Visible(page, scroll, visibleLines))
		{
			if (line.State == LineState.Heading)
			{
				html.Append("<font color='#8a949e'>").Append(Encode(line.Label.ToUpperInvariant())).Append("</font><br>");
				continue;
			}

			html.Append("<font color='").Append(Color(line.State)).Append("'>").Append(Marker(line.State)).Append("</font> ")
				.Append(Encode(line.Label)).Append(" <font color='#8a949e'>").Append(Encode(line.Detail)).Append("</font><br>");
		}

		html.Append(string.Format(CultureInfo.InvariantCulture, "<font color='#8a949e'>{0}/{1}  {2}</font>", pageNumber, pageCount, Controls));
		return html.ToString();
	}

	/// <summary>
	/// Formats a page as chat lines, for when nothing else can be drawn.
	/// </summary>
	/// <param name="page">The page.</param>
	/// <returns>The lines.</returns>
	public static IReadOnlyList<string> ToChatLines(PanelPage page)
	{
		Ensure.NotNull(page);

		List<string> lines = [$"{page.Title} ({page.Subtitle}) {Bar(page.Progress, 10)} {page.ProgressLabel}"];
		lines.AddRange(page.Lines.Where(l => l.State != LineState.Heading).Select(l => $" {Marker(l.State)} {l.Label}  {l.Detail}"));
		return lines;
	}

	/// <summary>
	/// Formats a map recap as one chat line, leaving out what didn't happen.
	/// </summary>
	/// <param name="recap">The recap.</param>
	/// <returns>The line, such as <c>This map: +1,240 XP · 18/9 K/D · 4 skill shots · 2 levels · best: Wallbang with the AK-47</c>.</returns>
	public static string ToRecapChat(MapRecap recap)
	{
		Ensure.NotNull(recap);

		List<string> parts = [string.Format(CultureInfo.InvariantCulture, "+{0:N0} XP", recap.Xp)];
		if ((recap.Kills > 0) || (recap.Deaths > 0))
		{
			parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}/{1} K/D", recap.Kills, recap.Deaths));
		}

		AddCount(parts, recap.SkillShots, "skill shot", "skill shots");
		AddCount(parts, recap.Levels, "level", "levels");
		AddCount(parts, recap.Tiers, "tier", "tiers");
		AddCount(parts, recap.RankUps, "rank up", "rank ups");
		AddCount(parts, recap.Feats, "feat", "feats");
		if (!string.IsNullOrEmpty(recap.Best))
		{
			parts.Add($"best: {recap.Best}");
		}

		return "This map: " + string.Join(" · ", parts);
	}

	private static void AddCount(List<string> parts, int count, string one, string many)
	{
		if (count > 0)
		{
			parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", count, count == 1 ? one : many));
		}
	}

	private static string Marker(LineState state) => state switch
	{
		LineState.Done => "+",
		LineState.InProgress => ">",
		_ => "o",
	};

	private static string Color(LineState state) => state switch
	{
		LineState.Done => "#6fcf97",
		LineState.InProgress => "#f2994a",
		_ => "#8a949e",
	};

	private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
