using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Warnings;
using BitMagic.Decompiler;
using DiscUtils.Partitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class SourceMapManager
{
    public Dictionary<int, SourceMap> MemoryToSourceMap { get; } = new();
    public Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();

    public Dictionary<int, DecompileReturn> DecompiledRom { get; } = new();
    public Dictionary<int, DecompileReturn> DecompiledRomById { get; } = new();

    // packed debugger address
    public Dictionary<int, string> Symbols { get; } = new();

    private readonly IdManager _idManager;

    public SourceMapManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public SourceMap? GetSourceMap(int debuggerAddress)
    {
        if (MemoryToSourceMap.ContainsKey(debuggerAddress))
            return MemoryToSourceMap[debuggerAddress];

        return null;
    }

    public SourceMap? GetPreviousMap(int debuggerAddress, int maxStep = -3)
    {
        var step = -1;
        while(step >= maxStep)
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
        DecompiledRom.Clear();
    }

    // Construct the source map for the debugger.
    public void ConstructSourceMap(CompileResult result)
    {
        var state = result.State;

        foreach (var segment in state.Segments.Values)
        {
            foreach (var defaultProc in segment.DefaultProcedure.Values)
            {
                MapProc(defaultProc);
            }
        }

        foreach(var (name, value) in result.State.ScopeFactory.GlobalVariables.GetChildVariables("App"))
        {
            if (!Symbols.ContainsKey(value))
                Symbols.Add(value, name);
        }
    }

    // todo: this doesn't handle banks
    private void MapProc(Procedure proc)
    {
        foreach (var line in proc.Data)
        {
            var toAdd = new SourceMap(line);

            if (MemoryToSourceMap.ContainsKey(toAdd.DebuggerAddress))
                throw new Exception("Could add line, as it was already in the hashset.");

            MemoryToSourceMap.Add(toAdd.DebuggerAddress, toAdd);

            HashSet<CodeMap> lineMap;
            var fileName = PathFunctions.FixPath(line.Source.Name);
            if (!SourceToMemoryMap.ContainsKey(fileName))
            {
                lineMap = new HashSet<CodeMap>();
                SourceToMemoryMap.Add(fileName, lineMap);
            }
            else
            {
                lineMap = SourceToMemoryMap[fileName];
            }

            lineMap.Add(new CodeMap(line.Source.LineNumber, line.Address, 0, line));
        }

        foreach (var p in proc.Procedures)
        {
            MapProc(p);
        }
    }


    /// <summary>
    /// Loads symbols from an external non-BitMagic file.
    /// </summary>
    /// <param name="fileName">Filename</param>
    /// <param name="ramBank">RAM bank, null for no bank.</param>
    /// <param name="romBank">ROM bank, null for no bank.</param>
    /// <exception cref="Exception"></exception>
    public void LoadSymbols(string fileName, int? ramBank, int? romBank)
    {
        const int addressLocation = 1;
        const int symbolLocation = 2;

        if (!File.Exists(fileName))
            throw new Exception($"File not found '{fileName}'");

        var contents = File.ReadAllLines(fileName);

        foreach(var line in contents)
        {
            var parts = line.Split(" ", StringSplitOptions.TrimEntries);

            var address = Convert.ToInt32(parts[addressLocation], 16);

            // ignore anything in a banked area if there is no bank specified
            if (address >= 0xc000 && romBank == null)
                continue;

            if (address >= 0xa000 && address < 0xc000 && ramBank == null)
                continue;

            var debuggerAddress = AddressFunctions.GetDebuggerAddress(address, ramBank ?? 0, romBank ?? 0);

            if (!Symbols.ContainsKey(debuggerAddress))
                Symbols.Add(debuggerAddress, parts[symbolLocation][1..]);
        }
    }

    public void DecompileRomBank(byte[] data, int bank)
    {
        var decompiler = new Decompiler.Decompiler();

        var result = decompiler.Decompile(data, 0xc000, bank, Symbols);

        DecompiledRom.Add(bank, result);
        _idManager.AddObject(result, ObjectType.DecompiledData);
    }
}

public class CodeMap
{
    public int LineNumber { get; }
    public int Address { get; }
    public int Bank { get; }
    public IOutputData Line { get; }

    public CodeMap(int lineNumber, int address, int bank, IOutputData line)
    {
        LineNumber = lineNumber;
        Address = address;
        Bank = bank;
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

public class SourceMap
{
    //public int Address => Line.Address;
    //public int Bank { get; }
    public IOutputData Line { get; }

    public int DebuggerAddress => Line.Address;
    public SourceMap(IOutputData line)
    {
        Line = line;
        //Address = line.Address;
    }
}