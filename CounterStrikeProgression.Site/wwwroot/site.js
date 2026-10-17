"use strict";

(() => {
	const SERVER_NAME = "ktsu.dev CS2";

	// Matches RankLadder.RankNames and RankLadder.Names in CounterStrikeProgression.Core, because the live
	// snapshot sends only the rank index.
	const RANK_BASE_NAMES = [
		"Silver",
		"Silver Elite",
		"Silver Elite Master",
		"Gold Nova",
		"Gold Nova Master",
		"Master Guardian",
		"Master Guardian Elite",
		"Distinguished Master Guardian",
		"Legendary Eagle",
		"Legendary Eagle Master",
		"Supreme Master First Class",
		"Global Elite",
	];
	const STAGES_PER_RANK = 5;
	const STAGE_NUMERALS = ["I", "II", "III", "IV", "V"];
	const RANK_NAMES = RANK_BASE_NAMES.flatMap((name) => STAGE_NUMERALS.map((stage) => `${name} · ${stage}`));

	// The badge color of each rank, by family: silver, gold, blue, purple, orange, and red.
	const RANK_COLORS = [
		"#b9c3cd", "#b9c3cd", "#b9c3cd",
		"#f2c94c", "#f2c94c",
		"#5fa8ff", "#5fa8ff", "#5fa8ff",
		"#c77dff", "#c77dff",
		"#ff8a3d",
		"#ff4d6d",
	];

	// Matches FeatCatalog.All in CounterStrikeProgression.Core, for the names on icons outside the player page.
	const FEATS = {
		global_elite: "Global Elite",
		millionaire: "Millionaire",
		weapon_master: "Weapon master",
		epic_ladder: "Epic ladder",
		full_ladder: "Full ladder",
		collector: "Collector",
		chicken_overlord: "Chicken overlord",
		chicken_connoisseur: "Chicken connoisseur",
	};

	const FEED_LIMIT = 50;
	const REFETCH_EVERY_MS = 2000;
	const STREAM_GRACE_MS = 15000;
	const STEAM_ID = /^\d{17}$/;
	const GOLD_KINDS = new Set(["RankUp", "WeaponLevelUp", "TierCompleted", "FeatEarned"]);
	const ROMAN = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

	function el(tag, className, text) {
		const node = document.createElement(tag);
		if (className) {
			node.className = className;
		}
		if (text !== undefined && text !== null) {
			node.textContent = String(text);
		}
		return node;
	}

	function formatXp(value) {
		const number = Number(value);
		return Number.isFinite(number) ? Math.round(number).toLocaleString("en-US") : "—";
	}

	function formatPercent(value) {
		const number = Number(value);
		return Number.isFinite(number) ? `${Math.round(number)}%` : "—";
	}

	function ago(date, online) {
		if (online) {
			return "online";
		}
		const then = new Date(date);
		const ms = Date.now() - then.getTime();
		if (!Number.isFinite(ms)) {
			return "—";
		}
		const minutes = Math.floor(ms / 60000);
		if (minutes < 1) {
			return "just now";
		}
		if (minutes < 60) {
			return `${minutes}m ago`;
		}
		const hours = Math.floor(minutes / 60);
		if (hours < 24) {
			return `${hours}h ago`;
		}
		const days = Math.floor(hours / 24);
		if (days <= 30) {
			return `${days}d ago`;
		}
		return formatDate(then);
	}

	function formatDate(date) {
		const value = new Date(date);
		return Number.isFinite(value.getTime())
			? value.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })
			: "—";
	}

	function rankName(index) {
		return RANK_NAMES[index] ?? `Rank ${Number(index) + 1}`;
	}

	const SVG = "http://www.w3.org/2000/svg";

	function svgNode(tag, attributes) {
		const node = document.createElementNS(SVG, tag);
		for (const [name, value] of Object.entries(attributes)) {
			if (name === "textContent") {
				node.textContent = String(value);
			} else {
				node.setAttribute(name, String(value));
			}
		}
		return node;
	}

	function starPoints(cx, cy, outer, inner) {
		const points = [];
		for (let i = 0; i < 10; i++) {
			const radius = i % 2 === 0 ? outer : inner;
			const angle = (Math.PI / 5) * i - Math.PI / 2;
			points.push(`${(cx + radius * Math.cos(angle)).toFixed(2)},${(cy + radius * Math.sin(angle)).toFixed(2)}`);
		}
		return points.join(" ");
	}

	function chevron(y, color) {
		return svgNode("path", { d: `M15 ${y + 6} L24 ${y} L33 ${y + 6} L33 ${y + 9} L24 ${y + 3} L15 ${y + 9} Z`, fill: color });
	}

	const SHIELD = "M17 3 L31 3 L31 10 Q31 16.5 24 19 Q17 16.5 17 10 Z";
	const WINGS = "M6 9 Q14 4 21 8 L24 12 L27 8 Q34 4 42 9 Q35 9 31 12 Q34 12 36 14 Q30 14 27 16 L24 19 L21 16 Q18 14 12 14 Q14 12 17 12 Q13 9 6 9 Z";
	const DARK = "#0d0f12";

	// One emblem for each of the 12 ranks, drawn in the rank's color in the badge's upper area.
	const RANK_EMBLEMS = [
		(c) => [chevron(7, c)],
		(c) => [chevron(4, c), chevron(10, c)],
		(c) => [chevron(2, c), chevron(7, c), chevron(12, c)],
		(c) => [svgNode("polygon", { points: starPoints(24, 11, 8, 3.4), fill: c })],
		(c) => [
			svgNode("circle", { cx: 24, cy: 11, r: 8.5, fill: "none", stroke: c, "stroke-width": 1.3 }),
			svgNode("polygon", { points: starPoints(24, 11, 6, 2.6), fill: c }),
		],
		(c) => [svgNode("path", { d: SHIELD, fill: c })],
		(c) => [svgNode("path", { d: SHIELD, fill: c }), svgNode("polygon", { points: starPoints(24, 10, 4, 1.7), fill: DARK })],
		(c) => [
			svgNode("path", { d: SHIELD, fill: c }),
			svgNode("polygon", { points: starPoints(24, 10, 4, 1.7), fill: DARK }),
			svgNode("polygon", { points: starPoints(11, 11, 3.5, 1.5), fill: c }),
			svgNode("polygon", { points: starPoints(37, 11, 3.5, 1.5), fill: c }),
		],
		(c) => [svgNode("path", { d: WINGS, fill: c })],
		(c) => [svgNode("path", { d: WINGS, fill: c }), svgNode("polygon", { points: starPoints(24, 4.5, 3.5, 1.5), fill: c })],
		(c) => [
			svgNode("path", { d: "M15 17 L15 7 L19.5 11 L24 4 L28.5 11 L33 7 L33 17 Z", fill: c }),
			svgNode("polygon", { points: starPoints(9, 12, 3.5, 1.5), fill: c }),
			svgNode("polygon", { points: starPoints(39, 12, 3.5, 1.5), fill: c }),
		],
		(c) => [
			svgNode("circle", { cx: 24, cy: 11, r: 8, fill: c }),
			svgNode("ellipse", { cx: 24, cy: 11, rx: 3.5, ry: 8, fill: "none", stroke: DARK, "stroke-width": 1.3 }),
			svgNode("line", { x1: 16, y1: 11, x2: 32, y2: 11, stroke: DARK, "stroke-width": 1.3 }),
			svgNode("polygon", { points: starPoints(9, 11, 3.5, 1.5), fill: c }),
			svgNode("polygon", { points: starPoints(39, 11, 3.5, 1.5), fill: c }),
		],
	];

	// A plate in the rank's color with the rank's own emblem, and five pips along the bottom that fill with the stage.
	function badgeSvg(index) {
		const rank = Math.max(0, Math.min(RANK_NAMES.length - 1, Math.floor(Number(index) || 0)));
		const base = Math.floor(rank / STAGES_PER_RANK);
		const stage = (rank % STAGES_PER_RANK) + 1;
		const color = RANK_COLORS[base] ?? RANK_COLORS[0];
		const svg = svgNode("svg", { viewBox: "0 0 48 28", role: "img", "aria-hidden": "true" });
		svg.append(svgNode("rect", { x: 0.5, y: 0.5, width: 47, height: 27, rx: 4, fill: "#15181d", stroke: color, "stroke-width": 1 }));
		svg.append(...RANK_EMBLEMS[base](color));
		for (let i = 0; i < STAGES_PER_RANK; i++) {
			svg.append(svgNode("rect", {
				x: 4 + i * 8.2,
				y: 22,
				width: 7,
				height: 3,
				rx: 1,
				fill: i < stage ? color : "#262b33",
			}));
		}
		return svg;
	}

	// One glyph for each feat, drawn inside a round medallion.
	const FEAT_GLYPHS = {
		global_elite: (c) => [
			svgNode("circle", { cx: 14, cy: 14, r: 6.5, fill: "none", stroke: c, "stroke-width": 1.5 }),
			svgNode("ellipse", { cx: 14, cy: 14, rx: 2.8, ry: 6.5, fill: "none", stroke: c, "stroke-width": 1.2 }),
			svgNode("line", { x1: 7.5, y1: 14, x2: 20.5, y2: 14, stroke: c, "stroke-width": 1.2 }),
		],
		millionaire: (c) => [svgNode("text", { x: 14, y: 17.5, "text-anchor": "middle", "font-size": 9, "font-weight": "bold", "font-family": "Consolas, monospace", fill: c, textContent: "1M" })],
		weapon_master: (c) => [
			svgNode("circle", { cx: 14, cy: 14, r: 5.5, fill: "none", stroke: c, "stroke-width": 1.5 }),
			svgNode("path", { d: "M14 5 L14 10 M14 18 L14 23 M5 14 L10 14 M18 14 L23 14", stroke: c, "stroke-width": 1.5 }),
		],
		epic_ladder: (c) => [svgNode("path", { d: "M16 5 L9 15 L13.5 15 L11.5 23 L19 12 L14.5 12 Z", fill: c })],
		full_ladder: (c) => [svgNode("path", { d: "M10 6 L10 22 M18 6 L18 22 M10 9 L18 9 M10 13 L18 13 M10 17 L18 17 M10 21 L18 21", stroke: c, "stroke-width": 1.5, fill: "none" })],
		collector: (c) => {
			const dots = [];
			for (let row = 0; row < 3; row++) {
				for (let column = 0; column < 3; column++) {
					dots.push(svgNode("circle", { cx: 9.5 + column * 4.5, cy: 9.5 + row * 4.5, r: 1.6, fill: c }));
				}
			}
			return dots;
		},
		chicken_overlord: (c) => [svgNode("path", { d: "M7 19 L7 10 L11 13.5 L14 7 L17 13.5 L21 10 L21 19 Z", fill: c })],
		chicken_connoisseur: (c) => [
			svgNode("ellipse", { cx: 11.5, cy: 11.5, rx: 5, ry: 4.2, transform: "rotate(-45 11.5 11.5)", fill: c }),
			svgNode("path", { d: "M14.5 14.5 L19.5 19.5", stroke: c, "stroke-width": 2.2, "stroke-linecap": "round" }),
			svgNode("circle", { cx: 20.5, cy: 18.5, r: 1.6, fill: c }),
			svgNode("circle", { cx: 18.5, cy: 20.5, r: 1.6, fill: c }),
		],
	};

	// A challenge's icon, drawn in the text color. The markup comes from icons.js, which the site ships itself.
	function challengeIcon(id) {
		const markup = (window.CHALLENGE_ICONS ?? {})[id];
		if (!markup) {
			return null;
		}
		const svg = svgNode("svg", {
			viewBox: "0 0 24 24",
			class: "challenge-icon",
			fill: "none",
			stroke: "currentColor",
			"stroke-width": 2,
			"stroke-linecap": "round",
			"stroke-linejoin": "round",
			"aria-hidden": "true",
		});
		svg.innerHTML = markup;
		return svg;
	}

	// An earned feat is gold, a locked one grey.
	function featIcon(id, earned, title) {
		const color = earned ? "#f2c94c" : "#4a515b";
		const svg = svgNode("svg", { viewBox: "0 0 28 28", class: earned ? "feat-icon earned" : "feat-icon", role: "img" });
		const label = svgNode("title", {});
		label.textContent = title ?? FEATS[id] ?? id;
		svg.append(label, svgNode("circle", { cx: 14, cy: 14, r: 13, fill: earned ? "#2a2410" : "#15181d", stroke: color, "stroke-width": 1.4 }));
		svg.append(...(FEAT_GLYPHS[id] ?? (() => []))(color));
		return svg;
	}

	function featIcons(ids) {
		const span = el("span", "feat-icons");
		for (const id of Array.isArray(ids) ? ids : []) {
			span.append(featIcon(id, true));
		}
		return span;
	}

	function rankBadge(index, name, big = false) {
		const badge = el("span", big ? "badge big" : "badge");
		badge.title = name ?? rankName(index);
		badge.append(badgeSvg(index), el("span", "rank", name ?? rankName(index)));
		return badge;
	}

	function playerLink(steamId, name) {
		const id = String(steamId ?? "");
		if (!STEAM_ID.test(id)) {
			return el("span", null, name);
		}
		const link = el("a", null, name);
		link.href = `/players/${id}`;
		return link;
	}

	function cell(className, content) {
		const td = el("td", className);
		if (content instanceof Node) {
			td.append(content);
		} else if (content !== undefined && content !== null) {
			td.textContent = String(content);
		}
		return td;
	}

	function headerRow(columns) {
		const tr = el("tr");
		for (const [label, className] of columns) {
			tr.append(el("th", className, label));
		}
		const thead = el("thead");
		thead.append(tr);
		return thead;
	}

	function table(columns, rows) {
		const wrap = el("div", "table-wrap");
		const tableNode = el("table");
		const tbody = el("tbody");
		tbody.append(...rows);
		tableNode.append(headerRow(columns), tbody);
		wrap.append(tableNode);
		return wrap;
	}

	function bar(fraction, className) {
		const outer = el("div", className ? `bar ${className}` : "bar");
		const inner = el("i");
		const clamped = Math.min(1, Math.max(0, Number(fraction) || 0));
		inner.style.width = `${(clamped * 100).toFixed(1)}%`;
		outer.append(inner);
		return outer;
	}

	function message(text) {
		return el("div", "message", text);
	}

	async function getJson(url) {
		const response = await fetch(url, { headers: { Accept: "application/json" }, cache: "no-store" });
		if (!response.ok) {
			const error = new Error(`HTTP ${response.status}`);
			error.status = response.status;
			throw error;
		}
		return response.json();
	}

	// Runs at once when idle, otherwise once more at the end of the window, so a burst of events costs at most one fetch per window.
	function throttle(fn, windowMs) {
		let last = 0;
		let timer = null;
		return () => {
			if (timer !== null) {
				return;
			}
			const wait = Math.max(0, last + windowMs - Date.now());
			timer = setTimeout(() => {
				timer = null;
				last = Date.now();
				fn();
			}, wait);
		};
	}

	const listeners = new Map();
	const statusListeners = [];
	let stream = null;
	let streamDownSince = null;
	let hadError = false;
	let statusTimer = null;

	function connect() {
		if (stream !== null) {
			return;
		}
		stream = new EventSource("/api/stream");
		stream.addEventListener("open", () => {
			const reconnected = hadError;
			streamDownSince = null;
			hadError = false;
			notifyStatus(reconnected);
		});
		stream.addEventListener("error", () => {
			hadError = true;
			if (streamDownSince === null) {
				streamDownSince = Date.now();
			}
			// The browser stops retrying after a non-200 answer, such as a 502 from the proxy during a deploy.
			if (stream.readyState === EventSource.CLOSED) {
				stream.close();
				stream = null;
				setTimeout(connect, 5000);
			}
			notifyStatus(false);
		});
		for (const name of ["snapshot", "feed", "progress", "online"]) {
			stream.addEventListener(name, (event) => {
				let data;
				try {
					data = JSON.parse(event.data);
				} catch {
					return;
				}
				for (const handler of listeners.get(name) ?? []) {
					handler(data);
				}
			});
		}
		if (statusTimer === null) {
			statusTimer = setInterval(() => notifyStatus(false), 1000);
		}
	}

	function on(name, handler) {
		if (!listeners.has(name)) {
			listeners.set(name, []);
		}
		listeners.get(name).push(handler);
		connect();
	}

	function onStatus(handler) {
		statusListeners.push(handler);
		connect();
	}

	function streamLost() {
		return streamDownSince !== null && Date.now() - streamDownSince > STREAM_GRACE_MS;
	}

	function notifyStatus(reconnected) {
		for (const handler of statusListeners) {
			handler({ lost: streamLost(), reconnected });
		}
	}

	// The player page leaves the name out, because every line is the same player.
	function feedItem(entry, showPlayer = true) {
		const li = el("li", GOLD_KINDS.has(entry.kind) ? "gold" : null);
		const what = el("span", "what");
		if (entry.kind === "FeatEarned") {
			const id = Object.keys(FEATS).find((key) => FEATS[key] === entry.title);
			if (id) {
				what.append(featIcon(id, true), " ");
			}
		}
		const icon = entry.challengeId ? challengeIcon(entry.challengeId) : null;
		if (icon) {
			what.append(icon, " ");
		}
		if (showPlayer) {
			what.append(playerLink(entry.steamId, entry.playerName), " · ");
		}
		what.append(entry.title);
		// A weapon level-up's title already names the weapon.
		if (entry.weapon && entry.kind !== "WeaponLevelUp") {
			what.append(` · ${entry.weapon}`);
		}
		if (entry.caption) {
			what.append(el("span", "caption", entry.caption));
		}
		const side = el("span", "side");
		if (Number(entry.xp) > 0) {
			side.append(el("span", "xp", `+${formatXp(entry.xp)}`));
		}
		const when = el("time", "when", ago(entry.at));
		when.dateTime = String(entry.at);
		side.append(when);
		li.append(what, side);
		return li;
	}

	function initLive(root) {
		const top = el("div", "top");
		const grid = el("div", "live-grid");
		const boardBox = el("section");
		const feedBox = el("section");
		const feedList = el("ul", "feed");
		boardBox.append(el("h2", "section-title", "Scoreboard"));
		feedBox.append(el("h2", "section-title", "Feed"), feedList);
		const board = el("div");
		boardBox.append(board);
		grid.append(boardBox, feedBox);
		const panel = el("div", "panel");
		panel.append(top, grid);
		root.replaceChildren(panel);

		let serverOnline = false;
		let snapshot = null;
		let feats = {};
		let feed = [];

		function renderTop() {
			const offline = !serverOnline || streamLost();
			top.classList.toggle("offline", offline);
			const left = el("span");
			left.append(el("b", null, offline ? "OFFLINE" : "LIVE"));
			if (snapshot) {
				left.append(` · ${snapshot.map} · ${snapshot.gameMode}`);
			}
			const right = el("span", null, snapshot ? `${snapshot.players.length}/${snapshot.maxPlayers}` : "");
			top.replaceChildren(left, right);
		}

		function renderBoard() {
			if (!serverOnline) {
				board.replaceChildren(message("The server is offline."));
				return;
			}
			if (!snapshot || snapshot.players.length === 0) {
				board.replaceChildren(message(snapshot ? "Nobody is playing." : "Waiting for the server."));
				return;
			}
			const players = [...snapshot.players].sort((a, b) => (b.kills - a.kills) || (a.deaths - b.deaths));
			const rows = players.map((player) => {
				const tr = el("tr");
				const name = player.isBot ? el("span", null, player.name) : playerLink(player.steamId, player.name);
				const nameCell = el("span");
				nameCell.append(name);
				if (!player.isBot && feats[String(player.steamId)]) {
					nameCell.append(featIcons(feats[String(player.steamId)]));
				}
				const badge = player.isBot ? el("span", "rank bot", "bot") : rankBadge(player.rank);
				tr.append(
					cell("name", nameCell),
					cell(null, badge),
					cell("num", player.kills),
					cell("num", player.deaths),
					cell("num", player.isBot ? "—" : formatXp(player.mapXp)),
				);
				return tr;
			});
			board.replaceChildren(table([["Player"], ["Rank"], ["K", "num"], ["D", "num"], ["XP", "num"]], rows));
		}

		function feedKey(entry) {
			return `${entry.at}|${entry.steamId}|${entry.kind}|${entry.title}`;
		}

		function renderFeed() {
			if (feed.length === 0) {
				feedList.replaceChildren(el("li", null, "Nothing yet."));
				return;
			}
			feedList.replaceChildren(...feed.map((entry) => feedItem(entry)));
		}

		function atTime(entry) {
			const time = new Date(entry.at).getTime();
			return Number.isFinite(time) ? time : 0;
		}

		// Keeps entries that arrived over the stream while the page loaded, so none is lost to the fetch.
		function mergeFeed(fetched) {
			const seen = new Set();
			const merged = [];
			for (const entry of [...fetched, ...feed]) {
				const key = feedKey(entry);
				if (!seen.has(key)) {
					seen.add(key);
					merged.push(entry);
				}
			}
			merged.sort((a, b) => atTime(b) - atTime(a));
			return merged.slice(0, FEED_LIMIT);
		}

		function addFeed(entry) {
			const key = feedKey(entry);
			if (feed.some((existing) => feedKey(existing) === key)) {
				return;
			}
			feed.unshift(entry);
			feed = feed.slice(0, FEED_LIMIT);
			renderFeed();
		}

		async function load() {
			try {
				const live = await getJson("/api/live");
				serverOnline = Boolean(live.online);
				snapshot = live.snapshot ?? null;
				feed = mergeFeed(Array.isArray(live.feed) ? live.feed : []);
				feats = live.feats && typeof live.feats === "object" ? live.feats : {};
			} catch {
				serverOnline = false;
			}
			renderTop();
			renderBoard();
			renderFeed();
		}

		on("snapshot", (data) => {
			snapshot = data;
			renderTop();
			renderBoard();
		});
		on("feed", (entry) => {
			addFeed(entry);
			const id = Object.keys(FEATS).find((key) => FEATS[key] === entry.title);
			if (entry.kind === "FeatEarned" && id) {
				const steamId = String(entry.steamId);
				const ids = new Set(feats[steamId] ?? []);
				ids.add(id);
				feats[steamId] = Object.keys(FEATS).filter((key) => ids.has(key));
				renderBoard();
			}
		});
		on("online", (data) => {
			serverOnline = data === true;
			renderTop();
			renderBoard();
		});
		onStatus(({ reconnected }) => {
			if (reconnected) {
				load();
			} else {
				renderTop();
			}
		});
		setInterval(renderFeed, 30000);
		load();
	}

	function initRanks(root) {
		const top = el("div", "top");
		const body = el("div");
		const panel = el("div", "panel");
		panel.append(top, body);
		root.replaceChildren(panel);
		top.append(el("b", null, "Ranks"));

		let ids = new Set();
		let onlineIds = new Set();
		// Players on the server but not on the page that already caused a refetch. A player with no progress yet
		// stays off the page, so this stops every snapshot from refetching for them.
		const sought = new Set();

		function render(rows) {
			const count = el("span", null, `${rows.length} ${rows.length === 1 ? "player" : "players"}`);
			const title = el("span");
			title.append(el("b", null, "Ranks"));
			top.replaceChildren(title, count);
			ids = new Set(rows.map((row) => String(row.steamId)));
			onlineIds = new Set(rows.filter((row) => row.online).map((row) => String(row.steamId)));
			if (rows.length === 0) {
				body.replaceChildren(message("No players yet."));
				return;
			}
			const trs = rows.map((row) => {
				const tr = el("tr");
				tr.append(
					cell("num", row.position),
					cell("name", (() => {
						const span = el("span");
						span.append(playerLink(row.steamId, row.name), featIcons(row.feats));
						return span;
					})()),
					cell(null, rankBadge(row.rank, row.rankName)),
					cell("num", formatXp(row.rankXp)),
					cell(row.online ? "online" : null, ago(row.lastSeen, row.online)),
				);
				return tr;
			});
			body.replaceChildren(table([["#", "num"], ["Player"], ["Rank"], ["XP", "num"], ["Last seen"]], trs));
		}

		async function load() {
			try {
				render(await getJson("/api/ranks"));
			} catch {
				body.replaceChildren(message("The ranks can't be loaded right now."));
			}
		}

		const refetch = throttle(load, REFETCH_EVERY_MS);
		on("progress", (changed) => {
			if (Array.isArray(changed) && changed.length > 0) {
				refetch();
			}
		});
		on("snapshot", (snapshot) => {
			const present = new Set(snapshot.players.filter((p) => !p.isBot).map((p) => String(p.steamId)).filter((id) => id !== "0"));
			const joinedOrLeft = [...ids].some((id) => present.has(id) !== onlineIds.has(id));
			const unseen = [...present].filter((id) => !ids.has(id) && !sought.has(id));
			for (const id of unseen) {
				sought.add(id);
			}
			if (joinedOrLeft || unseen.length > 0) {
				refetch();
			}
		});
		onStatus(({ reconnected }) => {
			if (reconnected) {
				refetch();
			}
		});
		load();
	}

	function suggestionCard(suggestion) {
		const card = el("div", suggestion.isNew ? "suggestion new" : "suggestion");
		const head = el("div", "head");
		const tier = ROMAN[suggestion.nextTier] ?? suggestion.nextTier;
		const title = el("b");
		const icon = challengeIcon(suggestion.challengeId);
		if (icon) {
			title.append(icon, " ");
		}
		title.append(`${suggestion.challengeName} ${tier}`);
		head.append(title, el("span", "xp", `+${formatXp(suggestion.xp)}`));
		const weapon = el("div", "muted", suggestion.isNew ? `Try something new · ${suggestion.weaponName}` : suggestion.weaponName);
		card.append(head, weapon);
		if (suggestion.description) {
			card.append(el("div", "muted", suggestion.description));
		}
		const left = Math.max(0, Number(suggestion.target) - Number(suggestion.count));
		card.append(bar(suggestion.progress), el("div", "muted", `${formatXp(suggestion.count)}/${formatXp(suggestion.target)} · ${formatXp(left)} to go`));
		return card;
	}

	// Skill shots on the weapons tab are listed commonest first.
	const RARITY_ORDER = { Common: 0, Rare: 1, Epic: 2 };

	function tierPips(finished, count) {
		const pips = el("span", "pips");
		pips.setAttribute("aria-hidden", "true");
		for (let i = 0; i < count; i++) {
			pips.append(el("i", i < finished ? "on" : null));
		}
		return pips;
	}

	function challengeRow(challenge) {
		const finished = Number(challenge.tier) || 0;
		const tierCount = Number(challenge.tierCount) || 0;
		const complete = challenge.nextTarget === null || challenge.nextTarget === undefined;
		const row = el("li", complete ? "challenge complete" : (finished > 0 ? "challenge started" : "challenge"));

		const head = el("div", "head");
		const title = el("span", "title");
		const icon = challengeIcon(challenge.id);
		if (icon) {
			title.append(icon, " ");
		}
		title.append(el("b", null, challenge.name));
		if (challenge.rarity) {
			title.append(el("span", `rarity ${String(challenge.rarity).toLowerCase()}`, challenge.rarity));
		}
		const tier = el("span", "tier", `${finished > 0 ? (ROMAN[finished] ?? finished) : "–"} / ${ROMAN[tierCount] ?? tierCount}`);
		tier.title = `${finished} of ${tierCount} tiers done`;
		head.append(title, tier);
		row.append(head);

		if (challenge.description) {
			row.append(el("div", "muted", challenge.description));
		}

		const progress = el("div", "progress");
		progress.append(tierPips(finished, tierCount), bar(complete ? 1 : challenge.progress, "thin"));
		progress.append(el("span", "count", complete
			? `${formatXp(challenge.count)} · done`
			: `${formatXp(challenge.count)}/${formatXp(challenge.nextTarget)}`));
		row.append(progress);
		return row;
	}

	function challengeGroup(title, challenges) {
		const box = el("div", "challenge-group");
		const done = challenges.filter((c) => c.nextTarget === null || c.nextTarget === undefined).length;
		box.append(el("h3", "section-title", `${title} · ${done}/${challenges.length} complete`));
		const list = el("ul", "challenges");
		list.append(...challenges.map(challengeRow));
		box.append(list);
		return box;
	}

	function tiersDone(weapon) {
		const challenges = Array.isArray(weapon.challenges) ? weapon.challenges : [];
		return {
			done: challenges.reduce((sum, c) => sum + (Number(c.tier) || 0), 0),
			total: challenges.reduce((sum, c) => sum + (Number(c.tierCount) || 0), 0),
		};
	}

	function weaponDetail(weapon) {
		const box = el("section", "weapon-detail");
		const head = el("div", "weapon-head");
		const name = el("h2", "weapon-name", weapon.displayName);
		const maxed = weapon.level >= weapon.maxLevel;
		const level = el("div", "weapon-level");
		level.append(el("span", null, maxed ? `Level ${weapon.level} · max` : `Level ${weapon.level}/${weapon.maxLevel}`), bar(maxed ? 1 : weapon.levelProgress));
		head.append(name, level);

		const stats = el("div", "weapon-stats");
		const { done, total } = tiersDone(weapon);
		for (const [label, value] of [
			["Kills", formatXp(weapon.kills)],
			["HS%", formatPercent(weapon.headshotPercent)],
			["Accuracy", formatPercent(weapon.accuracy)],
			["Tiers", `${done}/${total}`],
		]) {
			const stat = el("div", "stat");
			stat.append(el("b", null, value), el("span", "muted", label));
			stats.append(stat);
		}
		box.append(head, stats);

		const challenges = Array.isArray(weapon.challenges) ? weapon.challenges : [];
		if (challenges.length === 0) {
			box.append(message("This weapon has no challenges."));
			return box;
		}
		const volume = challenges.filter((c) => c.kind !== "Skill");
		const skill = challenges
			.filter((c) => c.kind === "Skill")
			.map((c, index) => ({ c, index }))
			.sort((a, b) => ((RARITY_ORDER[a.c.rarity] ?? 0) - (RARITY_ORDER[b.c.rarity] ?? 0)) || (a.index - b.index))
			.map(({ c }) => c);
		if (volume.length > 0) {
			box.append(challengeGroup("Ladder", volume));
		}
		if (skill.length > 0) {
			box.append(challengeGroup("Skill shots", skill));
		}
		return box;
	}

	function ordinal(n) {
		const value = Number(n) || 0;
		const tens = value % 100;
		const suffix = tens >= 11 && tens <= 13 ? "th" : ({ 1: "st", 2: "nd", 3: "rd" }[value % 10] ?? "th");
		return `${value}${suffix}`;
	}

	// One chip per season: this season's place while it runs, then each top 3 finish of an ended season.
	function seasonBadges(seasons) {
		const badges = Array.isArray(seasons?.badges) ? seasons.badges : [];
		if (badges.length === 0) {
			return null;
		}
		const row = el("div", "season-badges");
		for (const badge of badges) {
			const place = Number(badge.place);
			const medal = !badge.current && place >= 1 && place <= 3 ? ` place-${place}` : "";
			const chip = el("a", `season-badge${badge.current ? " current" : ""}${medal}`);
			chip.href = badge.current ? "/seasons" : `/seasons?start=${badge.start}`;
			const text = badge.current
				? `${badge.season} · ${ordinal(place)} of ${badge.players} · ${formatXp(badge.xp)} XP`
				: `${ordinal(place)} · ${badge.season}`;
			chip.append(el("b", null, badge.current ? "Played this season" : "Season finish"), el("span", null, text));
			row.append(chip);
		}
		return row;
	}

	const PLAYER_TABS = [
		["next", "Next up"],
		["weapons", "Weapons"],
		["feats", "Feats"],
	];

	function initPlayer(root) {
		const match = /^\/players\/(\d{17})\/?$/.exec(window.location.pathname);
		const id = match ? match[1] : null;
		const panel = el("div", "panel");
		root.replaceChildren(panel);
		let online = false;
		let last = null;
		let seasons = null;
		// The open tab and the weapon picked on the weapons tab, kept across refetches.
		let tab = null;
		let weaponKey = null;

		function tabFromHash() {
			const name = window.location.hash.replace(/^#/, "");
			return PLAYER_TABS.some(([key]) => key === name) ? name : null;
		}

		function notFound() {
			document.title = `Player not found · ${SERVER_NAME}`;
			panel.replaceChildren(message("Player not found"));
		}

		function nextTab(player) {
			const suggestions = Array.isArray(player.suggestions) ? player.suggestions : [];
			return suggestions.length > 0 ? "next" : "weapons";
		}

		function renderNext(player) {
			const box = el("div", "pad");
			const suggestions = Array.isArray(player.suggestions) ? player.suggestions : [];
			if (suggestions.length === 0) {
				box.append(message("Nothing to suggest yet. Play a few rounds and the closest tiers show up here."));
				return box;
			}
			const cards = el("div", "suggestions");
			cards.append(...suggestions.map(suggestionCard));
			box.append(el("h2", "section-title", "Closest to a tier"), cards);
			return box;
		}

		function renderWeapons(player) {
			const weapons = Array.isArray(player.weapons) ? player.weapons : [];
			if (weapons.length === 0) {
				return message("No weapon progress yet.");
			}
			if (!weapons.some((weapon) => weapon.key === weaponKey)) {
				weaponKey = weapons[0].key;
			}
			const selected = weapons.find((weapon) => weapon.key === weaponKey);
			const picks = el("div", "weapon-picks");
			for (const weapon of weapons) {
				const pick = el("button", weapon.key === weaponKey ? "weapon-pick selected" : "weapon-pick");
				pick.type = "button";
				pick.setAttribute("aria-pressed", weapon.key === weaponKey ? "true" : "false");
				pick.append(el("span", null, weapon.displayName), el("span", "level", weapon.level));
				const { done, total } = tiersDone(weapon);
				pick.title = `Level ${weapon.level} · ${formatXp(weapon.kills)} kills · ${done}/${total} tiers`;
				pick.addEventListener("click", () => {
					weaponKey = weapon.key;
					render(last);
				});
				picks.append(pick);
			}
			const layout = el("div", "pad");
			layout.append(picks, weaponDetail(selected));
			return layout;
		}

		function renderFeats(player) {
			const featList = Array.isArray(player.feats) ? player.feats : [];
			if (featList.length === 0) {
				return message("No feats yet.");
			}
			const box = el("div", "pad");
			const grid = el("div", "feats");
			for (const feat of featList) {
				const card = el("div", feat.earned ? "feat earned" : "feat");
				const text = el("div", "feat-text");
				text.append(el("b", null, feat.name), el("div", "muted", feat.description));
				if (!feat.earned) {
					const target = Number(feat.target) || 0;
					text.append(bar(target > 0 ? Number(feat.current) / target : 0, "small"), el("span", "muted", ` ${formatXp(feat.current)}/${formatXp(target)}`));
				}
				card.append(featIcon(feat.id, feat.earned, feat.name), text);
				grid.append(card);
			}
			box.append(grid);
			return box;
		}

		function renderFeed(player) {
			const feed = Array.isArray(player.feed) ? player.feed : [];
			const box = el("section", "player-feed");
			const feedList = el("ul", "feed");
			if (feed.length === 0) {
				feedList.append(el("li", null, "Nothing yet. Skill shots, rank ups, and tiers show up here as they happen."));
			} else {
				feedList.append(...feed.map((entry) => feedItem(entry, false)));
			}
			box.append(el("h2", "section-title", "XP feed"), feedList);
			return box;
		}

		function renderTab(player) {
			switch (tab) {
				case "weapons":
					return renderWeapons(player);
				case "feats":
					return renderFeats(player);
				default:
					return renderNext(player);
			}
		}

		function tabLabel(key, label, player) {
			if (key === "weapons" && Array.isArray(player.weapons)) {
				return `${label} · ${player.weapons.length}`;
			}
			if (key === "feats" && Array.isArray(player.feats) && player.feats.length > 0) {
				return `${label} · ${player.feats.filter((feat) => feat.earned).length}/${player.feats.length}`;
			}
			return label;
		}

		function tabBar(player) {
			const strip = el("div", "tabs");
			strip.setAttribute("role", "tablist");
			const buttons = PLAYER_TABS.map(([key, label]) => {
				const button = el("button", "tab", tabLabel(key, label, player));
				button.type = "button";
				button.id = `tab-${key}`;
				button.setAttribute("role", "tab");
				button.setAttribute("aria-controls", "player-tab");
				button.setAttribute("aria-selected", key === tab ? "true" : "false");
				button.tabIndex = key === tab ? 0 : -1;
				button.addEventListener("click", () => selectTab(key, true));
				return button;
			});
			strip.addEventListener("keydown", (event) => {
				const step = { ArrowRight: 1, ArrowLeft: -1 }[event.key];
				if (!step) {
					return;
				}
				event.preventDefault();
				const index = PLAYER_TABS.findIndex(([key]) => key === tab);
				const next = PLAYER_TABS[(index + step + PLAYER_TABS.length) % PLAYER_TABS.length][0];
				selectTab(next, true);
				panel.querySelector(`#tab-${next}`)?.focus();
			});
			strip.append(...buttons);
			return strip;
		}

		function selectTab(key, updateHash) {
			tab = key;
			if (updateHash) {
				history.replaceState(null, "", `#${key}`);
			}
			if (last) {
				render(last);
			}
		}

		function render(player) {
			last = player;
			online = Boolean(player.online);
			document.title = `${player.name} · ${SERVER_NAME}`;
			tab = tab ?? tabFromHash() ?? nextTab(player);

			const head = el("div", "top player-head");
			const left = el("span");
			left.append(el("b", "name", player.name), " · ");
			left.append(online ? el("span", "online", "online now") : el("span", null, `last seen ${ago(player.lastSeen)}`));
			const steam = el("a", null, "Steam profile");
			steam.href = `https://steamcommunity.com/profiles/${id}`;
			steam.rel = "noopener noreferrer";
			steam.target = "_blank";
			head.append(left, steam);

			const summary = el("div", "pad");
			const atTop = player.nextRankName === null || player.nextRankName === undefined;
			const details = [`${formatXp(player.rankXp)} XP`];
			details.push(atTop ? "top rank" : `${formatXp(player.xpToNextRank)} XP to ${player.nextRankName}`);
			details.push(`first seen ${formatDate(player.firstSeen)}`);
			summary.append(rankBadge(player.rank, player.rankName, true), bar(atTop ? 1 : player.rankProgress), el("div", "muted", details.join(" · ")));
			const badges = seasonBadges(seasons);
			if (badges) {
				summary.append(badges);
			}

			const body = el("div", "tab-panel");
			body.id = "player-tab";
			body.setAttribute("role", "tabpanel");
			body.setAttribute("aria-labelledby", `tab-${tab}`);
			body.append(renderTab(player));

			const tabs = el("div", "player-tabs");
			tabs.append(tabBar(player), body);
			const grid = el("div", "player-grid");
			grid.append(tabs, renderFeed(player));

			panel.replaceChildren(head, summary, grid);
		}

		async function load() {
			try {
				// The badges are extra, so the page still shows without them.
				const [player, playerSeasons] = await Promise.all([
					getJson(`/api/players/${id}`),
					getJson(`/api/players/${id}/seasons`).catch(() => seasons),
				]);
				seasons = playerSeasons;
				render(player);
			} catch (error) {
				if (error.status === 404) {
					notFound();
				} else {
					panel.replaceChildren(message("This player can't be loaded right now."));
				}
			}
		}

		if (id === null) {
			notFound();
			return;
		}

		window.addEventListener("hashchange", () => {
			const fromHash = tabFromHash();
			if (fromHash && fromHash !== tab) {
				selectTab(fromHash, false);
			}
		});

		const refetch = throttle(load, REFETCH_EVERY_MS);
		on("progress", (changed) => {
			if (Array.isArray(changed) && changed.some((changedId) => String(changedId) === id)) {
				refetch();
			}
		});
		on("snapshot", (snapshot) => {
			const present = snapshot.players.some((p) => !p.isBot && String(p.steamId) === id);
			if (present !== online) {
				refetch();
			}
		});
		onStatus(({ reconnected }) => {
			if (reconnected) {
				refetch();
			}
		});
		load();
	}

	function initLadder(root) {
		const top = el("div", "top");
		const body = el("div");
		const panel = el("div", "panel ladder");
		panel.append(top, body);
		root.replaceChildren(panel);

		function render(steps) {
			const total = steps.reduce((sum, step) => sum + (Number(step.players) || 0), 0);
			const most = Math.max(1, ...steps.map((step) => Number(step.players) || 0));
			const title = el("span");
			title.append(el("b", null, "Ladder"));
			top.replaceChildren(title, el("span", null, `${total} ${total === 1 ? "player" : "players"}`));
			const rows = steps.map((step) => {
				const tr = el("tr", step.stage === 1 ? "rank-start" : null);
				if (Number(step.players) > 0) {
					tr.classList.add("populated");
				}
				const count = cell("ladder-count");
				count.append(bar(Number(step.players) / most, "count"), el("span", null, step.players));
				tr.append(
					cell(null, rankBadge(step.rank, step.name)),
					cell("num", formatXp(step.minXp)),
					count,
				);
				return tr;
			});
			body.replaceChildren(table([["Rank"], ["XP needed", "num"], ["Players"]], rows));
		}

		async function load() {
			try {
				render(await getJson("/api/ladder"));
			} catch {
				body.replaceChildren(message("The ladder can't be loaded right now."));
			}
		}

		const refetch = throttle(load, REFETCH_EVERY_MS);
		on("progress", (changed) => {
			if (Array.isArray(changed) && changed.length > 0) {
				refetch();
			}
		});
		onStatus(({ reconnected }) => {
			if (reconnected) {
				refetch();
			}
		});
		load();
	}

	function initSeasons(root) {
		const top = el("div", "top");
		const body = el("div");
		const panel = el("div", "panel");
		panel.append(top, body);
		root.replaceChildren(panel);
		top.append(el("b", null, "Seasons"));

		const requested = new URLSearchParams(window.location.search).get("start");
		const start = /^\d{4}-\d{2}-\d{2}$/.test(requested ?? "") ? requested : null;
		let current = start === null;

		function seasonLink(text, target) {
			const link = el("a", null, text);
			link.href = target ? `/seasons?start=${target}` : "/seasons";
			return link;
		}

		function render(season) {
			current = Boolean(season.current);
			document.title = `Season ${season.name} · ${SERVER_NAME}`;
			const title = el("span");
			title.append(el("b", null, `Season ${season.name}`), current ? " · running now" : " · ended");
			const links = el("span");
			links.append(seasonLink("← Previous", season.previous));
			if (season.next) {
				links.append(" · ", seasonLink("Next →", season.next));
			}
			top.replaceChildren(title, links);

			const lastDay = new Date(`${season.end}T00:00:00Z`);
			lastDay.setUTCDate(lastDay.getUTCDate() - 1);
			const note = current
				? `Season XP counts every point of rank XP earned from ${season.start} to ${lastDay.toISOString().slice(0, 10)} (UTC). Lifetime rank never resets.`
				: `Final standings. The top 3 keep a badge on their player page.`;
			const intro = el("div", "pad muted", note);

			const rows = Array.isArray(season.rows) ? season.rows : [];
			if (rows.length === 0) {
				body.replaceChildren(intro, message(current ? "No one has earned XP this season yet." : "No one played this season."));
				return;
			}
			const trs = rows.map((row) => {
				const tr = el("tr", !current && row.position <= 3 ? `place-${row.position}` : null);
				tr.append(
					cell("num", row.position),
					cell("name", playerLink(row.steamId, row.name)),
					cell(null, rankBadge(row.rank, row.rankName)),
					cell("num", formatXp(row.xp)),
					cell(row.online ? "online" : null, row.online ? "online" : ""),
				);
				return tr;
			});
			body.replaceChildren(intro, table([["#", "num"], ["Player"], ["Rank"], ["Season XP", "num"], [""]], trs));
		}

		async function load() {
			try {
				render(await getJson(start ? `/api/seasons?start=${start}` : "/api/seasons"));
			} catch (error) {
				body.replaceChildren(message(error.status === 404 ? "There's no such season." : "The season can't be loaded right now."));
			}
		}

		const refetch = throttle(load, REFETCH_EVERY_MS);
		on("progress", (changed) => {
			if (current && Array.isArray(changed) && changed.length > 0) {
				refetch();
			}
		});
		onStatus(({ reconnected }) => {
			if (reconnected) {
				refetch();
			}
		});
		load();
	}

	const pages = { live: initLive, ranks: initRanks, player: initPlayer, ladder: initLadder, seasons: initSeasons };

	document.addEventListener("DOMContentLoaded", () => {
		const root = document.getElementById("app");
		const init = pages[document.body.dataset.page];
		if (root && init) {
			init(root);
		}
	});
})();
