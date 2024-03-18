using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal interface IBitMagicPrgSourceFile : IPrgSourceFile
{
    public Dictionary<string, IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)>> SourceBreakpoints { get; }
    public string GeneratedFilename { get; }
}
