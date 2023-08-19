using BitMagic.Common;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger;

internal class DisassemblerManager
{
    private readonly SourceMapManager _sourceMapManager;
    private readonly Emulator _emulator;
    private readonly IdManager _idManager;
    public Dictionary<(int RamBank, int RomBank), int> BankToId { get; } = new();
    public Dictionary<string, DecompileReturn> DecompiledData = new();
    private X16DebugProject? _project;
    private const int NotSet = -1;

    public DisassemblerManager(SourceMapManager sourceMapManager, Emulator emulator, IdManager idManaager)
    {
        _sourceMapManager = sourceMapManager;
        _emulator = emulator;
        _idManager = idManaager;
        CreateRamDocuments();
    }

    public void SetProject(X16DebugProject project)
    {
        _project = project;
        for (var i = 0; i < _project.RamBankNames.Length; i++)
        {
            var id = BankToId[(i, NotSet)];
            var data = _idManager.GetObject<DecompileReturn>(id);
            data.Name = $"{_project.RamBankNames[i]}.bmasm";
            data.Path = $"Ram/{data.Name}";
        }
    }

    // Consider the request to be delimited by main memory, or banked memeory.
    // This way we can use the correct symbols.
    public DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        // assume this address the debugger address, so convert it to the machine address
        var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(Convert.ToInt32(arguments.MemoryReference, 16));

        Console.WriteLine($"Disassemble request: 0x{address:X4} A:{ramBank} O:{romBank} {arguments.InstructionCount} {arguments.InstructionOffset}");

        if (address > 0xc000)
            return DisassembleRequestFromRom(arguments, address, romBank);

        if (address > 0xa000)
            return DisassembleRequestFromRam(arguments, address, ramBank);

