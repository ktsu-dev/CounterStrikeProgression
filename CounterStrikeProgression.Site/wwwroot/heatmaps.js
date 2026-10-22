"use strict";

// The heatmap page: kill or death positions counted into cells over the map's radar, for the server or one
// player, or lines from each killer to their victim. The site sends cell counts and lines between cell centres,
// not positions, and this draws them as a heatmap or as additive strokes.
(() => {
	const SERVER_NAME = "ktsu.dev CS2";
	const STEAM_ID = /^\d{17}$/;
	// A bot persona's id, from BotIds in the plugin: 10000000000000000 to 10000000099999999.
	const BOT_ID = /^100000000\d{8}$/;

	// Dark blue through orange to near white, the usual heatmap ramp, read by intensity.
	const RAMP = [
		[0, [30, 60, 160]],
		[0.35, [40, 190, 200]],
		[0.6, [240, 210, 60]],
		[0.85, [242, 120, 40]],
		[1, [255, 240, 220]],
	];

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

	function message(text) {
		return el("div", "message", text);
	}

	function formatCount(value) {
		return Number(value ?? 0).toLocaleString("en-US");
	}

	function mapLabel(map) {
		return map.replace(/^(de|cs|ar|gd|dm|aim|fy|mg)_/, "").replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
	}

	function palette() {
		const colors = new Uint8ClampedArray(256 * 3);
		for (let i = 0; i < 256; i++) {
			const t = i / 255;
			let upper = 1;
			while (upper < RAMP.length - 1 && RAMP[upper][0] < t) {
				upper++;
			}
			const [t0, c0] = RAMP[upper - 1];
			const [t1, c1] = RAMP[upper];
			const f = t1 === t0 ? 0 : (t - t0) / (t1 - t0);
			for (let k = 0; k < 3; k++) {
				colors[i * 3 + k] = c0[k] + (c1[k] - c0[k]) * f;
			}
		}
		return colors;
	}

	const COLORS = palette();

	// The Lines tab's colors: the site's orange server-wide, and its green and red for a player's kills and deaths.
	const LINE_COLORS = {
		server: [242, 163, 58],
		kill: [76, 209, 122],
		death: [229, 83, 75],
	};
	// One kill's stroke alpha. Strokes add up under "lighter", so a line with Count kills is drawn at Count times this.
	const LINE_ALPHA = 0.14;
	const KINDS = ["kills", "deaths", "lines"];

	function readQuery() {
		const query = new URLSearchParams(location.search);
		const player = query.get("player");
		return {
			map: query.get("map") || "",
			kind: KINDS.includes(query.get("kind")) ? query.get("kind") : "kills",
			player: STEAM_ID.test(player ?? "") ? player : "",
		};
	}

	function writeQuery(state) {
		const query = new URLSearchParams();
		if (state.map) {
			query.set("map", state.map);
		}
		if (state.kind !== "kills") {
			query.set("kind", state.kind);
		}
		if (state.player) {
			query.set("player", state.player);
		}
		const search = query.toString();
		history.replaceState(null, "", search ? `?${search}` : location.pathname);
	}

	function loadImage(url) {
		return new Promise((resolve) => {
			const image = new Image();
			image.onload = () => resolve(image);
			image.onerror = () => resolve(null);
			image.src = url;
		});
	}

	function drawOutline(context, view) {
		const size = view.cellPixels;
		context.fillStyle = "#101318";
		context.fillRect(0, 0, view.size, view.size);
		context.fillStyle = "#232a33";
		for (const [x, y] of view.outline) {
			context.fillRect(x * size - 1, y * size - 1, size + 2, size + 2);
		}
	}

	function drawHeat(context, view) {
		if (!view.cells.length || !view.max) {
			return;
		}

		const heat = document.createElement("canvas");
		heat.width = view.size;
		heat.height = view.size;
		const blobs = heat.getContext("2d");
		const radius = view.cellPixels * 2.5;
		for (const [x, y, count] of view.cells) {
			const cx = (x + 0.5) * view.cellPixels;
			const cy = (y + 0.5) * view.cellPixels;
			const gradient = blobs.createRadialGradient(cx, cy, 0, cx, cy, radius);
			gradient.addColorStop(0, "rgba(0,0,0,1)");
			gradient.addColorStop(1, "rgba(0,0,0,0)");
			// A square root keeps one busy corner from washing out everywhere else.
			blobs.globalAlpha = Math.min(1, 0.05 + 0.35 * Math.sqrt(count / view.max));
			blobs.fillStyle = gradient;
			blobs.fillRect(cx - radius, cy - radius, radius * 2, radius * 2);
		}

		const image = blobs.getImageData(0, 0, view.size, view.size);
		const pixels = image.data;
		for (let i = 0; i < pixels.length; i += 4) {
			const alpha = pixels[i + 3];
			if (alpha === 0) {
				continue;
			}
			pixels[i] = COLORS[alpha * 3];
			pixels[i + 1] = COLORS[alpha * 3 + 1];
			pixels[i + 2] = COLORS[alpha * 3 + 2];
			pixels[i + 3] = Math.min(230, 60 + alpha);
		}
		blobs.putImageData(image, 0, 0);
		context.drawImage(heat, 0, 0);
	}

	function drawLines(context, view) {
		if (!view.lines?.length) {
			return;
		}

		const size = view.cellPixels;
		context.save();
		context.globalCompositeOperation = "lighter";
		context.lineWidth = 1.5;
		context.lineCap = "round";
		for (const [x1, y1, x2, y2, death, count] of view.lines) {
			const rgb = (!view.player ? LINE_COLORS.server : death ? LINE_COLORS.death : LINE_COLORS.kill).join(",");
			const fromX = (x1 + 0.5) * size;
			const fromY = (y1 + 0.5) * size;
			const toX = (x2 + 0.5) * size;
			const toY = (y2 + 0.5) * size;
			// Each line fades in from nothing at the shooter, so its bright end marks where the victim fell.
			const fade = context.createLinearGradient(fromX, fromY, toX, toY);
			fade.addColorStop(0, `rgba(${rgb},0)`);
			fade.addColorStop(1, `rgba(${rgb},${Math.min(1, LINE_ALPHA * count).toFixed(3)})`);
			context.strokeStyle = fade;
			context.beginPath();
			context.moveTo(fromX, fromY);
			context.lineTo(toX, toY);
			context.stroke();
		}
		context.restore();
	}

	async function draw(canvas, view) {
		canvas.width = view.size;
		canvas.height = view.size;
		const context = canvas.getContext("2d");
		const radar = view.radar ? await loadImage(view.radar) : null;
		if (radar) {
			context.drawImage(radar, 0, 0, view.size, view.size);
			// Leaves the radar at 35% so its own colored outlines don't compete with the heat. Matches playerheat.js.
			context.fillStyle = "rgba(13,15,18,0.65)";
			context.fillRect(0, 0, view.size, view.size);
		} else {
			drawOutline(context, view);
		}
		if (view.kind === "Lines") {
			drawLines(context, view);
		} else {
			drawHeat(context, view);
		}
	}

	function swatch(rgb, label) {
		const item = el("span", "heat-key");
		const line = el("span", "heat-swatch");
		line.style.background = `rgb(${rgb.join(",")})`;
		item.append(line, el("span", null, label));
		return item;
	}

	function legend(view) {
		const wrap = el("div", "heat-legend muted");
		if (view.kind === "Lines") {
			if (view.player) {
				wrap.append(swatch(LINE_COLORS.kill, "their kills"), swatch(LINE_COLORS.death, "their deaths"));
			} else {
				const ramp = el("span", "heat-ramp");
				const [r, g, b] = LINE_COLORS.server;
				ramp.style.background = `linear-gradient(90deg, rgba(${r},${g},${b},0.25), rgb(${r},${g},${b}) 70%, rgb(255,240,220))`;
				wrap.append(el("span", null, "one kill"), ramp, el("span", null, "a lane used over and over"));
			}
			return wrap;
		}

		const ramp = el("span", "heat-ramp");
		ramp.style.background = `linear-gradient(90deg, ${RAMP.map(([t, c]) => `rgb(${c.join(",")}) ${t * 100}%`).join(", ")})`;
		wrap.append(el("span", null, "fewer"), ramp, el("span", null, "more"));
		return wrap;
	}

	function initHeatmaps(root) {
		const state = readQuery();
		let request = 0;

		function tabs() {
			const bar = el("div", "tabs");
			bar.setAttribute("role", "tablist");
			for (const [kind, label] of [["kills", "Kills"], ["deaths", "Deaths"], ["lines", "Lines"]]) {
				const button = el("button", "tab", label);
				button.type = "button";
				button.setAttribute("role", "tab");
				button.setAttribute("aria-selected", String(state.kind === kind));
				button.addEventListener("click", () => {
					if (state.kind !== kind) {
						state.kind = kind;
						load();
					}
				});
				bar.append(button);
			}
			return bar;
		}

		function mapPicker(view) {
			const wrap = el("label", "picker");
			wrap.append(el("span", "muted", "Map"));
			const select = el("select");
			for (const map of view.maps) {
				const option = el("option", null, `${mapLabel(map.map)} · ${formatCount(map.points)} kills`);
				option.value = map.map;
				option.selected = map.map === view.map;
				select.append(option);
			}
			select.addEventListener("change", () => {
				state.map = select.value;
				load();
			});
			wrap.append(select);
			return wrap;
		}

		function describe(view) {
			const who = view.player ? (view.player.name || "This player") : "Everyone";
			const what = view.kind === "Lines"
				? (view.player ? "lines to the players they killed, and from the players who killed them," : "lines from each killer to their victim")
				: view.kind === "Deaths" ? "where they died" : "where they got their kills";
			const count = view.points > 0 ? `, from the latest ${formatCount(view.points)}` : "";
			const lines = [`${who}: ${what} on ${mapLabel(view.map)}${count}.`];
			if (!view.calibrated) {
				lines.push(view.kind === "Lines"
					? "This map has no known radar layout, so no lines are drawn."
					: "This map has no known radar layout, so the kills are drawn on their own outline.");
			} else if (!view.radar) {
				lines.push("No radar image is on the site for this map yet, so the outline is drawn from where people fight.");
			}
			return lines;
		}

		function render(view) {
			root.replaceChildren();
			const head = el("div", "top");
			const title = el("span");
			title.append(el("b", null, "Heatmaps"));
			head.append(title);
			if (view.player) {
				const back = el("span");
				const link = el("a", null, view.player.name || "Player");
				link.href = `/players/${view.player.steamId}`;
				const all = el("a", null, "whole server");
				all.href = "#";
				all.addEventListener("click", (event) => {
					event.preventDefault();
					state.player = "";
					load();
				});
				back.append(link);
				if (BOT_ID.test(String(view.player.steamId))) {
					back.append(el("span", "bot-tag", "BOT"));
				}
				back.append(" · ", all);
				head.append(back);
			}

			const panel = el("div", "panel");
			panel.append(head);
			if (!view.map) {
				panel.append(message("No kills are recorded yet. They show up here after the next map is played."));
				root.append(panel);
				return;
			}

			const controls = el("div", "pad board-controls");
			controls.append(mapPicker(view));
			panel.append(controls, tabs());

			const body = el("div", "pad heat-body");
			const canvas = el("canvas", "heat-canvas");
			canvas.setAttribute("role", "img");
			canvas.setAttribute("aria-label", describe(view)[0]);
			// Lines need the game's radar layout, so an uncalibrated map's Lines tab shows only its outline and why.
			const noLines = view.kind === "Lines" && !view.calibrated;
			body.append(canvas);
			if (!noLines) {
				body.append(legend(view));
			}
			for (const line of describe(view)) {
				body.append(el("div", "muted", line));
			}
			if (view.points === 0 && !noLines) {
				body.append(el("div", "muted", view.player ? "Nothing is recorded for this player on this map yet." : "Nothing is recorded here yet."));
			}
			panel.append(body);
			root.append(panel);
			draw(canvas, view);
		}

		async function load() {
			const mine = ++request;
			const query = new URLSearchParams();
			if (state.map) {
				query.set("map", state.map);
			}
			query.set("kind", state.kind);
			if (state.player) {
				query.set("player", state.player);
			}
			try {
				const response = await fetch(`/api/heatmaps?${query}`, { headers: { Accept: "application/json" } });
				if (!response.ok) {
					throw new Error(String(response.status));
				}
				const view = await response.json();
				if (mine !== request) {
					return;
				}
				state.map = view.map ?? "";
				writeQuery(state);
				const botSuffix = BOT_ID.test(String(view.player?.steamId ?? "")) ? " [BOT]" : "";
				document.title = view.player?.name ? `${view.player.name}${botSuffix} · Heatmaps · ${SERVER_NAME}` : `Heatmaps · ${SERVER_NAME}`;
				render(view);
			} catch {
				if (mine === request) {
					root.replaceChildren(message("The heatmaps can't be loaded right now."));
				}
			}
		}

		load();
	}

	document.addEventListener("DOMContentLoaded", () => {
		const root = document.getElementById("app");
		if (root && document.body.dataset.page === "heatmaps") {
			initHeatmaps(root);
		}
	});
})();
