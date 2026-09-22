using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BitMagic.X16Debugger.Tests.Variables;

// Exercises the "Kernal" scope's R0-R15 VariableMaps. Unlike VERA/VIA, these are plain zero-page
// memory (no register-decode indirection), so the setter just writes the two bytes directly - no
// resync story to verify.
[TestClass]
public class VariableManagerKernalScopeTests
{
    private Emulator _emulator = null!;
    private ScopeManager _scopeManager = null!;
    private VariableManager _variableManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();

        var idManager = new IdManager();
        var logger = new FakeEmulatorLogger();
        var debugableFileManager = new DebugableFileManager(idManager);
        var sourceMapManager = new SourceMapManager(_emulator, logger);
        _scopeManager = new ScopeManager(idManager);
        var disassemblerManager = new DisassemblerManager(sourceMapManager, _emulator, idManager);
        var stackManager = new StackManager(_emulator, idManager, sourceMapManager, disassemblerManager, debugableFileManager);
        var spriteManager = new SpriteManager(_emulator);
        var paletteManager = new PaletteManager(_emulator);
        var psgManager = new PsgManager(_emulator);

        _variableManager = new VariableManager(idManager, _emulator, _scopeManager, paletteManager, spriteManager, stackManager, psgManager);
    }

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    private VariableMap GetKernalVariable(string name) =>
        (VariableMap)_scopeManager.GetScope("Kernal", false).Variables.First(v => v.Name == name);

    private static void Set(IVariableItem variable, string name, string value) =>
        variable.SetVariable(new SetVariableArguments { Name = name, Value = value });

    [TestMethod]
    public void R0_SetVariable_WritesBothBytesLittleEndian()
    {
        var r0 = GetKernalVariable("R0");

        Set(r0, "R0", "0x1234");

        Assert.AreEqual((byte)0x34, _emulator.Memory[0x02]);
        Assert.AreEqual((byte)0x12, _emulator.Memory[0x03]);
    }

    [TestMethod]
    public void R7_SetVariable_WritesCorrectByteRange()
    {
        var r7 = GetKernalVariable("R7");

        Set(r7, "R7", "0xBEEF");

        Assert.AreEqual((byte)0xEF, _emulator.Memory[0x10]);
        Assert.AreEqual((byte)0xBE, _emulator.Memory[0x11]);
    }

    [TestMethod]
    public void R15_SetVariable_WritesCorrectByteRange()
    {
        var r15 = GetKernalVariable("R15");

        Set(r15, "R15", "1000");

        Assert.AreEqual((byte)(1000 & 0xff), _emulator.Memory[0x20]);
        Assert.AreEqual((byte)(1000 >> 8), _emulator.Memory[0x21]);
    }

    [TestMethod]
    public void R0_SetVariable_DoesNotDisturbAdjacentRegister()
    {
        var r0 = GetKernalVariable("R0");
        var r1 = GetKernalVariable("R1");
        Set(r1, "R1", "0xAAAA");

        Set(r0, "R0", "0x1234");

        Assert.AreEqual((byte)0xAA, _emulator.Memory[0x04]);
        Assert.AreEqual((byte)0xAA, _emulator.Memory[0x05]);
    }

    [TestMethod]
    public void R0_SetVariable_UnparsableText_LeavesRegisterUnchanged()
    {
        var r0 = GetKernalVariable("R0");
        _emulator.Memory[0x02] = 0x11;
        _emulator.Memory[0x03] = 0x22;

        Set(r0, "R0", "not-a-number");

        Assert.AreEqual((byte)0x11, _emulator.Memory[0x02]);
        Assert.AreEqual((byte)0x22, _emulator.Memory[0x03]);
    }
}
