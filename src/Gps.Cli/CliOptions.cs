namespace Gps.Cli;

internal readonly record struct CliOptions(string PortName, int BaudRate)
{
    public static CliOptions Parse(string[] args)
    {
        var portName = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : "COM3";

        var baudRate = args.Length > 1 && int.TryParse(args[1], out var parsedBaudRate)
            ? parsedBaudRate
            : 38400;

        return new CliOptions(portName, baudRate);
    }
}
