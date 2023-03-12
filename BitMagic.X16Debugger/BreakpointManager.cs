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
    public Dictionary<string, List<BitMagicBreakpointMap>> BitMagicBreakpoints = new Dictionary<string, List<BitMagicBreakpointMap>>();
    public Dictionary<int, List<MemoryBreakpointMap>> MemoryBreakpoints = new Dictionary<int, List<MemoryBreakpointMap>>();
    private readonly Emulator _emulator;
    private readonly X16Debug _debugger;
    private readonly SourceMapManager _sourceMapManager;
    private readonly IdManager _idManager;

    internal BreakpointManager(Emulator emulator, X16Debug debugger, SourceMapManager sourceMapManager, IdManager idManager)
    {
        _emulator = emulator;
        _debugger = debugger;
        _sourceMapManager = sourceMapManager;
        _idManager = idManager;
    }

    public SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        // There are two types of breakpoint, those on BitMagic code, and those on Rom\Ram. They have to be handled slightly differently.

        // Clear breakpoints
        if ((arguments.Source.SourceReference ?? 0) == 0)
        {
            if (BitMagicBreakpoints.ContainsKey(arguments.Source.Path))
            {
                foreach (var breakpoint in BitMagicBreakpoints[arguments.Source.Path].Where(i => i.Breakpoint.Verified))
                {
                    if (breakpoint.Source == null)
                        continue;

                    // todo: add bank handling
                    var (address, ramBank, romBank) = AddressFunctions.GetMachineAddress(breakpoint.Source.Address);
                    var (offset, secondOffset) = GetBreakpointLocation(ramBank > 0 ? ramBank : romBank, address);

                    _emulator.Breakpoints[offset] = 0;
                    if (secondOffset != 0)
                        _emulator.Breakpoints[secondOffset] = 0;
                }

                BitMagicBreakpoints[arguments.Source.Path].Clear();
            }
            else
            {
                BitMagicBreakpoints.Add(arguments.Source.Path, new List<BitMagicBreakpointMap>());
            }

            var biitMagicBreakpoints = BitMagicBreakpoints[arguments.Source.Path];
            // Add breakpoints
            foreach (var sourceBreakpoint in arguments.Breakpoints)
            {
                var filemap = _sourceMapManager.GetSourceFileMap(arguments.Source.Path);
                if (filemap == null) // we dont recognise the file
                    continue;

                var source = filemap.FirstOrDefault(i => i.LineNumber == sourceBreakpoint.Line);

                if (source == null) // dont recognise the line
                    continue;

                var breakpoint = new Breakpoint();
                breakpoint.Source = arguments.Source;
                breakpoint.Line = sourceBreakpoint.Line;
                breakpoint.Verified = source != null;

                var toAdd = new BitMagicBreakpointMap(breakpoint, source!.Line);

                biitMagicBreakpoints.Add(toAdd);

                var (address, secondAddress) = GetBreakpointLocation(source!.Bank, source.Address);

                _emulator.Breakpoints[address] = 1;
                if (secondAddress != 0)
                    _emulator.Breakpoints[secondAddress] = 1;
            }

            return new SetBreakpointsResponse(biitMagicBreakpoints.Select(i => i.Breakpoint).ToList());
        }

        // this isn't a BitMagic breakpoint, so set on the decompiled memory source.
        var sourceId = arguments.Source.SourceReference ?? 0;
        if (MemoryBreakpoints.ContainsKey(sourceId))
        {
            foreach (var breakpoint in MemoryBreakpoints[sourceId])
            {
                var (offset, secondOffset) = GetBreakpointLocation(breakpoint.RamBank > 0 ? breakpoint.RamBank : breakpoint.RomBank, breakpoint.Address);

                _emulator.Breakpoints[offset] = 0;
                if (secondOffset != 0)
                    _emulator.Breakpoints[secondOffset] = 0;
            }

            MemoryBreakpoints[sourceId].Clear();
        }
        else
        {
            MemoryBreakpoints.Add(sourceId, new List<MemoryBreakpointMap>());
        }

        var decompiledFile = _idManager.GetObject<DecompileReturn>(sourceId);
        if (decompiledFile == null)
            return new SetBreakpointsResponse(new List<Breakpoint> { });

        foreach (var sourceBreakpoint in arguments.Breakpoints)
        {
            if (!decompiledFile.Items.ContainsKey(sourceBreakpoint.Line))
                continue;

            var thisLine = decompiledFile.Items[sourceBreakpoint.Line];

            var breakpoint = new Breakpoint();
            breakpoint.Source = arguments.Source;
            breakpoint.Line = sourceBreakpoint.Line;
            breakpoint.Verified = true;

            var toAdd = new MemoryBreakpointMap(thisLine.Address, decompiledFile.RamBank, decompiledFile.RomBank, breakpoint);
            MemoryBreakpoints[sourceId].Add(toAdd);

            var bank = thisLine.Address >= 0xc000 ? decompiledFile.RomBank : decompiledFile.RamBank;
            var (address, secondAddress) = GetBreakpointLocation(bank, thisLine.Address);

            _emulator.Breakpoints[address] = 1;
            if (secondAddress != 0)
                _emulator.Breakpoints[secondAddress] = 1;
        }
        return new SetBreakpointsResponse(MemoryBreakpoints[sourceId].Select(i => i.Breakpoint).ToList());
    }

    public void Clear()
    {
        BitMagicBreakpoints.Clear();
        MemoryBreakpoints.Clear();

        // yes this is awful.
        for (var i = 0; i < _emulator.Breakpoints.Length; i++)
        {
            _emulator.Breakpoints[i] = 0;
        }
    }

    // returns the location in the break point array for a given bank\address
    // second value is returned if the address is currently the active bank
    // breakpoint array:
    // Start      End (-1)     0x:-
    //       0 =>   10,000   : active memory
    //  10,000 =>  110,000   : ram banks
    // 110,000 =>  310,000   : rom banks
    private (int address, int secondAddress) GetBreakpointLocation(int bank, int address)
    {
        // normal ram
        if (address < 0xa000)
        {
            return (address, 0);
        }

        // ram bank
        if (address < 0xc000)
        {
            return (address, bank * 0x2000 + address - 0xa000);
        }

        // rom bank
        return (address, bank * 0x4000 + address - 0xc000);
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