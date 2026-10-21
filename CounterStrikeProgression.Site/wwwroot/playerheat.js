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

	// Two fights or fewer stay faint, so one death doesn't paint an area solid red.
	const FAINT_FIGHTS = 2;
	const SOLID_FIGHTS = 6;
	const FAINT_ALPHA = 0.25;
	const SOLID_ALPHA = 0.85;

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

	function cellColor(won, lost) {
		const fights = won + lost;
		const balance = fights > 0 ? (won - lost) / fights : 0;
		const end = balance < 0 ? RED : GREEN;
		const t = Math.abs(balance);
		const rgb = GREY.map((c, i) => Math.round(c + (end[i] - c) * t));
		const confidence = Math.min(1, Math.max(0, (fights - FAINT_FIGHTS) / (SOLID_FIGHTS - FAINT_FIGHTS)));
		const alpha = FAINT_ALPHA + (SOLID_ALPHA - FAINT_ALPHA) * confidence;
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
		const readout = el("div", "muted player-heat-readout", BLANK);
		const note = el("div", "muted");
		element.append(head, filters, canvas, readout, note);

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
				context.fillStyle = "rgba(13,15,18,0.35)";
				context.fillRect(0, 0, size, size);
			} else {
				context.fillStyle = "#101318";
				context.fillRect(0, 0, size, size);
			}

			const pixels = view.cellPixels;
			const inset = 2;
			for (const cell of state.cells.values()) {
				context.fillStyle = cellColor(cell.won, cell.lost);
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
			readout.hidden = canvas.hidden || state.cells.size === 0;
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
			canvas.hidden = !drawable;
			showNote();
			showLive();
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
					canvas.hidden = true;
					readout.hidden = true;
					live.hidden = true;
					note.textContent = "The fights can't be loaded right now.";
				}
			}
		}

		function setOnline(online) {
			const now = Boolean(online);
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
				if (!canvas.hidden) {
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
					if (!canvas.hidden) {
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


		return { element, setOnline };
	}

	window.PlayerHeat = { create };
})();
