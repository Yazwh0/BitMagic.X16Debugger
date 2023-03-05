using BitMagic.Compiler;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.Core.Contexts;
using System.Transactions;
using static BitMagic.Decompiler.Addressing;

namespace BitMagic.X16Debugger;

internal class DissassemblerManager
{
    private readonly SourceMapManager _sourceMapManager;
    private readonly Emulator _emulator;

    public DissassemblerManager(SourceMapManager sourceMapManager, Emulator emulator)
    {
        _sourceMapManager = sourceMapManager;
        _emulator = emulator;
    }

    // Consider the request to be delimited by main memory, or banked memeory.
    // This way we can use the correct symbols.
    public DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        var toReturn = new DisassembleResponse();

        // assume this address the debugger address, so convert it to the machine address
        var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(Convert.ToInt32(arguments.MemoryReference, 16));

        if (address > 0xc000 && _sourceMapManager.DecompiledRom.ContainsKey(romBank))
            return DisassembleRequestFromRom(arguments, address, romBank);


        var instructions = new Dictionary<int, DisassembledInstruction>(); // offset
        var done = false;
        var startOffset = 0;
        var dataValid = false;
        DissasemblyReturn dissasembleResult;

        // walk backwards
        if ((arguments.InstructionOffset ?? 0) < 0)
        {
            var offset = arguments.InstructionOffset ?? 0 * 3;
            while (!done)
            {
                var startAddress = address + offset + startOffset++;

                if (startAddress < 0)
                    startAddress = 0;

                dissasembleResult = DissasembleMainMemory(startAddress, address, ramBank, romBank);

                if (dissasembleResult.LastAddress == address)
                {
                    foreach (var kv in dissasembleResult.Items.TakeLast(offset * -1))
                        instructions.Add(offset++, kv.Value.Instruction); // offset value doesn't matter, as long as its always negative, for ordering later

                    done = true;
                    dataValid = true;
                }

                if (startOffset > arguments.InstructionOffset * -3)
                {
                    dataValid = false;
                    done = true;
                }
            }
        }

        if (!dataValid)
        {
            // and do....?
        }

        // walk forwards
        dissasembleResult = DissasembleMainMemory(address, address + 3 * (arguments.InstructionCount + (arguments.InstructionOffset ?? 0)), ramBank, romBank);

        var idx = 0;
        foreach (var i in dissasembleResult.Items.Values.Take(arguments.InstructionCount + (arguments.InstructionOffset ?? 0)))
            instructions.Add(idx++, i.Instruction);


        toReturn.Instructions.AddRange(instructions.OrderBy(i => i.Key).Select(i => i.Value));

