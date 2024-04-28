using BitMagic.Common.Address;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger;

internal class DisassemblerManager
{
    private readonly SourceMapManager _sourceMapManager;
    private readonly Emulator _emulator;
    private readonly IdManager _idManager;
    private BreakpointManager? _breakpointManager = null;

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

    public string GetName(int address, int ramBank, int RomBank)
    {
        if (address < 0xa000)
            return "MainRam.bmasm";

        if (address < 0xc000)
        {
            if (_project != null && _project.RamBankNames.Length > ramBank)
                return $"{_project.RamBankNames[ramBank]}.bmasm";

            return $"z_Bank_0x{ramBank:X2}.bmasm";
        }

        if (_project != null && _project.RomBankNames.Length > RomBank)
            return $"{_project.RomBankNames[RomBank]}.bmasm";

        return $"RomBank_{RomBank}.bmasm";
    }

    public void SetBreakpointManager(BreakpointManager breakpointManager)
    {
        _breakpointManager = breakpointManager;
    }

    public void SetProject(X16DebugProject project)
    {
        _project = project;
        for (var i = 0; i < _project.RamBankNames.Length; i++)
        {
            var id = BankToId[(i, NotSet)];
            var data = _idManager.GetObject<DecompileReturn>(id);
            var name = $"{_project.RamBankNames[i]}.bmasm";
            data.SetName(name, $"Ram/{name}");
        }
    }

    // Consider the request to be delimited by main memory, or banked memeory.
    // This way we can use the correct symbols.
    public DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.MemoryReference)) // not sure why this happens...?
        {
            return new DisassembleResponse();
        }

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

        if (result.Generate != null)
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

        if (decompileReturn.Generate != null)
            decompileReturn.Generate();

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
        decompileReturn.SetName(name, $"Ram/{name}");
        decompileReturn.RamBank = ramBank;
        decompileReturn.SetGenerate(() =>
        {
            var item = decompileReturn;
            var decompiler = new Decompiler.Decompiler();
            var data = _emulator.RamBank.Slice(ramBank * 0x2000, 0x2000);
            var result = decompiler.Decompile(data, 0xa000, 0xbfff, ramBank, _sourceMapManager.Symbols, null);
            item.Items = result.Items;
            if (item.ReferenceId != null && _breakpointManager != null)
            {
                var existing = _breakpointManager.MemoryBreakpoints(item.ReferenceId.Value).ToDictionary(i => i.Address, i => i);

                if (existing.Any())
                {
                    foreach (var i in existing.Values)
                    {
                        i.Breakpoint.Verified = false;
                    }

                    foreach (var i in result.Items.Values.Where(i => i.Address != 0 && existing.ContainsKey(i.Address)))
                    {
                        existing[i.Address].Breakpoint.Line = i.LineNumber;
                        existing[i.Address].Breakpoint.Verified = true;
                    }
                }

                _breakpointManager.UpdateBreakpoints(existing.Values.Select(i => i.Breakpoint), BreakpointsUpdatedEventArgs.BreakpointsUpdatedType.Changed);
            }
        });

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
        decompileReturn.SetName(name, name);
        decompileReturn.SetGenerate(() =>
        {
            var item = decompileReturn;
            var decompiler = new Decompiler.Decompiler();

            // decompile from 0x200 onwards
            var data = _emulator.Memory.Slice(0x200, 0xa000);
            var result = decompiler.Decompile(data, 0x0200, 0x9f00, 0, _sourceMapManager.Symbols);
            var newBreakpoints = new List<Breakpoint>();
            item.Items = result.Items;
            if (item.ReferenceId != null && _breakpointManager != null)
            {
                var existing = _breakpointManager.MemoryBreakpoints(item.ReferenceId.Value).ToDictionary(i => i.Address, i => i);

                if (existing.Any())
                {
                    foreach (var i in existing.Values)
                    {
                        i.Breakpoint.Verified = false;
                    }

                    foreach (var i in result.Items.Values.Where(i => i.Address != 0 && existing.ContainsKey(i.Address)))
                    {
                        existing[i.Address].Breakpoint.Line = i.LineNumber;
                        existing[i.Address].Breakpoint.Verified = true;

                        newBreakpoints.Add(new Breakpoint()
                        {
                            InstructionReference = AddressFunctions.GetDebuggerAddressString(i.Address, 0, 0),
                            Verified = true,
                            Offset = 0,
                            Source = new Source
                            {
                                Name = decompileReturn.Name,
                                Path = decompileReturn.Path,
                                Origin = decompileReturn.Origin.ToString(),
                                SourceReference = decompileReturn.ReferenceId,
                            },
                            Id = 2001
                        });

                        //newBreakpoints.Add(new Breakpoint()
                        //{
                        //    Verified = true,
                        //    Id = 3001,
                        //    Source = existing[i.Address].Breakpoint.Source,
                        //    Line = i.LineNumber + 1
                        //});
                    }
                }

                if (existing.Any())
                    _breakpointManager.UpdateBreakpoints(existing.Values.Select(i => i.Breakpoint), BreakpointsUpdatedEventArgs.BreakpointsUpdatedType.Changed);
                if (newBreakpoints.Any())
                    _breakpointManager.UpdateBreakpoints(newBreakpoints, BreakpointsUpdatedEventArgs.BreakpointsUpdatedType.New);
            }
        });
        DecompiledData.Add(decompileReturn.Path, decompileReturn);

        BankToId.Add((NotSet, NotSet), decompileReturn.ReferenceId ?? 0);
    }

    private DisassembleResponse ConvertDisassemblyToReponse(int instructionOffset, int instructionCount, int address, DecompileReturn decompileReturn, int ramBank, int romBank)
    {
        var toReturn = new DisassembleResponse();

        var actItems = decompileReturn.Items.Values.ToArray();

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

        var first = true;

        for (var i = 0; i < instructionCount;)
        {
            if (idx < 0)
            {
                idx++;
                i++;
                continue;
            }

            if (idx >= actItems.Length)
                break;

            var thisLine = actItems[idx];

            toReturn.Instructions.Add(new DisassembledInstruction()
            {
                Address = AddressFunctions.GetDebuggerAddressString(thisLine.Address, ramBank, romBank),
                Line = thisLine.LineNumber,
                Symbol = thisLine.Symbol,
                Instruction = thisLine.Instruction.StartsWith("/*") ? thisLine.Instruction.Substring(thisLine.Instruction.IndexOf("*/")+2).Trim()  :  thisLine.Instruction.Trim(),
                InstructionBytes = string.Join(" ", thisLine.Data.Select(i => i.ToString("X2"))),
                Location = first ? new Source
                {
                    Name = decompileReturn.Name,
                    Path = decompileReturn.Path,
                    Origin = decompileReturn.Origin.ToString(),
                    SourceReference = decompileReturn.ReferenceId,
                } : null,
            });

            i++;
            idx++;
            first = false;
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
            name = $"RomBank_{bank}.bmasm";
        else
            name += ".bmasm";

        result.SetName(name, $"Rom/{name}");

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
