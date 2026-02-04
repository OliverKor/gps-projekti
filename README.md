# GPS Project (gps-projekti)

A .NET 10 application suite for reading, parsing, and visualizing GPS data from u-blox GNSS receivers via serial port.

## Overview

This project consists of multiple components:

- **Gps.Cli**: Console application that reads UBX binary protocol from a u-blox GNSS receiver (COM3), decodes NAV-PVT messages, and writes CSV output
- **Gps.Core**: Core library with GPS data structures and parsers (CSV reader for track data)
- **Gps.Ui.Wpf**: WPF user interface for visualizing GPS tracks from CSV files

## Features

### Gps.Cli - Serial GPS Reader

- **Robust UBX framing**: Scans byte stream for UBX sync pattern (0xB5 0x62) with safe resynchronization
- **Checksum validation**: 8-bit Fletcher algorithm (CK_A/CK_B) validation for data integrity
- **Mixed protocol handling**: Tolerates arbitrary interleaving of ASCII NMEA and binary UBX data
- **NAV-PVT decoding**: Extracts position, velocity, time, and satellite information from u-blox receivers
- **CSV logging**: Writes GPS samples to `track.csv` with proper ISO 8601 formatting
- **Rate limiting**: 1 Hz throttling via timestamp deduplication
- **Continuous operation**: Runs until Ctrl+C with graceful shutdown
- **Proper locale handling**: Uses InvariantCulture for CSV (decimal separator: '.')

### CSV Format

Output file: `track.csv`

```csv
timestamp,lat,lon,speed_mps,num_sv,fix_type
2026-02-04T14:15:06.0000000+00:00,62.7905840,22.8185170,0.05,6,3D
```

**Fields:**
- `timestamp`: ISO 8601 with UTC offset (format "o")
- `lat`: Latitude in decimal degrees (F7 format)
- `lon`: Longitude in decimal degrees (F7 format)
- `speed_mps`: Ground speed in m/s (F2 format)
- `num_sv`: Number of satellites used
- `fix_type`: Fix type (NoFix, DR, 2D, 3D, GNSS+DR, TimeOnly)

## Requirements

### Hardware

- u-blox GNSS receiver connected to Windows via serial port (default: COM3)
- Baud rate: 38400 (configurable via command-line arguments)

### Software

- .NET 10 SDK or runtime
- Windows (SerialPort.Read uses Windows-specific I/O)

## Getting Started

### Clone the Repository

```bash
git clone https://github.com/OliverKor/gps-projekti
cd gps-projekti
```

### Build

```bash
dotnet build
```

### Run Gps.Cli

```bash
# Use default COM3 @ 38400 baud
dotnet run --project src/Gps.Cli/Gps.Cli.csproj

# Or specify custom port and baud rate
dotnet run --project src/Gps.Cli/Gps.Cli.csproj -- COM4 115200
```

**Output:**
- Console logs show live GPS samples (1 Hz max)
- CSV file `track.csv` is created/updated in the current directory
- Press Ctrl+C to stop

Example console output:
```
Opening COM3 @ 38400 ...
Opened.
Press Ctrl+C to stop...

=== Debug: Reading bytes to confirm data flow ===
read 256 bytes (total 256)
read 128 bytes (total 384)
...
Debug complete: 50000 bytes in 200 loops.

=== Starting UBX frame parser (continuous mode) ===
LOG 2026-02-04T14:15:06.0000000+00:00 lat=62.790584 lon=22.818517 speed=0.05 sv=6 fix=3D
LOG 2026-02-04T14:15:07.0000000+00:00 lat=62.790590 lon=22.818520 speed=0.06 sv=6 fix=3D
...
^C
Ctrl+C detected, stopping...

Stopped. Parsed 1250 valid UBX frames in 1500 read loops.
```

### Run Gps.Ui.Wpf

```bash
dotnet run --project src/Gps.Ui.Wpf/Gps.Ui.Wpf.csproj
```

Opens a WPF window to visualize GPS tracks from `track.csv`.

## Project Structure

```
gps-projekti/
??? global.json              # .NET version specification
??? README.md               # This file
??? track.csv               # Generated GPS track data (CSV)
??? src/
?   ??? Gps.Cli/            # Console GPS reader application
?   ?   ??? Program.cs      # Serial port reader, UBX parser, NAV-PVT decoder
?   ?   ??? Gps.Cli.csproj
?   ??? Gps.Core/           # Shared library
?   ?   ??? CsvFixReader.cs # CSV parser for track data
?   ?   ??? Gps.Core.csproj
?   ??? Gps.Ui.Wpf/         # WPF user interface
?       ??? MainWindow.xaml(.cs)
?       ??? Gps.Ui.Wpf.csproj
??? .gitignore
```

