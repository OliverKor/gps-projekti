using System.Globalization;
using System.IO.Ports;

namespace Gps.Cli;

internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine();
            Console.WriteLine("Stopping...");
        };

        var options = CliOptions.Parse(args);

        using var serialPort = CreatePort(options);

        try
        {
            Console.WriteLine($"Opening {options.PortName} @ {options.BaudRate} ...");
            serialPort.Open();
            Console.WriteLine("Opened.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open serial port: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Press Ctrl+C to stop.");

        using var csvWriter = new TrackCsvWriter("track.csv");
        var parser = new UbxStreamParser(capacity: 8192);
        var readBuffer = new byte[1024];

        DateTimeOffset? lastLoggedTimestamp = null;
        var validFrameCount = 0;
        var loggedFixCount = 0;
        var readLoopCount = 0;

        while (!cancellation.Token.IsCancellationRequested)
        {
            int readCount;
            try
            {
                readCount = serialPort.Read(readBuffer, 0, readBuffer.Length);
                readLoopCount++;
            }
            catch (TimeoutException)
            {
                readLoopCount++;
                continue;
            }

            if (readCount <= 0)
            {
                continue;
            }

            parser.Append(readBuffer, readCount);

            while (parser.TryReadFrame(out var frame))
            {
                validFrameCount++;

                if (!NavPvtDecoder.TryDecode(frame, out var sample))
                {
                    continue;
                }

                if (sample.Timestamp == lastLoggedTimestamp)
                {
                    continue;
                }

                lastLoggedTimestamp = sample.Timestamp;
                csvWriter.Write(sample);
                loggedFixCount++;

                Console.WriteLine(string.Format(Inv,
                    "LOG {0} lat={1:F6} lon={2:F6} speed={3:F2} sv={4} fix={5}",
                    sample.Timestamp.ToString("o"),
                    sample.LatitudeDeg,
                    sample.LongitudeDeg,
                    sample.SpeedMps,
                    sample.NumSv,
                    sample.FixType));
            }
        }

        Console.WriteLine($"Stopped. Parsed {validFrameCount} frames, wrote {loggedFixCount} fixes, {readLoopCount} read loops.");
        return 0;
    }

    private static SerialPort CreatePort(CliOptions options)
    {
        return new SerialPort(options.PortName, options.BaudRate)
        {
            ReadTimeout = 500,
            WriteTimeout = 500,
            DtrEnable = true,
            Handshake = Handshake.None
        };
    }
}
