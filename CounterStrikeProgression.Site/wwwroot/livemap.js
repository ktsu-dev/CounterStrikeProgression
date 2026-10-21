"use strict";

// The home page's live map: every player on the server, humans and bots, drawn on the current map's radar and
// moved with each batch from the site's event stream.
(() => {
	const SVG = "http://www.w3.org/2000/svg";
	// A human's dot radius on a 1024 pixel radar, the same as the player page's marker in playerheat.js.
	const MARKER_RADIUS = 16;
	// The pin's viewBox reaches the wedge's tip on every side, so the pin can turn without clipping.
	const PIN_EXTENT = MARKER_RADIUS * 2.6;
	// Without a positions event for this long, the server has stopped sending and the positions are old. Every
	// in-order batch brings one, about every 2 seconds, so this leaves room for a few late batches.
	const QUIET_MS = 20000;
	// How soon to ask /api/positions again while the current map's radar isn't drawn, such as after a failed
	// fetch or image load, or while the site has no radar image for the map yet. Each try doubles the wait, up to
	// the ceiling, until the map changes or a radar draws.
	const RADAR_RETRY_MS = 5000;
	const RADAR_RETRY_MAX_MS = 60000;
	// A label on a marker past this share of the width goes on the marker's left, so it stays on the map.
	const FLIP_AT = 0.7;
	const STEAM_ID = /^\d+$/;

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

	function svg(tag, className) {
		const node = document.createElementNS(SVG, tag);
		if (className) {
			node.setAttribute("class", className);
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

	function fetchPositions() {
		return fetch("/api/positions", { headers: { Accept: "application/json" }, cache: "no-store" })
			.then((response) => (response.ok ? response.json() : null))
			.catch(() => null);
	}

	// A dot with a wedge pointing the way the player faces, drawn like the player page's marker.
	function makePin(bot) {
		const pin = svg("svg", bot ? "live-map-pin bot" : "live-map-pin");
		pin.setAttribute("viewBox", `${-PIN_EXTENT} ${-PIN_EXTENT} ${PIN_EXTENT * 2} ${PIN_EXTENT * 2}`);
		pin.setAttribute("aria-hidden", "true");
		const turn = svg("g");
		const wedge = svg("path", "live-map-wedge");
		wedge.setAttribute("d", `M ${-MARKER_RADIUS * 0.8} ${-MARKER_RADIUS * 0.6} L 0 ${-MARKER_RADIUS * 2.6} L ${MARKER_RADIUS * 0.8} ${-MARKER_RADIUS * 0.6} Z`);
		const dot = svg("circle", "live-map-dot");
		dot.setAttribute("r", String(MARKER_RADIUS));
		turn.append(wedge, dot);
		pin.append(turn);
		return { pin, turn };
	}

	function create(on) {
		const element = el("section", "live-map");
		const head = el("h2", "section-title live-map-head");
		const title = el("span", null, "Map");
		const live = el("span", "live-dot");
		live.title = "Updating live";
		live.hidden = true;
		head.append(title, live);

		const canvas = el("canvas", "live-map-canvas");
		canvas.setAttribute("role", "img");
		const layer = el("div", "live-map-layer");
		const frame = el("div", "live-map-frame");
		frame.hidden = true;
		frame.append(canvas, layer);
		const note = el("div", "muted", "Waiting for the server.");
		element.append(head, frame, note);

		const state = {
			map: null,
			calibrated: false,
			size: 1024,
			players: [],
			// The map and URL the canvas's radar is for, the loaded image, and whether a load is under way.
			radarMap: null,
			radarUrl: null,
			radar: null,
			radarLoading: false,
			// Whether /api/positions has answered for radarMap, so a missing radar URL means no image exists.
			radarAnswered: false,
			// The wait before the next radar retry.
			retryMs: RADAR_RETRY_MS,
			// Whether a fetch of /api/positions is under way, and when the last one started.
			fetching: false,
			fetchedAt: 0,
			online: null,
			lastAt: 0,
			// Counts positions events, so a fetch that started before one doesn't overwrite it.
			events: 0,
			markers: new Map(),
			open: null,
		};

		function isStale() {
			return state.online === false || (state.lastAt > 0 && Date.now() - state.lastAt > QUIET_MS);
		}

		function drawRadar() {
			const size = state.size;
			if (canvas.width !== size) {
				canvas.width = size;
				canvas.height = size;
			}
			const context = canvas.getContext("2d");
			context.fillStyle = "#101318";
			context.fillRect(0, 0, size, size);
			if (state.radar) {
				context.drawImage(state.radar, 0, 0, size, size);
				// Leaves the radar at 35% so its own colored outlines don't compete with the markers. Matches heatmaps.js.
				context.fillStyle = "rgba(13,15,18,0.65)";
				context.fillRect(0, 0, size, size);
			}
		}

		// Clears the canvas to the new map's radar at once, so markers never sit on another map's radar. The same
		// map and URL again is left alone while its image is loaded or loading, and loaded again after a failure.
		function showRadar(map, url) {
			if (map === state.radarMap && url === state.radarUrl && (state.radar || state.radarLoading)) {
				return;
			}
			if (map !== state.radarMap) {
				state.radarAnswered = false;
				state.retryMs = RADAR_RETRY_MS;
			}
			state.radarMap = map;
			state.radarUrl = url;
			state.radar = null;
			state.radarLoading = Boolean(url);
			drawRadar();
			if (!url) {
				return;
			}
			loadImage(url).then((image) => {
				if (state.radarMap === map && state.radarUrl === url) {
					state.radar = image;
					state.radarLoading = false;
					if (image) {
						state.retryMs = RADAR_RETRY_MS;
					}
					drawRadar();
				}
			});
		}

		function radarMissing() {
			return Boolean(state.map && state.calibrated) && (state.radarMap !== state.map || (!state.radar && !state.radarLoading));
		}

		function markerKey(player) {
			return player.isBot ? `${player.slot}|bot|${player.name}` : `${player.slot}|${player.steamId}`;
		}

		function makeMarker(player) {
			const { pin, turn } = makePin(player.isBot);
			let label;
			if (player.isBot) {
				// A bot's name shows on hover, or on a tap that keeps it open until the next tap elsewhere.
				label = el("button", "live-map-bot");
				label.type = "button";
				label.append(el("span", "live-map-name"));
				label.addEventListener("click", (event) => {
					event.stopPropagation();
					const key = label.dataset.key;
					state.open = state.open === key ? null : key;
					showOpen();
				});
			} else {
				label = el("a", "live-map-name");
			}
			layer.append(pin, label);
			return { pin, turn, label };
		}

		function showOpen() {
			for (const [key, marker] of state.markers) {
				marker.label.classList.toggle("open", key === state.open);
			}
		}

		function placeMarkers() {
			const size = state.size;
			const seen = new Set();
			const players = state.calibrated ? state.players.filter((player) => player.alive) : [];
			for (const player of players) {
				const key = markerKey(player);
				seen.add(key);
				const marker = state.markers.get(key) ?? makeMarker(player);
				state.markers.set(key, marker);
				const left = `${((Number(player.x) / size) * 100).toFixed(2)}%`;
				const top = `${((Number(player.y) / size) * 100).toFixed(2)}%`;
				marker.pin.style.left = left;
				marker.pin.style.top = top;
				marker.turn.setAttribute("transform", `rotate(${(Number(player.facing) || 0).toFixed(1)})`);
				marker.label.style.left = left;
				marker.label.style.top = top;
				marker.label.classList.toggle("flip", Number(player.x) / size > FLIP_AT);
				marker.label.dataset.key = key;
				const name = String(player.name ?? "");
				if (player.isBot) {
					marker.label.firstChild.textContent = name;
					marker.label.setAttribute("aria-label", `${name} (bot)`);
				} else {
					marker.label.textContent = name;
					const id = String(player.steamId ?? "");
					if (STEAM_ID.test(id)) {
						marker.label.href = `/players/${id}`;
					} else {
						marker.label.removeAttribute("href");
					}
				}
			}
			for (const [key, marker] of state.markers) {
				if (!seen.has(key)) {
					marker.pin.remove();
					marker.label.remove();
					state.markers.delete(key);
				}
			}
			if (state.open !== null && !seen.has(state.open)) {
				state.open = null;
			}
			showOpen();
		}

		function showState() {
			const stale = isStale();
			const drawable = Boolean(state.map && state.calibrated);
			frame.hidden = !drawable;
			layer.classList.toggle("stale", stale);
			live.hidden = !drawable || stale;
			title.textContent = state.map ? `Map · ${mapLabel(state.map)}` : "Map";
			const alive = state.players.filter((player) => player.alive).length;
			if (!state.map) {
				note.textContent = "Waiting for the server.";
			} else if (!state.calibrated) {
				note.textContent = "This map has no radar yet, so nobody can be placed on it.";
			} else if (state.radarAnswered && state.radarMap === state.map && !state.radarUrl) {
				note.textContent = "No radar image for this map yet.";
			} else if (stale) {
				note.textContent = alive > 0 ? "The server is offline. These are the last positions it sent." : "The server is offline.";
			} else if (alive === 0) {
				note.textContent = "Nobody is alive on the map right now.";
			} else {
				note.textContent = "Names link to player pages. Tap or hover a bot for its name.";
			}
		}

		function takePositions(positions, radar) {
			if (!positions || typeof positions !== "object") {
				return;
			}
			const map = positions.map ?? null;
			const changed = map !== state.map;
			state.map = map;
			state.calibrated = Boolean(positions.calibrated);
			state.size = Number(positions.size) > 0 ? Number(positions.size) : 1024;
			state.players = Array.isArray(positions.players) ? positions.players : [];
			canvas.setAttribute("aria-label", map ? `Where everyone is on ${mapLabel(map)}` : "The live map");
			if (radar !== undefined) {
				showRadar(map, radar);
			} else if (changed) {
				// Events carry no radar, so a new map's radar comes from /api/positions.
				showRadar(map, null);
				if (map && state.calibrated) {
					refresh();
				}
			}
			placeMarkers();
			showState();
		}

		function refresh() {
			state.fetching = true;
			state.fetchedAt = Date.now();
			const before = state.events;
			fetchPositions().then((positions) => {
				state.fetching = false;
				if (!positions) {
					return;
				}
				if (state.events === before) {
					takePositions(positions, positions.radar ?? null);
				} else if (positions.map === state.map) {
					showRadar(positions.map, positions.radar ?? null);
				}
				state.radarAnswered = positions.map === state.radarMap;
				showState();
			});
		}

		on("positions", (positions) => {
			state.events += 1;
			state.lastAt = Date.now();
			takePositions(positions);
		});

		on("online", (online) => {
			state.online = online === true;
			if (state.online) {
				state.lastAt = Date.now();
			}
			showState();
		});

		document.addEventListener("click", () => {
			if (state.open !== null) {
				state.open = null;
				showOpen();
			}
		});

		setInterval(() => {
			showState();
			if (radarMissing() && !state.fetching && Date.now() - state.fetchedAt >= state.retryMs) {
				state.retryMs = Math.min(state.retryMs * 2, RADAR_RETRY_MAX_MS);
				refresh();
			}
		}, 1000);

		state.lastAt = Date.now();
		refresh();

		return { element };
	}

	window.LiveMap = { create };
})();
