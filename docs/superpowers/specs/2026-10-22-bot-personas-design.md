# Bot personas

Bots become persistent players. Each bot keeps its progress across maps and days, earns everything a human earns, and has its own player page, so the server's regular bots become characters you can compare yourself against.

## Why

The server usually has one human and a bot quota of 10. Today bots are anonymous: they earn nothing, appear nowhere but the live map, and every kill on them counts as a kill on "a bot". The owner wants each bot to read like a player, such as Muhlik with a rank, a favourite weapon, and a record against you, seen exactly as any other player would be.

## What changes for a bot

A bot is tracked exactly like a human player:

- XP and rank, weapon levels and mastery stars, challenge ladders, skill shots, feats, contracts, the play streak, season XP and standings, per-map totals, rivals and head-to-heads, and kill positions for heatmaps.
- The same rules and the same numbers as a human. A bot earning XP against a bot, or against a human, uses exactly the rule a human uses for the same event, including the bot XP multiplier for a kill on a bot.
- A human's XP for killing a bot is unchanged.
- Nemesis XP and bounties work with bots exactly as between humans (the owner's call). A bot can be a human's nemesis, and killing that bot pays the nemesis bonus. A bot can carry a bounty, which a human or another bot collects by ending its streak, and a bot can collect a human's bounty.
- Bots are tracked only while at least one human is on the server (design author's call). CS2 adds bots only once a human joins today (`bot_join_after_player`), so this is a safeguard: a later config change can't let bots farm XP, streaks, and season standings on an empty server around the clock.

## Identity

- A bot's identity is its name. CS2 draws bot names from a fixed list, so the same names keep coming back, and the same name is the same persona every time.
- Each bot name gets a stable player id that never collides with a real SteamID64 and stays the same across map changes, plugin restarts, and server restarts. The id is assigned the first time the name is seen and kept from then on.
- Ids for bots sit in a range the site can recognise as bot ids on its own, without a database lookup, so every page can mark a bot from its id.
- A human who happens to use a bot's name is a different player: humans stay keyed by SteamID64.

## Where bots appear

Mixed in with humans, and marked as bots everywhere a player's name is shown:

- Player pages: a bot has the same page as a human (rank, tabs, its own feed, fights heatmap, live marker, share card, compare link), with a bot marker beside the name.
- Ranks, ladder, leaderboards (every board), seasons, the compare page, rivals and head-to-heads, the live scoreboard, and the live map.
- The marker is a small consistent badge or label, the same on every page. Its exact look is the builder's call.
- In-game lists and reports too (design author's call, following "the same as any other player"): `!top` and `!top <weapon>`, `!stats <name>`, and the nemesis and victim report include bots, marked `[BOT]` in chat text.
- The server-wide `/heatmaps` page includes bots' kills and deaths (design author's call): bots are players now.

## Kill history

- Kills between two bots are stored in `csp_kills` so a bot's own fights map has history (design author's call).
- They must never push human-involved kills out of the per-map cap. Kills with a human on either end keep the existing cap (`MaxPointsPerMap`), and kills between two bots are kept under their own cap, so the owner's All time history is unchanged in depth. How the two caps are kept apart is the builder's call.

## Where bots don't appear

- **Server records.** A bot never holds a server record. A bot can't break a record, and a bot's achievements are never checked against records. Humans' records, and whether a human's kills on bots count toward them (`RecordsCountBots`), are unchanged.
- **Global feeds and announcements.** A bot's moments (skill shots, rank-ups, weapon levels, feats, contracts, mastery stars) don't appear in:
  - the home page live feed,
  - Discord posts, including the weekly digest,
  - in-game chat announcements broadcast to the server.
- A bot's moments still appear on that bot's own player page feed.
- **Anything aimed at a person.** Bots have no client, so nothing is drawn or sent to them: no panel, no chat lines, no welcome message, no floaties.
- **The admin panel** (the CSS-BANS panel and SimpleAdmin) stays human-only. The plugin's own correction commands, `css_csp_grant` and `css_csp_reset`, can target a bot persona by its name (design author's call), so a persona that goes wrong can be corrected or reset.

## Constraints

- The plugin and site ship together from this repo.
- Existing tables keep their shape and their human rows. Adding columns or tables is allowed, but the site's database user is read-only with `SELECT` on a fixed list of tables, so any new table the site reads needs a matching grant in the homelab (`files/cs2-progression-site-db.sh`). Prefer designs where the site needs no new table.
- Nothing slow on the game thread. Bot progress loads and saves the same way human progress does, off the game thread.
- Records stay human-only even when `RecordsCountBots` is true.
- A bot that joins and leaves often (the quota adds and kicks bots as humans come and go) must not lose progress, double-count, or pile up duplicate personas.
- MSTest with semantic asserts, tabs, file-scoped namespaces, no global warning suppressions, `Endpoints` at its CA1506 coupling limit so new handlers go in their own classes.

## Non-goals

- Bot behaviour, difficulty, or which bots join. That's the game's.
- Separate bot-only boards.
- Renaming or merging bot personas.
- Backfilling history for bots from before this change.

## Left to the builder

- The id scheme within the constraints above, and where the name-to-id mapping is kept.
- How the plugin's human-only checks are split between "track this player" (now humans and bots) and "talk to this player" (still humans only).
- How the site keeps a bot's moments for its own page while leaving them out of the global feed.
- The bot marker's look.
