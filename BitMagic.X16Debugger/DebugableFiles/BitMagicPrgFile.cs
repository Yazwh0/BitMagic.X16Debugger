using BitMagic.Compiler;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class BitMagicPrgFile : IPrgFile
{
    public static IEnumerable<BitMagicPrgFile> ProcessCompileResult(CompileResult result)
    {
        foreach (var file in result.Data.Values)
        {
            yield return new BitMagicPrgFile(file.FileName.ToUpper(), file.ToArray(), file.IsMain, result);
        }
    }

    public string Filename { get; }
    public byte[] Data { get; }
    public bool IsMain { get; }
    private CompileResult Result { get; }
    public bool Loaded { get; private set; }
    IEnumerable<IPrgSourceFile> IPrgFile.SourceFiles => SourceFiles;
    public List<PrgSourceFile> SourceFiles { get; } = new();

    public BitMagicPrgFile(string filename, byte[] data, bool isMain, CompileResult result)
    {
        Filename = filename;
        Data = data;
        IsMain = isMain;
        Result = result;
        SourceFiles.Add(new PrgSourceFile((result.Project.Code.Parent ?? result.Project.Code).Path, this));
    }

    public List<Breakpoint> LoadDebuggerInfo(int address, bool hasHeader, SourceMapManager sourceMapManager, BreakpointManager breakpointManager)
    {
        // need to load debugger symbols and maps
        var toReturn = breakpointManager.ClearBreakpoints(address, Data.Length - (hasHeader ? 2 : 0)); // unload any breakpoints

        sourceMapManager.ConstructSourceMap(Result);
        foreach(var source in SourceFiles.Where(i => i.Breakpoints.Any()))
        {
            breakpointManager.SetBitmagicBreakpoints(source);
            toReturn.AddRange(source.Breakpoints.Select(i => i.Breakpoint));
        }
        Loaded = true;
        return toReturn;
    }

    public void LoadIntoMemory(Emulator emulator, int address) => emulator.LoadIntoMemory(Data, address);
}
