using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class PrgSourceFile : IPrgSourceFile
{
    IEnumerable<IPrgFile> IPrgSourceFile.Output => Output;
    public List<BitMagicPrgFile> Output { get; } = new();
    public string Filename { get; }
    IEnumerable<Breakpoint> IPrgSourceFile.Breakpoints => Breakpoints;
    public List<Breakpoint> Breakpoints { get; } = new();

    public PrgSourceFile(string filename, BitMagicPrgFile output)
    {
        Filename = filename;
        Output.Add(output);
    }
}

internal class BitMagicPrgSourceFile : IBitMagicPrgSourceFile
{
    IEnumerable<IPrgFile> IPrgSourceFile.Output => Output;
    public List<BitMagicPrgFile> Output { get; } = new();
    public string Filename { get; }
    IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> IBitMagicPrgSourceFile.SourceBreakpoints => SourceBreakpoints;
    public List<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> SourceBreakpoints { get; } = new();
    public IEnumerable<Breakpoint> Breakpoints => SourceBreakpoints.Select(i => i.Breakpoint);

    public BitMagicPrgSourceFile(string filename, BitMagicPrgFile output)
    {
        Filename = filename;
        Output.Add(output);
    }

    public BitMagicPrgSourceFile(string filename)
    {
        Filename = filename;
    }
}