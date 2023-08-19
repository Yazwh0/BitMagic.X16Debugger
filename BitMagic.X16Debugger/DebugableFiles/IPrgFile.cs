using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

// defined a prg file that can be loaded in and debugged in some way
internal interface IPrgFile
{
    string Filename { get; }
    List<Breakpoint> LoadDebuggerInfo(int address, bool hasHeader, SourceMapManager sourceMapManager, BreakpointManager breakpointManager);
    public byte[] Data { get; }
    public bool Loaded { get; }
    public IEnumerable<IPrgSourceFile> SourceFiles { get; }
}
