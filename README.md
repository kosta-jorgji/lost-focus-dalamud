# lost-focus-dalamud

A Dalamud plugin that reports your in-game activity to an HTTP server of your choice, for live status pages, stats or overlays. Everything is opt-in per category from `/lostfocus`; a category that's off is never read from the game, let alone sent.

## What it sends

| Toggle | Heartbeat fields (every 30s while logged in) | Events |
|---|---|---|
| Online status & playtime | `online` | `login`, `logout`, `playtime` (the game's `/playtime` total in minutes; asked once per login with the reply hidden from chat, and read whenever you type it) |
| Zone, duty & pulls | `zone`, `duty`, `inCombat`, `pullNumber` | `pull`, `duty_complete` |
| Deaths & wipes | – | `death` (with zone / duty when zone sharing is on), `revive` (seconds spent dead), `wipe` |
| Job, level & item level | `job`, `level`, `itemLevel` | – |
| PvP record | `pvp` (Frontline placements, CC rank, series level) | `pvp_match` (placement / win, derived by diffing the PvP profile on leaving a PvP zone) |
| Emote counts | – | `emotes` (uses per emote, batched once per heartbeat) |
| Time per job | `jobActivity`: cumulative seconds per job, longest streak, and the current streak | – |
| Time per mount | `mountActivity`: cumulative seconds per mount ID, ride count, longest ride, and the current ride | – |
| Exact location (**off by default**) | – | `POST /plugin/location` on its own 1–60s interval, only when the map coordinates change: zone, sub-area, map path, in-game X/Y, and in housing districts the ward / plot / apartment. |

### Job and mount timers

Totals are saved in the plugin config and sent as cumulative values, so they survive restarts and a missed heartbeat loses nothing. Loading screens pause the timers without starting a new ride; logout stops them. Standing around on a job or mount still counts. Crafters, gatherers and unnamed quest/duty mounts aren't timed. Ride counts only include rides observed while tracking is on, so reloading the plugin while mounted starts a new ride.

## Server API

Every request is a JSON `POST` with `Authorization: Bearer <secret>`, sent to the configured server URL:

- `/plugin/heartbeat`: a `SnapshotDto` every 30s, immediately on job/mount changes and toggle changes, and with `online: false` on logout, disable or unload.
- `/plugin/event`: an `EventDto` (`type`, ISO `at`, `data`).
- `/plugin/location`: a `LocationDto`.

The shapes are in `LostFocus/Api/StateDto.cs`. Failed requests are logged and dropped, not retried.

## Install

1. Dalamud settings → Experimental → Custom Plugin Repositories → add
   `https://raw.githubusercontent.com/kosta-jorgji/lost-focus-dalamud/main/repo/pluginmaster.json`
2. Install **Lost Focus Tracker** from the plugin installer.
3. `/lostfocus` → paste the server URL and the secret, tick **Enabled**, untick whatever you don't want shared.

## Build

Needs the .NET 10 SDK and a Dalamud install (`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or set `DALAMUD_HOME`).

```
dotnet build LostFocus/LostFocus.csproj -c Release
```

Output: `LostFocus/bin/Release/LostFocus/latest.zip`. For local testing without the repo, add `LostFocus/bin/Release/LostFocus.dll` as a dev plugin in Dalamud settings.

Timer regression checks (no game or Dalamud process required):

```
dotnet run --project tests/ActivityTimers/ActivityTimers.Tests.csproj
```

## Release

Tag `vX.Y.Z` (bump `<Version>` in the csproj first). CI builds, creates a GitHub Release with `LostFocus.zip`, and rewrites `repo/pluginmaster.json` to point at it. The repo has to stay public for the download link to work without auth.

## API level

Built against Dalamud API 15 (`Dalamud.NET.Sdk/15.0.0`). When Dalamud bumps, update the SDK version in the csproj and fix whatever moved.
