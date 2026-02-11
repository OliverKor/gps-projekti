namespace Gps.Core;

internal readonly record struct UbxFrame(byte Class, byte Id, ReadOnlyMemory<byte> Payload);
