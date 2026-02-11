# GPS Project (gps-projekti)

Live WPF application for reading UBX binary GNSS data from serial devices and visualizing position updates in real time.

## Overview

This repository now focuses on a single runtime app:

- **Gps.Ui.Wpf**: Live serial reader + map/table UI
- **Gps.Core**: UBX parser/decoder and CSV utilities used by the UI

## Features

- Live UBX stream parsing from serial (`0xB5 0x62` sync, checksum validation, resync on noise)
- NAV-PVT decoding (class `0x01`, id `0x07`, payload `92`)
- Real-time map and fix table updates in WPF
- Connect/Disconnect controls with runtime COM port and baud selection
- Optional CSV logging (`track.csv`), default OFF
- In-memory history cap of 5000 fixes for stable long sessions
- Strict timestamp validity checks (`validDate`, `validTime`, `fullyResolved`)

## Requirements

- Windows
- .NET 10 SDK
- u-blox (or compatible) GNSS receiver exposed as a serial COM port

## Run

```bash
dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj
```

## WPF Usage

1. Start app.
2. Select COM port and baud rate.
3. Optionally check `Log to CSV (track.csv)`.
4. Click `Connect`.
5. Watch live fixes on map + table.
6. Click `Disconnect` to stop session.

CSV logging toggle is disabled while connected to keep behavior deterministic.

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
    Gps.Ui.Wpf/      # Live serial session + WPF UI
  tests/
    Gps.Core.Tests/  # Parser/decoder/CSV tests
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

## Testing

Run all solution tests:

```bash
dotnet test gps-projekti.sln
```

## Repository

- GitHub: https://github.com/OliverKor/gps-projekti
