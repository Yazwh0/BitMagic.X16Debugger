using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Decompiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Emulator;
using CodingSeb.ExpressionEvaluator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using static BitMagic.X16Debugger.BreakpointsUpdatedEventArgs;
using static BitMagic.X16Emulator.Emulator.VeraState;

namespace BitMagic.X16Debugger;

internal record class BreakpointState(SourceBreakpoint? SourceBreakpoint, Breakpoint Breakpoint) { public int HitCount { get; internal set; } = 0; }

internal class BreakpointManager
{
    private readonly Dictionary<int, List<MemoryBreakpointMap>> _memoryExecutionBreakpoints = new();
    private readonly Dictionary<int, List<MemoryBreakpointMap>> _instructionBreakpoints = new();

    // keyed on debugger address
    private readonly Dictionary<int, BreakpointState> _breakpoints = new();
    //private readonly Dictionary<int, int> _breakpointHitCount = new();
    private readonly Emulator _emulator;
    private readonly IdManager _idManager;
    private readonly DisassemblerManager _disassemblerManager;
    private readonly DebugableFileManager _debugableFileManager;
    private ExpressionManager? _expressionManager;

    // Function breakpoints
    private readonly List<VsyncBreakpoint> _vsyncBreakpoints = new();
    private readonly List<VramReadWriteBreakpoint> _vramReadWriteBreakpoints = new();

    public event EventHandler<BreakpointsUpdatedEventArgs>? BreakpointsUpdated;

    private readonly HashSet<int> _debuggerBreakpoints = new(); // breakpoints which the debugger rely on.

    internal BreakpointManager(Emulator emulator,
        IdManager idManager, DisassemblerManager disassemblerManager,
        DebugableFileManager debugableFileManager)
    {
        _emulator = emulator;
        _idManager = idManager;
        _disassemblerManager = disassemblerManager;
        _debugableFileManager = debugableFileManager;
    }

    internal void SetExpressionManager(ExpressionManager expressionManager)
    {
        _expressionManager = expressionManager;
    }

    public HashSet<int> DebuggerBreakpoints => _debuggerBreakpoints;

    public IEnumerable<VramReadWriteBreakpoint> VramReadWriteBreakpoints => _vramReadWriteBreakpoints;
    public IEnumerable<VsyncBreakpoint> VsyncBreakpoinst => _vsyncBreakpoints;

    public void UpdateBreakpoints(IEnumerable<Breakpoint> breakpoints, BreakpointsUpdatedType updateType)
    {
        if (BreakpointsUpdated != null)
            BreakpointsUpdated.Invoke(this, new BreakpointsUpdatedEventArgs() { Breakpoints = breakpoints.ToArray(), UpdateType = updateType });
    }