        return DisassembleRequestFromMainMemory(arguments, address, _emulator.Pc);
    }

    private DisassembleResponse DisassembleRequestFromMainMemory(DisassembleArguments arguments, int address, int pc)
    {
        DecompileReturn? result = null;
        if (!BankToId.ContainsKey((NotSet, NotSet)))
            throw new NotImplementedException("Main ram isn't set!");

        result = _idManager.GetObject<DecompileReturn>(BankToId[(NotSet, NotSet)]) ?? throw new Exception("Cannot find main ram object!");

        result.Generate();

        return ConvertDisassemblyToReponse(arguments.InstructionOffset ?? 0, arguments.InstructionCount, address, result, 0, 0);
    }

    // We've been asked to dissemble ROM, which we've already done.
    // As ROM cannot be changed, we can happily use these symbols.
    private DisassembleResponse DisassembleRequestFromRom(DisassembleArguments arguments, int address, int romBank)
    {
        DecompileReturn decompileReturn = GetDecompileReturn(NotSet, romBank);

        return ConvertDisassemblyToReponse(arguments.InstructionOffset ?? 0, arguments.InstructionCount, address, decompileReturn, 0, romBank);
    }

    private DisassembleResponse DisassembleRequestFromRam(DisassembleArguments arguments, int address, int ramBank)
    {
        DecompileReturn decompileReturn = GetDecompileReturn(ramBank, NotSet);

        return ConvertDisassemblyToReponse(arguments.InstructionOffset ?? 0, arguments.InstructionCount, address, decompileReturn, ramBank, 0);
    }

    private DecompileReturn GetDecompileReturn(int ramBank, int romBank)
    {
        var id = BankToId[(ramBank, romBank)];
        return _idManager.GetObject<DecompileReturn>(id) ?? throw new Exception($"Does not exist! {ramBank} {romBank}");
    }

    private DecompileReturn CreateDecompileReturn(int ramBank)
    {
        var decompileReturn = new DecompileReturn();
        decompileReturn.ReferenceId = _idManager.AddObject(decompileReturn, ObjectType.DecompiledData);
        var name = $"z_Bank_0x{ramBank:X2}.bmasm"; // name in the project changess this when the project is set
        decompileReturn.Name = name;
        decompileReturn.Path = $"Ram/{name}";
        decompileReturn.Origin = SourceFileOrigin.Decompiled;
        decompileReturn.Volatile = true;
        decompileReturn.RamBank = ramBank;
        decompileReturn.Generate = () =>
        {
            var item = decompileReturn;
            var decompiler = new Decompiler.Decompiler();
            var data = _emulator.RamBank.Slice(ramBank * 0x2000, 0x2000);
            var result = decompiler.Decompile(data, 0xa000, 0xbfff, ramBank, _sourceMapManager.Symbols, null);
            item.Items = result.Items;
        };

        BankToId.Add((ramBank, NotSet), decompileReturn.ReferenceId ?? 0);

        DecompiledData.Add(decompileReturn.Path, decompileReturn);
        return decompileReturn;
    }

    private void CreateRamDocuments()
    {
        for (var i = 0; i < 256; i++)
            CreateDecompileReturn(i);

        var decompileReturn = new DecompileReturn();
        decompileReturn.ReferenceId = _idManager.AddObject(decompileReturn, ObjectType.DecompiledData);
        var name = $"MainRam.bmasm";
        decompileReturn.Name = name;
        decompileReturn.Path = name;
        decompileReturn.Origin = SourceFileOrigin.Decompiled;
        decompileReturn.Volatile = true;
        decompileReturn.Generate = () =>
        {
            var additionalSymbols = new Dictionary<int, string>
            {
                { 0x100, "stackstart" },
                { 0x200, "stackend" }
                //{ _emulator.Pc, "_pc" }
            };

            var item = decompileReturn;
            var decompiler = new Decompiler.Decompiler();
            var data = _emulator.Memory.Slice(0, 0xa000);
            var result = decompiler.Decompile(data, 0x0000, 0x9fff, 0, _sourceMapManager.Symbols, additionalSymbols);
            item.Items = result.Items;
        };
        DecompiledData.Add(decompileReturn.Path, decompileReturn);

        BankToId.Add((NotSet, NotSet), decompileReturn.ReferenceId ?? 0);
    }

    private DisassembleResponse ConvertDisassemblyToReponse(int instructionOffset, int instructionCount, int address, DecompileReturn decompileReturn, int ramBank, int romBank)
    {
        var toReturn = new DisassembleResponse();

        var actItems = decompileReturn.Items.Values.Where(i => i.HasInstruction).ToArray();

        var idx = 0;
        foreach (var i in actItems)
        {
            if (i.Address >= address)
            {
                break;
            }
            idx++;
        }

        idx += instructionOffset;

        for (var i = 0; i < instructionCount;)
        {
            if (idx < 0)
            {
                idx++;
                i++;
                continue;
            }

            if (idx > actItems.Length)
                break;

            var thisLine = actItems[idx];

            toReturn.Instructions.Add(new DisassembledInstruction()
            {
                Address = AddressFunctions.GetDebuggerAddressString(thisLine.Address, ramBank, romBank),
                Line = thisLine.LineNumber,
                Symbol = thisLine.Symbol,
                Instruction = thisLine.Instruction,
                InstructionBytes = string.Join(" ", thisLine.Data.Select(i => i.ToString("X2"))),
                Location = new Source
                {
                    Name = decompileReturn.Name,
                    Path = decompileReturn.Path,
                    Origin = decompileReturn.Origin.ToString(),
                    SourceReference = decompileReturn.ReferenceId,
                }
            });

            i++;
            idx++;
        }

        if (toReturn.Instructions.Any())
            Console.WriteLine($"Returning {toReturn.Instructions.First().Address} -> {toReturn.Instructions.Last().Address}");
        else
            Console.WriteLine($"Nothing to return");

        return toReturn;
    }

    public int GetDisassemblyId(int address, int ramBank, int romBank)
    {
        if (address < 0xa000)
            return BankToId[(NotSet, NotSet)];

        if (address >= 0xc000)
            return BankToId[(NotSet, romBank)];

        if (address >= 0xa000)
            return BankToId[(ramBank, NotSet)];

        return 0;
    }

    public int GetDisassemblyId(int debuggerAddress)
    {
        var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(debuggerAddress);

        return GetDisassemblyId(address, ramBank, romBank);
    }

    public void DecompileRomBank(byte[] data, int bank)
    {
        var decompiler = new Decompiler.Decompiler();

        var result = decompiler.Decompile(data, 0xc000, 0xffff, bank, _sourceMapManager.Symbols);

        var id = _idManager.AddObject(result, ObjectType.DecompiledData);
        BankToId.Add((NotSet, bank), id);

        var name = _project?.RomBankNames.Length >= bank ? _project.RomBankNames[bank] : "";

        if (string.IsNullOrWhiteSpace(name))
            result.Name = $"RomBank_{bank}.bmasm";
        else
            result.Name = name + ".bmasm";

        result.Path = $"Rom/{result.Name}";
        result.Origin = SourceFileOrigin.Decompiled;
        result.ReferenceId = id;
        result.RomBank = bank;

        DecompiledData.Add(result.Path, result);
    }

    public bool IsRomDecompiled(int bank) => BankToId.ContainsKey((NotSet, bank));
}

internal class DissasemblyReturn
{
    public int LastAddress { get; set; }
    public Dictionary<int, DissasemblyItem> Items { get; set; } = new();
}

internal class DissasemblyItem
{
    public int Address { get; set; }
    public DisassembledInstruction? Instruction { get; set; }
}

internal static class DecompileReturnExtensions
{
    public static Source AsSource(this DecompileReturn decompileReturn) => new Source()
    {

        Name = decompileReturn.Name,
        Path = decompileReturn.Path,
        Origin = decompileReturn.Origin.ToString(),
        SourceReference = decompileReturn.ReferenceId,
    };
}
