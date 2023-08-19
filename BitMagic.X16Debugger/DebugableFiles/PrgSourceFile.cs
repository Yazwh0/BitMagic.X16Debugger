using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class PrgSourceFile : IPrgSourceFile
{
    IPrgFile IPrgSourceFile.Parent => Parent;
    public BitMagicPrgFile Parent { get; }
    public string Filename { get; }
    public List<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> Breakpoints { get; } = new();

    public PrgSourceFile(string filename, BitMagicPrgFile parent)
    {
        Filename = filename;
        Parent = parent;
    }
}