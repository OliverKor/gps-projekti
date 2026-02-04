using System.Globalization;
using System.IO.Ports;

// Graceful shutdown on Ctrl+C using CancellationTokenSource
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\nCtrl+C detected, stopping...");
};

var portName = args.Length > 0 ? args[0] : "COM3";
var baudRate = args.Length > 1 && int.TryParse(args[1], out var br) ? br : 38400;

using var sp = new SerialPort(portName, baudRate)
{
    ReadTimeout = 500,
    WriteTimeout = 500,
    DtrEnable = true,
    Handshake = Handshake.None
};

Console.WriteLine($"Opening {portName} @ {baudRate} ...");
sp.Open();
Console.WriteLine("Opened.");
Console.WriteLine("Press Ctrl+C to stop...\n");

// ===== Step 1: Debug loop to confirm data is flowing =====
Console.WriteLine("=== Debug: Reading bytes to confirm data flow ===");
var debugBuf = new byte[1024];
int totalDebug = 0;
int loopsDebug = 0;

while (totalDebug < 50000 && loopsDebug < 200 && !cts.IsCancellationRequested)
{
    int n;
    try
    {
        n = sp.Read(debugBuf, 0, debugBuf.Length);
    }
    catch (TimeoutException)
    {
        loopsDebug++;
        continue;
    }
    
    totalDebug += n;
    loopsDebug++;
    Console.WriteLine($"read {n} bytes (total {totalDebug})");
    
    if (totalDebug >= 50000 || loopsDebug >= 200)
    {
        break;
    }
}

Console.WriteLine($"Debug complete: {totalDebug} bytes in {loopsDebug} loops.\n");

// ===== Step 2: UBX Frame Parser with NAV-PVT Decoding (Continuous) =====
Console.WriteLine("=== Starting UBX frame parser (continuous mode) ===");

var buffer = new List<byte>(8192);
int validFrames = 0;
int readLoops = 0;
var lastSyncMsg = DateTime.MinValue;
var lastBufferingMsg = DateTime.MinValue;

// CSV logging setup
var csvPath = "track.csv";
DateTimeOffset? lastLoggedTimestamp = null;

// Create CSV with header if it doesn't exist
if (!File.Exists(csvPath))
{
    File.WriteAllText(csvPath, "timestamp,lat,lon,speed_mps,num_sv,fix_type\n");
    Console.WriteLine($"Created {csvPath}");
}

var inv = CultureInfo.InvariantCulture;

