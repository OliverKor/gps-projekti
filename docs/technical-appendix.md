# Technical Appendix

## 1. System Architecture

### 1.1 Runtime flow
1. `LiveGpsSession` reads bytes from serial (`System.IO.Ports`) off the UI thread.
2. `UbxNavPvtStreamDecoder` feeds `UbxStreamParser` and extracts valid NAV-PVT fixes.
3. Fixes are emitted to `MainWindow` on `Dispatcher`.
4. UI renders:
   - map overlays (`Real map` or `Local XY`)
   - fix table
   - telemetry panel (fix-rate/age/distance/MQTT health)
5. If MQTT is enabled, `MqttSessionCoordinator` publishes:
   - fix updates
   - diagnostics
   - alerts
   - retained status (`online`/`offline`) with Last Will fallback

### 1.2 Components
- `src/Gps.Core`
  - UBX parser and decoder
  - fix model + CSV reader/writer
  - telemetry metrics and alert rules
- `src/Gps.Ui.Wpf`
  - serial session lifecycle
  - map and table rendering
  - MQTT configuration/publisher/coordinator
- `infra/`
  - Mosquitto broker (`1883`)
  - Node-RED dashboard (`1880`)

## 2. UBX NAV-PVT Decode Policy

Accepted frame:
- UBX class `0x01`, id `0x07`
- payload length `92`
- valid checksum

Accepted fix:
- `validDate`, `validTime`, and `fullyResolved` flags required
- `gnssFixOk` required
- positional fix type required (`2D`, `3D`, `GNSS+DR`)

Filtering behavior:
- invalid payload/date-time/flags are skipped
- checksum failures recover by sync scanning
- repeated timestamp fixes are deduplicated

## 3. MQTT Topic Contract

Base topic format: `<baseTopic>/<deviceId>/...`

Topics:
- `fix`
- `diag`
- `alert`
- `status` (retained)

QoS/retention:
- QoS: at least once
- `status` uses retained publish
- Last Will publishes `offline/unexpected_disconnect` on ungraceful termination

Diagnostics payload includes:
- fix rate
- last-fix age
- no-fix seconds
- queue depth
- dropped count
- publish failures

## 4. Alert Catalog

Existing:
- `SPEED_JUMP` (`warning`): abrupt speed delta between close consecutive fixes
- `NO_FIX_10S` (`warning`): no valid fix for >= 10 seconds
- `NO_FIX_30S` (`critical`): no valid fix for >= 30 seconds

Configurable alert rules:
- `speedLimitMps`
  - `SPEED_LIMIT_EXCEEDED` (`warning`): crossing from `<= limit` to `> limit`
  - `SPEED_LIMIT_RECOVERED` (`info`): crossing from `> limit` to `<= limit`
- circular geofence (`geofenceCenterLat`, `geofenceCenterLon`, `geofenceRadiusM`)
  - `GEOFENCE_EXIT` (`warning`): inside -> outside transition
  - `GEOFENCE_ENTER` (`info`): outside -> inside transition

Transition rules:
- no repeated alerts while state remains unchanged
- first fix initializes speed/geofence state and does not emit transition alerts

## 5. Configuration Notes

MQTT settings file:
- path: `AppContext.BaseDirectory/mqttsettings.json`
- shipped default has `enabled: true`
- missing/invalid file yields explicit `CONFIG ERROR` state in UI and MQTT is disabled

Optional telemetry rule fields:
- `speedLimitMps` (nullable, > 0 enables speed-limit rule)
- `geofenceCenterLat` (nullable, -90..90)
- `geofenceCenterLon` (nullable, -180..180)
- `geofenceRadiusM` (nullable, > 0)

Geofence is enabled only when all geofence fields are present and valid.

## 6. Operational Limitations and Assumptions

- WPF runtime target is Windows (`net10.0-windows`).
- Real map mode depends on external tile connectivity.
- Local Mosquitto demo config allows anonymous local clients (demo use).
- UI telemetry panel is presentation-oriented; MQTT publishing stays asynchronous with bounded queue.

## 7. Demo Runbook Checklist

1. Start demo infra:
   - `docker compose -f infra/docker-compose.yml up -d --build`
2. Run app:
   - `dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj`
3. Connect GNSS serial port and confirm:
   - fixes update in table/map
   - telemetry panel updates fix rate/age/distance
4. Open Node-RED dashboard:
   - `http://localhost:1880/ui`
5. Validate MQTT health:
   - queue depth, dropped, publish failures
   - no config error in status
6. Optional alert validation:
   - set speed/geofence settings and verify alert messages on topic/dashboard
