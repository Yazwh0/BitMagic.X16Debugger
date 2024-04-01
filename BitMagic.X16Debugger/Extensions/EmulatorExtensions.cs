using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.Extensions;

internal static class EmulatorExtensions
{
    public static void LoadIntoMemory(this Emulator emulator, IReadOnlyList<byte> data, int address, bool hasHeader)
    {
        var destAddress = address;
        for (var i = hasHeader ? 2 : 0; i < data.Count; i++)
        {
            emulator.Memory[destAddress++] = data[i];
        }
    }
}