// Outer loop: keep reading from serial port continuously
while (!cts.IsCancellationRequested)
{
    // Read more data from serial port
    var readBuf = new byte[1024];
    int n;
    try
    {
        n = sp.Read(readBuf, 0, readBuf.Length);
        // Add to buffer
        for (int i = 0; i < n; i++)
        {
            buffer.Add(readBuf[i]);
        }
        readLoops++;
    }
    catch (TimeoutException)
    {
        readLoops++;
        // No data this cycle, try to process what we have
    }
    
    // Try to extract UBX frames from buffer
    int extractIterations = 0;
    bool madeProgress = true;
    
    while (madeProgress && !cts.IsCancellationRequested && extractIterations < 1000)
    {
        extractIterations++;
        madeProgress = false;
        int initialBufferSize = buffer.Count;
        
        // Need at least 2 bytes to look for sync
        if (buffer.Count < 2)
        {
            break; // Need more data from serial port
        }
        
        // Search for sync bytes 0xB5 0x62
        int syncIndex = -1;
        for (int i = 0; i <= buffer.Count - 2; i++)
        {
            if (buffer[i] == 0xB5 && buffer[i + 1] == 0x62)
            {
                syncIndex = i;
                break;
            }
        }
        
        if (syncIndex == -1)
        {
            // No sync found - discard all but last byte (might be partial 0xB5)
            if ((DateTime.Now - lastBufferingMsg).TotalSeconds >= 1.0 && buffer.Count > 100)
            {
                Console.WriteLine($"no sync yet, buffering ... ({buffer.Count} bytes)");
                lastBufferingMsg = DateTime.Now;
            }
            
            if (buffer.Count > 1)
            {
                buffer.RemoveRange(0, buffer.Count - 1);
                madeProgress = true;
            }
            break; // Need more data
        }
        
        // Sync found at syncIndex
        // Need at least: sync(2) + class(1) + id(1) + length(2) = 6 bytes
        if (buffer.Count < syncIndex + 6)
        {
            // Not enough bytes for header - discard bytes before sync and wait for more
            if ((DateTime.Now - lastSyncMsg).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"SYNC found at {syncIndex}, waiting for more bytes...");
                lastSyncMsg = DateTime.Now;
            }
            
            if (syncIndex > 0)
            {
                buffer.RemoveRange(0, syncIndex);
                madeProgress = true;
            }
            break; // Need more data
        }
        
        // Read header
        byte cls = buffer[syncIndex + 2];
        byte id = buffer[syncIndex + 3];
        int payloadLen = buffer[syncIndex + 4] | (buffer[syncIndex + 5] << 8); // little-endian
        
        // Sanity check on payload length
        if (payloadLen > 4096)
        {
            // Invalid length, likely false sync - discard sync bytes (2 bytes) and continue
            buffer.RemoveRange(0, syncIndex + 2);
            madeProgress = true;
            continue;
        }
        
        // Calculate total frame length: sync(2) + class(1) + id(1) + len(2) + payload + checksum(2)
        int frameLen = 2 + 4 + payloadLen + 2; // = payloadLen + 8
        
        if (buffer.Count < syncIndex + frameLen)
        {
            // Not enough bytes for complete frame - discard bytes before sync and wait
            if ((DateTime.Now - lastSyncMsg).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"SYNC found at {syncIndex}, waiting for more bytes... (need {frameLen}, have {buffer.Count - syncIndex})");
                lastSyncMsg = DateTime.Now;
            }
            
            if (syncIndex > 0)
            {
                buffer.RemoveRange(0, syncIndex);
                madeProgress = true;
            }
            break; // Need more data
        }
        
        // We have a complete frame - validate checksum
        // Checksum is computed over: class, id, length(2 bytes), payload
        // That's bytes from syncIndex+2 to syncIndex+5+payloadLen
        byte ckA = 0, ckB = 0;
        for (int i = syncIndex + 2; i < syncIndex + 6 + payloadLen; i++)
        {
            ckA += buffer[i];
            ckB += ckA;
        }
        
        byte expectedCkA = buffer[syncIndex + 6 + payloadLen];
        byte expectedCkB = buffer[syncIndex + 6 + payloadLen + 1];
        
        if (ckA == expectedCkA && ckB == expectedCkB)
        {
            // Valid frame!
            validFrames++;
            
            // Check if this is NAV-PVT (cls=0x01, id=0x07, len=92)
            if (cls == 0x01 && id == 0x07 && payloadLen == 92)
            {
                // Extract payload
                var payload = new byte[payloadLen];
                for (int i = 0; i < payloadLen; i++)
                {
                    payload[i] = buffer[syncIndex + 6 + i];
                }
                
                // Try to decode NAV-PVT
                if (TryDecodeNavPvt(payload, out var pvt))
                {
                    // Rate limit: only log if timestamp is different from last logged (1 Hz throttle)
                    if (lastLoggedTimestamp == null || pvt.Timestamp != lastLoggedTimestamp)
                    {
                        lastLoggedTimestamp = pvt.Timestamp;
                        
                        // Write to CSV with InvariantCulture (uses '.' decimal separator)
                        var csvLine = string.Format(inv,
                            "{0},{1:F7},{2:F7},{3:F2},{4},{5}\n",
                            pvt.Timestamp.ToString("o"),
                            pvt.LatitudeDeg,
                            pvt.LongitudeDeg,
                            pvt.SpeedMps,
                            pvt.NumSv,
                            pvt.FixType);
                        
                        File.AppendAllText(csvPath, csvLine);
                        
                        // Print console log with InvariantCulture
                        Console.WriteLine(string.Format(inv,
                            "LOG {0} lat={1:F6} lon={2:F6} speed={3:F2} sv={4} fix={5}",
                            pvt.Timestamp.ToString("o"),
                            pvt.LatitudeDeg,
                            pvt.LongitudeDeg,
                            pvt.SpeedMps,
                            pvt.NumSv,
                            pvt.FixType));
                    }
                }
            }
            
            // Discard this frame (including any junk before sync)
            buffer.RemoveRange(0, syncIndex + frameLen);
            madeProgress = true;
        }
        else
        {
            // Checksum failed - discard only the sync bytes (2 bytes) and continue scanning
            buffer.RemoveRange(0, syncIndex + 2);
            madeProgress = true;
        }
        
        // Safety check: did we make progress?
        if (buffer.Count >= initialBufferSize)
        {
            // No progress made - this shouldn't happen, but break to avoid infinite loop
            Console.WriteLine($"WARNING: No buffer reduction in iteration {extractIterations}");
            break;
        }
    }
    
    // Safety: if we looped too many times without finding valid frames, clear buffer
    if (extractIterations >= 1000)
    {
        Console.WriteLine($"WARNING: Extraction loop hit 1000 iterations without progress. Clearing buffer ({buffer.Count} bytes).");
        buffer.Clear();
    }
    
    // Safety: prevent buffer from growing too large
    if (buffer.Count > 16384)
    {
        Console.WriteLine($"WARNING: Buffer too large ({buffer.Count} bytes). Clearing old data.");
        // Keep only the last 8KB
        buffer.RemoveRange(0, buffer.Count - 8192);
    }
}

