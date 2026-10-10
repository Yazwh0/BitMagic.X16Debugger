using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Files;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Scopes;

// The debugger sees everything: private names, labels, operand labels and .export aliases, and
// .debugalias expressions can use exports.
[TestClass]
public class VisibilityTests
{
    private Emulator _emulator = null!;
    private VariableManager _variableManager = null!;
    private ExpressionManager _expressionManager = null!;
    private CompileResult _result = null!;

    private const string Code = @"
            .machine CommanderX16R40
            .org $810
            stp
        .proc private feed
            .padvar private byte level
            lda sample: $1234
        .loop:
            rts
        .endproc
        .export mix_sample feed:sample
        .debugalias ushort current mix_sample";

    [TestInitialize]
    public async Task Setup()
    {
        _emulator = new Emulator();
        var idManager = new IdManager();
        var logger = new FakeEmulatorLogger();
        var debugableFileManager = new DebugableFileManager(idManager);
        var sourceMapManager = new SourceMapManager(_emulator, logger);
        var disassemblerManager = new DisassemblerManager(sourceMapManager, _emulator, idManager);
        var stackManager = new StackManager(_emulator, idManager, sourceMapManager, disassemblerManager, debugableFileManager);
        var scopeManager = new ScopeManager(idManager);
        _variableManager = new VariableManager(idManager, _emulator, scopeManager, new PaletteManager(_emulator), new SpriteManager(_emulator), stackManager, new PsgManager(_emulator));
        _expressionManager = new ExpressionManager(_variableManager, _emulator);
        _variableManager.SetExpressionManager(_expressionManager);

        var project = new Project { Code = new StaticTextFile(Code, "visibility.bmasm") };
        _result = await new Compiler.Compiler(project, new(), logger).Compile();
        _expressionManager.AddState(_result.State);
    }

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    private IAsmVariable Find(string fullName) =>
        _result.State.Globals.GetChildVariables("App").First(i => i.Name == fullName).Value;

    [TestMethod]
    public void Evaluate_PrivateVariable()
    {
        _emulator.Memory[Find("App:Main:feed:level").Value] = 0x42;

        Assert.AreEqual("0x42", _expressionManager.Evaluate("feed:level"));
    }

    [TestMethod]
    public void Evaluate_OperandLabel_AndItsExport()
    {
        var sample = Find("App:Main:feed:sample").Value;
        _emulator.Memory[sample] = 0x34;
        _emulator.Memory[sample + 1] = 0x12;

        Assert.AreEqual("0x1234", _expressionManager.Evaluate("feed:sample"));
        Assert.AreEqual("0x1234", _expressionManager.Evaluate("mix_sample"));
    }

    [TestMethod]
    public void Evaluate_Label()
    {
        var loop = Find("App:Main:feed:loop").Value;

        StringAssert.Contains(_expressionManager.Evaluate("feed:loop").ToLower(), loop.ToString("x"));
    }

    [TestMethod]
    public void Evaluate_DebugAliasUsingAnExport()
    {
        var sample = Find("App:Main:feed:sample").Value;
        _emulator.Memory[sample] = 0x78;
        _emulator.Memory[sample + 1] = 0x56;

        Assert.AreEqual("0x5678", _expressionManager.Evaluate("current"));
    }

    [TestMethod]
    public void Export_ShowsWhatItStandsFor()
    {
        var memory = new MemoryWrapper(() => _emulator.Memory.ToArray());
        var item = DebuggerLocalVariables.GetVariable("mix_sample", Find("App:Main:mix_sample"), _expressionManager, memory, _emulator, _variableManager);

        Assert.IsNotNull(item);
        StringAssert.Contains(item.GetVariable().Type, "→ App:Main:feed:sample");
    }

    [TestMethod]
    public void Evaluate_DoesNotAddCompilerWarnings()
    {
        var before = _result.State.Warnings.Count;

        _expressionManager.Evaluate("feed:loop");
        _expressionManager.Evaluate("feed:sample");

        Assert.AreEqual(before, _result.State.Warnings.Count);
    }
}
