// Copyright (c) 2023-2026 ktsu-dev contributors
"use strict";

// The player page's fights panel: where one player wins and loses fights on a map, colored per radar cell by
// the balance of the two, and kept up to date from the site's event stream.
(() => {
	const SCOPES = [["match", "This match"], ["all", "All time"]];
	const STORAGE_KEYS = { true: "csp.heat.scope.online", false: "csp.heat.scope.offline" };
	const DEFAULT_SCOPES = { true: "match", false: "all" };

	const RED = [0xe5, 0x48, 0x4d];
	const GREY = [0x8a, 0x8f, 0x98];
	const GREEN = [0x30, 0xa4, 0x6c];

	// How strongly a cell shows by its number of fights, per scope. All time keeps two fights or fewer faint, so
	// one death doesn't paint an area solid red. This match has few fights in all, so even one shows clearly.
	const ALPHA = {
		all: { fewFights: 2, manyFights: 6, few: 0.25, many: 0.85 },
		match: { fewFights: 1, manyFights: 4, few: 0.6, many: 0.95 },
	};

	const SVG = "http://www.w3.org/2000/svg";
	// The marker's dot radius on a 1024 pixel radar.
	const MARKER_RADIUS = 16;

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

	function mapLabel(map) {
		return map.replace(/^(de|cs|ar|gd|dm|aim|fy|mg)_/, "").replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
	}

	function loadImage(url) {
		return new Promise((resolve) => {
			const image = new Image();
			image.onload = () => resolve(image);
			image.onerror = () => resolve(null);
			image.src = url;
		});
	}

	function readScope(online) {
		try {
			const stored = window.localStorage.getItem(STORAGE_KEYS[online]);
			if (SCOPES.some(([scope]) => scope === stored)) {
				return stored;
			}
		} catch {
			// Without storage the panel opens on the default.
		}
		return DEFAULT_SCOPES[online];
	}

	function saveScope(online, scope) {
		try {
			window.localStorage.setItem(STORAGE_KEYS[online], scope);
		} catch {
			// Without storage the choice still holds until the page is reloaded.
		}
	}

	function svg(tag, className) {
		const node = document.createElementNS(SVG, tag);
		if (className) {
			node.setAttribute("class", className);
		}
		return node;
	}

	function cellColor(won, lost, scope) {
		const fights = won + lost;
		const balance = fights > 0 ? (won - lost) / fights : 0;
		const end = balance < 0 ? RED : GREEN;
		const t = Math.abs(balance);
		const rgb = GREY.map((c, i) => Math.round(c + (end[i] - c) * t));
		const curve = ALPHA[scope] ?? ALPHA.all;
		const confidence = Math.min(1, Math.max(0, (fights - curve.fewFights) / (curve.manyFights - curve.fewFights)));
		const alpha = curve.few + (curve.many - curve.few) * confidence;
		return `rgba(${rgb.join(",")},${alpha.toFixed(3)})`;
	}

	// A no-break space keeps the readout line's height while nothing is hovered.
	const BLANK = String.fromCharCode(0xa0);

	function describe(won, lost) {
		return `${won} won, ${lost} lost`;
	}

	function create(steamId, on) {
		const element = el("section", "player-heat");
		const head = el("h2", "section-title player-heat-head");
		const title = el("span", null, "Fights");
		const live = el("span", "live-dot");
		live.title = "Updating live";
		live.hidden = true;
		head.append(title, live);

		const filters = el("div", "spans");
		const buttons = new Map();
		for (const [scope, label] of SCOPES) {
			const button = el("button", "weapon-pick", label);
			button.type = "button";
			button.addEventListener("click", () => {
				if (scope !== state.scope) {
					state.scope = scope;
					saveScope(state.online, scope);
					load();
				}
			});
			buttons.set(scope, button);
			filters.append(button);
		}

		const canvas = el("canvas", "player-heat-canvas");
		canvas.setAttribute("role", "img");

		// The player's marker sits over the canvas in its own layer, which lets pointer events through to the canvas
		// so the cell under the marker still reads out. The wedge points the way the player faces.
		const marker = svg("svg", "player-heat-marker");
		marker.setAttribute("aria-hidden", "true");
		marker.toggleAttribute("hidden", true);
		const pin = svg("g");
		const wedge = svg("path", "player-heat-wedge");
		wedge.setAttribute("d", `M ${-MARKER_RADIUS * 0.8} ${-MARKER_RADIUS * 0.6} L 0 ${-MARKER_RADIUS * 2.6} L ${MARKER_RADIUS * 0.8} ${-MARKER_RADIUS * 0.6} Z`);
		const dot = svg("circle", "player-heat-dot");
		dot.setAttribute("r", String(MARKER_RADIUS));
		pin.append(wedge, dot);
		marker.append(pin);

		const frame = el("div", "player-heat-map");
		frame.append(canvas, marker);
		const readout = el("div", "muted player-heat-readout", BLANK);
		const note = el("div", "muted");
		element.append(head, filters, frame, readout, note);

		const state = {
			online: null,
			scope: null,
			view: null,
			cells: new Map(),
			radar: null,
			radarUrl: null,
			radarLoad: Promise.resolve(),
			serverMap: null,
			serverStartedAt: null,
			request: 0,
			// The map the latest positions are for, and where the player was last seen.
			positionsMap: null,
			position: null,
			positionsSeen: false,
		};

		function key(x, y) {
			return `${x},${y}`;
		}

		function draw() {
			const view = state.view;
			const size = view.size;
			if (canvas.width !== size) {
				canvas.width = size;
				canvas.height = size;
			}
			const context = canvas.getContext("2d");
			context.clearRect(0, 0, size, size);
			if (state.radar) {
				context.drawImage(state.radar, 0, 0, size, size);
				// Leaves the radar at 35% so its own colored outlines don't compete with the cells. Matches heatmaps.js.
				context.fillStyle = "rgba(13,15,18,0.65)";
				context.fillRect(0, 0, size, size);
			} else {
				context.fillStyle = "#101318";
				context.fillRect(0, 0, size, size);
			}

			const pixels = view.cellPixels;
			const inset = 2;
			for (const cell of state.cells.values()) {
				context.fillStyle = cellColor(cell.won, cell.lost, state.scope);
				context.beginPath();
				if (context.roundRect) {
					context.roundRect(cell.x * pixels + inset, cell.y * pixels + inset, pixels - inset * 2, pixels - inset * 2, 6);
				} else {
					context.rect(cell.x * pixels + inset, cell.y * pixels + inset, pixels - inset * 2, pixels - inset * 2);
				}
				context.fill();
			}
		}

		// The note says why the map is empty, and the readout line only shows once there's a cell to point at.
		function showNote() {
			const view = state.view;
			readout.hidden = frame.hidden || state.cells.size === 0;
			if (!view.map) {
				note.textContent = state.scope === "match" ? "No match is being played right now." : "No fights here yet.";
			} else if (!view.calibrated) {
				note.textContent = "This map has no radar yet.";
			} else if (state.cells.size === 0) {
				note.textContent = "No fights here yet.";
			} else {
				note.textContent = "Green where you win most fights, red where you lose most. Faint areas have only a few.";
			}
		}

		// Shown only while the panel's map is the map the player was last seen on, and the positions are still for
		// that map. A dead player, or one missing from the latest positions, stays at the last known spot, faded.
		function placeMarker() {
			const view = state.view;
			const position = state.position;
			const visible = Boolean(view?.map && !frame.hidden && position && position.map === state.positionsMap && position.map === view.map);
			marker.toggleAttribute("hidden", !visible);
			if (!visible) {
				return;
			}
			const size = view.size;
			const scale = size / position.size;
			marker.setAttribute("viewBox", `0 0 ${size} ${size}`);
			pin.setAttribute("transform", `translate(${(position.x * scale).toFixed(1)} ${(position.y * scale).toFixed(1)}) rotate(${position.facing.toFixed(1)}) scale(${(size / 1024).toFixed(3)})`);
			marker.classList.toggle("stale", position.stale);
		}

		function takePositions(positions) {
			if (!positions || typeof positions !== "object") {
				return;
			}
			state.positionsMap = positions.map ?? null;
			const players = Array.isArray(positions.players) ? positions.players : [];
			const mine = players.find((player) => player.steamId !== null && player.steamId !== undefined && String(player.steamId) === steamId);
			if (mine && positions.map) {
				const size = Number(positions.size) > 0 ? Number(positions.size) : 1024;
				state.position = { map: positions.map, x: Number(mine.x), y: Number(mine.y), facing: Number(mine.facing) || 0, size, stale: !mine.alive };
			} else if (state.position) {
				state.position.stale = true;
			}
			placeMarker();
		}

		// Live when the panel shows the map being played, since only that map's fights arrive on the stream, and
		// only for a map with a radar, since the site can't place fights on any other.
		function showLive() {
			live.hidden = !state.view?.map || !state.view.calibrated || state.view.map !== state.serverMap;
		}

		async function show(view) {
			state.view = view;
			state.cells = new Map((Array.isArray(view.cells) ? view.cells : []).map((cell) => [key(cell.x, cell.y), { ...cell }]));
			title.textContent = view.map ? `Fights · ${mapLabel(view.map)}` : "Fights";
			readout.textContent = BLANK;
			const drawable = Boolean(view.map && view.calibrated);
			frame.hidden = !drawable;
			showNote();
			showLive();
			placeMarker();
			if (!drawable) {
				return;
			}
			canvas.setAttribute("aria-label", `Where this player wins and loses fights on ${mapLabel(view.map)}`);
			if (view.radar !== state.radarUrl) {
				const url = view.radar;
				state.radarUrl = url;
				state.radar = null;
				state.radarLoad = url
					? loadImage(url).then((image) => {
						if (state.radarUrl === url) {
							state.radar = image;
						}
					})
					: Promise.resolve();
			}
			await state.radarLoad;
			if (state.view !== view) {
				return;
			}
			draw();
			placeMarker();
		}

		async function load() {
			for (const [scope, button] of buttons) {
				const selected = scope === state.scope;
				button.classList.toggle("selected", selected);
				button.setAttribute("aria-pressed", selected ? "true" : "false");
			}
			const mine = ++state.request;
			try {
				const response = await fetch(`/api/players/${steamId}/fights?scope=${state.scope}`, { headers: { Accept: "application/json" }, cache: "no-store" });
				if (!response.ok) {
					throw new Error(String(response.status));
				}
				const view = await response.json();
				if (mine === state.request) {
					show(view);
				}
			} catch {
				if (mine === state.request) {
					state.view = null;
					frame.hidden = true;
					placeMarker();
					readout.hidden = true;
					live.hidden = true;
					note.textContent = "The fights can't be loaded right now.";
				}
			}
		}

		function setOnline(online) {
			const now = Boolean(online);
			if (!now && state.position) {
				state.position.stale = true;
				placeMarker();
			}
			if (now === state.online) {
				return;
			}
			state.online = now;
			state.scope = readScope(now);
			load();
		}

		function cellAt(event) {
			const view = state.view;
			const bounds = canvas.getBoundingClientRect();
			if (!view || bounds.width === 0) {
				return null;
			}
			const scale = view.size / bounds.width;
			const x = Math.floor(((event.clientX - bounds.left) * scale) / view.cellPixels);
			const y = Math.floor(((event.clientY - bounds.top) * scale) / view.cellPixels);
			return state.cells.get(key(x, y)) ?? null;
		}

		// Pointer events cover a tap as well as a hover, so a phone gets the same readout.
		function point(event) {
			const cell = cellAt(event);
			readout.textContent = cell ? describe(cell.won, cell.lost) : BLANK;
		}

		canvas.addEventListener("pointermove", point);
		canvas.addEventListener("pointerdown", point);
		canvas.addEventListener("pointerleave", (event) => {
			if (event.pointerType === "mouse") {
				readout.textContent = BLANK;
			}
		});

		on("fights", (fights) => {
			const view = state.view;
			if (!Array.isArray(fights) || !view?.map || !view.calibrated) {
				return;
			}
			let changed = false;
			for (const fight of fights) {
				if (String(fight.steamId) !== steamId || fight.map !== view.map) {
					continue;
				}
				const id = key(fight.x, fight.y);
				const cell = state.cells.get(id) ?? { x: fight.x, y: fight.y, won: 0, lost: 0 };
				if (fight.won) {
					cell.won += 1;
				} else {
					cell.lost += 1;
				}
				state.cells.set(id, cell);
				changed = true;
			}
			if (changed) {
				showNote();
				if (!frame.hidden) {
					draw();
				}
			}
		});

		on("snapshot", (snapshot) => {
			const map = snapshot?.map;
			if (!map) {
				return;
			}
			// A later start on the same map is a new match too, such as a rotation that repeats the map.
			const startedAt = Date.parse(snapshot.mapStartedAt ?? "");
			const restarted = map === state.serverMap && Number.isFinite(startedAt) && Number.isFinite(state.serverStartedAt) && startedAt > state.serverStartedAt;
			const newMap = map !== state.serverMap;
			if (Number.isFinite(startedAt)) {
				state.serverStartedAt = startedAt;
			}
			if (!newMap && !restarted) {
				return;
			}
			state.serverMap = map;
			showLive();
			if (restarted) {
				if (state.scope === "match" && state.view?.map === map) {
					// Cleared at once, so the new match's first fights don't land on the old one's while the refetch runs.
					state.cells = new Map();
					showNote();
					if (!frame.hidden) {
						draw();
					}
					load();
				}
				return;
			}
			// This match always follows the server, and so does All time while the player is on it.
			const follows = state.scope === "match" || state.online;
			if (follows && state.view && state.view.map !== map) {
				load();
			}
		});

		on("positions", (positions) => {
			state.positionsSeen = true;
			takePositions(positions);
		});

		// The page learns the server went quiet only from this event, since no snapshot arrives to say so.
		on("online", (online) => {
			if (online !== true) {
				setOnline(false);
			}
		});

		// The starting position, unless an event has already brought a newer one.
		fetch("/api/positions", { headers: { Accept: "application/json" }, cache: "no-store" })
			.then((response) => (response.ok ? response.json() : null))
			.then((positions) => {
				if (positions && !state.positionsSeen) {
					takePositions(positions);
				}
			})
			.catch(() => {
				// Without a starting position the marker appears with the first positions event.
			});

		return { element, setOnline };
	}

	window.PlayerHeat = { create };
})();