        return toReturn;
    }

    // We've been asked to dissemble ROM, which we've already done.
    // As ROM cannot be changed, we can happily use these symbols.
    private DisassembleResponse DisassembleRequestFromRom(DisassembleArguments arguments, int address, int romBank)
    {
        var toReturn = new DisassembleResponse();

        var romResult = _sourceMapManager.DecompiledRom[romBank];
        int referenceLine = 0;

        foreach (var i in romResult.Items.Values)
        {
            if (i.Address >= address)
            {
                referenceLine = i.LineNumber;
                break;
            }
        }

        var currentLine = referenceLine;
        var idx = 0;

        // hunt backward for the right linenumber
        while(true)
        {
            if (currentLine < 0 || idx == arguments.InstructionOffset || !romResult.Items.ContainsKey(currentLine))
            {
                break;
            }

            // only decr index if this is a line we'd output.
            if (romResult.Items[currentLine].Address > 0)
                idx--;

            currentLine--;
        }

        idx = 0;
        while (idx < arguments.InstructionCount)
        {
            if (!romResult.Items.ContainsKey(currentLine))
            {
                currentLine++;
                idx++;
                continue;
            }

            var thisLine = romResult.Items[currentLine];

            if (thisLine.Address != 0)
            {
                toReturn.Instructions.Add(new DisassembledInstruction()
                {
                    Address = AddressFunctions.GetDebuggerAddressString(thisLine.Address, 0, romBank),
                    //Line = currentLine,
                    Symbol = thisLine.Symbol,
                    Instruction = thisLine.Instruction,
                    InstructionBytes = string.Join(" ", thisLine.Data.Select(i => i.ToString("X2"))),
                });

                idx++;
            }

            currentLine++;
        }

        return toReturn;
    }

    private DisassembledInstruction GetNextInstruction(int debuggerAddress)
    {
        return new DisassembledInstruction();
    }


    private DissasemblyReturn DissasembleMainMemory(int startAddress, int endAddress, int ramBank, int romBank)
    {
        var toReturn = new DissasemblyReturn();
        var lineCount = 0;

        while (startAddress < endAddress)
        {
            var instruction = GetValidInstruction(startAddress, ramBank, romBank);

            if (instruction.Valid)
            {
                var source = _sourceMapManager.GetSourceMap(startAddress);
                Source? location = null;
                int? lineNumber = null;
                if (source != null)
                {
                    location = new Source()
                    {
                        Name = Path.GetFileName(source.Line.Source.Name),
                        Path = source.Line.Source.Name,
                    };
                    //lineNumber = source.Line.Source.LineNumber; // removed for now, as linking exactly shows the original asm and the dest. Maybe use when we have macros??
                }

                toReturn.Items.Add(lineCount++,
                    new DissasemblyItem()
                    {
                        Address = startAddress,
                        Instruction = new DisassembledInstruction()
                        {
                            Address = $"0x{startAddress:X6}",
                            Instruction = GetDisassemblyDisplay(instruction.OpCode, instruction.AddressMode, instruction.Parameter, startAddress),
                            InstructionBytes = string.Join(" ", instruction.Data!.Select(i => $"${i:X2}")),
                            Symbol = _sourceMapManager.GetSymbol(startAddress),
                            Location = location,
                            Line = lineNumber
                        }
                    });
                startAddress += Addressing.GetInstructionLenth(instruction.AddressMode);
            }
            else
            {
                toReturn.Items.Add(lineCount++,
                    new DissasemblyItem()
                    {
                        Address = startAddress,
                        Instruction = new DisassembledInstruction()
                        {
                            Address = $"0x{startAddress:X6}",
                            Instruction = $".byte ${instruction.Parameter:X2}",
                            InstructionBytes = $"{instruction.Parameter:X2}",
                            Symbol = _sourceMapManager.GetSymbol(startAddress)
                        }
                    });

                startAddress++;
            }
        }

        toReturn.LastAddress = startAddress;
        return toReturn;
    }

    private string GetDisassemblyDisplay(string opCode, AddressMode addressMode, int parameter, int address)
    {
        var symbol = _sourceMapManager.GetSymbol(parameter);

        opCode = opCode.ToLower();

        if (addressMode == AddressMode.Implied || addressMode == AddressMode.Accumulator || addressMode == AddressMode.Immediate || string.IsNullOrWhiteSpace(symbol))
        {
            return $"{opCode} {Addressing.GetModeText(addressMode, parameter, address)}";
        }

        return $"{opCode} {symbol}\t\t; {Addressing.GetModeText(addressMode, parameter, address)}";
    }

    //private (DisassembledInstruction Instruction, bool Complete, int NewAddress) GetPreviousInstruction(int currentAddress, int ramBank, int romBank)
    //{
    //    // first look in the source map for the nearest instruction behind, will use this as a hint for what could be the instruction
    //    var map = _sourceMapManager.GetPreviousMap(AddressFunctions.GetDebuggerAddress(currentAddress, ramBank, romBank));
    //    var address = 0;

    //    if (map != null)
    //    {
    //        address = map.DebuggerAddress;
    //        if (address >= currentAddress - 3)
    //        {
    //            // if we're within 3 bytes, lets check if we have a valid opcode and call it a day.
    //            var instruction = GetValidInstruction(address, ramBank, romBank, currentAddress - address);

    //            if (instruction.Valid)
    //            {
    //                return (new DisassembledInstruction()
    //                {
    //                    Address = $"0x{address}",
    //                    Instruction = $"{instruction.OpCode} {AddressModes.GetModeText(instruction.AddressMode, instruction.Parameter)}",
    //                    InstructionBytes = string.Join(" ", instruction.Data!.Select(i => i.ToString("X2"))),
    //                }, false, address);
    //            }
    //        }
    //    }





    //    var line = map.Line;

    //    var (opCode, addressMode) = OpCodes.GetOpcode(line.Data[0]);
    //    opCode ??= $"?? 0x{line.Data[0]:X2}";

    //    return (new DisassembledInstruction()
    //    {
    //        Address = $"0x{map.DebuggerAddress}",
    //        Instruction = $"{opCode} {AddressModes.GetModeText(addressMode, )}",
    //        InstructionBytes = string.Join(" ", line.Data.Select(i => i.ToString("X2"))),
    //        Location = new Source()
    //        {
    //            Name = Path.GetFileName(line.Source.Name),
    //            Path = line.Source.Name
    //        },
    //        Line = line.Source.LineNumber,
    //    }, false, map.DebuggerAddress);



    //    return (new DisassembledInstruction(), false, currentAddress);
    //}

    // Fetch a byte from the system.
    // Use the main memory if the bank matches the requested bank, otherwise use the banks in the emulator
    private byte GetMemoryValue(int address, int ramBank, int romBank)
    {
        if (address >= 0xc000)
        {
            if (_emulator.Memory[0x01] == romBank)
                return _emulator.Memory[address];

            return _emulator.RomBank[romBank * 0x4000 + address - 0xc000];
        }

        if (address >= 0xa000)
        {
            if (_emulator.Memory[0x00] == ramBank)
                return _emulator.Memory[address];

            return _emulator.RamBank[ramBank * 0x2000 + address - 0xa000];
        }

        return _emulator.Memory[address];
    }

    private (bool Valid, string OpCode, AddressMode AddressMode, int Parameter, byte[]? Data) GetValidInstruction(int debuggerAddress, int ramBank, int romBank, int reqLength = 0)
    {
        var opcodeValue = GetMemoryValue(debuggerAddress, ramBank, romBank);

        var opCode = OpCodes.GetOpcode(opcodeValue);

        if (string.IsNullOrWhiteSpace(opCode.OpCode))
            return (false, "", AddressMode.Implied, opcodeValue, null);

        var instructionLength = Addressing.GetInstructionLenth(opCode.AddressMode);

        if (reqLength != 0 && instructionLength != reqLength)
            return (false, "", AddressMode.Implied, opcodeValue, null);

        var data = instructionLength switch
        {
            1 => new byte[] { opcodeValue },
            2 => new byte[] { opcodeValue, GetMemoryValue(debuggerAddress + 1, ramBank, romBank) },
            3 => new byte[] { opcodeValue, GetMemoryValue(debuggerAddress + 1, ramBank, romBank), GetMemoryValue(debuggerAddress + 2, ramBank, romBank) },
            _ => Array.Empty<byte>(),
        };

        var parameter = instructionLength switch
        {
            2 => data[1],
            3 => data[1] + (data[2] << 8),
            _ => 0
        };

        return (true, opCode.OpCode, opCode.AddressMode, parameter, data);
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
    public DisassembledInstruction Instruction { get; set; }
}