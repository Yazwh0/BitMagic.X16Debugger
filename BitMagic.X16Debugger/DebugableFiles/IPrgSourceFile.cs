using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

// source code that generated a second file
internal interface IPrgSourceFile
{
    IPrgFile Parent { get; }
    string Filename { get; }
    public IEnumerable<Breakpoint> Breakpoints { get; }
}

internal interface IBitMagicPrgSourceFile : IPrgSourceFile
{
    public IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> SourceBreakpoints { get; }
}