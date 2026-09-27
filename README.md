# lost-focus-dalamud

**Lost Focus Tracker**, a Dalamud plugin. Sends game state to the [Lost Focus site](https://github.com/kosta-jorgji/lost-focus)'s backend so it can show it live. Everything is opt-in per category from `/lostfocus`; a category that's off is never read from the game, let alone sent.

## What it sends

| Toggle | Heartbeat fields (every 60s) | Events |
|---|---|---|
| Online status & playtime | `online` | `login`, `logout` |
| Zone, duty & pulls | `zone`, `duty`, `inCombat`, `pullNumber` | `pull`, `duty_complete` |
| Deaths & wipes | – | `death`, `revive` (seconds spent dead), `wipe` |
| Job, level & item level | `job`, `level`, `itemLevel` | – |
| PvP record | `pvp` (Frontline placements, CC rank, series level) | `pvp_match` (placement / win, derived by diffing the PvP profile on leaving a PvP zone) |
| Emote counts | – | `emotes` (uses per emote, batched once per heartbeat; the backend only keeps totals) |

## Install (for him)

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

Tag `vX.Y.Z` (bump `<Version>` in the csproj first). CI builds, creates a GitHub Release with `LostFocus.zip`, and rewrites `repo/pluginmaster.json` to point at it. The repo has to stay public for the download link to work without auth.

## API level

Built against Dalamud API 15 (`Dalamud.NET.Sdk/15.0.0`). When Dalamud bumps, update the SDK version in the csproj and fix whatever moved.

## Site

The website and backend live in [kosta-jorgji/lost-focus](https://github.com/kosta-jorgji/lost-focus). The JSON shape in `LostFocus/Api/StateDto.cs` must match `backend/src/types.ts` there.
