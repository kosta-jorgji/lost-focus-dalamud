# lost-focus-dalamud

**Lost Focus Tracker**, a Dalamud plugin. Sends game state to the [Lost Focus site](https://github.com/kosta-jorgji/lost-focus)'s backend so it can show it live. Everything is opt-in per category from `/lostfocus`; a category that's off is never read from the game, let alone sent.

## What it sends

| Toggle | Heartbeat fields (every 30s while logged in) | Events |
|---|---|---|
| Online status & playtime | `online` | `login`, `logout`, `playtime` (the game's `/playtime` total in minutes; asked once per login with the reply hidden from chat, and read whenever he types it) |
| Zone, duty & pulls | `zone`, `duty`, `inCombat`, `pullNumber` | `pull`, `duty_complete` |
| Deaths & wipes | – | `death` (with zone / duty when zone sharing is on), `revive` (seconds spent dead), `wipe` |
| Job, level & item level | `job`, `level`, `itemLevel` | – |
| PvP record | `pvp` (Frontline placements, CC rank, series level) | `pvp_match` (placement / win, derived by diffing the PvP profile on leaving a PvP zone) |
| Emote counts | – | `emotes` (uses per emote, batched once per heartbeat; the backend only keeps totals) |
| Time per job (**off by default**) | `jobActivity`: cumulative seconds per job, longest streak, and the current streak | None; cumulative readings are safe to resend |
| Time per mount (**off by default**) | `mountActivity`: cumulative seconds per mount ID, ride count, longest ride, and the current ride | None; each ride appends to that mount's total |
| Exact location (**off by default**) | – | `POST /plugin/location` on its own 1–60s interval, only when the map coordinates change: zone, sub-area, map path, in-game X/Y, and in housing districts the ward / plot / apartment. |

## Install (for him)

Job and mount timers start when their sharing toggles are enabled. Totals are saved in the plugin config and mirrored by the backend, so they survive restarts and missed heartbeats. Loading screens pause time without counting another ride; logout stops the current timers. Standing around on a job or mount still counts. Ride counts represent rides observed while tracking is enabled; reloading the plugin while mounted starts another observed ride.

1. Dalamud settings → Experimental → Custom Plugin Repositories → add
   `https://raw.githubusercontent.com/kosta-jorgji/lost-focus-dalamud/main/repo/pluginmaster.json`
2. Install **Lost Focus Tracker** from the plugin installer.
3. `/lostfocus` → paste the server URL and the secret, tick **Enabled**, untick whatever he doesn't want shared.

## Build

Needs the .NET 10 SDK and a Dalamud install (`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or set `DALAMUD_HOME`).

```
dotnet build LostFocus/LostFocus.csproj -c Release
```

Output: `LostFocus/bin/Release/LostFocus/latest.zip`. For local testing without the repo, add `LostFocus/bin/Release/LostFocus.dll` as a dev plugin in Dalamud settings.

## Release

Timer regression checks (no game/Dalamud process required):

```
dotnet run --project tests/ActivityTimers/ActivityTimers.Tests.csproj
```

Tag `vX.Y.Z` (bump `<Version>` in the csproj first). CI builds, creates a GitHub Release with `LostFocus.zip`, and rewrites `repo/pluginmaster.json` to point at it. The repo has to stay public for the download link to work without auth.

## API level

Built against Dalamud API 15 (`Dalamud.NET.Sdk/15.0.0`). When Dalamud bumps, update the SDK version in the csproj and fix whatever moved.

## Site

The website and backend live in [kosta-jorgji/lost-focus](https://github.com/kosta-jorgji/lost-focus). The JSON shape in `LostFocus/Api/StateDto.cs` must match `backend/src/types.ts` there.
