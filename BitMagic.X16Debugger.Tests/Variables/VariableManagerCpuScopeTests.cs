using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BitMagic.X16Debugger.Tests.Variables;

// Exercises the CPU scope's register/flag VariableMaps the way X16Debug.HandleSetVariableRequest
// does: look the entry up by name in the "CPU" scope, then GetVariable()/SetVariable() it -
// wiring a full VariableManager (mirroring ServiceManager.Reset()) rather than reaching into
// SetupVariables() directly, since the setters close over the manager's own _emulator field.
[TestClass]
public class VariableManagerCpuScopeTests
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

    private VariableMap GetCpuVariable(string name) =>
        (VariableMap)_scopeManager.GetScope("CPU", false).Variables.First(v => v.Name == name);

    private VariableMap GetFlagVariable(string name) =>
        (VariableMap)((VariableChildren)_scopeManager.GetScope("CPU", false).Variables.First(v => v.Name == "Flags")).Children.First(v => v.Name == name);

    [TestMethod]
    public void A_HasSetter_AndIsNotReadOnly()
    {
        var a = GetCpuVariable("A");

        Assert.IsNotNull(a.SetValue);
        Assert.IsFalse(a.GetVariable().PresentationHint!.Attributes.GetValueOrDefault().HasFlag(VariablePresentationHint.AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void A_SetVariable_FromDecimalText_WritesEmulatorRegister()
    {
        var a = GetCpuVariable("A");

        a.SetVariable(new SetVariableArguments { Name = "A", Value = "200" });

        Assert.AreEqual((byte)200, _emulator.A);
        Assert.AreEqual("0xC8", a.GetVariable().Value);
    }

    [TestMethod]
    public void X_SetVariable_From0xHexText_WritesEmulatorRegister()
    {
        var x = GetCpuVariable("X");

        x.SetVariable(new SetVariableArguments { Name = "X", Value = "0xFF" });

        Assert.AreEqual((byte)0xFF, _emulator.X);
    }

    [TestMethod]
    public void Y_SetVariable_FromDollarHexText_WritesEmulatorRegister()
    {
        var y = GetCpuVariable("Y");

        y.SetVariable(new SetVariableArguments { Name = "Y", Value = "$2A" });

        Assert.AreEqual((byte)0x2A, _emulator.Y);
    }

    [TestMethod]
    public void PC_SetVariable_WritesFullUshortValue()
    {
        var pc = GetCpuVariable("PC");

        pc.SetVariable(new SetVariableArguments { Name = "PC", Value = "0x0810" });

        Assert.AreEqual((ushort)0x0810, _emulator.Pc);
    }

    [TestMethod]
    public void SP_SetVariable_WritesFullStackAddress()
    {
        var sp = GetCpuVariable("SP");

        sp.SetVariable(new SetVariableArguments { Name = "SP", Value = "0x1F0" });

        Assert.AreEqual((ushort)0x1F0, _emulator.StackPointer);
    }

    [TestMethod]
    public void A_SetVariable_UnparsableText_LeavesRegisterUnchanged()
    {
        var a = GetCpuVariable("A");
        _emulator.A = 5;

        a.SetVariable(new SetVariableArguments { Name = "A", Value = "not-a-number" });

        Assert.AreEqual((byte)5, _emulator.A);
    }

    [TestMethod]
    public void Negative_SetVariable_True_SetsFlag()
    {
        var negative = GetFlagVariable("Negative");
        _emulator.Negative = false;

        negative.SetVariable(new SetVariableArguments { Name = "Negative", Value = "True" });

        Assert.IsTrue(_emulator.Negative);
    }

    [TestMethod]
    public void Carry_SetVariable_False_ClearsFlag()
    {
        var carry = GetFlagVariable("Carry");
        _emulator.Carry = true;

        carry.SetVariable(new SetVariableArguments { Name = "Carry", Value = "False" });

        Assert.IsFalse(_emulator.Carry);
    }

    [TestMethod]
    public void Zero_SetVariable_AcceptsNumericFallback()
    {
        var zero = GetFlagVariable("Zero");
        _emulator.Zero = false;

        zero.SetVariable(new SetVariableArguments { Name = "Zero", Value = "1" });

        Assert.IsTrue(_emulator.Zero);
    }

    [TestMethod]
    public void RamBank_SetVariable_WritesTheRawRegisterByte()
    {
        var ramBank = GetCpuVariable("Ram Bank");

        ramBank.SetVariable(new SetVariableArguments { Name = "Ram Bank", Value = "12" });

        Assert.AreEqual((byte)12, _emulator.Memory[0]);
    }

    [TestMethod]
    public void RomBankMemory_SetVariable_WritesTheRawRegisterByte()
    {
        var romBank = GetCpuVariable("Rom Bank (Memory)");

        romBank.SetVariable(new SetVariableArguments { Name = "Rom Bank (Memory)", Value = "0x07" });

        Assert.AreEqual((byte)0x07, _emulator.Memory[1]);
    }

    [TestMethod]
    public void RomBankAct_HasNoSetter_StaysReadOnly()
    {
        var romBankAct = GetCpuVariable("Rom Bank (Act)");

        Assert.IsNull(romBankAct.SetValue);
        Assert.IsTrue(romBankAct.GetVariable().PresentationHint!.Attributes.GetValueOrDefault().HasFlag(VariablePresentationHint.AttributesValue.ReadOnly));
    }
}
