using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class PrgSourceFile : IPrgSourceFile
{
    IPrgFile IPrgSourceFile.Parent => Parent;
    public BitMagicPrgFile Parent { get; }
    public string Filename { get; }
    IEnumerable<Breakpoint> IPrgSourceFile.Breakpoints => Breakpoints;
    public List<Breakpoint> Breakpoints { get; } = new();

    public PrgSourceFile(string filename, BitMagicPrgFile parent)
    {
        Filename = filename;
        Parent = parent;
    }
}

internal class BitMagicPrgSourceFile : IBitMagicPrgSourceFile
{
    IPrgFile IPrgSourceFile.Parent => Parent;
    public BitMagicPrgFile Parent { get; }
    public string Filename { get; }
    IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> IBitMagicPrgSourceFile.SourceBreakpoints => SourceBreakpoints;
    public List<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> SourceBreakpoints { get; } = new();
    public IEnumerable<Breakpoint> Breakpoints => SourceBreakpoints.Select(i => i.Breakpoint);

    public BitMagicPrgSourceFile(string filename, BitMagicPrgFile parent)
    {
        Filename = filename;
        Parent = parent;
    }
}