using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal interface IBinaryFile : ISourceFile
{
    IReadOnlyDictionary<int, string> Symbols { get; }
    int BaseAddress { get; }
    void LoadDebugData(Emulator emulator, SourceMapManager sourceMapManager, int debuggerAddress);
    IReadOnlyList<byte> Data { get; }
    void Relocate(int newBaseAddress);
    bool Written { get; }
    void SetWritten();
}

/// <summary>
/// A binary file with symbols in a CompileState, used for globals, watches and hovers regardless of source type.
/// </summary>
internal interface ICompiledBinaryFile : IBinaryFile
{
    CompileState State { get; }
}
