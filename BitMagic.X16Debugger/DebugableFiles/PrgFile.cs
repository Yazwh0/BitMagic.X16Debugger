using BitMagic.Compiler;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugableFileManager
{
    private readonly Dictionary<string, IPrgFile> Files = new();
    private readonly Dictionary<string, IPrgSourceFile> SourceFiles = new();

    public IPrgFile? GetFile(string filename)
    {
        if (Files.ContainsKey(filename))
            return Files[filename];

        return null;
    }

    public IPrgSourceFile? GetFileFromSource(string filename)
    {
        if (SourceFiles.ContainsKey(filename))
            return SourceFiles[filename];

        return null;
    }

    public void Addfile(IPrgFile file)
    {
        Files.Add(file.Filename, file);
        foreach(var source in file.SourceFiles)
        {
            SourceFiles.Add(FixFilename(source.Filename), source);
        }
    }

    public void AddFilesToSdCard(SdCard sdCard)
    {
        foreach (var file in Files.Values)
        {
            sdCard.AddCompiledFile(file.Filename, file.Data);
        }
    }

    private static string FixFilename(string path)
    {
#if OS_WINDOWS
        return char.ToLower(path[0]) + path[1..];
#endif
#if OS_LINUX
        return path;
#endif
    }
}

// defined a prg file that can be loaded in and debugged in some way
internal interface IPrgFile
{
    string Filename { get; }
    List<Breakpoint> LoadDebuggerInfo(int address, bool hasHeader, SourceMapManager sourceMapManager, BreakpointManager breakpointManager);
    public byte[] Data { get; }
    public bool Loaded { get; }
    public IEnumerable<IPrgSourceFile> SourceFiles { get; }
}

internal interface IPrgSourceFile
{
    IPrgFile Parent { get; }
    string Filename { get; }
    public List<(Breakpoint Breakpoint, SourceBreakpoint SourceBreakpoint)> Breakpoints { get; }
}

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