Console.WriteLine($"\nStopped. Parsed {validFrames} valid UBX frames in {readLoops} read loops.");

// ===== Helper Methods =====

static bool TryDecodeNavPvt(ReadOnlySpan<byte> payload, out NavPvt pvt)
{
    pvt = default;
    
    if (payload.Length != 92)
    {
        return false;
    }
    
    // Read time fields
    ushort year = (ushort)(payload[4] | (payload[5] << 8));
    byte month = payload[6];
    byte day = payload[7];
    byte hour = payload[8];
    byte min = payload[9];
    byte sec = payload[10];
    
    // Check valid flags (bit 0 = validDate, bit 1 = validTime)
    byte valid = payload[11];
    bool validDate = (valid & 0x01) != 0;
    bool validTime = (valid & 0x02) != 0;
    
    if (!validDate || !validTime)
    {
        return false; // Invalid date/time
    }
    
    // Construct timestamp (UTC)
    try
    {
        var dt = new DateTime(year, month, day, hour, min, sec, DateTimeKind.Utc);
        pvt.Timestamp = new DateTimeOffset(dt, TimeSpan.Zero);
    }
    catch
    {
        return false; // Invalid date/time values
    }
    
    // Read fixType
    byte fixTypeByte = payload[20];
    pvt.FixType = fixTypeByte switch
    {
        0 => "NoFix",
        1 => "DR",
        2 => "2D",
        3 => "3D",
        4 => "GNSS+DR",
        5 => "TimeOnly",
        _ => "Unknown"
    };
    
    // Read numSV
    pvt.NumSv = payload[23];
    
    // Read lon (I4 at offset 24, little-endian, 1e-7 degrees)
    int lonRaw = payload[24] | (payload[25] << 8) | (payload[26] << 16) | (payload[27] << 24);
    pvt.LongitudeDeg = lonRaw / 1e7;
    
    // Read lat (I4 at offset 28, little-endian, 1e-7 degrees)
    int latRaw = payload[28] | (payload[29] << 8) | (payload[30] << 16) | (payload[31] << 24);
    pvt.LatitudeDeg = latRaw / 1e7;
    
    // Read gSpeed (I4 at offset 60, little-endian, mm/s)
    int gSpeedRaw = payload[60] | (payload[61] << 8) | (payload[62] << 16) | (payload[63] << 24);
    pvt.SpeedMps = gSpeedRaw / 1000.0;
    
    return true;
}

// ===== Data Structures =====

struct NavPvt
{
    public DateTimeOffset Timestamp { get; set; }
    public double LatitudeDeg { get; set; }
    public double LongitudeDeg { get; set; }
    public double SpeedMps { get; set; }
    public int NumSv { get; set; }
    public string FixType { get; set; }
}
