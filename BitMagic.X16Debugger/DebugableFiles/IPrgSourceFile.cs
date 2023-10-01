using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

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