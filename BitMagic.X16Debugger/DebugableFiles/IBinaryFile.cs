using BitMagic.Common;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal interface IBinaryFile : ISourceFile
{
    IReadOnlyDictionary<int, string> Symbols { get; }
    int BaseAddress { get; }
    void LoadIntoMemory(Emulator emulator, int address);
    IReadOnlyList<byte> Data { get; }
    IReadOnlyList<uint> DebugData { get; }
}
