using BitMagic.Compiler;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

[Obsolete]
internal class BitMagicPrgFile : IPrgFile
{
    /// <summary>
    /// Create properly linked objects from the compile result.
    /// Includes a many to many link between source and output
    /// </summary>
    /// <param name="result">CompileResult to parse</param>
    /// <returns>BitMagicPrgFiles that are properly linked</returns>
    public static IEnumerable<BitMagicPrgFile> ProcessCompileResult(CompileResult result)
    {
        List<BitMagicPrgFile> outputs = new();
        Dictionary<string, BitMagicPrgSourceFile> sources = new();

        string sourceFilename;
        string generatedFilename;

        sourceFilename = (result.Project.Code.Parents.FirstOrDefault() ?? result.Project.Code).Path;
        generatedFilename = result.Project.Code.Path;

        if (generatedFilename == sourceFilename)
            generatedFilename = "";

        BitMagicPrgSourceFile source;
        if (!sources.ContainsKey(sourceFilename))
        {
            source = new BitMagicPrgSourceFile(sourceFilename, generatedFilename);
            sources.Add(sourceFilename, source);
        }
        else
        {
            source = sources[sourceFilename];
        }

        foreach (var file in result.Data.Values)
        {
            var prgFile = new BitMagicPrgFile(file.FileName.ToUpper(), file.ToArray(), file.IsMain, result);
            outputs.Add(prgFile);

            prgFile.SourceFiles.Add(source);
            source.Output.Add(prgFile);

            var processResult = result.Project.Code as BitMagic.TemplateEngine.Compiler.MacroAssembler.ProcessResult;

            if (processResult != null)
            {
                source.ReferencedFilenames.AddRange(
                    processResult.Source.Map.Select(i => i.SourceFilename)
                                            .Where(i => i != source.Filename && !string.IsNullOrWhiteSpace(i))
                                            .Distinct()
                                            .Where(i => !source.ReferencedFilenames.Contains(i)));
            }

        }

        return outputs;
    }

    public string Filename { get; }
    public byte[] Data { get; }
    public bool IsMain { get; }
    private CompileResult Result { get; }
    public bool Loaded { get; private set; }
    IEnumerable<IPrgSourceFile> IPrgFile.SourceFiles => SourceFiles;
    public List<IPrgSourceFile> SourceFiles { get; } = new();

    internal BitMagicPrgFile(string filename, byte[] data, bool isMain, CompileResult result)
    {
        Filename = filename;
        Data = data;
        IsMain = isMain;
        Result = result;
        //SourceFiles.Add(new BitMagicPrgSourceFile((result.Project.Code.Parent ?? result.Project.Code).Path, this));
    }

    /// <summary>
    /// Load debugger info for this file
    /// </summary>
    /// <param name="address">Debugger address where the file is loaded</param>
    /// <param name="hasHeader">To indicate if there is a header, so the correct length can be calculated</param>
    /// <param name="sourceMapManager">SourceMap Manager</param>
    /// <param name="breakpointManager">Breakpoint Manager</param>
    /// <returns>Breakpoints to return to VsCode</returns>
    public List<Breakpoint> LoadDebuggerInfo(int address, bool hasHeader, SourceMapManager sourceMapManager, BreakpointManager breakpointManager)
    {
        // need to load debugger symbols and maps
        var toReturn = breakpointManager.ClearBreakpoints(address, Data.Length - (hasHeader ? 2 : 0)); // unload any breakpoints

        sourceMapManager.ConstructSourceMap(Result, Filename);
        foreach (var source in SourceFiles)
        {
            if (source is not IBitMagicPrgSourceFile bmSource)
                continue;

            foreach (var sourceFilename in bmSource.GetFixedSourceFilenames())
            {
                breakpointManager.SetBitmagicBreakpoints(bmSource, Filename, sourceFilename);
                if (bmSource.Breakpoints.ContainsKey(sourceFilename))
                    toReturn.AddRange(bmSource.Breakpoints[sourceFilename]);
            }
        }

        Loaded = true;
        return toReturn;
    }

    public void LoadIntoMemory(Emulator emulator, int address) => emulator.LoadIntoMemory(Data, address, true);
}
