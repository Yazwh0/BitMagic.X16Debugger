using BitMagic.Common;
using BitMagic.X16Debugger.Builder;
using BitMagic.X16Debugger.Hovers;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Hovers;

// The language server's hover: a project is built, then positions in its source are hovered over.
[TestClass]
public class HoverSnapshotTests
{
    private const string Source =
@".machine CommanderX16R40
.org $810
.const COUNT $10
    lda #COUNT+1
    sta lines,x
    lda #@(2 * 3)
    jsr text:print
    stp
.lines:
    .byte 0

.scope text
    .const private SECRET 7
    .proc print
    .loop:
        dex
        bne loop
        lda #<SECRET
        rts
    .endproc
.endscope
";

    private string _folder = "";
    private string _path = "";
    private string[] _lines = [];
    private ServiceManager _serviceManager = null!;
    private ProjectBuilder _builder = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BitMagicHoverTest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "main.bmasm");
        File.WriteAllText(_path, Source);
        _path = _path.FixFilename();
        _lines = Source.Split('\n').Select(i => i.TrimEnd('\r')).ToArray();

        _serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());

        var projectService = new ProjectService();
        projectService.SetProject(new X16DebugProject { Source = "main.bmasm", BasePath = _folder }, _folder);
        _builder = new ProjectBuilder(projectService, _serviceManager, new FakeEmulatorLogger());

        await _builder.Build();
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    // hover over the first occurrence of 'at' on the line that contains 'line', offset into it
    private string Hover(string line, string at, int offset = 0)
    {
        var lineNumber = Array.FindIndex(_lines, i => i.Contains(line));
        Assert.AreNotEqual(-1, lineNumber, line);

        var column = _lines[lineNumber].IndexOf(at) + offset;
        var hover = _serviceManager.HoverSnapshot!.Hover(_path, lineNumber, _lines[lineNumber], column);

        Assert.IsNotNull(hover, $"no hover for '{at}' on '{line}'");
        return hover.Value.Result.ToMarkdown();
    }

    [TestMethod]
    public void Constant()
    {
        var text = Hover("lda #COUNT+1", "COUNT");

        StringAssert.Contains(text, "App:Main:COUNT");
        StringAssert.Contains(text, "constant");
        StringAssert.Contains(text, "`$10` (16)");
    }

    [TestMethod]
    public void Operand_ShowsValueAndBreakdown()
    {
        var text = Hover("lda #COUNT+1", "+");

        StringAssert.Contains(text, "`#COUNT+1` = `$11` (17), immediate");
        StringAssert.Contains(text, "`$10+1`");
    }

    [TestMethod]
    public void IndexedOperand()
    {
        var text = Hover("sta lines,x", "lines");

        StringAssert.Contains(text, "App:Main:lines");
        StringAssert.Contains(text, "label");
        StringAssert.Contains(text, "absolute,X");
    }

    [TestMethod]
    public void TemplateCode_ShowsWhatWasCompiled()
    {
        var text = Hover("lda #@(2 * 3)", "@");

        StringAssert.Contains(text, "Compiles as `lda #6`");
        StringAssert.Contains(text, "`$06` (6)");
    }

    [TestMethod]
    public void QualifiedName()
    {
        var text = Hover("jsr text:print", "print");

        StringAssert.Contains(text, "App:text:print");
        StringAssert.Contains(text, "procedure");
    }

    [TestMethod]
    public void PrivateName_InAnotherScope_StillShown()
    {
        // the code can't use it, so it isn't in the source, but the hover can see it
        var lineNumber = Array.FindIndex(_lines, i => i.Contains("jsr text:print"));
        var hover = _serviceManager.HoverSnapshot!.Hover(_path, lineNumber, "    jsr text:SECRET", 12);
        Assert.IsNotNull(hover);
        var text = hover.Value.Result.ToMarkdown();

        StringAssert.Contains(text, "App:text:SECRET");
        StringAssert.Contains(text, "private");
        StringAssert.Contains(text, "`$07` (7)");
    }

    [TestMethod]
    public void ScopeComesFromTheLine()
    {
        // 'loop' only exists in text:print, so is found from a line in it
        var text = Hover("bne loop", "loop");

        StringAssert.Contains(text, "App:text:print:loop");
    }

    [TestMethod]
    public void ByteOfAName()
    {
        var text = Hover("lda #<SECRET", "<");

        StringAssert.Contains(text, "`#<SECRET` = `$07` (7), immediate");
    }

    [TestMethod]
    public void Definition_ShowsItsValue()
    {
        var text = Hover(".const private SECRET 7", "SECRET");

        StringAssert.Contains(text, "App:text:SECRET");
        StringAssert.Contains(text, "`7` = `$07` (7)");
    }

    [TestMethod]
    public void Unknown()
    {
        var lineNumber = Array.FindIndex(_lines, i => i.Contains("lda #COUNT+1"));
        var hover = _serviceManager.HoverSnapshot!.Hover(_path, lineNumber, "    lda NOT_DEFINED", 10);

        Assert.IsNotNull(hover);
        StringAssert.Contains(hover.Value.Result.ToMarkdown(), "`NOT_DEFINED` isn't defined here.");
    }

    [TestMethod]
    public async Task FailedBuild_KeepsTheLastGoodOne()
    {
        var snapshot = _serviceManager.HoverSnapshot;
        Assert.IsNotNull(snapshot);

        File.WriteAllText(_path, Source.Replace("lda #COUNT+1", "lda #COUNT+"));

        var failed = false;
        try
        {
            await _builder.Build();
        }
        catch (Exception)
        {
            failed = true;
        }

        Assert.IsTrue(failed, "the build should fail");

        Assert.AreSame(snapshot, _serviceManager.HoverSnapshot);
    }
}
