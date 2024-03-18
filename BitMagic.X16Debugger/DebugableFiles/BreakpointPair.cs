using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class BreakpointPair
{
    public Breakpoint Breakpoint { get; init; }
    public SourceBreakpoint SourceBreakpoint { get; init; }
    public int PrimaryAddress { get; set; }
    public int SecondaryAddress { get; set; }

    public BreakpointPair(Breakpoint breakpoint, SourceBreakpoint sourceBreakpoint, int primaryAddress, int secondaryAddress)
    {
        Breakpoint = breakpoint;
        SourceBreakpoint = sourceBreakpoint;
        PrimaryAddress = primaryAddress;
        SecondaryAddress = secondaryAddress;
    }

}