    public void SetDebuggerBreakpoints()
    {
        foreach (var debuggerAddress in _debuggerBreakpoints)
        {
            var (address, bank) = AddressFunctions.GetAddressBank(debuggerAddress);

            var currentBank = address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];
            var (primaryAddress, secondAddress) = AddressFunctions.GetMemoryLocations(bank, address);

            // only set local breakpoint if we're in the right bank
            if (primaryAddress < 0xa000 || bank == currentBank)
                _emulator.Breakpoints[address] = DebugConstants.SystemBreakpoint;

            if (secondAddress != 0)
                _emulator.Breakpoints[secondAddress] = DebugConstants.SystemBreakpoint;
        }
    }

    public List<Breakpoint> ClearBreakpoints(int debuggerAddress, int length)
    {
        var toReturn = new List<Breakpoint>();
        for (var i = debuggerAddress; i < debuggerAddress + length; i++)
        {
            if (_breakpoints.ContainsKey(i))
            {
                var bp = _breakpoints[i].Breakpoint;
                bp.Verified = false;
                toReturn.Add(bp);
                _breakpoints.Remove(i);
            }

            //if (_breakpointHitCount.ContainsKey(i))
            //    _breakpointHitCount.Remove(i);

            var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(i);
            var (offset, secondOffset) = AddressFunctions.GetMemoryLocations(ramBank > 0 ? ramBank : romBank, address);

            var breakpointValue = _debuggerBreakpoints.Contains(i) ? DebugConstants.SystemBreakpoint : 0;

            _emulator.Breakpoints[offset] = breakpointValue;
            if (secondOffset != 0)
                _emulator.Breakpoints[secondOffset] = breakpointValue;

            if (_breakpoints.ContainsKey(i))
                _breakpoints.Remove(i);
        }

        return toReturn;
    }

    public void ClearBreakpoints(DebugWrapper wrapper)
    {
        foreach (var bp in wrapper.Breakpoints)
        {
            if (bp.PrimaryAddress != 0)
                _emulator.Breakpoints[bp.PrimaryAddress] &= DebugConstants.SystemBreakpoint;
            if (bp.SecondaryAddress != 0)
                _emulator.Breakpoints[bp.SecondaryAddress] &= DebugConstants.SystemBreakpoint;

            var thisAddress = bp.SecondaryAddress == 0 ? bp.PrimaryAddress : bp.SecondaryAddress;
            if (_breakpoints.ContainsKey(thisAddress))
                _breakpoints.Remove(thisAddress);
        }
    }

    // Called when a file is loaded, this is used by the wrapper to construct its Breakpoint list.
    public List<Breakpoint> CreateBitMagicBreakpoints(int debuggerAddress, DebugWrapper wrapper, DebugableFileManager fileManager)
    {
        var toReturn = new List<Breakpoint>();
        // Breakpoints are placed in code, but we need to find where those places map to.
        // SourceBreakpoint are placed in code from VSCode
        // Breakpoint is the response, which we return as it will need to be sent to VSCode

        // Go through the file and hunt for breakpoints in the parents wrapper.
        // Can't use the actual parent, as these are not wrapped.       
        for (var i = 0; i < wrapper.ParentMap.Count; i++)
        {
            if (wrapper.ParentMap[i].relativeId != -1)
            {
                foreach (var b in wrapper.FindParentBreakpoints(i, fileManager))
                {
                    toReturn.Add(b.Breakpoint);
                    b.Breakpoint.Verified = true;

                    // set breakpoint in memory
                    var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress + i) ? DebugConstants.SystemBreakpoint + DebugConstants.Breakpoint : DebugConstants.Breakpoint;

                    var (_, bank) = AddressFunctions.GetAddressBank(debuggerAddress + i);

                    var (address, secondAddress) = AddressFunctions.GetMemoryLocations(debuggerAddress + i);
                    var currentBank = address >= 0xc000 ? _emulator.RomBankAct : _emulator.RamBankAct;

                    b.PrimaryAddress = address;
                    b.SecondaryAddress = secondAddress;

                    if (address < 0xa000 || bank == currentBank)
                        _emulator.Breakpoints[address] = breakpointValue;

                    if (secondAddress != 0)
                        _emulator.Breakpoints[secondAddress] = breakpointValue;

                    var thisAddress = secondAddress == 0 ? address : secondAddress;
                    if (!_breakpoints.ContainsKey(thisAddress))
                        _breakpoints.Add(thisAddress, new BreakpointState(b.SourceBreakpoint, b.Breakpoint));
                }
            }
        }
        return toReturn;
    }

    // Called when VSCode sets a breakpoint
    public List<Breakpoint> HandleSetBreakpointsBitmagic(SetBreakpointsArguments arguments, DebugWrapper wrapper)
    {
        ClearBreakpoints(wrapper);
        wrapper.Breakpoints.Clear();

        var toReturn = new List<Breakpoint>();

        foreach (var sbp in arguments.Breakpoints)
        {
            var added = false;
            foreach (var (debuggerAddress, loaded) in wrapper.FindUltimateAddresses(sbp.Line - 1, _debugableFileManager))
            {
                var breakpoint = sbp.ConvertBreakpoint(arguments.Source, loaded, _idManager);

                // set system bit
                var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? DebugConstants.SystemBreakpoint + DebugConstants.Breakpoint : DebugConstants.Breakpoint;

                var (_, bank) = AddressFunctions.GetAddressBank(debuggerAddress);

                var (address, secondAddress) = AddressFunctions.GetMemoryLocations(debuggerAddress);

                if (loaded)
                {
                    var currentBank = address >= 0xc000 ? _emulator.RomBankAct : _emulator.RamBankAct;

                    if (address < 0xa000 || bank == currentBank)
                        _emulator.Breakpoints[address] = breakpointValue;

                    if (secondAddress != 0)
                        _emulator.Breakpoints[secondAddress] = breakpointValue;

                    var thisAddress = secondAddress == 0 ? address : secondAddress;
                    if (!_breakpoints.ContainsKey(thisAddress))
                        _breakpoints.Add(thisAddress, new BreakpointState(sbp, breakpoint));
                }

                added = true;
                wrapper.Breakpoints.Add(new BreakpointPair(breakpoint, sbp, address, secondAddress));
                toReturn.Add(breakpoint);
            }

            if (!added)
            {
                var breakpoint = sbp.ConvertBreakpoint(arguments.Source, false, _idManager);
                wrapper.Breakpoints.Add(new BreakpointPair(breakpoint, sbp, 0, 0));
                toReturn.Add(breakpoint);
            }
        }

        return toReturn;
    }

    public SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        /// NEW CODE

        arguments.Source.Path = arguments.Source.Path.FixFilename();

        var f = _debugableFileManager.GetFileSource(arguments.Source);

        if (f != null) // can have files with breakpoints that are not part of the project
        {
            var bps = HandleSetBreakpointsBitmagic(arguments, f);

            return new SetBreakpointsResponse(bps) { Breakpoints = bps };
        }

        /// END

        // -----------------------------------------------------------------------------------------------------------
        // this isn't a BitMagic breakpoint, so set on the decompiled memory source.

        var toReturn = new SetBreakpointsResponse();

        var sourceId = arguments.Source.SourceReference ?? 0;
        var decompiledFile = _idManager.GetObject<DecompileReturn>(sourceId);

        if (decompiledFile != null && decompiledFile.Path != arguments.Source.Path)
            decompiledFile = null;

        // if the id doesn't match, then check the dissasembly cache
        if (decompiledFile == null && _disassemblerManager.DecompiledData.ContainsKey(arguments.Source.Path))
        {
            decompiledFile = _disassemblerManager.DecompiledData[arguments.Source.Path];
            sourceId = decompiledFile.ReferenceId ?? 0;
        }

        if (_memoryExecutionBreakpoints.ContainsKey(sourceId))
        {
            foreach (var breakpoint in _memoryExecutionBreakpoints[sourceId])
            {
                // Need to ensure system breakpoints are set
                var debuggerAddress = AddressFunctions.GetDebuggerAddress(breakpoint.Address, breakpoint.RamBank, breakpoint.RomBank);
                var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? DebugConstants.SystemBreakpoint : 0;

                var (offset, secondOffset) = AddressFunctions.GetMemoryLocations(breakpoint.RamBank > 0 ? breakpoint.RamBank : breakpoint.RomBank, breakpoint.Address);

                _emulator.Breakpoints[offset] = breakpointValue;
                if (secondOffset != 0)
                    _emulator.Breakpoints[secondOffset] = breakpointValue;

                var thisAddress = secondOffset == 0 ? offset : secondOffset;
                if (_breakpoints.ContainsKey(thisAddress))
                    _breakpoints.Remove(thisAddress);
            }

            _memoryExecutionBreakpoints[sourceId].Clear();
        }
        else
        {
            _memoryExecutionBreakpoints.Add(sourceId, new List<MemoryBreakpointMap>());
        }

        if (decompiledFile != null)
        {
            foreach (var sourceBreakpoint in arguments.Breakpoints)
            {
                if (decompiledFile.RequireUpdate)
                    decompiledFile.UpdateContent().GetAwaiter().GetResult();

                if (!decompiledFile.Items.ContainsKey(sourceBreakpoint.Line))
                {
                    continue;
                }

                var thisLine = decompiledFile.Items[sourceBreakpoint.Line];

                var breakpoint = new Breakpoint
                {
                    Source = decompiledFile.AsSource(),
                    Line = sourceBreakpoint.Line,
                    Verified = true,
                    Id = _idManager.GetId()
                };

                var debuggerAddress = AddressFunctions.GetDebuggerAddress(thisLine.Address, decompiledFile.RamBank, decompiledFile.RomBank);
                var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? DebugConstants.SystemBreakpoint + DebugConstants.Breakpoint : DebugConstants.Breakpoint;

                //breakpoint.InstructionReference = AddressFunctions.GetDebuggerAddressString(debuggerAddress, decompiledFile.RamBank, decompiledFile.RomBank);

                var toAdd = new MemoryBreakpointMap(thisLine.Address, decompiledFile.RamBank, decompiledFile.RomBank, breakpoint);
                _memoryExecutionBreakpoints[sourceId].Add(toAdd);

                var bank = thisLine.Address >= 0xc000 ? decompiledFile.RomBank : decompiledFile.RamBank;
                var currentBank = thisLine.Address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];
                var (address, secondAddress) = AddressFunctions.GetMemoryLocations(bank, thisLine.Address);

                // only set local breakpoint if we're in the right bank
                if (address < 0xa000 || bank == currentBank)
                    _emulator.Breakpoints[address] = breakpointValue;

                if (secondAddress != 0)
                    _emulator.Breakpoints[secondAddress] = breakpointValue;

                var thisAddress = secondAddress == 0 ? address : secondAddress;
                if (!_breakpoints.ContainsKey(thisAddress))
                    _breakpoints.Add(thisAddress, new BreakpointState(sourceBreakpoint, breakpoint));
            }
        }

        toReturn.Breakpoints.AddRange(_memoryExecutionBreakpoints[sourceId].Select(i => i.Breakpoint));


        return toReturn;
    }

    public SetInstructionBreakpointsResponse HandleSetInstructionBreakpointsRequest(SetInstructionBreakpointsArguments arguments)
    {
        var toReturn = new SetInstructionBreakpointsResponse();

        foreach (var b in arguments.Breakpoints)
        {
            var debuggerAddress = Convert.ToInt32(b.InstructionReference, 16);
            debuggerAddress += b.Offset ?? 0;

            var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(debuggerAddress);

            var name = _disassemblerManager.GetName(address, ramBank, romBank);
            if (!_disassemblerManager.DecompiledData.ContainsKey(name))
            {
                var breakpoint = new Breakpoint()
                {
                    Verified = false,
                    Message = "Cannot locate decompiled data.",
                    InstructionReference = b.InstructionReference,
                    Offset = b.Offset
                };

                toReturn.Breakpoints.Add(breakpoint);
                continue;
            }

            var decompiledFile = _disassemblerManager.DecompiledData[name];

            if (decompiledFile.ReferenceId == null)
            {
                var breakpoint = new Breakpoint()
                {
                    Verified = false,
                    Message = "Decompiled file has no reference ID.",
                    InstructionReference = b.InstructionReference,
                    Offset = b.Offset
                };

                toReturn.Breakpoints.Add(breakpoint);
                continue;
            }

            var sourceId = decompiledFile.ReferenceId ?? 0;

            if (!_instructionBreakpoints.ContainsKey(sourceId))
            {
                _instructionBreakpoints.Add(sourceId, new List<MemoryBreakpointMap>());
            }
            else
            {
                // clear existing breakpoints
                foreach (var i in _instructionBreakpoints[sourceId])
                {
                    var da = AddressFunctions.GetDebuggerAddress(i.Address, i.RamBank, i.RomBank);
                    var breakpointValue = _debuggerBreakpoints.Contains(da) ? DebugConstants.SystemBreakpoint : 0;

                    var (offset, secondOffset) = AddressFunctions.GetMemoryLocations(i.RamBank > 0 ? i.RamBank : i.RomBank, i.Address);

                    _emulator.Breakpoints[offset] = breakpointValue;
                    if (secondOffset != 0)
                        _emulator.Breakpoints[secondOffset] = breakpointValue;

                    var thisAddress = secondOffset == 0 ? offset : secondOffset;
                    if (_breakpoints.ContainsKey(thisAddress))
                        _breakpoints.Remove(thisAddress);
                }

                _instructionBreakpoints[sourceId].Clear();
            }

            bool found = false;
            foreach (var i in decompiledFile.Items.Values.Where(i => i.Address == address))
            {
                var breakpoint = new Breakpoint()
                {
                    Verified = true,
                    InstructionReference = b.InstructionReference,
                    Offset = b.Offset,
                    Source = decompiledFile.AsSource()
                };

                toReturn.Breakpoints.Add(breakpoint);

                var toAdd = new MemoryBreakpointMap(address, decompiledFile.RamBank, decompiledFile.RomBank, breakpoint);
                _instructionBreakpoints[sourceId].Add(toAdd);

                var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? DebugConstants.SystemBreakpoint + DebugConstants.Breakpoint : DebugConstants.Breakpoint;

                var bank = address >= 0xc000 ? decompiledFile.RomBank : decompiledFile.RamBank;
                var currentBank = address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];
                var (lowAddress, secondAddress) = AddressFunctions.GetMemoryLocations(bank, address);

                // only set local breakpoint if we're in the right bank
                if (lowAddress < 0xa000 || bank == currentBank)
                    _emulator.Breakpoints[address] = breakpointValue;

                if (secondAddress != 0)
                    _emulator.Breakpoints[secondAddress] = breakpointValue;

                var thisAddress = secondAddress == 0 ? address : secondAddress;
                if (!_breakpoints.ContainsKey(thisAddress))
                    _breakpoints.Add(thisAddress, new BreakpointState(null, breakpoint));

                found = true;
                break;
            }

            if (found)
                continue;

            toReturn.Breakpoints.Add(new Breakpoint()
            {
                Verified = false,
                Message = $"No instruction at {address}",
                InstructionReference = b.InstructionReference,
                Offset = b.Offset
            });
        }

        return toReturn;
    }

    // function breakpoints are used to set data breakpoints on vram, as well as frame breaks
    public SetFunctionBreakpointsResponse HandleFunctionBreakpointsRequest(SetFunctionBreakpointsArguments arguments)
    {
        var toReturn = new SetFunctionBreakpointsResponse();

        _vramReadWriteBreakpoints.Clear();
        _vsyncBreakpoints.Clear();

        // clear out VRAM Breakpoints
        for (var i = 0; i < 0x20000; i++)
        {
            _emulator.VramBreakpoints[i] = 0;
        }

        foreach (var b in arguments.Breakpoints)
        {
            bool added = false;

            try
            {
                _expressionManager!.EvaluateExpression(b.Name, (object? _, FunctionEvaluationEventArg e) =>
                {
                    var breakpoint = e.Name switch
                    {
                        "vram" => SetVramBreakpoint(e),
                        "vsync" => SetVsyncBreakpoint(e),
                        _ => new Breakpoint()
                        {
                            Message = $"Unknown breakpoint function '{e.Name}'.",
                            Verified = false
                        }
                    };

                    if (breakpoint != null)
                    {
                        toReturn.Breakpoints.Add(breakpoint);
                        added = true;
                    }

                    e.FunctionReturnedValue = true;
                });
            }
            catch (Exception e)
            {
                added = true;
                toReturn.Breakpoints.Add(new Breakpoint() { Message = e.Message, Verified = false });
            }

            if (!added)
                toReturn.Breakpoints.Add(new Breakpoint() { Message = "Not a breakpoint function.", Verified = false });
        }

        return toReturn;
    }

    private Breakpoint? SetVramBreakpoint(FunctionEvaluationEventArg evaluateArgs)
    {
        var toReturn = new Breakpoint();
        toReturn.Id = _idManager.GetId();

        if (evaluateArgs.Args.Count == 0)
        {
            toReturn.Message = "Invalid parameters.";
            toReturn.Verified = false;
            return toReturn;
        }

        int address;
        int length = 1;
        try
        {
            address = evaluateArgs.EvaluateArg<int>(0);
        }
        catch (Exception e)
        {
            toReturn.Message = e.Message;
            toReturn.Verified = false;
            return toReturn;
        }

        var breakpointType = VeraBreakpointType.Read | VeraBreakpointType.Write;

        if (evaluateArgs.Args.Count >= 2)
        {
            var secondParam = evaluateArgs.EvaluateArg(1);

            if (secondParam is string sparam)
            {
                breakpointType = (VeraBreakpointType)(
                                 (sparam.Contains('R') ? (int)VeraBreakpointType.Read : 0) +
                                 (sparam.Contains('W') ? (int)VeraBreakpointType.Write : 0));
            }

            if (secondParam is int iparam)
            {
                address = Math.Min(address, iparam);
                var second = Math.Max(address, iparam);
                length = second - address;
            }
        }

        if (evaluateArgs.Args.Count >= 3)
        {
            var thirdParam = evaluateArgs.EvaluateArg(2);

            if (thirdParam is string sparam)
            {
                breakpointType = (VeraBreakpointType)(
                                 (sparam.Contains('R') ? (int)VeraBreakpointType.Read : 0) +
                                 (sparam.Contains('W') ? (int)VeraBreakpointType.Write : 0));
            }
        }

        length = Math.Min(length, 0x20000 - address);

        if (length == 1)
            toReturn.Message = $"VRAM 0x{address:X5} for{((breakpointType & VeraBreakpointType.Read) != 0 ? " Read" : "")}{((breakpointType & VeraBreakpointType.Write) != 0 ? " Write" : "")}";
        else
            toReturn.Message = $"VRAM 0x{address:X5} -> 0x{address + length:X5} ({length} bytes) for{((breakpointType & VeraBreakpointType.Read) != 0 ? " Read" : "")}{((breakpointType & VeraBreakpointType.Write) != 0 ? " Write" : "")}" ;

        toReturn.Verified = true;

        var toAdd = new VramReadWriteBreakpoint(new BreakpointState(null, toReturn), address, length, breakpointType);
        toAdd.Apply(_emulator);

        _vramReadWriteBreakpoints.Add(toAdd);

        return toReturn;
    }

    private Breakpoint? SetVsyncBreakpoint(FunctionEvaluationEventArg evaluateArgs)
    {
        var toReturn = new Breakpoint();

        toReturn.Id = _idManager.GetId();
        toReturn.Verified = true;
        uint frameNumber = 0;

        if (evaluateArgs.Args.Count == 0)
        {
            // always set
            _emulator.Vera.Frame_Count_Breakpoint = _emulator.State.Frame_Count + 1;
            toReturn.Message = $"VSYNC on the next frame {_emulator.State.Frame_Count + 1}";
        }
        else
        {
            frameNumber = (uint)evaluateArgs.EvaluateArg<int>(0);
            toReturn.Message = $"VSYNC on frame {frameNumber}";

            if (_emulator.Vera.Frame_Count_Breakpoint > frameNumber || _emulator.Vera.Frame_Count_Breakpoint <= _emulator.Vera.Frame_Count)
                _emulator.Vera.Frame_Count_Breakpoint = frameNumber;
        }

        var toAdd = new VsyncBreakpoint(new BreakpointState(null, toReturn), frameNumber);

        _vsyncBreakpoints.Add(toAdd);

        return toReturn;
    }

    public List<MemoryBreakpointMap> MemoryBreakpoints(int sourceId)
    {
        if (_memoryExecutionBreakpoints.ContainsKey(sourceId))
            return _memoryExecutionBreakpoints[sourceId];

        return new List<MemoryBreakpointMap>(0);
    }

    /// <summary>
    /// Gets a breakpoint and the times its been hit this run. Important: Increments the hitcount.
    /// </summary>
    /// <param name="address"></param>
    /// <param name="ramBank"></param>
    /// <param name="romBank"></param>
    /// <returns></returns>
    public (BreakpointState? BreakpointState, uint BreakpointValue) GetCurrentBreakpoint(int address, int ramBank, int romBank)
    {
        var (_, secondAddress) = AddressFunctions.GetMemoryLocations(address >= 0xc000 ? romBank : ramBank, address);

        var thisAddress = secondAddress == 0 ? address : secondAddress;

        if (_breakpoints.ContainsKey(thisAddress))
        {
            var bps = _breakpoints[thisAddress];
            bps.HitCount++;
            return (bps, _emulator.Breakpoints[address]);
        }

        return (null, _emulator.Breakpoints[address]);
    }

    public void Clear()
    {
        _memoryExecutionBreakpoints.Clear();
        _instructionBreakpoints.Clear();

        // just to be sure
        for (var i = 0; i < _emulator.Breakpoints.Length; i++)
        {
            _emulator.Breakpoints[i] = 0;
        }
    }

    public void SetNonSourceBreakpoints(IEnumerable<int> breakpoints)
    {
        foreach (var address in breakpoints)
        {
            var machineAddress = AddressFunctions.GetMachineAddress(address);

            var breakpointValue = _debuggerBreakpoints.Contains(address) ? DebugConstants.SystemBreakpoint + DebugConstants.Breakpoint : DebugConstants.Breakpoint;

            var bank = address >= 0xc000 ? machineAddress.RomBank : machineAddress.RamBank;
            var currentBank = address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];
            var (lowAddress, secondAddress) = AddressFunctions.GetMemoryLocations(bank, address);

            // only set local breakpoint if we're in the right bank
            if (lowAddress < 0xa000 || bank == currentBank)
                _emulator.Breakpoints[address] = breakpointValue;

            if (secondAddress != 0)
                _emulator.Breakpoints[secondAddress] = breakpointValue;
        }
    }
}

