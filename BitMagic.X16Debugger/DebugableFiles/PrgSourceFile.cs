using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

//internal class PrgSourceFile : IPrgSourceFile
//{
//    IEnumerable<IPrgFile> IPrgSourceFile.Output => Output;
//    public List<BitMagicPrgFile> Output { get; } = new();
//    public string Filename { get; }
//    Dictionary<string, IEnumerable<Breakpoint>> IPrgSourceFile.Breakpoints => Breakpoints.ToDictionary(i => i.Key, i => i.Value.Select(j => j));
//    public Dictionary<string, List<Breakpoint>> Breakpoints { get; } = new();
//    IEnumerable<string> IPrgSourceFile.ReferencedFilenames => Array.Empty<string>();
//    public PrgSourceFile(string filename, BitMagicPrgFile output)
//    {
//        Filename = filename;
//        Output.Add(output);
//    }
//}

internal class BitMagicPrgSourceFile : IBitMagicPrgSourceFile
{
    IEnumerable<IPrgFile> IPrgSourceFile.Output => Output;
    public List<BitMagicPrgFile> Output { get; } = new();
    public string Filename { get; }
    public string GeneratedFilename { get; }
    IEnumerable<string> IPrgSourceFile.ReferencedFilenames => ReferencedFilenames;
    public List<string> ReferencedFilenames { get; } = new();
    Dictionary<string, IEnumerable<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)>> IBitMagicPrgSourceFile.SourceBreakpoints
        => SourceBreakpoints.ToDictionary(i => i.Key, i => i.Value.Select(j => j));
    public Dictionary<string, List<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)>> SourceBreakpoints { get; } = new();
    public Dictionary<string, IEnumerable<Breakpoint>> Breakpoints => SourceBreakpoints.ToDictionary(i => i.Key, i => i.Value.Select(j => j.Breakpoint));

    public BitMagicPrgSourceFile(string filename, BitMagicPrgFile output)
    {
        Filename = filename;
        Output.Add(output);
    }

    public BitMagicPrgSourceFile(string filename, string generatedFilename)
    {
        Filename = filename;
        GeneratedFilename = generatedFilename;
    }
}