using BitMagic.Common.Address;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

// Breakpoints set in the disassembly view. Each setInstructionBreakpoints request holds the full set, so it replaces
// whatever was set before, in every disassembly.
[TestClass]
public class InstructionBreakpointTests
{
    private ServiceManager _serviceManager = null!;
    private Emulator Emulator => _serviceManager.Emulator;

    [TestInitialize]
    public void Setup()
    {
        _serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());

        // nops from $0810, so each byte is an instruction
        for (var i = 0; i < 16; i++)
            Emulator.Memory[0x810 + i] = 0xea;
    }

    private SetInstructionBreakpointsResponse Set(params (int Address, int RamBank)[] breakpoints) =>
        _serviceManager.BreakpointManager.HandleSetInstructionBreakpointsRequest(new SetInstructionBreakpointsArguments
        {
            Breakpoints = breakpoints.Select(i => new InstructionBreakpoint
            {
                InstructionReference = AddressFunctions.GetDebuggerAddressString(i.Address, i.RamBank, 0)
            }).ToList()
        });

    private bool IsSet(int address, int ramBank = 0)
    {
        var (low, banked) = AddressFunctions.GetMemoryLocations(ramBank, address);
        return Emulator.Breakpoints[banked == 0 ? low : banked] != 0;
    }

    [TestMethod]
    public void TwoInOneDisassembly_BothSet()
    {
        var response = Set((0x810, 0), (0x812, 0));

        Assert.IsTrue(response.Breakpoints.All(i => i.Verified), string.Join(", ", response.Breakpoints.Select(i => i.Message)));
        Assert.IsTrue(IsSet(0x810), "The first shouldn't be cleared by the second.");
        Assert.IsTrue(IsSet(0x812));
    }

    [TestMethod]
    public void RemovingOne_KeepsTheOther()
    {
        Set((0x810, 0), (0x812, 0));
        Set((0x812, 0));

        Assert.IsFalse(IsSet(0x810));
        Assert.IsTrue(IsSet(0x812));
    }

    [TestMethod]
    public void RemovingTheLast_ClearsIt()
    {
        Set((0x810, 0));
        Set();

        Assert.IsFalse(IsSet(0x810));
    }

    // main RAM and RAM bank 1 are separate disassemblies
    [TestMethod]
    public void RemovingOneInAnotherDisassembly_ClearsIt()
    {
        var response = Set((0x810, 0), (0xa000, 1));
        Assert.IsTrue(response.Breakpoints.All(i => i.Verified), string.Join(", ", response.Breakpoints.Select(i => i.Message)));
        Assert.IsTrue(IsSet(0xa000, 1));

        Set((0x810, 0));

        Assert.IsTrue(IsSet(0x810));
        Assert.IsFalse(IsSet(0xa000, 1));
    }
}
