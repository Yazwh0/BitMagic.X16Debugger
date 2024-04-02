using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Compiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Exceptions;
using BitMagic.X16Emulator;
using System.Diagnostics;

namespace BitMagic.X16Debugger;

internal class SourceMapManager
{
    private Dictionary<int, IBinaryFile> MemoryToSourceFile { get; } = new();


    // Address to line lookup
    private Dictionary<int, IDebuggerMapItem> MemoryToSourceMap { get; } = new();
    // source filename to codemap
    private Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();
    // prg file to codemap
    private Dictionary<string, HashSet<CodeMap>> OutputToMemoryMap { get; } = new();

    // packed debugger address
    public Dictionary<int, string> Symbols { get; } = new();

    private readonly Emulator _emulator;

    public SourceMapManager(Emulator emulator)
    {
        _emulator = emulator;
    }

    public IBinaryFile? GetSourceFile(int debuggerAddress)
    {
        if (MemoryToSourceFile.ContainsKey(debuggerAddress))
            return MemoryToSourceFile[debuggerAddress];

        return null;
    }

    public void ClearSourceMap(int startAddress, int length)
    {
        for(var i = 0; i < length; i++)
        {
            if (MemoryToSourceMap.ContainsKey(startAddress + i))
                MemoryToSourceMap.Remove(startAddress + i);
        }
    }

    public void AddSourceMap(int debuggerAddress, IDebuggerMapItem map)
    {
        if (MemoryToSourceMap.ContainsKey(debuggerAddress))
            MemoryToSourceMap[debuggerAddress] = map;
        else
            MemoryToSourceMap.Add(debuggerAddress, map);
    }

    public IDebuggerMapItem? GetSourceMap(int debuggerAddress)
    {
        if (MemoryToSourceMap.ContainsKey(debuggerAddress))
            return MemoryToSourceMap[debuggerAddress];

        return null;
    }

    public IDebuggerMapItem? GetPreviousMap(int debuggerAddress, int maxStep = -3)
    {
        var step = -1;
        while (step >= maxStep)
        {
            if (MemoryToSourceMap.ContainsKey(debuggerAddress + step))
                return MemoryToSourceMap[debuggerAddress + step];

            step--;
        }
        return null;
    }

    public HashSet<CodeMap>? GetSourceFileMap(string filename)
    {
        var filePath = PathFunctions.FixPath(filename);

        if (!SourceToMemoryMap.ContainsKey(filePath))
            return null;

        return SourceToMemoryMap[filePath];
    }

    public HashSet<CodeMap>? GetOutputFileMap(string outputFilename)
    {
        if (!OutputToMemoryMap.ContainsKey(outputFilename))
            return null;

        return OutputToMemoryMap[outputFilename];
    }

    public string GetSymbol(int machineAddress)
    {
        if (!Symbols.ContainsKey(machineAddress))
            return "";

        return Symbols[machineAddress];
    }

    public void Clear()
    {
        Symbols.Clear();
        MemoryToSourceMap.Clear();
        SourceToMemoryMap.Clear();
        OutputToMemoryMap.Clear();

        MemoryToSourceFile.Clear();
    }


    /// <summary>
    /// Load IBinaryFile into memory
    /// </summary>
    /// <param name="source"></param>
    public void ConstructNewSourceMap(IBinaryFile source, bool hasHeader)
    {
        var debuggerAddress = AddressFunctions.GetDebuggerAddress(source.BaseAddress, _emulator);

        for (var i = hasHeader ? 2 : 0; i < source.Data.Count; i++)
        {
            if (MemoryToSourceFile.ContainsKey(debuggerAddress))
            {
                MemoryToSourceFile.Remove(debuggerAddress);
            }

            MemoryToSourceFile[debuggerAddress] = source;
            debuggerAddress++;
        }
    }

    /// <summary>
    /// Construct the source map for a given file and add it to the current debugging state
    /// </summary>
    /// <param name="result">Compile result for the BM prg file</param>
    /// <param name="outputFilename">Filename to be constructed</param>
    [Obsolete]
    public void ConstructSourceMap(CompileResult result, string outputFilename)
    {
        var state = result.State;

        foreach (var segment in state.Segments.Values.Where(i => string.Equals(i.Filename, outputFilename, StringComparison.InvariantCultureIgnoreCase)))
        {
            foreach (var defaultProc in segment.DefaultProcedure.Values)
            {
                MapProc(defaultProc, outputFilename);
            }
        }

        foreach (var (name, value) in result.State.ScopeFactory.GlobalVariables.GetChildVariables("App"))
        {
            if (!Symbols.ContainsKey(value.Value))
                Symbols.Add(value.Value, name);
        }
    }

