using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal class BreakpointManager
{
    public Dictionary<string, List<BreakpointMap>> Breakpoints = new Dictionary<string, List<BreakpointMap>>();
    private readonly Emulator _emulator;
    private readonly X16Debug _debugger;
    private readonly SourceMapManager _sourceMapManager;

    internal BreakpointManager(Emulator emulator, X16Debug debugger, SourceMapManager sourceMapManager)
    {
        _emulator = emulator;
        _debugger = debugger;
        _sourceMapManager = sourceMapManager;
    }

    public SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        // Clear breakpoints
        if (Breakpoints.ContainsKey(arguments.Source.Path))
        {
            foreach(var breakpoint in Breakpoints[arguments.Source.Path].Where(i => i.Breakpoint.Verified))
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

            Breakpoints[arguments.Source.Path].Clear();
        }
        else
        {
            Breakpoints.Add(arguments.Source.Path, new List<BreakpointMap>());
        }

        // Add breakpoints
        foreach (var sourceBreakpoint in arguments.Breakpoints)
        {
            var filemap = _sourceMapManager.GetSourceFileMap(arguments.Source.Path);
            if (filemap == null) // we dont recognise the file
                continue;

            var source = filemap.FirstOrDefault(i => i.LineNumber == sourceBreakpoint.Line);

            var breakpoint = new Breakpoint();

            breakpoint.Source = arguments.Source;
            breakpoint.Line = sourceBreakpoint.Line;
            breakpoint.Verified = source != null;

            var toAdd = new BreakpointMap(breakpoint, source?.Line);

            Breakpoints[toAdd.Breakpoint.Source.Path].Add(toAdd);

            if (source != null)
            {
                var (address, secondAddress) = GetBreakpointLocation(source.Bank, source.Address);

                _emulator.Breakpoints[address] = 1;
                if (secondAddress != 0)
                    _emulator.Breakpoints[secondAddress] = 1;
            }
        }

        return new SetBreakpointsResponse(Breakpoints.Values.SelectMany(i => i).Select(i => i.Breakpoint).ToList());
    }

    public void Clear()
    {
        Breakpoints.Clear();

        // yes this is awful.
        for(var i = 0; i < _emulator.Breakpoints.Length; i++) {
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

internal class BreakpointMap
{
    public Breakpoint Breakpoint { get; }
    public IOutputData? Source { get; set; }

    internal BreakpointMap(Breakpoint breakpoint, IOutputData? source)
    {
        Breakpoint = breakpoint;
        Source = source;
    }
}