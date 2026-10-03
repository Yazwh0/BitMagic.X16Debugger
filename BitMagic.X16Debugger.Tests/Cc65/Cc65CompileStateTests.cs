using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Cc65;

// Symbols from TestData/src/second.asm, see Cc65BinaryFileTests for how it is built.
[TestClass]
public class Cc65CompileStateTests
{
    private static string TestData => Path.Combine(AppContext.BaseDirectory, "Cc65", "TestData");

    private static (List<Cc65BinaryFile> Files, CompileState State) Build() =>
        Cc65BinaryFileFactory.Build(new Cc65InputFile
        {
            Type = "cc65",
            DebugFile = "build.dbg",
            Outputs =
            [
                new Cc65InputFileOutput { Filename = "BUILD.PRG", HasHeader = true },
                new Cc65InputFileOutput { Filename = "DAT/*", HasHeader = false }
            ]
        }, TestData, new FakeEmulatorLogger());

    private static IAsmVariable Get(CompileState state, string name)
    {
        Assert.IsTrue(state.Procedure.Variables.TryGetValue(name, new SourceFilePosition(), out var result), $"'{name}' not found.");
        return result!;
    }

    [TestMethod]
    public void Variables_TypedBySize()
    {
        var (_, state) = Build();

        var counter = Get(state, "counter"); // BSS .res 1
        Assert.AreEqual(0x83d, counter.Value);
        Assert.AreEqual(VariableDataType.Byte, counter.VariableDataType);
        Assert.IsFalse(counter.Array);

        var buffer = Get(state, "buffer"); // BSS .res 16
        Assert.AreEqual(VariableDataType.Byte, buffer.VariableDataType);
        Assert.IsTrue(buffer.Array);
        Assert.AreEqual(16, buffer.Length);

        var pointer = Get(state, "zp_pointer"); // ZEROPAGE .res 2
        Assert.AreEqual(0x22, pointer.Value);
        Assert.AreEqual(VariableDataType.Ushort, pointer.VariableDataType);
    }

    [TestMethod]
    public void DataLabel_IsVariable()
    {
        var (_, state) = Build();

        var table = Get(state, "table"); // RODATA .byte 1, 2, 3, 4
        Assert.AreEqual(VariableDataType.Byte, table.VariableDataType);
        Assert.IsTrue(table.Array);
        Assert.AreEqual(4, table.Length);

        // over several lines so ld65 gives no size, it runs to the next label.
        var words = Get(state, "words");
        Assert.AreEqual(VariableDataType.Byte, words.VariableDataType);
        Assert.IsTrue(words.Array);
        Assert.AreEqual(6, words.Length);
    }

    [TestMethod]
    public void CodeLabelsAndConstants_AreNotVariables()
    {
        var (_, state) = Build();

        Assert.AreEqual(VariableDataType.LabelPointer, Get(state, "second").VariableDataType);
        Assert.AreEqual(0x822, Get(state, "second").Value);

        var max = Get(state, "MAX_COUNT");
        Assert.AreEqual(VariableDataType.Constant, max.VariableDataType);
        Assert.AreEqual(10, max.Value);
    }

    [TestMethod]
    public void RepeatedLabel_ResolvesPerScope()
    {
        var (_, state) = Build();

        // 'loop' is in main.asm's module scope and in second.asm's 'work' scope, each is unique in its own scope.
        Assert.AreEqual(0x81e, state.Procedure.Variables.Values["loop"].Value);
    }

    [TestMethod]
    public void ScopeBecomesProcedure()
    {
        var (_, state) = Build();

        var work = state.ScopeFactory.GlobalVariables.Children.SelectMany(AllVariables).Single(i => i.Namespace == "work");

        Assert.AreEqual(VariableDataType.Byte, work.Values["local_count"].VariableDataType);
        Assert.AreEqual(0x82e, work.Values["loop"].Value);
    }

    [TestMethod]
    public void BytesInScope_HaveProcedure()
    {
        var (files, state) = Build();
        var prg = files.Single(i => i.Name == "BUILD.PRG");

        using var emulator = new Emulator();
        var sourceMapManager = new SourceMapManager(emulator, new FakeEmulatorLogger());
        prg.LoadDebugData(emulator, sourceMapManager, prg.BaseAddress);

        Assert.AreEqual("work", sourceMapManager.GetSourceMap(0x825)!.Scope.Name); // lda table
        Assert.AreNotEqual("work", sourceMapManager.GetSourceMap(0x822)!.Scope.Name); // ldx #$42, before the proc
        Assert.AreSame(state, prg.State);
    }

    [TestMethod]
    public void ExpressionManager_SearchesAllStates()
    {
        var (_, state) = Build();

        var other = new CompileState(new BitMagic.Compiler.Variables("App"), "other");
        other.Procedure.Variables.SetValue("other_value", 0x900, VariableDataType.Byte, false);

        using var emulator = new Emulator();
        var idManager = new IdManager();
        var sourceMapManager = new SourceMapManager(emulator, new FakeEmulatorLogger());
        var disassemblerManager = new DisassemblerManager(sourceMapManager, emulator, idManager);
        var stackManager = new StackManager(emulator, idManager, sourceMapManager, disassemblerManager, new DebugableFileManager(idManager));
        var variableManager = new BitMagic.X16Debugger.Variables.VariableManager(idManager, emulator, new ScopeManager(idManager), new PaletteManager(emulator), new SpriteManager(emulator), stackManager, new PsgManager(emulator));
        var expressionManager = new ExpressionManager(variableManager, emulator);

        expressionManager.AddState(state);
        expressionManager.AddState(other);

        emulator.Memory[0x83d] = 0x5a;
        emulator.Memory[0x900] = 0x12;

        Assert.AreEqual("0x5A", expressionManager.Evaluate("counter"));
        Assert.AreEqual("0x12", expressionManager.Evaluate("other_value"));
        Assert.AreEqual("10", expressionManager.Evaluate("MAX_COUNT"));
    }

    private static IEnumerable<BitMagic.Compiler.Variables> AllVariables(BitMagic.Compiler.Variables variables) =>
        new[] { variables }.Concat(variables.Children.SelectMany(AllVariables));
}