## Technical Details

### UBX Protocol Parsing

The Gps.Cli parser:

1. **Buffers incoming bytes** from SerialPort.Read (non-blocking with 500ms timeout)
2. **Scans for sync bytes** (0xB5 0x62) in the accumulating buffer
3. **Reads frame header**: class (1 byte), ID (1 byte), payload length (2 bytes, little-endian)
4. **Waits for complete frame**: class + ID + length + payload + checksum (2 bytes)
5. **Validates checksum**: 8-bit Fletcher algorithm over class, ID, length, and payload
6. **On checksum fail**: Resyncs by discarding only the sync bytes (2 bytes) and continues scanning
7. **On success**: Extracts payload, decodes if NAV-PVT (cls=0x01, id=0x07, len=92), and logs to CSV

**Progress guarantees**: Every iteration either removes bytes from buffer or breaks to read more data, preventing infinite loops.

### NAV-PVT Decoding

Extracts from UBX NAV-PVT v1 payload (92 bytes):

| Field | Offset | Type | Description |
|-------|--------|------|-------------|
| year | 4 | U2 | Year (e.g., 2026) |
| month | 6 | U1 | Month (1-12) |
| day | 7 | U1 | Day (1-31) |
| hour | 8 | U1 | Hour (0-23) |
| min | 9 | U1 | Minute (0-59) |
| sec | 10 | U1 | Second (0-59) |
| valid | 11 | U1 | Validity flags (bit0=validDate, bit1=validTime) |
| fixType | 20 | U1 | Fix type (0=NoFix, 1=DR, 2=2D, 3=3D, 4=GNSS+DR, 5=TimeOnly) |
| numSV | 23 | U1 | Number of satellites |
| lon | 24 | I4 | Longitude (1e-7 degrees, little-endian) |
| lat | 28 | I4 | Latitude (1e-7 degrees, little-endian) |
| gSpeed | 60 | I4 | Ground speed (mm/s, little-endian) |

Only samples with both `validDate` and `validTime` flags set are logged.

### SerialPort Configuration

```csharp
ReadTimeout = 500       // ms - prevents hanging on timeouts
WriteTimeout = 500      // ms
DtrEnable = true        // Data Terminal Ready (enables many USB-serial adapters)
Handshake = None        // No flow control
```

### Rate Limiting

CSV output is throttled to **1 Hz** by comparing NAV-PVT timestamps:
- Only writes a line if the timestamp differs from the previous logged timestamp
- Prevents duplicate samples and keeps CSV files manageable

## Troubleshooting

### Program hangs on startup

- Check SerialPort is configured with `ReadTimeout = 500ms` and `DtrEnable = true`
- Verify COM port exists and device is connected
- Try a different COM port: `dotnet run --project src/Gps.Cli/Gps.Cli.csproj -- COM4 38400`

### No GPS samples appear

- Check device is transmitting UBX frames (use a serial terminal to inspect raw bytes)
- Verify NAV-PVT is enabled on the u-blox receiver
- Check `validDate` and `validTime` flags in the NAV-PVT payload (parsing only logs valid timestamps)

### CSV has comma decimal separators instead of periods

- Ensure code uses `CultureInfo.InvariantCulture` for all numeric formatting
- This is already fixed in the current version

### track.csv is empty or missing

- Ensure Gps.Cli has write permission to the directory where it's run
- Check console output for LOG messages (if none appear, no valid NAV-PVT samples were decoded)

## Development Notes

### Adding Support for Other UBX Messages

1. Add message ID constants (cls, id)
2. Extend the frame detection in the parser main loop
3. Implement a `TryDecodeXxxMessage()` method similar to `TryDecodeNavPvt()`
4. Add CSV field mappings and output

### Testing with Simulated Data

To test without hardware, create a mock serial port or load test data into `track.csv` directly.

## License

MIT (or specify your license)

## Repository

- **GitHub**: https://github.com/OliverKor/gps-projekti
- **Branch**: master
- **.NET Target**: .NET 10

## Author

Oliver Kor

---

**Last Updated**: 2026-02-04

For issues or contributions, visit the GitHub repository.
