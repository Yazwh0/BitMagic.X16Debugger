using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BitMagic.X16Debugger.Tests.Variables;

// Exercises the VIA scope's editable VariableMaps. Setters here write Emulator.Via.* (decoded
// state), same reasoning as the VERA scope: via_init (Via.asm) re-encodes A In/Out/Direction into
// PRA/ORA and the Timer1/2 counter+latch fields into memory, unconditionally on every emulator
// run/step - see the comment above the "VIA" scope in VariablesManager.cs. These tests only check
// the decoded side; they don't exercise via_init itself (needs a running emulator).
[TestClass]
public class VariableManagerViaScopeTests
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

    private VariableMap GetViaVariable(string name) =>
        (VariableMap)_scopeManager.GetScope("VIA", false).Variables.First(v => v.Name == name);

    private static void Set(IVariableItem variable, string name, string value) =>
        variable.SetVariable(new SetVariableArguments { Name = name, Value = value });

    [TestMethod]
    public void AInValue_SetVariable_WritesDecodedField()
    {
        var aIn = GetViaVariable("A In Value ");

        Set(aIn, "A In Value ", "0x5A");

        Assert.AreEqual((byte)0x5A, _emulator.Via.Register_A_InValue);
    }

    [TestMethod]
    public void AOutValue_SetVariable_WritesDecodedField()
    {
        var aOut = GetViaVariable("A Out Value");

        Set(aOut, "A Out Value", "0xA5");

        Assert.AreEqual((byte)0xA5, _emulator.Via.Register_A_OutValue);
    }

    [TestMethod]
    public void ADirection_SetVariable_WritesDecodedField()
    {
        var direction = GetViaVariable("A Direction");

        Set(direction, "A Direction", "0xFF");

        Assert.AreEqual((byte)0xFF, _emulator.Via.Register_A_Direction);
    }

    [TestMethod]
    public void AValue_HasNoSetter_StaysReadOnly()
    {
        var aValue = GetViaVariable("A Value    ");

        Assert.IsNull(aValue.SetValue);
        Assert.IsTrue(aValue.GetVariable().PresentationHint!.Attributes.GetValueOrDefault().HasFlag(VariablePresentationHint.AttributesValue.ReadOnly));
    }

    [TestMethod]
    public void Timer1_SetVariable_WritesDecodedCounter()
    {
        var timer1 = GetViaVariable("Timer1");

        Set(timer1, "Timer1", "1000");

        Assert.AreEqual((ushort)1000, _emulator.Via.Timer1_Counter);
    }

    [TestMethod]
    public void Timer1Latch_SetVariable_WritesDecodedLatch()
    {
        var latch = GetViaVariable("Timer1 Latch");

        Set(latch, "Timer1 Latch", "0x1234");

        Assert.AreEqual((ushort)0x1234, _emulator.Via.Timer1_Latch);
    }

    [TestMethod]
    public void Timer1Continuous_SetVariable_True_SetsFlag()
    {
        var continuous = GetViaVariable("Timer1 Continuous");
        _emulator.Via.Timer1_Continous = false;

        Set(continuous, "Timer1 Continuous", "True");

        Assert.IsTrue(_emulator.Via.Timer1_Continous);
    }

    [TestMethod]
    public void Timer1Running_SetVariable_False_ClearsFlag()
    {
        var running = GetViaVariable("Timer1 Running");
        _emulator.Via.Timer1_Running = true;

        Set(running, "Timer1 Running", "False");

        Assert.IsFalse(_emulator.Via.Timer1_Running);
    }

    [TestMethod]
    public void Timer1Pb7_SetVariable_True_SetsFlag()
    {
        var pb7 = GetViaVariable("Timer1 Pb7");
        _emulator.Via.Timer1_Pb7 = false;

        Set(pb7, "Timer1 Pb7", "True");

        Assert.IsTrue(_emulator.Via.Timer1_Pb7);
    }

    [TestMethod]
    public void Timer2_SetVariable_WritesDecodedCounter()
    {
        var timer2 = GetViaVariable("Timer2");

        Set(timer2, "Timer2", "0x0800");

        Assert.AreEqual((ushort)0x0800, _emulator.Via.Timer2_Counter);
    }

    [TestMethod]
    public void Timer2Latch_SetVariable_WritesDecodedLatch()
    {
        var latch = GetViaVariable("Timer2 Latch");

        Set(latch, "Timer2 Latch", "500");

        Assert.AreEqual((ushort)500, _emulator.Via.Timer2_Latch);
    }

    [TestMethod]
    public void Timer2Running_SetVariable_AcceptsNumericFallback()
    {
        var running = GetViaVariable("Timer2 Running");
        _emulator.Via.Timer2_Running = false;

        Set(running, "Timer2 Running", "1");

        Assert.IsTrue(_emulator.Via.Timer2_Running);
    }

    [TestMethod]
    public void Timer2PulseCount_SetVariable_True_SetsFlag()
    {
        var pulseCount = GetViaVariable("Timer2 Pulse Count");
        _emulator.Via.Timer2_PulseCount = false;

        Set(pulseCount, "Timer2 Pulse Count", "True");

        Assert.IsTrue(_emulator.Via.Timer2_PulseCount);
    }

    // ViaState.Interrupt_* now properly AND/OR the bit (fixed in X16Emulator.cs - it used to be
    // OR-only, unable to clear), so these are wired up like any other flag.
    [TestMethod]
    public void Timer1Interupt_SetVariable_False_ClearsFlag()
    {
        var interupt = GetViaVariable("Timer1 Interupt");
        _emulator.Via.Interrupt_Timer1 = true;

        Set(interupt, "Timer1 Interupt", "False");

        Assert.IsFalse(_emulator.Via.Interrupt_Timer1);
    }

    [TestMethod]
    public void InteruptCb1_SetVariable_True_SetsFlag()
    {
        var cb1 = GetViaVariable("Interupt Cb1");
        _emulator.Via.Interrupt_Cb1 = false;

        Set(cb1, "Interupt Cb1", "True");

        Assert.IsTrue(_emulator.Via.Interrupt_Cb1);
    }

    [TestMethod]
    public void Timer1_SetVariable_UnparsableText_LeavesCounterUnchanged()
    {
        var timer1 = GetViaVariable("Timer1");
        _emulator.Via.Timer1_Counter = 42;

        Set(timer1, "Timer1", "not-a-number");

        Assert.AreEqual((ushort)42, _emulator.Via.Timer1_Counter);
    }
}
