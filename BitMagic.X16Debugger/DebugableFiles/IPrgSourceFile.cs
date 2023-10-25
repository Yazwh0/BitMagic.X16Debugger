using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Collections.Generic;

namespace BitMagic.X16Debugger.DebugableFiles;

// source code that generated a file
internal interface IPrgSourceFile
{
    IEnumerable<IPrgFile> Output { get; }
    string Filename { get; }
    public Dictionary<string, IEnumerable<Breakpoint>> Breakpoints { get; }
    public IEnumerable<string> ReferencedFilenames { get; }
}

internal interface IBitMagicPrgSourceFile : IPrgSourceFile
{
    public Dictionary<string, IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)>> SourceBreakpoints { get; }
    public string GeneratedFilename { get; }
}

internal interface IBinaryFile : ISourceFile
{
    IReadOnlyDictionary<int, string> Symbols { get; }
    int BaseAddress { get; }
    void LoadIntoMemory(Emulator emulator, int address);
    IReadOnlyList<byte> Data { get; }
}

public static class SourceFileExtensions
{
    public static Source AsSource(this ISourceFile source) => new Source {
        Name = Path.GetFileName(source.Name),
        Path = source.Path,
        SourceReference = source.ReferenceId,
        Origin = source.Origin.ToString()
    };
}