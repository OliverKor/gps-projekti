using Gps.Core;
using System.IO.Ports;

namespace Gps.Ui.Wpf;

internal sealed class LiveGpsSession : IDisposable
{
    private readonly object _sync = new();
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly bool _enableCsvLogging;
    private readonly string _csvPath;

    private SerialPort? _serialPort;
    private CancellationTokenSource? _cancellation;
    private Task? _readTask;
    private FixCsvWriter? _csvWriter;

    public LiveGpsSession(string portName, int baudRate, bool enableCsvLogging, string csvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);

        _portName = portName;
        _baudRate = baudRate;
        _enableCsvLogging = enableCsvLogging;
        _csvPath = csvPath;
    }

    public event EventHandler<Fix>? FixReceived;

    public event EventHandler<string>? Error;

    public event EventHandler? Stopped;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _readTask is not null;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_readTask is not null)
            {
                throw new InvalidOperationException("Session is already running.");
            }

            _serialPort = new SerialPort(_portName, _baudRate)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                DtrEnable = true,
                Handshake = Handshake.None
            };

            _serialPort.Open();

            if (_enableCsvLogging)
            {
                _csvWriter = new FixCsvWriter(_csvPath);
            }

            _cancellation = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_cancellation.Token), _cancellation.Token);
        }
    }

    public async Task StopAsync()
    {
        Task? readTask;
        CancellationTokenSource? cancellation;

        lock (_sync)
        {
            readTask = _readTask;
            cancellation = _cancellation;
        }

        if (readTask is null)
        {
            return;
        }

        cancellation?.Cancel();

        try
        {
            await readTask.ConfigureAwait(false);
        }
        catch
        {
            // Read loop handles error propagation through events.
        }
    }

    private void ReadLoop(CancellationToken token)
    {
        var parser = new UbxNavPvtStreamDecoder();
        var readBuffer = new byte[1024];
        var emittedFixes = new List<Fix>(8);

        try
        {
            while (!token.IsCancellationRequested)
            {
                int readCount;
                try
                {
                    readCount = _serialPort!.Read(readBuffer, 0, readBuffer.Length);
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (readCount <= 0)
                {
                    continue;
                }

                emittedFixes.Clear();
                var emitted = parser.TryAppend(readBuffer.AsSpan(0, readCount), emittedFixes);
                if (!emitted)
                {
                    continue;
                }

                foreach (var fix in emittedFixes)
                {
                    _csvWriter?.Write(fix);
                    FixReceived?.Invoke(this, fix);
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Error?.Invoke(this, $"Serial read failed: {ex.Message}");
        }
        finally
        {
            CleanupResources();
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CleanupResources()
    {
        SerialPort? serialPort;
        FixCsvWriter? csvWriter;
        CancellationTokenSource? cancellation;

        lock (_sync)
        {
            serialPort = _serialPort;
            _serialPort = null;

            csvWriter = _csvWriter;
            _csvWriter = null;

            cancellation = _cancellation;
            _cancellation = null;

            _readTask = null;
        }

        csvWriter?.Dispose();

        if (serialPort is not null)
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
            catch
            {
                // Ignore close errors during shutdown.
            }

            serialPort.Dispose();
        }

        cancellation?.Dispose();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
