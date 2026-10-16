// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Panel;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Panel;
using Microsoft.Extensions.Logging;

/// <summary>
/// Opens, navigates, draws, and closes each player's progress panel. Drawing steps down from the side panel
/// to the center card to chat, per player, whenever a way of drawing fails.
/// </summary>
/// <param name="settings">How the panel is drawn.</param>
/// <param name="builder">Builds the pages.</param>
/// <param name="logger">Where failures are reported.</param>
/// <param name="chatPrefix">The chat prefix.</param>
internal sealed class PanelPresenter(PanelSettings settings, PanelBuilder builder, ILogger logger, string chatPrefix)
{
	private const double CenterRefreshSeconds = 0.5;

	/// <summary>Gets the side panel entities.</summary>
	public WorldTextPanel WorldText { get; } = new(settings);

	/// <summary>
	/// Opens the panel on the overview, or closes it when it's already open.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	public void Toggle(CCSPlayerController player, PlayerSession session)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(session);

		if (session.Panel.IsOpen)
		{
			Close(player, session);
			return;
		}

		if (session.Progress is null)
		{
			player.PrintToChat($" {ChatColors.Gold}{chatPrefix}{ChatColors.Default} Your progress is still loading.");
			return;
		}

		session.Panel.IsOpen = true;
		session.Panel.Page = 0;
		session.Panel.Scroll = 0;
		session.Panel.IsStale = true;
		session.Panel.LastButtons = player.Buttons;
		if (session.Panel.Mode == PanelMode.Auto)
		{
			session.Panel.Mode = settings.Mode;
		}

		if (EffectiveMode(session) == PanelMode.Chat)
		{
			PrintToChat(player, session);
			session.Panel.IsOpen = false;
		}
	}

	/// <summary>
	/// Closes a player's panel.
	/// </summary>
	/// <param name="player">The player, if still connected.</param>
	/// <param name="session">The player's session.</param>
	public void Close(CCSPlayerController? player, PlayerSession session)
	{
		Ensure.NotNull(session);

		if (!session.Panel.IsOpen)
		{
			return;
		}

		session.Panel.IsOpen = false;
		WorldText.Hide(session.Slot);
		if ((player is not null) && player.IsValid && (EffectiveMode(session) == PanelMode.CenterHtml))
		{
			player.PrintToCenterHtml(" ", 1);
		}
	}

	/// <summary>
	/// Marks a player's panel for redrawing, after their progress changed.
	/// </summary>
	/// <param name="session">The player's session.</param>
	public static void Invalidate(PlayerSession session)
	{
		Ensure.NotNull(session);
		session.Panel.IsStale = true;
	}

	/// <summary>
	/// Handles input and draws a player's open panel. Called every tick.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="session">The player's session.</param>
	public void Tick(CCSPlayerController player, PlayerSession session)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(session);

		PanelState panel = session.Panel;
		if (!panel.IsOpen || (session.Progress is null))
		{
			return;
		}

		CCSPlayerPawn? pawn = GameState.PawnOf(player);
		if ((pawn is null) || !player.PawnIsAlive)
		{
			Close(player, session);
			return;
		}

		HandleInput(player, session);
		if (!panel.IsOpen)
		{
			return;
		}

		PanelMode mode = EffectiveMode(session);
		int visibleLines = mode == PanelMode.CenterHtml ? settings.CenterVisibleLines : settings.VisibleLines;
		bool changed = false;
		if (panel.IsStale)
		{
			PanelPage page = builder.Page(session.Progress, panel.Page);
			panel.Scroll = PanelFormatter.ClampScroll(page, panel.Scroll, visibleLines);
			string text = mode == PanelMode.CenterHtml
				? PanelFormatter.ToCenterHtml(page, panel.Page + 1, PanelBuilder.PageCount, panel.Scroll, visibleLines)
				: PanelFormatter.ToPlainText(page, panel.Page + 1, PanelBuilder.PageCount, panel.Scroll, visibleLines, WorldTextPanel.MaxTextBytes);
			changed = !string.Equals(text, panel.Text, StringComparison.Ordinal);
			panel.Text = text;
			panel.IsStale = false;
		}

		Draw(player, pawn, session, mode, changed);
	}

	private void HandleInput(CCSPlayerController player, PlayerSession session)
	{
		PanelState panel = session.Panel;
		PlayerButtons buttons = player.Buttons;
		PlayerButtons pressed = buttons & ~panel.LastButtons;
		panel.LastButtons = buttons;
		if (pressed == 0)
		{
			return;
		}

		if (pressed.HasFlag(PlayerButtons.Reload))
		{
			Close(player, session);
			return;
		}

		int pageCount = PanelBuilder.PageCount;
		if (pressed.HasFlag(PlayerButtons.Moveleft))
		{
			panel.Page = (panel.Page - 1 + pageCount) % pageCount;
			panel.Scroll = 0;
			panel.IsStale = true;
		}

		if (pressed.HasFlag(PlayerButtons.Moveright))
		{
			panel.Page = (panel.Page + 1) % pageCount;
			panel.Scroll = 0;
			panel.IsStale = true;
		}

		if (pressed.HasFlag(PlayerButtons.Forward))
		{
			panel.Scroll--;
			panel.IsStale = true;
		}

		if (pressed.HasFlag(PlayerButtons.Back))
		{
			panel.Scroll++;
			panel.IsStale = true;
		}
	}

	private void Draw(CCSPlayerController player, CCSPlayerPawn pawn, PlayerSession session, PanelMode mode, bool changed)
	{
		if (mode == PanelMode.WorldText)
		{
			try
			{
				WorldText.Show(player, pawn, session.Panel.Text, changed);
				return;
			}
			catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException or ArgumentException or NotSupportedException)
			{
				// One player's broken panel must not take the tick down with it.
				WorldText.Hide(session.Slot);
				PanelMode fallback = settings.Mode == PanelMode.WorldText ? PanelMode.Chat : PanelMode.CenterHtml;
				Log.PanelFallback(logger, player.PlayerName, fallback.ToString(), exception);
				session.Panel.Mode = fallback;
				session.Panel.IsStale = true;
				return;
			}
		}

		if (mode == PanelMode.CenterHtml)
		{
			// Each send is a network message, so it goes out when the text changes and otherwise just often
			// enough to keep the one-second card from fading.
			double now = Server.CurrentTime;
			if (changed || ((now - session.Panel.LastCenterSend) >= CenterRefreshSeconds))
			{
				player.PrintToCenterHtml(session.Panel.Text, 1);
				session.Panel.LastCenterSend = now;
			}

			return;
		}

		PrintToChat(player, session);
		session.Panel.IsOpen = false;
	}

	private void PrintToChat(CCSPlayerController player, PlayerSession session)
	{
		PanelPage page = builder.Page(session.Progress!, session.Panel.Page);
		foreach (string line in PanelFormatter.ToChatLines(page).Take(settings.VisibleLines))
		{
			player.PrintToChat($" {ChatColors.Gold}{chatPrefix}{ChatColors.Default} {line}");
		}
	}

	private static PanelMode EffectiveMode(PlayerSession session) =>
		session.Panel.Mode == PanelMode.Auto ? PanelMode.WorldText : session.Panel.Mode;
}