    private void MapProc(Procedure proc, string outputFilename)
    {
        HashSet<CodeMap> outputMap;
        if (!OutputToMemoryMap.ContainsKey(outputFilename))
        {
            outputMap = new HashSet<CodeMap>();
            OutputToMemoryMap.Add(outputFilename, outputMap);
        }
        else
        {
            outputMap = OutputToMemoryMap[outputFilename];
        }

        foreach (var line in proc.Data)
        {
            //var toAdd = new SourceMapLine(line);

            var debuggerAddress = AddressFunctions.GetDebuggerAddress(line.Address, _emulator);

            if (MemoryToSourceMap.ContainsKey(debuggerAddress))
                MemoryToSourceMap.Remove(debuggerAddress);      // we're overwriting something in memory
                                                                //throw new Exception("Couldn't add line, as it was already in the hashset.");

            MemoryToSourceMap.Add(debuggerAddress, line);

            // Add to source filemap
            HashSet<CodeMap> lineMap;
            var sourceFilename = PathFunctions.FixPath(line.Source.Name);
            if (!SourceToMemoryMap.ContainsKey(sourceFilename))
            {
                lineMap = new HashSet<CodeMap>();
                SourceToMemoryMap.Add(sourceFilename, lineMap);
            }
            else
            {
                lineMap = SourceToMemoryMap[sourceFilename];
            }

            var codemap = new CodeMap(line.Source.LineNumber, debuggerAddress, line);
            lineMap.Add(codemap);

            // Add to output filemap
            outputMap.Add(codemap);
        }

        foreach (var p in proc.Procedures)
        {
            MapProc(p, outputFilename);
        }
    }

    public void AddSymbolsFromMachine(IMachine machine)
    {
        foreach (var i in machine.Variables.Values)
        {
            if (!Symbols.ContainsKey(i.Value.Value))
                Symbols.Add(i.Value.Value, i.Key);
        }
    }

    /// <summary>
    /// Loads symbols from an external non-BitMagic file.
    /// </summary>
    /// <param name="fileName">Filename</param>
    /// <param name="ramBank">RAM bank, null for no bank.</param>
    /// <param name="romBank">ROM bank, null for no bank.</param>
    /// <exception cref="Exception"></exception>
    public void LoadSymbols(SymbolsFile file)
    {
        const int addressLocation = 1;
        const int symbolLocation = 2;

        if (string.IsNullOrWhiteSpace(file.Symbols))
            return;

        if (!File.Exists(file.Symbols))
            throw new SymbolsFileNotFound(file.Symbols);

        var contents = File.ReadAllLines(file.Symbols);

        foreach (var line in contents)
        {
            var parts = line.Split(" ", StringSplitOptions.TrimEntries);

            var address = Convert.ToInt32(parts[addressLocation], 16);

            // ignore anything in a banked area if there is no bank specified
            if (address >= 0xc000 && file.RomBank == null)
                continue;

            if (address >= 0xa000 && address < 0xc000 && file.RamBank == null)
                continue;

            var debuggerAddress = AddressFunctions.GetDebuggerAddress(address, file.RamBank ?? 0, file.RomBank ?? 0);

            if (!Symbols.ContainsKey(debuggerAddress))
                Symbols.Add(debuggerAddress, parts[symbolLocation][1..]);
        }
    }

    public void LoadJumpTable(RangeDefinition[] defintion, int baseAddress, int bank, Span<Byte> data)
    {
        // Jump tables are lists of `jmp xxxx`
        // We want to ensure they have symbols so the are decompiled properly.

        var bankAdd = bank << 16;

        foreach (var i in defintion.Where(i => string.Equals(i.Type, "jumptable", StringComparison.InvariantCultureIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(i.Start) || string.IsNullOrWhiteSpace(i.End)) continue;

            var startAddress = Convert.ToInt32(i.Start, 16) - baseAddress;
            var endAddress = Convert.ToInt32(i.End, 16) - baseAddress;

            while (startAddress <= endAddress)
            {
                if (data[startAddress] == 0x4c) // only abs jumps are considered
                {
                    var destAddress = AddressFunctions.GetDebuggerAddress(data[startAddress + 1] + (data[startAddress + 2] << 8), 0, bank);
                    string dest;
                    if (Symbols.ContainsKey(destAddress))
                        dest = $"jt_{Symbols[destAddress]}";
                    else
                        dest = $"jt_{destAddress:X4}";

                    Symbols.TryAdd(startAddress + baseAddress + bankAdd, dest);
                }
                startAddress += 3;
            }
        }
    }
}

[DebuggerDisplay("{LineNumber} -> {AddressDisplay}")]
public class CodeMap
{
    private string AddressDisplay => $"0x{Address:X6}";
    public int LineNumber { get; }
    public int Address { get; }
    public IOutputData Line { get; }

    public CodeMap(int lineNumber, int address, IOutputData line)
    {
        LineNumber = lineNumber;
        Address = address;
        Line = line;
    }

    public override int GetHashCode() => LineNumber;

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;

        var o = obj as CodeMap;

        if (o == null) return false;

        return LineNumber == o.LineNumber;
    }
}
