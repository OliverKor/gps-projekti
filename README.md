# GPS Project (gps-projekti)

Live WPF application for reading UBX binary GNSS data from serial devices and visualizing position updates in real time.

## Overview

This repository now focuses on a single runtime app:

- **Gps.Ui.Wpf**: Live serial reader + map/table UI
- **Gps.Core**: UBX parser/decoder and CSV utilities used by the UI

## Features

- Live UBX stream parsing from serial (`0xB5 0x62` sync, checksum validation, resync on noise)
- NAV-PVT decoding (class `0x01`, id `0x07`, payload `92`)
- Real-time map modes in WPF: `Real map` (OSM basemap) and `Local XY` (meter-projected)
- Connect/Disconnect controls with runtime COM port and baud selection
- Optional CSV logging (`track.csv`), default OFF
- In-memory history cap of 5000 fixes for stable long sessions
- Strict timestamp validity checks (`validDate`, `validTime`, `fullyResolved`)
- Optional MQTT publishing (`fix`, `diag`, `alert`, `status`) with reconnect and bounded queue
- Live telemetry panel in WPF with fix rate, last-fix age, distance, and MQTT health
- Optional speed-limit and circular geofence alerts on the MQTT `alert` topic
- Local demo infra (`Mosquitto` + `Node-RED`) for a one-panel telemetry dashboard

Technical appendix: `docs/technical-appendix.md`

## Requirements

- Windows
- .NET 10 SDK
- u-blox (or compatible) GNSS receiver exposed as a serial COM port
- Docker Desktop (or Docker Engine via WSL2) for local Mosquitto/Node-RED demo stack

## Run

```bash
dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj
```

## MQTT Demo (Localhost)

### 1. Start local broker + dashboard

From repository root:

```bash
docker compose -f infra/docker-compose.yml up -d --build
```

Services:

- Mosquitto broker: `localhost:1883`
- Node-RED dashboard editor: `http://localhost:1880`

### 2. Review MQTT app settings

Edit `mqttsettings.json` in the app output folder (`AppContext.BaseDirectory`).

When running with `dotnet run`, this is typically:

- `src/Gps.Ui.Wpf/bin/Debug/net10.0-windows/`

Use this sample:

```json
{
  "enabled": true,
  "host": "localhost",
  "port": 1883,
  "baseTopic": "gps/v1",
  "deviceId": "demo-truck-01",
  "username": "",
  "password": "",
  "diagIntervalSeconds": 5,
  "queueCapacity": 500,
  "drainTimeoutSeconds": 2,
  "speedLimitMps": null,
  "geofenceCenterLat": null,
  "geofenceCenterLon": null,
  "geofenceRadiusM": null
}
```

`enabled` defaults to `true` in the shipped config. If the config file is missing or invalid, the app reports a visible MQTT config error and keeps MQTT disabled.

### 3. Run app and connect GPS

```bash
dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj
```

Node-RED dashboard path:

- `http://localhost:1880/ui`
- Dashboard speed widgets display values in `km/h`.
- MQTT `fix` payload remains unchanged (`speedMps`, `averageSpeedMps` are still in m/s).

### Topics

- `gps/v1/<deviceId>/fix`
- `gps/v1/<deviceId>/diag`
- `gps/v1/<deviceId>/alert`
- `gps/v1/<deviceId>/status` (retained, with MQTT Last Will offline fallback)

## WPF Usage

1. Start app.
2. Select COM port and baud rate.
3. Optionally check `Log to CSV (track.csv)`.
4. Click `Connect`.
5. Choose map mode:
   - `Real map`: OpenStreetMap basemap with live GPS overlay.
   - `Local XY`: meter-projected local track view.
6. Watch live fixes on map + table.
7. Observe the telemetry panel below status (fix rate, last-fix age, distance, MQTT health).
8. Click `Disconnect` to stop session.

CSV logging toggle is disabled while connected to keep behavior deterministic.

`Real map` requires internet access for tiles. If repeated tile fetch failures occur, the app falls back to `Local XY`.

## CSV Output

When logging is enabled, rows are appended to `track.csv` in the app base directory:

```csv
timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m
2026-02-11T12:00:00.0000000+00:00,62.7905840,22.8185170,0.05,6,3D,0.00,0.00
```

## Project Structure

```text
gps-projekti/
  src/
    Gps.Core/        # UBX parsing + NAV-PVT decoding + CSV read/write
    Gps.Ui.Wpf/      # Live serial session + WPF UI + optional MQTT publish
  docs/
    technical-appendix.md
  infra/
    mosquitto/       # Local broker config
    node-red/        # Local dashboard image + flow
  tests/
    Gps.Core.Tests/  # Parser/decoder/CSV + telemetry queue/metrics tests
```

## Troubleshooting

### No serial ports found

- Confirm GNSS receiver is connected.
- Click `Refresh Ports`.
- Check Windows Device Manager for COM assignment.

### Failed to open serial port

- Port may be busy (another app is using it).
- Verify port name and baud rate.
- Reconnect the USB serial device and retry.

### No live fixes despite connected state

- Ensure device outputs UBX NAV-PVT messages.
- Mixed NMEA + UBX is supported, but only valid NAV-PVT fixes are displayed.
- Samples with unresolved GNSS date/time are intentionally skipped.

### CSV not created

- Enable `Log to CSV (track.csv)` before connecting.
- Check app directory write permissions.

### MQTT is not publishing

- Verify `enabled: true` in output-folder `mqttsettings.json`.
- Check for `CONFIG ERROR (...)` in app status if settings are missing/invalid.
- Ensure Mosquitto is running on `localhost:1883`.
- Confirm Docker services with `docker compose -f infra/docker-compose.yml ps`.
- Check topic activity:

```bash
docker exec -it $(docker ps --filter name=mosquitto --format "{{.ID}}") \
  mosquitto_sub -h localhost -t 'gps/v1/+/+' -v
```

### Node-RED dashboard unavailable

- Verify container is running and `http://localhost:1880` opens.
- If port 1880 is in use, remap it in `infra/docker-compose.yml`.

## Testing

Run all solution tests:

```bash
dotnet test gps-projekti.sln -p:EnableWindowsTargeting=true
```

## Repository

- GitHub: https://github.com/OliverKor/gps-projekti
