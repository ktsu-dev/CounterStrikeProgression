"use strict";

(() => {
	const SERVER_NAME = "ktsu.dev CS2";

	// Matches RankLadder.Names in CounterStrikeProgression.Core, because the live snapshot sends only the index.
	const RANK_NAMES = [
		"Silver I",
		"Silver II",
		"Silver III",
		"Silver IV",
		"Silver Elite",
		"Silver Elite Master",
		"Gold Nova I",
		"Gold Nova II",
		"Gold Nova III",
		"Gold Nova Master",
		"Master Guardian I",
		"Master Guardian II",
		"Master Guardian Elite",
		"Distinguished Master Guardian",
		"Legendary Eagle",
		"Legendary Eagle Master",
		"Supreme Master First Class",
		"Global Elite",
	];

	const FEED_LIMIT = 50;
	const REFETCH_EVERY_MS = 2000;
	const STREAM_GRACE_MS = 15000;
	const STEAM_ID = /^\d{17}$/;
	const GOLD_KINDS = new Set(["RankUp", "WeaponLevelUp", "TierCompleted"]);
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

	function rankBadge(name) {
		return el("span", "rank", name);
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
			if (!snapshot || snapshot.players.length === 0) {
				board.replaceChildren(message(snapshot ? "Nobody is playing." : "Waiting for the server."));
				return;
			}
			const players = [...snapshot.players].sort((a, b) => (b.kills - a.kills) || (a.deaths - b.deaths));
			const rows = players.map((player) => {
				const tr = el("tr");
				const name = player.isBot ? el("span", null, player.name) : playerLink(player.steamId, player.name);
				const badge = player.isBot ? el("span", "rank bot", "bot") : rankBadge(rankName(player.rank));
				tr.append(
					cell("name", name),
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

		function feedItem(entry) {
			const li = el("li", GOLD_KINDS.has(entry.kind) ? "gold" : null);
			const what = el("span", "what");
			what.append(playerLink(entry.steamId, entry.playerName), ` · ${entry.title}`);
			if (entry.weapon) {
				what.append(` · ${entry.weapon}`);
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

		function renderFeed() {
			if (feed.length === 0) {
				feedList.replaceChildren(el("li", null, "Nothing yet."));
				return;
			}
			feedList.replaceChildren(...feed.map(feedItem));
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
				feed = Array.isArray(live.feed) ? live.feed.slice(0, FEED_LIMIT) : [];
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
		on("feed", addFeed);
		on("online", (data) => {
			serverOnline = data === true;
			renderTop();
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
					cell("name", playerLink(row.steamId, row.name)),
					cell(null, rankBadge(row.rankName)),
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
			if (Array.isArray(changed) && changed.some((id) => ids.has(String(id)))) {
				refetch();
			}
		});
		on("snapshot", (snapshot) => {
			const present = new Set(snapshot.players.filter((p) => !p.isBot).map((p) => String(p.steamId)));
			const joinedOrLeft = [...ids].some((id) => present.has(id) !== onlineIds.has(id));
			if (joinedOrLeft) {
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

	function challengeChip(challenge) {
		const finished = Number(challenge.tier) || 0;
		let label = challenge.name;
		if (finished > 0) {
			label += ` ${ROMAN[finished] ?? finished}`;
		}
		if (challenge.nextTarget !== null && challenge.nextTarget !== undefined) {
			label += ` · ${formatXp(challenge.count)}/${formatXp(challenge.nextTarget)}`;
		}
		return el("span", finished > 0 ? "done" : null, label);
	}

	function initPlayer(root) {
		const match = /^\/players\/(\d{17})\/?$/.exec(window.location.pathname);
		const id = match ? match[1] : null;
		const panel = el("div", "panel");
		root.replaceChildren(panel);
		let online = false;

		function notFound() {
			document.title = `Player not found · ${SERVER_NAME}`;
			panel.replaceChildren(message("Player not found"));
		}

		function render(player) {
			online = Boolean(player.online);
			document.title = `${player.name} · ${SERVER_NAME}`;

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
			summary.append(el("div", "big", player.rankName), bar(atTop ? 1 : player.rankProgress), el("div", "muted", details.join(" · ")));

			const weapons = Array.isArray(player.weapons) ? player.weapons : [];
			const parts = [head, summary];
			if (weapons.length === 0) {
				parts.push(message("No weapon progress yet."));
			} else {
				const rows = weapons.map((weapon) => {
					const tr = el("tr");
					const level = el("span", null, weapon.level);
					const levelCell = cell("num");
					levelCell.append(level, bar(weapon.level >= weapon.maxLevel ? 1 : weapon.levelProgress, "small"));
					tr.append(
						cell("name", weapon.displayName),
						levelCell,
						cell("num", formatXp(weapon.kills)),
						cell("num", formatPercent(weapon.headshotPercent)),
						cell("num", formatPercent(weapon.accuracy)),
					);
					return tr;
				});
				parts.push(table([["Weapon"], ["Lvl", "num"], ["Kills", "num"], ["HS%", "num"], ["Acc", "num"]], rows));

				const ladders = el("div", "pad");
				for (const weapon of weapons) {
					const challenges = Array.isArray(weapon.challenges) ? weapon.challenges : [];
					if (challenges.length === 0) {
						continue;
					}
					const tiers = el("div", "tiers");
					tiers.append(...challenges.map(challengeChip));
					ladders.append(el("div", "muted", `${weapon.displayName} challenges`), tiers);
				}
				if (ladders.childElementCount > 0) {
					parts.push(ladders);
				}
			}
			panel.replaceChildren(...parts);
		}

		async function load() {
			try {
				render(await getJson(`/api/players/${id}`));
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

	const pages = { live: initLive, ranks: initRanks, player: initPlayer };

	document.addEventListener("DOMContentLoaded", () => {
		const root = document.getElementById("app");
		const init = pages[document.body.dataset.page];
		if (root && init) {
			init(root);
		}
	});
})();
