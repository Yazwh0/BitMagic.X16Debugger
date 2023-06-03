using BitMagic.Common;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class BreakpointManager
{
    // breakpoints for bmasm files
    private readonly Dictionary<string, List<BitMagicBreakpointMap>> _bitMagicBreakpoints = new();
    private readonly Dictionary<int, List<MemoryBreakpointMap>> _memoryBreakpoints = new();
    private readonly Dictionary<int, SourceBreakpoint> _breakpoints = new();
    private readonly Dictionary<int, int> _breakpointHitCount = new();
    private readonly Emulator _emulator;
    private readonly X16Debug _debugger;
    private readonly SourceMapManager _sourceMapManager;
    private readonly IdManager _idManager;
    private readonly DisassemblerManager _disassemblerManager;
    private readonly CodeGeneratorManager _codeGeneratorManager;

    private HashSet<int> _debuggerBreakpoints = new(); // breakpoints which the debugger rely on.

    internal BreakpointManager(Emulator emulator, X16Debug debugger, SourceMapManager sourceMapManager,
        IdManager idManager, DisassemblerManager disassemblerManager, CodeGeneratorManager codeGeneratorManager)
    {
        _emulator = emulator;
        _debugger = debugger;
        _sourceMapManager = sourceMapManager;
        _idManager = idManager;
        _disassemblerManager = disassemblerManager;
        _codeGeneratorManager = codeGeneratorManager;
    }

    public HashSet<int> DebuggerBreakpoints => _debuggerBreakpoints;

    public void SetDebuggerBreakpoints()
    {
        foreach(var debuggerAddress in _debuggerBreakpoints)
        {
            var (address, bank) = AddressFunctions.GetAddressBank(debuggerAddress);

            var currentBank = address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];
            var (primaryAddress, secondAddress) = AddressFunctions.GetMemoryLocations(bank, address);

            // only set local breakpoint if we're in the right bank
            if (primaryAddress < 0xa000 || bank == currentBank)
                _emulator.Breakpoints[address] = 0x80;

            if (secondAddress != 0)
                _emulator.Breakpoints[secondAddress] = 0x80;
        }
    }

    public SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        // There are two types of breakpoint, those on BitMagic code, and those on Rom\Ram. They have to be handled slightly differently.

        var isBitMagic = false;

        if ((arguments.Source.SourceReference ?? 0) != 0)
        {
            var source = _idManager.GetObject<ISourceFile>(arguments.Source.SourceReference ?? 0);

            if (source != null)
                isBitMagic = source.Origin == SourceFileOrigin.Intermediary;
        }
        else
            isBitMagic = true;


        // Clear breakpoints
        if (isBitMagic)
        {
            if (_bitMagicBreakpoints.ContainsKey(arguments.Source.Path))
            {
                foreach (var breakpoint in _bitMagicBreakpoints[arguments.Source.Path].Where(i => i.Breakpoint.Verified))
                {
                    if (breakpoint.Source == null)
                        continue;

                    // Need to ensure system breakpoints are set
                    var breakpointValue = _debuggerBreakpoints.Contains(breakpoint.Source.Address) ? (byte)0x80 : (byte)0;

                    // todo: add bank handling
                    var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(breakpoint.Source.Address);
                    var (offset, secondOffset) = AddressFunctions.GetMemoryLocations(ramBank > 0 ? ramBank : romBank, address);

                    _emulator.Breakpoints[offset] = breakpointValue;
                    if (secondOffset != 0)
                        _emulator.Breakpoints[secondOffset] = breakpointValue;

                    var thisAddress = secondOffset == 0 ? offset : secondOffset;
                    if (_breakpoints.ContainsKey(thisAddress))
                        _breakpoints.Remove(thisAddress);
                }

                _bitMagicBreakpoints[arguments.Source.Path].Clear();
            }
            else
            {
                _bitMagicBreakpoints.Add(arguments.Source.Path, new List<BitMagicBreakpointMap>());
            }

            var bitMagicBreakpoints = _bitMagicBreakpoints[arguments.Source.Path];
            // Add breakpoints
            foreach (var sourceBreakpoint in arguments.Breakpoints)
            {
                // get id of generated file
                var template = _codeGeneratorManager.Get(arguments.Source.Path);

                // get the map to the generated file.
                var filemap = _sourceMapManager.GetSourceFileMap(template.Template.Name);
                if (filemap == null) // we dont recognise the file
                    continue;

                // now we need to find all the instances in filemap were the line matches in the template.
                // as one line in the template can produce multiple items in the filemap.
                var map = template.Template.Source.Map;
                for (var i = 0; i < map.Length; i++)
                {
                    if (map[i] == sourceBreakpoint.Line)
                    {
                        // add breakpoint.
                        var lineNumber = i + 1;
                        var source = filemap.FirstOrDefault(i => i.LineNumber == lineNumber);

                        if (source == null) // dont recognise the line
                            continue;

                        // set system bit
                        var breakpointValue = _debuggerBreakpoints.Contains(source.Address) ? (byte)0x81 : (byte)0x01;

                        var breakpoint = new Breakpoint();
                        breakpoint.Source = arguments.Source;
                        breakpoint.Line = sourceBreakpoint.Line;
                        breakpoint.Verified = source != null;

                        var toAdd = new BitMagicBreakpointMap(breakpoint, source!.Line);

                        bitMagicBreakpoints.Add(toAdd);

                        var (address, secondAddress) = AddressFunctions.GetMemoryLocations(source!.Bank, source.Address);
                        var currentBank = address >= 0xc000 ? _emulator.Memory[0x01] : _emulator.Memory[0x00];

                        if (address < 0xa000 || source!.Bank == currentBank)
                            _emulator.Breakpoints[address] = breakpointValue;

                        if (secondAddress != 0)
                            _emulator.Breakpoints[secondAddress] = breakpointValue;

                        var thisAddress = secondAddress == 0 ? address : secondAddress;
                        if (!_breakpoints.ContainsKey(thisAddress))
                            _breakpoints.Add(thisAddress, sourceBreakpoint);
                    }
                }
            }

            return new SetBreakpointsResponse(bitMagicBreakpoints.Select(i => i.Breakpoint).ToList());
        }

        // this isn't a BitMagic breakpoint, so set on the decompiled memory source.
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

        if (_memoryBreakpoints.ContainsKey(sourceId))
        {
            foreach (var breakpoint in _memoryBreakpoints[sourceId])
            {
                // Need to ensure system breakpoints are set
                var debuggerAddress = AddressFunctions.GetDebuggerAddress(breakpoint.Address, breakpoint.RamBank, breakpoint.RomBank);
                var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? (byte)0x80 : (byte)0;

                var (offset, secondOffset) = AddressFunctions.GetMemoryLocations(breakpoint.RamBank > 0 ? breakpoint.RamBank : breakpoint.RomBank, breakpoint.Address);

                _emulator.Breakpoints[offset] = breakpointValue;
                if (secondOffset != 0)
                    _emulator.Breakpoints[secondOffset] = breakpointValue;

                var thisAddress = secondOffset == 0 ? offset : secondOffset;
                if (_breakpoints.ContainsKey(thisAddress))
                    _breakpoints.Remove(thisAddress);
            }

            _memoryBreakpoints[sourceId].Clear();
        }
        else
        {
            _memoryBreakpoints.Add(sourceId, new List<MemoryBreakpointMap>());
        }

        if (decompiledFile == null)
            return new SetBreakpointsResponse(new List<Breakpoint> { });

        foreach (var sourceBreakpoint in arguments.Breakpoints)
        {
            if (!decompiledFile.Items.ContainsKey(sourceBreakpoint.Line))
                continue;

            var thisLine = decompiledFile.Items[sourceBreakpoint.Line];

            var breakpoint = new Breakpoint();
            breakpoint.Source = decompiledFile.AsSource();
            breakpoint.Line = sourceBreakpoint.Line;
            breakpoint.Verified = true;

            var debuggerAddress = AddressFunctions.GetDebuggerAddress(thisLine.Address, decompiledFile.RamBank, decompiledFile.RomBank);
            var breakpointValue = _debuggerBreakpoints.Contains(debuggerAddress) ? (byte)0x81 : (byte)1;

            var toAdd = new MemoryBreakpointMap(thisLine.Address, decompiledFile.RamBank, decompiledFile.RomBank, breakpoint);
            _memoryBreakpoints[sourceId].Add(toAdd);

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
                _breakpoints.Add(thisAddress, sourceBreakpoint);
        }
        return new SetBreakpointsResponse(_memoryBreakpoints[sourceId].Select(i => i.Breakpoint).ToList());
    }

    /// <summary>
    /// Gets a breakpoint and the times its been hit this run. Important: Increments the hitcount.
    /// </summary>
    /// <param name="address"></param>
    /// <param name="ramBank"></param>
    /// <param name="romBank"></param>
    /// <returns></returns>
    public (SourceBreakpoint? BreakPoint, int HitCount, int BreakpointValue) GetCurrentBreakpoint(int address, int ramBank, int romBank)
    {
        var (_, secondAddress) = AddressFunctions.GetMemoryLocations(address >= 0xc000 ? romBank : ramBank, address);

        var thisAddress = secondAddress == 0 ? address : secondAddress;

        int hitCount;
        if (_breakpointHitCount.ContainsKey(thisAddress))
        {
            hitCount = _breakpointHitCount[thisAddress];
            hitCount++;
            _breakpointHitCount[thisAddress] = hitCount;
        }
        else
        {
            hitCount = 1;
            _breakpointHitCount.Add(thisAddress, hitCount);
        }

        if (_breakpoints.ContainsKey(thisAddress))
            return (_breakpoints[thisAddress], hitCount, _emulator.Breakpoints[address]);

        return (null, hitCount, _emulator.Breakpoints[address]);
    }

    public void Clear()
    {
        _bitMagicBreakpoints.Clear();
        _memoryBreakpoints.Clear();
        _breakpointHitCount.Clear();

        // yes this is awful.
        for (var i = 0; i < _emulator.Breakpoints.Length; i++)
        {
            _emulator.Breakpoints[i] = 0;
        }
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