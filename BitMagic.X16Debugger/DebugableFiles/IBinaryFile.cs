using BitMagic.Common;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal interface IBinaryFile : ISourceFile
{
    IReadOnlyDictionary<int, string> Symbols { get; }
    int BaseAddress { get; }
    void LoadDebugData(Emulator emulator, SourceMapManager sourceMapManager, int debuggerAddress);
    IReadOnlyList<byte> Data { get; }
}
