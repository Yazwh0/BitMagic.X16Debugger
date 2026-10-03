using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CompilerVariables = BitMagic.Compiler.Variables;

namespace BitMagic.X16Debugger.Tests.Scopes;

// Locals leave out the top level (global) variables when inside a procedure, and Globals group large numbers of
// variables by segment. Both apply to any CompileState, BitMagic or cc65.
[TestClass]
public class VariableScopeTests
{
    private Emulator _emulator = null!;
    private IdManager _idManager = null!;
    private ScopeManager _scopeManager = null!;
    private VariableManager _variableManager = null!;
    private ExpressionManager _expressionManager = null!;
    private DebugableFileManager _debugableFileManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();
        _idManager = new IdManager();
        var logger = new FakeEmulatorLogger();
        _debugableFileManager = new DebugableFileManager(_idManager);
        var sourceMapManager = new SourceMapManager(_emulator, logger);
        var disassemblerManager = new DisassemblerManager(sourceMapManager, _emulator, _idManager);
        var stackManager = new StackManager(_emulator, _idManager, sourceMapManager, disassemblerManager, _debugableFileManager);
        _scopeManager = new ScopeManager(_idManager);
        _variableManager = new VariableManager(_idManager, _emulator, _scopeManager, new PaletteManager(_emulator), new SpriteManager(_emulator), stackManager, new PsgManager(_emulator));
        _expressionManager = new ExpressionManager(_variableManager, _emulator);
        _variableManager.SetExpressionManager(_expressionManager);
        _debugableFileManager.SetBreakpointManager(new BreakpointManager(_emulator, _idManager, disassemblerManager, _debugableFileManager));
    }

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    private string[] GetLocals(IScope scope)
    {
        var locals = new DebuggerLocalVariables("Locals", 1);
        locals.SetLocalScope(new StackFrameState(new StackFrame()) { Scope = scope }, _emulator, _expressionManager, _variableManager);
        return locals.Variables.Select(i => i.Name).Order().ToArray();
    }

    // Shaped like a BitMagic compile: a top level variable, a .proc, and a .segment whose default procedure shares
    // the top level variables.
    private static (CompileState State, Procedure Proc, Procedure SegmentProc) CreateBitMagicState()
    {
        var state = new CompileState(new CompilerVariables("App"), "test");
        state.Procedure.Variables.SetValue("global_byte", 0x900, VariableDataType.Byte, false);

        var proc = state.Procedure.GetProcedure("work", 0x810);
        proc.Variables.SetValue("local_byte", 0x901, VariableDataType.Byte, false);

        var segmentDefault = new Procedure(state.ScopeFactory.GetScope("Main"), "Segment_Other_Main_Default", true, state.Procedure);
        var segmentProc = segmentDefault.GetProcedure("other", 0xa000);
        segmentProc.Variables.SetValue("other_byte", 0x902, VariableDataType.Byte, false);

        return (state, proc, segmentProc);
    }

    [TestMethod]
    public void Locals_InProcedure_ExcludeGlobals()
    {
        var (_, proc, _) = CreateBitMagicState();

        CollectionAssert.AreEqual(new[] { "local_byte" }, GetLocals(proc));
    }

    [TestMethod]
    public void Locals_InProcedureInSegment_ExcludeGlobals()
    {
        var (_, _, segmentProc) = CreateBitMagicState();

        CollectionAssert.AreEqual(new[] { "other_byte" }, GetLocals(segmentProc));
    }

    [TestMethod]
    public void Locals_AtTopLevel_ShowGlobals()
    {
        var (state, _, _) = CreateBitMagicState();

        CollectionAssert.Contains(GetLocals(state.Procedure), "global_byte");
    }

    [TestMethod]
    public void Locals_Cc65NestedScope_IncludesEnclosingScopes()
    {
        var state = new CompileState(new CompilerVariables("App"), "test");
        state.Procedure.Variables.SetValue("global_byte", 0x900, VariableDataType.Byte, false);
        var outer = new Procedure("sound", state.Procedure);
        outer.Variables.SetValue("outer_byte", 0x901, VariableDataType.Byte, false);
        var inner = new Procedure("tick", outer);
        inner.Variables.SetValue("inner_byte", 0x902, VariableDataType.Byte, false);

        CollectionAssert.AreEqual(new[] { "inner_byte", "outer_byte" }, GetLocals(inner));
    }

    private IVariableItem[] GetGlobals(CompileState state)
    {
        _debugableFileManager.AddFiles(new Cc65BinaryFile("TEST.PRG", 0x801, 1) { State = state });
        _variableManager.RebuildGlobals(_debugableFileManager);

        var file = (VariableChildren)_scopeManager.GetScope("Globals", false).Variables.Single();
        var main = (VariableChildren)file.Children.Single();
        return main.Children.ToArray();
    }

    private static void AddVariables(CompileState state, string prefix, int count, string? segment, int address)
    {
        for (var i = 0; i < count; i++)
        {
            state.Procedure.Variables.SetValue($"{prefix}{i}", address + i, VariableDataType.Byte, false);
            ((AsmVariable)state.Procedure.Variables.Values[$"{prefix}{i}"]).Segment = segment;
        }
    }

    [TestMethod]
    public void Globals_ManyVariables_GroupedBySegment()
    {
        var state = new CompileState(new CompilerVariables("App"), "test");
        AddVariables(state, "zp", 10, "ZEROPAGE", 0x22);
        AddVariables(state, "bank", 10, "BANKMISC", 0xa000);
        AddVariables(state, "other", 2, null, 0x900);

        var globals = GetGlobals(state);

        CollectionAssert.AreEquivalent(new[] { "ZEROPAGE", "BANKMISC", "other0", "other1" }, globals.Select(i => i.Name).ToArray());
        Assert.AreEqual(10, ((VariableChildren)globals.Single(i => i.Name == "ZEROPAGE")).Children.Count());
    }

    [TestMethod]
    public void Globals_FewVariables_NotGrouped()
    {
        var state = new CompileState(new CompilerVariables("App"), "test");
        AddVariables(state, "zp", 3, "ZEROPAGE", 0x22);
        AddVariables(state, "bank", 3, "BANKMISC", 0xa000);

        Assert.AreEqual(6, GetGlobals(state).Length);
    }

    [TestMethod]
    public void Globals_NoSegments_NotGrouped()
    {
        // BitMagic variables have no segment, so are never grouped.
        var state = new CompileState(new CompilerVariables("App"), "test");
        AddVariables(state, "var", 30, null, 0x900);

        Assert.AreEqual(30, GetGlobals(state).Length);
    }

    [TestMethod]
    public void Globals_StillResolveByName()
    {
        var state = new CompileState(new CompilerVariables("App"), "test");
        AddVariables(state, "zp", 10, "ZEROPAGE", 0x22);
        AddVariables(state, "bank", 10, "BANKMISC", 0xa000);
        GetGlobals(state);
        _expressionManager.AddState(state);

        _emulator.Memory[0x23] = 0x42;

        Assert.AreEqual("0x42", _expressionManager.Evaluate("zp1"));
    }
}
