## v1.21.0 (minor)

Changes since v1.20.0:

- Merge main into the ten-challenges branch (Claude)
- [minor] Let players reroll the "try something new" suggestion (Claude)
- [minor] Announce the progression site's domain at match start and end (Claude)
- Show ten recommended challenges [patch] (Claude)

## v1.20.0 (minor)

Changes since v1.19.0:

- Add a SessionStart hook for Claude Code cloud sessions (Claude)
- Merge main (per-map stats) into compare two players (Claude)
- [patch] Scale a clutch against only bots by BotXpMultiplier (Claude)
- Merge main (server records) into compare two players (Claude)
- Merge main (heatmaps) into compare two players (Claude)
- Merge main into compare two players (Claude)
- Look up the second player's weapon by key instead of a null-forgiving fallback (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-u0zzyj (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-u0zzyj (Claude)
- [minor] Add a page that compares two players side by side (Claude)

## v1.19.2 (patch)

Changes since v1.19.1:

- Add a SessionStart hook for Claude Code cloud sessions (Claude)

## v1.19.1 (patch)

Changes since v1.19.0:

- [patch] Scale a clutch against only bots by BotXpMultiplier (Claude)

## v1.19.0 (minor)

Changes since v1.18.0:

- [patch] Keep hits on teammates out of life accuracy (Claude)
- Merge main (server records) into per-map stats (Claude)
- Merge main (heatmaps, bounty, play streak, digest) into per-map stats (Claude)
- Merge main (rivals) into map stats (Claude)
- Fix the share card test for the weapon view's mastery star fields (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-akwjjz (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-akwjjz (Claude)
- Cover the map endpoints' database errors and the merge, and drop a duplicated CSS selector (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-akwjjz (Claude)
- [minor] Add per-map stats, a best map, and a board per map (Claude)

## v1.18.1 (patch)

Changes since v1.18.0:

- [patch] Keep hits on teammates out of life accuracy (Claude)

## v1.18.1-pre.1 (prerelease)

Changes since v1.18.0:

- Bump CounterStrikeSharp.API from 1.0.376 to 1.0.377 (dependabot[bot])

## v1.18.0 (minor)

Changes since v1.17.0:

- Merge main (heatmaps, bounty, rivals, play streak, digest) into server records (Claude)
- Keep the plugin's record wiring small (Claude)
- Pass the mastery stars in the share card test's weapons (Claude)
- Merge main into claude/project-thread-ojc8x1 (Claude)
- Merge main into claude/project-thread-ojc8x1 (Claude)
- Move record saving and wording into Core, and cover them (Claude)
- [minor] Add server records (Claude)
- Merge main into claude/project-thread-ojc8x1 (Claude)
- WIP server records (Claude)

## v1.17.0 (minor)

Changes since v1.16.0:

- [minor] Add kill heatmaps over each map's radar (#38) (matt-edmondson)
- Merge main into the play streak branch (Claude)
- Merge main (nemesis and rivals) into the play streak branch (Claude)
- Fix the card test for WeaponView's mastery star fields (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-aluulv (Claude)
- Word the player page's streak line on the server (Claude)
- Merge main into the play streak branch (Claude)
- Read the daily rows in one statement (Claude)
- Merge main into the play streak branch, and build the streak in Rebuild (Claude)
- [minor] Add a daily play streak with Regular and Devoted feats (Claude)

## v1.16.0 (minor)

Changes since v1.15.0:

