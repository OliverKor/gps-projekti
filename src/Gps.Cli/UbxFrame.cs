namespace Gps.Cli;

internal readonly record struct UbxFrame(byte Class, byte Id, ReadOnlyMemory<byte> Payload);