internal class VramReadWriteBreakpoint
{
    public BreakpointState BreakpointState { get; }
    public int Address { get; }
    public int Length { get; }
    public VeraBreakpointType BreakpointType { get; }

    public VramReadWriteBreakpoint(BreakpointState breakpoint, int address, int lenght, VeraBreakpointType breakpointType)
    {
        BreakpointState = breakpoint;
        Address = address;
        Length = lenght;
        BreakpointType = breakpointType;
    }

    public void Apply(Emulator emulator)
    {
        for (var i = Address; i < Address + Length; i++)
        {
            emulator.VramBreakpoints[i] = (byte)((byte)BreakpointType | emulator.VramBreakpoints[i]);
        }
    }
}

internal class VsyncBreakpoint
{
    public BreakpointState BreakpointState { get; }
    public uint FrameNumber { get; set; }

    public VsyncBreakpoint(BreakpointState breakpoint, uint frameNumber)
    {
        BreakpointState = breakpoint;
        FrameNumber = frameNumber;
    }
}


internal class BitMagicBreakpointMap
{
    public Breakpoint Breakpoint { get; }
    public IOutputData? Source { get; set; }

    internal BitMagicBreakpointMap(Breakpoint breakpoint, IOutputData? source)
    {
        Breakpoint = breakpoint;
        Source = source;
    }
}

internal class MemoryBreakpointMap
{
    public int Address { get; }
    public int RamBank { get; }
    public int RomBank { get; }
    public Breakpoint Breakpoint { get; }

    public MemoryBreakpointMap(int address, int ramBank, int romBank, Breakpoint breakpoint)
    {
        Address = address;
        RamBank = ramBank;
        RomBank = romBank;
        Breakpoint = breakpoint;
    }
}

internal static class SouceBreakpointExtensions
{
    internal static Breakpoint ConvertBreakpoint(this SourceBreakpoint breakpoint, Source source, bool verified, IdManager idManager)
    {
        return new Breakpoint()
        {
            Line = breakpoint.Line,
            Id = idManager.GetId(),
            Source = source,
            Verified = verified
        };
    }
}

public class BreakpointsUpdatedEventArgs : EventArgs
{
    public Breakpoint[]? Breakpoints { get; set; }
    public BreakpointsUpdatedType UpdateType { get; set; } = BreakpointsUpdatedType.Changed;

    public enum BreakpointsUpdatedType
    {
        New,
        Changed,
        Removed
    }
}
