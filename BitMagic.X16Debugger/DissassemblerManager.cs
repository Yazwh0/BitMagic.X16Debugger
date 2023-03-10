using BitMagic.Compiler;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using System.Net;
using System.Transactions;
using static BitMagic.Decompiler.Addressing;

namespace BitMagic.X16Debugger;

internal class DisassemblerManager
{
    private readonly SourceMapManager _sourceMapManager;
    private readonly Emulator _emulator;
    private readonly IdManager _idManaager;

    private int _mainRamId = 0;

    public DisassemblerManager(SourceMapManager sourceMapManager, Emulator emulator, IdManager idManaager)
    {
        _sourceMapManager = sourceMapManager;
        _emulator = emulator;
        _idManaager = idManaager;
    }

    // Consider the request to be delimited by main memory, or banked memeory.
    // This way we can use the correct symbols.
    public DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        // assume this address the debugger address, so convert it to the machine address
        var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(Convert.ToInt32(arguments.MemoryReference, 16));

        Console.WriteLine($"Disassemble request: 0x{address:X4} A:{ramBank} O:{romBank} {arguments.InstructionCount} {arguments.InstructionOffset}");


        if (address > 0xc000 && _sourceMapManager.DecompiledRom.ContainsKey(romBank))
            return DisassembleRequestFromRom(arguments, address, romBank);

        return DisassembleRequestFromMainMemory(arguments, address, _emulator.Pc);
    }

    private DisassembleResponse DisassembleRequestFromMainMemory(DisassembleArguments arguments, int address, int pc)
    {
        DecompileReturn? result = null;
        if (_mainRamId != 0)
            result = _idManaager.GetObject<DecompileReturn>(_mainRamId);

        if (result == null)
        {
            var decompiler = new Decompiler.Decompiler();

            var additionalSymbols = new Dictionary<int, string>
            {
                { 0x100, "stackstart" },
                { 0x200, "stackend" },
                { pc, "_pc" }
            };

            result = decompiler.Decompile(_emulator.Memory[0..0x9fff], 0, 0x9fff, 0, _sourceMapManager.Symbols, additionalSymbols);


            if (_mainRamId == 0)
            {
                result.Name = "MainRam.bmasm";
                result.Path = "Ram/MainRam.bmasm";
                result.Origin = "Decompiled";

                var id = _idManaager.AddObject(result, ObjectType.DecompiledData);
                result.ReferenceId = id;
            }
            else
                _idManaager.UpdateObject(_mainRamId, result);
        }

        return ConvertDisassemblyToReponse(arguments.InstructionOffset ?? 0, arguments.InstructionCount, address, result, 0, 0);
    }

    // We've been asked to dissemble ROM, which we've already done.
    // As ROM cannot be changed, we can happily use these symbols.
    private DisassembleResponse DisassembleRequestFromRom(DisassembleArguments arguments, int address, int romBank)
    {
        var romResult = _sourceMapManager.DecompiledRom[romBank];

        return ConvertDisassemblyToReponse(arguments.InstructionOffset ?? 0, arguments.InstructionCount, address, romResult, 0, romBank);
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
                    Origin = decompileReturn.Origin,
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

    public int GetDisassembleyId(int address, int ramBank, int romBank)
    {
        if (address < 0xa000)
            return _mainRamId;

        if (address > 0xc000)
            return _sourceMapManager.RomToId[romBank];

        return 0;
    }

    public int GetDisassembleyId(int debuggerAddress)
    {
        var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(debuggerAddress);

        return GetDisassembleyId(address, ramBank, romBank);
    }
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