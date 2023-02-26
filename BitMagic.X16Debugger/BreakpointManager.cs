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

    internal BreakpointManager(Emulator emulator, X16Debug debugger)
    {
        _emulator = emulator;
        _debugger = debugger;
    }

    public SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        if (Breakpoints.ContainsKey(arguments.Source.Path))
        {
            foreach(var breakpoint in Breakpoints[arguments.Source.Path].Where(i => i.Breakpoint.Verified))
            {
                // todo: add bank handling
                var (address, secondAddress) = GetBreakpointLocation(0, breakpoint.Source.Address);

                _emulator.Breakpoints[address] = 0;
                if (secondAddress != 0)
                    _emulator.Breakpoints[secondAddress] = 0;
            }

            Breakpoints[arguments.Source.Path].Clear();
        }
        else
        {
            Breakpoints.Add(arguments.Source.Path, new List<BreakpointMap>());
        }

        foreach (var sourceBreakpoint in arguments.Breakpoints)
        {
            var filemap = _debugger.SourceToMemoryMap.FirstOrDefault(i => String.Equals(arguments.Source.Path, i.Key, StringComparison.InvariantCultureIgnoreCase));
            var source = filemap.Value.FirstOrDefault(i => i.LineNumber == sourceBreakpoint.Line);

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

    //returns the location in the break point array for a given bank\address
    //second value is returned if the address is currently the active bank
    // todo: add second address handling
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
            return (address, 0);
        }

        // rom bank
        return (address, 0);
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