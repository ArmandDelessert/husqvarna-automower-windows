<p align="center">
  <img src="src\HusqaCockpit.App\Assets\AppIcon.svg" alt="Logo de HusqA Cockpit" width="160">
</p>

# HusqA Cockpit

An unofficial Windows client for Husqvarna Automower® robotic lawnmowers, developed in C# / .NET.

HusqA Cockpit (**Husq**varna **A**utomower) shows all the mowers of a Husqvarna account side by side, lets you
control them from the desktop and raises Windows notifications when one of them needs attention.
It uses the official [Automower Connect API](https://developer.husqvarnagroup.cloud/apis/automower-connect-api).

## Features

- **Dashboard** with one card per mower: activity, battery, next scheduled start, current error, cutting height,
  headlights and time of the last update.
- **Commands**: mow for a given duration, pause, park (until the next schedule, until further notice or for a duration),
  resume the schedule — per mower or for all mowers at once.
- **Mower details**: cutting height and headlight settings, lifetime statistics, event history, blade-usage counter
  reset, error acknowledgement.
- **Schedule editor**: add, change and remove the weekly time slots of a mower (start, end, days). Overlapping slots,
  empty days and slots running past midnight are rejected before anything is sent.
- **Map**: an OpenStreetMap map with the GPS track of a mower on its detail page, and a *Map* page showing the tracks
  of all mowers together.
- **Windows notifications** for errors, theft alarms, recoveries, stops requiring a manual action and connectivity changes
  (each category can be turned off).
- **Notification area icon**: the app keeps watching the mowers when its window is closed; the icon gets a red badge when
  a mower is in error. Optional start with Windows.
- **French and English** user interface (follows Windows by default).

## Getting started

### 1. Create a Husqvarna API key

1. Sign in to the [Husqvarna developer portal](https://developer.husqvarnagroup.cloud/) with your Automower Connect account.
2. Create an application, then connect it to the **Authentication API** and the **Automower Connect API**.
3. Copy the application key and the application secret.

### 2. Run the app

Requirements: Windows 10 (19041) or later, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
and the Microsoft Edge WebView2 runtime (preinstalled on Windows 11; used for the map).
Visual Studio is not required.

```bash
dotnet run --project src/HusqaCockpit.App
```

On first launch, open **Settings**, paste the key and secret, then click **Save and connect**.
They are stored in the Windows Credential Manager of the current user, never in plain text.

### Build a distributable folder

```bash
dotnet publish src/HusqaCockpit.App -c Release -r win-x64 -o artifacts/publish/win-x64
```

Use `-r win-arm64` for ARM devices. The output folder carries the Windows App SDK (no runtime installer needed)
and only requires the [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Start `HusqaCockpit.exe`.

## How it works

| Concern | Approach |
|---|---|
| Authentication | OAuth2 client credentials. The 24-hour token is cached in the Credential Manager: Husqvarna rejects logins that are too frequent (`simultaneous.logins`). |
| Live updates | WebSocket `wss://ws.openapi.husqvarna.dev/v1` (v2 events), with keep-alive and reconnection before the server's 2-hour limit. |
| Fallback | If the WebSocket is unavailable (e.g. HTTP 403), the app polls `GET /mowers` periodically (10 minutes by default). |
| Rate limits | Requests are serialized and spaced by ≥ 1.1 s (limit: 1 request/second). The Settings page shows this month's request count (limit: 10,000 per month and per key). |
| Map | [Leaflet](https://leafletjs.com/) (bundled, BSD-2-Clause) in a WebView2; only the OpenStreetMap tiles are loaded from the Internet. The track is made of the last 50 positions reported by the mower. |
| Time stamps | Next start, error and message times are sent in the mower's local time; they are interpreted in the PC's time zone. |

## Project structure

```
src/HusqaCockpit.Core          API client, WebSocket stream, fleet state and alert detection (no UI, unit-tested)
src/HusqaCockpit.App           WinUI 3 application (unpackaged): views, view models, tray icon, notifications
tests/HusqaCockpit.Core.Tests  xUnit tests for the Core library
tools/strings                  Source of the UI strings and error-code texts (generates the .resw files)
tools/icons                    Script that draws the application icons
```

Run the tests with `dotnet test`.

### Translations

Edit `tools/strings/ui_strings.py` or `tools/strings/error_codes.py`, then regenerate the resource files:

```bash
python tools/strings/generate_resw.py
```

## Known limitations

- **Real-time events may be refused (HTTP 403)** for some application keys whose token lacks the `amc:api` scope.
  The app then falls back to periodic refreshes. Renewing the key or reconnecting the Automower Connect API to the
  application on the developer portal usually fixes it.
- The schedule of mowers with work areas (EPOS / NERA models) cannot be edited yet.
- The map shows the last 50 positions reported by the mower, not the full history of the day.
- Work areas and stay-out zones (EPOS / NERA models) are not displayed yet.
- Mowers are assumed to be in the same time zone as the PC.

## Disclaimer

This project is not affiliated with, endorsed by or supported by Husqvarna AB. Automower® is a trademark of Husqvarna AB.

## License

[MIT](LICENSE)
