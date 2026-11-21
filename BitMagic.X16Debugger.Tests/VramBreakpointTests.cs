using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static BitMagic.X16Emulator.Emulator.VeraState;

namespace BitMagic.X16Debugger.Tests;

// VRAM breakpoints, set as function breakpoints: vram(address), vram(address, "RW"), vram(start, end) and
// vram(start, end, "RW"). A range's end is exclusive.
[TestClass]
public class VramBreakpointTests
{
    private ServiceManager _serviceManager = null!;
    private Emulator Emulator => _serviceManager.Emulator;

    [TestInitialize]
    public void Setup()
    {
        _serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());
    }

    private Breakpoint Set(string name) =>
        _serviceManager.BreakpointManager.HandleFunctionBreakpointsRequest(new SetFunctionBreakpointsArguments
        {
            Breakpoints = [new FunctionBreakpoint { Name = name }]
        }).Breakpoints.Single();

    private VeraBreakpointType At(int address) => (VeraBreakpointType)Emulator.VramBreakpoints[address];

    [TestMethod]
    [DataRow("W")]
    [DataRow("w")]
    public void Write_EitherCase(string type)
    {
        var breakpoint = Set($"vram(0x1558, \"{type}\")");

        Assert.IsTrue(breakpoint.Verified, breakpoint.Message);
        Assert.AreEqual(VeraBreakpointType.Write, At(0x1558));
        Assert.AreEqual((VeraBreakpointType)0, At(0x1557));
        Assert.AreEqual((VeraBreakpointType)0, At(0x1559));
    }

    [TestMethod]
    public void NoType_ReadAndWrite()
    {
        var breakpoint = Set("vram(0x1558)");

        Assert.IsTrue(breakpoint.Verified, breakpoint.Message);
        Assert.AreEqual(VeraBreakpointType.Read | VeraBreakpointType.Write, At(0x1558));
    }

    [TestMethod]
    public void NeitherReadNorWrite_NotVerified()
    {
        var breakpoint = Set("vram(0x1558, \"x\")");

        Assert.IsFalse(breakpoint.Verified);
        Assert.AreEqual((VeraBreakpointType)0, At(0x1558));
    }

    [TestMethod]
    [DataRow("vram(0x1540, 0x1560, \"W\")")]
    [DataRow("vram(0x1560, 0x1540, \"W\")")]
    public void Range_EndExclusive(string name)
    {
        var breakpoint = Set(name);

        Assert.IsTrue(breakpoint.Verified, breakpoint.Message);
        StringAssert.Contains(breakpoint.Message, "32 bytes");
        Assert.AreEqual((VeraBreakpointType)0, At(0x153f));
        Assert.AreEqual(VeraBreakpointType.Write, At(0x1540));
        Assert.AreEqual(VeraBreakpointType.Write, At(0x155f));
        Assert.AreEqual((VeraBreakpointType)0, At(0x1560));
    }

    [TestMethod]
    public void OutsideVram_NotVerified()
    {
        var breakpoint = Set("vram(0x20000, \"W\")");

        Assert.IsFalse(breakpoint.Verified);
    }
}
