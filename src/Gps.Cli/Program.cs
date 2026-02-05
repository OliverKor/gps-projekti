using System.Buffers.Binary;
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

var buffer = new RingBuffer(8192);
int validFrames = 0;
int readLoops = 0;
var lastSyncMsg = DateTime.MinValue;

// CSV logging setup with StreamWriter
var csvPath = "track.csv";
DateTimeOffset? lastLoggedTimestamp = null;

// Create CSV with header if it doesn't exist
var fileExists = File.Exists(csvPath);
var csvWriter = new StreamWriter(csvPath, append: true) { AutoFlush = true };

if (!fileExists)
{
    csvWriter.WriteLine("timestamp,lat,lon,speed_mps,num_sv,fix_type");
    Console.WriteLine($"Created {csvPath}");
}

var inv = CultureInfo.InvariantCulture;

// Reusable read buffer (avoid allocation per iteration)
var readBuf = new byte[1024];

// Outer loop: keep reading from serial port continuously
while (!cts.IsCancellationRequested)
{
    // Read more data from serial port
    int n = 0;
    try
    {
        n = sp.Read(readBuf, 0, readBuf.Length);
        buffer.Write(readBuf, 0, n);
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
        
        // Need at least 2 bytes to look for sync
        if (buffer.Available < 2)
        {
            break; // Need more data from serial port
        }
        
        // Search for sync bytes 0xB5 0x62
        int syncIndex = buffer.FindSync();
        
        if (syncIndex == -1)
        {
            // No sync found - discard all but last byte (might be partial 0xB5)
            int toDiscard = Math.Max(0, buffer.Available - 1);
            if (toDiscard > 0)
            {
                buffer.Consume(toDiscard);
                madeProgress = true;
            }
            break; // Need more data
        }
        
        // Sync found at syncIndex
        // Discard any junk before sync
        if (syncIndex > 0)
        {
            buffer.Consume(syncIndex);
            madeProgress = true;
            continue; // Re-check from beginning
        }
        
        // Sync is at position 0
        // Need at least: sync(2) + class(1) + id(1) + length(2) = 6 bytes
        if (buffer.Available < 6)
        {
            // Not enough bytes for header - wait for more
            if ((DateTime.Now - lastSyncMsg).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"SYNC found, waiting for header... ({buffer.Available} bytes available)");
                lastSyncMsg = DateTime.Now;
            }
            break; // Need more data
        }
        
        // Read header
        byte cls = buffer.PeekByte(2);
        byte id = buffer.PeekByte(3);
        int payloadLen = buffer.PeekByte(4) | (buffer.PeekByte(5) << 8); // little-endian
        
        // Sanity check on payload length
        if (payloadLen > 4096)
        {
            // Invalid length, likely false sync - discard sync bytes (2 bytes) and continue
            buffer.Consume(2);
            madeProgress = true;
            continue;
        }
        
        // Calculate total frame length: sync(2) + class(1) + id(1) + len(2) + payload + checksum(2)
        int frameLen = 8 + payloadLen;
        
        if (buffer.Available < frameLen)
        {
            // Not enough bytes for complete frame - wait
            if ((DateTime.Now - lastSyncMsg).TotalSeconds >= 1.0)
            {
                Console.WriteLine($"SYNC found, waiting for frame... (need {frameLen}, have {buffer.Available})");
                lastSyncMsg = DateTime.Now;
            }
            break; // Need more data
        }
        
        // We have a complete frame - validate checksum
        // Checksum is computed over: class, id, length(2 bytes), payload
        byte ckA = 0, ckB = 0;
        for (int i = 2; i < 6 + payloadLen; i++)
        {
            byte b = buffer.PeekByte(i);
            ckA += b;
            ckB += ckA;
        }
        
        byte expectedCkA = buffer.PeekByte(6 + payloadLen);
        byte expectedCkB = buffer.PeekByte(6 + payloadLen + 1);
        
        if (ckA != expectedCkA || ckB != expectedCkB)
        {
            // Checksum failed - discard only the sync bytes (2 bytes) and continue scanning
            buffer.Consume(2);
            madeProgress = true;
            continue;
        }
        
        // Valid frame!
        validFrames++;
        
        // Check if this is NAV-PVT (cls=0x01, id=0x07, len=92)
        if (cls == 0x01 && id == 0x07 && payloadLen == 92)
        {
            // Decode NAV-PVT directly from buffer at offset 6 (payload starts after header)
            if (TryDecodeNavPvt(buffer, 6, out var pvt))
            {
                // Rate limit: only log if timestamp is different from last logged (1 Hz throttle)
                if (lastLoggedTimestamp == null || pvt.Timestamp != lastLoggedTimestamp)
                {
                    lastLoggedTimestamp = pvt.Timestamp;
                    
                    // Write to CSV with InvariantCulture (uses '.' decimal separator)
                    csvWriter.WriteLine(string.Format(inv,
                        "{0},{1:F7},{2:F7},{3:F2},{4},{5}",
                        pvt.Timestamp.ToString("o"),
                        pvt.LatitudeDeg,
                        pvt.LongitudeDeg,
                        pvt.SpeedMps,
                        pvt.NumSv,
                        pvt.FixType));
                    
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
        
        // Discard this frame
        buffer.Consume(frameLen);
        madeProgress = true;
    }
    
    // Safety: if we looped too many times, clear buffer
    if (extractIterations >= 1000)
    {
        Console.WriteLine($"WARNING: Extraction loop hit 1000 iterations. Clearing buffer ({buffer.Available} bytes).");
        buffer.Clear();
    }
}

// Flush and close CSV writer
csvWriter.Flush();
csvWriter.Close();

Console.WriteLine($"\nStopped. Parsed {validFrames} valid UBX frames in {readLoops} read loops.");

// ===== Helper Methods =====

static bool TryDecodeNavPvt(RingBuffer buffer, int offset, out NavPvt pvt)
{
    pvt = default;
    
    // NAV-PVT payload is 92 bytes
    // To use BinaryPrimitives, we need contiguous memory
    // Copy the 92-byte payload to a temp buffer
    Span<byte> payload = stackalloc byte[92];
    for (int i = 0; i < 92; i++)
    {
        payload[i] = buffer.PeekByte(offset + i);
    }
    
    // Read time fields
    ushort year = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
    byte month = payload[6];
    byte day = payload[7];
    byte hour = payload[8];
    byte min = payload[9];
    byte sec = payload[10];
    
    // Check valid flags (bit 0 = validDate, bit 1 = validTime, bit 2 = fullyResolved)
    byte valid = payload[11];
    bool validDate = (valid & 0x01) != 0;
    bool validTime = (valid & 0x02) != 0;
    bool fullyResolved = (valid & 0x04) != 0;
    
    if (!validDate || !validTime || !fullyResolved)
    {
        return false; // Invalid or not fully resolved date/time
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
    int lonRaw = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(24, 4));
    pvt.LongitudeDeg = lonRaw / 1e7;
    
    // Read lat (I4 at offset 28, little-endian, 1e-7 degrees)
    int latRaw = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(28, 4));
    pvt.LatitudeDeg = latRaw / 1e7;
    
    // Read gSpeed (I4 at offset 60, little-endian, mm/s)
    int gSpeedRaw = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(60, 4));
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

// ===== Ring Buffer Implementation =====

class RingBuffer
{
    private readonly byte[] _buffer;
    private int _start;
    private int _count;
    
    public RingBuffer(int capacity)
    {
        _buffer = new byte[capacity];
        _start = 0;
        _count = 0;
    }
    
    public int Available => _count;
    
    public void Write(byte[] data, int offset, int length)
    {
        if (length > _buffer.Length - _count)
        {
            // Not enough space - this shouldn't happen with proper buffer sizing
            // For safety, clear old data to make room
            int toRemove = length - (_buffer.Length - _count);
            Consume(toRemove);
        }
        
        for (int i = 0; i < length; i++)
        {
            int pos = (_start + _count) % _buffer.Length;
            _buffer[pos] = data[offset + i];
            _count++;
        }
    }
    
    public byte PeekByte(int index)
    {
        if (index >= _count)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        int pos = (_start + index) % _buffer.Length;
        return _buffer[pos];
    }
    
    public void Consume(int count)
    {
        if (count > _count)
            count = _count;
        
        _start = (_start + count) % _buffer.Length;
        _count -= count;
    }
    
    public int FindSync()
    {
        // Search for 0xB5 0x62
        for (int i = 0; i < _count - 1; i++)
        {
            if (PeekByte(i) == 0xB5 && PeekByte(i + 1) == 0x62)
            {
                return i;
            }
        }
        return -1;
    }
    
    public void Clear()
    {
        _start = 0;
        _count = 0;
    }
}
