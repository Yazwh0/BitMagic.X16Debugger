using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Warnings;
using BitMagic.Decompiler;
using DiscUtils.Partitions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class SourceMapManager
{
    public Dictionary<int, SourceMap> MemoryToSourceMap { get; } = new();
    public Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();

    public Dictionary<int, DecompileReturn> DecompiledRom { get; } = new();
    public Dictionary<int, int> RomToId { get; } = new();

    // packed debugger address
    public Dictionary<int, string> Symbols { get; } = new();

    private readonly IdManager _idManager;
    private X16DebugProject? _project;

    public SourceMapManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public void SetProject(X16DebugProject project)
    {
        _project = project;
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

    public string GetSymbol(int machineAddress)
    {
        if (!Symbols.ContainsKey(machineAddress))
            return "";

        return Symbols[machineAddress];
    }

    public void Clear()
    {
        RomToId.Clear();
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

        foreach (var (name, value) in result.State.ScopeFactory.GlobalVariables.GetChildVariables("App"))
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

    public void AddSymbolsFromMachine(IMachine machine)
    {
        foreach (var i in machine.Variables.Values)
        {
            if (!Symbols.ContainsKey(i.Value))
                Symbols.Add(i.Value, i.Key);
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

        if (!File.Exists(file.Name))
            throw new Exception($"File not found '{file.Name}'");

        var contents = File.ReadAllLines(file.Name);

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

    public void DecompileRomBank(byte[] data, int bank)
    {
        var decompiler = new Decompiler.Decompiler();

        var result = decompiler.Decompile(data, 0xc000, 0xffff, bank, Symbols);

        DecompiledRom.Add(bank, result);
        var id = _idManager.AddObject(result, ObjectType.DecompiledData);
        RomToId.Add(bank, id);

        var name = _project?.RomBankNames.Length >= bank ? _project.RomBankNames[bank] : "";

        if (string.IsNullOrWhiteSpace(name))
            result.Name = $"RomBank_{bank}.bmasm";
        else
            result.Name = name + ".bmasm";

        result.Path = $"Rom/{result.Name}";
        result.Origin = "Decompiled";
        result.ReferenceId = id;
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