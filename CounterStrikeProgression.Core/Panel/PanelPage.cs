// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Panel;

/// <summary>
/// How far along a panel line is.
/// </summary>
internal enum LineState
{
	NotStarted = 0,
	InProgress,
	Done,
	Heading,
}

/// <summary>
/// One line of a panel page.
/// </summary>
/// <param name="Label">The left-hand text, such as a challenge name and tier.</param>
/// <param name="Detail">The right-hand text, such as progress and the next reward.</param>
/// <param name="State">How far along it is, which picks its marker and color.</param>
internal sealed record PanelLine(string Label, string Detail, LineState State)
{
	/// <summary>
	/// Makes a section heading.
	/// </summary>
	/// <param name="text">The heading text.</param>
	/// <returns>The heading line.</returns>
	public static PanelLine Heading(string text) => new(text, string.Empty, LineState.Heading);
}

/// <summary>
/// One page of the progress panel: the overview, or one weapon.
/// </summary>
/// <param name="Title">The page title, such as the rank name or the weapon name.</param>
/// <param name="Subtitle">A short line under the title, such as the level.</param>
/// <param name="Progress">Progress toward the next rank or level, from 0 to 1.</param>
/// <param name="ProgressLabel">The numbers behind the progress bar.</param>
/// <param name="Lines">The body lines.</param>
internal sealed record PanelPage(string Title, string Subtitle, double Progress, string ProgressLabel, IReadOnlyList<PanelLine> Lines);
