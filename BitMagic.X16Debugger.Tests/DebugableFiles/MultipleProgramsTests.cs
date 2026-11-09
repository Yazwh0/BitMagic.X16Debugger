using BitMagic.X16Debugger.Builder;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.DebugableFiles;

// A project's 'files' list builds several programs in one debug session, each to its own output (issue #45).
[TestClass]
public class MultipleProgramsTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BitMagicMultipleTest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "first.bmasm"), ".machine CommanderX16R40\n    lda #1\n    stp\n");
        File.WriteAllText(Path.Combine(_folder, "second.bmasm"), ".machine CommanderX16R40\n    lda #2\n    rts\n");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    [TestMethod]
    public async Task TwoFiles_BothBuiltAndWritten()
    {
        var serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());

        var projectService = new ProjectService();
        projectService.SetProject(new X16DebugProject
        {
            BasePath = _folder,
            OutputFolder = "out",
            Files = [
                new BitmagicInputFile { Type = "bitmagic", Filename = "first.bmasm", OutputFilename = "FIRST.PRG" },
                new BitmagicInputFile { Type = "bitmagic", Filename = "second.bmasm", OutputFilename = "BIN/SECOND.PRG" },
            ]
        }, _folder);

        await new ProjectBuilder(projectService, serviceManager, new FakeEmulatorLogger()).Build();

        var built = serviceManager.DebugableFileManager.GetBitMagicFilesToWrite().Select(i => i.Path.Replace('\\', '/')).ToList();
        CollectionAssert.IsSubsetOf(new[] { "FIRST.PRG", "BIN/SECOND.PRG" }, built, string.Join(", ", built));

        var first = File.ReadAllBytes(Path.Combine(_folder, "out", "FIRST.PRG"));
        var second = File.ReadAllBytes(Path.Combine(_folder, "out", "BIN", "SECOND.PRG"));

        // each has a load address header, then lda #n
        CollectionAssert.AreEqual(new byte[] { 0xa9, 0x01 }, first[2..4]);
        CollectionAssert.AreEqual(new byte[] { 0xa9, 0x02 }, second[2..4]);
    }
}
