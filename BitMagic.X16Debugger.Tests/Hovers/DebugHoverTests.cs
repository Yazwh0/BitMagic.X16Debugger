using BitMagic.Common;
using BitMagic.X16Debugger.Builder;
using BitMagic.X16Debugger.Hovers;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace BitMagic.X16Debugger.Tests.Hovers;

// The debugger's hover: live values from the emulator, for the build being debugged.
[TestClass]
public class DebugHoverTests
{
    private const string Source =
@".machine CommanderX16R40
.org $810
.const PTR $22
    lda counter
    lda table,x
    lda (PTR),y
    lda $9f23
    lda $a000
    lda #5
    stp
    .var byte counter 0
.table:
    .byte 1, 2, 3, 4
";

    private string _folder = "";
    private string _path = "";
    private string[] _lines = [];
    private ServiceManager _serviceManager = null!;
    private Emulator _emulator = null!;
    private DebugHover _hover = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BitMagicDebugHoverTest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "main.bmasm");
        File.WriteAllText(_path, Source);
        _path = _path.FixFilename();
        _lines = Source.Split('\n').Select(i => i.TrimEnd('\r')).ToArray();

        _emulator = new Emulator();
        _serviceManager = new ServiceManager(_ => _emulator, new FakeEmulatorLogger());

        var projectService = new ProjectService();
        projectService.SetProject(new X16DebugProject { Source = "main.bmasm", BasePath = _folder }, _folder);
        await new ProjectBuilder(projectService, _serviceManager, new FakeEmulatorLogger()).Build();

        _hover = new DebugHover(_serviceManager);
        _hover.SetSnapshot(_serviceManager.HoverSnapshot);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    // an evaluate request as VSCode sends it for a hover over 'expression' on the line containing 'line'
    private EvaluateArguments Request(string line, string expression, bool withPosition = true)
    {
        var lineNumber = Array.FindIndex(_lines, i => i.Contains(line));
        var column = _lines[lineNumber].IndexOf(expression);

        var json = withPosition ?
            JsonConvert.SerializeObject(new { expression, context = "hover", source = new { path = _path }, line = lineNumber + 1, column = column + 1 }) :
            JsonConvert.SerializeObject(new { expression, context = "hover" });

        return JsonConvert.DeserializeObject<EvaluateArguments>(json)!;
    }

    private List<Variable> Children(EvaluateResponse response) =>
        ((BitMagic.X16Debugger.Variables.VariableIndex)_serviceManager.VariableManager.Get(response.VariablesReference)!).GetChildren().ToList();

    private int Address(string name) => _serviceManager.HoverSnapshot!.Units[0].State.Globals.GetChildVariables("App")
        .First(i => i.Name == $"App:Main:{name}").Value.Value;

    [TestMethod]
    public void Variable_LiveValue()
    {
        _emulator.Memory[Address("counter")] = 0x2a;

        var response = _hover.Evaluate(Request("lda counter", "counter"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$2A (42)", response.Result);
    }

    [TestMethod]
    public void Indexed_ValueAtTheEffectiveAddress()
    {
        _emulator.X = 2;
        _emulator.Memory[Address("table") + 2] = 3; // the build isn't loaded, so put the table's byte there

        var response = _hover.Evaluate(Request("lda table,x", "table,x"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$03 (3)", response.Result);
        Assert.AreEqual("absolute,X", response.Type);
        Assert.IsTrue(response.VariablesReference > 0);

        var children = Children(response);
        Assert.AreEqual("$02 (2)", children.First(i => i.Name == "X").Value);
        StringAssert.StartsWith(children.First(i => i.Name == "address").Value, $"${Address("table") + 2:X4}");
    }

    [TestMethod]
    public void IndirectY_FollowsThePointer()
    {
        _emulator.Memory[0x22] = 0x00;
        _emulator.Memory[0x23] = 0x40;
        _emulator.Memory[0x4003] = 0x99;
        _emulator.Y = 3;

        var response = _hover.Evaluate(Request("lda (PTR),y", "(PTR),y"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$99 (153)", response.Result);

        var children = Children(response);
        Assert.AreEqual("$4000 at $22", children.First(i => i.Name == "pointer").Value);
        Assert.AreEqual("$4003", children.First(i => i.Name == "address").Value);
    }

    [TestMethod]
    public void Io_IsntRead()
    {
        var response = _hover.Evaluate(Request("lda $9f23", "$9f23"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$9F23", response.Result);

        var children = Children(response);
        Assert.AreEqual("$9F23 (IO, not read)", children.First(i => i.Name == "address").Value);
        Assert.IsFalse(children.Any(i => i.Name == "value"));
    }

    [TestMethod]
    public void Banked_UsesTheCurrentBank()
    {
        _emulator.Memory[0] = 2; // RAM bank
        _emulator.RamBank[2 * 0x2000] = 0x55;

        var response = _hover.Evaluate(Request("lda $a000", "$a000"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$55 (85)", response.Result);

        var children = Children(response);
        Assert.AreEqual("$A000 (RAM bank 2)", children.First(i => i.Name == "address").Value);
    }

    [TestMethod]
    public void Immediate()
    {
        var response = _hover.Evaluate(Request("lda #5", "#5"));

        Assert.IsNotNull(response);
        Assert.AreEqual("$05 (5)", response.Result);
        Assert.AreEqual("immediate", response.Type);
    }

    [TestMethod]
    public void NoPosition_LeftToTheExpressionEvaluator()
    {
        Assert.IsNull(_hover.Evaluate(Request("lda counter", "counter", withPosition: false)));
    }

    [TestMethod]
    public void FileChanged_LeftToTheExpressionEvaluator()
    {
        var request = Request("lda counter", "counter");
        File.WriteAllText(_path, Source.Replace("lda counter", "lda nothing"));

        Assert.IsNull(_hover.Evaluate(request));
    }
}