- [minor] Put a bounty on the leading life streak (#29) (matt-edmondson)
- Merge main (nemesis and rivals) (Claude)
- Merge main, and fix CardTests for the mastery star fields (Claude)
- Return a completed task rather than null when no digest check starts (Claude)
- Move the weekly digest's scheduling into Core so it's tested (Claude)
- Merge main (Claude)
- [minor] Post a weekly Discord digest every Monday (Claude)

## v1.15.0 (minor)

Changes since v1.14.0:

- [minor] Nemesis and rivals (#35) (matt-edmondson)
- Append the rune directly when filtering card names (Claude)
- Merge main into share cards (Claude)
- [minor] Add share cards and link previews for player pages (Claude)
- Cover the stars in !stats and !top (Claude)
- Test the ingest counting middleware and the metrics route (Claude)
- Merge main into claude/project-thread-88d2z4 (Claude)
- Merge main (Claude)
- [minor] Add weapon mastery stars past the top weapon level (Claude)
- Document the metrics in the README (Claude)
- [minor] Add Prometheus metrics to the site, with the plugin's health counters (Claude)

## v1.14.0 (minor)

Changes since v1.13.0:

- Make the welcome-back test's half pool an explicit whole number (Claude)
- [minor] Add Untouchable, kill streak skill shots, and the Arsenal and Polymath feats (Claude)
- [minor] Add a welcome-back bonus for returning players (Claude)
- [patch] Keep featured weapons to extra weapon XP, not faster ladders (matt-edmondson)

## v1.13.1 (patch)

Changes since v1.13.0:

- [patch] Keep featured weapons to extra weapon XP, not faster ladders (matt-edmondson)

## v1.13.0 (minor)

Changes since v1.12.0:

- [minor] Add daily and weekly contracts (#13) (matt-edmondson)

## v1.12.0 (minor)

Changes since v1.11.0:

- [minor] Add XP history graphs and an XP this week board (#14) (matt-edmondson)
- Merge main into claude/project-thread-6b1z3i (Claude)
- Keep the history refetch out of the player load's try, and cover the weekly route (Claude)
- Merge main into claude/project-thread-6b1z3i (Claude)
- Parse the MariaDB test target without an ambiguous Split overload (Claude)
- Merge main into claude/project-thread-6b1z3i (Claude)
- [minor] Add XP history graphs and an XP this week board (Claude)

## v1.11.0 (minor)

Changes since v1.10.0:

- Merge main into claude/project-thread-3vaa00 (Claude)
- Merge main into claude/project-thread-3vaa00 (Claude)
- Merge main into seasons (Claude)
- Pass an explicit token when starting the Discord reader (Claude)
- Implement the weapon leaderboard read on the failing test store (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-s59t3q (Claude)
- Merge main into claude/project-thread-3vaa00 (Claude)
- Drop the empty header column from the season board (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-s59t3q (Claude)
- Match Discord notices with property patterns so the null checks carry through (Claude)
- Cover the season reads and simplify the live season overlay (Claude)
- Dispose the Discord sender's cancellation source and simplify its retry loop (Claude)
- [minor] Add quarterly seasons with a season board and badges (Claude)
- Post feats, epic skill shots, and high rank-ups to a Discord webhook [minor] (Claude)

## v1.10.0 (minor)

Changes since v1.9.0:

- [minor] Add a featured weapon class of the day (#6) (matt-edmondson)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-4a8m2s (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-4btt0h (Claude)
- Merge remote-tracking branch 'origin/main' into claude/project-thread-f720x9 (Claude)
- Describe the featured class in Core and order the banner styles by specificity (Claude)
- Make the test null checks explicit and filter the target loop up front (Claude)
- Move the correction logic into Core so it's tested (Claude)
- Cover the rotation helpers and give week dates a UTC kind (Claude)
- Add weapon and skill shot leaderboards and a skill shot guide [minor] (Claude)
- [minor] Add admin commands to grant XP and reset a player (Claude)
- [minor] Add a featured weapon class of the day (Claude)

## v1.9.0 (minor)

Changes since v1.8.0:

- Move the empty-recap check into Core and cover the recap edge cases (Claude)
- Move the welcome and !stats lookup into Core, with tests (Claude)
- Add a map recap in chat and a last session on the player page [minor] (Claude)
- Filter connected players with LINQ in !stats (Claude)
- Add a join message and more chat commands [minor] (Claude)
- Show rank as the clan tag and a chat prefix [minor] (Claude)

## v1.8.0 (minor)

Changes since v1.7.0:

- Merge main into claude/player-page-tabs-a6cz6a (Claude)
- Keep the player feed beside the tabs (Claude)
- Pick the player tab renderer with a switch (Claude)
- Look up the player tab renderer in a Map (Claude)
- [minor] Split the player page into tabs and rework weapon challenges (Claude)
- [minor] Add a ladder page with player counts and an icon for every challenge (matt-edmondson)

## v1.7.0 (minor)

Changes since v1.6.0:

- [minor] Add feats and draw a distinct badge for each rank (matt-edmondson)

## v1.6.0 (minor)

Changes since v1.5.0:

- [minor] Add twelve chicken skill shots (matt-edmondson)

## v1.5.0 (minor)

Changes since v1.4.0:

- [minor] Add a Chickens challenge to every weapon (matt-edmondson)
- [minor] Fold numbered ranks into one rank each, so stage names never double up (matt-edmondson)

## v1.4.0 (minor)

Changes since v1.3.0:

- [minor] Split each rank into five stages, raise the rank XP, and show rank badges (matt-edmondson)
- [patch] Caption feed lines with how the moment was earned (matt-edmondson)

## v1.3.0 (minor)

Changes since v1.2.0:

- [minor] Add a player XP feed and next challenge suggestions to the player page (matt-edmondson)

## v1.2.0 (minor)

Changes since v1.1.0:

- [minor] Add the live progression site (matt-edmondson)
- [patch] Draw XP floaties 50% bigger (matt-edmondson)
- Ignore local worktrees (matt-edmondson)
- Add the progression site plan (matt-edmondson)
- Add the progression site design (matt-edmondson)
- [patch] Draw XP floaties in the world at a fixed size (matt-edmondson)
- [patch] Leave HUD XP floaties where the crosshair was (matt-edmondson)

## v1.1.3 (patch)

Changes since v1.1.2:

- [patch] Draw XP floaties 50% bigger (matt-edmondson)
- Ignore local worktrees (matt-edmondson)
- Add the progression site plan (matt-edmondson)

## v1.1.2 (patch)

Changes since v1.1.1:

- Add the progression site design (matt-edmondson)
- [patch] Draw XP floaties in the world at a fixed size (matt-edmondson)

## v1.1.1 (patch)

Changes since v1.1.0:

- [patch] Leave HUD XP floaties where the crosshair was (matt-edmondson)

## v1.1.0 (minor)

Changes since v1.0.0:

- [minor] Float XP up from the center of the screen (matt-edmondson)
- [patch] Draw XP floaties at their real position by default (matt-edmondson)
- [patch] Draw the floatie outline at half the text's opacity (matt-edmondson)
- [patch] Draw XP floaties in front of walls (matt-edmondson)
- [patch] Shrink XP floaties and give them a black outline (matt-edmondson)
- [patch] Keep XP floaties the same size on screen (matt-edmondson)
- [patch] Retry creating the tables at startup (matt-edmondson)

## v1.0.2 (patch)

Changes since v1.0.1:

- [patch] Draw XP floaties in front of walls (matt-edmondson)
- [patch] Shrink XP floaties and give them a black outline (matt-edmondson)

## v1.0.1 (patch)

Changes since v1.0.0:

- [patch] Keep XP floaties the same size on screen (matt-edmondson)
- [patch] Retry creating the tables at startup (matt-edmondson)

## v1.0.0

Changes since v1.0.0-pre.1:

- [minor] Float XP gains in the world (matt-edmondson)
- [patch] Keep the plugin projects off NuGet (matt-edmondson)

## v1.0.0-pre.1 (prerelease)

- [minor] Add the CounterStrikeProgression plugin (matt-edmondson)

