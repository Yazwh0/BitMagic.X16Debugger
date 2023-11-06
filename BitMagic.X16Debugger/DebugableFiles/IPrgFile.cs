using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

// defined prg file that can be loaded in and debugged in some way
[Obsolete("To be removed")]
internal interface IPrgFile
{
    string Filename { get; }
    List<Breakpoint> LoadDebuggerInfo(int address, bool hasHeader, SourceMapManager sourceMapManager, BreakpointManager breakpointManager);
    public bool Loaded { get; }
    public IEnumerable<IPrgSourceFile> SourceFiles { get; }
}
